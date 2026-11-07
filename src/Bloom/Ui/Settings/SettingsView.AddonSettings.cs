using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온 > an add-on other than the taskbar one: its settings, drawn from the settings.schema.json it ships, each
/// group under its name and each setting by its kind (SchemaRow; chips for bits, a line per entry of a list, a shortcut,
/// a record's fields under its name). A setting the schema puts in a file of the add-on's own (its window's settings) is
/// read from and written to that file, so the add-on's window and this page show the same values (AddonValues).
/// pill.theme and pill.glass are left out: Bloom keeps them to its own look (AddonHub.FollowTheme).
internal sealed partial class SettingsView
{
    private static readonly HashSet<string> Followed = new(StringComparer.Ordinal) { "pill.theme", "pill.glass" };

    /// Whether the add-on's page has settings to draw.
    private static bool HasSettings(Addon addon) =>
        addon.Schema is { } schema && schema.Groups.SelectMany(g => g.Items).Any(i => !Followed.Contains(i.Key));

    private void AddonSettings(Addon addon, string filter)
    {
        if (addon.Schema is not { } schema || addon.Values is not { } v) return;
        foreach (var group in schema.Groups)
        {
            var items = group.Items.Where(i => !Followed.Contains(i.Key)).ToList();
            if (items.Count == 0) continue;
            string title = group.Name.Shown;
            // a list says what it is for under the group's name (its lines are its entries)
            string? note = group.About?.Shown ?? items.FirstOrDefault(i => i.Type == "list")?.About?.Shown;
            Header(title, note);
            foreach (var item in items)
                switch (item.Type)
                {
                    case "flags":
                        AddonFlags(item, v, filter);
                        break;
                    case "list":
                        AddonList(item, v, filter);
                        break;
                    case "hotkey":
                        AddonHotkey(item, v, filter);
                        break;
                    case "records":
                        AddonRecords(item, v, group.About?.Shown ?? item.About?.Shown, filter);
                        Header(title); // what follows the records is the group's again
                        break;
                    default:
                        SchemaRow(schema, v, item.Key, Glyph(item), filter);
                        break;
                }
        }
    }

    /// Bits of one number as chips; with a min of 1 the last chip on stays on.
    private void AddonFlags(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key, title = item.Name.Shown;
        string? sub = item.About?.Shown;
        int bits = v.Int(key);
        var chips = new List<(string label, bool on, Action<bool> set)>();
        foreach (var o in item.Options)
        {
            int bit = int.Parse(o.Value, CultureInfo.InvariantCulture);
            Action<bool> set = on =>
            {
                int now = v.Int(key), next = on ? now | bit : now & ~bit;
                if (next == 0 && item.Min >= 1)
                {
                    Dispatcher.BeginInvoke(TaskbarRebuild); // the key goes back on
                    return;
                }
                v.Set(key, next);
            };
            chips.Add((o.Name.Shown, (bits & bit) != 0, set));
        }
        string names = string.Join(", ", chips.Select(c => c.label));
        string? shown = collected is null && filter.Length == 0 ? sub : sub is null ? names : $"{sub} · {names}";
        Place(title, shown, () => Line(title, shown, null, folding && filter.Length > 0, filter.Length == 0 ? Chips(chips) : null), null, null, filter);
    }

    /// A list of names: a line per entry with 지우기, then a field that adds one (Enter).
    private void AddonList(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key;
        var list = v.List(key);
        foreach (string entry in list)
        {
            string e = entry;
            void Remove()
            {
                v.SetList(key, v.List(key).Where(x => x != e));
                Dispatcher.BeginInvoke(TaskbarRebuild);
            }
            Add(e, null, RowButton(Tr("지우기", "Remove"), Remove), G.Delete, null, null, filter);
        }
        int max = item.Max ?? 200;
        string title = Tr($"{item.Name.Shown} 추가", $"Add: {item.Name.Shown}");
        if (list.Count >= max)
        {
            Add(title, Tr($"{max}개까지 넣을 수 있습니다", $"Up to {max} can be added"), null, G.Add, null, null, filter);
            return;
        }
        string? suffix = item.Suffix;
        string sub = suffix is null
            ? Tr("이름을 입력하고 Enter를 누릅니다", "Type a name and press Enter")
            : Tr($"{suffix}로 끝나는 파일 이름을 입력하고 Enter를 누릅니다", $"Type a file name ending in {suffix} and press Enter");
        Field(title, sub, G.Add, "", suffix is null ? "" : "name" + suffix, x =>
        {
            string name = ListEntry(x, suffix);
            if (name.Length > 0 && !v.List(key).Contains(name)) v.SetList(key, v.List(key).Append(name));
        }, filter, x =>
        {
            string name = ListEntry(x, suffix);
            if (x.Trim().Length == 0) return null;
            if (name.Length == 0)
                return suffix is null ? Tr("쓸 수 없는 이름입니다", "That name can't be used") : Tr($"{suffix}로 끝나는 파일 이름만 됩니다", $"Only a file name ending in {suffix}");
            return v.List(key).Contains(name) ? Tr("이미 목록에 있습니다", "Already in the list") : null;
        });
    }

