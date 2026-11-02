using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Tests;

/// The token count's index (Ai\TokenIndex) and its passes (Ai\TokenScan): a call counted once however often its key
/// comes, the file kept and read back, tidied by age, and the logs read in parts as they grow, shrink, vanish or move;
/// each call in the session its line names, the sessions' titles, folders and top threads, kept and tidied too.
internal static class TokenIndexTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 30, 0, DateTimeKind.Utc);

    private static TokenCall Call(ulong key, int output, DateTime? time = null, int read = 0, string model = "claude-opus-5-5") =>
        new(key, time ?? Now.AddMinutes(-10), "claude", model, TokenVariant.None, new TokenCounts(1, 1, output, read, 0, 0));

    private static TokenCounts Sum(TokenIndex index) => index.Snapshot().Aggregate(default(TokenCounts), (a, b) => a + b.Counts);

    public static void LinesOfOneResponseCountOnce()
    {
        var index = new TokenIndex { Now = Now };
        Check.True(index.Add(Call(1, 10, read: 500)), "a new call");
        Check.True(index.Add(Call(1, 50)), "the same response grew");
        Check.True(!index.Add(Call(1, 30, read: 500)), "an earlier value again changes nothing");
        index.Add(Call(2, 5));
        Check.True(index.Add(Call(1, 70, read: 600)), "the key back after another one");
        index.EndFile();
        Check.Eq(Sum(index), new TokenCounts(2, 2, 75, 600, 0, 0), "each field at its most, two calls");
        Check.Eq(index.Snapshot().Length, 1, "all in the hour first counted");
    }

    public static void CopiesInOtherFilesCountOnce()
    {
        var index = new TokenIndex { Now = Now };
        var old = Now.AddDays(-2);
        index.Add(Call(7, 10, old));
        index.EndFile();
        Check.True(!index.Add(Call(7, 10, old)), "a resumed session's copy");
        Check.True(!index.Add(Call(7, 99, old)), "even with other values");
        index.EndFile();
        Check.Eq(Sum(index).Calls, 1L, "once");
        Check.Eq(Sum(index).Output, 10L, "as first met");
    }

    public static void ResponsesCutBetweenPassesGrowOn()
    {
        var index = new TokenIndex { Now = Now };
        index.Add(Call(9, 10));
        index.EndFile();
        Check.True(index.Add(Call(9, 40)), "the rest of a response cut off by the last pass");
        Check.True(!index.Add(Call(9, 40)), "and its copy adds nothing");
        Check.Eq(Sum(index), new TokenCounts(1, 1, 40, 0, 0, 0), "one call at its whole");
        Check.Eq(index.OpenCount, 1, "kept open");
    }

    public static void TheIndexIsKeptAndReadBack()
    {
        string dir = Temp();
        try
        {
            string path = Path.Combine(dir, "index.bin");
            var index = new TokenIndex { Now = Now, Complete = true };
            index.Add(Call(1, 10));
            index.Add(Call(2, 20, Now.AddDays(-3), model: "claude-sonnet-5-5") with { Variant = TokenVariant.Fast | TokenVariant.Us });
            index.Add(new TokenCall(3, Now.AddHours(-1), "codex", "gpt-6.1-sol", TokenVariant.Long, new TokenCounts(1, 5, 6, 7, 8, 0)));
            index.EndFile();
            index.Files["C:\\a.jsonl"] = new TokenFile { Path = "C:\\a.jsonl", Size = 100, Mtime = 5, Offset = 90 };
            index.Files["x|u"] = new TokenFile { Path = "C:\\r.jsonl", Size = 10, Mtime = 6, Offset = 10, Thread = "u", Model = "gpt-6.1-sol", Records = true, LastTotal = 42 };
            index.Save(path);
            var back = TokenIndex.Load(path);
            back.Now = Now;
            Check.True(back.Complete, "the first count's end");
            Check.Eq(string.Join(";", back.Snapshot().OrderBy(b => b.Hour).Select(b => $"{b.Hour:O} {b.Provider} {b.Model} {b.Variant} {b.Counts}")),
                string.Join(";", index.Snapshot().OrderBy(b => b.Hour).Select(b => $"{b.Hour:O} {b.Provider} {b.Model} {b.Variant} {b.Counts}")), "the sums");
            var f = back.Files["x|u"];
            Check.True(f is { Path: "C:\\r.jsonl", Size: 10, Mtime: 6, Offset: 10, Thread: "u", Model: "gpt-6.1-sol", Records: true, LastTotal: 42 }, "a Codex file's state");
            Check.True(back.Files["c:\\A.JSONL"] is { Offset: 90, Thread: null }, "a Claude file's, by path in any case");
            Check.Eq(back.SeenCount, 3, "the keys");
            Check.Eq(back.OpenCount, 2, "the open ones");
            Check.True(!back.Add(Call(2, 99, Now.AddDays(-3))), "a key seen before");
            Check.True(back.Add(Call(1, 15)), "an open key grows on");

            byte[] whole = File.ReadAllBytes(path);
            File.WriteAllBytes(path, whole[..(whole.Length - 9)]);
            var cut = TokenIndex.Load(path);
            Check.True(cut is { Complete: false, SeenCount: 0, Files.Count: 0 } && cut.Snapshot().Length == 0, "a file cut short: an empty index, counted again");
            File.WriteAllBytes(path, [.. "BTKI"u8, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xF0, 0xFF, 0xFF, 0x7F]);
            Check.Eq(TokenIndex.Load(path).Complete, false, "more keys than the file holds: counted again, no table made for them");

            File.WriteAllBytes(path, [.. "BTKI"u8, 99, 0, 0, 0, 1]);
            Check.Eq(TokenIndex.Load(path).Snapshot().Length, 0, "another layout: counted again");
            File.WriteAllBytes(path, [1, 2, 3]);
            Check.Eq(TokenIndex.Load(path).SeenCount, 0, "a broken file: counted again");
            Check.Eq(TokenIndex.Load(Path.Combine(dir, "none.bin")).Complete, false, "no file: a first count");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void OldSumsFoldAndOldKeysGo()
    {
        var index = new TokenIndex { Now = Now };
        var old = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc); // 124 days before
        index.Add(Call(1, 1, old.AddHours(3)));
        index.Add(Call(2, 2, old.AddHours(17)));
        index.Add(Call(6, 32, old.AddMinutes(10))); // at the day's midnight, where a day was folded before
        index.Add(Call(3, 4, Now.AddDays(-HourDaysLess())));
        index.Add(Call(4, 8, Now.AddHours(-1)));
        index.Add(Call(5, 16, Now.AddHours(-8)));
        index.EndFile();
        Check.Eq(index.OpenCount, 1, "only the last 6 hours are open");
        index.Roll();
        Check.Eq(index.SeenCount, 6, "no key forgotten during the first count");
        var sums = index.Snapshot().OrderBy(b => b.Hour).ToList();
        Check.Eq(sums[0].Hour, old.AddHours(12), "the old hours fold into their UTC day, at its noon");
        Check.Eq(sums[0].Counts.Output, 35L, "summed");
        Check.Eq(sums.Count, 4, "the recent hours stay hours");
        index.Complete = true;
        index.Roll();
        Check.Eq(index.SeenCount, 3, "keys older than 120 days are forgotten once it is over");
        Check.Eq(index.Snapshot().Length, 4, "a folded day stays as it is");
        index.Now = Now.AddHours(8);
        index.Roll();
        Check.Eq(index.OpenCount, 0, "and open keys close");
    }

    public static void OldCopiesCountOnceThroughTheFirstCount()
    {
        var index = new TokenIndex { Now = Now };
        var old = Now.AddDays(-150); // kept past Claude Code's 30 days, then resumed: the copy is read first (newest first)
        Check.True(index.Add(Call(1, 10, old)), "an old call, met first in its copy");
        index.EndFile();
        index.Roll(); // a checkpoint of the first count
        Check.Eq(index.SeenCount, 1, "its key kept while the first count goes on");
        Check.True(!index.Add(Call(1, 10, old)), "the original, read later, is not counted again");
        index.EndFile();
        index.Complete = true;
        index.Roll();
        Check.Eq(index.SeenCount, 0, "forgotten once the first count is over");
        Check.True(!index.Add(Call(1, 10, old)), "a copy met after that is still not counted");
        Check.True(index.Add(Call(2, 5)), "a new call is");
        index.EndFile();
        Check.Eq(Sum(index).Calls, 2L, "the old call once");
    }

    public static void AFileThatCantBeReadDoesNotHoldTheFirstCount()
    {
        string root = Temp();
        try
        {
            string a = Path.Combine(root, "proj", "a.jsonl"), b = Path.Combine(root, "proj", "b.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            File.WriteAllText(a, ClaudeLine("m1", 10));
            File.WriteAllText(b, ClaudeLine("m2", 20));
            var index = new TokenIndex();
            TokenScan scan;
            using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
                scan = Pass(index, root);
            Check.True(index.Complete && !scan.Stopped, "the first count is over");
            Check.True(index.Files.ContainsKey(a) && !index.Files.ContainsKey(b), "the file held elsewhere is not known yet");
            Check.Eq(Sum(index).Calls, 1L, "what could be read");
            Check.Eq(Pass(index, root).FilesRead, 1, "it is read on the next pass");
            Check.Eq(Sum(index).Calls, 2L, "and counted");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Sums(TokenIndex index) => string.Join(";", index.Snapshot()
        .Select(b => $"{b.Hour:O} {b.Provider} {b.Model} {(byte)b.Variant} {b.Counts} {b.Session?.Id}/{b.Session?.Title}/{b.Session?.Folder}/{b.Session?.Bloom}")
        .Order(StringComparer.Ordinal));

    private static string Table(TokenIndex index) => string.Join(";", index.Sessions.Select(s => s.ToString()).Order(StringComparer.Ordinal));

    public static void SessionsAreKeptAndReadBack()
    {
        string dir = Temp();
        try
        {
            string path = Path.Combine(dir, "index.bin");
            var index = new TokenIndex { Now = Now, Complete = true };
            index.Add(Call(1, 10) with { Session = "s-a", Folder = @"D:\Work\Repo", Own = true, Variant = TokenVariant.Fast.WithEffort(TokenEffort.Max) });
            index.Add(Call(2, 20, Now.AddDays(-2)) with { Session = "s-a", Variant = TokenVariant.None.WithEffort(TokenEffort.High) });
            index.Add(Call(3, 5) with { Session = "s-b" });
            index.Add(Call(4, 5)); // no session
            index.Add(new TokenCall(5, Now.AddHours(-2), "codex", "gpt-6.1-sol", TokenVariant.None.WithEffort(TokenEffort.Medium), new TokenCounts(1, 5, 6, 7, 8, 0), "t-child"));
            index.EndFile();
            index.Title("claude", "s-a", "Home blocks", custom: true, Now);
            index.Describe("codex", "t-child", "t-top", "Repo", false, Now);
            index.Describe("codex", "t-top", null, "Repo", false, Now);
            index.Title("codex", "t-top", "The top thread", custom: true, Now);
            index.Files["x|u"] = new TokenFile { Path = "C:\\r.jsonl", Size = 10, Mtime = 6, Offset = 10, Thread = "u", Model = "gpt-6.1-sol", Effort = TokenEffort.XHigh, Records = true, LastTotal = 42 };
            index.Save(path);
            var back = TokenIndex.Load(path);
            back.Now = Now;
            Check.Eq(Sums(back), Sums(index), "the sums, their sessions and efforts");
            Check.Eq(Table(back), Table(index), "the sessions' table");
            Check.Eq(back.Sessions.Count, 4, "s-a, s-b, t-child, t-top");
            Check.True(back.Sessions.Single(s => s.Id == "s-a") is { Folder: "Repo", FolderOwn: true }, "a folder from the session's own log");
            Check.Eq(back.Files["x|u"].Effort, TokenEffort.XHigh, "a Codex file's effort");
            Check.Eq(back.Snapshot().Single(b => b.Provider == "codex").Session?.Title, "The top thread", "a child thread's calls under its top thread");
            Check.True(back.Add(Call(1, 15)), "an open key grows on");
            Check.Eq(back.Snapshot().Where(b => b.Session?.Id == "s-a").Sum(b => b.Counts.Output), 35L, "in its session");

            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write("BTKI"u8);
                w.Write(TokenIndex.Version);
                w.Write(true);
                w.Write(1);
                w.Write("claude");
                w.Write("claude-opus-5-5");
                w.Write(0); // no session
                w.Write(1); // one sum, of session 3
                w.Write(Now.Ticks);
                w.Write(0);
                w.Write((byte)0);
                w.Write(3);
                for (int i = 0; i < 6; i++) w.Write(1L);
            }
            Check.True(TokenIndex.Load(path) is { Complete: false, Sessions.Count: 0 }, "a sum of a session not in the table: counted again");
            File.WriteAllBytes(path, [.. "BTKI"u8, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            Check.Eq(TokenIndex.Load(path).Complete, false, "an index of version 1: counted again");
            File.WriteAllBytes(path, [.. "BTKI"u8, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            Check.Eq(TokenIndex.Load(path).Complete, false, "of version 2 (folders as first met): counted again");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void OldSessionsGoAndTheSumsStay()
    {
        var index = new TokenIndex { Now = Now, Complete = true };
        var old = Now.AddDays(-TokenIndex.HourDays - 5);
        index.Add(Call(1, 1, old) with { Session = "s-old", Variant = TokenVariant.None.WithEffort(TokenEffort.Max) });
        index.Add(Call(2, 2, Now.AddDays(-50)) with { Session = "s-mid" });
        index.Add(Call(3, 4, Now.AddHours(-1)) with { Session = "s-new" });
        index.Add(Call(6, 8, old.AddHours(2)) with { Session = "s-new", Variant = TokenVariant.None.WithEffort(TokenEffort.Max) }); // an old call of a session still going
        index.EndFile();
        index.Title("claude", "s-gone", "Only a title", custom: true, old); // a log written long ago
        int[] before = [index.Sessions.Count, index.Snapshot().Length];
        var sum = Sum(index);
        index.Roll();
        Check.Eq(Sum(index), sum, "the sums as they were");
        Check.Eq(string.Join(" ", index.Sessions.Select(s => s.Id)), "s-mid s-new", "the sessions not seen for 100 days leave the table");
        Check.True(before[0] == 4 && before[1] == 4, "four sessions and sums before");
        var sums = index.Snapshot();
        var folded = sums.Where(b => b.Hour < Now.AddDays(-TokenIndex.HourDays)).ToList();
        Check.True(folded.Count == 1 && folded[0].Session is null, "the old hours folded into one day with no session");
        Check.Eq(folded[0].Variant.Effort(), TokenEffort.Max, "keeping the effort");
        Check.Eq(sums.Single(b => b.Counts.Output == 2).Session?.Id, "s-mid", "the others keep theirs, numbered again");
        Check.Eq(sums.Single(b => b.Counts.Output == 4).Session?.Id, "s-new", "the others keep theirs, numbered again");
        Check.True(index.Add(Call(3, 9)), "an open key grows on after the numbers moved");
        Check.Eq(index.Snapshot().Single(b => b.Session?.Id == "s-new").Counts.Output, 9L, "in its own session still");
    }

    public static void ThreadsShowUnderTheirTop()
    {
        var index = new TokenIndex { Now = Now };
        static TokenCall Codex(ulong key, string thread) => new(key, Now.AddMinutes(-10), "codex", "gpt-6.1-sol", TokenVariant.None, new TokenCounts(1, 1, 1, 0, 0, 0), thread);
        foreach (var (key, thread) in new[] { (1UL, "top"), (2UL, "child"), (3UL, "grandchild"), (4UL, "loop-a"), (5UL, "loop-b"), (6UL, "orphan") })
            index.Add(Codex(key, thread));
        index.EndFile();
        index.Describe("codex", "child", "top", "Repo", false, Now);
        index.Describe("codex", "grandchild", "child", null, false, Now);
        index.Describe("codex", "loop-a", "loop-b", null, false, Now);
        index.Describe("codex", "loop-b", "loop-a", null, false, Now);
        index.Describe("codex", "orphan", "not-read", null, false, Now);
        var by = index.Snapshot().GroupBy(b => b.Session?.Id).ToDictionary(g => g.Key ?? "", g => g.Sum(b => b.Counts.Calls));
        Check.Eq(by["top"], 3L, "a subagent's and its own subagent's calls under the top thread");
        Check.True(by.ContainsKey("orphan"), "a parent never read: the thread is its own top");
        Check.Eq(by.GetValueOrDefault("loop-a") + by.GetValueOrDefault("loop-b"), 2L, "a loop stops");
        Check.True(!by.ContainsKey(""), "every sum has its session");
        Check.True(index.Describe("codex", "top", null, null, true, Now), "originator bloom");
        Check.True(index.Sessions.Single(s => s.Id == "top") is { Bloom: true, Title: null }, "Bloom's own");
        Check.True(!index.Title("codex", "top", "a chat's name", custom: true, Now), "keeps no title");
        Check.True(!index.Title("codex", "unknown", "x", custom: true, Now, onlyKnown: true) && index.Sessions.All(s => s.Id != "unknown"), "a name of a thread not met is left");
    }

    private static int HourDaysLess() => TokenIndex.HourDays - 1;

    private static string Temp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-tokens-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ClaudeLine(string id, int output, string time = "2026-11-02T10:00:00Z") =>
        """{"type":"assistant","message":{"model":"claude-opus-5-5","id":"@ID","usage":{"input_tokens":1,"output_tokens":@OUTPUT}},"requestId":"r","uuid":"u","timestamp":"@TIME"}""".Replace("@ID", id, StringComparison.Ordinal).Replace("@OUTPUT", output.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@TIME", time, StringComparison.Ordinal) + "\n";

    private static TokenScan Pass(TokenIndex index, string root, bool codex = false)
    {
        index.Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var scan = new TokenScan(index, [(codex, root)]);
        scan.Run();
        return scan;
    }

    public static void LogsAreReadInParts()
    {
        string root = Temp();
        try
        {
            string a = Path.Combine(root, "proj", "a.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            string done = ClaudeLine("m1", 10);
            File.WriteAllText(a, done + ClaudeLine("m2", 5)[..40]);
            var index = new TokenIndex();
            var scan = Pass(index, root);
            Check.True(scan.Changed && index.Complete, "the first pass");
            Check.Eq(Sum(index).Calls, 1L, "the unfinished line waits");
            Check.Eq(index.Files[a].Offset, (long)done.Length, "the offset stops before it");

            File.WriteAllText(a, done + ClaudeLine("m2", 5) + ClaudeLine("m2", 25));
            Check.Eq(Pass(index, root).FilesRead, 1, "a file that grew is read on");
            Check.Eq(Sum(index), new TokenCounts(2, 2, 35, 0, 0, 0), "the finished line and its next block");
            Check.Eq(Pass(index, root).FilesRead, 0, "a file as it was is not opened");

            File.WriteAllText(a, ClaudeLine("m1", 10));
            Check.True(!Pass(index, root).Changed, "a file that shrank is read from the start, nothing counted twice");
            Check.Eq(index.Files[a].Offset, (long)done.Length, "read again from 0");

            string sub = Path.Combine(root, "proj", "s", "subagents", "agent-1.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(sub)!);
            File.WriteAllText(sub, ClaudeLine("m3", 1));
            File.WriteAllText(Path.Combine(root, "proj", "notes.txt"), ClaudeLine("m4", 1));
            Pass(index, root);
            Check.Eq(Sum(index).Calls, 3L, "a subagent's log too, no other file");

            File.Delete(a);
            Pass(index, root);
            Check.True(!index.Files.ContainsKey(a), "a file gone is forgotten");
            Check.Eq(Sum(index).Calls, 3L, "its sums stay");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private const string Sa = "aaaaaaaa-0000-4000-8000-000000000001", Sb = "bbbbbbbb-0000-4000-8000-000000000002", Sc = "cccccccc-0000-4000-8000-000000000003", Sd = "dddddddd-0000-4000-8000-000000000004";

    private static string Session(string id, string? session, string cwd = "", int minute = 0) =>
        ("""{"type":"assistant","message":{"model":"claude-opus-5-5","id":"@ID","usage":{"input_tokens":1,"output_tokens":1}},"requestId":"r","uuid":"u","timestamp":"2026-11-02T10:@MM:00Z","effort":"high"@SID@CWD}""")
        .Replace("@ID", id, StringComparison.Ordinal).Replace("@MM", minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("@SID", session is null ? "" : $",\"sessionId\":\"{session}\"", StringComparison.Ordinal)
        .Replace("@CWD", cwd.Length == 0 ? "" : $",\"cwd\":\"{cwd.Replace("\\", "\\\\", StringComparison.Ordinal)}\"", StringComparison.Ordinal) + "\n";

    private static string TitleLine(string kind, string session, string title) =>
        $$"""{"type":"{{kind}}-title","{{kind}}Title":"{{title}}","sessionId":"{{session}}"}""" + "\n";

    public static void ClaudeCallsGoToTheSessionTheirLinesName()
    {
        string bloomAi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloom", "ai");
        var results = new List<string>();
        foreach (bool copyFirst in new[] { false, true })
        {
            string root = Temp();
            try
            {
                string proj = Path.Combine(root, "proj");
                Directory.CreateDirectory(Path.Combine(proj, Sa, "subagents"));
                Directory.CreateDirectory(Path.Combine(proj, Sd, "subagents"));
                string a = Path.Combine(proj, Sa + ".jsonl"), b = Path.Combine(proj, Sb + ".jsonl"), d = Path.Combine(proj, Sd + ".jsonl");
                string subA = Path.Combine(proj, Sa, "subagents", "agent-1.jsonl"), subD = Path.Combine(proj, Sd, "subagents", "agent-2.jsonl");
                File.WriteAllText(a, TitleLine("ai", Sa, "auto") + Session("m1", Sa, @"D:\Work\Repo\") + TitleLine("custom", Sa, "first") + Session("m2", Sa, minute: 1)
                    + TitleLine("custom", Sa, "second") + TitleLine("ai", Sa, "later ai"));
                // resumed: its parent's title and line copied first
                File.WriteAllText(b, TitleLine("custom", Sa, "first") + Session("m1", Sa, @"D:\Work\Repo\") + Session("m3", Sb, @"D:\", minute: 2));
                File.WriteAllText(subA, Session("m4", Sa, @"D:\Work\Repo\.claude\worktrees\wf-1", minute: 3)); // a subagent in a worktree
                File.WriteAllText(Path.Combine(proj, Sc + ".jsonl"), Session("m5", null, minute: 4));
                File.WriteAllText(Path.Combine(proj, "notes.jsonl"), Session("m6", null, minute: 5));
                File.WriteAllText(d, TitleLine("custom", Sd, "a chat") + Session("m7", Sd, bloomAi + @"\files", minute: 6));
                File.WriteAllText(subD, Session("m10", Sd, @"D:\Work\Other", minute: 9));
                File.WriteAllText(Path.Combine(proj, "dev.jsonl"), Session("m8", "s-dev", @"C:\Temp\bq-x\local\ai", minute: 7) + TitleLine("custom", "s-dev", "a dev chat")
                    + TitleLine("custom", "s-named", "Bloom · a chat") + TitleLine("custom", "s-local", "x") + Session("m9", "s-local", @"D:\local\aix", minute: 8));
                // newest first: the copy and the subagents before or after their session's own log
                File.SetLastWriteTimeUtc(a, Now.AddHours(copyFirst ? -2 : -1));
                File.SetLastWriteTimeUtc(b, Now.AddHours(copyFirst ? -1 : -2));
                File.SetLastWriteTimeUtc(d, Now.AddHours(-2));
                File.SetLastWriteTimeUtc(subA, Now.AddHours(copyFirst ? -3 : -0.5));
                File.SetLastWriteTimeUtc(subD, Now.AddHours(copyFirst ? -3 : -0.5));
                var index = new TokenIndex();
                Pass(index, root);
                var calls = index.Snapshot().GroupBy(x => x.Session?.Id ?? "none").ToDictionary(g => g.Key, g => g.Sum(x => x.Counts.Calls));
                Check.Eq(calls[Sa], 3L, "a session's calls, its copy in another file once and its subagent's");
                Check.Eq((calls[Sb], calls[Sc], calls["none"], calls[Sd]), (1L, 1L, 1L, 2L), "the resumed one's own; by the file's name; none; Bloom's and its subagent's");
                Check.True(index.Snapshot().All(x => x.Variant.Effort() == TokenEffort.High), "each at its effort");
                var sa = index.Sessions.Single(s => s.Id == Sa);
                Check.True(sa is { Title: "second", Kind: TokenSessionInfo.Named.Custom, Folder: "Repo", FolderOwn: true, Bloom: false },
                    "the last custom title, an ai one after it and a resumed copy's not; its own log's folder, not its subagent's worktree");
                Check.Eq(index.Sessions.Single(s => s.Id == Sb).Folder, "D:", "a drive's root");
                Check.True(index.Sessions.Single(s => s.Id == Sd) is { Bloom: true, Title: null, Folder: "files" }, "Bloom's AI folder: Bloom's own, its title not kept, whatever its subagent's");
                Check.True(index.Sessions.Single(s => s.Id == "s-dev") is { Bloom: true, Title: null, Folder: "ai" }, "a dev copy's (<data>\\local\\ai) too");
                Check.True(index.Sessions.Single(s => s.Id == "s-named") is { Bloom: true, Title: null }, "a session Bloom named");
                Check.True(index.Sessions.Single(s => s.Id == "s-local") is { Bloom: false, Title: "x" }, "a folder only like it");
                File.AppendAllText(a, TitleLine("custom", Sa, "third"));
                Pass(index, root);
                Check.Eq(index.Sessions.Single(s => s.Id == Sa).Title, "third", "a title added later");
                results.Add(Sums(index) + "|" + Table(index));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
        Check.Eq(results[1], results[0], "the same whichever file is read first");
    }

    public static void CodexThreadsAreDescribedAndNamed()
    {
        string home = Temp();
        try
        {
            const string t1 = "01a10000-0000-7000-8000-000000000001", t2 = "01a10000-0000-7000-8000-000000000002", t3 = "01a10000-0000-7000-8000-000000000003";
            string day = Path.Combine(home, "sessions", "2026", "11", "01");
            Directory.CreateDirectory(day);
            static string Meta(string id, string extra) => """{"timestamp":"2026-11-01T07:00:00Z","type":"session_meta","payload":{"id":"@ID","cwd":"D:\\Work\\Repo","originator":"codex_exec"@X}}""".Replace("@ID", id, StringComparison.Ordinal).Replace("@X", extra, StringComparison.Ordinal) + "\n";
            static string Turn(string effort) => """{"timestamp":"2026-11-01T07:00:01Z","type":"turn_context","payload":{"model":"gpt-6.1-sol","effort":"@E"}}""".Replace("@E", effort, StringComparison.Ordinal) + "\n";
            static string Rec(string id, int hour) => """{"timestamp":"2026-11-01T@H:00:00Z","type":"token_usage_record","payload":{"response_id":"@ID","usage":{"input_tokens":10,"output_tokens":1}}}""".Replace("@ID", id, StringComparison.Ordinal).Replace("@H", hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) + "\n";
            string r1 = Path.Combine(day, $"rollout-2026-11-01T16-00-00-{t1}.jsonl");
            File.WriteAllText(r1, Meta(t1, "") + Turn("high") + Rec("resp_1", 7));
            File.WriteAllText(Path.Combine(day, $"rollout-2026-11-01T16-00-01-{t2}.jsonl"), Meta(t2, $",\"parent_thread_id\":\"{t1}\"") + Turn("low") + Rec("resp_2", 7));
            File.WriteAllText(Path.Combine(day, $"rollout-2026-11-01T16-00-02-{t3}.jsonl"), Meta(t3, "").Replace("codex_exec", "bloom", StringComparison.Ordinal) + Turn("medium") + Rec("resp_3", 7));
            string names = Path.Combine(home, "session_index.jsonl");
            File.WriteAllText(names, $$"""
                {"id":"{{t1}}","thread_name":"first","updated_at":"x"}
                {"id":"{{t3}}","thread_name":"a Bloom chat","updated_at":"x"}
                {"id":"01a19999-0000-7000-8000-000000000009","thread_name":"never read","updated_at":"x"}
                {"id":"{{t1}}","thread_name":"renamed","updated_at":"x"}

                """);
            string sessions = Path.Combine(home, "sessions"), path = Path.Combine(home, "index.bin");
            var index = new TokenIndex();
            Pass(index, sessions, codex: true);
            Check.True(index.Sessions.Single(s => s.Id == t1) is { Title: "renamed", Folder: "Repo", Bloom: false }, "the last name of the thread, its folder");
            Check.True(index.Sessions.Single(s => s.Id == t3) is { Bloom: true, Title: null }, "originator bloom: no name kept");
            Check.Eq(index.Sessions.Count, 3, "a name of a thread not read is left");
            var sums = index.Snapshot();
            Check.True(sums.All(b => b.Session?.Id is { } id && id != t2), "a subagent's thread under its parent");
            Check.Eq(string.Join(" ", sums.Where(b => b.Session?.Id == t1).Select(b => b.Variant.Effort()).Order()), "Low High", "each call at its turn's effort");

            index.Save(path);
            index = TokenIndex.Load(path);
            File.AppendAllText(r1, Rec("resp_4", 9));
            Check.Eq(Pass(index, sessions, codex: true).FilesRead, 1, "read on from the offset");
            Check.Eq(index.Snapshot().Single(b => b.Hour.Hour == 9).Variant.Effort(), TokenEffort.High, "with the effort kept in the index");

            index.Save(path);
            index = TokenIndex.Load(path);
            File.WriteAllText(names, $$"""{"id":"{{t1}}","thread_name":"named again later","updated_at":"x"}""" + "\n");
            var scan = Pass(index, sessions, codex: true);
            Check.True(scan.FilesRead == 0 && scan.Changed, "no rollout read, the names file changed");
            Check.Eq(index.Sessions.Single(s => s.Id == t1).Title, "named again later", "read again");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    public static void TheOwnLogNamesTheFolder()
    {
        string bloomAi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloom", "ai");
        var index = new TokenIndex { Now = Now };
        index.Add(Call(1, 1) with { Session = "s", Folder = @"D:\Work\Repo\.claude\worktrees\wf-1" });
        index.EndFile();
        Check.Eq(index.Sessions.Single().Folder, "wf-1", "a subagent's folder until the own log names one");
        Check.True(index.Add(Call(1, 1) with { Session = "s", Folder = @"D:\Work\Repo", Own = true }), "a call counted before from a copy");
        Check.True(index.Sessions.Single() is { Folder: "Repo", FolderOwn: true }, "names it all the same");
        index.Add(Call(2, 1) with { Session = "s", Folder = @"D:\Work\Other", Own = true });
        index.Add(Call(3, 1) with { Session = "s", Folder = @"D:\Work\Third" });
        Check.Eq(index.Sessions.Single().Folder, "Repo", "the first one the own log names, another file's not after it");
        index.Add(Call(4, 1) with { Session = "b", Folder = bloomAi + @"\files" });
        Check.True(index.Sessions.Single(s => s.Id == "b") is { Bloom: true, Folder: "files" }, "Bloom's AI folder in another file");
        index.Add(Call(5, 1) with { Session = "b", Folder = @"D:\Work\Repo", Own = true });
        Check.True(index.Sessions.Single(s => s.Id == "b") is { Bloom: false, Folder: "Repo" }, "Bloom's own judged again by the own log's folder");
    }

    public static void CodexNamesComeAfterAStoppedPass()
    {
        string home = Temp();
        try
        {
            const string t1 = "01a10000-0000-7000-8000-000000000011";
            string sessions = Path.Combine(home, "sessions"), projects = Path.Combine(home, "projects"), day = Path.Combine(sessions, "2026", "11", "01");
            Directory.CreateDirectory(day);
            Directory.CreateDirectory(projects);
            File.WriteAllText(Path.Combine(home, "session_index.jsonl"), $$"""{"id":"{{t1}}","thread_name":"named","updated_at":"x"}""" + "\n");
            (bool, string)[] roots = [(false, projects), (true, sessions)];
            var index = new TokenIndex();
            new TokenScan(index, roots).Run(); // the names file read with no thread to name yet
            string rollout = Path.Combine(day, $"rollout-2026-11-01T16-00-00-{t1}.jsonl");
            File.WriteAllText(rollout, """{"timestamp":"2026-11-01T07:00:00Z","type":"session_meta","payload":{"id":"@ID","cwd":"D:\\Work\\Repo"}}""".Replace("@ID", t1, StringComparison.Ordinal) + "\n"
                + """{"timestamp":"2026-11-01T07:00:01Z","type":"turn_context","payload":{"model":"gpt-6.1-sol"}}""" + "\n"
                + """{"timestamp":"2026-11-01T07:00:02Z","type":"token_usage_record","payload":{"response_id":"resp_1","usage":{"input_tokens":10,"output_tokens":1}}}""" + "\n");
            string log = Path.Combine(projects, Sa + ".jsonl");
            File.WriteAllText(log, Session("m1", Sa));
            File.SetLastWriteTimeUtc(rollout, Now.AddHours(-1)); // read first
            File.SetLastWriteTimeUtc(log, Now.AddHours(-2));
            int asked = 0;
            var stopped = new TokenScan(index, roots)
            {
                Wanted = () =>
                {
                    if (asked++ > 0) return false; // asked again before the Claude log: the pass stops there
                    System.Threading.Thread.Sleep(TokenScan.AskEvery + TimeSpan.FromMilliseconds(100));
                    return true;
                },
            };
            stopped.Run();
            Check.True(stopped is { Stopped: true, FilesRead: 1 }, "a pass stopped after the rollout");
            var next = new TokenScan(index, roots);
            next.Run();
            Check.Eq(next.FilesRead, 1, "the next pass reads only the Claude log");
            Check.Eq(index.Sessions.Single(s => s.Id == t1).Title, "named", "the thread named all the same");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    public static void CodexRolloutsAreFollowed()
    {
        string root = Temp();
        try
        {
            const string thread = "01a195f8-1b89-7e01-bc47-41dae7935ad7";
            string day = Path.Combine(root, "sessions", "2026", "11", "01");
            Directory.CreateDirectory(day);
            string plain = Path.Combine(day, $"rollout-2026-11-01T16-17-08-{thread}.jsonl");
            string context = """{"timestamp":"2026-11-01T07:17:10.000Z","ordinal":3,"type":"turn_context","payload":{"model":"gpt-6.1-sol"}}""" + "\n";
            static string Rec(string id, string at) => """{"timestamp":"@AT","ordinal":11,"type":"token_usage_record","payload":{"response_id":"@ID","usage":{"input_tokens":100,"cached_input_tokens":60,"cache_write_input_tokens":0,"output_tokens":9}}}""".Replace("@AT", at, StringComparison.Ordinal).Replace("@ID", id, StringComparison.Ordinal) + "\n";
            File.WriteAllText(plain, context + Rec("resp_1", "2026-11-01T07:17:15.875Z"));
            var mtime = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(plain, mtime);
            var index = new TokenIndex();
            var scan = Pass(index, Path.Combine(root, "sessions"), codex: true);
            Check.Eq(Sum(index), new TokenCounts(1, 40, 9, 60, 0, 0), "a rollout's record");
            string id = "x|" + thread;
            Check.True(index.Files.ContainsKey(id), "known by its thread");

            // compressed by Codex, its time kept: only the path moves, and a .zst is never read
            string zst = plain + ".zst";
            File.Move(plain, zst);
            File.WriteAllText(zst, "not json, not read");
            File.SetLastWriteTimeUtc(zst, mtime);
            scan = Pass(index, Path.Combine(root, "sessions"), codex: true);
            Check.True(scan.FilesRead == 0 && scan.Dirty, "the state moves, nothing is read");
            Check.Eq(index.Files[id].Path, zst, "to the .zst");
            Check.Eq(index.Files[id].Model, "gpt-6.1-sol", "keeping the model");

            // a rollout never read before and already compressed is passed over
            string other = Path.Combine(day, "rollout-2026-11-01T10-00-00-01a19000-0000-7000-8000-000000000000.jsonl.zst");
            File.WriteAllText(other, "x");
            Check.Eq(Pass(index, Path.Combine(root, "sessions"), codex: true).FilesRead, 0, "an unread .zst");
            Check.True(!index.Files.ContainsKey("x|01a19000-0000-7000-8000-000000000000"), "and not remembered");

            // written on as a plain file again, later: read from the offset, with the model it had
            File.Delete(zst);
            File.WriteAllText(plain, context + Rec("resp_1", "2026-11-01T07:17:15.875Z") + Rec("resp_2", "2026-11-01T09:00:00Z"));
            Pass(index, Path.Combine(root, "sessions"), codex: true);
            Check.Eq(Sum(index).Calls, 2L, "the new record only");
            Check.Eq(index.Snapshot().Single(b => b.Hour.Hour == 9).Model, "gpt-6.1-sol", "under the model kept in the state");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    public static void RootsAreMetOnce()
    {
        string home = Temp();
        try
        {
            Directory.CreateDirectory(Path.Combine(home, "projects"));
            Directory.CreateDirectory(Path.Combine(home, "sessions"));
            var roots = TokenScan.Roots([("claude", home), ("claude", home + "\\"), ("chatgpt", home), ("claude", Path.Combine(home, "none"))]);
            Check.Eq(string.Join(" ", roots.Select(r => (r.Codex ? "x:" : "c:") + Path.GetFileName(r.Folder))), "c:projects x:sessions", "each folder there, once");
            Check.Eq(TokenScan.Id(true, @"C:\s\rollout-2026-11-01T16-17-08-01a195f8-1b89-7e01-bc47-41dae7935ad7.jsonl.zst"), "x|01a195f8-1b89-7e01-bc47-41dae7935ad7", "a rollout by its uuid");
            Check.Eq(TokenScan.Id(false, @"C:\p\a.jsonl"), @"C:\p\a.jsonl", "a Claude log by its path");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
