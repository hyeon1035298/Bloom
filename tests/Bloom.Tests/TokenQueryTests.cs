using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Tests;

/// A period's numbers from the index's sums (Ai\TokenQuery): the bars of each period and their bounds in local time
/// (midnight, Monday, a day of 25 hours), sums that add up across bars and models, the models with no price, and
/// TokenStats.Query handing out the same view until the numbers or the hour change.
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
