using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The calendar: Bloom's reminders and timers as a month (Ui\MonthView) with the picked day's list next to it.
/// A search lists every matching one by day instead and dims the rest in the month.
internal sealed partial class Engine
{
    private static bool CalHit(Query q, TimerEntry e) => q.IsEmpty || Matcher.Score(q, new MatchTarget(e.Label)) > 0;

    /// The month's marks: per day, one chip per reminder (its words; the time is in the list); a search dims the rest.
    public static Dictionary<DateTime, List<Ui.DayMark>> CalendarMarks(string text)
    {
        var q = new Query(text);
        var marks = new Dictionary<DateTime, List<Ui.DayMark>>();
        foreach (var e in Timers.Waiting)
        {
            var day = e.DueAt.Date;
            if (!marks.TryGetValue(day, out var list)) marks[day] = list = new();
            list.Add(new Ui.DayMark(e.Label, e.Kind == "reminder", CalHit(q, e)));
        }
        foreach (var list in marks.Values) list.Sort((x, y) => y.Hit.CompareTo(x.Hit)); // matches stay visible in the cell
        return marks;
    }

    public static List<Section> CalendarView(string text, DateTime pick)
    {
        var sections = new List<Section>();
        var q = new Query(text);
        if (!q.IsEmpty)
        {
            // a search: every matching reminder, by day
            var hits = Timers.Waiting.Where(e => CalHit(q, e)).ToList();
            foreach (var g in hits.GroupBy(e => e.DueAt.Date))
            {
                var s = new Section(DayTitle(g.Key));
                foreach (var e in g) s.Items.Add(FromTimer(e));
                sections.Add(s);
            }
            if (hits.Count == 0) sections.Add(Info("cal:none", Tr("검색된 알림 없음", "No matching reminders"), null));
            return sections;
        }

        var lines = Timers.Waiting.Where(e => e.DueAt.Date == pick.Date).ToList();
        var day1 = new Section(DayTitle(pick.Date), lines.Count > 0 ? Tr($"알림 {lines.Count}개", $"{lines.Count} reminders") : null);
        foreach (var e in lines) day1.Items.Add(FromTimer(e));
        if (lines.Count == 0)
            day1.Items.Add(new ResultItem
            {
                Id = "cal:empty",
                Kind = ItemKind.Info,
                Title = Tr("이 날짜의 알림 없음", "No reminders for this date"),
                Subtitle = Tr("검색창에 \"내일 9시 업로드 알림\"처럼 입력하세요 · ←→ 날짜 · PgUp/PgDn 달", "Type like \"Remind me to upload tomorrow at 9 AM\" in the search bar · ←→ Date · PgUp/PgDn Month"),
                Icon = IconSpec.Of(G.Calendar),
            });
        sections.Add(day1);
        return sections;
    }

    private static Section Info(string id, string title, string? sub)
    {
        var s = new Section(null);
        s.Items.Add(new ResultItem { Id = id, Kind = ItemKind.Info, Title = title, Subtitle = sub, Icon = IconSpec.Of(G.Calendar) });
        return s;
    }

    private static readonly string[] Weekdays = { Tr("일요일", "Sunday"), Tr("월요일", "Monday"), Tr("화요일", "Tuesday"), Tr("수요일", "Wednesday"), Tr("목요일", "Thursday"), Tr("금요일", "Friday"), Tr("토요일", "Saturday") };

    private static string DayTitle(DateTime d)
    {
        int n = (int)(d - DateTime.Today).TotalDays;
        string date = d.ToString(Tr("M월 d일 dddd", "dddd, MMM d"), Lang.Culture);
        return n switch
        {
            0 => Tr($"오늘 · {date}", $"Today · {date}"),
            1 => Tr($"내일 · {date}", $"Tomorrow · {date}"),
            2 => Tr($"모레 · {date}", $"In 2 days · {date}"),
            < 0 => Tr($"{date} · {-n}일 전", $"{date} · {-n}d ago"),
            _ => Tr($"{date} · {n}일 뒤", $"{date} · in {n}d"),
        };
    }
}
