using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Search;

/// Day counting in the search line, answered on the calculator card:
/// "100일 뒤" / "3주 후 무슨 요일" / "in 3 weeks" → the date and its weekday; "크리스마스까지" / "12월 25일까지 며칠" /
/// "days until 2026-12-25" → D-77; "2026-03-01부터" / "days since 3/1" → +222일 (223일째);
/// "2026-01-01 ~ 2026-12-25" → 358일; "2026-12-25" / "10월 9일 무슨 요일" → its weekday and D-day.
/// A bare "12/25" stays maths (12 ÷ 25); slashes need a word around them.
internal static partial class DateCalc
{
    private static string Caption => Tr("날짜 계산", "Date Calculation");

    // a date: 2026-12-25 · 2026.12.25 · 2026/12/25 · 12/25 · 2026년 12월 25일 · 12월 25일 · Dec 25 (2026) · 25 Dec
    private const string DatePattern =
        @"(?:(?<y>\d{4})\s*[-./]\s*(?<m>\d{1,2})\s*[-./]\s*(?<d>\d{1,2})\.?" +
        @"|(?:(?<y>\d{4})\s*년\s*)?(?<m>\d{1,2})\s*월\s*(?<d>\d{1,2})\s*일" +
        @"|(?<m>\d{1,2})\s*/\s*(?<d>\d{1,2})" +
        @"|(?<mn>[A-Za-z]{3,9})\.?\s+(?<d>\d{1,2})(?:st|nd|rd|th)?(?:,?\s*(?<y>\d{4}))?" +
        @"|(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?<mn>[A-Za-z]{3,9})\.?(?:,?\s*(?<y>\d{4}))?" +
        @"|(?<word>오늘|today|내일|tomorrow|모레|어제|yesterday|크리스마스|성탄절|christmas|새해|신정|new\s*year'?s?(?:\s*day)?|발렌타인\s*데이|밸런타인\s*데이|valentine'?s?(?:\s*day)?|화이트\s*데이|white\s*day|할로윈|halloween|빼빼로\s*데이|어린이날|삼일절|현충일|제헌절|광복절|개천절|한글날))";

