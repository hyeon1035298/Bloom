using System;
using System.IO;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Tests;

/// The token count's index (Ai\TokenIndex) and its passes (Ai\TokenScan): a call counted once however often its key
/// comes, the file kept and read back, tidied by age, and the logs read in parts as they grow, shrink, vanish or move.
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
            File.WriteAllBytes(path, [.. "BTKI"u8, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xF0, 0xFF, 0xFF, 0x7F]);
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
