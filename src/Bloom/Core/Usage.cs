using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// How often and how recently each item was opened; drives the ranking boost and "최근".
internal sealed class Usage
{
    public sealed class Entry
    {
        public int Count { get; set; }
        public long Last { get; set; }
    }

    public Dictionary<string, Entry> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Usage Current { get; private set; } = new();

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<Usage>(Paths.In("usage.json"));
        saver = Saver.ForJson(Paths.In("usage.json"), () => Current, 1500);
    }

    public static void Touch(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!Current.Items.TryGetValue(id, out var e)) Current.Items[id] = e = new Entry();
        e.Count++;
        e.Last = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        saver?.Touch();
    }

    public static void Forget(string id)
    {
        if (Current.Items.Remove(id)) saver?.Touch();
    }

    /// 설정 > 데이터 > 최근 사용: every count and time goes, so the ranking starts over.
    public static void Clear()
    {
        Current.Items.Clear();
        saver?.Touch();
    }

    public static void Save() => saver?.Touch();

    /// 0…~70: frequency (log) plus a recency step.
    public static int Boost(string id)
    {
        if (!Current.Items.TryGetValue(id, out var e) || e.Count == 0) return 0;
        double ageDays = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - e.Last) / 86_400_000.0;
        double recency = ageDays < 1 ? 24 : ageDays < 7 ? 14 : ageDays < 30 ? 6 : 0;
        return (int)Math.Round(16 * Math.Log(1 + e.Count) + recency);
    }

    public static void Flush() => saver?.Flush();
}
