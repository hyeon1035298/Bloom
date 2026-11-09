using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
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
/// For the add-ons Bloom ships, AddonEasy words and lays the lines out plainly (a line shown only while its switch is on,
/// pictures for how something looks, rare lines under 자세히) and AddonPreviews draws what the values come to.
internal sealed partial class SettingsView
{
    private static readonly HashSet<string> Followed = new(StringComparer.Ordinal) { "pill.theme", "pill.glass" };

    /// The add-on whose page is being drawn: AddonEasy's and AddonPictures' entries are its.
    private string easyAddon = "";

    /// 실행 중에서 고르기's list while it is open.
    private PopupMenu? running;

    /// Whether the add-on's page has settings to draw.
    private static bool HasSettings(Addon addon) =>
        addon.Schema is { } schema && schema.Groups.SelectMany(g => g.Items).Any(i => !Followed.Contains(i.Key));

    private void AddonSettings(Addon addon, string filter)
    {
        if (addon.Schema is not { } schema || addon.Values is not { } v) return;
        string id = addon.Id;
        easyAddon = id;
        var order = AddonEasy.Order(id);
        var groups = schema.Groups.Select((g, i) => (g, at: Array.IndexOf(order, g.Id) is var o and >= 0 ? o : order.Length + i)).OrderBy(x => x.at).Select(x => x.g);
        foreach (var group in groups)
        {
            var items = group.Items.Where(i => !Followed.Contains(i.Key)).ToList();
            if (items.Count == 0) continue;
            string title = AddonEasy.Group(id, group.Id) ?? group.Name.Shown;
            // a list says what it is for under the group's name (its lines are its entries)
            string? note = AddonEasy.GroupNote(id, group.Id) ?? group.About?.Shown ?? items.FirstOrDefault(i => i.Type == "list")?.About?.Shown;
            Header(title, note);
            if (AddonEasy.Folded(id, group.Id))
            {
                FoldedGroup(group.Id, items, schema, v, title, filter);
                continue;
            }
            var more = new List<(bool changed, Action add)>();
            foreach (var item in items)
            {
                var easy = AddonEasy.Of(id, item.Key);
                var it = item;
                if (easy?.More == true) more.Add((v.Changed(it.Key), () => AddonItem(schema, v, it, easy, title, filter)));
                else AddonItem(schema, v, it, easy, title, filter);
            }
            More(group.Id, filter, more);
        }
    }

    /// A group under one 자세히 line (a monitor's brightness range): its count is the records', or the lines'.
    private void FoldedGroup(string groupId, List<AddonSetting> items, AddonSchema schema, AddonValues v, string title, string filter)
    {
        bool all = collected is not null || filter.Length > 0;
        string key = pageKey + "/" + groupId;
        bool open = all || unfolded.Contains(key);
        if (!all)
        {
            int monitors = items.Where(i => i.Type == "records").Sum(i => v.Records(i.Key).Count);
            string words = monitors > 0 ? Tr($"자세히 · 모니터 {monitors}대", $"More · {monitors} monitors") : Tr($"자세히 · {items.Count}개", $"More · {items.Count}");
            Disclose(words, null, open, () =>
            {
                if (!unfolded.Remove(key)) unfolded.Add(key);
                TaskbarRebuild();
            }, filter);
        }
        if (!open) return;
        folding = true;
        foreach (var item in items) AddonItem(schema, v, item, AddonEasy.Of(easyAddon, item.Key), title, filter);
        folding = false;
    }

