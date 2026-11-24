using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Ai;

/// Claude Code's turns, read from what Claude Code writes itself (nothing is started): every running Claude Code — the
/// CLI, the desktop app's Code tab, T3 Code's threads — keeps <folder>/sessions/<pid>.json up to date with its session
/// id, title, folder and status (busy / idle / waiting); the transcript under <folder>/projects says what was asked.
/// The folders are all of Homes(): T3 Code gives its Claude Code a folder of its own (a ~/.claude-* one).
internal sealed partial class ClaudeWatch
{
    private sealed record Info(string SessionId, int Pid, long ProcStart, string Status, string? Name, string? Cwd, long StatusAt, bool Headless)
    {
        public string Home { get; init; } = "";
    }

    private string[] homes = [];
    private DateTime homesAt;
    private readonly Dictionary<string, (DateTime write, Info? info)> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (DateTime at, bool alive)> alive = new();
    private readonly Dictionary<string, string?> prompts = new(); // session id + turn start → what was asked
    private Dictionary<string, AgentTask> active = new();

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex NotSlug();

    [GeneratedRegex(@"wait|permission|approval|input|blocked", RegexOptions.IgnoreCase)]
    private static partial Regex WaitingStatus();

    /// A turn ended normally: the session went idle, its Claude Code still running (closing it is no news).
    public event Action<AgentTask, TimeSpan>? Finished;

