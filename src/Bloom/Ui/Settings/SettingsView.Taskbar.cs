using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Addons;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;
using static Bloom.Ui.TaskbarMap;
using Control = Bloom.Ui.TaskbarMap.Control;
using Section = Bloom.Ui.TaskbarMap.Section;

namespace Bloom.Ui;

/// 설정 > 애드온 > 작업표시줄, the taskbar add-on's page (PillTaskbar, a separate GPL-3.0 program Bloom downloads and
/// starts): its status text and switch (the line and the page's head are SettingsView.Addons', with what it is:
/// version, license, source); its look (a preview of the taskbar on top), tray, auto-hide island and folders
/// (TaskbarEditor), drawn from the settings.schema.json it ships (names, defaults, ranges) and laid out by TaskbarMap.
internal sealed partial class SettingsView
{
    /// The folds open now (자세히 under a group), kept while Bloom runs.
    private static readonly HashSet<string> taskbarOpen = new();

    /// QA (Captures.AddonPages): every 자세히 on the taskbar page open, or all shut again.
    internal static void OpenTaskbarFoldsForQa(bool open)
    {
        taskbarOpen.Clear();
        if (open) taskbarOpen.UnionWith(["look", "tray", "island", "folders"]);
    }

    /// The folder editor's selection and half-done steps, kept across the page's rebuilds.
    private readonly TaskbarEditor.Edit taskbarEdit = new();

    /// The tests: a hand-made add-on in place of the shipped one.
    private Addon? taskbarAddon;

    /// On the taskbar add-on's page while it is on: its settings, what is looked for first (its look) first.
    private void TaskbarLines(Addon addon, AddonStatus status, string filter)
    {
        // the add-on's own settings: only with the schema it ships (its names and ranges are not Bloom's to guess)
        if (addon.Schema is not { } schema || addon.Values is not { } values) return;
        easyAddon = addon.Id;
        TaskbarLook(schema, values, filter);
        TaskbarTray(schema, values, filter);
        TaskbarIsland(schema, values, filter);
        TaskbarFolders(addon, schema, values, status, filter);
    }

    // ── 폴더 ─────────────────────────────────────────────────────────────────────────────────────
    private void TaskbarFolders(Addon addon, AddonSchema schema, AddonValues v, AddonStatus status, string filter)
    {
        Header(Tr("작업표시줄 폴더", "Taskbar folders"));
        bool on = v.Bool("folders.enabled");
        SchemaRow(schema, v, "folders.enabled", G.Folder, filter, sub: on
            ? Tr("앱을 다른 앱 위에 끌어 놓으면 폴더가 됩니다", "Dropping an app onto another makes a folder")
            : Tr("켜면 고정한 앱을 폴더로 묶습니다", "On, pinned apps can be grouped into folders"), rebuild: true);
        if (on && collected is null && filter.Length == 0)
        {
            if (pageKey == current)
            {
                var state = addon.State();
                string? why = status.State != AddonState.Running ? Tr("작업표시줄 꾸미기가 작동하는 동안 폴더를 고칠 수 있습니다", "Folders can be edited while taskbar customization runs")
                    : state is null ? Tr("작업표시줄에서 아직 폴더 정보가 오지 않았습니다", "The taskbar hasn't sent its folders yet")
                    : addon.Commands.TimedOut ? Tr("작업표시줄이 5초 안에 답하지 않아 지금은 볼 수만 있습니다", "The taskbar didn't answer within 5 seconds, so for now this is read only")
                    : state.Folders is { Enabled: false } ? Tr("작업표시줄 폴더가 아직 켜지지 않았습니다", "Taskbar folders haven't turned on yet")
                    : null;
                Block(new TaskbarEditor(host, addon, v, state, taskbarEdit, why, TaskbarRebuild), filter, true);
            }
        }
        else if (on)
        {
            // the editor isn't a line: the search finds it, and the running folder's two settings, by these
            Add(Tr("폴더 편집", "Edit folders"), Tr("폴더를 만들고 이름, 아이콘, 안의 앱을 바꿉니다", "Make folders and change their names, icons and apps"), null, G.Folder, () => Go(AddonPageKey(addon.Id)), null, filter);
            foreach (var e in In(Section.Folders).Where(e => e.Control == Control.Running))
                if (schema[e.Key] is { } item && AddonEasy.Of(schema, e.Key) is var easy)
                    Add(easy?.Title ?? item.Split().name, easy?.Sub is { Length: > 0 } s ? s : item.About?.Shown, null, G.Folder, () => Go(AddonPageKey(addon.Id)), null, filter);
        }
        Fold("folders", Section.FoldersMore, schema, v, filter);
    }

