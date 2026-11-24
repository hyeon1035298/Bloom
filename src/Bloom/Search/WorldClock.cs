using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Search;

/// Other cities' clocks, answered on the calculator card: "뉴욕 시간" / "tokyo time" → the time there now;
/// "뉴욕 오후 3시" / "london 9am" → that time there, here; "오후 3시 뉴욕" / "3pm in tokyo" → this time here, there.
/// Windows' own time-zone data (daylight saving included); "here" is this PC's zone, never named.
internal static class WorldClock
{
    /// city words (Korean, English, short forms) → Windows time-zone id
    private static readonly (string zone, string[] names)[] Zones =
    {
        ("Korea Standard Time", new[] { "서울", "한국", "seoul", "korea", "kst" }),
        ("Tokyo Standard Time", new[] { "도쿄", "동경", "일본", "오사카", "tokyo", "japan", "osaka", "jst" }),
        ("China Standard Time", new[] { "베이징", "북경", "상하이", "중국", "홍콩", "beijing", "shanghai", "china", "hong kong", "hongkong" }),
        ("Taipei Standard Time", new[] { "타이베이", "대만", "taipei", "taiwan" }),
        ("Singapore Standard Time", new[] { "싱가포르", "싱가폴", "쿠알라룸푸르", "마닐라", "필리핀", "singapore", "kuala lumpur", "manila", "philippines" }),
        ("SE Asia Standard Time", new[] { "방콕", "태국", "하노이", "호치민", "베트남", "자카르타", "bangkok", "thailand", "hanoi", "ho chi minh", "vietnam", "jakarta" }),
        ("India Standard Time", new[] { "인도", "뉴델리", "델리", "뭄바이", "india", "new delhi", "delhi", "mumbai" }),
        ("Arabian Standard Time", new[] { "두바이", "아부다비", "dubai", "abu dhabi", "uae" }),
        ("Russian Standard Time", new[] { "모스크바", "러시아", "moscow" }),
        ("Turkey Standard Time", new[] { "이스탄불", "터키", "튀르키예", "istanbul", "turkey" }),
        ("GMT Standard Time", new[] { "런던", "영국", "더블린", "리스본", "london", "uk", "dublin", "lisbon" }),
        ("Romance Standard Time", new[] { "파리", "프랑스", "마드리드", "스페인", "브뤼셀", "paris", "france", "madrid", "spain", "brussels" }),
        ("W. Europe Standard Time", new[] { "베를린", "독일", "로마", "이탈리아", "암스테르담", "네덜란드", "빈", "취리히", "스위스", "berlin", "germany", "rome", "italy", "amsterdam", "vienna", "zurich", "cet" }),
        ("Eastern Standard Time", new[] { "뉴욕", "워싱턴", "보스턴", "토론토", "마이애미", "new york", "nyc", "washington", "boston", "toronto", "miami", "est", "edt" }),
        ("Central Standard Time", new[] { "시카고", "댈러스", "휴스턴", "chicago", "dallas", "houston", "cst", "cdt" }),
        ("Mountain Standard Time", new[] { "덴버", "denver", "mst", "mdt" }),
        ("Pacific Standard Time", new[] { "la", "엘에이", "로스앤젤레스", "샌프란시스코", "실리콘밸리", "시애틀", "밴쿠버", "라스베이거스", "los angeles", "san francisco", "silicon valley", "seattle", "vancouver", "las vegas", "pst", "pdt" }),
        ("Hawaiian Standard Time", new[] { "하와이", "호놀룰루", "hawaii", "honolulu" }),
        ("AUS Eastern Standard Time", new[] { "시드니", "멜버른", "호주", "sydney", "melbourne", "australia", "aest" }),
        ("New Zealand Standard Time", new[] { "오클랜드", "뉴질랜드", "auckland", "new zealand" }),
        ("E. South America Standard Time", new[] { "상파울루", "브라질", "sao paulo", "são paulo", "brazil" }),
        ("UTC", new[] { "utc", "gmt" }),
    };

    private const string Clock = @"(?:(?<ap>오전|오후|am|pm)\s*)?(?<h>\d{1,2})(?:(?::(?<mi>\d{2}))|\s*시(?:\s*(?<mi>\d{1,2})\s*분|\s*(?<half>반))?)?\s*(?<ap2>am|pm)?";

    private static Regex? now, there, here;
    private static Dictionary<string, string>? byName;

    private static void Build()
    {
        if (byName is not null) return;
        byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (zone, names) in Zones)
            foreach (string n in names) byName[n] = zone;
        string city = "(?<city>" + string.Join("|", byName.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")";
        const RegexOptions o = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        now = new Regex(@"^\s*(?:(?:지금|현재|time\s+in|what\s+time\s+is\s+it\s+in|what\s+time\s+in)\s+" + city + @"\s*(?:은|는|의)?\s*(?:시간|시각|몇\s*시)?|" + city + @"\s*(?:은|는|의)?\s*(?:지금|현재)?\s*(?:시간|시각|몇\s*시|time|now|clock))\s*\??\s*$", o);
        there = new Regex(@"^\s*" + city + @"\s*(?:시간|time)?\s*(?:으로|로|기준)?\s*" + Clock + @"\s*(?:은|는|이면|에)?\s*\??\s*$", o);
        here = new Regex(@"^\s*" + Clock + @"\s*(?:은|는|이면|에|in|→|->|to)?\s*" + city + @"\s*(?:시간|time)?\s*(?:으로|로|에서는|에선|은|는|기준)?\s*\??\s*$", o);
    }

