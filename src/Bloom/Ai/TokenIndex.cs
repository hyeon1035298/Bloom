using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Core;

namespace Bloom.Ai;

/// What the token count keeps between passes, in one file (%LOCALAPPDATA%\Bloom\tokens\index.bin) so that the sums, the
/// files' offsets and the keys counted are always of the same moment: hourly sums per model, variant (its effort too)
/// and session (TokenBucket), the sessions met (TokenSessionInfo), each log file's TokenFile, the keys counted (seen)
/// with their hour, and the recent keys' counted values (open). No message text and no whole folder path is kept: the
/// logs' paths on this PC, model names, the sessions' ids, titles (none for Bloom's own chats) and last folder names,
/// key hashes and numbers.
///
/// A call goes to the session its line names (TokenCall.Session; a resumed copy names the session it came from, so it
/// goes there however the files are read); a Codex subagent's or guardian's thread shows under its top thread
/// (Snapshot). Sums older than HourDays fold into days with no session, and a session not seen for HourDays leaves the
/// table (Roll), so the table and the sums keep about HourDays of sessions.
///
/// A call is counted once however often its key comes (Add): the lines of one response in the file being read add only
/// what they hold above the most counted so far, field by field, to the hour first counted; so do the keys of the last
/// OpenHours (a response cut off between two passes); any other key counted before is a resumed or forked copy and adds
/// nothing. A copy whose values differ from the first one met (one side all zeros) keeps the first one's: accepted.
/// Once the first count is over, a call older than SeenDays whose key is not kept is such a copy too (its key was
/// forgotten): a log first met after that with calls only of then is not counted (one that could not be read during
/// the first count, or an account added later with older logs; 설정's 토큰 집계 지우기 counts them all again).
internal sealed class TokenIndex
{
    /// The file's layout; another one is not read (the count starts again). 3: a session's folder from its own log.
    public const int Version = 3;

    /// Hourly sums older than this become one sum per UTC day, put at its noon so that a local day or month takes the
    /// day it shares the most hours with (the 90-day blocks stay hourly). The All period's month bars may so take up
    /// to 12 hours of a neighbouring day at a month's edge, for the months past HourDays only; the totals stay whole.
    public const int HourDays = 100;

    /// Keys older than this are forgotten once the first count is over (a copy keeps the original's time, so they are
    /// kept well past the 30 days Claude Code keeps its logs; during the first count none is, as the copy and the
    /// original may be read far apart).
    public const int SeenDays = 120;

    /// A key this recent may still be growing in a file being written (open).
    public const int OpenHours = 6;

    /// How many parents up a Codex thread is followed to its top one (a loop stops sooner).
    public const int ParentsMost = 8;

    private static ReadOnlySpan<byte> Magic => "BTKI"u8;

    private readonly List<(string Provider, string Model)> models = [];
    private readonly Dictionary<(string Provider, string Model), int> modelIds = new();
    private readonly List<TokenSessionInfo> sessions = [];
    private readonly Dictionary<(string Provider, string Id), int> sessionIds = new();
    private readonly Dictionary<Cell, TokenCounts> cells = new();
    private readonly Dictionary<ulong, int> seen = new(); // key → its hour (since 0001-01-01)
    private readonly Dictionary<ulong, Applied> open = new();
    private readonly Dictionary<ulong, Applied> inFile = new();

    /// The log files known, by TokenScan.Id.
    public Dictionary<string, TokenFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// Every log file found has been read through once (the first count is over).
    public bool Complete { get; set; }

    /// What "recent" is measured from (OpenHours, Roll); UTC.
    public DateTime Now { get; set; } = DateTime.UtcNow;

    public int SeenCount => seen.Count;
    public int OpenCount => open.Count;

    /// The sessions met, as the table holds them now (the counting thread; the tests).
    public IReadOnlyList<TokenSessionInfo> Sessions => sessions;

    /// One sum: its hour (UTC ticks), model (index into models), variant and session (index into sessions, -1: none).
    private readonly record struct Cell(long Hour, int Model, TokenVariant Variant, int Session);

    /// What a key has added so far, field by field, and the sum it went to.
    private readonly record struct Applied(Cell Cell, TokenCounts Most);

