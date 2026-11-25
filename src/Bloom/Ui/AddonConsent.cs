using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// What Bloom says before an add-on is turned on, in Settings and in the tutorial.
internal static class AddonConsent
{
    internal sealed record Facts(string Id, string Name, string License, bool Symbols, int SymbolsMb, string? Source);

    /// Its name, licence, symbol download and source page. The catalog stands in for the add-on's addon.json until that
    /// is downloaded, so the first question already names the licence and its 소스 보기 opens something.
    public static Facts Describe(Addon a)
    {
        var c = a.Catalog;
        string name = a.Id == AddonProtocol.Id ? Tr("작업표시줄", "Taskbar") : c?.Title(Lang.Code) ?? a.Manifest?.Title(Lang.Code) ?? a.Id;
        string license = a.Manifest is { License: { Length: > 0 } own } ? own : c?.License ?? "";
        string? source = a.Manifest is { } m && Web(m.Source) is not null ? m.Source : c?.Page;
        return new Facts(a.Id, name, license, c?.Symbols ?? false, c?.SymbolsMb ?? 0, source);
    }

    /// An http(s) address, or null: an add-on's source is opened only as a web page, never as whatever else it names.
    private static Uri? Web(string? s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u : null;

    /// "GPL-3.0-only" → "GPL-3.0"; "" → "".
    public static string LicenseShown(string spdx) => spdx.EndsWith("-only", StringComparison.Ordinal) ? spdx[..^"-only".Length] : spdx;

    /// The lines under an add-on's name: the licence, then the symbol download where it has one.
    public static IReadOnlyList<string> Lines(Facts f)
    {
        var lines = new List<string>();
        if (LicenseShown(f.License) is { Length: > 0 } shown) lines.Add(Tr("오픈 소스 {0}", "Open source {0}").Replace("{0}", shown, StringComparison.Ordinal));
        if (f.Symbols && f.SymbolsMb > 0)
            lines.Add(Tr("처음 켤 때 Microsoft에서 기호 파일 약 {0} MB를 받습니다", "Downloads about {0} MB of symbol files from Microsoft on first turn-on").Replace("{0}", f.SymbolsMb.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        else if (f.Symbols)
            lines.Add(Tr("처음 켤 때 Microsoft에서 기호 파일을 받습니다", "Downloads symbol files from Microsoft on first turn-on"));
        return lines;
    }

    public static bool Confirmed(Addon a) => Settings.Current.AddonsConfirmed.Contains(a.Id);

    private static void Remember(IEnumerable<Addon> addons)
    {
        var confirmed = Settings.Current.AddonsConfirmed;
        foreach (var a in addons)
            if (!confirmed.Contains(a.Id)) confirmed.Add(a.Id);
        Settings.Save();
    }

    // ---- Settings: one add-on

    /// Settings' question for one add-on: its title, its text, and whether it offers 소스 보기 (the user's own add-ons
    /// are not Bloom's to point at).
    internal static (string Title, string Body, bool Source) Question(Addon a)
    {
        if (a.Id == AddonProtocol.Id)
            // a separate GPL program that loads code into Explorer and downloads its symbol files
            return (Tr("작업표시줄 꾸미기 켜기", "Enable Taskbar Styling"),
                Tr("PillTaskbar는 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 작업표시줄의 모양을 바꿉니다. 라이선스는 GPL-3.0입니다. 처음 켤 때 Microsoft 기호 서버에서 작업표시줄 기호 파일(약 70 MB)을 받습니다.",
                    "PillTaskbar is a separate GPL-3.0 program that injects code into Explorer to restyle the taskbar. On first launch, it downloads taskbar symbol files (~70 MB) from Microsoft's symbol server."), true);
        var f = Describe(a);
        string name = f.Name;
        if (a.User)
            // a program Bloom doesn't ship and never checked, which may load code into Explorer
            return (Tr($"{name} 켜기", $"Turn On {name}"),
                Tr("직접 넣은 애드온은 Bloom이 만들거나 확인한 것이 아닌 별도 프로그램입니다. 켜면 Bloom이 그 프로그램을 실행하고, 그 프로그램은 탐색기에 코드를 넣을 수 있습니다. 믿을 수 있는 애드온만 켜세요.",
                    "Custom add-ons are separate programs neither created nor verified by Bloom. Turning one on lets Bloom run that program, which may inject code into Explorer. Only turn on add-ons you trust."), false);
        // one of the catalog's: a program apart from Bloom with its own source, which loads code into Explorer, under its
        // licence, and downloads symbol files only where the catalog says it does
        string license = f.License;
        string body = f.Symbols
            ? Tr($"이 애드온은 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 동작합니다. 라이선스는 {license}입니다. 처음 켤 때 Microsoft 기호 서버에서 필요한 기호 파일을 받습니다.",
                $"This add-on is a separate program from Bloom and works by injecting code into Explorer. Its license is {license}. When turned on for the first time, it downloads required symbol files from the Microsoft symbol server.")
            : Tr($"이 애드온은 Bloom과 별도인 프로그램으로, 탐색기에 코드를 넣어 동작합니다. 라이선스는 {license}입니다.",
                $"This add-on is a separate program from Bloom and works by injecting code into Explorer. Its license is {license}.");
        return (Tr($"{name} 켜기", $"Turn On {name}"), body, true);
    }

    /// Settings: one add-on, today's window and wording. True = turn it on (and it is recorded as confirmed).
    public static bool Ask(Window? owner, Addon a)
    {
        var (title, body, source) = Question(a);
        bool yes;
        if (!source)
            yes = ConfirmWindow.Ask(owner, title, body, (Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.Cancel)) == MessageBoxResult.Yes;
        else
            while (true)
            {
                // 소스 보기 opens the source and asks again
                var r = ConfirmWindow.Ask(owner, title, body,
                    (Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("소스 보기", "View Source"), MessageBoxResult.No), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
                if (r != MessageBoxResult.No)
                {
                    yes = r == MessageBoxResult.Yes;
                    break;
                }
                OpenSource(a);
            }
        if (yes) Remember([a]);
        return yes;
    }

    // ---- The tutorial: every picked add-on at once

    /// The tutorial: every add-on about to be turned on in one window. True = 켜기 (and they are recorded as confirmed).
    public static bool AskMany(Window? owner, IReadOnlyList<Addon> picked, bool taskbarChat)
    {
        var (title, body) = ManyText(picked.Count);
        bool yes = ConfirmWindow.Ask(owner, title, body, ManyContent(picked, taskbarChat), ManyWidth, ManyChoices()) == MessageBoxResult.Yes;
        if (yes) Remember(picked);
        return yes;
    }

    private const double ManyWidth = 500;

    private static (string Title, string Body) ManyText(int count) =>
        (count == 1 ? Tr("애드온 1개 켜기", "Turn On 1 Add-on") // English says one in the singular
            : Tr("애드온 {0}개 켜기", "Turn On {0} Add-ons").Replace("{0}", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            Tr("애드온은 Bloom과 따로 실행되고, Windows 탐색기 안에서 동작합니다.", "Add-ons run separately from Bloom and operate inside Windows Explorer."));

    private static (string, MessageBoxResult)[] ManyChoices() =>
        [(Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("켜지 않기", "Don't Turn On"), MessageBoxResult.No)];

    /// Under the text: a card per add-on, then what the taskbar AI panel does when it is turned on too.
    private static StackPanel ManyContent(IReadOnlyList<Addon> picked, bool taskbarChat)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        for (int i = 0; i < picked.Count; i++)
        {
            var row = Row(picked[i]);
            if (i > 0) row.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(row);
        }
        if (taskbarChat)
        {
            // what the user agrees to is read in full: the note takes the lines it needs
            var note = TutorialParts.Note(ChatNote, 4);
            note.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(note);
        }
        return sp;
    }

    /// One add-on: the glyph it wears in Settings, its name, its lines, and 소스 보기, which leaves the window open.
    private static SmoothBorder Row(Addon a)
    {
        var f = Describe(a);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Icons.Tile(a.Id == AddonProtocol.Id ? G.Screen : G.Plug, false, 26));

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(TutorialParts.Lines(Theme.T(f.Name, 13, Theme.P.Text, Theme.Medium), 18));
        foreach (string line in Lines(f))
            text.Children.Add(new TutorialParts.EvenLines(Theme.T(Hangul.KeepAll(line), 11.5, Theme.P.TextFaint), 16, 3) { Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        if (f.Source is not null)
        {
            var link = TutorialParts.Link(Tr("소스 보기", "View Source"), () => OpenSource(a), 12);
            link.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(link, 2);
            g.Children.Add(link);
        }
        return new SmoothBorder
        {
            CornerRadius = new CornerRadius(9),
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 9, 12, 9),
            MinHeight = 56,
            Child = g,
        };
    }

    /// Only the taskbar AI panel is new (the add-on is on already). True = 켜기.
    public static bool AskTaskbarChat(Window? owner) =>
        ConfirmWindow.Ask(owner, Tr("작업표시줄 AI 패널 켜기", "Turn On Taskbar AI panel"), ChatNote,
            (Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("켜지 않기", "Don't Turn On"), MessageBoxResult.No)) == MessageBoxResult.Yes;

    /// The panel drives other programs, so turning it on is said before it happens.
    private static string ChatNote => Tr("작업표시줄 AI 패널은 실험 기능입니다. Claude 데스크톱 앱과 Codex 앱을 대신 조작해 대화를 주고받고, 작업표시줄에 AI 사용량도 표시합니다.",
        "The Taskbar AI panel is an experimental feature. It controls the Claude desktop app and Codex app on your behalf to send and receive chats, and also shows AI usage on the taskbar.");

    /// The add-on's source repository, as a web page only.
    public static void OpenSource(Addon a)
    {
        if (Web(Describe(a).Source) is { } u) Launch.Open(u.AbsoluteUri);
    }

    /// The line Settings shows for the add-on's state now (its status line, or its download line after a failed
    /// download), word for word. The finish flow's failure card uses it.
    public static string StatusText(Addon a)
    {
        if (a.Fetch.Failed is { } why) return SettingsView.FailureText(why);
        var st = a.Status();
        return a.Id == AddonProtocol.Id ? SettingsView.TaskbarStatusText(st) : SettingsView.AddonStatusText(st);
    }

    /// QA: the AskMany window, not shown, to be captured.
    internal static Window ForQa(IReadOnlyList<Addon> picked, bool taskbarChat)
    {
        var (title, body) = ManyText(picked.Count);
        return ConfirmWindow.ForQa(title, body, ManyContent(picked, taskbarChat), ManyWidth, ManyChoices());
    }
}
