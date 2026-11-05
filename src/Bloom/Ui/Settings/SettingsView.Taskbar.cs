using System;
using System.IO;
using System.Windows;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 작업표시줄: the taskbar add-on (PillTaskbar, a separate GPL-3.0 program Bloom ships and starts). Its switch with
/// the host's status under it, bringing its settings over from the Windhawk build, and what it is (version, license,
/// source).
internal sealed partial class SettingsView
{
    private void PageTaskbar(Settings s, string filter)
    {
        var addon = AddonHub.Taskbar;
        Page("taskbar", Tr("작업표시줄", "Taskbar"), G.App, Tr("작업표시줄을 가운데 알약 모양으로 바꾸는 애드온 PillTaskbar", "PillTaskbar, an add-on that turns the taskbar into a centred pill"), filter);
        Header("PillTaskbar");
        // the status reads the host's state files: not while the launcher's search collects the lines
        var status = collected is null ? addon.Status() : new AddonStatus(AddonState.Off);
        bool usable = status.State is not (AddonState.Dev or AddonState.NotShipped or AddonState.Broken);
        Switch? sw = null;
        sw = Toggle(s.AddonsOn.Contains(addon.Id), v => SetTaskbar(addon, v, sw!), out var toggle);
        if (!usable)
        {
            sw.On = false;
            sw.Opacity = 0.45;
            sw.IsHitTestVisible = false;
        }
        string sub = collected is null ? TaskbarStatusText(status) : Tr("작업표시줄을 가운데 알약 모양으로 바꿉니다", "Turns the taskbar into a centred pill");
        Add(Tr("작업표시줄 꾸미기", "Customize the taskbar"), sub, sw, G.Screen, usable ? toggle : null, null, filter);

        if (collected is null && status.State is AddonState.SafeMode or AddonState.Download or AddonState.Stopped)
        {
            bool reset = status.State == AddonState.SafeMode;
            string title = reset ? Tr("다시 켜기", "Turn on again") : Tr("다시 시도", "Try again");
            void Again()
            {
                _ = AddonHub.Retry(addon, reset);
                Build(lastFilter);
            }
            Add(title, reset ? Tr("탐색기가 멈춘 기록을 지우고 다시 켭니다", "Clears Explorer's crash record and turns it on again") : Tr("작업표시줄 꾸미기를 다시 시작합니다", "Starts taskbar customization again"),
                RowButton(title, Again), G.Refresh, Again, null, filter);
        }

        // only where the Windhawk build's settings are (read only), and not while the search collects (it reads the registry)
        var windhawk = new WindhawkRegistry();
        if (collected is null && usable && windhawk.Read() is not null)
            Add(Tr("Windhawk 설정 가져오기", "Import Windhawk settings"), Tr("Windhawk의 pill-taskbar 모드 설정을 그대로 옮깁니다 · 작동 중이면 바로 적용됩니다", "Brings over the pill-taskbar mod's settings from Windhawk · applies at once while running"),
                null, G.Import, () => ImportWindhawk(addon, windhawk), null, filter);

        if (addon.Manifest is { } m)
            Add($"PillTaskbar {m.Version}", Tr($"PillTaskbar는 GPL-3.0인 별도 프로그램입니다 · {m.License}", $"PillTaskbar is a separate GPL-3.0 program · {m.License}"),
                RowButton(Tr("소스 보기", "View source"), () => OpenSource(m)), G.Info, () => OpenSource(m), null, filter);
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
        AddonState.NotShipped => Tr("이 Bloom에는 작업표시줄 애드온이 없습니다", "This Bloom has no taskbar add-on"),
        AddonState.Broken => Tr("작업표시줄 애드온 파일이 올바르지 않습니다 · Bloom을 다시 설치하세요", "The taskbar add-on's files are not valid · reinstall Bloom"),
        AddonState.Off => Tr("꺼짐", "Off"),
        AddonState.Skipped => Tr("Shift를 누른 채 시작해 이번에는 켜지 않았습니다", "Bloom started with Shift held, so it is off this time"),
        AddonState.Starting when st.Percent >= 0 => Tr($"켜는 중 · 기호 파일 받는 중 {st.Percent}%", $"Turning on · downloading symbol files {st.Percent}%"),
        AddonState.Starting => Tr("켜는 중", "Turning on"),
        AddonState.Running => Tr("작동 중", "Running"),
        AddonState.SafeMode => Tr("멈춤 · 탐색기가 연달아 멈춰 작업표시줄 꾸미기를 껐습니다", "Paused · Explorer kept crashing, so taskbar customization was turned off"),
        AddonState.NoSymbols => Tr("기호 없음 · 이 Windows 버전에는 필요한 작업표시줄 기호가 없습니다", "No symbols · this Windows version lacks the taskbar symbols it needs"),
        AddonState.Windhawk => Tr("Windhawk에서 켜져 있음 · Windhawk에서 PillTaskbar 모드를 먼저 끄세요", "On in Windhawk · turn the PillTaskbar mod off in Windhawk first"),
        AddonState.Blocked => Tr("보안 기능이 막음 · Windows 보안(스마트 앱 컨트롤)이 불러오기를 막았습니다", "Blocked by a security feature · Windows Security (Smart App Control) kept it from loading"),
        AddonState.Download => Tr("기호 파일을 받지 못했습니다 · 인터넷 연결을 확인하고 다시 시도하세요", "Could not download the symbol files · check your connection and try again"),
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

    private void ImportWindhawk(Addon addon, ISettingsSource source)
    {
        string title = Tr("Windhawk 설정 가져오기", "Import Windhawk settings");
        try
        {
            int n = WindhawkImport.Import(source, AddonHub.UserValuesFile(addon.Id), Path.Combine(addon.DataDir, AddonProtocol.ValuesFile));
            if (n < 0) ConfirmWindow.Tell(Window.GetWindow(this), title, Tr("Windhawk에 pill-taskbar 모드 설정이 없습니다.", "Windhawk has no settings for the pill-taskbar mod."));
            else Notices.Result(true, title, Tr($"설정 {n}개 가져오기 완료", $"{n} settings imported"), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("windhawk import", e);
            ConfirmWindow.Tell(Window.GetWindow(this), title, Tr("설정을 저장하지 못했습니다. 데이터 폴더의 권한을 확인하고 다시 시도하세요.", "Could not save the settings. Check the data folder's permissions, then retry."));
        }
    }
}
