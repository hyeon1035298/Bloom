using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온: every add-on with its switch and its host's status, and a page of its own under it in the list (the
/// line opens it). First the taskbar add-on (PillTaskbar), then the others the release Bloom's catalog pins
/// (AddonHub.Listed), each downloaded on its line (AddonDownload), then the ones the user put in Bloom's user-addons
/// folder themselves (AddonStore.User). Bloom draws settings for none but the taskbar add-on: on its page while it is on
/// (SettingsView.Taskbar).
internal sealed partial class SettingsView
{
    /// The tests: hand-made add-ons in place of the catalog's and the user's.
    private IReadOnlyList<Addon>? listedAddons, userAddons;

    /// One add-on as its line and its page show it: names, status now, its switch's action, and what its page holds
    /// below the head (null: nothing Bloom can change).
    private sealed record AddonView(Addon Addon, string Name, string About, char Glyph, AddonStatus Status, Func<AddonStatus, string> StatusText,
        Action<bool, Switch> Set, Action? Body);

    private void PageAddons(Settings s, string filter)
    {
        var taskbar = taskbarAddon ?? AddonHub.Taskbar;
        var listed = listedAddons ?? AddonHub.Listed.ToList();
        var users = userAddons ?? AddonHub.Users.ToList();
        Page("addons", Tr("애드온", "Add-ons"), G.Plug,
            Tr("애드온은 Bloom이 함께 실행하는 별도 프로그램이며, 여기서 하나씩 켜고 끕니다. 줄을 누르면 그 애드온의 페이지가 열립니다. Bloom의 로컬 데이터 폴더 안 user-addons에 직접 넣은 애드온은 Bloom을 다시 시작하면 보입니다.",
                "Add-ons are separate programs Bloom starts, each turned on and off here. Click one to open its page. One you put in the user-addons folder in Bloom's local data folder shows after Bloom restarts."), filter);
        Header(Tr("애드온 목록", "All add-ons"));
        var views = new List<AddonView>();

        string taskbarName = Tr("작업표시줄", "Taskbar"), taskbarAbout = Tr("작업표시줄을 가운데 알약 모양으로 바꿉니다", "Turns the taskbar into a centred pill");
        Action<bool, Switch> setTaskbar = (v, sw) => SetAddon(taskbar, v, sw, () => ConfirmTaskbar(taskbar));
        var status = AddonLine(s, taskbar, taskbarName, taskbarAbout, G.Screen, TaskbarStatusText, Tr("작업표시줄 꾸미기를 다시 시작합니다", "Starts taskbar customization again"),
            setTaskbar, filter);
        AddonDownload(taskbar, "PillTaskbar", Tr("작업표시줄 꾸미기를 껐다 켜면 새 버전으로 바뀝니다", "Turn taskbar customization off and on to switch to the new version"), status, filter);
        views.Add(new(taskbar, taskbarName, taskbarAbout, G.Screen, status, TaskbarStatusText, setTaskbar, () =>
        {
            if (s.AddonsOn.Contains(taskbar.Id)) TaskbarLines(taskbar, status, filter);
            else AddonNote(Tr("켜면 이 애드온의 설정이 여기에 보입니다", "Its settings show here while it is on"), filter);
        }));
        foreach (var addon in listed)
        {
            // named by the catalog, which has Bloom's languages and is here before the add-on's addon.json
            var entry = AddonCatalog.Find(addon.Id);
            string name = entry?.Title(Lang.Code) ?? addon.Manifest?.Title(Lang.Code) ?? addon.Id;
            string about = entry?.About(Lang.Code) ?? "";
            Action<bool, Switch> set = (v, sw) => SetAddon(addon, v, sw, () => ConfirmListed(addon, name));
            var st = AddonLine(s, addon, name, about, G.Plug, AddonStatusText, Tr("이 애드온을 다시 시작합니다", "Starts this add-on again"), set, filter);
            AddonDownload(addon, name, Tr("이 애드온을 껐다 켜면 새 버전으로 바뀝니다", "Turn this add-on off and on to switch to the new version"), st, filter);
            views.Add(new(addon, name, about, G.Plug, st, AddonStatusText, set, null));
        }
        foreach (var addon in users)
        {
            string name = addon.Manifest?.Title(Lang.Code) ?? addon.Id;
            string about = Tr("직접 넣은 애드온", "An add-on you added");
            Action<bool, Switch> set = (v, sw) => SetAddon(addon, v, sw, () => ConfirmUser(name));
            var st = AddonLine(s, addon, name, about, G.Plug, AddonStatusText, Tr("이 애드온을 다시 시작합니다", "Starts this add-on again"), set, filter);
            views.Add(new(addon, name, about, G.Plug, st, AddonStatusText, set, null));
        }
        foreach (var v in views) AddonPage(s, v, filter);
    }

