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

/// 설정 > 애드온's taskbar add-on (PillTaskbar, a separate GPL-3.0 program Bloom ships and starts): its status text and
/// switch (the line itself is SettingsView.Addons'); its folders (TaskbarEditor), look, tray and auto-hide island, drawn
/// from the settings.schema.json it ships (names, defaults, ranges) and laid out by TaskbarMap; and what it is (version,
/// license, source).
internal sealed partial class SettingsView
{
    /// The folds open now (자세히 under a group), kept while Bloom runs.
    private static readonly HashSet<string> taskbarOpen = new();

    /// The folder editor's selection and half-done steps, kept across the page's rebuilds.
    private readonly TaskbarEditor.Edit taskbarEdit = new();

    /// The tests: a hand-made add-on in place of the shipped one.
    private Addon? taskbarAddon;

    /// Under the list on 설정 > 애드온 while the taskbar add-on is on: its settings and what it is.
    private void TaskbarLines(Addon addon, AddonStatus status, string filter)
    {
        // the add-on's own settings: only with the schema it ships (its names and ranges are not Bloom's to guess)
        if (addon.Schema is { } schema && addon.Values is { } values)
        {
            TaskbarFolders(addon, schema, values, status, filter);
            TaskbarLook(schema, values, filter);
            TaskbarTray(schema, values, filter);
            TaskbarIsland(schema, values, filter);
        }

        Header(Tr("PillTaskbar 정보", "About PillTaskbar"));
        if (addon.Manifest is { } m)
            Add($"PillTaskbar {m.Version}", Tr($"PillTaskbar는 GPL-3.0인 별도 프로그램입니다 · {m.License}", $"PillTaskbar is a separate GPL-3.0 program · {m.License}"),
                RowButton(Tr("소스 보기", "View source"), () => OpenSource(m)), G.Info, () => OpenSource(m), null, filter);
    }

