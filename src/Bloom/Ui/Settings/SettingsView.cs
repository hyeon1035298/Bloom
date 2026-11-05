using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// Settings by category (in SettingsWindow): the categories in `Nav` on the left, one page of grouped cards here.
/// ↑↓ move, Enter switches, ←→ for choices, Ctrl+Tab / Ctrl+PgDn for the next category.
/// A filter shows the matching lines of every category, headed by their category.
internal sealed partial class SettingsView : ScrollViewer
{
    private readonly Host host;
    private readonly StackPanel stack = new() { Margin = new Thickness(0, 0, 0, 24) };
    public readonly StackPanel Nav = new() { Margin = new Thickness(0, 2, 0, 12) };
    private readonly List<(Row row, Action? toggle, Func<int, bool>? step)> rows = new();
    private readonly List<Dropdown> dropdowns = new();
    private readonly List<(string key, string title, char glyph, string section)> pages = new();
    private readonly Dictionary<string, int> hits = new();
    private readonly Dictionary<int, string> homeKeys = new(); // row index → home block (Ctrl+↑↓ moves it)
    private int sel;
    private static string? drawing; // "AI로 전부 그리기" while it runs
    private string lastFilter = "";
    private List<Ai.McpServerDef>? mcpFound; // 설정 > PC 작업 · MCP: the servers found on this PC, read once a visit

    /// Collect mode (SettingsCatalog): the lines the pages add are recorded here as text instead of built as rows.
    private List<SettingsCatalog.Line>? collected;

    /// The open category; kept for the next time the window opens.
    private static string current = "general";

    // where Build is: the sidebar section, the category and group the next lines go into
    private string section = "", pageKey = "", pageTitle = "", groupTitle = "";
    private bool pageShown;
    private StackPanel? card;

    /// Clears the window's filter line (a category picked while filtering).
    public Action? ClearFilter;

    /// Puts the keyboard back in the window's filter line (after a number field is done).
    public Action? FocusFilter;

    public SettingsView(Host host)
    {
        this.host = host;
        Content = stack;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        Template = Launcher.ThinScroll();
        // 업데이트 확인's line and the AI accounts' sign-in lines follow while the window is open
        Loaded += (_, _) =>
        {
            Updates.Changed += OnUpdates;
            UsageLogin.Changed += OnUpdates;
            GptCatalog.Updated += OnCatalog;
            ApiCatalog.Updated += OnCatalog;
            OmpCatalog.Updated += OnCatalog;
            Addons.AddonHub.Changed += OnAddons;
        };
        Unloaded += (_, _) =>
        {
            Updates.Changed -= OnUpdates;
            UsageLogin.Changed -= OnUpdates;
            GptCatalog.Updated -= OnCatalog;
            ApiCatalog.Updated -= OnCatalog;
            OmpCatalog.Updated -= OnCatalog;
            Addons.AddonHub.Changed -= OnAddons;
        };
    }

    private void OnUpdates() => Dispatcher.BeginInvoke(() => Build(lastFilter));