    /// Counts a call → whether the sums or a session's folder changed (see the class). EndFile follows each file's lines.
    public bool Add(TokenCall call)
    {
        bool folder = call.Own && OwnFolder(call);
        if (inFile.TryGetValue(call.Key, out var applied) || open.TryGetValue(call.Key, out applied))
        {
            var most = Most(applied.Most, call.Counts);
            if (most == applied.Most) return folder;
            Put(applied.Cell, Less(most, applied.Most));
            applied = applied with { Most = most };
            inFile[call.Key] = applied;
            if (open.ContainsKey(call.Key)) open[call.Key] = applied;
            return true;
        }
        if (seen.ContainsKey(call.Key) || (Complete && Hours(call.Time) < Forgotten)) return folder;
        var counts = call.Counts with { Calls = 1 };
        int session = string.IsNullOrEmpty(call.Session) ? -1 : SessionOf(call.Provider, call.Session, call.Folder, call.Own, Hours(call.Time));
        var cell = new Cell(Floor(call.Time).Ticks, ModelId(call.Provider, call.Model), call.Variant, session);
        Put(cell, counts);
        seen[call.Key] = Hours(call.Time);
        applied = new Applied(cell, counts);
        inFile[call.Key] = applied;
        if (call.Time >= Now.AddHours(-OpenHours)) open[call.Key] = applied;
        return true;
    }

    /// The file whose lines were being added is done: its keys are now only seen (or open).
    public void EndFile() => inFile.Clear();

    /// Tidies by Now: hourly sums older than HourDays fold into UTC days (at noon; a day folded at its midnight before
    /// moves there too) with no session, sessions not seen for HourDays leave the table (the others numbered again),
    /// keys older than SeenDays are forgotten once the first count is over, keys older than OpenHours are no longer open.
    public void Roll()
    {
        long fold = Now.AddDays(-HourDays).Ticks, noon = TimeSpan.TicksPerDay / 2;
        foreach (var (cell, counts) in cells.Where(c => c.Key.Hour < fold && (c.Key.Hour % TimeSpan.TicksPerDay != noon || c.Key.Session >= 0)).ToList())
        {
            cells.Remove(cell);
            Put(cell with { Hour = cell.Hour - cell.Hour % TimeSpan.TicksPerDay + noon, Session = -1 }, counts);
        }
        DropSessions(Hours(Now.AddDays(-HourDays)));
        if (Complete)
        {
            int old = Forgotten;
            foreach (var key in seen.Where(s => s.Value < old).Select(s => s.Key).ToList()) seen.Remove(key);
        }
        long recent = Floor(Now.AddHours(-OpenHours)).Ticks;
        foreach (var key in open.Where(o => o.Value.Cell.Hour < recent).Select(o => o.Key).ToList()) open.Remove(key);
    }

    /// The sessions last seen before `hour` leave the table; the rest are numbered again, in the sums and the open keys
    /// too (a sum of a session gone, which Roll's fold leaves none of, goes to no session).
    private void DropSessions(int hour)
    {
        if (!sessions.Exists(s => s.Seen < hour)) return;
        var map = new int[sessions.Count];
        var kept = new List<TokenSessionInfo>(sessions.Count);
        for (int i = 0; i < sessions.Count; i++)
        {
            map[i] = sessions[i].Seen < hour ? -1 : kept.Count;
            if (map[i] >= 0) kept.Add(sessions[i]);
        }
        sessions.Clear();
        sessions.AddRange(kept);
        sessionIds.Clear();
        for (int i = 0; i < sessions.Count; i++) sessionIds[(sessions[i].Provider, sessions[i].Id)] = i;
        Cell Moved(Cell c) => c.Session < 0 ? c : c with { Session = map[c.Session] };
        var all = cells.ToList();
        cells.Clear();
        foreach (var (cell, counts) in all) Put(Moved(cell), counts);
        foreach (var (key, applied) in open.ToList()) open[key] = applied with { Cell = Moved(applied.Cell) };
        foreach (var (key, applied) in inFile.ToList()) inFile[key] = applied with { Cell = Moved(applied.Cell) };
    }

    /// The sums, for the blocks (a copy: the index may go on changing on the counting thread). Each sum's session is
    /// its top one; the sessions' entries are shared, never changed.
    public TokenBucket[] Snapshot()
    {
        var tops = new TokenSessionInfo?[sessions.Count];
        for (int i = 0; i < tops.Length; i++) tops[i] = Top(i);
        return cells.Select(c => new TokenBucket(new DateTime(c.Key.Hour, DateTimeKind.Utc), models[c.Key.Model].Provider, models[c.Key.Model].Model, c.Key.Variant, c.Value,
            c.Key.Session < 0 ? null : tops[c.Key.Session])).ToArray();
    }

