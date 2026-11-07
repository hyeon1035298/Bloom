using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Bloom.Search;

/// How often an AI command runs by itself: every N minutes (30 at least), hours or days, or every day at a time. Kept as
/// "30m", "2h", "3d" or "daily 09:00" (UserCommand.Every).
internal static partial class AiSchedule
{
    /// One AI CLI call takes about 400 MB for a moment: nothing runs more often than this.
    public static readonly TimeSpan Shortest = TimeSpan.FromMinutes(30);

    [GeneratedRegex(@"^(?:every\s+)?(\d+(?:\.\d+)?)\s*(m|min|mins|minutes?|분|h|hr|hrs|hours?|시간|d|days?|일)\s*(?:마다)?$", RegexOptions.IgnoreCase)]
    private static partial Regex EveryRx();

    [GeneratedRegex(@"^(?:매일|daily|every\s*day)?\s*(?:at\s*)?(\d{1,2})\s*[:시]\s*(\d{1,2})?\s*분?$", RegexOptions.IgnoreCase)]
    private static partial Regex DailyRx();

    /// What the user typed → the kept form, or an error; empty, "끄기" or "off" → (null, null) = no schedule.
    public static (string? every, string? error) Parse(string typed)
    {
        string t = typed.Trim();
        if (t.Length == 0 || t is "끄기" or "없음" || t.Equals("off", StringComparison.OrdinalIgnoreCase) || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return (null, null);
        if (EveryRx().Match(t) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n > 0)
        {
            char unit = char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'h' or '시' => 'h', 'd' or '일' => 'd', _ => 'm' };
            var span = unit switch { 'h' => TimeSpan.FromHours(n), 'd' => TimeSpan.FromDays(n), _ => TimeSpan.FromMinutes(n) };
            if (span < Shortest) return (null, Tr("가장 짧은 주기는 30분입니다", "The shortest interval is 30 minutes"));
            if (span > TimeSpan.FromDays(365)) return (null, Tr("주기는 365일까지 정할 수 있습니다", "The interval can be at most 365 days"));
            // whole minutes keep the kept form short: 1.5시간 → 90m
            return unit != 'm' && n == Math.Floor(n) ? ($"{n:0}{unit}", null) : ($"{(int)Math.Round(span.TotalMinutes)}m", null);
        }
        if (DailyRx().Match(t) is { Success: true } d)
        {
            int h = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture), min = d.Groups[2].Success ? int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (h is >= 0 and <= 23 && min is >= 0 and <= 59) return ($"daily {h:00}:{min:00}", null);
        }
        return (null, Tr("예) 30분, 2시간, 3일, 매일 09:00", "e.g. 30m, 2h, 3d, daily 09:00"));
    }

    /// The kept form as the user reads it: "2시간마다", "매일 09:00".
    public static string Name(string every)
    {
        if (Daily(every) is not null) return Tr($"매일 {every[6..]}", $"Daily at {every[6..]}");
        if (Span(every) is not { } span) return every;
        if (every.EndsWith('d')) return Tr($"{span.TotalDays:0}일마다", $"Every {span.TotalDays:0} d");
        if (every.EndsWith('h')) return Tr($"{span.TotalHours:0}시간마다", $"Every {span.TotalHours:0} h");
        return span.TotalMinutes % 60 == 0 ? Tr($"{span.TotalHours:0}시간마다", $"Every {span.TotalHours:0} h") : Tr($"{span.TotalMinutes:0}분마다", $"Every {span.TotalMinutes:0} min");
    }

    /// "daily 09:00" → 09:00; null for an interval.
    private static TimeSpan? Daily(string every) =>
        every.StartsWith("daily ", StringComparison.Ordinal) && TimeSpan.TryParseExact(every[6..], "hh\\:mm", CultureInfo.InvariantCulture, out var at) ? at : null;

    /// "2h" → 2 hours; null for a daily time or anything unreadable.
    private static TimeSpan? Span(string every)
    {
        if (every.Length < 2 || !int.TryParse(every[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n <= 0) return null;
        TimeSpan? span = every[^1] switch { 'm' => TimeSpan.FromMinutes(n), 'h' => TimeSpan.FromHours(n), 'd' => TimeSpan.FromDays(n), _ => null };
        return span is { } s && s < Shortest ? Shortest : span; // a hand-edited file still runs no more often than the shortest
    }

    /// When it runs next after `last` (local time); null when `every` can't be read.
    public static DateTime? Next(string every, DateTime last)
    {
        if (Daily(every) is { } at)
        {
            var today = last.Date + at;
            return today > last ? today : today.AddDays(1);
        }
        return Span(every) is { } span ? last + span : null;
    }

    /// Is a run due now? A run missed while Bloom was off is due once: after it, Next counts from that run.
    public static bool Due(string every, DateTime last, DateTime now) => Next(every, last) is { } next && next <= now;
}
