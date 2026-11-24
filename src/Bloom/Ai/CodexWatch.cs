using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bloom.Ai;

/// Codex's turns, read from the session logs Codex writes itself (the desktop app, CLI and IDE all do; nothing is
/// started): CODEX_HOME\sessions\YYYY\MM\DD\rollout-*-<thread id>.jsonl, where a turn runs from event_msg task_started to
/// task_complete | turn_aborted with approval requests in between; thread names in session_index.jsonl. Each look reads
/// only what was added since the last one. Every Codex folder is followed (Homes): T3 Code runs its Codex threads with
/// folders of their own (~/.codex-local-*).
internal sealed partial class CodexWatch
{
    private const int FirstRead = 256 * 1024; // a log seen for the first time: only its end, without news
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(40), Recent = TimeSpan.FromHours(8), PidRecheck = TimeSpan.FromSeconds(15);

    /// One log's state.
    internal sealed class Thread
    {
        public string? Cwd, Prompt;
        public bool Subagent, Headless, TurnOpen, Waiting;
        public DateTime TurnStart, LastEvent;
        public int? Pid; // the Codex process behind it, when CodexProcess could tell
        public DateTime PidAsked; // when (UTC)
        public long Offset = -1; // -1: not read yet
        public bool Skip; // Bloom's own run or a subagent: never news, so its lines are never read
    }

    private string[] homes = [];
    private readonly Dictionary<string, Thread> threads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> names = new();
    private readonly Dictionary<string, long> indexOffsets = new(StringComparer.OrdinalIgnoreCase); // a folder → how far its session_index.jsonl is read
    private DateTime lastScan;