    /// The session a thread shows under: its parent's, up to ParentsMost up, stopping at a parent not in the table or
    /// one met before on the way (a loop).
    private TokenSessionInfo Top(int i)
    {
        var at = sessions[i];
        Span<int> way = stackalloc int[ParentsMost + 1];
        way[0] = i;
        for (int n = 1; n <= ParentsMost && at.Parent is { } parent && sessionIds.TryGetValue((at.Provider, parent), out int up) && !way[..n].Contains(up); n++)
        {
            way[n] = up;
            at = sessions[up];
        }
        return at;
    }

    /// A call's session (made when new): the hour it was last seen moves on, and its folder is set (WithFolder).
    private int SessionOf(string provider, string id, string? cwd, bool own, int hour)
    {
        if (sessionIds.TryGetValue((provider, id), out int i))
        {
            var had = sessions[i];
            var now = WithFolder(had with { Seen = Math.Max(had.Seen, hour) }, cwd, own);
            if (now != had) sessions[i] = now;
            return i;
        }
        return AddSession(WithFolder(new TokenSessionInfo(provider, id, null, null, TokenSessionInfo.Named.None, null, false, hour), cwd, own));
    }

    /// The folder of a call from its session's own log, even one counted before from a copy → whether it changed.
    private bool OwnFolder(TokenCall call)
    {
        if (call.Folder is null || string.IsNullOrEmpty(call.Session) || !sessionIds.TryGetValue((call.Provider, call.Session), out int i)) return false;
        var had = sessions[i];
        var now = WithFolder(had, call.Folder, own: true);
        if (now == had) return false;
        sessions[i] = now;
        return true;
    }

    /// A session's folder whichever file is read first: the first one its own log names (a file is read in order), else
    /// the first one met in another file (a subagent's log, a resumed copy) until the own log names one. A session in
    /// Bloom's AI folder is Bloom's (no title kept); the own log's folder judges that again (a title dropped stays so).
    private static TokenSessionInfo WithFolder(TokenSessionInfo s, string? cwd, bool own)
    {
        if ((s.Folder is not null && (s.FolderOwn || !own)) || TokenSessionInfo.Leaf(cwd) is not { } leaf) return s;
        if (TokenSessionInfo.IsBloom(cwd)) return s with { Folder = leaf, FolderOwn = own, Bloom = true, Title = null, Kind = TokenSessionInfo.Named.None };
        return s with { Folder = leaf, FolderOwn = own, Bloom = s.Bloom && s.Folder is null };
    }

    private int AddSession(TokenSessionInfo s)
    {
        sessions.Add(s);
        return sessionIds[(s.Provider, s.Id)] = sessions.Count - 1;
    }

    /// A session's title (Claude Code's custom-title or ai-title line, a Codex thread_name) → whether it changed. The
    /// last one met wins, but an ai title never replaces a custom one; Bloom's own sessions keep none (a Claude Code
    /// session Bloom named, TokenSessionInfo.BloomTitle, is Bloom's). A session not in the table is made (`seen`: when
    /// its log was written) unless `onlyKnown`.
    public bool Title(string provider, string id, string? title, bool custom, DateTime seen, bool onlyKnown = false)
    {
        if (TokenSessionInfo.Clean(title) is not { } clean) return false;
        var kind = custom ? TokenSessionInfo.Named.Custom : TokenSessionInfo.Named.Ai;
        bool bloom = custom && provider == "claude" && clean.StartsWith(TokenSessionInfo.BloomTitle, StringComparison.Ordinal);
        if (!sessionIds.TryGetValue((provider, id), out int i))
        {
            if (onlyKnown) return false;
            AddSession(bloom ? new TokenSessionInfo(provider, id, null, null, TokenSessionInfo.Named.None, null, true, Hours(seen))
                : new TokenSessionInfo(provider, id, null, clean, kind, null, false, Hours(seen)));
            return true;
        }
        var had = sessions[i];
        if (bloom && !had.Bloom)
        {
            sessions[i] = had with { Bloom = true, Title = null, Kind = TokenSessionInfo.Named.None };
            return true;
        }
        if (had.Bloom || (had.Kind == TokenSessionInfo.Named.Custom && !custom) || (had.Title == clean && had.Kind == kind)) return false;
        sessions[i] = had with { Title = clean, Kind = kind };
        return true;
    }

