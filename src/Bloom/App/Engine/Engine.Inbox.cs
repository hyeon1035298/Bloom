using System;
using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 알림 센터: the lines NoticeLog keeps, newest first; Enter opens the AI chat a line came from, the preview shows its
/// whole start. Built only while the view is open.
internal sealed partial class Engine
{
    public List<Section> InboxView(string text)
    {
        var items = NoticeLog.Current.Items;
        var q = new Query(text);
        bool Hit(NoticeEntry e) => q.IsEmpty || Matcher.Score(q, new MatchTarget(e.Title, new List<string> { e.Head, e.Detail ?? "" })) > 0;
        var sections = new List<Section>();
        // approvals still waiting, above everything: they need the user now
        var waiting = new Section(Tr("승인 대기", "Waiting for approval"));
        foreach (var e in NoticeLog.Waiting)
            if (Hit(e)) waiting.Items.Add(FromWaiting(e));
        if (waiting.Items.Count > 0) sections.Add(waiting);
        var list = new Section(waiting.Items.Count > 0 ? Tr("기록", "History") : null);
        foreach (var e in items)
            if (Hit(e)) list.Items.Add(FromNotice(e));
        if (list.Items.Count == 0 && waiting.Items.Count == 0)
            list.Items.Add(new ResultItem
            {
                Id = "inbox:none",
                Kind = ItemKind.Info,
                Title = items.Count == 0 ? Tr("알림 없음", "No notifications") : Tr("찾는 알림 없음", "No matching notifications"),
                Subtitle = items.Count == 0 ? Tr("작업 완료 · 한도 · 승인 대기 · 사용자 명령 결과가 여기에 쌓입니다", "Finished tasks, limits, approvals and command results collect here") : null,
                Icon = IconSpec.Of(G.Bell),
            });
        if (list.Items.Count > 0) sections.Add(list);
        return sections;
    }

    /// A 승인 대기 line: Enter goes to the session that asks (where Bloom knows its window).
    private static ResultItem FromWaiting(NoticeEntry e) => new()
    {
        Id = "inbox:wait:" + e.Key,
        Kind = e.Open is null ? ItemKind.Info : ItemKind.Command,
        Title = e.Title,
        Subtitle = e.Detail is { Length: > 0 } d && !e.Head.EndsWith(d, StringComparison.Ordinal) ? $"{e.Head} · {d}" : e.Head, // "… · 승인 대기" once
        Accessory = NoticeWhen(e.At),
        Icon = IconSpec.Of(G.Clock, true),
        PrimaryLabel = Tr("세션으로 이동", "Go to session"),
        Run = e.Open,
    };

    private static string NoticeWhen(long at) => When(DateTimeOffset.FromUnixTimeMilliseconds(at).LocalDateTime);

    /// "14:05" today, "11월 6일 14:05" on another day.
    private static string When(DateTime t) => t.Date == DateTime.Today ? t.ToString("HH:mm") : Tr($"{t.Month}월 {t.Day}일 {t:HH:mm}", $"{t.Month}/{t.Day} {t:HH:mm}");

    private ResultItem FromNotice(NoticeEntry e)
    {
        string? thread = e.Thread;
        var item = new ResultItem
        {
            Id = "inbox:" + e.Id,
            Kind = ItemKind.Command,
            Title = e.Title,
            Subtitle = e.Detail is { Length: > 0 } d ? $"{e.Head} · {d}" : e.Head,
            Accessory = NoticeWhen(e.At),
            Icon = IconSpec.Of(e.Ok ? G.Done : G.Warning, !e.Ok),
            PrimaryLabel = thread is not null ? Tr("채팅 열기", "Open chat") : e.Open is not null ? Tr("열기", "Open") : Tr("복사", "Copy"),
            Run = thread is not null ? () => host.OpenThread(thread) : e.Open ?? (() => Launch.Copy(e.Detail ?? e.Title)),
            Detail = () => new Detail { Text = e.Detail ?? "" }.Add(Tr("결과", "Result"), e.Head).Add(Tr("시각", "Time"), NoticeWhen(e.At)),
        };
        item.MoreActions = () => new List<ActionItem>
        {
            new() { Title = Tr("미리보기 복사", "Copy the preview"), Glyph = G.Copy, Run = () => Launch.Copy(e.Detail ?? e.Title) },
            new() { Title = Tr("이 알림 지우기", "Delete this notification"), Glyph = G.Delete, Danger = true, StayOpen = true, Run = () => NoticeLog.Remove(e.Id) },
            new()
            {
                Title = Tr("모두 지우기", "Delete all"),
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () => host.Window.Confirm(Tr($"알림 {NoticeLog.Current.Items.Count}개를 모두 지웁니다.", $"Deletes all {NoticeLog.Current.Items.Count} notifications."), new ActionItem
                {
                    Title = Tr("모두 지우기", "Delete all"),
                    Keys = "Enter",
                    Glyph = G.Delete,
                    Danger = true,
                    StayOpen = true,
                    Run = NoticeLog.Clear,
                }),
            },
        };
        return item;
    }
}