    // ── 폴더 ─────────────────────────────────────────────────────────────────────────────────────
    private void TaskbarFolders(Addon addon, AddonSchema schema, AddonValues v, AddonStatus status, string filter)
    {
        Header(Tr("작업표시줄 폴더", "Taskbar folders"));
        bool on = v.Bool("folders.enabled");
        SchemaRow(schema, v, "folders.enabled", G.Folder, filter, sub: on
            ? Tr("작업표시줄에서 앱을 다른 앱 위에 끌어 잠깐 두어도 폴더가 됩니다", "Dragging an app onto another on the taskbar and holding it there briefly makes a folder too")
            : Tr("꺼짐 · 켜면 고정한 앱을 폴더로 묶습니다", "Off · when on, pinned apps can be grouped into folders"), rebuild: true);
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
            Add(Tr("폴더 편집", "Edit folders"), Tr("폴더를 만들고 이름, 아이콘, 안의 앱을 바꿉니다", "Make folders and change their names, icons and apps"), null, G.Folder, () => Go("addons"), null, filter);
            foreach (var e in In(Section.Folders).Where(e => e.Control == Control.Running))
                if (schema[e.Key] is { } item) Add(item.Split().name, item.About?.Shown, null, G.Folder, () => Go("addons"), null, filter);
        }
        Fold("folders", Section.FoldersMore, schema, v, filter);
    }

    // ── 모양 ─────────────────────────────────────────────────────────────────────────────────────
    private void TaskbarLook(AddonSchema schema, AddonValues v, string filter)
    {
        Header(Tr("작업표시줄 모양", "Taskbar look"));
        string size = SizeNow(v);
        var sizes = Sizes.Select(p => (p.key, p.key switch { "s" => Tr("작게", "Small"), "m" => Tr("보통", "Medium"), _ => Tr("크게", "Large") })).ToList();
        if (size == "custom") sizes.Add(("custom", Tr("직접", "Custom")));
        Choice(Tr("크기", "Size"), Tr("작업표시줄 높이와 아이콘 크기를 함께 바꿉니다", "Changes the taskbar's height and icon size together"), G.Screen, sizes.ToArray(), size, k =>
        {
            if (Sizes.FirstOrDefault(p => p.key == k) is not { key: not null } p) return;
            v.Set("size.enabled", true);
            v.Set("size.taskbarHeight", p.height);
            v.Set("size.iconSize", p.icon);
            v.Set("size.iconSpacing", p.spacing);
            Dispatcher.BeginInvoke(TaskbarRebuild); // 직접 goes, and 자세히's numbers follow
        }, filter);
        SchemaRow(schema, v, "pill.style", G.Palette, filter, Tr("배경", "Background"),
            Tr("알약 뒤를 흐리게 비춥니다. 빛 받는 유리는 가장자리에 빛과 반사광이 더해집니다", "Blurs what is behind the pill. Lit glass adds light and a sheen at the edges"));
        SchemaRow(schema, v, "start.mode", G.Power, filter, sub: Tr("꽃 버튼은 누르면 Bloom을 열고 닫습니다", "The flower button opens and closes Bloom"));
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
        IconChips(schema, v, Section.Tray, Tr("보일 아이콘", "Icons shown"), Tr("켠 것만 트레이에 남고, 끈 아이콘은 자리도 남기지 않습니다", "Only the ones on stay in the tray; the ones off leave no gap"), filter);
        SchemaRow(schema, v, "tray.clock", G.Clock, filter);
        SchemaRow(schema, v, "media.enabled", G.Volume, filter, Tr("재생 중인 곡", "Now playing"),
            Tr("앨범 아트, 제목, 이전·재생·다음 단추를 트레이 앞에 둡니다", "Puts the album art, the title and previous, play and next buttons before the tray"));
        SchemaRow(schema, v, "usage.enabled", G.Speed, filter, Tr("AI 사용량", "AI usage"),
            Tr("계정마다 고른 값 하나를 막대로 보입니다. 숨김 아일랜드에도 같은 값이 나옵니다", "Shows one picked value per account as a bar. The auto-hide island shows the same"), rebuild: true);
        // the add-on reads the usage file Bloom writes (설정 > 홈 화면 > AI 사용량), which is off by default
        if (collected is null && v.Bool("usage.enabled") && !UsageExport.On)
        {
            void Export()
            {
                UsageExport.Set(true);
                Settings.Save();
                TaskbarRebuild();
            }
            Add(Tr("사용량 파일 내보내기 꺼짐", "Usage file export is off"), Tr("작업표시줄은 Bloom이 내보낸 사용량 파일을 읽습니다. 켜야 사용량이 보입니다", "The taskbar reads the usage file Bloom exports; turn it on to see the usage"),
                RowButton(Tr("켜기", "Turn on"), Export), G.Warning, Export, null, filter);
        }
        // shared with the home cards (SettingsView.Usage): each account's bar, 이름표, 아이콘 색
        if (collected is not null || v.Bool("usage.enabled"))
        {
            UsageBarRows(filter);
            UsageTagRow(filter);
            UsageLogoRow(v, Tr("홈 카드와 같은 설정입니다", "The same setting as the home cards"), filter);
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
        foreach (var e in In(section))
        {
            if (schema[e.Key] is not { } item) continue;
            string key = e.Key;
            if (e.Control == Control.Shown) chips.Add((ChipName(item), !v.Bool(key), on => v.Set(key, !on)));
            else if (e.Control == Control.Bell) chips.Add((ChipName(item), v.Text(key) != "hidden", on => v.Set(key, on ? "shown" : "hidden")));
            else if (e.Control == Control.Chip) chips.Add((ChipName(item), v.Bool(key), on => v.Set(key, on)));
        }
        if (chips.Count == 0) return;
        // searched and collected by the chips' names too ("볼륨" finds this line)
        string names = string.Join(", ", chips.Select(c => c.label));
        bool normal = collected is null && filter.Length == 0;
        Add(title, normal ? sub : sub is null ? names : $"{sub} · {names}", null, section is Section.Island ? G.Screen : G.View, null, null, filter);
        Block(Chips(chips), filter, false);
    }

    /// A chip's label from the setting's name: "볼륨 아이콘 숨기기" / "Hide volume icon" → 볼륨 / Volume.
    internal static string ChipName(AddonSetting item)
    {
        string n = item.Name.Shown;
        foreach (string tail in new[] { " 아이콘 숨기기", " 숨기기", " 보이기", " icon" })
            if (n.EndsWith(tail, StringComparison.Ordinal)) n = n[..^tail.Length];
        foreach (string head in new[] { "Hide ", "Show " })
            if (n.StartsWith(head, StringComparison.Ordinal)) n = n[head.Length..];
        return n.Length > 0 ? char.ToUpper(n[0], CultureInfo.CurrentCulture) + n[1..] : n;
    }

    /// The chips as keys, under their line and lined up with its title.
    private static FrameworkElement Chips(List<(string label, bool on, Action<bool> set)> chips)
    {
        var wrap = new WrapPanel { Margin = new Thickness(13, 0, 12, 10) };
        foreach (var (label, on, set) in chips)
        {
            var key = new ToggleKey(label, on) { Margin = new Thickness(0, 0, 6, 6) };
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
        if (schema["island.enabled"] is not null)
        {
            // the island lives in the auto-hide helper and needs Windows' own auto-hide: all three go on and off
            // together (the host turns Windows' back on if it is off when the add-on starts)
            var sw = Toggle(v.Bool("island.enabled"), on =>
            {
                v.Set("island.enabled", on);
                v.Set("autohide.enabled", on);
                Native.SystemControl.SetTaskbarAutoHide(on);
            }, out var t);
            Add(Tr("숨김 아일랜드 쓰기", "Use the auto-hide island"), Tr("켜면 작업표시줄이 자동으로 숨고 화면 위 가운데에 아일랜드가 생깁니다", "The taskbar hides itself, and an island appears at the top centre of the screen"), sw, G.Screen, t, null, filter);
        }
        SchemaRow(schema, v, "island.reveal", G.Up, filter, sub: Tr("클릭이면 포인터를 대기만 해서는 나오지 않습니다", "With click, pointing at it alone doesn't bring it out"));
        IconChips(schema, v, Section.Island, Tr("아일랜드에 보일 것", "Shown on the island"), null, filter);
        SchemaRow(schema, v, "island.hoverTransparency", G.View, filter, Tr("포인터를 올리면", "With the pointer over it"),
            Tr("포인터가 있는 동안 아래가 비쳐 보입니다. 0이면 그대로, 100이면 다 비칩니다", "What is below shows through while the pointer is there: 0 not at all, 100 fully"));
        Fold("island", Section.IslandMore, schema, v, filter);
    }

    // ── the pieces ──────────────────────────────────────────────────────────────────────────────
    /// 자세히: a section's other settings, folded at first (always open while filtering or collecting, so they are found).
    /// `extra` adds `extras` lines of the page's own before the schema's.
    private void Fold(string id, Section section, AddonSchema schema, AddonValues v, string filter, string? openSub = null, int extras = 0, Action? extra = null)
    {
        var items = In(section).Where(e => e.Control is Control.Number or Control.Choice or Control.Switch or Control.Text && schema[e.Key] is not null).ToList();
        bool all = collected is not null || filter.Length > 0;
        bool open = all || taskbarOpen.Contains(id);
        if (!all)
        {
            void Flip()
            {
                if (!taskbarOpen.Remove(id)) taskbarOpen.Add(id);
                TaskbarRebuild();
            }
            int n = items.Count + extras;
            string summary = string.Join(" · ", items.Where(e => e.Control == Control.Number).Select(e => Summary(schema[e.Key]!, v)));
            Disclose(Tr($"자세히 · {n}개", $"More · {n}"), open ? openSub : summary.Length > 0 ? summary : null, open, Flip, filter);
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

    private static string Summary(AddonSetting item, AddonValues v)
    {
        var (name, unit) = item.Split();
        int n = v.Int(item.Key);
        return $"{name} {Special(item.Key, n) ?? Unit(n, unit)}";
    }

    private static string Unit(int n, string unit) => unit switch
    {
        "" => n.ToString(CultureInfo.CurrentCulture),
        "%" => $"{n}%",
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
    /// `sub` replace them where the page words a line its own way. `rebuild`: other lines follow the value.
    private void SchemaRow(AddonSchema schema, AddonValues v, string key, char glyph, string filter, string? title = null, string? sub = null, bool rebuild = false)
    {
        if (schema[key] is not { } item) return;
        var (name, unit) = item.Split();
        title ??= name;
        sub ??= item.About?.Shown;
        void Then()
        {
            if (rebuild) Dispatcher.BeginInvoke(TaskbarRebuild);
        }
        switch (item.Type)
        {
            case "bool":
                var sw = Toggle(v.Bool(key), on =>
                {
                    v.Set(key, on);
                    Then();
                }, out var t);
                Add(title, sub, sw, glyph, t, null, filter);
                break;
            case "choice":
                var options = item.Options.Select(o => (o.Value, parts: OptionLabel(key, o.Value) is { } own ? (name: own, detail: (string?)null) : OptionName(o.Name.Shown))).ToArray();
                // a few short names side by side; else a list, with what an option's name says in brackets under it
                if (options.Length <= 3 && options.All(o => o.parts.name.Length <= 16))
                    Choice(title, sub, glyph, options.Select(o => (o.Value, o.parts.name)).ToArray(), v.Text(key), x =>
                    {
                        v.Set(key, x);
                        Then();
                    }, filter);
                else
                    Menu(title, sub, glyph, options.Select(o => (o.Value, o.parts.name, o.parts.detail)).ToArray(), v.Text(key), x =>
                    {
                        v.Set(key, x);
                        Then();
                    }, filter);
                break;
            case "int":
                int min = item.Min ?? 0, max = item.Max ?? 100, def = AddonSchema.Int(item.Default) ?? min;
                if (max - min <= 200)
                {
                    Level(title, sub, glyph, Math.Clamp(v.Int(key), min, max), min, max, max - min >= 100 ? 5 : 1, n => Special(key, n) ?? Unit(n, unit), n => v.Set(key, n), filter);
                    break;
                }
                // a wide range (a height in pixels, a delay in ms): typed in, empty = the default; a "not set" number
                // shows as its words in the empty field
                string range = Tr($"{Unit(min, unit)}에서 {Unit(max, unit)}까지", $"{Unit(min, unit)} to {Unit(max, unit)}");
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
        AddonState.Running => Tr("작동 중", "Running"),
        AddonState.SafeMode => Tr("멈춤 · 탐색기가 연달아 멈춰 작업표시줄 꾸미기를 껐습니다", "Paused · Explorer kept crashing, so taskbar customization was turned off"),
        AddonState.NoSymbols => Tr("기호 없음 · 이 Windows 버전에는 필요한 작업표시줄 기호가 없습니다", "No symbols · this Windows version lacks the taskbar symbols it needs"),
        AddonState.Windhawk => Tr("Windhawk에서 켜져 있음 · Windhawk에서 PillTaskbar 모드를 끄세요. Windhawk를 쓰지 않으면 서비스를 수동 시작으로 바꾸고 Windhawk를 끄거나 지워도 됩니다",
            "On in Windhawk · turn the PillTaskbar mod off in Windhawk. If you don't use Windhawk, set its service to manual start and close or uninstall Windhawk instead"),
        AddonState.Blocked => Tr("보안 기능이 막음 · Windows 보안(스마트 앱 컨트롤)이 불러오기를 막았습니다", "Blocked by a security feature · Windows Security (Smart App Control) kept it from loading"),
        AddonState.Download => Tr("기호 파일을 받지 못했습니다 · 인터넷 연결을 확인하고 다시 시도하세요", "Could not download the symbol files · check your connection and try again"),
        AddonState.Restart => Tr("탐색기 재시작 필요 · 탐색기를 다시 시작한 뒤 다시 시도하세요", "Explorer needs a restart · restart Explorer, then try again"),
        _ => Tr("멈춤 · 작업표시줄 꾸미기가 멈췄습니다", "Stopped · taskbar customization stopped"),
    };

    /// The switch: the first time on, what the add-on is (a separate GPL program that loads code into Explorer and
    /// downloads symbol files) is said once and accepted; then the setting is saved and the host started or stopped.
    private void SetTaskbar(Addon addon, bool on, Switch sw)
    {
        var s = Settings.Current;
        if (on && !s.AddonsConfirmed.Contains(addon.Id))
        {
            if (!ConfirmTaskbar(addon))
            {
                sw.On = false;
                return;
            }
            s.AddonsConfirmed.Add(addon.Id);
        }
        _ = AddonHub.Set(addon, on);
        Build(lastFilter);
    }

    private bool ConfirmTaskbar(Addon addon)
    {
        var owner = Window.GetWindow(this);
        while (true)
        {
            var r = ConfirmWindow.Ask(owner, Tr("작업표시줄 꾸미기 켜기", "Turn on taskbar customization"),
                Tr("PillTaskbar는 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 작업표시줄의 모양을 바꿉니다. 라이선스는 GPL-3.0입니다. 처음 켤 때 Microsoft 기호 서버에서 작업표시줄 기호 파일(약 70 MB)을 받습니다.",
                    "PillTaskbar is a program separate from Bloom. It changes how the taskbar looks by loading code into Explorer, and is licensed under GPL-3.0. The first start downloads the taskbar's symbol files (about 70 MB) from the Microsoft symbol server."),
                (Tr("켜기", "Turn on"), MessageBoxResult.Yes), (Tr("소스 보기", "View source"), MessageBoxResult.No), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
            if (r != MessageBoxResult.No) return r == MessageBoxResult.Yes;
            if (addon.Manifest is { } m) OpenSource(m); // and the question stays
        }
    }

    /// The add-on's source repository: a web address only, never anything else the manifest might name.
    private static void OpenSource(AddonManifest m)
    {
        if (Uri.TryCreate(m.Source, UriKind.Absolute, out var u) && u.Scheme is "https" or "http") Launch.Open(u.AbsoluteUri);
    }
}