    /// What a Codex rollout's session_meta told of its thread (TokenFile.Met) → whether anything changed: the parent it
    /// shows under, its folder (the first one met), Bloom's own (its title goes). Made when not in the table yet
    /// (`seen`: when its log was written).
    public bool Describe(string provider, string id, string? parent, string? folder, bool bloom, DateTime seen)
    {
        if (!sessionIds.TryGetValue((provider, id), out int i)) i = AddSession(new TokenSessionInfo(provider, id, null, null, TokenSessionInfo.Named.None, null, false, Hours(seen)));
        var had = sessions[i];
        var now = had with
        {
            Parent = parent ?? had.Parent,
            Folder = had.Folder ?? folder,
            Bloom = had.Bloom || bloom,
        };
        if (now.Bloom) now = now with { Title = null, Kind = TokenSessionInfo.Named.None };
        if (now == had) return false;
        sessions[i] = now;
        return true;
    }

    private void Put(Cell cell, TokenCounts counts) => cells[cell] = cells.TryGetValue(cell, out var had) ? had + counts : counts;

    private int ModelId(string provider, string model)
    {
        if (modelIds.TryGetValue((provider, model), out int id)) return id;
        models.Add((provider, model));
        return modelIds[(provider, model)] = models.Count - 1;
    }

    private static DateTime Floor(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);

    private static int Hours(DateTime utc) => (int)(utc.Ticks / TimeSpan.TicksPerHour);

    /// The hour before which keys are forgotten (Roll) and, the first count over, calls not counted (Add).
    private int Forgotten => Hours(Now.AddDays(-SeenDays));

    /// Field by field the larger (Calls as in `a`).
    private static TokenCounts Most(TokenCounts a, TokenCounts b) =>
        new(a.Calls, Math.Max(a.Input, b.Input), Math.Max(a.Output, b.Output), Math.Max(a.CacheRead, b.CacheRead), Math.Max(a.CacheWrite, b.CacheWrite), Math.Max(a.CacheWriteLong, b.CacheWriteLong));

    /// `a` − `b`, field by field, no call.
    private static TokenCounts Less(TokenCounts a, TokenCounts b) =>
        new(0, a.Input - b.Input, a.Output - b.Output, a.CacheRead - b.CacheRead, a.CacheWrite - b.CacheWrite, a.CacheWriteLong - b.CacheWriteLong);

