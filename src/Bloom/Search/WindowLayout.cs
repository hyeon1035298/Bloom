using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

/// One place a window can go: a share of the work area (left half, top-right quarter, …) or a move (centre, the next
/// screen). X/Y/W/H are fractions of the work area (also what its icon draws); Kind says when it is not a plain share;
/// a Glyph replaces the drawn icon where a box can't show it (the next screen, restore).
internal sealed record Layout(string Key, string Ko, string En, string Words, double X = 0, double Y = 0, double W = 1, double H = 1, string Kind = "share", char Glyph = '\0')
{
    public string Id => "layout:" + Key;

    public MatchTarget Match { get; } = MatchTarget.Named(Ko, En, Words);
}

/// 창 배치: puts the window in front onto a part of its screen's work area (the taskbar stays clear), the way Windows'
/// own snapping would, but from the search line or a shortcut. The invisible resize border Windows 10/11 adds around
/// most windows is taken into account, so halves meet edge to edge.
internal static class WindowLayout
{
    private const string Window = "창 window ";

    public static readonly Layout[] All =
    {
        new("left", "왼쪽 절반", "Left Half", Window + "left half 왼쪽 절반 반 좌측 왼편", W: 0.5),
        new("right", "오른쪽 절반", "Right Half", Window + "right half 오른쪽 절반 반 우측 오른편", X: 0.5, W: 0.5),
        new("top", "위쪽 절반", "Top Half", Window + "top half 위쪽 절반 위 상단", H: 0.5),
        new("bottom", "아래쪽 절반", "Bottom Half", Window + "bottom half 아래쪽 절반 아래 하단", Y: 0.5, H: 0.5),
        new("top-left", "왼쪽 위 1/4", "Top Left 1/4", Window + "top left quarter 왼쪽 위 사분의 일 1/4 쿼터", W: 0.5, H: 0.5),
        new("top-right", "오른쪽 위 1/4", "Top Right 1/4", Window + "top right quarter 오른쪽 위 사분의 일 1/4 쿼터", X: 0.5, W: 0.5, H: 0.5),
        new("bottom-left", "왼쪽 아래 1/4", "Bottom Left 1/4", Window + "bottom left quarter 왼쪽 아래 사분의 일 1/4 쿼터", Y: 0.5, W: 0.5, H: 0.5),
        new("bottom-right", "오른쪽 아래 1/4", "Bottom Right 1/4", Window + "bottom right quarter 오른쪽 아래 사분의 일 1/4 쿼터", X: 0.5, Y: 0.5, W: 0.5, H: 0.5),
        new("left-third", "왼쪽 1/3", "Left 1/3", Window + "left third 왼쪽 삼분의 일 1/3", W: 1 / 3.0),
        new("center-third", "가운데 1/3", "Center 1/3", Window + "center middle third 가운데 중앙 삼분의 일 1/3", X: 1 / 3.0, W: 1 / 3.0),
        new("right-third", "오른쪽 1/3", "Right 1/3", Window + "right third 오른쪽 삼분의 일 1/3", X: 2 / 3.0, W: 1 / 3.0),
        new("left-two-thirds", "왼쪽 2/3", "Left 2/3", Window + "left two thirds 왼쪽 삼분의 이 2/3", W: 2 / 3.0),
        new("right-two-thirds", "오른쪽 2/3", "Right 2/3", Window + "right two thirds 오른쪽 삼분의 이 2/3", X: 1 / 3.0, W: 2 / 3.0),
        new("maximize", "최대화", "Maximize", Window + "maximize full screen 최대화 전체 화면 크게 꽉", Kind: "max"),
        new("almost", "거의 최대화", "Almost Maximize", Window + "almost maximize large 거의 최대화 크게 여백", X: 0.05, Y: 0.05, W: 0.9, H: 0.9),
        new("center", "가운데로", "Center", Window + "center middle 가운데 중앙 가운데로", X: 0.25, Y: 0.2, W: 0.5, H: 0.6, Kind: "center"),
        new("next-screen", "다음 화면으로", "Move to Next Display", Window + "next screen monitor display move 다음 화면 모니터 옮기기 다른 화면", Kind: "next", Glyph: '\uE7F4'),
        new("restore", "원래 크기로", "Restore Size", Window + "restore unmaximize 원래 크기 되돌리기 복원", Kind: "restore", Glyph: '\uE73F'),
    };

    public static Layout? Find(string id) => Array.Find(All, l => l.Id == id);

