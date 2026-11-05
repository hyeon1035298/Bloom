using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Bloom.Native;

namespace Bloom.Ui;

/// The island window (a pill a taskbar add-on keeps at the top middle of the taskbar's monitor, drawn in explorer)
/// on the primary monitor, where the notices are, in DIPs. Its window also spans the taskbar pill it turns into, so
/// what shows is the window's region, read here as the region's box moved to the window.
internal static class NoticeIsland
{
    private const string ClassPrefix = "PillTaskbarIsland_";

    /// QA: this box (DIP) in place of a look at the screen.
    public static Rect? Fake;

    public static Rect? Find()
    {
        if (Fake is { } fake) return fake;
        IntPtr primary = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(primary, ref info)) return null;
        if (Win32.GetDpiForMonitor(primary, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        Rect? found = null;
        var name = new StringBuilder(64);
        Win32.EnumWindows((h, _) =>
        {
            name.Clear();
            if (Win32.GetClassName(h, name, name.Capacity) <= 0 || !name.ToString().StartsWith(ClassPrefix, StringComparison.Ordinal) || !Win32.IsWindowVisible(h)) return true;
            if (!Win32.GetWindowRect(h, out var window)) return true;
            int kind = Win32.GetWindowRgnBox(h, out var box);
            found = NoticePlacement.Island(window, kind, box, info.rcMonitor, scale);
            return found is null; // one island a monitor: the first on this one is it
        }, IntPtr.Zero);
        return found;
    }
}