    // ── 모양 ─────────────────────────────────────────────────────────────────────────────────────
    private void TaskbarLook(AddonSchema schema, AddonValues v, string filter)
    {
        Header(Tr("작업표시줄 모양", "Taskbar look"));
        Block(AddonPreviews.Taskbar(v), filter, false);
        string size = SizeNow(v);
        var sizes = Sizes.Select(p => (p.key, p.key switch { "s" => Tr("작게", "Small"), "m" => Tr("보통", "Medium"), _ => Tr("크게", "Large") })).ToList();
        if (size == "custom") sizes.Add(("custom", Tr("직접", "Custom")));
        void PickSize(string k)
        {
            if (Sizes.FirstOrDefault(p => p.key == k) is not { key: not null } p) return;
            v.Set("size.enabled", true);
            v.Set("size.taskbarHeight", p.height);
            v.Set("size.iconSize", p.icon);
            v.Set("size.iconSpacing", p.spacing);
            Dispatcher.BeginInvoke(TaskbarRebuild); // 직접 goes, and 자세히's numbers follow
        }
        string sizeTitle = Tr("크기", "Size"), sizeSub = Tr("작업표시줄 높이와 아이콘 크기", "The taskbar's height and icon size");
        var sizePictures = sizes.Select(o => AddonPictures.Option(schema, "size", o.key)).ToArray();
        // an add-on without the pictures: the sizes side by side
        if (sizePictures.All(p => p is not null)) Pictures(sizeTitle, sizeSub, G.Screen, sizes.Select((o, i) => (o.key, o.Item2, sizePictures[i]!)).ToArray(), size, PickSize, filter);
        else Choice(sizeTitle, sizeSub, G.Screen, sizes.ToArray(), size, PickSize, filter);
        SchemaRow(schema, v, "pill.style", G.Palette, filter, Tr("배경", "Background"), Tr("알약 뒤를 흐리게 비춥니다", "Blurs what is behind the pill"), rebuild: true);
        SchemaRow(schema, v, "start.mode", G.Power, filter, sub: Tr("꽃 버튼은 누르면 Bloom을 열고 닫습니다", "The flower button opens and closes Bloom"), rebuild: true);
        SchemaRow(schema, v, "pill.edge", G.Screen, filter, Tr("화면의 어디에", "Where on the screen"), Tr("Windows 설정은 바꾸지 않습니다", "Windows' own setting stays as it is"), rebuild: true);
        SchemaRow(schema, v, "pill.align", G.Screen, filter, Tr("알약 정렬", "Pill alignment"), "", rebuild: true);
        Fold("look", Section.LookMore, schema, v, filter, Tr("숫자로 바꾸면 위의 크기는 ‘직접’이 됩니다", "A number changed here makes the size above ‘Custom’"));
    }

    /// The size preset the three numbers match ("m" while the add-on leaves the size to Windows), else "custom".
    private static string SizeNow(AddonValues v)
    {
        if (!v.Bool("size.enabled")) return "m";
        int h = v.Int("size.taskbarHeight"), i = v.Int("size.iconSize"), g = v.Int("size.iconSpacing");
        return Sizes.FirstOrDefault(p => p.height == h && p.icon == i && p.spacing == g).key ?? "custom";
    }

