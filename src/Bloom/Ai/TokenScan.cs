using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.InteropServices;
using Bloom.Core;

namespace Bloom.Ai;

/// One pass over the logs into a TokenIndex, on the thread that calls Run. The roots are each account's Claude Code
/// projects folder and Codex sessions and archived_sessions folders (Roots), met once each by their real path; a folder
/// below them that is a link is not gone into. A file whose size and time are as last seen is not opened; one that grew
/// is read from its offset; one that shrank is read again from the start (its keys keep it from counting twice). A Codex
/// rollout is followed by its thread's uuid: moved to archived_sessions with its time unchanged, or compressed to .zst
/// (never read: no zstd reader), only its path changes. The files are read newest first, so the last days fill first
/// in a long first count. Between two files, at most every AskEvery, Wanted is asked (no: the pass stops there) and,
/// during the first count, Progress is told; Checkpoint saves every SaveEvery or SaveBytes of it. The first count is
/// over once every file was tried: one that can't be read now (tried again next pass) or a line too long to keep
/// (LogLines) does not hold it open.
internal sealed class TokenScan
{
    public static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(2), SaveEvery = TimeSpan.FromSeconds(20);
    public const long SaveBytes = 500L << 20;

    private readonly TokenIndex index;
    private readonly IReadOnlyList<(bool Codex, string Folder)> roots;

    public TokenScan(TokenIndex index, IReadOnlyList<(bool Codex, string Folder)> roots)
    {
        this.index = index;
        this.roots = roots;
    }

    /// Asked between files: false stops the pass there (no block wants the numbers, or a Reset came).
    public Func<bool> Wanted { get; init; } = static () => true;

    /// During the first count: the share of the bytes to read that has been read, 0…1.
    public Action<double>? Progress { get; init; }

    /// During the first count: the index is whole between two files, save it now.
    public Action? Checkpoint { get; init; }

    /// The sums changed.
    public bool Changed { get; private set; }

    /// Anything in the index changed (the sums or a file's state): it wants saving.
    public bool Dirty { get; private set; }

    /// Wanted said no before every file was read.
    public bool Stopped { get; private set; }

    /// No log folder was found.
    public bool NoLogs => roots.Count == 0;

    public int FilesRead { get; private set; }
    public long BytesRead { get; private set; }

    /// A file as the folder listing tells it (Mtime: UTC ticks).
    private readonly record struct Found(string Path, long Size, long Mtime, bool Codex, bool Zst);

