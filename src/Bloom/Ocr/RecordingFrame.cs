using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Bloom.Native;

namespace Bloom.Ocr;

/// The area 화면 녹화 records, shown on the screen and never in the video. Before it starts, the rest of the screen is
/// dimmed (which also keeps clicks off the apps around it) and a line in the accent runs round the area; while it
/// records the dim goes and the line turns red. Two plain popups, not WPF windows: their pixels are physical and their
/// colour is their class's brush, so they span monitors of any scale and hold no picture of their own (a WPF window the
/// size of the screen keeps a surface that size in Bloom). The line lies outside the area, and both are kept out of
/// screen captures.
internal sealed class RecordingFrame
{
    private readonly ScreenRect area;
    private readonly int thickness;
    private IntPtr shade, line;

    public RecordingFrame(ScreenRect area)
    {
        this.area = area;
        var center = new Win32.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 };
        IntPtr mon = Win32.MonitorFromPoint(center, Win32.MONITOR_DEFAULTTONEAREST);
        if (Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        thickness = Thickness(dpi / 96.0);
    }

    /// The line's width in pixels: 2 DIPs at the area's scale, never under 2 px.
    internal static int Thickness(double scale) => Math.Max(2, (int)Math.Round(2 * scale));

    /// Dimmed around the area, the accent line round it.
    public void Show(Color accent)
    {
        int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77), vw = Win32.GetSystemMetrics(78), vh = Win32.GetSystemMetrics(79);
        shade = Popup(Color.FromRgb(0, 0, 0), vx, vy, vw, vh, 0x70, clickThrough: false, Hole(vw, vh, area.X - vx, area.Y - vy, area.Width, area.Height));
        line = Line(accent);
    }

    /// Recording: no dim, the line red.
    public void Recording(Color red)
    {
        Destroy(ref shade);
        Destroy(ref line);
        line = Line(red);
    }

    public void Close()
    {
        Destroy(ref shade);
        Destroy(ref line);
    }

    private IntPtr Line(Color c)
    {
        int t = thickness;
        int w = area.Width + 2 * t, h = area.Height + 2 * t;
        return Popup(c, area.X - t, area.Y - t, w, h, 0xFF, clickThrough: true, Hole(w, h, t, t, area.Width, area.Height));
    }

    /// A `w` × `h` region with the given rectangle cut out of it (the system owns it once set).
    private static IntPtr Hole(int w, int h, int x, int y, int hw, int hh)
    {
        IntPtr all = CreateRectRgn(0, 0, w, h), hole = CreateRectRgn(x, y, x + hw, y + hh);
        CombineRgn(all, all, hole, 4 /* RGN_DIFF */);
        Win32.DeleteObject(hole);
        return all;
    }

    private static IntPtr Popup(Color c, int x, int y, int w, int h, byte alpha, bool clickThrough, IntPtr region)
    {
        uint ex = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | (clickThrough ? WS_EX_TRANSPARENT : 0);
        IntPtr hwnd = CreateWindowEx(ex, ClassFor(c), null, WS_POPUP, x, y, w, h, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            Win32.DeleteObject(region);
            return IntPtr.Zero;
        }
        SetLayeredWindowAttributes(hwnd, 0, alpha, 2 /* LWA_ALPHA */);
        if (SetWindowRgn(hwnd, region, false) == 0) Win32.DeleteObject(region);
        SetWindowDisplayAffinity(hwnd, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
        Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
        return hwnd;
    }

    private static void Destroy(ref IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
        hwnd = IntPtr.Zero;
    }

    // one window class a colour, its brush the background DefWindowProc paints: no window procedure of our own
    private static readonly Dictionary<uint, string> classes = new();

    private static string ClassFor(Color c)
    {
        uint rgb = c.R | ((uint)c.G << 8) | ((uint)c.B << 16);
        if (classes.TryGetValue(rgb, out var name)) return name;
        name = $"Bloom.RecordingFrame.{rgb:X6}";
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, 32512 /* IDC_ARROW */),
            hbrBackground = CreateSolidBrush(rgb),
            lpszClassName = name,
        };
        RegisterClassEx(ref wc);
        classes[rgb] = name;
        return name;
    }

    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint rgb);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
