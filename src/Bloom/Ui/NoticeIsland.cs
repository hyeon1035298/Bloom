using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Bloom.Native;

namespace Bloom.Ui;

/// What the notices keep under at the top of the primary monitor, in DIPs: the island window (a pill a taskbar add-on
/// keeps at the top middle of the taskbar's monitor, drawn in explorer; its window also spans the taskbar pill it turns
/// into, so what shows is the window's region, read here as the region's box moved to the window) and the taskbar
/// while it shows at the top.
internal static class NoticeIsland
{
    private const string ClassPrefix = "PillTaskbarIsland_";

    /// QA: this box (DIP) in place of a look at the screen (and no taskbar).
    public static Rect? Fake;

    private static IntPtr island; // the island window the last search found

    /// The island alone, looked for through the windows.
    public static Rect? Find() => Look(true).Island;

    /// The island in physical pixels (it is on the primary monitor, whose DIPs start at 0,0), for windows placed by
    /// monitor pixels: the capture's overlays keep their hint and bar off it.
    public static Rect? FindPixels()
    {
        if (Find() is not { } r) return null;
        IntPtr primary = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        if (Win32.GetDpiForMonitor(primary, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        double s = dpi / 96.0;
        return new Rect(r.X * s, r.Y * s, r.Width * s, r.Height * s);
    }

    /// The island and the taskbar as they show now. `search`: look through the windows for the island (once a second);
    /// else the window found last is read again (every frame while the taskbar moves).
    public static Above Look(bool search)
    {
        if (Fake is { } fake) return new Above(fake, null);
        IntPtr primary = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(primary, ref info)) return default;
        if (Win32.GetDpiForMonitor(primary, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        double scale = dpi / 96.0;
        if (search || !Win32.IsWindow(island)) island = Search(info.rcMonitor);
        Rect? box = null;
        if (island != IntPtr.Zero && Win32.IsWindowVisible(island) && Win32.GetWindowRect(island, out var window))
            box = NoticePlacement.Island(window, Win32.GetWindowRgnBox(island, out var region), region, info.rcMonitor, scale);
        return new Above(box, Bar(info.rcMonitor, scale));
    }

    /// The primary monitor's taskbar window (zero when there is none).
    public static IntPtr TaskbarWindow => Win32.FindWindow("Shell_TrayWnd", null);

    private static Rect? Bar(Win32.RECT monitor, double scale)
    {
        IntPtr h = TaskbarWindow;
        if (h == IntPtr.Zero || !Win32.IsWindowVisible(h) || !Win32.GetWindowRect(h, out var window)) return null;
        return NoticePlacement.Taskbar(window, Win32.GetWindowRgnBox(h, out var region), region, monitor, scale);
    }

    /// The island window on this monitor: one a monitor, so the first visible one whose window is on it (its region
    /// may be empty for now, while the taskbar shows).
    private static IntPtr Search(Win32.RECT monitor)
    {
        IntPtr found = IntPtr.Zero;
        var name = new StringBuilder(64);
        Win32.EnumWindows((h, _) =>
        {
            name.Clear();
            if (Win32.GetClassName(h, name, name.Capacity) <= 0 || !name.ToString().StartsWith(ClassPrefix, StringComparison.Ordinal) || !Win32.IsWindowVisible(h)) return true;
            if (!Win32.GetWindowRect(h, out var w) || w.Right <= monitor.Left || w.Left >= monitor.Right || w.Bottom <= monitor.Top || w.Top >= monitor.Bottom) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}

/// What shows above the cards: the island's box and the taskbar's, each null while it doesn't show.
internal readonly record struct Above(Rect? Island, Rect? Taskbar)
{
    public IEnumerable<Rect> Boxes
    {
        get
        {
            if (Island is { } i) yield return i;
            if (Taskbar is { } t) yield return t;
        }
    }
}
