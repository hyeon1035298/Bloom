using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Core;

namespace Bloom.Ai;

/// What the token count keeps between passes, in one file (%LOCALAPPDATA%\Bloom\tokens\index.bin) so that the sums, the
/// files' offsets and the keys counted are always of the same moment: hourly sums per model and variant (TokenBucket),
/// each log file's TokenFile, the keys counted (seen) with their hour, and the recent keys' counted values (open). No
/// message text, folder or project name is kept: only the logs' paths on this PC, model names, key hashes and numbers.
///
/// A call is counted once however often its key comes (Add): the lines of one response in the file being read add only
/// what they hold above the most counted so far, field by field, to the hour first counted; so do the keys of the last
/// OpenHours (a response cut off between two passes); any other key counted before is a resumed or forked copy and adds
/// nothing. A copy whose values differ from the first one met (one side all zeros) keeps the first one's: accepted.
internal sealed class TokenIndex
{
    /// The file's layout; another one is not read (the count starts again).
    public const int Version = 1;

    /// Hourly sums older than this become one sum per UTC day (the 90-day blocks stay hourly).
    public const int HourDays = 100;

    /// Keys older than this are forgotten (a copy keeps the original's time, so they are kept well past the 30 days
    /// Claude Code keeps its logs).
    public const int SeenDays = 120;

    /// A key this recent may still be growing in a file being written (open).
    public const int OpenHours = 6;

    private static ReadOnlySpan<byte> Magic => "BTKI"u8;

    private readonly List<(string Provider, string Model)> models = [];
    private readonly Dictionary<(string Provider, string Model), int> modelIds = new();
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

    /// One sum: its hour (UTC ticks), model (index into models) and variant.
    private readonly record struct Cell(long Hour, int Model, TokenVariant Variant);

    /// What a key has added so far, field by field, and the sum it went to.
    private readonly record struct Applied(Cell Cell, TokenCounts Most);

    /// Counts a call → whether the sums changed (see the class). EndFile follows each file's lines.
    public bool Add(TokenCall call)
    {
        if (inFile.TryGetValue(call.Key, out var applied) || open.TryGetValue(call.Key, out applied))
        {
            var most = Most(applied.Most, call.Counts);
            if (most == applied.Most) return false;
            Put(applied.Cell, Less(most, applied.Most));
            applied = applied with { Most = most };
            inFile[call.Key] = applied;
            if (open.ContainsKey(call.Key)) open[call.Key] = applied;
            return true;
        }
        if (seen.ContainsKey(call.Key)) return false;
        var counts = call.Counts with { Calls = 1 };
        var cell = new Cell(Floor(call.Time).Ticks, ModelId(call.Provider, call.Model), call.Variant);
        Put(cell, counts);
        seen[call.Key] = Hours(call.Time);
        applied = new Applied(cell, counts);
        inFile[call.Key] = applied;
        if (call.Time >= Now.AddHours(-OpenHours)) open[call.Key] = applied;
        return true;
    }

    /// The file whose lines were being added is done: its keys are now only seen (or open).
    public void EndFile() => inFile.Clear();

    /// Tidies by Now: hourly sums older than HourDays fold into UTC days, keys older than SeenDays are forgotten, keys
    /// older than OpenHours are no longer open.
    public void Roll()
    {
        long fold = Now.AddDays(-HourDays).Ticks;
        foreach (var (cell, counts) in cells.Where(c => c.Key.Hour < fold && c.Key.Hour % TimeSpan.TicksPerDay != 0).ToList())
        {
            cells.Remove(cell);
            Put(cell with { Hour = cell.Hour - cell.Hour % TimeSpan.TicksPerDay }, counts);
        }
        int old = Hours(Now.AddDays(-SeenDays));
        foreach (var key in seen.Where(s => s.Value < old).Select(s => s.Key).ToList()) seen.Remove(key);
        long recent = Floor(Now.AddHours(-OpenHours)).Ticks;
        foreach (var key in open.Where(o => o.Value.Cell.Hour < recent).Select(o => o.Key).ToList()) open.Remove(key);
    }

    /// The sums, for the blocks (a copy: the index may go on changing on the counting thread).
    public TokenBucket[] Snapshot() =>
        cells.Select(c => new TokenBucket(new DateTime(c.Key.Hour, DateTimeKind.Utc), models[c.Key.Model].Provider, models[c.Key.Model].Model, c.Key.Variant, c.Value)).ToArray();

    private void Put(Cell cell, TokenCounts counts) => cells[cell] = cells.TryGetValue(cell, out var had) ? had + counts : counts;

    private int ModelId(string provider, string model)
    {
        if (modelIds.TryGetValue((provider, model), out int id)) return id;
        models.Add((provider, model));
        return modelIds[(provider, model)] = models.Count - 1;
    }

    private static DateTime Floor(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);

    private static int Hours(DateTime utc) => (int)(utc.Ticks / TimeSpan.TicksPerHour);

    /// Field by field the larger (Calls as in `a`).
    private static TokenCounts Most(TokenCounts a, TokenCounts b) =>
        new(a.Calls, Math.Max(a.Input, b.Input), Math.Max(a.Output, b.Output), Math.Max(a.CacheRead, b.CacheRead), Math.Max(a.CacheWrite, b.CacheWrite), Math.Max(a.CacheWriteLong, b.CacheWriteLong));

    /// `a` − `b`, field by field, no call.
    private static TokenCounts Less(TokenCounts a, TokenCounts b) =>
        new(0, a.Input - b.Input, a.Output - b.Output, a.CacheRead - b.CacheRead, a.CacheWrite - b.CacheWrite, a.CacheWriteLong - b.CacheWriteLong);

    /// The index kept at `path`; an empty one when there is none, it is of another Version, or it can't be read (logged).
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
                    f.Records = r.ReadBoolean();
                    f.LastTotal = r.ReadUInt64();
                }
                index.Files[id] = f;
            }
            int keys = r.ReadInt32();
            index.seen.EnsureCapacity(keys);
            for (int i = 0; i < keys; i++) index.seen[r.ReadUInt64()] = r.ReadInt32();
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                ulong key = r.ReadUInt64();
                index.open[key] = new Applied(index.ReadCell(r), ReadCounts(r));
            }
            return index;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException)
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
            w.Write(f.Records);
            w.Write(f.LastTotal);
        }
        w.Write(seen.Count);
        foreach (var (key, hour) in seen.OrderBy(s => s.Key))
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
        if (model < 0 || model >= models.Count) throw new FormatException("a sum of no model");
        return new Cell(hour, model, variant);
    }

    private static void WriteCell(BinaryWriter w, Cell cell)
    {
        w.Write(cell.Hour);
        w.Write(cell.Model);
        w.Write((byte)cell.Variant);
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
