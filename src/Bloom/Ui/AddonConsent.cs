using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// What Bloom says before an add-on is turned on, in Settings and in the tutorial.
internal static class AddonConsent
{
    internal sealed record Facts(string Id, string Name, string License, bool Symbols, int SymbolsMb, string? Source);

    public static Facts Describe(Addon a)
    {
        var c = a.Catalog;
        string name = a.Id == AddonProtocol.Id ? Tr("작업표시줄", "Taskbar") : c?.Title(Lang.Code) ?? a.Manifest?.Title(Lang.Code) ?? a.Id;
        string license = a.Manifest is { License: { Length: > 0 } own } ? own : c?.License ?? "";
        string? source = a.Manifest is { } m && Uri.TryCreate(m.Source, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? m.Source
            : c?.Page;
        return new Facts(a.Id, name, license, c?.Symbols ?? false, c?.SymbolsMb ?? 0, source);
    }

    /// "GPL-3.0-only" → "GPL-3.0"; "" → "".
    public static string LicenseShown(string spdx) => spdx.EndsWith("-only", StringComparison.Ordinal) ? spdx[..^"-only".Length] : spdx;

    /// The lines under an add-on's name: the licence, then the symbol download where it has one.
    public static IReadOnlyList<string> Lines(Facts f)
    {
        var lines = new List<string>();
        if (LicenseShown(f.License) is { Length: > 0 } shown) lines.Add(Tr("오픈 소스 {0}", "Open source {0}").Replace("{0}", shown, StringComparison.Ordinal));
        if (f.Symbols && f.SymbolsMb > 0)
            lines.Add(Tr("처음 켤 때 Microsoft에서 기호 파일 약 {0} MB를 받습니다", "Downloads about {0} MB of symbol files from Microsoft the first time").Replace("{0}", f.SymbolsMb.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        else if (f.Symbols)
            lines.Add(Tr("처음 켤 때 Microsoft에서 기호 파일을 받습니다", "Downloads symbol files from Microsoft the first time"));
        return lines;
    }

    public static bool Confirmed(Addon a) => Settings.Current.AddonsConfirmed.Contains(a.Id);

    /// Settings: one add-on, today's window and wording. True = turn it on (and it is recorded as confirmed).
    public static bool Ask(Window? owner, Addon a) => true;

    /// The tutorial: every add-on about to be turned on in one window. True = 켜기 (and they are recorded as confirmed).
    public static bool AskMany(Window? owner, IReadOnlyList<Addon> picked, bool taskbarChat)
    {
        string title = Tr("애드온 {0}개 켜기", "Turn on {0} add-ons").Replace("{0}", picked.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        bool yes = ConfirmWindow.Ask(owner, title,
            Tr("애드온은 Bloom과 따로 실행되고, Windows 탐색기 안에서 동작합니다.", "Add-ons run apart from Bloom and work inside Windows Explorer."),
            (Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("켜지 않기", "Don't turn on"), MessageBoxResult.No)) == MessageBoxResult.Yes;
        if (!yes) return false;
        var confirmed = Settings.Current.AddonsConfirmed;
        foreach (var a in picked)
            if (!confirmed.Contains(a.Id)) confirmed.Add(a.Id);
        Settings.Save();
        return true;
    }

    /// Only the taskbar AI panel is new (the add-on is on already). True = 켜기.
    public static bool AskTaskbarChat(Window? owner) =>
        ConfirmWindow.Ask(owner, Tr("작업표시줄 AI 패널 켜기", "Turn on the taskbar AI panel"), ChatNote,
            (Tr("켜기", "Turn On"), MessageBoxResult.Yes), (Tr("켜지 않기", "Don't turn on"), MessageBoxResult.No)) == MessageBoxResult.Yes;

    private static string ChatNote => Tr("작업표시줄 AI 패널은 실험 기능입니다. Claude 데스크톱 앱과 Codex 앱을 대신 조작해 대화를 주고받고, 작업표시줄에 AI 사용량도 표시합니다.",
        "The taskbar AI panel is experimental. It operates the Claude desktop app and the Codex app for you, and also shows AI usage on the taskbar.");

    public static void OpenSource(Addon a)
    {
        if (a.Catalog?.Page is { } page) Launch.Open(page);
    }

    /// The line Settings shows for the add-on's state now (its status line, or its download line after a failed
    /// download), word for word. The finish flow's failure card uses it.
    public static string StatusText(Addon a) => a.Status().State.ToString();

    /// QA: the AskMany window, not shown, to be captured.
    internal static Window ForQa(IReadOnlyList<Addon> picked, bool taskbarChat) => new();
}
