using System;
using System.Collections.Generic;
using System.Linq;

namespace Bloom.Ai;

/// A period's numbers made from the index's sums (TokenStats.Query; nothing else is read). The bars' bounds are in
/// local time: Day → the 24 hours up to now (the last one the hour now runs in), Week / Month → the last 7 / 30 days
/// counting today, Quarter → the last 90 days in weeks from Monday (the first week only from the 90 days' start), All →
/// from the first day counted, by day up to 60 days and by month after. The sums, the bars and the models' lines all
/// cover the same span, so they add up to each other.
internal static class TokenQuery
{
    /// The All period goes by day up to this many days, by month after.
    public const int AllDays = 60;

    /// `buckets` summed for `period` as it stands at `now` (UTC) in `zone`; `price` gives a model's price or null.
    public static TokenView Make(IReadOnlyList<TokenBucket> buckets, Func<string, TokenPrice?> price, TokenPeriod period, DateTime now, TimeZoneInfo zone)
    {
        now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        var today = local.Date;
        TokenUnit unit;
        DateTime from; // UTC: what counts starts here
        var starts = new List<DateTime>(); // the bars' starts, UTC
        switch (period)
        {
            case TokenPeriod.Day:
                unit = TokenUnit.Hour;
                var hour = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
                for (int i = 23; i >= 0; i--) starts.Add(hour.AddHours(-i));
                from = starts[0];
                break;
            case TokenPeriod.Week or TokenPeriod.Month:
                unit = TokenUnit.Day;
                int days = period == TokenPeriod.Week ? 7 : 30;
                for (int i = days - 1; i >= 0; i--) starts.Add(ToUtc(today.AddDays(-i), zone));
                from = starts[0];
                break;
            case TokenPeriod.Quarter:
                unit = TokenUnit.Week;
                var first = today.AddDays(-89);
                from = ToUtc(first, zone);
                for (var d = first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); d <= today; d = d.AddDays(7)) starts.Add(ToUtc(d, zone));
                break;
            default:
                var start = buckets.Count == 0 ? today : TimeZoneInfo.ConvertTimeFromUtc(buckets.Min(b => b.Hour), zone).Date;
                if (start > today) start = today;
                if ((today - start).Days + 1 <= AllDays)
                {
                    unit = TokenUnit.Day;
                    for (var d = start; d <= today; d = d.AddDays(1)) starts.Add(ToUtc(d, zone));
                }
                else
                {
                    unit = TokenUnit.Month;
                    for (var d = new DateTime(start.Year, start.Month, 1); d <= today; d = d.AddMonths(1)) starts.Add(ToUtc(d, zone));
                }
                from = ToUtc(start, zone);
                break;
        }
        // UTC: what counts ends here, with the last bar (a time ahead of the clock is left out)
        var end = period == TokenPeriod.Day ? starts[^1].AddHours(1) : ToUtc(today.AddDays(1), zone);

        var bars = new TokenCounts[starts.Count];
        var barCost = new double[starts.Count];
        var prices = new Dictionary<string, TokenPrice?>(StringComparer.Ordinal);
        var models = new Dictionary<(string Provider, string Model), (TokenCounts Counts, double Cost, bool Priced)>();
        TokenCounts sum = default;
        double cost = 0;
        foreach (var b in buckets)
        {
            if (b.Hour < from || b.Hour >= end) continue;
            int i = starts.BinarySearch(b.Hour);
            if (i < 0) i = ~i - 1;
            if (i < 0) i = 0; // the first week starts before `from`; nothing before `from` gets here
            if (!prices.TryGetValue(b.Model, out var p)) prices[b.Model] = p = price(b.Model);
            double c = p?.Cost(b.Counts, b.Variant) ?? 0;
            bars[i] += b.Counts;
            barCost[i] += c;
            sum += b.Counts;
            cost += c;
            var key = (b.Provider, TokenPrices.WithoutDate(b.Model));
            var had = models.TryGetValue(key, out var m) ? m : (Counts: default(TokenCounts), Cost: 0.0, Priced: true);
            models[key] = (had.Counts + b.Counts, had.Cost + c, had.Priced && p is not null);
        }

        int unpriced = 0;
        long unpricedTokens = 0;
        var lines = new List<TokenModel>(models.Count);
        foreach (var ((provider, model), m) in models)
        {
            if (!m.Priced)
            {
                unpriced++;
                unpricedTokens += m.Counts.Total;
            }
            lines.Add(new TokenModel(provider, model, m.Counts, m.Priced ? m.Cost : null, sum.Total == 0 ? 0 : (double)m.Counts.Total / sum.Total));
        }
        lines.Sort((a, b) => b.Counts.Total != a.Counts.Total ? b.Counts.Total.CompareTo(a.Counts.Total) : string.CompareOrdinal(a.Model, b.Model));
        var barList = new TokenBar[starts.Count];
        for (int i = 0; i < starts.Count; i++)
            barList[i] = new TokenBar(TimeZoneInfo.ConvertTimeFromUtc(starts[i], zone), bars[i], barCost[i]);
        return new TokenView(period, TimeZoneInfo.ConvertTimeFromUtc(from, zone), local, sum, cost, unpriced, unpricedTokens, lines, unit, barList);
    }

    /// The UTC moment of a local day's or month's start; a start that a clock change skips is taken an hour on.
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