    [GeneratedRegex(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.jsonl$", RegexOptions.IgnoreCase)]
    private static partial Regex ThreadId();

    [GeneratedRegex(@"""cwd"":""((?:[^""\\]|\\.)*)""")]
    private static partial Regex MetaCwd();

    [GeneratedRegex(@"""(?:thread_)?source""\s*:\s*(?:""[^""]*|\{[^}]*)sub_?agent", RegexOptions.IgnoreCase)]
    private static partial Regex MetaSubagent();

    [GeneratedRegex(@"""originator"":""[^""]*exec", RegexOptions.IgnoreCase)]
    private static partial Regex MetaExec();

    /// A turn completed (not stopped), seen as it happened: the task and how long it took.
    public event Action<AgentTask, TimeSpan>? Finished;

    /// Every Codex folder: CODEX_HOME (or ~/.codex) and the user's other ~/.codex-* folders.
    public static IEnumerable<string> Homes()
    {
        string main = CodexAuth.DefaultHome();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.TrimEndingDirectorySeparator(main) };
        yield return main;
        string[] found;
        try
        {
            found = Directory.GetDirectories(Core.Paths.Home, ".codex-*");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            found = [];
        }
        foreach (string d in found)
            if (seen.Add(Path.TrimEndingDirectorySeparator(d))) yield return d;
    }

    /// The turns working or waiting now (subagents and Bloom's own runs left out).
    public List<AgentTask> Poll()
    {
        if (DateTime.UtcNow - lastScan > TimeSpan.FromSeconds(10)) Scan();
        foreach (string home in homes) ReadNames(home);
        var now = DateTime.Now;
        var list = new List<AgentTask>();
        foreach (var (file, t) in threads)
        {
            Follow(file, t);
            if (t.TurnOpen && !t.Subagent && !AgentText.IsBloomRun(t.Cwd) && now - t.LastEvent < Stale) list.Add(Task(file, t));
        }
        return list;
    }

    /// New logs from today and yesterday; logs quiet for hours are let go.
    private void Scan()
    {
        lastScan = DateTime.UtcNow;
        homes = Homes().ToArray();
        var today = DateTime.Today;
        foreach (var (home, day) in homes.SelectMany(h => new[] { (h, today), (h, today.AddDays(-1)) }))
        {
            string dir = Path.Combine(home, "sessions", day.Year.ToString(CultureInfo.InvariantCulture), day.Month.ToString("00", CultureInfo.InvariantCulture), day.Day.ToString("00", CultureInfo.InvariantCulture));
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("rollout-*.jsonl"))
                    if (!threads.ContainsKey(f.FullName) && DateTime.UtcNow - f.LastWriteTimeUtc < Recent) threads[f.FullName] = new Thread();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                /* the next scan */
            }
        }
        foreach (var (file, t) in threads.ToList())
            if (!t.TurnOpen && t.Offset >= 0 && (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Recent)) threads.Remove(file);
    }

    /// What the log got since the last look (whole lines only).
    private void Follow(string file, Thread t)
    {
        try
        {
            long length = new FileInfo(file).Length;
            if (length == t.Offset) return;
            bool first = t.Offset < 0;
            if (first)
            {
                Meta(file, t);
                t.Skip = t.Subagent || AgentText.IsBloomRun(t.Cwd);
                t.Offset = Math.Max(0, length - FirstRead);
            }
            if (t.Skip)
            {
                t.Offset = length; // most of a day's logs are Bloom's own chats: left unread (was ~45 MB of garbage at start)
                return;
            }
            if (length < t.Offset) t.Offset = 0; // started over
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(t.Offset, SeekOrigin.Begin);
            var buf = new byte[length - t.Offset];
            int got = fs.ReadAtLeast(buf, buf.Length, false);
            int end = got > 0 ? Array.LastIndexOf(buf, (byte)'\n', got - 1) : -1;
            if (end < 0) return; // no whole line yet
            int start = first && t.Offset > 0 ? Array.IndexOf(buf, (byte)'\n') + 1 : 0; // a log read from its middle: the cut line goes
            t.Offset += end + 1;
            foreach (string line in Encoding.UTF8.GetString(buf, start, Math.Max(0, end + 1 - start)).Split('\n'))
            {
                var (ev, took) = Step(t, line);
                if (!first && ev == "done" && !t.Subagent && !AgentText.IsBloomRun(t.Cwd)) Finished?.Invoke(Task(file, t), took);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* busy: the next look */
        }
    }

    /// The first line (session_meta) says the folder and whether it is a subagent; it carries the whole instructions,
    /// so only its start is read and searched as text: 16 KB, or up to FirstRead when the folder isn't in those.
    private static void Meta(string file, Thread t)
    {
        string head = Head(file, MetaRead, out bool whole);
        if (!whole && !MetaCwd().IsMatch(head)) head = Head(file, FirstRead, out _);
        if (MetaCwd().Match(head) is { Success: true } m)
            try
            {
                t.Cwd = JsonSerializer.Deserialize<string>("\"" + m.Groups[1].Value + "\"");
            }
            catch (JsonException)
            {
                t.Cwd = null;
            }
        t.Subagent = MetaSubagent().IsMatch(head);
        t.Headless = MetaExec().IsMatch(head); // codex exec, run by a program
    }

    private const int MetaRead = 16 * 1024;

    /// The start of a log up to its first line's end (at most `bytes`; `whole`: the line ended within them).
    private static string Head(string file, int bytes, out bool whole)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[Math.Min(fs.Length, bytes)];
        int got = fs.ReadAtLeast(buf, buf.Length, false);
        int nl = Array.IndexOf(buf, (byte)'\n', 0, got);
        whole = nl >= 0 || got == fs.Length;
        return Encoding.UTF8.GetString(buf, 0, nl >= 0 ? nl : got);
    }

    /// One log line into the thread's state → "start" | "done" | "aborted" | null, and for "done" how long the turn took.
    internal static (string? ev, TimeSpan took) Step(Thread t, string line)
    {
        if (line.Length < 20 || !(line.Contains("\"event_msg\"", StringComparison.Ordinal) || line.Contains("\"response_item\"", StringComparison.Ordinal) || line.Contains("\"turn_context\"", StringComparison.Ordinal)))
            return (null, default);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var at = Str(r, "timestamp") is { } ts && DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : DateTime.Now;
            if (at > t.LastEvent) t.LastEvent = at;
            if (!r.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return (null, default);
            switch (Str(r, "type"))
            {
                case "turn_context":
                    if (Str(p, "cwd") is { Length: > 0 } cwd) t.Cwd = cwd;
                    return (null, default);
                case "response_item":
                    if ((Str(p, "type") ?? "").EndsWith("call", StringComparison.Ordinal)) t.Waiting = false; // it went on
                    return (null, default);
            }
            switch (Str(p, "type"))
            {
                case "task_started" or "turn_started":
                    t.TurnOpen = true;
                    t.Waiting = false;
                    t.TurnStart = p.TryGetProperty("started_at", out var sa) && sa.TryGetInt64(out long s) && s > 0 ? DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime : at;
                    return ("start", default);
                case "task_complete" or "turn_complete":
                    var took = p.TryGetProperty("duration_ms", out var dm) && dm.TryGetInt64(out long ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms)
                        : t.TurnOpen ? at - t.TurnStart : TimeSpan.Zero;
                    t.TurnOpen = t.Waiting = false;
                    return ("done", took);
                case "turn_aborted":
                    t.TurnOpen = t.Waiting = false;
                    return ("aborted", default);
                case "user_message":
                    if (Str(p, "message") is { } msg && !msg.TrimStart().StartsWith('<')) t.Prompt = AgentText.Line(msg);
                    return (null, default);
                case "exec_approval_request" or "apply_patch_approval_request" or "request_user_input" or "elicitation_request":
                    t.Waiting = true;
                    return (null, default);
                case "exec_command_begin" or "exec_command_end" or "item_completed":
                    t.Waiting = false;
                    return (null, default);
            }
        }
        catch (JsonException)
        {
            /* not a whole JSON line */
        }
        return (null, default);
    }

    private AgentTask Task(string file, Thread t)
    {
        string id = ThreadId().Match(file) is { Success: true } m ? m.Groups[1].Value : file;
        string title = (names.TryGetValue(id, out var name) ? AgentText.Title(name) : null) ?? t.Prompt ?? "Codex";
        return new AgentTask("codex:" + id, "Codex", title, t.Prompt == title ? null : t.Prompt, t.Waiting, t.TurnStart == default ? t.LastEvent : t.TurnStart, t.Headless, t.Headless ? null : Pid(file, t));
    }

    /// The process behind a thread, asked again now and then (a thread can be resumed in another process).
    private static int? Pid(string file, Thread t)
    {
        if (DateTime.UtcNow - t.PidAsked > PidRecheck)
        {
            t.PidAsked = DateTime.UtcNow;
            t.Pid = CodexProcess.Find(File.GetCreationTimeUtc(file));
        }
        return t.Pid;
    }

    /// Thread names (session_index.jsonl: id, thread_name), following the file like a log.
    private void ReadNames(string home)
    {
        string index = Path.Combine(home, "session_index.jsonl");
        try
        {
            if (!File.Exists(index)) return;
            long length = new FileInfo(index).Length;
            long indexOffset = indexOffsets.GetValueOrDefault(home, -1);
            if (length == indexOffset) return;
            if (indexOffset < 0 || length < indexOffset) indexOffset = Math.Max(0, length - (1024 * 1024));
            using var fs = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(indexOffset, SeekOrigin.Begin);
            var buf = new byte[length - indexOffset];
            int got = fs.ReadAtLeast(buf, buf.Length, false);
            int end = got > 0 ? Array.LastIndexOf(buf, (byte)'\n', got - 1) : -1;
            if (end < 0) return;
            indexOffsets[home] = indexOffset + end + 1;
            foreach (string line in Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
            {
                if (!line.StartsWith('{')) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (Str(doc.RootElement, "id") is { } id && Str(doc.RootElement, "thread_name") is { } name) names[id] = name;
                }
                catch (JsonException)
                {
                    /* a cut line */
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* the next look */
        }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
