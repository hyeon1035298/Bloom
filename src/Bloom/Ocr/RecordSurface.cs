using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Ocr;

internal enum RecordSurfaceKind { Plain, Pill, Island }

/// Where 화면 녹화's bar goes: by the auto-hide island at the top (Island), by Pill taskbar's pill at the bottom (Pill),
/// else a bar of its own under the area (Plain). `Box` is the island or pill in physical pixels. `Edge` (physical y)
/// is the taskbar's edge once it is all the way out: the bar floats under it at the top, over it at the bottom, so the
/// taskbar coming out never covers it.
internal readonly record struct RecordSurface(RecordSurfaceKind Kind, Rect Box, double Edge = 0)
{
    /// The shape on the monitor that holds `area` (physical).
    public static RecordSurface Find(ScreenRect area)
    {
        IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref info);
        IntPtr taskbar = TaskbarOn(mon);
        if (NoticeIsland.FindPixels(mon) is { } island)
        {
            // the island's window spans the pill it opens into; the taskbar reaches as far once it is out
            double under = island.Bottom;
            IntPtr w = NoticeIsland.WindowOn(mon);
            if (w != IntPtr.Zero && Win32.GetWindowRect(w, out var iw)) under = Math.Max(under, iw.Bottom);
            if (OutEdge(taskbar, info.rcMonitor) is { top: true } t) under = Math.Max(under, t.edge);
            return new(RecordSurfaceKind.Island, island, under);
        }
        // the pill is the primary taskbar's: on another monitor the plain bar stays by the area
        IntPtr primary = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        if (mon == primary && PillBox() is { } pill)
        {
            double over = pill.Top;
            if (OutEdge(taskbar, info.rcMonitor) is { top: false } b) over = Math.Min(over, b.edge);
            return new(RecordSurfaceKind.Pill, pill, over);
        }
        return new(RecordSurfaceKind.Plain, Rect.Empty);
    }

    /// The primary taskbar's pill (physical), null when the taskbar is Windows' own or hidden.
    private static Rect? PillBox()
    {
        IntPtr h = NoticeIsland.TaskbarWindow;
        if (h == IntPtr.Zero || !Win32.IsWindowVisible(h) || !Win32.GetWindowRect(h, out var window)) return null;
        return Pill(window, Win32.GetWindowRgnBox(h, out var box), box);
    }

    /// Pill taskbar cuts the taskbar window down to its pill (and the tray beside it) with a window region; a taskbar
    /// with no region, an empty one or one as wide as the window is Windows' own. `box` is the region's box in the
    /// window, `regionKind` as GetWindowRgnBox answers.
    internal static Rect? Pill(Win32.RECT window, int regionKind, Win32.RECT box)
    {
        if (regionKind is not (2 or 3) || box.Right <= box.Left || box.Bottom <= box.Top) return null;
        if (box.Right - box.Left >= window.Right - window.Left - 2) return null;
        return new Rect(window.Left + box.Left, window.Top + box.Top, box.Right - box.Left, box.Bottom - box.Top);
    }

    /// The taskbar's inner edge once it is all the way out (an auto-hidden one is mostly off the screen now): at the top,
    /// the monitor's top plus what it shows of its height; at the bottom, the monitor's bottom less that. What it shows is
    /// its region (Pill taskbar's pill) when it has one, else the whole window. `top`: which edge it is docked to.
    internal static (bool top, double edge) OutEdge(Win32.RECT monitor, Win32.RECT window, int regionKind, Win32.RECT box)
    {
        bool top = window.Top + window.Bottom < monitor.Top + monitor.Bottom;
        bool region = regionKind is 2 or 3 && box.Bottom > box.Top;
        int height = window.Bottom - window.Top;
        return top
            ? (true, monitor.Top + (region ? box.Bottom : height))
            : (false, monitor.Bottom - height + (region ? box.Top : 0));
    }

    private static (bool top, double edge)? OutEdge(IntPtr taskbar, Win32.RECT monitor) =>
        taskbar != IntPtr.Zero && Win32.GetWindowRect(taskbar, out var window)
            ? OutEdge(monitor, window, Win32.GetWindowRgnBox(taskbar, out var box), box)
            : null;

    /// The taskbar on `monitor`: the primary one, or a secondary one Windows keeps on another monitor.
    private static IntPtr TaskbarOn(IntPtr monitor)
    {
        IntPtr primary = NoticeIsland.TaskbarWindow;
        if (primary != IntPtr.Zero && Win32.MonitorFromWindow(primary, Win32.MONITOR_DEFAULTTONEAREST) == monitor) return primary;
        IntPtr found = IntPtr.Zero;
        var name = new StringBuilder(32);
        Win32.EnumWindows((h, _) =>
        {
            name.Clear();
            if (Win32.GetClassName(h, name, name.Capacity) <= 0 || name.ToString() != "Shell_SecondaryTrayWnd") return true;
            if (Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONEAREST) != monitor) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