    /// The log folders of these accounts ((Kind "claude" or "chatgpt", home folder)) that are there, each once by its
    /// real path (a folder reached through a link is the folder it leads to).
    public static List<(bool Codex, string Folder)> Roots(IEnumerable<(string Kind, string Home)> homes)
    {
        var list = new List<(bool, string)>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, home) in homes)
        {
            bool codex = kind == "chatgpt";
            foreach (string sub in codex ? new[] { "sessions", "archived_sessions" } : ["projects"])
            {
                string folder = Path.Combine(home, sub);
                if (!Directory.Exists(folder)) continue;
                string real = Path.TrimEndingDirectorySeparator(RealPath.Final(folder) ?? Path.GetFullPath(folder));
                if (known.Add(real)) list.Add((codex, real));
            }
        }
        return list;
    }

    /// How the index knows a file: a Claude Code log by its path, a Codex rollout by its thread's uuid.
    public static string Id(bool codex, string path)
    {
        if (!codex) return path;
        string name = Path.GetFileName(path);
        name = name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        name = name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? name[..^6] : name;
        return "x|" + (name.Length >= 36 ? name[^36..] : name);
    }

    public void Run()
    {
        var clock = Stopwatch.StartNew();
        var present = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            try
            {
                foreach (var f in Files(root))
                {
                    string id = Id(f.Codex, f.Path);
                    // the same thread twice (being compressed or moved): the plain file, else the newer one
                    if (present.TryGetValue(id, out var had) && (had.Zst == f.Zst ? had.Mtime >= f.Mtime : f.Zst)) continue;
                    present[id] = f;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("token count: " + root.Folder, e); // gone while listed: its files are met next pass
            }

        foreach (string gone in index.Files.Keys.Where(id => !present.ContainsKey(id)).ToList())
        {
            index.Files.Remove(gone); // its sums stay
            Dirty = true;
        }

        var work = new List<(string Id, TokenFile File, Found Found)>();
        foreach (var (id, f) in present)
        {
            index.Files.TryGetValue(id, out var state);
            if (f.Zst)
            {
                // compressed after it was read: what was counted stays, the rest is not read
                if (state is not null && !SamePath(state.Path, f.Path))
                {
                    (state.Path, state.Size, state.Mtime) = (f.Path, f.Size, f.Mtime);
                    Dirty = true;
                }
                continue;
            }
            if (state is null)
            {
                work.Add((id, new TokenFile { Path = f.Path, Thread = f.Codex ? id[2..] : null }, f));
                continue;
            }
            if (!SamePath(state.Path, f.Path))
            {
                Dirty = true;
                state.Path = f.Path;
                if (state.Mtime == f.Mtime)
                {
                    state.Size = f.Size; // moved, not written to
                    continue;
                }
            }
            else if (state.Size == f.Size && state.Mtime == f.Mtime) continue;
            if (f.Size < state.Offset) state.Restart();
            if (f.Size == state.Offset)
            {
                (state.Size, state.Mtime) = (f.Size, f.Mtime);
                Dirty = true;
                continue;
            }
            work.Add((id, state, f));
        }
        work.Sort((a, b) => b.Found.Mtime.CompareTo(a.Found.Mtime));

        bool first = !index.Complete;
        long toRead = Math.Max(1, work.Sum(w => w.Found.Size - w.File.Offset));
        var lines = new LogLines();
        var asked = -AskEvery; // the first file asks at once
        var saved = clock.Elapsed;
        long savedBytes = 0;
        foreach (var (id, state, f) in work)
        {
            if (clock.Elapsed - asked >= AskEvery)
            {
                asked = clock.Elapsed;
                if (!Wanted())
                {
                    Stopped = true;
                    break;
                }
                if (first) Progress?.Invoke(Math.Min(1, (double)lines.Bytes / toRead));
            }
            Read(id, state, f, lines);
            if (first && Checkpoint is not null && (clock.Elapsed - saved >= SaveEvery || lines.Bytes - savedBytes >= SaveBytes))
            {
                index.Roll();
                Checkpoint();
                (saved, savedBytes) = (clock.Elapsed, lines.Bytes);
            }
        }
        BytesRead = lines.Bytes;
        if (!Stopped) ReadNames();
        if (!Stopped && !index.Complete)
        {
            index.Complete = true;
            Dirty = true;
        }
        index.Roll();
    }

    /// Reads one file from its offset into the index; a file that can't be read now is tried again next pass (what was
    /// counted of it stays counted once: its keys are known). A Claude Code call whose line names no session goes to
    /// the session the file is named after (<uuid>.jsonl), else to none; a title line names its session. A rollout's
    /// session_meta describes its thread once the file is read.
    private void Read(string id, TokenFile state, Found f, LogLines lines)
    {
        var written = new DateTime(f.Mtime, DateTimeKind.Utc);
        string? named = f.Codex ? null : UuidName(f.Path);
        var strings = f.Codex ? null : new ClaudeLog.Strings();
        try
        {
            long offset = lines.Read(f.Path, state.Offset, (line, at) =>
            {
                TokenCall call;
                if (f.Codex)
                {
                    if (CodexLog.Parse(line, state, at) is not { } read) return;
                    call = read;
                }
                else if (ClaudeLog.Parse(line, strings) is { } read)
                    call = read.Session is null && named is not null ? read with { Session = named } : read;
                else
                {
                    if (line.Length <= TitleLineMost && line.IndexOf("-title\""u8) >= 0 && ClaudeLog.Title(line) is { } t && (t.Session ?? named) is { } session
                        && index.Title("claude", session, t.Title, t.Custom, written)) Changed = true;
                    return;
                }
                if (TokenPrices.IsLong(call.Model, call.Counts)) call = call with { Variant = call.Variant | TokenVariant.Long };
                if (index.Add(call)) Changed = true;
            });
            (state.Offset, state.Size, state.Mtime) = (offset, f.Size, f.Mtime);
            if (state.Met)
            {
                if (index.Describe("codex", state.Thread!, state.Parent, state.Folder, state.Bloom, written)) Changed = true;
                (state.Met, state.Parent, state.Folder, state.Bloom) = (false, null, null, false);
            }
            index.Files[id] = state;
            Dirty = true;
            FilesRead++;
            if (f.Codex) codexRead = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("token count: " + Path.GetFileName(f.Path), e);
        }
        finally
        {
            index.EndFile();
        }
    }

    /// The longest line looked at for a session title (Claude Code writes them short, its type first).
    private const int TitleLineMost = 4096;

    /// The most read of a session_index.jsonl: its last part, where the newest names are.
    public const int NamesMost = 1 << 20;

    /// The session_index.jsonl files read, as they were (size, UTC ticks): read again when they change or a pass read
    /// a rollout (a thread new to the index may have been named since). The counting thread's only.
    private static readonly Dictionary<string, (long Size, long Mtime)> namesRead = new(StringComparer.OrdinalIgnoreCase);

    private bool codexRead;

    /// The Codex thread names next to the sessions folders (<home>\session_index.jsonl, the last line of an id wins)
    /// given to the threads the index has; a name of a thread it has not met is left (the pass that reads its rollout
    /// reads the names again).
    private void ReadNames()
    {
        foreach (string home in roots.Where(r => r.Codex).Select(r => Path.GetDirectoryName(r.Folder)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(home, "session_index.jsonl");
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                var now = (info.Length, info.LastWriteTimeUtc.Ticks);
                if (!codexRead && namesRead.TryGetValue(path, out var had) && had == now) continue;
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long from = Math.Max(0, file.Length - NamesMost);
                    file.Position = from;
                    var bytes = new byte[file.Length - from];
                    file.ReadExactly(bytes);
                    var rest = bytes.AsSpan();
                    if (from > 0) rest = rest[(rest.IndexOf((byte)'\n') + 1)..]; // a line cut by the start: none
                    while (rest.Length > 0)
                    {
                        int end = rest.IndexOf((byte)'\n');
                        var line = end < 0 ? rest : rest[..end];
                        rest = end < 0 ? [] : rest[(end + 1)..];
                        if (CodexLog.Name(line) is { Name: { } name } n) names[n.Id] = name;
                    }
                }
                foreach (var (id, name) in names)
                    if (index.Title("codex", id, name, custom: true, info.LastWriteTimeUtc, onlyKnown: true)) Changed = Dirty = true;
                namesRead[path] = now;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("token count: " + path, e); // the next pass
            }
        }
    }

    /// A Claude Code log named after its session (<uuid>.jsonl): the uuid; null for any other name.
    private static string? UuidName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return Guid.TryParseExact(name, "D", out _) ? name : null;
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// The root's log files: *.jsonl for Claude Code, rollout-*.jsonl[.zst] for Codex; no folder that is a link is gone into.
    private static FileSystemEnumerable<Found> Files((bool Codex, string Folder) root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        bool codex = root.Codex;
        var files = new FileSystemEnumerable<Found>(root.Folder,
            (ref FileSystemEntry e) => new Found(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcTicks, codex, e.FileName.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)), options);
        if (codex) files.ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.FileName.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)
            && (e.FileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || e.FileName.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase));
        else files.ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.FileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
        return files;
    }

    /// The calling thread's disk and memory priority lowered for the pass (on) and back after it (off); its CPU priority
    /// is the thread's own (ThreadPriority.Lowest).
    public static void Background(bool on) => SetThreadPriority(GetCurrentThread(), on ? BackgroundBegin : BackgroundEnd);

    private const int BackgroundBegin = 0x00010000, BackgroundEnd = 0x00020000; // THREAD_MODE_BACKGROUND_BEGIN / _END

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr thread, int priority);
}
