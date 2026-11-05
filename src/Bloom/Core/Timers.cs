using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// A running timer ("10분 타이머") or a reminder ("30분 뒤 빨래 확인", "15:30 회의 알림").
internal sealed class TimerEntry
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "timer"; // timer | reminder
    public string Label { get; set; } = "";
    public long Due { get; set; } // unix ms
    public long Started { get; set; } // unix ms (a timer's bar)

    [JsonIgnore] public DateTime DueAt => DateTimeOffset.FromUnixTimeMilliseconds(Due).LocalDateTime;
    [JsonIgnore] public TimeSpan Left => DueAt - DateTime.Now;
}

/// The timers and reminders that are waiting (timers.json; they survive a restart, and one that came due while
/// Bloom was off rings as soon as it starts).
internal sealed class Timers
{
    public List<TimerEntry> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Timers Current { get; private set; } = new();
    public static event Action? Changed;

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<Timers>(Paths.In("timers.json"));
        saver = Saver.ForJson(Paths.In("timers.json"), () => Current);
    }

    public static IEnumerable<TimerEntry> Waiting => Current.Items.OrderBy(t => t.Due);

    public static TimerEntry Add(string kind, string label, DateTime due)
    {
        var e = new TimerEntry
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Kind = kind,
            Label = label,
            Due = new DateTimeOffset(due).ToUnixTimeMilliseconds(),
            Started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        Current.Items.Add(e);
        Touch();
        return e;
    }

    public static void Remove(string id)
    {
        if (Current.Items.RemoveAll(t => t.Id == id) > 0) Touch();
    }

    /// 설정 > 데이터 > 타이머 · 알림: every waiting timer and reminder is cancelled (none rings) → how many went.
    public static int Clear()
    {
        int n = Current.Items.Count;
        if (n == 0) return 0;
        Current.Items.Clear();
        Touch();
        return n;
    }

    public static void Extend(string id, TimeSpan by)
    {
        if (Current.Items.Find(t => t.Id == id) is not { } e) return;
        e.Due += (long)by.TotalMilliseconds;
        Touch();
    }

    /// The ones whose time has come (taken out of the list).
    public static List<TimerEntry> TakeDue()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var due = Current.Items.Where(t => t.Due <= now).ToList();
        if (due.Count > 0)
        {
            Current.Items.RemoveAll(t => t.Due <= now);
            Touch();
        }
        return due;
    }

    private static void Touch()
    {
        saver?.Touch();
        Changed?.Invoke();
    }

    public static void Flush() => saver?.Flush();

    /// "9:41" / "1:02:05" / "3일 4시간" — what is left, for the lists.
    public static string LeftText(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return Tr($"{(int)t.TotalDays}일 {t.Hours}시간", $"{(int)t.TotalDays}d {t.Hours}h");
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// "10분", "1시간 30분", "90초"
    public static string DurationText(TimeSpan t)
    {
        var parts = new List<string>();
        if (t.Hours + t.Days * 24 > 0) parts.Add(Tr($"{t.Hours + t.Days * 24}시간", $"{t.Hours + t.Days * 24} h"));
        if (t.Minutes > 0) parts.Add(Tr($"{t.Minutes}분", $"{t.Minutes} min"));
        if (t.Seconds > 0 && t.TotalMinutes < 10) parts.Add(Tr($"{t.Seconds}초", $"{t.Seconds} s"));
        return parts.Count == 0 ? Tr("0초", "0 s") : string.Join(" ", parts);
    }

    /// "오늘 15:30" / "내일 09:00" / "10월 9일 18:00"
    public static string WhenText(DateTime d)
    {
        string day = d.Date == DateTime.Today ? Tr("오늘", "today") : d.Date == DateTime.Today.AddDays(1) ? Tr("내일", "tomorrow") : d.ToString(Tr("M월 d일", "MMM d"), Lang.Culture);
        return $"{day} {d:HH:mm}";
    }
}