    /// An entry as the list keeps it ("Chrome.EXE" → "chrome.exe" with a suffix), or "" when it can't be one.
    internal static string ListEntry(string text, string? suffix)
    {
        string name = text.Trim();
        if (name.Length == 0 || name.Length > 260 || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0) return "";
        if (suffix is null) return name;
        name = name.ToLowerInvariant();
        return name.Length > suffix.Length && name.EndsWith(suffix.ToLowerInvariant(), StringComparison.Ordinal) ? name : "";
    }

    /// A shortcut the add-on registers, pressed in like Bloom's own (KeyRecorder); Delete clears it.
    private void AddonHotkey(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key, title = item.Name.Shown;
        var (mods, vk) = v.Key(key);
        string combo = Hotkeys.Combo(mods, vk);
        string sub = item.About?.Shown ?? (combo == "off"
            ? Tr("꺼짐 · 클릭하여 단축키 지정", "Off — click to set a shortcut")
            : Tr("클릭하여 다른 조합을 누르고, Delete로 지웁니다", "Click and press another combination; Delete clears it"));
        if (Record(title, sub, filter)) return;
        var keys = new KeyRecorder(combo)
        {
            Listening = on => host.Shortcuts?.Pause(on),
            Done = () => FocusFilter?.Invoke(),
            Check = c => host.Shortcuts?.Owner(c) is not null ? Tr("Bloom에서 사용 중인 키", "Bloom already uses it") : null,
        };
        keys.Picked += c =>
        {
            if (c == "off") v.SetKey(key, 0, 0);
            else if (Hotkeys.Parse(c) is { } k) v.SetKey(key, k.mods, k.vk);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        };
        Add(title, sub, keys, G.Keyboard, keys.Start, null, filter);
    }

    /// Records the add-on keeps (a monitor each): its fields under each one's name. Bloom changes fields, never which
    /// records there are.
    private void AddonRecords(AddonSetting item, AddonValues v, string? about, string filter)
    {
        string key = item.Key;
        var records = v.Records(key);
        if (records.Count == 0)
        {
            Add(item.Name.Shown, Tr("애드온이 한 번 실행되면 여기에 보입니다", "They show here once the add-on has run"), null, G.Screen, null, null, filter);
            return;
        }
        string? note = about; // under the first record's name only
        foreach (var (id, name) in records)
        {
            Header(name, note);
            note = null;
            foreach (var f in item.Fields)
            {
                var (fieldName, unit) = f.Split();
                string? sub = f.About?.Shown;
                string field = f.Key;
                if (f.Type == "bool")
                {
                    bool on = AddonSchema.Int(v.Record(key, id, field)) is { } i ? i != 0 : v.Record(key, id, field)?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                    var sw = Toggle(on, x => v.SetRecord(key, id, field, JsonValue.Create(x)), out var t);
                    Add(fieldName, sub, sw, G.Check, t, null, filter);
                }
                else
                {
                    int min = f.Min ?? 0, max = f.Max ?? 100;
                    int now = Math.Clamp(AddonSchema.Int(v.Record(key, id, field)) ?? AddonSchema.Int(f.Default) ?? min, min, max);
                    Level(fieldName, sub, G.Sliders, now, min, max, max - min >= 100 ? 5 : 1, n => Unit(n, unit), n => v.SetRecord(key, id, field, JsonValue.Create(n)), filter);
                }
            }
        }
    }
}
