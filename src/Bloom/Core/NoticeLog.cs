using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// 알림 센터 (notices.json): what ended while the user may not have been looking, newest first, the last 200. Read from
/// disk only when something is added or the view is opened, so a start and the launcher's opening never touch it.
internal sealed class NoticeLog
{
    public const int Keep = 200;
    private const int PreviewLength = 300;

    public List<NoticeEntry> Items { get; set; } = new();

    [JsonIgnore] private static NoticeLog? current;
    [JsonIgnore] private static Saver? saver;

    public static event Action? Changed;

    /// Loaded on first use (on the UI thread).
    public static NoticeLog Current
    {
        get
        {
            if (current is not null) return current;
            current = Json.Load<NoticeLog>(Paths.In("notices.json"));
            saver = Saver.ForJson(Paths.In("notices.json"), () => current);
            return current;
        }
    }

    /// The whole text in one line as plain words (an AI answer's Markdown marks taken off), cut to a preview.
    public static string Preview(string text)
    {
        string one = string.Join(' ', MarkdownText.Plain(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length > PreviewLength ? one[..PreviewLength] + "…" : one;
    }

    /// Approvals waiting now (승인 대기), newest first: shown above the kept lines while they wait, never written to disk,
    /// gone once answered, refused or ended (Settle).
    public static readonly List<NoticeEntry> Waiting = new();

    private static NoticeEntry Entry(bool ok, string head, string title, string? detail, string? thread, long? at) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        At = at ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Ok = ok,
        Head = head,
        Title = title,
        Detail = detail is null ? null : Preview(detail),
        Thread = thread,
    };

    /// `open`: what Enter does in 알림 센터 while this Bloom runs (a turn's session window); not kept.
    public static NoticeEntry Add(bool ok, string head, string title, string? detail, string? thread, long? at = null, Action? open = null)
    {
        var e = Entry(ok, head, title, detail, thread, at);
        e.Open = open;
        var items = Current.Items;
        items.Insert(0, e);
        if (items.Count > Keep) items.RemoveRange(Keep, items.Count - Keep);
        Touch();
        return e;
    }

    /// An approval starts waiting under `key` (one line per key: a newer one replaces it).
    public static void Wait(string key, string head, string title, string? detail, Action? open)
    {
        Waiting.RemoveAll(x => x.Key == key);
        var e = Entry(false, head, title, detail, null, null);
        e.Key = key;
        e.Open = open;
        Waiting.Insert(0, e);
        Changed?.Invoke();
    }

    /// The approval under `key` was answered, refused or went away.
    public static void Settle(string key)
    {
        if (Waiting.RemoveAll(x => x.Key == key) > 0) Changed?.Invoke();
    }

    /// Watching stopped (설정 > 알림 off): nothing is known to wait any more.
    public static void SettleAll()
    {
        if (Waiting.Count == 0) return;
        Waiting.Clear();
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        if (Current.Items.RemoveAll(x => x.Id == id) > 0) Touch();
    }

    /// The kept lines; approvals still waiting stay.
    public static void Clear()
    {
        if (Current.Items.Count == 0) return;
        Current.Items.Clear();
        Touch();
    }

    private static void Touch()
    {
        saver?.Touch();
        Changed?.Invoke();
    }

    public static void Flush() => saver?.Flush();
}