    /// One setting of an add-on's page, by its kind, with what AddonEasy says of it; a line shown only while its switch
    /// is on is left out while it is off (never while searching or collecting) and set in under it.
    private void AddonItem(AddonSchema schema, AddonValues v, AddonSetting item, AddonEasy.Line? easy, string groupTitle, string filter)
    {
        string key = item.Key;
        bool everything = collected is not null || filter.Length > 0;
        // the pictures a group starts with, and the switch the Win+Alt+Tab lines hang on
        switch (easyAddon, key)
        {
            case ("brightness", "taskbar.number"):
                Block(AddonPreviews.Brightness(v), filter, false);
                break;
            case ("brightness", "idle.minutes") when v.Bool("idle.on"):
                Block(AddonPreviews.Idle(v), filter, true);
                break;
            case ("keep-awake", "awake.display"):
                Block(AddonPreviews.Coffee(v), filter, false);
                break;
            case ("alt-tab-per-monitor", "monitor.winAltTabLocation"):
                WinSame(v, filter);
                break;
        }
        if (easy?.When is { } when && !when(v) && !everything) return;
        indenting = easy?.When is not null;
        try
        {
            switch ((easyAddon, key, item.Type))
            {
                case ("memory-clean", "clean.areas", _):
                    CleanStrength(item, v, filter);
                    break;
                case ("memory-clean", "auto.percent", _):
                    int now = AddonPreviews.MemoryNow();
                    SchemaRow(schema, v, key, G.Sliders, filter, easy: easy, mark: now, sub: now >= 0 ? Tr($"세로 눈금이 지금 사용량 {now}%입니다", $"The upright mark is the {now}% in use now") : null);
                    break;
                case (_, _, "flags"):
                    AddonFlags(item, v, filter);
                    break;
                case (_, _, "list"):
                    AddonList(item, v, easy, filter);
                    break;
                case (_, _, "hotkey"):
                    AddonHotkey(item, v, easy, filter);
                    break;
                case (_, _, "records"):
                    AddonRecords(item, v, null, filter);
                    Header(groupTitle); // what follows the records is the group's again
                    break;
                default:
                    // a Win+Alt+Tab set as Alt+Tab follows it
                    Action<string>? also = (easyAddon, key) switch
                    {
                        ("alt-tab-per-monitor", "monitor.altTabLocation") when AddonEasy.SameWin(v) => x => v.Set("monitor.winAltTabLocation", x),
                        ("alt-tab-per-monitor", "monitor.altTabWindows") when AddonEasy.SameWin(v) => x => v.Set("monitor.winAltTabWindows", x),
                        _ => null,
                    };
                    SchemaRow(schema, v, key, Glyph(item), filter, rebuild: true, easy: easy, also: also);
                    break;
            }
        }
        finally
        {
            indenting = false;
        }
    }

    /// 'Win+Alt+Tab도 같게': on, Win+Alt+Tab takes Alt+Tab's two choices and follows them; off, its own two lines show.
    private void WinSame(AddonValues v, string filter)
    {
        string title = Tr("Win+Alt+Tab도 같게", "Win+Alt+Tab the same");
        string sub = Tr("끄면 Win+Alt+Tab을 따로 정합니다", "Off, Win+Alt+Tab is set on its own");
        if (Record(title, sub, filter)) return;
        var sw = Toggle(AddonEasy.SameWin(v), on =>
        {
            AddonEasy.WinApart = !on;
            if (on)
            {
                v.Set("monitor.winAltTabLocation", v.Text("monitor.altTabLocation"));
                v.Set("monitor.winAltTabWindows", v.Text("monitor.altTabWindows"));
            }
            Dispatcher.BeginInvoke(TaskbarRebuild);
        }, out var t);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        void Combo(string combo)
        {
            foreach (string part in combo.Split('+'))
            {
                var cap = Keys.Cap(part);
                if (caps.Children.Count == 0) cap.Margin = new Thickness(0);
                caps.Children.Add(cap);
            }
        }
        Combo("Win+Alt+Tab");
        var same = Theme.T("=", 12, Theme.P.TextFaint);
        same.Margin = new Thickness(8, 0, 5, 0);
        caps.Children.Add(same);
        Combo("Alt+Tab");
        Place(title, sub, () => Line(title, sub, sw, folding && filter.Length > 0, filter.Length == 0 ? caps : null), t, null, filter);
    }

