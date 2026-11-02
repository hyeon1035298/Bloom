using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;

namespace Bloom.App;

/// Made-up token numbers for the recording mode and the QA pictures (Ai\TokenStats.SetForQa): hourly sums from a fixed
/// seed, busy in the day and quiet at night, summed the way the real views are, so every total matches its bars,
/// models, efforts and sessions. A session is a lane of work in a six-hour slot of a day, named from made-up titles and
/// folders; a few calls name no session. No log, path or name of this PC is in them.
internal static class TokenDemo
{
    /// A made-up model: its calls in a busy hour, each call's tokens, USD per 1M tokens (null: no price), the lanes its
    /// sessions run in (one picked each hour) and the efforts it is used at with their weights (none: Unknown).
    private sealed record Kind(string Provider, string Model, int Calls, TokenCounts PerCall, (double input, double write, double writeLong, double read, double output)? Price,
        int[] Lanes, (TokenEffort effort, double weight)[] Efforts);

    private static readonly Kind[] Kinds =
    [
        new("claude", "claude-opus-5-5", 46, new(1, 6, 640, 58_000, 300, 1_400), (5, 6.25, 10, 0.5, 25),
            [0, 1], [(TokenEffort.Max, 0.3), (TokenEffort.XHigh, 0.4), (TokenEffort.High, 0.28), (TokenEffort.Unknown, 0.02)]),
        new("claude", "claude-sonnet-5-5", 18, new(1, 5, 420, 31_000, 200, 900), (3, 3.75, 6, 0.1, 15),
            [2, 1], [(TokenEffort.High, 0.6), (TokenEffort.Medium, 0.4)]),
        new("codex", "gpt-6.1-sol", 14, new(1, 2_100, 820, 21_000, 0, 0), (1.25, 0, 0, 0.125, 10),
            [3], [(TokenEffort.Medium, 0.8), (TokenEffort.High, 0.2)]),
        new("codex", "codex-auto-review", 3, new(1, 4_800, 260, 5_200, 0, 0), null,
            [4], []),
    ];

    /// Made-up session names: some short, one long enough to be cut in a narrow block.
    private static readonly string[] Titles =
    [
        "Tidy the home blocks", "Fix the installer path check", "Speed up the search index", "Write the release notes",
        "Sort photos by the day they were taken", "Port the settings page", "Review the image cache", "Draft the weekly report",
        "Rework the download queue so retries keep their place on restart", "Clean up the test runner",
    ];

    private static readonly string[] Folders = ["launcher", "docs-site", "photo-tools", "web-shop", "notes", "scripts"];

    /// How long the made-up counting has gone on: past 60 days, so 전체 shows months.
    private const int CountedDays = 75;

    /// This hour's views, one per period; a new hour starts them over.
    private static readonly Dictionary<TokenPeriod, TokenView> Made = [];
    private static DateTime madeHour;

    /// The view of a period as of this hour (the same object within the hour, as TokenStats.Query gives).
    public static TokenView View(TokenPeriod period)
    {
        var hour = Hour(DateTime.Now);
        if (hour != madeHour)
        {
            Made.Clear();
            madeHour = hour;
        }
        if (Made.TryGetValue(period, out var made)) return made;
        made = Make(period, hour);
        Made[period] = made;
        return made;
    }

