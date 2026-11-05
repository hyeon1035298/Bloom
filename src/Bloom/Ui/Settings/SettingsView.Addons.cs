using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온: the add-ons the user put in Bloom's user-addons folder themselves (AddonStore.User). Bloom ships none
/// of them and draws no settings for them, so each gets one line: its name, its host's status and its switch. The page
/// is there only while the folder holds one.
internal sealed partial class SettingsView
{
    /// The tests: hand-made add-ons in place of the user's.
    private IReadOnlyList<Addon>? userAddons;

    private void PageAddons(Settings s, string filter)
    {
        var list = userAddons ?? AddonHub.Users.ToList();
        if (list.Count == 0) return;
        Page("addons", Tr("애드온", "Add-ons"), G.Plug,
            Tr("Bloom의 로컬 데이터 폴더 안 user-addons에 직접 넣은 애드온입니다. 새로 넣은 애드온은 Bloom을 다시 시작하면 보입니다.",
                "Add-ons you put in the user-addons folder in Bloom's local data folder yourself. One added there shows after Bloom restarts."), filter);
        Header(Tr("직접 넣은 애드온", "Your add-ons"));
        foreach (var addon in list) UserAddonLines(s, addon, filter);
    }

    private void UserAddonLines(Settings s, Addon addon, string filter)
    {
        string name = addon.Manifest?.Title(Lang.Code) ?? addon.Id;
        // the status reads the host's state files: not while the launcher's search collects the lines
        var status = collected is null ? addon.Status() : new AddonStatus(AddonState.Off);
        bool usable = status.State is not (AddonState.Dev or AddonState.NotShipped or AddonState.Broken);
        Switch? sw = null;
        sw = Toggle(s.AddonsOn.Contains(addon.Id), v => SetUserAddon(addon, name, v, sw!), out var toggle);
        if (!usable)
        {
            sw.On = false;
            sw.Opacity = 0.45;
            sw.IsHitTestVisible = false;
        }
        string sub = collected is null ? UserAddonStatusText(status) : Tr("직접 넣은 애드온", "An add-on you added");
        Add(name, sub, sw, G.Plug, usable ? toggle : null, null, filter);

        if (collected is null && status.State is AddonState.SafeMode or AddonState.Download or AddonState.Restart or AddonState.Stopped)
        {
            bool reset = status.State == AddonState.SafeMode;
            string title = reset ? Tr("다시 켜기", "Turn on again") : Tr("다시 시도", "Try again");
            void Again()
            {
                _ = AddonHub.Retry(addon, reset);
                Build(lastFilter);
            }
            Add(title, reset ? Tr("탐색기가 멈춘 기록을 지우고 다시 켭니다", "Clears Explorer's crash record and turns it on again") : Tr("이 애드온을 다시 시작합니다", "Starts this add-on again"),
                RowButton(title, Again), G.Refresh, Again, null, filter);
        }
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