    /// The index kept at `path`; an empty one when there is none, it is of another Version, or it can't be read, cut
    /// short or broken (logged).
    public static TokenIndex Load(string path)
    {
        if (!File.Exists(path)) return new TokenIndex();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var r = new BinaryReader(stream, Encoding.UTF8);
            Span<byte> magic = stackalloc byte[4];
            r.BaseStream.ReadExactly(magic);
            int version = r.ReadInt32();
            if (!magic.SequenceEqual(Magic) || version != Version)
            {
                Log.Write($"token count: index of another layout ({version}), counting again");
                return new TokenIndex();
            }
            var index = new TokenIndex { Complete = r.ReadBoolean() };
            for (int i = r.ReadInt32(); i > 0; i--) index.ModelId(r.ReadString(), r.ReadString());
            int count = r.ReadInt32();
            if (count < 0 || count > stream.Length - stream.Position) throw new FormatException("more sessions than the file holds");
            for (int i = 0; i < count; i++)
            {
                var s = ReadSession(r);
                if (index.sessionIds.ContainsKey((s.Provider, s.Id))) throw new FormatException("a session twice");
                index.AddSession(s);
            }
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                var cell = index.ReadCell(r);
                index.cells[cell] = ReadCounts(r);
            }
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                string id = r.ReadString();
                var f = new TokenFile { Path = r.ReadString(), Size = r.ReadInt64(), Mtime = r.ReadInt64(), Offset = r.ReadInt64() };
                if (r.ReadBoolean())
                {
                    f.Thread = r.ReadString();
                    f.Model = r.ReadBoolean() ? string.Intern(r.ReadString()) : null;
                    byte effort = r.ReadByte();
                    f.Effort = effort <= (byte)TokenEffort.Max ? (TokenEffort)effort : TokenEffort.Unknown;
                    f.Records = r.ReadBoolean();
                    f.LastTotal = r.ReadUInt64();
                }
                index.Files[id] = f;
            }
            int keys = r.ReadInt32();
            if (keys < 0 || keys > (stream.Length - stream.Position) / 12) throw new FormatException("more keys than the file holds");
            index.seen.EnsureCapacity(keys);
            for (int i = 0; i < keys; i++) index.seen[r.ReadUInt64()] = r.ReadInt32();
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                ulong key = r.ReadUInt64();
                index.open[key] = new Applied(index.ReadCell(r), ReadCounts(r));
            }
            return index;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or OutOfMemoryException)
        {
            Log.Error("token count: index not read, counting again", e);
            return new TokenIndex();
        }
    }

    /// Writes the whole index at once (a staging file swapped in).
    public void Save(string path) => AtomicFile.Write(path, stream =>
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(Complete);
        w.Write(models.Count);
        foreach (var (provider, model) in models)
        {
            w.Write(provider);
            w.Write(model);
        }
        w.Write(sessions.Count);
        foreach (var s in sessions) WriteSession(w, s);
        w.Write(cells.Count);
        foreach (var (cell, counts) in cells)
        {
            WriteCell(w, cell);
            WriteCounts(w, counts);
        }
        w.Write(Files.Count);
        foreach (var (id, f) in Files)
        {
            w.Write(id);
            w.Write(f.Path);
            w.Write(f.Size);
            w.Write(f.Mtime);
            w.Write(f.Offset);
            w.Write(f.IsCodex);
            if (!f.IsCodex) continue;
            w.Write(f.Thread!);
            w.Write(f.Model is not null);
            if (f.Model is not null) w.Write(f.Model);
            w.Write((byte)f.Effort);
            w.Write(f.Records);
            w.Write(f.LastTotal);
        }
        w.Write(seen.Count);
        foreach (var (key, hour) in seen) // in no order: Load needs none
        {
            w.Write(key);
            w.Write(hour);
        }
        w.Write(open.Count);
        foreach (var (key, applied) in open)
        {
            w.Write(key);
            WriteCell(w, applied.Cell);
            WriteCounts(w, applied.Most);
        }
    });

    private Cell ReadCell(BinaryReader r)
    {
        long hour = r.ReadInt64();
        int model = r.ReadInt32();
        var variant = (TokenVariant)r.ReadByte();
        int session = r.ReadInt32();
        if (model < 0 || model >= models.Count) throw new FormatException("a sum of no model");
        if (session < -1 || session >= sessions.Count) throw new FormatException("a sum of no session");
        return new Cell(hour, model, variant, session);
    }

    private static void WriteCell(BinaryWriter w, Cell cell)
    {
        w.Write(cell.Hour);
        w.Write(cell.Model);
        w.Write((byte)cell.Variant);
        w.Write(cell.Session);
    }

    [Flags]
    private enum Has : byte { None = 0, Parent = 1, Title = 2, Folder = 4, Bloom = 8, FolderOwn = 16 }

    private static TokenSessionInfo ReadSession(BinaryReader r)
    {
        string provider = string.Intern(r.ReadString()), id = r.ReadString();
        var has = (Has)r.ReadByte();
        var kind = (TokenSessionInfo.Named)r.ReadByte();
        string? parent = has.HasFlag(Has.Parent) ? r.ReadString() : null;
        string? title = has.HasFlag(Has.Title) ? r.ReadString() : null;
        string? folder = has.HasFlag(Has.Folder) ? r.ReadString() : null;
        int seen = r.ReadInt32();
        if (kind > TokenSessionInfo.Named.Custom || (title is null) != (kind == TokenSessionInfo.Named.None)) throw new FormatException("a session's title");
        return new TokenSessionInfo(provider, id, parent, title, kind, folder, has.HasFlag(Has.Bloom), seen) { FolderOwn = folder is not null && has.HasFlag(Has.FolderOwn) };
    }

    private static void WriteSession(BinaryWriter w, TokenSessionInfo s)
    {
        w.Write(s.Provider);
        w.Write(s.Id);
        w.Write((byte)((s.Parent is null ? Has.None : Has.Parent) | (s.Title is null ? Has.None : Has.Title) | (s.Folder is null ? Has.None : Has.Folder) | (s.Bloom ? Has.Bloom : Has.None)
            | (s.FolderOwn ? Has.FolderOwn : Has.None)));
        w.Write((byte)s.Kind);
        if (s.Parent is not null) w.Write(s.Parent);
        if (s.Title is not null) w.Write(s.Title);
        if (s.Folder is not null) w.Write(s.Folder);
        w.Write(s.Seen);
    }

    private static TokenCounts ReadCounts(BinaryReader r) => new(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64());

    private static void WriteCounts(BinaryWriter w, TokenCounts c)
    {
        w.Write(c.Calls);
        w.Write(c.Input);
        w.Write(c.Output);
        w.Write(c.CacheRead);
        w.Write(c.CacheWrite);
        w.Write(c.CacheWriteLong);
    }
}
