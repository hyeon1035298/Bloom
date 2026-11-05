using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온: every add-on with its switch and its host's status. First the taskbar add-on (PillTaskbar), which this
/// page downloads from the release Bloom's catalog pins (TaskbarDownload), then the ones the user put in Bloom's
/// user-addons folder themselves (AddonStore.User), which Bloom draws no settings for. Below the list, the taskbar
/// add-on's own settings while it is on (SettingsView.Taskbar).
internal sealed partial class SettingsView
{
    /// The tests: hand-made add-ons in place of the user's.
    private IReadOnlyList<Addon>? userAddons;

    private void PageAddons(Settings s, string filter)
    {
        var taskbar = taskbarAddon ?? AddonHub.Taskbar;
        var users = userAddons ?? AddonHub.Users.ToList();
        Page("addons", Tr("애드온", "Add-ons"), G.Plug,
            Tr("애드온은 Bloom이 함께 실행하는 별도 프로그램이며, 여기서 하나씩 켜고 끕니다. Bloom의 로컬 데이터 폴더 안 user-addons에 직접 넣은 애드온은 Bloom을 다시 시작하면 보입니다.",
                "Add-ons are separate programs Bloom starts, each turned on and off here. One you put in the user-addons folder in Bloom's local data folder shows after Bloom restarts."), filter);
        Header(Tr("애드온 목록", "All add-ons"));
        var status = AddonLine(s, taskbar, Tr("작업표시줄", "Taskbar"), Tr("작업표시줄을 가운데 알약 모양으로 바꿉니다", "Turns the taskbar into a centred pill"), G.Screen,
            TaskbarStatusText, Tr("작업표시줄 꾸미기를 다시 시작합니다", "Starts taskbar customization again"), (v, sw) => SetTaskbar(taskbar, v, sw), filter);
        TaskbarDownload(taskbar, status, filter);
        foreach (var addon in users)
        {
            string name = addon.Manifest?.Title(Lang.Code) ?? addon.Id;
            AddonLine(s, addon, name, Tr("직접 넣은 애드온", "An add-on you added"), G.Plug,
                UserAddonStatusText, Tr("이 애드온을 다시 시작합니다", "Starts this add-on again"), (v, sw) => SetUserAddon(addon, name, v, sw), filter);
        }
        if (s.AddonsOn.Contains(taskbar.Id)) TaskbarLines(taskbar, status, filter);
    }

    /// One add-on's line: its switch (greyed where it can't run) with its host's status under it (`collectedSub` while
    /// the launcher's search collects), and a line to start it again after it stopped. Returns the status it showed.
    private AddonStatus AddonLine(Settings s, Addon addon, string title, string collectedSub, char glyph, Func<AddonStatus, string> statusText,
        string againSub, Action<bool, Switch> set, string filter)
    {
        // the status reads the host's state files: not while the launcher's search collects the lines
        var status = collected is null ? addon.Status() : new AddonStatus(AddonState.Off);
        bool usable = status.State is not (AddonState.Dev or AddonState.NotShipped or AddonState.Broken);
        Switch? sw = null;
        sw = Toggle(s.AddonsOn.Contains(addon.Id), v => set(v, sw!), out var toggle);
        if (!usable)
        {
            sw.On = false;
            sw.Opacity = 0.45;
            sw.IsHitTestVisible = false;
        }
        Add(title, collected is null ? statusText(status) : collectedSub, sw, glyph, usable ? toggle : null, null, filter);

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

    private static string UserAddonStatusText(AddonStatus st) => st.State switch
    {
        AddonState.Dev => Tr("개발 복사본에서는 켜지 않습니다", "Not turned on in a development copy"),
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

    /// The switch: the first time on, what such an add-on is (a program Bloom doesn't ship that may load code into
    /// Explorer) is said once and accepted; then the setting is saved and the host started or stopped.
    private void SetUserAddon(Addon addon, string name, bool on, Switch sw)
    {
        var s = Settings.Current;
        if (on && !s.AddonsConfirmed.Contains(addon.Id))
        {
            var r = ConfirmWindow.Ask(Window.GetWindow(this), Tr($"{name} 켜기", $"Turn on {name}"),
                Tr("직접 넣은 애드온은 Bloom이 만들거나 확인한 것이 아닌 별도 프로그램입니다. 켜면 Bloom이 그 프로그램을 실행하고, 그 프로그램은 탐색기에 코드를 넣을 수 있습니다. 믿을 수 있는 애드온만 켜세요.",
                    "An add-on you added is a separate program that Bloom neither made nor checked. Turning it on starts that program, and it may load code into Explorer. Only turn on add-ons you trust."),
                (Tr("켜기", "Turn on"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
            if (r != MessageBoxResult.Yes)
            {
                sw.On = false;
                return;
            }
            s.AddonsConfirmed.Add(addon.Id);
        }
        _ = AddonHub.Set(addon, on);
        Build(lastFilter);
    }
}
