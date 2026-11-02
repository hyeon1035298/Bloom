using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;

namespace Bloom.App;

/// Made-up token numbers for the recording mode and the QA pictures (Ai\TokenStats.SetForQa): hourly sums from a fixed
/// seed, busy in the day and quiet at night, summed the way the real views are, so every total matches its bars and
/// models. No log, path or name of this PC is in them.
internal static class TokenDemo
{
    /// A made-up model: its calls in a busy hour, each call's tokens, and USD per 1M tokens (null: no price).
    private sealed record Kind(string Provider, string Model, int Calls, TokenCounts PerCall, (double input, double write, double writeLong, double read, double output)? Price);

    private static readonly Kind[] Kinds =
    [
        new("claude", "claude-opus-5-5", 46, new(1, 6, 640, 58_000, 300, 1_400), (5, 6.25, 10, 0.5, 25)),
        new("claude", "claude-sonnet-5-5", 18, new(1, 5, 420, 31_000, 200, 900), (3, 3.75, 6, 0.1, 15)),
        new("codex", "gpt-6.1-sol", 14, new(1, 2_100, 820, 21_000, 0, 0), (1.25, 0, 0, 0.125, 10)),
        new("codex", "codex-auto-review", 3, new(1, 4_800, 260, 5_200, 0, 0), null),
    ];

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
        for (var h = from; h <= hour; h = h.AddHours(1))
        {
            int bar = starts.FindLastIndex(s => s <= h);
            for (int k = 0; k < Kinds.Length; k++)
            {
                var c = Counts(Kinds[k], h);
                if (c.Calls == 0) continue;
                bars[bar] += c;
                barCost[bar] += Cost(Kinds[k], c) ?? 0;
                perModel[k] += c;
            }
        }
        TokenCounts sum = default;
        foreach (var c in perModel) sum += c;
        var models = Kinds.Select((k, i) => new TokenModel(k.Provider, k.Model, perModel[i], Cost(k, perModel[i]), sum.Total == 0 ? 0 : (double)perModel[i].Total / sum.Total))
            .Where(m => m.Counts.Calls > 0).OrderByDescending(m => m.Counts.Total).ToList();
        var unpriced = models.Where(m => m.Cost is null).ToList();
        return new TokenView(period, from, DateTime.Now, sum, models.Sum(m => m.Cost ?? 0), unpriced.Count, unpriced.Sum(m => m.Counts.Total),
            models, unit, starts.Select((s, i) => new TokenBar(s, bars[i], barCost[i])).ToList());
    }

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
        var rng = new Random((((hour.Year * 400) + hour.DayOfYear) * 24 + hour.Hour) * 31 + k.Calls); // not HashCode: it differs each run
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