    /// 정리 세기: the seven kinds of memory the add-on can clean, as three steps and 직접 (the chips, set in under it).
    private void CleanStrength(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key;
        int bits = v.Int(key);
        string now = AddonEasy.CustomAreas ? "custom" : AddonEasy.Strengths.FirstOrDefault(s => s.bits == bits).key ?? "custom";
        string title = Tr("정리 세기", "Cleaning strength");
        string sub = now switch
        {
            "light" => Tr("가볍게: 프로그램이 쥐고만 있는 메모리를 비웁니다", "Light: frees what programs hold but don't use"),
            "normal" => Tr("보통: 프로그램과 캐시의 남는 메모리를 비웁니다", "Normal: frees what programs and caches don't use"),
            "strong" => Tr("강하게: 일곱 가지를 모두 비웁니다. 잠깐 멈칫할 수 있습니다", "Strong: frees all seven kinds; things may pause a moment"),
            _ => Tr("직접: 정리할 곳을 아래에서 고릅니다", "Custom: pick what to clean below"),
        };
        var options = new[] { ("light", Tr("가볍게", "Light")), ("normal", Tr("보통", "Normal")), ("strong", Tr("강하게", "Strong")), ("custom", Tr("직접", "Custom")) }
            .Select(o => (o.Item1, o.Item2, AddonPictures.Option("memory-clean", "clean.strength", o.Item1)!)).ToArray();
        // how many of the seven kinds the value cleans, a square each (its name on the pointer)
        var squares = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var o in item.Options)
        {
            bool on = (bits & int.Parse(o.Value, CultureInfo.InvariantCulture)) != 0;
            squares.Children.Add(new SmoothBorder { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 3, 0), Background = on ? Theme.P.Accent : Theme.P.Edge, ToolTip = o.Name.Shown, VerticalAlignment = VerticalAlignment.Center });
        }
        int count = item.Options.Count(o => (bits & int.Parse(o.Value, CultureInfo.InvariantCulture)) != 0);
        var countText = Theme.T(Tr($"{item.Options.Count}가지 중 {count}가지", $"{count} of {item.Options.Count} kinds"), 12, Theme.P.TextFaint);
        countText.Margin = new Thickness(5, 0, 0, 0);
        squares.Children.Add(countText);
        Pictures(title, sub, G.Sliders, options, now, k =>
        {
            AddonEasy.CustomAreas = k == "custom";
            var step = AddonEasy.Strengths.FirstOrDefault(s => s.key == k);
            if (step.key is not null) v.Set(key, step.bits);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        }, filter, squares);
        if (now != "custom" && collected is null && filter.Length == 0) return;
        indenting = true;
        AddonFlags(item, v, filter, Tr("정리할 곳", "What to clean"));
        indenting = false;
    }

    /// Bits of one number as chips; with a min of 1 the last chip on stays on.
    private void AddonFlags(AddonSetting item, AddonValues v, string filter, string? ownTitle = null)
    {
        string key = item.Key, title = ownTitle ?? item.Name.Shown;
        string? sub = ownTitle is null ? item.About?.Shown : null;
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
                Dispatcher.BeginInvoke(TaskbarRebuild); // the squares by 정리 세기 follow
            };
            chips.Add((o.Name.Shown, (bits & bit) != 0, set));
        }
        string names = string.Join(", ", chips.Select(c => c.label));
        string? shown = collected is null && filter.Length == 0 ? sub : sub is null ? names : $"{sub} · {names}";
        Place(title, shown, () => Line(title, shown, null, folding && filter.Length > 0, filter.Length == 0 ? Chips(chips) : null), null, null, filter);
    }

    /// A list of names: a line per entry with 지우기, then a field that adds one (Enter); a list of programs also picks
    /// one from those running (read when the list opens, dropped when it closes).
    private void AddonList(AddonSetting item, AddonValues v, AddonEasy.Line? easy, string filter)
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
        string name = easy?.Title ?? item.Name.Shown;
        string title = Tr($"{name} 더하기", $"Add: {name}");
        if (list.Count >= max)
        {
            Add(title, Tr($"{max}개까지 넣을 수 있습니다", $"Up to {max} can be added"), null, G.Add, null, null, filter);
            return;
        }
        string? suffix = item.Suffix;
        if (suffix == ".exe")
        {
            string pickTitle = Tr("실행 중에서 고르기", "Pick from running");
            FrameworkElement? button = null;
            void Pick()
            {
                running?.Close();
                var taken = v.List(key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                    using (p)
                    {
                        string exe = ListEntry(p.ProcessName + ".exe", suffix);
                        if (exe.Length > 0 && p.SessionId != 0 && !taken.Contains(exe)) names.Add(exe);
                    }
                var items = names.Take(40).Select(n => new ActionItem
                {
                    Title = n,
                    Glyph = G.App,
                    Run = () =>
                    {
                        if (!v.List(key).Contains(n)) v.SetList(key, v.List(key).Append(n));
                        Dispatcher.BeginInvoke(TaskbarRebuild);
                    },
                }).ToList();
                if (items.Count == 0) return;
                running = new PopupMenu();
                running.Open(items, button);
            }
            button = RowButton(pickTitle, Pick);
            Add(title, Tr("실행 중인 프로그램에서 고르거나, 아래에 이름을 입력하세요", "Pick a running program, or type its name below"), button, G.Add, Pick, null, filter);
        }
        string sub = suffix is null
            ? Tr("이름을 입력하고 Enter를 누릅니다", "Type a name and press Enter")
            : Tr($"{suffix}로 끝나는 파일 이름을 입력하고 Enter를 누릅니다", $"Type a file name ending in {suffix} and press Enter");
        Field(suffix == ".exe" ? Tr("이름으로 더하기", "Add by name") : title, sub, G.Add, "", suffix is null ? "" : "name" + suffix, x =>
        {
            string entryName = ListEntry(x, suffix);
            if (entryName.Length > 0 && !v.List(key).Contains(entryName)) v.SetList(key, v.List(key).Append(entryName));
        }, filter, x =>
        {
            string entryName = ListEntry(x, suffix);
            if (x.Trim().Length == 0) return null;
            if (entryName.Length == 0)
                return suffix is null ? Tr("쓸 수 없는 이름입니다", "That name can't be used") : Tr($"{suffix}로 끝나는 파일 이름만 됩니다", $"Only a file name ending in {suffix}");
            return v.List(key).Contains(entryName) ? Tr("이미 목록에 있습니다", "Already in the list") : null;
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

    /// A shortcut the add-on registers, pressed in like Bloom's own (KeyRecorder, shown as key caps); Delete clears it.
    private void AddonHotkey(AddonSetting item, AddonValues v, AddonEasy.Line? easy, string filter)
    {
        string key = item.Key, title = easy?.Title ?? item.Name.Shown;
        var (mods, vk) = v.Key(key);
        string combo = Hotkeys.Combo(mods, vk);
        string? sub = combo == "off" ? Tr("꺼짐 · 클릭하여 단축키 지정", "Off — click to set a shortcut")
            : easy?.Sub is { } own ? own.Length > 0 ? own : null
            : item.About?.Shown ?? Tr("클릭하여 다른 조합을 누르고, Delete로 지웁니다", "Click and press another combination; Delete clears it");
        if (Record(title, sub, filter)) return;
        var keys = new KeyRecorder(combo)
        {
            Listening = on => host.Shortcuts?.Pause(on),
            Done = () => FocusFilter?.Invoke(),
            Check = c => host.Shortcuts?.Owner(c) is not null ? Tr("Bloom에서 사용 중인 키", "Bloom already uses it") : null,
            Caps = true,
        };
        keys.Picked += c =>
        {
            if (c == "off") v.SetKey(key, 0, 0);
            else if (Hotkeys.Parse(c) is { } k) v.SetKey(key, k.mods, k.vk);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        };
        Add(title, sub, keys, G.Keyboard, keys.Start, null, filter);
    }

    /// Records the add-on keeps (a monitor each): its fields under each one's name, a low and a high field as one range.
    /// Bloom changes fields, never which records there are.
    private void AddonRecords(AddonSetting item, AddonValues v, string? about, string filter)
    {
        string key = item.Key;
        var records = v.Records(key);
        if (records.Count == 0)
        {
            Add(item.Name.Shown, Tr("애드온이 한 번 실행되면 여기에 보입니다", "They show here once the add-on has run"), null, G.Screen, null, null, filter);
            return;
        }
        var lo = item.Fields.FirstOrDefault(f => f.Key == "lo" && f.Type == "int");
        var hi = item.Fields.FirstOrDefault(f => f.Key == "hi" && f.Type == "int");
        string? note = about; // under the first record's name only
        foreach (var (id, name) in records)
        {
            Header(name, note);
            note = null;
            if (lo is not null && hi is not null) RangeRow(item, v, id, lo, hi, filter);
            foreach (var f in item.Fields)
            {
                if (lo is not null && hi is not null && (f == lo || f == hi)) continue;
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

    /// A record's low and high field as one span (RangeSlider): where 0 and 100 fall on the monitor's own brightness.
    private void RangeRow(AddonSetting item, AddonValues v, string id, AddonSetting lo, AddonSetting hi, string filter)
    {
        string key = item.Key;
        int min = lo.Min ?? 0, max = hi.Max ?? 100;
        int low = Math.Clamp(AddonSchema.Int(v.Record(key, id, lo.Key)) ?? AddonSchema.Int(lo.Default) ?? min, min, max);
        int high = Math.Clamp(AddonSchema.Int(v.Record(key, id, hi.Key)) ?? AddonSchema.Int(hi.Default) ?? max, min, max);
        string title = Tr("밝기 범위", "Brightness range");
        string sub = Tr($"밝기 0은 실제 {low}%, 100은 실제 {high}%입니다", $"0 is {low}% and 100 is {high}% of the monitor's own");
        if (Record(title, sub, filter)) return;
        var range = new RangeSlider(low, high, min, max, 1, (a, b) => $"{a}–{b}%");
        range.Picked += (a, b) =>
        {
            v.SetRecord(key, id, lo.Key, JsonValue.Create(a));
            v.SetRecord(key, id, hi.Key, JsonValue.Create(b));
            Dispatcher.BeginInvoke(TaskbarRebuild);
        };
        Add(title, sub, range, G.Sliders, null, null, filter);
    }
}