    /// The window a layout goes to: the one in front, or — while Bloom itself is in front (the launcher) — the one that
    /// was in front before it. Never the desktop, the taskbar or a Bloom window.
    public static IntPtr Target(IntPtr before)
    {
        IntPtr fg = Win32.GetForegroundWindow();
        _ = Win32.GetWindowThreadProcessId(fg, out uint pid);
        IntPtr h = GetAncestor(pid == Environment.ProcessId ? before : fg, 2 /* GA_ROOT */);
        return Movable(h) ? h : IntPtr.Zero;
    }

    private static bool Movable(IntPtr h)
    {
        if (h == IntPtr.Zero || !Win32.IsWindow(h) || !Win32.IsWindowVisible(h)) return false;
        if (Win32.GetWindowThreadProcessId(h, out uint pid) == 0 || pid == Environment.ProcessId) return false;
        var cls = new System.Text.StringBuilder(64);
        _ = Win32.GetClassName(h, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    public static bool Apply(Layout l, IntPtr h)
    {
        if (!Movable(h)) return false;
        switch (l.Kind)
        {
            case "max":
                Win32.ShowWindow(h, 3 /* SW_MAXIMIZE */);
                break;
            case "restore":
                Win32.ShowWindow(h, Win32.SW_RESTORE);
                break;
            case "center":
                Unmax(h);
                if (!Win32.GetWindowRect(h, out var r)) return false;
                var c = Work(h);
                int w = Math.Min(r.Right - r.Left, c.Right - c.Left), hh = Math.Min(r.Bottom - r.Top, c.Bottom - c.Top);
                Place(h, c.Left + (c.Right - c.Left - w) / 2, c.Top + (c.Bottom - c.Top - hh) / 2, w, hh, false);
                break;
            case "next":
                if (!NextScreen(h)) return false;
                break;
            default:
                Unmax(h);
                var a = Work(h);
                double aw = a.Right - a.Left, ah = a.Bottom - a.Top;
                int x = a.Left + (int)Math.Round(aw * l.X), y = a.Top + (int)Math.Round(ah * l.Y);
                int right = a.Left + (int)Math.Round(aw * (l.X + l.W)), bottom = a.Top + (int)Math.Round(ah * (l.Y + l.H));
                Place(h, x, y, right - x, bottom - y, true);
                break;
        }
        Win32.ForceForeground(h);
        return true;
    }

    /// The same share of the next screen (by position, left to right), keeping its maximized state.
    private static bool NextScreen(IntPtr h)
    {
        var screens = new List<Win32.RECT>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(mon, ref info)) screens.Add(info.rcWork);
            return true;
        }, IntPtr.Zero);
        if (screens.Count < 2) return false;
        screens.Sort((p, q) => p.Left != q.Left ? p.Left.CompareTo(q.Left) : p.Top.CompareTo(q.Top));
        var now = Work(h);
        int at = screens.FindIndex(s => s.Left == now.Left && s.Top == now.Top);
        var to = screens[(at + 1) % screens.Count];
        bool max = IsZoomed(h);
        Unmax(h);
        if (!Win32.GetWindowRect(h, out var r)) return false;
        // the window keeps where it sits on its screen, as a share of it, so a left half stays a left half
        double fx = (r.Left - now.Left) / (double)(now.Right - now.Left), fy = (r.Top - now.Top) / (double)(now.Bottom - now.Top);
        double fw = (r.Right - r.Left) / (double)(now.Right - now.Left), fh = (r.Bottom - r.Top) / (double)(now.Bottom - now.Top);
        int tw = to.Right - to.Left, th = to.Bottom - to.Top;
        Win32.SetWindowPos(h, IntPtr.Zero, to.Left + (int)(fx * tw), to.Top + (int)(fy * th), Math.Min(tw, (int)(fw * tw)), Math.Min(th, (int)(fh * th)), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        if (max) Win32.ShowWindow(h, 3 /* SW_MAXIMIZE */);
        return true;
    }

    private static void Unmax(IntPtr h)
    {
        if (IsZoomed(h) || Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
    }

    private static Win32.RECT Work(IntPtr h)
    {
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONEAREST), ref info);
        return info.rcWork;
    }

    /// Moves the window so what can be seen of it fills the box: the invisible border (window rect − DWM's frame
    /// bounds) is added back on each side.
    private static void Place(IntPtr h, int x, int y, int w, int hgt, bool fit)
    {
        int l = 0, t = 0, r = 0, b = 0;
        if (fit && Win32.GetWindowRect(h, out var outer) && DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var seen, Marshal.SizeOf<Win32.RECT>()) == 0)
        {
            l = seen.Left - outer.Left;
            t = seen.Top - outer.Top;
            r = outer.Right - seen.Right;
            b = outer.Bottom - seen.Bottom;
        }
        Win32.SetWindowPos(h, IntPtr.Zero, x - l, y - t, w + l + r, hgt + t + b, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Win32.RECT value, int size);
}
