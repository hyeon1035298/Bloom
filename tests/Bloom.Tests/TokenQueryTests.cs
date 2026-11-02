using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Tests;

/// A period's numbers from the index's sums (Ai\TokenQuery): the bars of each period and their bounds in local time
/// (midnight, Monday, a day of 25 hours), sums that add up across bars, models and sessions, the efforts, the models
/// with no price, and TokenStats.Query handing out the same view until the numbers or the hour change.
internal static class TokenQueryTests
{
    private static readonly TimeZoneInfo Seoul = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    private static TokenBucket At(DateTime utc, string model, long output, string provider = "claude", TokenVariant variant = TokenVariant.None) =>
        new(new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc), provider, model, variant, new TokenCounts(1, 10, output, 100, 0, 0));

    private static TokenView Make(IReadOnlyList<TokenBucket> sums, TokenPeriod period, DateTime now, TimeZoneInfo zone) => TokenQuery.Make(sums, TokenPrices.Of, period, now, zone);

    public static void EachPeriodHasItsBars()
    {
        var now = new DateTime(2026, 11, 3, 5, 40, 0, DateTimeKind.Utc); // Tuesday 14:40 in Seoul
        var sums = new List<TokenBucket> { At(now, "claude-opus-5-5", 1) };
        var day = Make(sums, TokenPeriod.Day, now, Seoul);
        Check.Eq((day.Unit, day.Bars.Count), (TokenUnit.Hour, 24), "24 hours");
        Check.Eq(day.Bars[^1].Start, new DateTime(2026, 11, 3, 14, 0, 0), "the last one runs now");
        Check.Eq(day.From, new DateTime(2026, 11, 2, 15, 0, 0), "from 23 hours before it");
        Check.Eq(day.To, new DateTime(2026, 11, 3, 14, 40, 0), "to now");

        var week = Make(sums, TokenPeriod.Week, now, Seoul);
        Check.Eq((week.Unit, week.Bars.Count), (TokenUnit.Day, 7), "7 days");
        Check.Eq(week.Bars[0].Start, new DateTime(2026, 10, 28), "from midnight 6 days before today");
        Check.Eq(week.Bars[^1].Start, new DateTime(2026, 11, 3), "today last");
        var month = Make(sums, TokenPeriod.Month, now, Seoul);
        Check.Eq((month.Unit, month.Bars.Count, month.Bars[0].Start), (TokenUnit.Day, 30, new DateTime(2026, 10, 5)), "30 days");

        var quarter = Make(sums, TokenPeriod.Quarter, now, Seoul);
        Check.Eq(quarter.Unit, TokenUnit.Week, "weeks");
        Check.Eq(quarter.From, new DateTime(2026, 8, 6), "90 days counting today");
        Check.Eq(quarter.Bars[0].Start, new DateTime(2026, 8, 3), "the Monday of the first day's week");
        Check.True(quarter.Bars.All(b => b.Start.DayOfWeek == DayOfWeek.Monday), "every bar from a Monday");
        Check.Eq(quarter.Bars.Count, 14, "13 to 14 of them");
        Check.Eq(quarter.Bars[^1].Start, new DateTime(2026, 11, 2), "this week last");

        var all = Make([At(now.AddDays(-20), "claude-opus-5-5", 1), .. sums], TokenPeriod.All, now, Seoul);
        Check.Eq((all.Unit, all.Bars.Count, all.From), (TokenUnit.Day, 21, new DateTime(2026, 10, 14)), "All by day from the first day counted");
        var longer = Make([At(now.AddDays(-75), "claude-opus-5-5", 1), .. sums], TokenPeriod.All, now, Seoul);
        Check.Eq((longer.Unit, longer.Bars.Count, longer.Bars[0].Start), (TokenUnit.Month, 4, new DateTime(2026, 8, 1)), "by month past 60 days");
        Check.Eq(longer.Sum.Calls, 2L, "both counted");
        var none = Make([], TokenPeriod.All, now, Seoul);
        Check.Eq((none.Sum.Calls, none.Bars.Count), (0L, 1), "nothing counted: today alone, empty");
        Check.Eq(Make([], TokenPeriod.Week, now, Seoul).Bars.Count, 7, "every bar, zeros too");
    }

    public static void BarsFollowLocalDays()
    {
        // 2026-11-01 is 25 hours long in Los Angeles (summer time ends at 2:00)
        var now = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc); // 12:00 on the 3rd
        var sums = new List<TokenBucket>
        {
            At(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), "claude-opus-5-5", 1), // 23:30 on the 31st (summer time)
            At(new DateTime(2026, 11, 1, 7, 10, 0, DateTimeKind.Utc), "claude-opus-5-5", 2), // 0:10 on the 1st
            At(new DateTime(2026, 11, 2, 7, 30, 0, DateTimeKind.Utc), "claude-opus-5-5", 4), // 23:30 on the 1st (standard time)
            At(new DateTime(2026, 11, 2, 8, 10, 0, DateTimeKind.Utc), "claude-opus-5-5", 8), // 0:10 on the 2nd
        };
        var week = Make(sums, TokenPeriod.Week, now, Pacific);
        var outputs = week.Bars.ToDictionary(b => b.Start.Day, b => b.Counts.Output);
        Check.Eq((outputs[31], outputs[1], outputs[2]), (1L, 6L, 8L), "each sum in its local day");
        Check.True(week.Bars.All(b => b.Start.TimeOfDay == TimeSpan.Zero), "every bar from midnight");
        var day = Make(sums, TokenPeriod.Day, new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc), Pacific); // 12:00 on the 1st
        Check.Eq(day.Bars.Count, 24, "24 hours across the change");
        Check.Eq(day.Bars.Count(b => b.Start.Hour == 1), 2, "1:00 twice");
        Check.Eq(day.Sum.Output, 3L, "the last 24 hours");
    }

    public static void SumsAddUp()
    {
        var now = new DateTime(2026, 11, 3, 5, 40, 0, DateTimeKind.Utc);
        var sums = new List<TokenBucket>
        {
            At(now, "claude-opus-5-5", 1000),
            At(now.AddHours(-2), "claude-opus-5-5-20261101", 500, variant: TokenVariant.Fast),
            At(now.AddHours(-3), "claude-sonnet-5-5", 300),
            At(now.AddHours(-1), "gpt-6.1-sol", 200, "codex"),
            At(now.AddHours(-1), "codex-auto-review", 50, "codex"),
            At(now.AddHours(-4), "claude-opus-5-7", 70),
            At(now.AddDays(-3), "claude-opus-5-5", 9999),
        };
        var view = Make(sums, TokenPeriod.Day, now, Seoul);
        Check.Eq(view.Sum.Calls, 6L, "the calls of the last 24 hours");
        Check.Eq(view.Sum, view.Bars.Aggregate(default(TokenCounts), (a, b) => a + b.Counts), "the bars add up to the sum");
        Check.Eq(view.Sum, view.Models.Aggregate(default(TokenCounts), (a, m) => a + m.Counts), "so do the models");
        Check.Near(view.Models.Sum(m => m.Share), 1, 1e-9, "the shares add up to 1");
        Check.Near(view.Bars.Sum(b => b.Cost), view.Cost, 1e-9, "the bars' cost too");
        Check.Near(view.Models.Sum(m => m.Cost ?? 0), view.Cost, 1e-9, "the models' cost too");
        var opus = view.Models.Single(m => m.Model == "claude-opus-5-5");
        Check.Eq(opus.Counts.Calls, 2L, "a date tail and fast mode join the model's line");
        var opusPrice = TokenPrices.Of("claude-opus-5-5")!;
        Check.Near(opus.Cost!.Value, opusPrice.Cost(sums[0].Counts, TokenVariant.None) + opusPrice.Cost(sums[1].Counts, TokenVariant.Fast), 1e-12, "each at its own price");
        Check.Eq(view.Models[0].Model, "claude-opus-5-5", "most tokens first");
        Check.Eq(view.Unpriced, 2, "codex-auto-review and a model with no row");
        Check.Eq(view.UnpricedTokens, 50 + 110 + 70 + 110L, "their tokens");
        Check.True(view.Models.Single(m => m.Model == "codex-auto-review").Cost is null, "no cost, not $0");
        Check.Eq(view.Models.Single(m => m.Model == "gpt-6.1-sol").Provider, "codex", "the provider kept");
    }

    public static void SessionsAndEffortsAddUp()
    {
        var now = new DateTime(2026, 11, 3, 5, 40, 0, DateTimeKind.Utc); // 14:40 in Seoul
        static TokenSessionInfo S(string provider, string id, string? title = null, bool bloom = false) =>
            new(provider, id, null, title, title is null ? TokenSessionInfo.Named.None : TokenSessionInfo.Named.Custom, "Repo", bloom, 0);
        var (a, b, c, d, e) = (S("claude", "s-a", "Home blocks"), S("codex", "s-b"), S("claude", "s-c"), S("claude", "s-d", bloom: true), S("claude", "s-e"));
        static TokenVariant E(TokenEffort effort) => TokenVariant.None.WithEffort(effort);
        var sums = new List<TokenBucket>
        {
            At(now, "claude-opus-5-5", 1000, variant: E(TokenEffort.High)) with { Session = a },
            At(now.AddHours(-3), "claude-opus-5-5-20261101", 500, variant: TokenVariant.Fast.WithEffort(TokenEffort.Max)) with { Session = a },
            At(now.AddHours(-1), "claude-sonnet-5-5", 300, variant: E(TokenEffort.High)) with { Session = a },
            At(now.AddHours(-2), "gpt-6.1-sol", 200, "codex", E(TokenEffort.Medium)) with { Session = b },
            At(now.AddHours(-2), "codex-auto-review", 50, "codex", E(TokenEffort.Medium)) with { Session = b },
            At(now.AddHours(-4), "claude-opus-5-7", 70) with { Session = c },
            At(now.AddHours(-1), "claude-opus-5-5", 20) with { Session = e },
            At(now.AddHours(-1), "claude-opus-5-5", 20) with { Session = d },
            At(now, "claude-opus-5-5", 9), // no session
            At(now.AddDays(-3), "claude-opus-5-5", 9999) with { Session = a }, // before the period
        };
        var view = Make(sums, TokenPeriod.Day, now, Seoul);
        Check.Eq(view.Sessions.Aggregate(view.NoSession, (s, x) => s + x.Counts), view.Sum, "the sessions and what no session holds add up to the sum");
        Check.Eq(view.NoSession.Calls, 1L, "a call with no session");
        Check.Eq(string.Join(" ", view.Sessions.Select(s => s.Id)), "s-a s-b s-c s-d s-e", "most tokens first, the same tokens by id");
        Check.Near(view.Sessions.Sum(s => s.Share) + (double)view.NoSession.Total / view.Sum.Total, 1, 1e-9, "the shares add up to 1");
        foreach (var s in view.Sessions) Check.Eq(s.Efforts.Sum(x => x.Tokens), s.Counts.Total, $"{s.Id}'s efforts add up to its tokens");
        foreach (var m in view.Models) Check.Eq(m.Efforts.Sum(x => x.Tokens), m.Counts.Total, $"{m.Model}'s efforts add up to its tokens");
        var sa = view.Sessions[0];
        Check.Eq(string.Join(" ", sa.Efforts.Select(x => $"{x.Effort}:{x.Tokens}")), "Max:610 High:1520", "the highest effort first");
        Check.Eq(string.Join(" ", view.Models.Single(m => m.Model == "claude-opus-5-5").Efforts.Select(x => $"{x.Effort}:{x.Tokens}")), "Max:610 High:1110 Unknown:379", "Unknown last");
        Check.Eq(string.Join(" ", sa.Models.Select(x => $"{x.Model}:{x.Tokens}")), "claude-opus-5-5:1720 claude-sonnet-5-5:410", "its models, date tail off, most tokens first");
        Check.Eq((sa.First, sa.Last), (new DateTime(2026, 11, 3, 11, 0, 0), new DateTime(2026, 11, 3, 14, 0, 0)), "its first and last hour, local");
        Check.True(sa is { Provider: "claude", Title: "Home blocks", Folder: "Repo", Bloom: false, UnpricedTokens: 0 } && sa.Cost is > 0, "its name and folder, priced");
        var opusPrice = TokenPrices.Of("claude-opus-5-5")!;
        Check.Near(sa.Cost!.Value, opusPrice.Cost(sums[0].Counts, sums[0].Variant) + opusPrice.Cost(sums[1].Counts, TokenVariant.Fast) + TokenPrices.Of("claude-sonnet-5-5")!.Cost(sums[2].Counts, TokenVariant.None), 1e-12, "each call at its own price");
        Check.True(view.Sessions[1] is { Provider: "codex", UnpricedTokens: 160 } && view.Sessions[1].Cost is > 0, "a model with no price beside one with: its tokens told apart");
        Check.True(view.Sessions[2] is { Cost: null, UnpricedTokens: 180 }, "no model with a price: no cost, not $0");
        Check.True(view.Sessions[3] is { Bloom: true, Title: null }, "Bloom's own");
        Check.True(TokenQuery.Make([], TokenPrices.Of, TokenPeriod.Day, now, Seoul) is { Sessions.Count: 0, NoSession.Calls: 0 }, "nothing counted: no sessions");
    }

    public static void QueryHandsOutTheSameView()
    {
        try
        {
            TokenStats.Use([At(DateTime.UtcNow, "claude-opus-5-5", 5)]);
            var a = TokenStats.Query(TokenPeriod.Week);
            Check.True(ReferenceEquals(a, TokenStats.Query(TokenPeriod.Week)), "the same object while nothing changed");
            Check.Eq(a.Sum.Calls, 1L, "made from the sums in memory");
            Check.True(!ReferenceEquals(a, TokenStats.Query(TokenPeriod.Month)), "another period, another view");
            TokenStats.Use([At(DateTime.UtcNow, "claude-opus-5-5", 5), At(DateTime.UtcNow, "claude-sonnet-5-5", 5)]);
            var b = TokenStats.Query(TokenPeriod.Week);
            Check.True(!ReferenceEquals(a, b) && b.Sum.Calls == 2, "new numbers, a new view");
        }
        finally
        {
            TokenStats.Use([]);
        }
    }
}
