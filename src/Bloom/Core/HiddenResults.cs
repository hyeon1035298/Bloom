using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Bloom.Search;

namespace Bloom.Core;

/// Results the user took out of the search and the home one by one (Ctrl+K › 검색에서 숨기기), with the name they had
/// then, so 설정 › 런처 › 숨긴 항목 can list them after the item itself is gone. Changed on the UI thread; the lookup set is
/// swapped whole, so reading it needs no lock.
internal sealed class HiddenResults
{
    public sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Sub { get; set; }
        public string Kind { get; set; } = "";
        public long At { get; set; }
    }

    public List<Entry> Items { get; set; } = new();

    private const string File = "hidden.json";

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] private static HashSet<string> ids = new(StringComparer.Ordinal);
    [JsonIgnore] public static HiddenResults Current { get; private set; } = new();

    /// The kinds a row can be hidden by: things that answer a search line again and again. Calculator answers, notes,
    /// the AI and web lines, windows and clipboard entries change or are made up from the line itself.
    private static readonly HashSet<ItemKind> Kinds = [ItemKind.App, ItemKind.Command, ItemKind.Setting, ItemKind.File, ItemKind.Folder, ItemKind.Quicklink, ItemKind.Toggle];

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<HiddenResults>(Paths.In(File));
        Current.Items.RemoveAll(e => string.IsNullOrEmpty(e.Id));
        Rebuild();
        saver = Saver.ForJson(Paths.In(File), () => Current);
    }

    public static int Count => ids.Count;

    public static bool Any => ids.Count > 0;

    public static bool Has(string id) => ids.Count > 0 && ids.Contains(id);

    public static bool CanHide(ResultItem item) => item.Id.Length > 0 && Kinds.Contains(item.Kind);

    /// Newest first, as the settings list shows them.
    public static List<Entry> List() => Current.Items.OrderByDescending(e => e.At).ToList();

    public static void Add(ResultItem item)
    {
        if (!CanHide(item) || Has(item.Id)) return;
        Current.Items.Add(new Entry { Id = item.Id, Title = item.Title, Sub = item.Subtitle, Kind = item.Kind.ToString(), At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        Changed();
    }

    public static void Remove(string id)
    {
        if (Current.Items.RemoveAll(e => e.Id == id) > 0) Changed();
    }

    public static void Clear()
    {
        if (Current.Items.Count == 0) return;
        Current.Items.Clear();
        Changed();
    }

    /// Takes the hidden rows out of the sections from index `from` on and drops a list section left with none.
    public static void Strip(List<Section> sections, int from = 0)
    {
        if (ids.Count == 0) return;
        for (int i = sections.Count - 1; i >= from; i--)
        {
            var s = sections[i];
            if (s.Items.RemoveAll(x => ids.Contains(x.Id)) > 0 && s.Items.Count == 0 && s.Widget is null) sections.RemoveAt(i);
        }
    }

    public static void Flush() => saver?.Flush();

    private static void Changed()
    {
        Rebuild();
        saver?.Touch();
    }

    private static void Rebuild() => ids = new HashSet<string>(Current.Items.Select(e => e.Id), StringComparer.Ordinal);
}
