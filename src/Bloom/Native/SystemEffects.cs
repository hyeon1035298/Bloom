using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Bloom.Native;

/// Whether Windows keeps its own glass (acrylic, Mica) off just now, 투명 효과 aside: in a high-contrast theme, over a
/// remote desktop and while energy saver is on. DWM's acrylic turns to a plain fill by itself in these; the glass Bloom
/// draws (OwnGlass) is told through Theme.Acrylic. A hidden window hears when one of them, or 투명 효과, changes while
/// Bloom runs.
internal static class SystemEffects
{
    public static bool Off => HighContrast() || Win32.GetSystemMetrics(SM_REMOTESESSION) != 0 || energySaver || batterySaver;

    private static HwndSource? window;
    private static bool pending;
    // as the power notices last said (Windows sends each one's state when Watch asks for them): energy saver from
    // Windows 11 24H2 on, where the older battery saver state no longer follows it, and battery saver before
    private static bool energySaver, batterySaver = BatterySaver();

    /// `changed` is called on this (the UI) thread after Windows' look, session or power state changed; notices that
    /// come together make one call.
    public static void Watch(Action changed)
    {
        if (window is not null) return;
        // top-level (a message-only window gets no broadcasts), never shown
        window = new HwndSource(new HwndSourceParameters("BloomSystemEffects") { Width = 0, Height = 0, WindowStyle = unchecked((int)WS_POPUP), ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE });
        foreach (Guid setting in new[] { GUID_ENERGY_SAVER_STATUS, GUID_POWER_SAVING_STATUS })
        {
            Guid g = setting;
            _ = RegisterPowerSettingNotification(window.Handle, ref g, 0 /* to the window */);
        }
        _ = WTSRegisterSessionNotification(window.Handle, 0 /* this session */);
        var ui = Dispatcher.CurrentDispatcher;
        window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            bool mine = msg switch
            {
                WM_SETTINGCHANGE or WM_WTSSESSION_CHANGE => true, // 투명 효과, high contrast; a remote desktop joined or left
                WM_POWERBROADCAST => w.ToInt64() == PBT_POWERSETTINGCHANGE && PowerSetting(l),
                _ => false,
            };
            if (mine && !pending)
            {
                pending = true;
                ui.BeginInvoke(() =>
                {
                    pending = false;
                    changed();
                }, DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        });
    }

    private static bool HighContrast()
    {
        var hc = new HIGHCONTRAST { cbSize = Marshal.SizeOf<HIGHCONTRAST>() };
        return SystemParametersInfo(SPI_GETHIGHCONTRAST, hc.cbSize, ref hc, 0) && (hc.dwFlags & 1 /* HCF_HIGHCONTRASTON */) != 0;
    }

    /// A POWERBROADCAST_SETTING (the setting's GUID, the data's length, the data): one of the two savers, off (0) or on.
    private static bool PowerSetting(IntPtr data)
    {
        if (data == IntPtr.Zero) return false;
        Guid setting = Marshal.PtrToStructure<Guid>(data);
        bool on = Marshal.ReadInt32(data, 16) >= 4 && Marshal.ReadInt32(data, 20) != 0;
        if (setting == GUID_ENERGY_SAVER_STATUS) energySaver = on;
        else if (setting == GUID_POWER_SAVING_STATUS) batterySaver = on;
        else return false;
        return true;
    }

    private static bool BatterySaver() => GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1;

    private const int SM_REMOTESESSION = 0x1000;
    private const int WM_SETTINGCHANGE = 0x001A, WM_POWERBROADCAST = 0x0218, WM_WTSSESSION_CHANGE = 0x02B1;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint WS_POPUP = 0x80000000;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly Guid GUID_POWER_SAVING_STATUS = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5"), GUID_ENERGY_SAVER_STATUS = new("550E8400-E29B-41D4-A716-446655440000");

    [StructLayout(LayoutKind.Sequential)]
    private struct HIGHCONTRAST
    {
        public int cbSize, dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, int size, ref HIGHCONTRAST value, uint winIni);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
}
