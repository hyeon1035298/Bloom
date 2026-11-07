using System;
using System.Windows;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Ocr;

internal enum RecordSurfaceKind { Plain, Pill, Island }

/// What 화면 녹화's bar grows out of: the auto-hide island while it shows, the taskbar's pill while Pill taskbar shapes
/// the taskbar, else nothing (a bar of its own under the area). `Box` is that shape in physical pixels.
internal readonly record struct RecordSurface(RecordSurfaceKind Kind, Rect Box)
{
    /// The shape on the monitor that holds `area` (physical).
    public static RecordSurface Find(ScreenRect area)
    {
        IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 }, Win32.MONITOR_DEFAULTTONEAREST);
        if (NoticeIsland.FindPixels(mon) is { } island) return new(RecordSurfaceKind.Island, island);
        // the pill is the primary taskbar's: on another monitor the plain bar stays by the area
        IntPtr primary = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        if (mon == primary && PillBox() is { } pill) return new(RecordSurfaceKind.Pill, pill);
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
}