    // ── 트레이 ───────────────────────────────────────────────────────────────────────────────────
    private void TaskbarTray(AddonSchema schema, AddonValues v, string filter)
    {
        Header(Tr("작업표시줄 트레이", "Taskbar tray"));
        IconChips(schema, v, Section.Tray, Tr("보일 아이콘", "Icons shown"), Tr("끈 아이콘은 자리도 남기지 않습니다", "An icon turned off leaves no gap"), filter);
        SchemaRow(schema, v, "tray.clock", G.Clock, filter, sub: "", rebuild: true);
        if (v.Text("tray.clock") != "hidden" || collected is not null || filter.Length > 0)
        {
            indenting = true;
            SchemaRow(schema, v, "tray.clockFormat", G.Clock, filter, sub: "", rebuild: true);
            indenting = false;
        }
        SchemaRow(schema, v, "media.enabled", G.Volume, filter, Tr("재생 중인 곡", "Now playing"), Tr("앨범 아트, 제목, 재생 단추", "Album art, title and play buttons"), rebuild: true);
        SchemaRow(schema, v, "usage.enabled", G.Speed, filter, Tr("AI 사용량", "AI usage"), Tr("계정마다 막대 하나. 숨김 아일랜드에도 나옵니다", "A bar per account; the island shows it too"), rebuild: true);
        // the add-on reads the usage file Bloom writes (설정 > 홈 화면 > AI 사용량), which is off by default
        if (collected is null && v.Bool("usage.enabled") && !UsageExport.On)
        {
            indenting = true;
            void Export()
            {
                UsageExport.Set(true);
                Settings.Save();
                TaskbarRebuild();
            }
            Add(Tr("사용량 파일 내보내기 꺼짐", "Usage file export is off"), Tr("작업표시줄은 Bloom이 내보낸 사용량 파일을 읽습니다. 켜야 사용량이 보입니다", "The taskbar reads the usage file Bloom exports; turn it on to see the usage"),
                RowButton(Tr("켜기", "Turn on"), Export), G.Warning, Export, null, filter);
            indenting = false;
        }
        // shared with the home cards (SettingsView.Usage): each account's bar, 이름표, 아이콘 색
        if (collected is not null || filter.Length > 0 || v.Bool("usage.enabled"))
        {
            indenting = true;
            UsageBarRows(filter);
            UsageTagRow(filter);
            UsageLogoRow(v, Tr("홈 카드와 같은 설정입니다", "The same setting as the home cards"), filter);
            indenting = false;
        }
        Fold("tray", Section.TrayMore, schema, v, filter, null, 2, () =>
        {
            IconChips(schema, v, Section.TrayMore, Tr("그 밖의 아이콘", "Other icons"), null, filter);
            // the bell's third choice: its chip above is shown / hidden
            if (schema["tray.bell"] is not null)
            {
                var bell = Toggle(v.Text("tray.bell") == "whenNew", on => v.Set("tray.bell", on ? "whenNew" : "shown"), out var t);
                Add(Tr("새 알림이 있을 때만 알림 벨", "Bell only with new notifications"), Tr("알림이 없으면 알림 벨을 숨깁니다", "Hides the bell while there are no notifications"), bell, G.Bell, t, null, filter);
            }
        });
    }

    /// One row of chips for a section's tray icons (a chip on = the icon shows), and the bell's.
    private void IconChips(AddonSchema schema, AddonValues v, Section section, string title, string? sub, string filter)
    {
        var chips = new List<(string label, bool on, Action<bool> set)>();
        var glyphs = new List<char>();
        foreach (var e in In(section))
        {
            if (schema[e.Key] is not { } item) continue;
            string key = e.Key;
            // a chip changes the preview above it: the page follows
            void Then() => Dispatcher.BeginInvoke(TaskbarRebuild);
            if (e.Control == Control.Shown) chips.Add((ChipName(item), !v.Bool(key), on =>
            {
                v.Set(key, !on);
                Then();
            }
                ));
            else if (e.Control == Control.Bell) chips.Add((ChipName(item), v.Text(key) != "hidden", on =>
            {
                v.Set(key, on ? "shown" : "hidden");
                Then();
            }
                ));
            else if (e.Control == Control.Chip) chips.Add((ChipName(item), v.Bool(key), on =>
            {
                v.Set(key, on);
                Then();
            }
                ));
            else continue;
            glyphs.Add(AddonPreviews.ChipGlyph(key));
        }
        if (chips.Count == 0) return;
        // searched and collected by the chips' names too ("볼륨" finds this line)
        string names = string.Join(", ", chips.Select(c => c.label));
        bool normal = collected is null && filter.Length == 0;
        string? shown = normal ? sub : sub is null ? names : $"{sub} · {names}";
        // while filtering the line alone is shown, as a match to open
        Place(title, shown, () => Line(title, shown, null, folding && filter.Length > 0, filter.Length == 0 ? Chips(chips, glyphs) : null), null, null, filter);
    }

