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

    /// The whole text in one line, cut to a preview.
    public static string Preview(string text)
    {
        string one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length > PreviewLength ? one[..PreviewLength] + "…" : one;
    }

    public static NoticeEntry Add(bool ok, string head, string title, string? detail, string? thread, long? at = null)
    {
        var e = new NoticeEntry
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            At = at ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Ok = ok,
            Head = head,
            Title = title,
            Detail = detail is null ? null : Preview(detail),
            Thread = thread,
        };
        var items = Current.Items;
        items.Insert(0, e);
        if (items.Count > Keep) items.RemoveRange(Keep, items.Count - Keep);
        Touch();
        return e;
    }

    public static void Remove(string id)
    {
        if (Current.Items.RemoveAll(x => x.Id == id) > 0) Touch();
    }

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