    [GeneratedRegex(@"^\s*(?:오늘\s*(?:부터|에서)?\s*)?(?<n>\d{1,5})\s*(?<u>일|주|주일|개월|달|년|days?|weeks?|months?|years?)\s*(?<dir>뒤|후|전|이후|later|from\s+now|from\s+today|ago|before)\s*(?:은|는|이|가)?\s*(?:무슨|몇)?\s*(?:요일|날|며칠)?\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetKo();

    [GeneratedRegex(@"^\s*in\s+(?<n>\d{1,5})\s*(?<u>days?|weeks?|months?|years?)\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetIn();

    [GeneratedRegex(@"^\s*(?:d\s*-?\s*day\s+|days?\s+(?:until|till|to|left\s+until)\s+|how\s+many\s+days\s+(?:until|till|to)\s+)(?<a>" + DatePattern + @")\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UntilEn();

    [GeneratedRegex(@"^\s*(?<a>" + DatePattern + @")\s*(?:까지|디데이|d\s*-?\s*day)\s*(?:며칠|몇\s*일|남은\s*날|얼마나|남았|d\s*-?\s*day)?\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UntilKo();

    [GeneratedRegex(@"^\s*(?:days?\s+since|since)\s+(?<a>" + DatePattern + @")\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SinceEn();

    [GeneratedRegex(@"^\s*(?<a>" + DatePattern + @")\s*(?:부터|이후로?|에서)\s*(?:오늘까지|지금까지)?\s*(?:며칠|몇\s*일|얼마나)?\s*(?:지났|됐|째)?\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SinceKo();

    [GeneratedRegex(@"^\s*(?:between\s+)?(?<a>" + DatePattern + @")\s*(?:~|부터|-\s|–|to|and|→|->)\s*(?<b>" + DatePattern + @")\s*(?:까지)?\s*(?:며칠|몇\s*일)?\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Between();

    [GeneratedRegex(@"^\s*(?:what\s+day\s+is\s+)?(?<a>" + DatePattern + @")\s*(?:은|는|이|가)?\s*(?:무슨|몇)?\s*(?:요일|weekday|day\s+of\s+(?:the\s+)?week)?\s*\??\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Weekday();

    public static CalcAnswer? Evaluate(string input, DateTime? now = null)
    {
        string t = input.Trim();
        if (t.Length is < 2 or > 60) return null;
        DateTime today = (now ?? DateTime.Now).Date;
        var m = OffsetKo().Match(t);
        if (!m.Success) m = OffsetIn().Match(t);
        if (m.Success) return Offset(m, today, t);
        if ((m = UntilEn().Match(t)).Success || (m = UntilKo().Match(t)).Success)
            return ReadDate(m, "a", today, upcoming: true) is { } until ? Until(until, today, t) : null;
        if ((m = SinceEn().Match(t)).Success || (m = SinceKo().Match(t)).Success)
            return ReadDate(m, "a", today, upcoming: false) is { } since ? Since(since, today, t) : null;
        if ((m = Between().Match(t)).Success && ReadDate(m, "a", today, false) is { } a && ReadDate(m, "b", today, false) is { } b)
        {
            int days = (int)(b - a).TotalDays;
            return new CalcAnswer { Caption = Caption, Expr = $"{Long(a)} → {Long(b)}", Value = Tr($"{Math.Abs(days):#,0}일", $"{Math.Abs(days):#,0} days"), Copy = Math.Abs(days).ToString(CultureInfo.InvariantCulture), Detail = Tr($"첫날 포함 {Math.Abs(days) + 1:#,0}일 · {Weeks(Math.Abs(days))}", $"{Math.Abs(days) + 1:#,0} days inclusive · {Weeks(Math.Abs(days))}") };
        }
        // a date alone: only when it can't be anything else (a slash date or a bare number is maths)
        if ((m = Weekday().Match(t)).Success && (t.Contains('요') || t.Contains("day", StringComparison.OrdinalIgnoreCase) || !t.Contains('/')) && !IsOnlyWord(m))
            return ReadDate(m, "a", today, upcoming: true) is { } d ? Day(d, today) : null;
        return null;
    }

    private static bool IsOnlyWord(Match m) => m.Groups["word"].Success && m.Groups["word"].Value.Length == m.Groups["a"].Value.Length && !m.Value.Contains('요');

    private static CalcAnswer? Offset(Match m, DateTime today, string expr)
    {
        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
        string u = m.Groups["u"].Value.ToLowerInvariant(), dir = m.Groups["dir"].Value.ToLowerInvariant();
        bool back = dir is "전" or "ago" or "before";
        if (back) n = -n;
        DateTime d;
        try
        {
            d = u[0] switch
            {
                '일' or 'd' => today.AddDays(n),
                '주' or 'w' => today.AddDays(n * 7),
                '개' or '달' or 'm' => today.AddMonths(n),
                _ => today.AddYears(n),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        int days = (int)(d - today).TotalDays;
        return new CalcAnswer
        {
            Caption = Caption,
            Expr = expr,
            Value = Long(d),
            Copy = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Detail = back ? Tr($"오늘부터 {-days:#,0}일 전", $"{-days:#,0} days ago from today") : Tr($"오늘부터 {days:#,0}일 뒤 · D-{days}", $"{days:#,0} days from today · D-{days}"),
        };
    }

    private static CalcAnswer Until(DateTime d, DateTime today, string expr)
    {
        int days = (int)(d - today).TotalDays;
        string value = days == 0 ? "D-Day" : days > 0 ? $"D-{days:#,0}" : $"D+{-days:#,0}";
        return new CalcAnswer
        {
            Caption = Caption,
            Expr = expr,
            Value = value,
            Copy = Math.Abs(days).ToString(CultureInfo.InvariantCulture),
            Detail = Long(d) + " · " + (days == 0 ? Tr("오늘", "Today") : days > 0 ? Tr($"{days:#,0}일 남음 · {Weeks(days)}", $"{days:#,0} days left · {Weeks(days)}") : Tr($"{-days:#,0}일 경과", $"{-days:#,0} days elapsed")),
        };
    }

    private static CalcAnswer Since(DateTime d, DateTime today, string expr)
    {
        int days = (int)(today - d).TotalDays;
        if (days < 0) return Until(d, today, expr);
        return new CalcAnswer
        {
            Caption = Caption,
            Expr = expr,
            Value = Tr($"{days:#,0}일 경과", $"{days:#,0} days elapsed"),
            Copy = days.ToString(CultureInfo.InvariantCulture),
            Detail = Tr($"오늘이 {days + 1:#,0}일째 · {Long(d)}부터", $"Day {days + 1:#,0} today · Since {Long(d)}"),
        };
    }

    private static CalcAnswer Day(DateTime d, DateTime today)
    {
        int days = (int)(d - today).TotalDays;
        return new CalcAnswer
        {
            Caption = Caption,
            Expr = d.ToString(Tr("yyyy. M. d.", "MMM d, yyyy"), Lang.Culture),
            Value = d.ToString("dddd", Lang.Culture),
            Copy = d.ToString("dddd", Lang.Culture),
            Detail = days == 0 ? Tr("오늘", "Today") : days > 0 ? $"D-{days:#,0} · {Long(d)}" : $"D+{-days:#,0} · {Long(d)}",
        };
    }

    /// The date a match names; a date with no year is this year's, or next year's once it has passed (upcoming).
    private static DateTime? ReadDate(Match m, string group, DateTime today, bool upcoming)
    {
        var whole = m.Groups[group];
        if (!whole.Success) return null;
        // the named groups of this date: the ones inside its span
        string Part(string name)
        {
            foreach (Capture c in m.Groups[name].Captures)
                if (c.Index >= whole.Index && c.Index + c.Length <= whole.Index + whole.Length) return c.Value;
            return "";
        }
        string word = string.Concat(Part("word").Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        int y = 0, mo, d;
        switch (word)
        {
            case "오늘" or "today": return today;
            case "내일" or "tomorrow": return today.AddDays(1);
            case "모레": return today.AddDays(2);
            case "어제" or "yesterday": return today.AddDays(-1);
            case "": break;
            default:
                (mo, d) = Holiday(word);
                if (mo == 0) return null;
                return Next(today, mo, d, upcoming);
        }
        string mn = Part("mn");
        if (mn.Length > 0)
        {
            mo = MonthName(mn);
            if (mo == 0) return null;
        }
        else if (!int.TryParse(Part("m"), NumberStyles.None, CultureInfo.InvariantCulture, out mo)) return null;
        if (!int.TryParse(Part("d"), NumberStyles.None, CultureInfo.InvariantCulture, out d)) return null;
        string ys = Part("y");
        if (ys.Length > 0 && !int.TryParse(ys, NumberStyles.None, CultureInfo.InvariantCulture, out y)) return null;
        if (mo is < 1 or > 12 || d < 1) return null;
        if (y == 0) return d > DateTime.DaysInMonth(today.Year, mo) ? null : Next(today, mo, d, upcoming);
        if (y is < 1 or > 9999 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new DateTime(y, mo, d);
    }

    /// This year's day, or next year's when it has passed and the question looks ahead.
    private static DateTime Next(DateTime today, int month, int day, bool upcoming)
    {
        var d = new DateTime(today.Year, month, Math.Min(day, DateTime.DaysInMonth(today.Year, month)));
        return upcoming && d < today ? new DateTime(today.Year + 1, month, Math.Min(day, DateTime.DaysInMonth(today.Year + 1, month))) : d;
    }

    private static (int month, int day) Holiday(string word) => word switch
    {
        "크리스마스" or "성탄절" or "christmas" => (12, 25),
        "새해" or "신정" or "newyear" or "newyears" or "newyear's" or "newyearsday" or "newyear'sday" or "newyearday" => (1, 1),
        "발렌타인데이" or "밸런타인데이" or "valentine" or "valentines" or "valentine's" or "valentinesday" or "valentine'sday" or "valentineday" => (2, 14),
        "화이트데이" or "whiteday" => (3, 14),
        "할로윈" or "halloween" => (10, 31),
        "빼빼로데이" => (11, 11),
        "어린이날" => (5, 5),
        "삼일절" => (3, 1),
        "현충일" => (6, 6),
        "제헌절" => (7, 17),
        "광복절" => (8, 15),
        "개천절" => (10, 3),
        "한글날" => (10, 9),
        _ => (0, 0),
    };

    private static int MonthName(string name)
    {
        string n = name.ToLowerInvariant();
        string[] months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        int i = Array.FindIndex(months, x => n.StartsWith(x, StringComparison.Ordinal));
        // the whole word must be the month (or its short form): "march" yes, "marching" no
        return i >= 0 && CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(i + 1).StartsWith(n, StringComparison.OrdinalIgnoreCase) ? i + 1 : n == "sept" ? 9 : 0;
    }

    private static string Long(DateTime d) => d.ToString(Tr("yyyy년 M월 d일 (ddd)", "MMM d, yyyy (ddd)"), Lang.Culture);

    private static string Weeks(int days) => days < 14
        ? Tr($"{days}일", $"{days} days")
        : days % 7 == 0 ? Tr($"{days / 7}주", $"{days / 7}w") : Tr($"{days / 7}주 {days % 7}일", $"{days / 7}w {days % 7}d");
}