    /// Claude Code's own folder: CLAUDE_CONFIG_DIR, else ~/.claude.
    public static string Home => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } h ? h : Path.Combine(Paths.Home, ".claude");

    /// The running Claude Code with this process id, from its session file: typed in (not a program's `claude -p`), its
    /// name and folder; null when it has none.
    public static (bool Interactive, string? Name, string? Cwd)? Session(int pid) => Session(pid, Home);

    /// The same in another Claude Code folder (`home`): a session started with its own CLAUDE_CONFIG_DIR keeps its
    /// session file there.
    public static (bool Interactive, string? Name, string? Cwd)? Session(int pid, string home)
    {
        try
        {
            using var fs = new FileStream(Path.Combine(home, "sessions", pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            return (Str(r, "kind") is not { } kind || kind == "interactive", Str(r, "name"), Str(r, "cwd"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// Every Claude Code folder a session may keep its file in: Home, the user's other ~/.claude-* folders (a
    /// CLAUDE_CONFIG_DIR of their own) and Bloom's own Claude accounts (Local\accounts\claude-*), as the Run Card add-on
    /// looks for them.
    public static IEnumerable<string> Homes()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.TrimEndingDirectorySeparator(Home) };
        yield return Home;
        foreach (var (dir, pattern) in new[] { (Paths.Home, ".claude-*"), (Path.Combine(Paths.Local, "accounts"), "claude-*") })
        {
            string[] found;
            try
            {
                found = Directory.Exists(dir) ? Directory.GetDirectories(dir, pattern) : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                found = [];
            }
            foreach (string d in found)
                if (seen.Add(Path.TrimEndingDirectorySeparator(d))) yield return d;
        }
    }

    /// The running Claude Code `pid` is `session` typed in a terminal, its session file in one of the folders says: started
    /// from the CLI (entrypoint "cli"), interactive, and the process that wrote it still runs. Only such a one reads its
    /// console, where Esc stops its turn (Run Card's 정지); the desktop app, an SDK host (T3 Code) or an editor's panel
    /// read none.
    public static bool InTerminal(string session, int pid) => InTerminal(session, pid, Homes(), Running);

    internal static bool InTerminal(string session, int pid, IEnumerable<string> homes, Func<int, long, bool> running)
    {
        if (pid <= 0 || session.Length == 0) return false;
        string name = pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json";
        foreach (string home in homes)
        {
            try
            {
                using var fs = new FileStream(Path.Combine(home, "sessions", name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                var r = doc.RootElement;
                if (Str(r, "sessionId") == session && Str(r, "entrypoint") == "cli" && (Str(r, "kind") is not { } kind || kind == "interactive"))
                    return running(pid, Num(r, "procStart"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                /* none in this folder */
            }
        }
        return false;
    }

    /// The process runs and is the one that started at `procStart` (0: not known), not another that took its id.
    private static bool Running(int pid, long procStart)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && (procStart <= 0 || Math.Abs(p.StartTime.ToFileTimeUtc() - procStart) < 20_000_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// The Claude Code folder a transcript is kept in: `<folder>\projects\<project>\<session>.jsonl` → `<folder>`; null
    /// for a path not of that shape (relative, another file, not under a `projects` folder).
    public static string? HomeOf(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript) || !Path.IsPathFullyQualified(transcript)) return null;
        try
        {
            string file = Path.GetFullPath(transcript);
            if (!file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(file).Length == 0) return null;
            string? project = Path.GetDirectoryName(file), projects = project is null ? null : Path.GetDirectoryName(project);
            if (projects is null || !Path.GetFileName(projects).Equals("projects", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetDirectoryName(projects) is { Length: > 0 } home ? home : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// The sessions working or waiting now (Bloom's own CLI runs left out).
    public List<AgentTask> Poll()
    {
        var now = new Dictionary<string, AgentTask>();
        var infos = new Dictionary<string, Info>();
        // the folders looked for again each half minute (a new account, T3 Code's first thread)
        if (DateTime.UtcNow - homesAt > TimeSpan.FromSeconds(30))
        {
            homes = Homes().ToArray();
            homesAt = DateTime.UtcNow;
        }
        var all = new List<string>();
        foreach (string home in homes)
        {
            try
            {
                string dir = Path.Combine(home, "sessions");
                if (Directory.Exists(dir)) all.AddRange(Directory.GetFiles(dir, "*.json"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                /* this folder on the next look */
            }
        }
        foreach (string f in all)
        {
            if (Read(f) is not { } info || AgentText.IsBloomRun(info.Cwd)) continue;
            infos[info.SessionId] = info;
            bool working = info.Status is "busy" or "running" or "working" or "streaming" or "thinking";
            bool waiting = !working && WaitingStatus().IsMatch(info.Status);
            if ((!working && !waiting) || !Alive(info.Pid, info.ProcStart)) continue;
            var since = DateTimeOffset.FromUnixTimeMilliseconds(info.StatusAt > 0 ? info.StatusAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).LocalDateTime;
            // a wait inside a turn keeps the turn's start
            if (active.TryGetValue(info.SessionId, out var before)) since = before.Since;
            string? prompt = Prompt(info, since);
            string title = AgentText.Title(info.Name) ?? prompt ?? "Claude Code";
            now[info.SessionId] = new AgentTask("claude:" + info.SessionId, "Claude Code", title, prompt == title ? null : prompt, waiting, since, info.Headless, info.Pid);
        }
        foreach (var (id, task) in active)
            if (!now.ContainsKey(id) && infos.TryGetValue(id, out var info) && info.Status == "idle" && Alive(info.Pid, info.ProcStart))
                Finished?.Invoke(task, DateTime.Now - task.Since);
        active = now;
        foreach (string gone in files.Keys.Except(all, StringComparer.OrdinalIgnoreCase).ToList()) files.Remove(gone);
        if (prompts.Count > 200) prompts.Clear();
        if (alive.Count > 200) alive.Clear();
        return now.Values.ToList();
    }

    /// A session file, read again only when it changed.
    private Info? Read(string file)
    {
        DateTime write;
        try
        {
            write = File.GetLastWriteTimeUtc(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (files.TryGetValue(file, out var known) && known.write == write) return known.info;
        Info? info = null;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            if (Str(r, "sessionId") is { } id && r.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out int p))
                info = new Info(id, p, Num(r, "procStart"), (Str(r, "status") ?? "").ToLowerInvariant(), Str(r, "name"), Str(r, "cwd"), Num(r, "statusUpdatedAt"), Str(r, "kind") is { } kind && kind != "interactive")
                {
                    Home = Path.GetDirectoryName(Path.GetDirectoryName(file)) ?? Home, // <folder>\sessions\<pid>.json
                };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return known.info; // being written: the last one stands
        }
        files[file] = (write, info);
        return info;
    }

    /// The process still runs and is the same one (a pid comes back for other programs); asked at most every 15 s.
    private bool Alive(int pid, long procStart)
    {
        if (alive.TryGetValue(pid, out var a) && DateTime.UtcNow - a.at < TimeSpan.FromSeconds(15)) return a.alive;
        bool yes;
        try
        {
            using var p = Process.GetProcessById(pid);
            yes = !p.HasExited && (procStart <= 0 || Math.Abs(p.StartTime.ToFileTimeUtc() - procStart) < 20_000_000);
        }
        catch (ArgumentException)
        {
            yes = false; // not running
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            yes = true; // running, but not ours to look into
        }
        alive[pid] = (DateTime.UtcNow, yes);
        return yes;
    }

    /// What was asked last in this turn: the newest user line at the end of the transcript.
    private string? Prompt(Info info, DateTime since)
    {
        string key = info.SessionId + "|" + since.Ticks;
        if (prompts.TryGetValue(key, out var known)) return known;
        string? found = null;
        try
        {
            string file = Path.Combine(info.Home, "projects", NotSlug().Replace(info.Cwd ?? "", "-"), info.SessionId + ".jsonl");
            if (File.Exists(file)) found = LastPrompt(Tail(file, 256 * 1024));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            found = null;
        }
        // not written yet at the very start of a turn: asked again on the next looks, for a little while
        if (found is not null || DateTime.Now - since > TimeSpan.FromSeconds(15)) prompts[key] = found;
        return found;
    }

    /// The newest prompt in transcript lines: Claude Code's own `last-prompt` note, or a typed user line (not a tool
    /// result, command or meta line) — a long turn of tool calls has no user line near the end, the note comes often.
    internal static string? LastPrompt(string jsonl)
    {
        var lines = jsonl.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            bool note = line.Contains("\"type\":\"last-prompt\"", StringComparison.Ordinal);
            if (!note && !line.Contains("\"type\":\"user\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (note)
                {
                    if (Str(r, "lastPrompt") is { } lp && !lp.TrimStart().StartsWith('<') && AgentText.Line(lp) is { } said) return said;
                    continue;
                }
                if (Bool(r, "isMeta") || Bool(r, "isSidechain") || !r.TryGetProperty("message", out var m) || !m.TryGetProperty("content", out var c)) continue;
                string? text = c.ValueKind == JsonValueKind.String ? c.GetString()
                    : c.ValueKind == JsonValueKind.Array && !c.EnumerateArray().Any(b => Str(b, "type") == "tool_result")
                        ? c.EnumerateArray().Where(b => Str(b, "type") == "text").Select(b => Str(b, "text")).FirstOrDefault()
                        : null;
                if (text is null || text.TrimStart().StartsWith('<')) continue; // <command-name>, <system-reminder> …
                return AgentText.Line(text);
            }
            catch (JsonException)
            {
                /* a line cut by the tail */
            }
        }
        return null;
    }

    private static string Tail(string file, int bytes)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, fs.Length - bytes);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[fs.Length - start];
        fs.ReadExactly(buf);
        return Encoding.UTF8.GetString(buf);
    }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