    public static CalcAnswer? Evaluate(string input, DateTime? at = null)
    {
        string t = input.Trim();
        if (t.Length is < 2 or > 48) return null;
        Build();
        try
        {
            return Answer(t, at);
        }
        catch (ArgumentException)
        {
            return null; // a time skipped by a daylight-saving change
        }
    }

    private static CalcAnswer? Answer(string t, DateTime? at)
    {
        var local = TimeZoneInfo.Local;
        DateTime nowUtc = (at ?? DateTime.Now).ToUniversalTime();
        Match m;
        if ((m = now!.Match(t)).Success && Zone(m) is { } z0)
        {
            var there0 = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, z0);
            var here0 = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, local);
            return new CalcAnswer { Caption = Tr("세계 시간", "World Clock"), Expr = Tr($"{City(m)} 지금", $"{City(m)} now"), Value = Time(there0), Copy = Time(there0), Detail = Day(there0, here0) + " · " + Gap(z0, nowUtc) };
        }
        if ((m = there!.Match(t)).Success && Zone(m) is { } z1 && ClockOf(m) is { } c1)
        {
            // that time today in the city, as the time here
            var cityNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, z1);
            var cityAt = cityNow.Date + c1;
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cityAt, DateTimeKind.Unspecified), z1);
            var hereAt = TimeZoneInfo.ConvertTimeFromUtc(utc, local);
            return new CalcAnswer { Caption = Tr("세계 시간", "World Clock"), Expr = $"{City(m)} {Time(cityAt)}", Value = Tr($"여기 {Rel(hereAt, cityAt)}{Time(hereAt)}", $"Here {Rel(hereAt, cityAt)}{Time(hereAt)}"), Copy = Time(hereAt), Detail = Gap(z1, utc) };
        }
        if ((m = here!.Match(t)).Success && Zone(m) is { } z2 && ClockOf(m) is { } c2)
        {
            var hereNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, local);
            var hereAt = hereNow.Date + c2;
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(hereAt, DateTimeKind.Unspecified), local);
            var cityAt = TimeZoneInfo.ConvertTimeFromUtc(utc, z2);
            return new CalcAnswer { Caption = Tr("세계 시간", "World Clock"), Expr = Tr($"여기 {Time(hereAt)}", $"Here {Time(hereAt)}"), Value = $"{City(m)} {Rel(cityAt, hereAt)}{Time(cityAt)}", Copy = Time(cityAt), Detail = Gap(z2, utc) };
        }
        return null;
    }

    private static TimeZoneInfo? Zone(Match m)
    {
        if (!byName!.TryGetValue(m.Groups["city"].Value, out string? id)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static string City(Match m)
    {
        string c = m.Groups["city"].Value;
        return c.Length <= 3 ? c.ToUpperInvariant() : c;
    }

    /// The clock time a match names; it needs a colon, 시 or am/pm ("3" alone is no time).
    private static TimeSpan? ClockOf(Match m)
    {
        string ap = (m.Groups["ap"].Value + m.Groups["ap2"].Value).ToLowerInvariant();
        bool timeLike = m.Value.Contains(':') || m.Value.Contains('시') || ap.Length > 0;
        if (!timeLike || !int.TryParse(m.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int h)) return null;
        int min = m.Groups["half"].Success ? 30 : int.TryParse(m.Groups["mi"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int mi) ? mi : 0;
        if (ap is "오후" or "pm" && h < 12) h += 12;
        if (ap is "오전" or "am" && h == 12) h = 0;
        return h is >= 0 and < 24 && min is >= 0 and < 60 ? new TimeSpan(h, min, 0) : null;
    }

    private static string Time(DateTime t) => t.ToString(Tr("tt h:mm", "h:mm tt"), Lang.Culture);

    /// "내일 " / "어제 " when the other side's date differs.
    private static string Rel(DateTime t, DateTime from) => (t.Date - from.Date).Days switch
    {
        0 => "",
        1 => Tr("다음 날 ", "Next day "),
        -1 => Tr("전날 ", "Previous day "),
        _ => t.ToString(Tr("M월 d일 ", "MMM d "), Lang.Culture),
    };

    private static string Day(DateTime there, DateTime here) => there.ToString(Tr("M월 d일 (ddd)", "MMM d (ddd)"), Lang.Culture) + ((there.Date - here.Date).Days switch
    {
        1 => Tr(" · 여기보다 하루 앞", " · 1 day ahead"),
        -1 => Tr(" · 여기보다 하루 뒤", " · 1 day behind"),
        _ => "",
    });

    /// How far the zone is from here at that moment ("여기보다 13시간 느려요").
    private static string Gap(TimeZoneInfo zone, DateTime utc)
    {
        var diff = zone.GetUtcOffset(utc) - TimeZoneInfo.Local.GetUtcOffset(utc);
        if (diff == TimeSpan.Zero) return Tr("여기와 같은 시간", "Same time as here");
        string span = diff.Minutes == 0 ? Tr($"{Math.Abs(diff.Hours)}시간", $"{Math.Abs(diff.Hours)}h") : Tr($"{Math.Abs(diff.Hours)}시간 {Math.Abs(diff.Minutes)}분", $"{Math.Abs(diff.Hours)} hr {Math.Abs(diff.Minutes)} min");
        return diff > TimeSpan.Zero ? Tr($"현재 위치보다 {span} 빠름", $"{span} ahead of current location") : Tr($"현재 위치보다 {span} 느림", $"{span} behind current location");
    }
}