    /// An add-on's page, under 애드온 in the list: a head with its name, status, version, source and switch; keys that
    /// jump to its groups when it has three or more; then its settings (`Body`), or a line saying it has none.
    private void AddonPage(Settings s, AddonView v, string filter)
    {
        string key = AddonPageKey(v.Addon.Id);
        Page(key, v.Name, v.Glyph, "", filter, "addons", v.Status.State == AddonState.Running ? Theme.P.Ok : null);
        int jumps = -1;
        if (collected is null && filter.Length == 0 && key == current)
        {
            Header("");
            Block(AddonHead(s, v), filter, false);
            jumps = stack.Children.Count;
        }
        if (v.Body is { } body) body();
        else AddonNote(Tr("이 애드온은 Bloom에서 바꿀 설정이 없습니다", "This add-on has no settings to change in Bloom"), filter);
        if (jumps >= 0 && heads.Count >= 3) stack.Children.Insert(jumps, Jumps(v.Name));
    }

    /// The head of an add-on's page: its tile, name, what it does, status · version · license, 소스 보기 and its switch.
    private FrameworkElement AddonHead(Settings s, AddonView v)
    {
        var g = new Grid { Margin = new Thickness(9, 9, 12, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = Theme.Glyph(v.Glyph, 20, Theme.P.TileFg);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(new SmoothBorder { Width = 44, Height = 44, CornerRadius = new CornerRadius(11), Background = Theme.P.Tile, Child = glyph });

        var text = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(v.Name, 15, Theme.P.Text, Theme.Semi));
        if (v.About.Length > 0)
        {
            var about = Theme.T(Hangul.KeepAll(v.About), 12, Theme.P.TextFaint);
            about.TextWrapping = TextWrapping.Wrap;
            about.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(about);
        }
        var line = Theme.T("", 12, Theme.P.TextFaint);
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 2, 0, 0);
        line.Inlines.Add(new System.Windows.Documents.Run(v.StatusText(v.Status)) { Foreground = v.Status.State == AddonState.Running ? Theme.P.Ok : Theme.P.TextDim });
        if (v.Addon.Manifest is { } m)
        {
            string product = m.Title(Lang.Code);
            string version = product == v.Name ? m.Version : $"{product} {m.Version}";
            line.Inlines.Add(new System.Windows.Documents.Run(m.License.Length > 0 ? $" · {version} · {m.License}" : $" · {version}"));
        }
        text.Children.Add(line);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        if (v.Addon.Manifest is { } man && man.Source.Length > 0)
        {
            var source = RowButton(Tr("소스 보기", "View source"), () => OpenSource(man));
            source.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(source, 2);
            g.Children.Add(source);
        }
        var sw = AddonSwitch(s, v.Addon, v.Status, v.Set, out _);
        sw.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sw, 3);
        g.Children.Add(sw);
        return g;
    }

    /// A line in a card of its own saying what the page has instead of settings (only on the open page; never found).
    private void AddonNote(string text, string filter)
    {
        Header("");
        var t = Theme.T(Hangul.KeepAll(text), 12.5, Theme.P.TextFaint);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(13, 13, 13, 13);
        Block(t, filter, false);
    }

    /// Keys that scroll the page to each of its groups, named without the add-on's name ("작업표시줄 폴더" → 폴더).
    private FrameworkElement Jumps(string name)
    {
        var wrap = new WrapPanel { Margin = new Thickness(32, 14, 32, -6) };
        foreach (var (title, head) in heads)
        {
            string label = title.StartsWith(name + " ", StringComparison.Ordinal) && title.Length > name.Length + 1 ? title[(name.Length + 1)..] : title;
            label = char.ToUpper(label[0], System.Globalization.CultureInfo.CurrentCulture) + label[1..];
            var t = Theme.Label(label, 12, Theme.P.TextDim, Theme.Medium, inBorder: true);
            var key = new SmoothBorder
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.P.Edge,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = t,
            };
            key.MouseEnter += (_, _) =>
            {
                key.Background = Theme.P.Card;
                t.Foreground = Theme.P.Text;
            };
            key.MouseLeave += (_, _) =>
            {
                key.Background = System.Windows.Media.Brushes.Transparent;
                t.Foreground = Theme.P.TextDim;
            };
            key.MouseLeftButtonUp += (_, e) =>
            {
                ScrollToVerticalOffset(Math.Max(0, head.TransformToAncestor(stack).Transform(new Point()).Y - 12));
                e.Handled = true;
            };
            wrap.Children.Add(key);
        }
        return wrap;
    }

    /// An add-on's line on 애드온: the picture of what it does at the start (AddonArt; its glyph large where Bloom has no
    /// picture of it), then its name, what it does and its status, and at the end its switch with the › to its page.
    private static Row AddonRow(string id, string title, string about, char glyph, AddonStatus status, string statusText, FrameworkElement end)
    {
        var r = new Row(null, double.NaN) { MinHeight = 100, Margin = new Thickness(0), Padding = new Thickness(0, 8, 12, 8), Selectable = false };

        // the picture moves while the line is on screen (AddonArt)
        FrameworkElement inner;
        if (AddonArt.For(id) is { } art)
        {
            inner = art;
            r.MouseEnter += (_, _) => art.Pointed = true;
            r.MouseLeave += (_, _) => art.Pointed = false;
        }
        else
        {
            var big = Theme.Glyph(glyph, 30, Theme.P.TextFaint);
            big.Opacity = 0.6;
            big.HorizontalAlignment = HorizontalAlignment.Center;
            big.VerticalAlignment = VerticalAlignment.Center;
            inner = big;
        }
        var panel = new SmoothBorder { Width = 232, Height = 84, CornerRadius = new CornerRadius(8), Background = Theme.P.Card, Child = inner };
        panel.Margin = new Thickness(-5, 0, 16, 0); // the picture nearer the card's edge than a line's words
        r.Add(panel, GridLength.Auto);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(title, 14, Theme.P.Text, Theme.Semi));
        if (about.Length > 0)
        {
            var a = Theme.T(Hangul.KeepAll(about), 12, Theme.P.TextFaint, Theme.Regular, false);
            a.TextWrapping = TextWrapping.Wrap;
            a.Margin = new Thickness(0, 3, 0, 0);
            a.MaxHeight = Math.Ceiling(a.FontSize * a.FontFamily.LineSpacing * 2) + 1;
            text.Children.Add(a);
        }
        var state = Theme.T(statusText, 12, status.State == AddonState.Running ? Theme.P.Ok : Theme.P.TextDim);
        state.Margin = new Thickness(0, 4, 0, 0);
        state.ToolTip = statusText.Length > 40 ? statusText : null;
        text.Children.Add(state);
        r.Add(text, new GridLength(1, GridUnitType.Star));

        end.Margin = new Thickness(16, 0, 0, 0);
        r.Add(end, GridLength.Auto);
        return r;
    }

    private static bool Flip(Action toggle)
    {
        toggle();
        return true;
    }

    /// An add-on's switch, greyed where it can't run; a click on it switches it (its line's click opens the page).
    private static Switch AddonSwitch(Settings s, Addon addon, AddonStatus status, Action<bool, Switch> set, out Action? toggle)
    {
        bool usable = status.State is not (AddonState.Dev or AddonState.NotShipped or AddonState.Broken);
        Switch? sw = null;
        sw = Toggle(s.AddonsOn.Contains(addon.Id), v => set(v, sw!), out var flip);
        toggle = usable ? flip : null;
        if (!usable)
        {
            sw.On = false;
            sw.Opacity = 0.45;
            sw.IsHitTestVisible = false;
            return sw;
        }
        sw.Cursor = System.Windows.Input.Cursors.Hand;
        sw.MouseLeftButtonUp += (_, e) =>
        {
            flip();
            e.Handled = true;
        };
        return sw;
    }

    /// One add-on's line: its switch with its host's status under it (`collectedSub` while the launcher's search
    /// collects) and a › to its page, which the line's click and Enter open (←→ switch it); and a line to start it again
    /// after it stopped. Returns the status it showed.
    private AddonStatus AddonLine(Settings s, Addon addon, string title, string collectedSub, char glyph, Func<AddonStatus, string> statusText,
        string againSub, Action<bool, Switch> set, string filter)
    {
        // the status reads the host's state files: not while the launcher's search collects the lines
        var status = collected is null ? addon.Status() : new AddonStatus(AddonState.Off);
        var sw = AddonSwitch(s, addon, status, set, out var toggle);
        var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        end.Children.Add(sw);
        var chevron = Theme.Glyph(G.Right, 10, Theme.P.TextFaint);
        chevron.Margin = new Thickness(14, 0, 0, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        end.Children.Add(chevron);
        string page = AddonPageKey(addon.Id);
        string sub = collected is null ? statusText(status) : collectedSub;
        // on the page, a line with its picture; a match while filtering, a plain one
        Place(title, sub, () => filter.Length == 0 ? AddonRow(addon.Id, title, collectedSub, glyph, status, sub, end) : Line(title, sub, end), () => Go(page),
            d => toggle is not null && sw.On != d > 0 && Flip(toggle), filter);

        if (collected is null && status.State is AddonState.SafeMode or AddonState.Download or AddonState.Restart or AddonState.Stopped)
        {
            bool reset = status.State == AddonState.SafeMode;
            string again = reset ? Tr("다시 켜기", "Turn on again") : Tr("다시 시도", "Try again");
            void Again()
            {
                _ = AddonHub.Retry(addon, reset);
                Build(lastFilter);
            }
            Add(again, reset ? Tr("탐색기가 멈춘 기록을 지우고 다시 켭니다", "Clears Explorer's crash record and turns it on again") : againSub,
                RowButton(again, Again), G.Refresh, Again, null, filter);
        }
        return status;
    }

    /// The status line of an add-on other than the taskbar one.
    private static string AddonStatusText(AddonStatus st) => st.State switch
    {
        AddonState.Dev => Tr("개발 복사본에서는 켜지 않습니다", "Not turned on in a development copy"),
        AddonState.NotShipped => Tr("아직 받지 않았습니다", "Not downloaded yet"),
        AddonState.Broken => Tr("애드온 파일이 올바르지 않거나 더 새 Bloom이 필요합니다", "The add-on's files are not valid, or it needs a newer Bloom"),
        AddonState.Off => Tr("꺼짐", "Off"),
        AddonState.Skipped => Tr("Shift를 누른 채 시작해 이번에는 켜지 않았습니다", "Bloom started with Shift held, so it is off this time"),
        AddonState.Starting when st.Percent >= 0 => Tr($"켜는 중 · 기호 파일 받는 중 {st.Percent}%", $"Turning on · downloading symbol files {st.Percent}%"),
        AddonState.Starting => Tr("켜는 중", "Turning on"),
        AddonState.Running => Tr("작동 중", "Running"),
        AddonState.SafeMode => Tr("멈춤 · 탐색기가 연달아 멈춰 이 애드온을 껐습니다", "Paused · Explorer kept crashing, so this add-on was turned off"),
        AddonState.NoSymbols => Tr("기호 없음 · 이 Windows 버전에는 필요한 기호가 없습니다", "No symbols · this Windows version lacks the symbols it needs"),
        AddonState.Windhawk => Tr("Windhawk에서 켜져 있음 · Windhawk에서 같은 모드를 끄세요. Windhawk를 쓰지 않으면 서비스를 수동 시작으로 바꾸고 Windhawk를 끄거나 지워도 됩니다",
            "On in Windhawk · turn the same mod off in Windhawk. If you don't use Windhawk, set its service to manual start and close or uninstall Windhawk instead"),
        AddonState.Blocked => Tr("보안 기능이 막음 · Windows 보안(스마트 앱 컨트롤)이 불러오기를 막았습니다", "Blocked by a security feature · Windows Security (Smart App Control) kept it from loading"),
        AddonState.Download => Tr("기호 파일을 받지 못했습니다 · 인터넷 연결을 확인하고 다시 시도하세요", "Could not download the symbol files · check your connection and try again"),
        AddonState.Restart => Tr("탐색기 재시작 필요 · 탐색기를 다시 시작한 뒤 다시 시도하세요", "Explorer needs a restart · restart Explorer, then try again"),
        _ => Tr("멈춤 · 이 애드온이 멈췄습니다", "Stopped · this add-on stopped"),
    };

    /// The switch: the first time on, `confirm` says what the add-on is and must be accepted (once per add-on); then the
    /// setting is saved and the host started or stopped.
    private void SetAddon(Addon addon, bool on, Switch sw, Func<bool> confirm)
    {
        var s = Settings.Current;
        if (on && !s.AddonsConfirmed.Contains(addon.Id))
        {
            if (!confirm())
            {
                sw.On = false;
                return;
            }
            s.AddonsConfirmed.Add(addon.Id);
        }
        _ = AddonHub.Set(addon, on);
        Build(lastFilter);
    }

    /// One of the catalog's: a program apart from Bloom with its own source, which loads code into Explorer and downloads
    /// symbol files, under the license its addon.json gives (the switch only works once that file is here).
    private bool ConfirmListed(Addon addon, string name)
    {
        string license = addon.Manifest?.License ?? "";
        return ConfirmProgram(addon, Tr($"{name} 켜기", $"Turn on {name}"),
            Tr($"이 애드온은 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 동작합니다. 라이선스는 {license}입니다. 처음 켤 때 Microsoft 기호 서버에서 필요한 기호 파일을 받습니다.",
                $"This add-on is a program separate from Bloom. It works by loading code into Explorer, and is licensed under {license}. The first start downloads the symbol files it needs from the Microsoft symbol server."));
    }

    /// One the user put in: a program Bloom doesn't ship and never checked, which may load code into Explorer.
    private bool ConfirmUser(string name) =>
        ConfirmWindow.Ask(Window.GetWindow(this), Tr($"{name} 켜기", $"Turn on {name}"),
            Tr("직접 넣은 애드온은 Bloom이 만들거나 확인한 것이 아닌 별도 프로그램입니다. 켜면 Bloom이 그 프로그램을 실행하고, 그 프로그램은 탐색기에 코드를 넣을 수 있습니다. 믿을 수 있는 애드온만 켜세요.",
                "An add-on you added is a separate program that Bloom neither made nor checked. Turning it on starts that program, and it may load code into Explorer. Only turn on add-ons you trust."),
            (Tr("켜기", "Turn on"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.Cancel)) == MessageBoxResult.Yes;

    /// Before a program with its own source first runs: 켜기, 취소, or 소스 보기, which opens the source and asks again.
    private bool ConfirmProgram(Addon addon, string title, string body)
    {
        var owner = Window.GetWindow(this);
        while (true)
        {
            var r = ConfirmWindow.Ask(owner, title, body,
                (Tr("켜기", "Turn on"), MessageBoxResult.Yes), (Tr("소스 보기", "View source"), MessageBoxResult.No), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
            if (r != MessageBoxResult.No) return r == MessageBoxResult.Yes;
            if (addon.Manifest is { } m) OpenSource(m); // and the question stays
        }
    }
}
