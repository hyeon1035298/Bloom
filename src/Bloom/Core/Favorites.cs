using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// Pinned items, in the user's order. Kept as plain records so a pinned file or URL survives even
/// when it is not in any index.
internal sealed class Favorites
{
    public sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = ""; // app | uwp | file | folder | url | cmd | setting | tool | quicklink | sys | layout | group
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string Target { get; set; } = "";
        public List<Entry>? Items { get; set; } // a group (즐겨찾기 폴더): what is in it, in order
        public string? Icon { get; set; } // a group's own icon: "glyph:E714", or a picture / .ico / program path
        public string? Mono { get; set; } // a picture icon: "pic:E774" / "pic:sparkle" in the accent colour on a black tile (older: letters)
    }

    public const string GroupKind = "group";

    /// Every entry, the ones inside groups too.
    public static IEnumerable<Entry> All()
    {
        foreach (var e in Current.Items)
        {
            yield return e;
            if (e.Items is { } inner)
                foreach (var i in inner) yield return i;
        }
    }

    public static Entry? Group(string id) => Current.Items.Find(e => e.Id == id && e.Kind == GroupKind);

    public static Entry AddGroup(string name)
    {
        var g = new Entry { Id = "group:" + Guid.NewGuid().ToString("N")[..10], Kind = GroupKind, Title = name, Items = new() };
        Current.Items.Add(g);
        Touch();
        return g;
    }

    /// null = back to the small icons of what is inside.
    public static void SetGroupIcon(string id, string? icon)
    {
        if (Group(id) is not { } g) return;
        g.Icon = icon;
        Touch();
    }

    public static void RenameGroup(string id, string name)
    {
        if (Group(id) is not { } g) return;
        g.Title = name;
        Touch();
    }

    /// keepItems: what was inside goes back to the row, where the group was.
    public static void DeleteGroup(string id, bool keepItems)
    {
        int at = Current.Items.FindIndex(e => e.Id == id && e.Kind == GroupKind);
        if (at < 0) return;
        var g = Current.Items[at];
        Current.Items.RemoveAt(at);
        if (keepItems && g.Items is { } inner)
            Current.Items.InsertRange(at, inner.Where(i => !Current.Items.Exists(x => x.Id == i.Id)));
        Touch();
    }

    /// Into a group (out of the row or another group first).
    public static void AddToGroup(string groupId, Entry e)
    {
        if (Group(groupId) is not { } g) return;
        Remove(e.Id, notify: false);
        (g.Items ??= new()).Add(e);
        Touch();
    }

    /// From a group back to the row, after the group.
    public static void TakeOut(string groupId, string id)
    {
        if (Group(groupId) is not { Items: { } inner } g) return;
        var e = inner.Find(x => x.Id == id);
        if (e is null) return;
        inner.Remove(e);
        Current.Items.Insert(Current.Items.IndexOf(g) + 1, e);
        Touch();
    }

    public static void MoveInGroup(string groupId, string id, int delta)
    {
        if (Group(groupId) is not { Items: { } inner }) return;
        int i = inner.FindIndex(e => e.Id == id), j = i + delta;
        if (i < 0 || j < 0 || j >= inner.Count) return;
        (inner[i], inner[j]) = (inner[j], inner[i]);
        Touch();
    }

    private static void Touch()
    {
        saver?.Touch();
        Changed?.Invoke();
    }

    public List<Entry> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Favorites Current { get; private set; } = new();
    [JsonIgnore] public static bool Existed { get; private set; }

    public static event Action? Changed;

    public static void Load()
    {
        saver?.Dispose();
        Existed = File.Exists(Paths.In("favorites.json"));
        Current = Json.Load<Favorites>(Paths.In("favorites.json"));
        saver = Saver.ForJson(Paths.In("favorites.json"), () => Current);
        // entries of a kind Bloom no longer has (a feature that was taken out) leave the list
        static bool Gone(Entry e) => e.Kind is not ("app" or "uwp" or "file" or "folder" or "url" or "cmd" or "setting" or "tool" or "quicklink" or "sys" or "layout" or GroupKind);
        int dropped = Current.Items.RemoveAll(Gone);
        foreach (var g in Current.Items) dropped += g.Items?.RemoveAll(Gone) ?? 0;
        if (dropped > 0) saver.Touch();
    }

    public static bool Has(string id) => All().Any(e => e.Id == id && e.Kind != GroupKind);

    public static Entry? Find(string id) => All().FirstOrDefault(e => e.Id == id);

    /// One favorite's picture icon (null = its own icon again).
    public static void SetMono(string id, string? letters)
    {
        foreach (var e in All().Where(e => e.Id == id)) e.Mono = string.IsNullOrWhiteSpace(letters) ? null : letters.Trim();
        Touch();
    }

    /// Picture icons for every favorite (folders and what is in them too): `pick` chooses each symbol.
    public static int MakeMono(Func<Entry, string> pick)
    {
        int n = 0;
        foreach (var e in All())
        {
            e.Mono = pick(e);
            n++;
        }
        Touch();
        return n;
    }

    public static int MonoCount => All().Count(e => e.Mono is not null);

    public static void ClearMono()
    {
        foreach (var e in All()) e.Mono = null;
        Touch();
    }

    /// The group an entry is in, or null (in the row, or not a favorite).
    public static Entry? GroupOf(string id) => Current.Items.Find(g => g.Kind == GroupKind && g.Items?.Exists(e => e.Id == id) == true);

    public static void Add(Entry e)
    {
        if (Has(e.Id)) return;
        Current.Items.Add(e);
        saver?.Touch();
        Changed?.Invoke();
    }

    /// Out of the row or out of whichever group holds it (groups themselves go with DeleteGroup).
    public static void Remove(string id, bool notify = true)
    {
        int n = Current.Items.RemoveAll(e => e.Id == id && e.Kind != GroupKind);
        foreach (var g in Current.Items)
            if (g.Items is { } inner) n += inner.RemoveAll(e => e.Id == id);
        if (n > 0 && notify) Touch();
    }

    /// The row's order by dragging: `id` goes in front of `beforeId` (null = to the end).
    public static void MoveBefore(string id, string? beforeId)
    {
        var e = Current.Items.Find(x => x.Id == id);
        if (e is null || id == beforeId) return;
        Current.Items.Remove(e);
        int at = beforeId is null ? -1 : Current.Items.FindIndex(x => x.Id == beforeId);
        Current.Items.Insert(at < 0 ? Current.Items.Count : at, e);
        Touch();
    }

    public static void Move(string id, int delta)
    {
        int i = Current.Items.FindIndex(e => e.Id == id);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Current.Items.Count) return;
        (Current.Items[i], Current.Items[j]) = (Current.Items[j], Current.Items[i]);
        saver?.Touch();
        Changed?.Invoke();
    }

    /// 설정 > 데이터 > 즐겨찾기: every favorite, folder and picture icon goes. Item shortcuts keep their own copies
    /// (ItemKeys) and stay.
    public static void Clear()
    {
        Current.Items.Clear();
        Touch();
    }

    public static void Flush() => saver?.Flush();
}