    private void OnCatalog()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && IsVisible && (current is "ai" or "accounts" || lastFilter.Length > 0)) Build(lastFilter);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// An add-on's status moved (the host started, the symbol files came in, the taskbar's folders changed): its page
    /// follows while it shows, where it is scrolled to, and not under a field being typed in (the redraw would drop it).
    private void OnAddons()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is TextBox box && IsAncestorOf(box)) return;
            if (IsLoaded && IsVisible && (current is "addons" || lastFilter.Length > 0)) TaskbarRebuild();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// A line: its title, a faint line under it, its control at the end, and no icon (the sidebar carries the page's).
    /// Its height grows with a second line under it. `folded`: a match from under a 자세히 fold, marked so after its title.
    private static Row Line(string title, string? sub, FrameworkElement? control, bool folded = false)
    {
        var r = new Row(null) { Height = double.NaN, MinHeight = 56, Margin = new Thickness(0), Padding = new Thickness(0, 9, 12, 9), Selectable = false };
        FrameworkElement Title()
        {
            var t = Theme.T(title, 13, Theme.P.Text, Theme.Medium);
            if (!folded) return t;
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(t);
            var more = Theme.T(Tr("자세히", "More"), 11, Theme.P.TextFaint);
            more.Margin = new Thickness(8, 1, 0, 0);
            more.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(more);
            return line;
        }
        if (sub is null)
        {
            r.Add(folded ? Title() : Theme.Label(title, 13, Theme.P.Text, Theme.Medium, true), new GridLength(1, GridUnitType.Star));
        }
        else
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Title());
            // up to two lines next to a wide control (a long line was cut at one), Korean words kept whole
            var s = Theme.T(Hangul.KeepAll(sub), 12, Theme.P.TextFaint);
            s.TextWrapping = TextWrapping.Wrap;
            s.MaxHeight = Math.Ceiling(s.FontSize * s.FontFamily.LineSpacing * 2) + 1;
            s.Margin = new Thickness(0, 2, 0, 0);
            s.ToolTip = sub.Length > 40 ? sub : null;
            text.Children.Add(s);
            r.Add(text, new GridLength(1, GridUnitType.Star));
        }
        if (control is not null)
        {
            control.Margin = new Thickness(16, 0, 0, 0);
            r.Add(control, GridLength.Auto);
        }
        return r;
    }

    /// A category: a title and one line under it on its page, an entry in the list on the left.
    private void Page(string key, string title, char glyph, string blurb, string filter)
    {
        pages.Add((key, title, glyph, section));
        pageKey = key;
        pageTitle = title;
        groupTitle = "";
        card = null;
        pageShown = false;
        groupShown = false;
        if (collected is null && filter.Length == 0 && key == current)
        {
            var t = Theme.T(title, 15, Theme.P.Text, Theme.Semi);
            t.Margin = new Thickness(32, 24, 32, 0);
            stack.Children.Add(t);
            var b = Theme.T(Hangul.KeepAll(blurb), 13, Theme.P.TextDim, Theme.Regular, false);
            b.TextWrapping = TextWrapping.Wrap;
            b.MaxWidth = 600; // a readable measure on a wide window
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(32, 4, 32, 0);
            stack.Children.Add(b);
            pageShown = true;
        }
    }

    private bool groupShown; // the page has a group on it already (the first sits closer to the page's title)
    private string? groupNote; // a line under the group's title
    private (string text, Action click)? groupButton; // a text button at the end of the group's title

    /// A group of lines inside the category: a small title over one card; `note` a line under the title, `button` a
    /// text button beside it (미리 보기, 목록 다시 읽기).
    private void Header(string title, string? note = null, (string text, Action click)? button = null)
    {
        groupTitle = title;
        groupNote = note;
        groupButton = button;
        card = null;
    }

    /// The card the next line goes into (made on the first line, so empty groups leave nothing).
    private StackPanel Card(string filter)
    {
        if (card is not null) return card;
        if (filter.Length > 0 && !pageShown)
        {
            var t = Theme.T(pageTitle, 15, Theme.P.Text, Theme.Semi);
            t.Margin = new Thickness(32, stack.Children.Count == 0 ? 22 : 26, 28, 0);
            stack.Children.Add(t);
            pageShown = true;
        }
        double top = groupShown ? 28 : 20;
        groupShown = true;
        if (groupTitle.Length > 0)
        {
            var g = Theme.T(groupTitle, 13, Theme.P.Text, Theme.Semi);
            g.VerticalAlignment = VerticalAlignment.Center;
            var head = new DockPanel { Margin = new Thickness(32, top, 32, groupNote is null ? 10 : 2), LastChildFill = true };
            if (groupButton is { } button)
            {
                var b = ToolUi.TextButton(button.text, button.click);
                b.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(b, Dock.Right);
                head.Children.Add(b);
            }
            head.Children.Add(g);
            stack.Children.Add(head);
            if (groupNote is not null)
            {
                var n = Theme.T(Hangul.KeepAll(groupNote), 12, Theme.P.TextFaint);
                n.TextWrapping = TextWrapping.Wrap;
                n.Margin = new Thickness(32, 0, 32, 10);
                stack.Children.Add(n);
            }
        }
        // a faint card with no outline; its lines inset by its padding so a chosen line's fill keeps clear of its corners
        card = new StackPanel();
        stack.Children.Add(new SmoothBorder
        {
            Background = Theme.P.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Margin = new Thickness(32, groupTitle.Length > 0 ? 0 : top, 32, 0),
            Child = card,
        });
        return card;
    }

    /// A line (when it shows: the open category, or a match while filtering) → its row, else null. `glyph` is not drawn
    /// since the lines lost their icons; the pages still give one per line.
    private Row? Add(string title, string? sub, FrameworkElement? control, char glyph, Action? toggle, Func<int, bool>? step, string filter) =>
        Place(title, sub, () => Line(title, sub, control, folding && filter.Length > 0), toggle, step, filter);

    // ── 자세히 · N개 ──
    /// The folds open now (page/fold), kept while this window is open; a new window starts with all of them folded.
    private readonly HashSet<string> unfolded = new();
    private bool folding; // the lines being added are a fold's
    private string? nextTip; // a tooltip for the next line (Tip)

    /// The defaults a fold compares the values with (바꾼 값 N개).
    private static readonly Settings Defaults = new();

    /// A tooltip on the next line placed (its numbers, what the line under it leaves out).
    private void Tip(string text) => nextTip = text;

    /// 자세히 · N개: the rarely changed lines of a card, folded under a last line that opens and closes them; folded, it
    /// counts the ones whose value is not the default. Filtering and collecting ignore the fold (every line is found, and
    /// a match is marked 자세히). `lines`: whether the value is changed, and what adds the line.
    private void More(string id, string filter, List<(bool changed, Action add)> lines)
    {
        if (lines.Count == 0) return;
        bool all = collected is not null || filter.Length > 0;
        string key = pageKey + "/" + id;
        bool open = all || unfolded.Contains(key);
        if (!all)
        {
            int changed = lines.Count(l => l.changed);
            Disclose(Tr($"자세히 · {lines.Count}개", $"More · {lines.Count}"), !open && changed > 0 ? Tr($"바꾼 값 {changed}개", $"{changed} changed") : null, open, () =>
            {
                if (!unfolded.Remove(key)) unfolded.Add(key);
                TaskbarRebuild();
            }, filter);
        }
        if (!open) return;
        folding = true;
        foreach (var (_, add) in lines) add();
        folding = false;
    }

    /// A feature switched off on 기능 takes its lines elsewhere with it: one line in their place says so, and Enter opens
    /// 기능. Off only when every one of `keys` is off. True: the caller adds nothing more for it.
    private bool FeatureOff(string filter, params string[] keys)
    {
        if (keys.Any(Features.On)) return false;
        string names = string.Join(", ", keys.Select(k => Features.All.First(f => f.key == k).name));
        Add(names, Tr("꺼진 기능 · 기능에서 켜기", "Off · turn it on in Features"), null, G.Sliders, () => Go("features"), null, filter);
        return true;
    }

    /// A line and a row of keys under it (사용할 대화), each on or off on its own; while filtering or collecting, the line
    /// under the title names the keys too, so a key's name finds it.
    private void KeyChips(string title, string sub, List<(string label, bool on, Action<bool> set)> chips, char glyph, string filter)
    {
        string names = string.Join(", ", chips.Select(c => c.label));
        if (Add(title, collected is null && filter.Length == 0 ? sub : $"{sub} · {names}", null, glyph, null, null, filter) is null) return;
        card?.Children.Add(Chips(chips));
    }

    /// 자세히 · N개: a card's last line, opening the lines folded under it (`flip`): a chevron (turned down when open), the
    /// words, and `note` after them (a summary of the folded values, or what the open lines are).
    private void Disclose(string title, string? note, bool open, Action flip, string filter) =>
        Place(title, note, () =>
        {
            var r = new Row(null) { Height = double.NaN, MinHeight = 44, Margin = new Thickness(0), Padding = new Thickness(0, 6, 12, 6), Selectable = false, Cursor = Cursors.Hand };
            var chevron = Theme.Glyph(open ? G.Down : G.Right, 10, Theme.P.TextDim);
            chevron.Margin = new Thickness(0, 1, 6, 0);
            r.Add(chevron, GridLength.Auto);
            r.Add(Theme.Label(title, 12, Theme.P.TextDim, Theme.Medium), GridLength.Auto);
            var n = Theme.T(note ?? "", 12, Theme.P.TextFaint);
            n.Margin = new Thickness(12, 0, 0, 0);
            n.ToolTip = note is { Length: > 60 } ? note : null;
            r.Add(n, new GridLength(1, GridUnitType.Star));
            return r;
        }, flip, null, filter);

    /// A line made by `make`, placed like Add's: recorded while collecting, matched while filtering, then into the card.
    private Row? Place(string title, string? sub, Func<Row> make, Action? toggle, Func<int, bool>? step, string filter)
    {
        string? tip = nextTip;
        nextTip = null;
        if (collected is not null)
        {
            collected.Add(new SettingsCatalog.Line(title, sub ?? "", groupTitle, pageKey, pageTitle));
            return null;
        }
        if (filter.Length > 0)
        {
            var q = new Query(filter);
            if (Matcher.Score(q, new MatchTarget(title, new[] { sub ?? "", groupTitle, pageTitle })) <= 0) return null;
            hits[pageKey] = hits.GetValueOrDefault(pageKey) + 1;
        }
        else if (pageKey != current) return null;
        var r = make();
        if (tip is not null) r.ToolTip = tip;
        int index = rows.Count;
        r.Clicked = () =>
        {
            Select(index);
            toggle?.Invoke();
        };
        rows.Add((r, toggle, step));
        var into = Card(filter);
        if (into.Children.Count > 0) into.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Rule });
        into.Children.Add(r);
        return r;
    }

    /// Collect mode: records the line and tells the helper not to build its control (the helpers that end in Add).
    private bool Record(string title, string? sub, string filter)
    {
        if (collected is null) return false;
        Add(title, sub, null, '\0', null, null, filter);
        return true;
    }

    private static Switch Toggle(bool value, Action<bool> set, out Action toggle)
    {
        var sw = new Switch(value);
        toggle = () =>
        {
            sw.On = !sw.On;
            set(sw.On);
            Settings.Save();
        };
        return sw;
    }

    /// A segmented choice: click or ←→ picks, Enter goes round. Past four options, a list that opens under the value (Menu).
    private void Choice(string title, string? sub, char glyph, (string value, string label)[] options, string value, Action<string> set, string filter)
    {
        if (options.Length > 4)
        {
            Menu(title, sub, glyph, options.Select(o => (o.value, o.label, (string?)null)).ToArray(), value, set, filter);
            return;
        }
        if (Record(title, sub, filter)) return;
        var seg = new Segmented(options, value);
        void Pick(string v)
        {
            set(v);
            Settings.Save();
        }
        seg.Picked += Pick;
        Add(title, sub, seg, glyph, () => Pick(seg.Cycle()), d =>
        {
            Pick(seg.Step(d));
            return true;
        }, filter);
    }

    /// A value along a track (LevelSlider): dragging sets it as it moves, letting go saves it; ←→ step it by `step`.
    private void Level(string title, string? sub, char glyph, int value, int min, int max, int step, Func<int, string> label, Action<int> set, string filter)
    {
        if (Record(title, sub, filter)) return;
        var slider = new LevelSlider(value, min, max, label);
        slider.Moved += set;
        slider.Picked += _ => Settings.Save();
        Add(title, sub, slider, glyph, null, d =>
        {
            set(slider.Step(d * step));
            Settings.Save();
            return true;
        }, filter);
    }

    /// A global shortcut, pressed in (KeyRecorder): the row's Enter or a click listens for it; the line under it says
    /// whether Windows gave it to us.
    private void Shortcut(string name, string title, string what, char glyph, string value, Action<string> set, string filter)
    {
        string sub = host.Shortcuts?.Status(name) switch
        {
            "taken" => Tr("다른 앱이 이미 쓰고 있습니다 — 다른 조합을 누르세요", "Another app already uses it — press another combination"),
            _ when value != "off" => what, // "ok", or set but not registered (its feature is off)
            _ => Tr("꺼짐 · 클릭하여 단축키 지정", "Off — click to set a shortcut"),
        };
        if (Record(title, sub, filter)) return;
        var keys = new KeyRecorder(value)
        {
            Listening = on => host.Shortcuts?.Pause(on),
            Done = () => FocusFilter?.Invoke(),
            Check = combo => host.Shortcuts?.Owner(combo) is { } owner && owner != name ? Tr("Bloom에서 사용 중인 키", "Bloom already uses it") : null,
        };
        keys.Picked += v =>
        {
            set(v);
            Settings.Save();
            host.ApplyHotkeys();
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the status line under it
        };
        Add(title, sub, keys, glyph, keys.Start, null, filter);
    }

    /// A length of time on its row (보관 기간, 쉬는 대화 닫기): DurationField, the row's Enter (or a click) puts the caret
    /// in it; the line under it (`sub` of the value) follows a change.
    private void Duration(string title, Func<int, string> sub, char glyph, int value, int max, (string key, string label, int minutes)[] units, Action<int> set, string filter)
    {
        if (Record(title, sub(value), filter)) return;
        var field = new DurationField(value, max, units) { Done = () => FocusFilter?.Invoke() };
        field.Changed += v =>
        {
            set(v);
            Settings.Save();
            Dispatcher.BeginInvoke(() => Build(lastFilter));
        };
        Add(title, sub(value), field, glyph, field.FocusBox, null, filter);
    }

    /// A line of text on its row (a model name, a program's path): TextField; empty = the default.
    private void Field(string title, string? sub, char glyph, string? value, string hint, Action<string> set, string filter, Func<string, string?>? check = null)
    {
        if (Record(title, sub, filter)) return;
        var field = new TextField(value, hint) { Done = () => FocusFilter?.Invoke(), Check = check };
        field.Applied += v =>
        {
            set(v);
            Settings.Save();
            Dispatcher.BeginInvoke(() => Build(lastFilter));
        };
        Add(title, sub, field, glyph, field.FocusBox, null, filter);
    }

    /// A choice from a list that opens under the value (many options): click or Enter opens it, ←→ step.
    private void Menu(string title, string? sub, char glyph, (string value, string label, string? sub)[] options, string value, Action<string> set, string filter)
    {
        if (Record(title, sub, filter)) return;
        var dd = new Dropdown(options, value);
        void Pick(string v)
        {
            set(v);
            Settings.Save();
        }
        dd.Picked += Pick;
        dropdowns.Add(dd);
        Add(title, sub, dd, glyph, dd.Open, d =>
        {
            Pick(dd.Step(d));
            return true;
        }, filter);
    }

    public void Build(string filter)
    {
        filter = filter.Trim();
        int keep = filter == lastFilter ? sel : 0;
        lastFilter = filter;
        foreach (var dd in dropdowns) dd.Close();
        dropdowns.Clear();
        stack.Children.Clear();
        rows.Clear();
        homeKeys.Clear();
        homeRows.Clear();
        pages.Clear();
        hits.Clear();
        BuildPages(Settings.Current, filter);

        if (filter.Length > 0 && rows.Count == 0)
        {
            var none = Theme.T(Tr("검색된 설정 없음", "No settings found"), 13, Theme.P.TextFaint);
            none.Margin = new Thickness(32, 26, 28, 0);
            stack.Children.Add(none);
        }
        BuildNav(filter);
        if (keep == 0) ScrollToTop();
        Select(Math.Min(keep, rows.Count - 1));
    }

    /// Every category in the sidebar's three sections, 데이터 and 정보 on their own at the end (one source of the lines,
    /// for Build and Collect).
    private void BuildPages(Settings s, string filter)
    {
        section = Tr("기본", "Basics");
        PageGeneral(s, filter);
        PageLauncher(s, filter);
        PageFeatures(s, filter);
        PageShortcuts(s, filter);
        section = "AI";
        PageAi(s, filter);
        PageAccounts(s, filter);
        PageMcp(s, filter);
        PageNotices(s, filter);
        section = Tr("도구", "Tools");
        PageTools(s, filter);
        PageAddons(s, filter);
        section = "";
        PageData(s, filter);
        PageAbout(s, filter);
    }

    /// The page a key of an older layout went to (a search line, a notice or a QA list may still name one).
    internal static string PageOf(string key) => key switch
    {
        "performance" => "general",
        "home" or "search" => "launcher",
        "clipboard" or "screen" or "lens" or "player" => "tools",
        "taskbar" => "addons",
        _ => key,
    };

    /// Every line of every category as text, for the launcher's settings search (SettingsCatalog). Nothing is built;
    /// the pages skip what reads the disk or the PC (MCP servers, Lens files, CLIs) while this runs.
    public List<SettingsCatalog.Line> Collect() => Collect(BuildPages);

    /// The tests: 설정 > 애드온's lines alone (the other pages want a started Host), for `taskbar` (a hand-made one) or the
    /// shipped one, and `users` (hand-made ones, none by default) in place of the user's.
    internal List<SettingsCatalog.Line> CollectAddonsForTests(Addons.Addon? taskbar = null, IReadOnlyList<Addons.Addon>? users = null)
    {
        taskbarAddon = taskbar;
        userAddons = users ?? Array.Empty<Addons.Addon>();
        return Collect(PageAddons);
    }

    private List<SettingsCatalog.Line> Collect(Action<Settings, string> build)
    {
        collected = new();
        build(Settings.Current, "");
        return collected;
    }

    /// The categories on the left under their section names (a hairline before the one with none): the open one on a
    /// fill in the plain text colour; while filtering, how many lines each has.
    private void BuildNav(string filter)
    {
        Nav.Children.Clear();
        string? last = null;
        foreach (var (key, title, glyph, sec) in pages)
        {
            if (sec != last)
            {
                if (sec.Length == 0) Nav.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Line, Margin = new Thickness(20, 8, 20, 8) });
                else
                {
                    var head = Theme.T(sec, 12, Theme.P.TextFaint, Theme.Regular, false);
                    head.Margin = new Thickness(20, last is null ? 4 : 12, 12, 4);
                    Nav.Children.Add(head);
                }
                last = sec;
            }
            bool on = filter.Length == 0 && key == current;
            int count = hits.GetValueOrDefault(key);
            bool dim = filter.Length > 0 && count == 0;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Theme.Glyph(glyph, 14, on ? Theme.P.Text : Theme.P.TextDim);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            var t = Theme.Label(title, 13, on ? Theme.P.Text : Theme.P.TextDim, on ? Theme.Semi : Theme.Regular, true);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            if (filter.Length > 0 && count > 0)
            {
                var c = Theme.Label(count.ToString(), 12, Theme.P.TextFaint, Theme.Medium);
                Grid.SetColumn(c, 2);
                g.Children.Add(c);
            }
            var item = new SmoothBorder
            {
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 1, 8, 1),
                Padding = new Thickness(12, 0, 12, 0),
                Background = on ? Theme.P.Selected : System.Windows.Media.Brushes.Transparent,
                Opacity = dim ? 0.45 : 1,
                Cursor = Cursors.Hand,
                Child = g,
            };
            if (!on)
            {
                item.MouseEnter += (_, _) =>
                {
                    item.Background = Theme.P.Hover;
                    icon.Foreground = t.Foreground = Theme.P.Text;
                };
                item.MouseLeave += (_, _) =>
                {
                    item.Background = System.Windows.Media.Brushes.Transparent;
                    icon.Foreground = t.Foreground = Theme.P.TextDim;
                };
            }
            string k = key;
            item.MouseLeftButtonUp += (_, e) =>
            {
                Go(k);
                e.Handled = true;
            };
            Nav.Children.Add(item);
        }
    }

    /// Opens a category (clears the filter first).
    public void Go(string key)
    {
        current = PageOf(key);
        sel = 0;
        if (lastFilter.Length > 0 && ClearFilter is not null) ClearFilter(); // rebuilds through the filter line
        else
        {
            lastFilter = "\0"; // a new page starts at its first line
            Build("");
        }
    }

    private void Step(int d)
    {
        if (pages.Count == 0) return;
        int i = pages.FindIndex(p => p.key == current);
        Go(pages[((i < 0 ? 0 : i) + d + pages.Count) % pages.Count].key);
    }

    // ── dragging a home block in the list ──
    private readonly List<(Row row, string key)> homeRows = new();
    private Row? dragRow;
    private string? dragKey;
    private Point dragFrom;
    private bool dragging, justDragged;
    private int dropAt = -1;

    private void WireDrag(Row r, string key)
    {
        homeRows.Add((r, key));
        r.Cursor = Cursors.SizeNS;
        r.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside<Switch>(d)) return; // the switch stays a switch
            dragRow = r;
            dragKey = key;
            dragFrom = e.GetPosition(this);
            dragging = false;
        };
        r.PreviewMouseMove += (_, e) =>
        {
            if (dragRow != r || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (!dragging && Math.Abs(p.Y - dragFrom.Y) < 6) return;
            if (!dragging)
            {
                dragging = true;
                r.Opacity = 0.55;
                r.CaptureMouse();
            }
            ShowDrop(p);
        };
        r.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (dragRow != r) return;
            bool moved = dragging;
            r.ReleaseMouseCapture();
            r.Opacity = 1;
            int at = dropAt;
            string? k = dragKey;
            ClearDrop();
            dragRow = null;
            dragging = false;
            if (!moved || k is null || at < 0) return;
            justDragged = true;
            Home.MoveTo(k, at);
            Dispatcher.BeginInvoke(() =>
            {
                Build(lastFilter);
                if (homeKeys.FirstOrDefault(kv => kv.Value == k) is { Value: not null } now) Select(now.Key);
                host.Window.Refresh(true);
                justDragged = false;
            });
        };
    }

    /// The gap the block would land in: a rose line on the top of the row it goes before (bottom of the last).
    private void ShowDrop(Point p)
    {
        ClearDrop();
        dropAt = homeRows.Count;
        for (int i = 0; i < homeRows.Count; i++)
        {
            var row = homeRows[i].row;
            double mid = row.TranslatePoint(new Point(0, row.ActualHeight / 2), this).Y;
            if (p.Y < mid)
            {
                dropAt = i;
                break;
            }
        }
        var target = dropAt < homeRows.Count ? homeRows[dropAt].row : homeRows[^1].row;
        target.BorderBrush = Theme.P.Accent;
        target.BorderThickness = dropAt < homeRows.Count ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
    }

    private void ClearDrop()
    {
        foreach (var (row, _) in homeRows) row.BorderThickness = new Thickness(0);
    }

    private static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            if (x is T) return true;
        return false;
    }

    /// QA: a drag from one block to before another, as the mouse would.
    public void DragHomeForQa(string key, int before)
    {
        Home.MoveTo(key, before);
        Build(lastFilter);
    }

    private void MoveHome(string key, int d)
    {
        Home.Move(key, d);
        Build(lastFilter);
        if (homeKeys.FirstOrDefault(kv => kv.Value == key) is { Value: not null } at) Select(at.Key);
        host.Window.Refresh(true);
    }

    private static FrameworkElement Arrow(char glyph, string tip, Action click)
    {
        var b = new SmoothBorder
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = Theme.Glyph(glyph, 11, Theme.P.TextDim),
        };
        ((FrameworkElement)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)b.Child).VerticalAlignment = VerticalAlignment.Center;
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = System.Windows.Media.Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    private static string LangName(string tag) => tag.Split('-')[0] switch
    {
        "ko" => Tr("한국어", "Korean"),
        "ja" => Tr("일본어", "Japanese"),
        "en" => Tr("영어", "English"),
        "zh" => Tr("중국어", "Chinese"),
        _ => tag,
    };

    private static FrameworkElement Status(bool ok)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = ok ? Theme.P.Ok : Theme.P.Track, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        return dot;
    }

    private void Select(int i)
    {
        if (rows.Count == 0) return;
        sel = Math.Clamp(i, 0, rows.Count - 1);
        for (int k = 0; k < rows.Count; k++) rows[k].row.Selected = k == sel;
        rows[sel].row.BringIntoView();
    }

    public bool HandleKey(Key key)
    {
        if (dropdowns.Find(d => d.IsOpen) is { } open) return open.HandleKey(key);
        if (Keyboard.FocusedElement is TextBox { Tag: Func<Key, bool> numberKey }) return numberKey(key); // typing in a number field
        if (Keyboard.FocusedElement is KeyRecorder { IsListening: true }) return true; // a shortcut being pressed in reads its own keys
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (key)
        {
            case Key.Tab when ctrl:
                Step(shift ? -1 : 1);
                return true;
            case Key.PageDown when ctrl:
                Step(1);
                return true;
            case Key.PageUp when ctrl:
                Step(-1);
                return true;
            case Key.Up when ctrl:
            case Key.Down when ctrl:
                if (homeKeys.TryGetValue(sel, out var hk)) MoveHome(hk, key == Key.Up ? -1 : 1);
                return true;
            case Key.Down:
                Select(sel + 1 >= rows.Count ? 0 : sel + 1);
                return true;
            case Key.Up:
                Select(sel - 1 < 0 ? rows.Count - 1 : sel - 1);
                return true;
            case Key.Enter:
                if (sel >= 0 && sel < rows.Count) rows[sel].toggle?.Invoke();
                return true;
            case Key.Left:
            case Key.Right:
                if (sel >= 0 && sel < rows.Count && rows[sel].step is { } step) return step(key == Key.Right ? 1 : -1);
                return false;
        }
        return false;
    }

    /// QA: open a category by key.
    public void GoForQa(string key) => Go(key);

    /// QA: selects a row (from the end when negative), which scrolls it into view.
    public void SelectForQa(int i) => Select(i < 0 ? rows.Count + i : i);
}