    private static DateTime Hour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);

    /// One session as it is summed.
    private sealed class Sum(string provider, string id, string? title, string? folder, bool bloom)
    {
        public readonly string Provider = provider, Id = id;
        public readonly string? Title = title, Folder = folder;
        public readonly bool Bloom = bloom;
        public TokenCounts Counts;
        public double Cost;
        public bool Priced;
        public long Unpriced;
        public DateTime First = DateTime.MaxValue, Last = DateTime.MinValue;
        public readonly Dictionary<string, long> Models = [];
        public readonly Dictionary<TokenEffort, long> Efforts = [];
    }

    private static TokenView Make(TokenPeriod period, DateTime hour)
    {
        var today = hour.Date;
        var (from, unit) = period switch
        {
            TokenPeriod.Day => (hour.AddHours(-23), TokenUnit.Hour),
            TokenPeriod.Week => (today.AddDays(-6), TokenUnit.Day),
            TokenPeriod.Month => (today.AddDays(-29), TokenUnit.Day),
            TokenPeriod.Quarter => (today.AddDays(-89), TokenUnit.Week),
            _ => (today.AddDays(-CountedDays + 1), CountedDays > 60 ? TokenUnit.Month : TokenUnit.Day),
        };
        var starts = new List<DateTime>();
        var start = unit switch
        {
            TokenUnit.Week => from.AddDays(-(((int)from.DayOfWeek + 6) % 7)),
            TokenUnit.Month => new DateTime(from.Year, from.Month, 1),
            _ => from,
        };
        for (var t = start; t <= hour; t = Next(t, unit)) starts.Add(t);
        var bars = new TokenCounts[starts.Count];
        var barCost = new double[starts.Count];
        var perModel = new TokenCounts[Kinds.Length];
        var modelEfforts = Kinds.Select(_ => new Dictionary<TokenEffort, long>()).ToArray();
        var sessions = new Dictionary<string, Sum>();
        TokenCounts noSession = default;
        for (var h = from; h <= hour; h = h.AddHours(1))
        {
            int bar = starts.FindLastIndex(s => s <= h);
            for (int k = 0; k < Kinds.Length; k++)
            {
                var kind = Kinds[k];
                var c = Counts(kind, h);
                if (c.Calls == 0) continue;
                double? cost = Cost(kind, c);
                bars[bar] += c;
                barCost[bar] += cost ?? 0;
                perModel[k] += c;
                var rng = new Random(Seed(h) * 7 + k * 101 + 13);
                var effort = Pick(kind.Efforts, rng);
                modelEfforts[k][effort] = modelEfforts[k].GetValueOrDefault(effort) + c.Total;
                if (rng.NextDouble() < 0.04)
                {
                    noSession += c;
                    continue;
                }
                var s = SessionOf(kind, h, kind.Lanes[rng.Next(kind.Lanes.Length)], sessions);
                s.Counts += c;
                if (cost is { } priced)
                {
                    s.Cost += priced;
                    s.Priced = true;
                }
                else s.Unpriced += c.Total;
                if (h < s.First) s.First = h;
                if (h > s.Last) s.Last = h;
                s.Models[kind.Model] = s.Models.GetValueOrDefault(kind.Model) + c.Total;
                s.Efforts[effort] = s.Efforts.GetValueOrDefault(effort) + c.Total;
            }
        }
        TokenCounts sum = default;
        foreach (var c in perModel) sum += c;
        double Share(long tokens) => sum.Total == 0 ? 0 : (double)tokens / sum.Total;
        var models = Kinds.Select((k, i) => new TokenModel(k.Provider, k.Model, perModel[i], Cost(k, perModel[i]), Share(perModel[i].Total)) { Efforts = Ordered(modelEfforts[i]) })
            .Where(m => m.Counts.Calls > 0).OrderByDescending(m => m.Counts.Total).ToList();
        var unpriced = models.Where(m => m.Cost is null).ToList();
        var made = sessions.Values
            .Select(s => new TokenSession(s.Provider, s.Id, s.Title, s.Folder, s.Bloom, s.Counts, s.Priced ? s.Cost : null, s.Unpriced, Share(s.Counts.Total), s.First, s.Last,
                s.Models.OrderByDescending(m => m.Value).Select(m => (m.Key, m.Value)).ToList(), Ordered(s.Efforts)))
            .OrderByDescending(s => s.Counts.Total).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        return new TokenView(period, from, DateTime.Now, sum, models.Sum(m => m.Cost ?? 0), unpriced.Count, unpriced.Sum(m => m.Counts.Total),
            models, unit, starts.Select((s, i) => new TokenBar(s, bars[i], barCost[i])).ToList())
        { Sessions = made, NoSession = noSession };
    }

    /// The session of a lane in the six-hour slot `hour` falls in, made the first time it is met: an id, and a title, a
    /// folder or both from the slot's own seed; a Bloom chat now and then in lane 1 (no title: the chat names it).
    private static Sum SessionOf(Kind kind, DateTime hour, int lane, Dictionary<string, Sum> sessions)
    {
        var slot = hour.Date.AddHours(hour.Hour / 6 * 6);
        string key = $"{slot:yyyyMMddHH}-{lane}";
        if (sessions.TryGetValue(key, out var s)) return s;
        var rng = new Random(Seed(slot) * 11 + lane);
        var id = new byte[16];
        rng.NextBytes(id);
        bool bloom = lane == 1 && rng.NextDouble() < 0.35;
        double titled = kind.Provider == "codex" ? 0.3 : 0.8;
        string? title = bloom || rng.NextDouble() >= titled ? null : Titles[rng.Next(Titles.Length)];
        string? folder = bloom ? "ai" : rng.NextDouble() < 0.9 ? Folders[rng.Next(Folders.Length)] : null;
        s = new Sum(kind.Provider, new Guid(id).ToString(), title, folder, bloom);
        sessions[key] = s;
        return s;
    }

    private static TokenEffort Pick((TokenEffort effort, double weight)[] efforts, Random rng)
    {
        double at = rng.NextDouble() * efforts.Sum(e => e.weight);
        foreach (var (effort, weight) in efforts)
        {
            if (at < weight) return effort;
            at -= weight;
        }
        return TokenEffort.Unknown;
    }

    /// The highest effort first, Unknown last, none of 0 tokens.
    private static List<(TokenEffort, long)> Ordered(Dictionary<TokenEffort, long> efforts) =>
        efforts.Where(e => e.Value > 0).OrderByDescending(e => e.Key == TokenEffort.Unknown ? -1 : (int)e.Key).Select(e => (e.Key, e.Value)).ToList();

    private static int Seed(DateTime hour) => ((hour.Year * 400) + hour.DayOfYear) * 24 + hour.Hour;

    private static DateTime Next(DateTime t, TokenUnit unit) => unit switch
    {
        TokenUnit.Hour => t.AddHours(1),
        TokenUnit.Day => t.AddDays(1),
        TokenUnit.Week => t.AddDays(7),
        _ => t.AddMonths(1),
    };

    /// One model's calls in one hour: busy from 9 to 1 at night, a calmer weekend, some hours idle; the same each time asked.
    private static TokenCounts Counts(Kind k, DateTime hour)
    {
        var rng = new Random(Seed(hour) * 31 + k.Calls); // not HashCode: it differs each run
        double busy = hour.Hour switch { >= 9 and <= 23 => 1, 0 or 1 => 0.6, 2 => 0.2, _ => 0.03 };
        if (hour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) busy *= 0.55;
        if (rng.NextDouble() < 0.18) return default;
        int calls = (int)Math.Round(k.Calls * busy * (0.3 + rng.NextDouble() * 1.2));
        if (calls == 0) return default;
        var p = k.PerCall;
        double size = 0.7 + rng.NextDouble() * 0.6;
        long Of(long each) => (long)Math.Round(each * calls * size);
        return new TokenCounts(calls, Of(p.Input), Of(p.Output), Of(p.CacheRead), Of(p.CacheWrite), Of(p.CacheWriteLong));
    }

    private static double? Cost(Kind k, TokenCounts c) => k.Price is { } p
        ? (c.Input * p.input + c.CacheWrite * p.write + c.CacheWriteLong * p.writeLong + c.CacheRead * p.read + c.Output * p.output) / 1e6
        : null;
}
