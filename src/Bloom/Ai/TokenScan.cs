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
/// during the first count, Progress is told; Checkpoint saves every SaveEvery or SaveBytes of it.
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
        if (!Stopped && !index.Complete)
        {
            index.Complete = true;
            Dirty = true;
        }
        index.Roll();
    }

    /// Reads one file from its offset into the index; a file that can't be read now is tried again next pass (what was
    /// counted of it stays counted once: its keys are known).
    private void Read(string id, TokenFile state, Found f, LogLines lines)
    {
        try
        {
            long offset = lines.Read(f.Path, state.Offset, (line, at) =>
            {
                if ((f.Codex ? CodexLog.Parse(line, state, at) : ClaudeLog.Parse(line)) is not { } call) return;
                if (TokenPrices.IsLong(call.Model, call.Counts)) call = call with { Variant = call.Variant | TokenVariant.Long };
                if (index.Add(call)) Changed = true;
            });
            (state.Offset, state.Size, state.Mtime) = (offset, f.Size, f.Mtime);
            index.Files[id] = state;
            Dirty = true;
            FilesRead++;
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