    /// A chip's label: the schema's plain name for it (AddonEasy), else from the setting's name: "볼륨 아이콘 숨기기" / "Hide volume
    /// icon" → 볼륨 / Volume.
    internal static string ChipName(AddonSetting item)
    {
        if (item.Easy?.Name?.Shown is { } own) return own;
        string n = item.Name.Shown;
        foreach (string tail in new[] { " 아이콘 숨기기", " 숨기기", " 보이기", " icon" })
            if (n.EndsWith(tail, StringComparison.Ordinal)) n = n[..^tail.Length];
        foreach (string head in new[] { "Hide ", "Show " })
            if (n.StartsWith(head, StringComparison.Ordinal)) n = n[head.Length..];
        return n.Length > 0 ? char.ToUpper(n[0], CultureInfo.CurrentCulture) + n[1..] : n;
    }

    /// The chips as keys, inside their line under its title (Line's `below`); `glyphs`, one per chip, the icon each
    /// stands for ('\0' for none).
    private static FrameworkElement Chips(List<(string label, bool on, Action<bool> set)> chips, List<char>? glyphs = null)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, -6) };
        int i = 0;
        foreach (var (label, on, set) in chips)
        {
            var key = new ToggleKey(label, on, glyphs?[i++] ?? default) { Margin = new Thickness(0, 0, 6, 6) };
            key.Changed += v =>
            {
                set(v);
                Settings.Save();
            };
            wrap.Children.Add(key);
        }
        return wrap;
    }

    // ── 숨김 아일랜드 ────────────────────────────────────────────────────────────────────────────
    private void TaskbarIsland(AddonSchema schema, AddonValues v, string filter)
    {
        Header(Tr("숨김 아일랜드", "Auto-hide island"));
        bool on = v.Bool("island.enabled");
        if (schema["island.enabled"] is not null)
        {
            // the island lives in the auto-hide helper and needs Windows' own auto-hide: all three go on and off
            // together (the host turns Windows' back on if it is off when the add-on starts)
            PictureSwitch(Tr("숨김 아일랜드 쓰기", "Use the auto-hide island"), Tr("작업표시줄이 숨고 화면 가장자리에 작은 섬이 남습니다", "The taskbar hides, and a small island stays at the screen's edge"),
                AddonPictures.Thumb(schema["island.enabled"]), on, x =>
                {
                    v.Set("island.enabled", x);
                    v.Set("autohide.enabled", x);
                    Native.SystemControl.SetTaskbarAutoHide(x);
                    Dispatcher.BeginInvoke(TaskbarRebuild);
                }, filter);
        }
        // the island's own lines while it is on, set in under its switch
        if (on || collected is not null || filter.Length > 0)
        {
            indenting = true;
            Block(AddonPreviews.Island(v), filter, true);
            SchemaRow(schema, v, "island.reveal", G.Up, filter, Tr("작업표시줄 부르기", "Bringing the taskbar out"), "", rebuild: true);
            IslandColour(schema, v, filter);
            IconChips(schema, v, Section.Island, Tr("아일랜드에 보일 것", "Shown on the island"), null, filter);
            SchemaRow(schema, v, "island.hoverTransparency", G.View, filter, Tr("포인터를 올리면 비침", "See-through under the pointer"),
                Tr("0이면 그대로, 100이면 다 비칩니다", "0 not at all, 100 fully"));
            indenting = false;
        }
        Fold("island", Section.IslandMore, schema, v, filter);
    }

    /// 아일랜드 색: the island's two colours as colour dots (Swatches).
    private void IslandColour(AddonSchema schema, AddonValues v, string filter)
    {
        if (schema["island.color"] is not { } item) return;
        string title = Tr("아일랜드 색", "Island colour");
        if (Record(title, null, filter)) return;
        var dots = new Swatches(item.Options.Select(o => (o.Value, o.Name.Shown, o.Value == "white" ? Color.FromRgb(0xFA, 0xFA, 0xFA) : Color.FromRgb(0x11, 0x11, 0x11))), v.Text("island.color"));
        dots.Picked += x =>
        {
            v.Set("island.color", x);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        };
        Add(title, null, dots, G.Palette, null, null, filter);
    }

    // ── the pieces ──────────────────────────────────────────────────────────────────────────────
    /// 자세히: a section's other settings, folded at first (always open while filtering or collecting, so they are found).
    /// `extra` adds `extras` lines of the page's own before the schema's.
    private void Fold(string id, Section section, AddonSchema schema, AddonValues v, string filter, string? openSub = null, int extras = 0, Action? extra = null)
    {
        var items = In(section).Concat(TaskbarMap.Unplaced(schema.Keys, section, k => schema[k]?.Type))
            .Where(e => e.Control is Control.Number or Control.Choice or Control.Switch or Control.Text && schema[e.Key] is not null).ToList();
        bool all = collected is not null || filter.Length > 0;
        bool open = all || taskbarOpen.Contains(id);
        if (!all)
        {
            void Flip()
            {
                if (!taskbarOpen.Remove(id)) taskbarOpen.Add(id);
                TaskbarRebuild();
            }
            int n = items.Count + extras, changed = items.Count(e => v.Changed(e.Key));
            Disclose(Tr($"자세히 · {n}개", $"More · {n}"), open ? openSub : changed > 0 ? Tr($"바꾼 값 {changed}개", $"{changed} changed") : null, open, Flip, filter);
        }
        if (!open) return;
        extra?.Invoke();
        foreach (var e in items) SchemaRow(schema, v, e.Key, Glyph(schema[e.Key]!), filter);
    }

    /// "Bloom 버튼 (누르면 Bloom을 열고 닫습니다)" → the name, and the bracketed detail (null without one).
    internal static (string name, string? detail) OptionName(string text)
    {
        int open = text.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 && text.EndsWith(')') ? (text[..open], text[(open + 2)..^1]) : (text, null);
    }

    private static string Unit(int n, string unit) => unit switch
    {
        "" => n.ToString(CultureInfo.CurrentCulture),
        "%" or "초" or "분" => $"{n}{unit}",
        _ => $"{n} {unit}",
    };

    private static char Glyph(AddonSetting item) => item.Type switch
    {
        "int" => G.Sliders,
        "choice" => G.Settings,
        "text" => G.Pen,
        _ => G.Check,
    };

    /// One of the add-on's settings on its row, by its kind: a switch; a segmented choice (a list past three options); a
    /// slider for a short range, else a number field; a text field. Name and range come from the schema, `title` and
    /// `sub` replace them where the page words a line its own way ("" for no line under it), and so does AddonEasy's
    /// `easy`. `rebuild`: other lines follow the value. A switch with a picture of what it turns on, and a choice whose
    /// options all have pictures (AddonPictures, from the schema), are drawn with them. `also`: after a choice is
    /// set (another setting that follows it). `mark`: a reading on a slider's track.
    private void SchemaRow(AddonSchema schema, AddonValues v, string key, char glyph, string filter, string? title = null, string? sub = null, bool rebuild = false,
        AddonEasy.Line? easy = null, Action<string>? also = null, int mark = -1)
    {
        if (schema[key] is not { } item) return;
        easy ??= AddonEasy.Of(schema, key);
        var (name, unit) = item.Split();
        title ??= easy?.Title ?? name;
        sub ??= easy?.Sub ?? item.About?.Shown;
        if (sub?.Length == 0) sub = null;
        void Then()
        {
            if (rebuild) Dispatcher.BeginInvoke(TaskbarRebuild);
        }
        switch (item.Type)
        {
            case "bool":
                if (AddonPictures.Thumb(item) is { } thumb)
                {
                    PictureSwitch(title, sub, thumb, v.Bool(key), on =>
                    {
                        v.Set(key, on);
                        Dispatcher.BeginInvoke(TaskbarRebuild); // the picture follows
                    }, filter);
                    break;
                }
                var sw = Toggle(v.Bool(key), on =>
                {
                    v.Set(key, on);
                    Then();
                }, out var t);
                Add(title, sub, sw, glyph, t, null, filter);
                break;
            case "choice":
                var options = item.Options.Select(o => (o.Value, parts: (easy?.Option?.Invoke(o.Value) ?? OptionLabel(key, o.Value)) is { } own ? (name: own, detail: (string?)null) : OptionName(o.Name.Shown))).ToArray();
                var pictures = options.Select(o => AddonPictures.Option(schema, key, o.Value)).ToArray();
                if (options.Length <= 4 && pictures.All(p => p is not null))
                {
                    Pictures(title, sub, glyph, options.Select((o, i) => (o.Value, o.parts.name, pictures[i]!)).ToArray(), v.Text(key), x =>
                    {
                        v.Set(key, x);
                        also?.Invoke(x);
                        Dispatcher.BeginInvoke(TaskbarRebuild); // a preview, or the lines under it, follow
                    }, filter);
                    break;
                }
                // a few short names side by side; else a list, with what an option's name says in brackets under it
                if (options.Length <= 3 && options.All(o => o.parts.name.Length <= 16))
                    Choice(title, sub, glyph, options.Select(o => (o.Value, o.parts.name)).ToArray(), v.Text(key), x =>
                    {
                        v.Set(key, x);
                        also?.Invoke(x);
                        Then();
                    }, filter);
                else
                    Menu(title, sub, glyph, options.Select(o => (o.Value, o.parts.name, o.parts.detail)).ToArray(), v.Text(key), x =>
                    {
                        v.Set(key, x);
                        also?.Invoke(x);
                        Then();
                    }, filter);
                break;
            case "int":
                int min = item.Min ?? 0, max = item.Max ?? 100, def = AddonSchema.Int(item.Default) ?? min;
                if (easy?.Minutes == true)
                {
                    // minutes past an hour: typed in minutes or hours
                    string? said = sub;
                    Duration(title, _ => said, glyph, Math.Clamp(v.Int(key), min, max), max, DurationField.MinutesHours, n => v.Set(key, Math.Clamp(n, min, max)), filter);
                    break;
                }
                if (max - min <= 200)
                {
                    Level(title, sub, glyph, Math.Clamp(v.Int(key), min, max), min, max, max - min >= 100 ? 5 : 1, n => Special(key, n) ?? Unit(n, unit), n => v.Set(key, n), filter,
                        rebuild ? () => Dispatcher.BeginInvoke(TaskbarRebuild) : null, mark);
                    break;
                }
                // a wide range (a height in pixels, a delay in ms): typed in, empty = the default; a "not set" number
                // shows as its words in the empty field, so the range starts after it; short, so it stays on one line
                int low = Special(key, min) is null ? min : min + 1;
                string range = Tr($"{low}~{Unit(max, unit)}", $"{low}–{Unit(max, unit)}");
                int now = v.Int(key);
                Field(title, sub is null ? range : $"{sub} · {range}", glyph, Special(key, now) is null ? now.ToString(CultureInfo.InvariantCulture) : "",
                    Special(key, def) ?? def.ToString(CultureInfo.InvariantCulture),
                    x => v.Set(key, x.Length == 0 ? def : int.Parse(x, CultureInfo.InvariantCulture)), filter,
                    x => x.Length == 0 || int.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max ? null : range);
                break;
            default:
                Field(title, sub, glyph, v.Text(key), v.Default(key), x => v.Set(key, x.Length > 0 ? x : v.Default(key)), filter);
                break;
        }
    }

    /// A piece that isn't a line (the folder editor, a row of chips) in the group's card, under the line before it: only
    /// on the open category's page, never while filtering or collecting. `line`: a hairline above it.
    private void Block(FrameworkElement e, string filter, bool line)
    {
        if (collected is not null || filter.Length > 0 || pageKey != current) return;
        var into = Card(filter);
        if (line && into.Children.Count > 0) into.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Rule });
        into.Children.Add(e);
    }

    /// QA pictures: the folder editor showing `selected` (null: the first folder) and `error` as a command's outcome.
    public void TaskbarForQa(string? selected, string? error)
    {
        taskbarEdit.Selected = selected;
        taskbarEdit.New = false;
        taskbarEdit.Error = error;
    }

    /// Builds again where the page is scrolled to (a fold, a folder picked, the taskbar's state changing).
    private void TaskbarRebuild()
    {
        double y = VerticalOffset;
        Build(lastFilter);
        ScrollToVerticalOffset(y);
    }

    /// A text button sized to its text in a row's control column (the row would stretch it to its height).
    private static FrameworkElement RowButton(string text, Action click)
    {
        var b = ToolUi.TextButton(text, click);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    /// The status line under the switch.
    private static string TaskbarStatusText(AddonStatus st) => st.State switch
    {
        AddonState.Dev => Tr("개발 복사본에서는 켜지 않습니다", "Not turned on in a development copy"),
        AddonState.NotShipped => Tr("아직 받지 않았습니다", "Not downloaded yet"),
        AddonState.Broken => Tr("작업표시줄 애드온 파일이 올바르지 않거나 더 새 Bloom이 필요합니다", "The taskbar add-on's files are not valid, or it needs a newer Bloom"),
        AddonState.Off => Tr("꺼짐", "Off"),
        AddonState.Skipped => Tr("Shift를 누른 채 시작해 이번에는 켜지 않았습니다", "Bloom started with Shift held, so it is off this time"),
        AddonState.Starting when st.Percent >= 0 => Tr($"켜는 중 · 기호 파일 받는 중 {st.Percent}%", $"Turning on · downloading symbol files {st.Percent}%"),
        AddonState.Starting => Tr("켜는 중", "Turning on"),
        AddonState.Running when st.Note != RunningNote.None => RunningNoteText(st.Note),
        AddonState.Running => Tr("작동 중", "Running"),
        AddonState.PdbMissing => PdbMissingText(),
        AddonState.PortCrashed => PortCrashedText(),
        AddonState.SafeMode => Tr("멈춤 · 탐색기가 연달아 멈춰 작업표시줄 꾸미기를 껐습니다", "Paused · Explorer kept crashing, so taskbar customization was turned off"),
        AddonState.NoSymbols => Tr("기호 없음 · 이 Windows 버전에는 필요한 작업표시줄 기호가 없습니다", "No symbols · this Windows version lacks the taskbar symbols it needs"),
        AddonState.Windhawk => Tr("Windhawk에서 켜져 있음 · Windhawk에서 PillTaskbar 모드를 끄세요. Windhawk를 쓰지 않으면 서비스를 수동 시작으로 바꾸고 Windhawk를 끄거나 지워도 됩니다",
            "On in Windhawk · turn the PillTaskbar mod off in Windhawk. If you don't use Windhawk, set its service to manual start and close or uninstall Windhawk instead"),
        AddonState.Blocked => Tr("보안 기능이 막음 · Windows 보안(스마트 앱 컨트롤)이 불러오기를 막았습니다", "Blocked by a security feature · Windows Security (Smart App Control) kept it from loading"),
        AddonState.Download => DownloadStatusText(),
        AddonState.Restart => Tr("탐색기 재시작 필요 · 탐색기를 다시 시작한 뒤 다시 시도하세요", "Explorer needs a restart · restart Explorer, then try again"),
        _ => Tr("멈춤 · 작업표시줄 꾸미기가 멈췄습니다", "Stopped · taskbar customization stopped"),
    };

    /// The taskbar add-on's first switch-on: a separate GPL program that loads code into Explorer and downloads symbol
    /// files (SetAddon asks it once).
    private bool ConfirmTaskbar(Addon addon) => ConfirmProgram(addon, Tr("작업표시줄 꾸미기 켜기", "Turn on taskbar customization"),
        Tr("PillTaskbar는 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 작업표시줄의 모양을 바꿉니다. 라이선스는 GPL-3.0입니다. 처음 켤 때 Microsoft 기호 서버에서 작업표시줄 기호 파일(약 70 MB)을 받습니다.",
            "PillTaskbar is a program separate from Bloom. It changes how the taskbar looks by loading code into Explorer, and is licensed under GPL-3.0. The first start downloads the taskbar's symbol files (about 70 MB) from the Microsoft symbol server."));

    /// The add-on's source repository: a web address only, never anything else the manifest might name.
    private static void OpenSource(AddonManifest m)
    {
        if (Uri.TryCreate(m.Source, UriKind.Absolute, out var u) && u.Scheme is "https" or "http") Launch.Open(u.AbsoluteUri);
    }
}
