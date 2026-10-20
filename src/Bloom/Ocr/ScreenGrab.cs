using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Ocr;

/// A rectangle of the screen in physical pixels (virtual-screen coordinates).
internal readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width < 4 || Height < 4;
}

/// Screen pictures for OCR: the whole desktop frozen under one dimmed window per monitor, a
/// rectangle dragged over it (Esc cancels); and later grabs of the same rectangle for live mode.
/// With `draw` (화면 캡처) the screen stays frozen after the drag for the pen and highlighter (DrawLayer).
internal static class ScreenGrab
{
    /// The picked rectangle and its pixels (BGRA), or null when cancelled. With `draw`, the pixels have the ink in
    /// them and `then` is what the drawing bar chose (capture · pin · ask · palette · save); otherwise `then` is null.
    public static Task<(ScreenRect rect, byte[] bgra, string? then)?> Pick(string hint, bool draw = false)
    {
        var owner = Dispatcher.CurrentDispatcher;
        var preferences = DrawPreferences.Capture();
        var theme = DrawTheme.Capture();
        var persist = DrawPreferences.PersistOn(owner, changed => changed.Save());
        return CaptureDispatcher.Run(abort => PickOnDispatcher(hint, draw, preferences, theme, persist, abort));
    }

    private static async Task<(ScreenRect rect, byte[] bgra, string? then)?> PickOnDispatcher(string hint, bool draw,
        DrawPreferences preferences, DrawTheme theme, Action<DrawPreferences> persist, CancellationToken abort)
    {
        int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77), vw = Win32.GetSystemMetrics(78), vh = Win32.GetSystemMetrics(79);
        var desktop = GrabDesktop(new ScreenRect(vx, vy, vw, vh));
        if (desktop is null) return null;
        var done = new TaskCompletionSource<(ScreenRect rect, string? then)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<Window>();
        DrawLayer? layer = null;
        void CloseWindows()
        {
            foreach (var w in windows.ToArray())
                try { w.Close(); }
                catch (InvalidOperationException) { /* already closing */ }
        }
        void Finish(ScreenRect? r, string? then = null)
        {
            // done first: closing an overlay deactivates it, and its Deactivated calls back in here
            if (!done.TrySetResult(r is { } x && then != "" ? (x, then) : null)) return;
            CloseWindows();
        }
        // Only CaptureDispatcher cancels this token, on this same STA after a routed-input callback fails.
        using var cancellation = abort.Register(() => Finish(null));
        try
        {
            foreach (IntPtr mon in Launcher.Monitors())
            {
                var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
                if (!Win32.GetMonitorInfo(mon, ref info)) continue;
                var m = info.rcMonitor;
                windows.Add(Overlay(desktop, new ScreenRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top), vx, vy, hint, r => Finish(r),
                    !draw ? null : (canvas, sel, room, r) => layer = new DrawLayer(canvas, sel, room, then => Finish(r, then), preferences, theme, persist), () => layer,
                    DrawTheme.Brush(theme.Accent)));
            }
            if (windows.Count == 0) return null;
            foreach (var w in windows) w.Show();
            // the window under the mouse takes the keyboard (Esc)
            Win32.GetCursorPos(out var cur);
            foreach (var w in windows)
                if (w.Tag is ScreenRect r && cur.X >= r.X && cur.X < r.X + r.Width && cur.Y >= r.Y && cur.Y < r.Y + r.Height)
                {
                    w.Activate();
                    Win32.ForceForeground(new WindowInteropHelper(w).Handle);
                }
            var picked = await done.Task;
            if (picked is not { rect.IsEmpty: false } p) return null;
            var rect = p.rect;
            var px = Crop(desktop, rect.X - vx, rect.Y - vy, rect.Width, rect.Height);
            return (rect, layer?.Merge(px, rect.Width, rect.Height) ?? px, p.then);
        }
        finally
        {
            CloseWindows();
        }
    }

    /// One monitor's frozen picture. `startDraw` (화면 캡처) turns a finished drag into the drawing layer instead of
    /// the answer; `drawing` is that layer once any monitor has it (the other monitors then stay still).
    internal static Window Overlay(BitmapSource desktop, ScreenRect mon, int vx, int vy, string hint, Action<ScreenRect?> finish,
        Func<Canvas, Rect, Size, ScreenRect, DrawLayer>? startDraw, Func<DrawLayer?> drawing, Brush accent)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            // only the one under the mouse is activated (in Pick): showing the next monitor's one activated would
            // deactivate the first, which reads as "another app came forward" and cancelled Lens at once
            ShowActivated = false,
            Background = Brushes.Black,
            Cursor = Cursors.Cross,
            Tag = mon,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Width = 100,
            Height = 100,
        };
        var root = new Grid();
        var image = new Image { Source = new CroppedBitmap(desktop, new Int32Rect(mon.X - vx, mon.Y - vy, mon.Width, mon.Height)), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        root.Children.Add(image);
        var canvas = new Canvas();
        var dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
        var frame = new Rectangle { Stroke = accent, StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
        var size = new SmoothBorder
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x19, 0x1C)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 3),
            Child = Theme.T("", 11.5, Brushes.White, Theme.Medium, false),
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(dim);
        canvas.Children.Add(frame);
        canvas.Children.Add(size);
        root.Children.Add(canvas);
        var tip = new SmoothBorder
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x1E, 0x19, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            IsHitTestVisible = false,
            Child = Theme.T(Tr($"{hint}  ·  Esc 취소", $"{hint}  ·  Esc cancels"), 13, Brushes.White, Theme.Medium, false),
        };
        root.Children.Add(tip);
        w.Content = root;

        double scale = 1;
        Point? from = null;
        void Paint(Rect sel)
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new RectangleGeometry(new Rect(0, 0, w.ActualWidth, w.ActualHeight)));
            if (sel.Width > 0) g.Children.Add(new RectangleGeometry(sel));
            dim.Data = g;
            bool any = sel.Width > 0;
            frame.Visibility = size.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (!any) return;
            Canvas.SetLeft(frame, sel.X);
            Canvas.SetTop(frame, sel.Y);
            frame.Width = sel.Width;
            frame.Height = sel.Height;
            ((TextBlock)size.Child).Text = $"{Math.Round(sel.Width * scale)} × {Math.Round(sel.Height * scale)}";
            Canvas.SetLeft(size, sel.X);
            Canvas.SetTop(size, sel.Y + sel.Height + 6 > w.ActualHeight - 24 ? sel.Y - 26 : sel.Y + sel.Height + 6);
        }
        Rect Sel(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        w.SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            // Capture has its own UI thread but still shares WPF's renderer. Keep the animated
            // full-screen selection/ink surface off the hardware path without changing other windows.
            if (HwndSource.FromHwnd(h)?.CompositionTarget is { } target)
            {
                target.RenderMode = RenderMode.SoftwareOnly;
                Bloom.Core.Log.Write($"capture overlay {mon.Width}x{mon.Height} render={target.RenderMode}");
            }
            Glass.ToolWindow(w);
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, mon.X, mon.Y, mon.Width, mon.Height, Win32.SWP_NOACTIVATE);
        };
        // moved onto a monitor with another scale, WPF resizes the window by the scale ratio (a 100 % monitor beside a
        // 150 % one got a 1280x720 overlay on its 1920x1080): cover the monitor again
        w.DpiChanged += (_, _) => Win32.SetWindowPos(new WindowInteropHelper(w).Handle, Win32.HWND_TOPMOST, mon.X, mon.Y, mon.Width, mon.Height, Win32.SWP_NOACTIVATE);
        w.Loaded += (_, _) =>
        {
            scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
            Paint(new Rect());
        };
        w.SizeChanged += (_, _) => Paint(new Rect());
        w.MouseLeftButtonDown += (_, e) =>
        {
            if (drawing() is not null) return;
            from = e.GetPosition(root);
            w.CaptureMouse();
            tip.Visibility = Visibility.Collapsed;
        };
        w.MouseMove += (_, e) =>
        {
            if (from is { } a) Paint(Sel(a, e.GetPosition(root)));
        };
        w.MouseLeftButtonUp += (_, e) =>
        {
            if (from is not { } a) return;
            w.ReleaseMouseCapture();
            var r = Sel(a, e.GetPosition(root));
            from = null;
            double s = VisualTreeHelper.GetDpi(w).DpiScaleX;
            var picked = new ScreenRect(mon.X + (int)Math.Round(r.X * s), mon.Y + (int)Math.Round(r.Y * s), (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
            if (startDraw is null || picked.IsEmpty)
            {
                finish(picked);
                return;
            }
            size.Visibility = Visibility.Collapsed; // the bar takes its place
            ((TextBlock)tip.Child).Text = DrawLayer.Hint;
            tip.Visibility = Visibility.Visible;
            startDraw(canvas, r, new Size(w.ActualWidth, w.ActualHeight), picked);
        };
        w.MouseRightButtonUp += (_, _) =>
        {
            if (drawing() is null) finish(null); // a stray right click must not throw the drawing away
        };
        w.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) finish(null);
            else if (drawing() is { } d && d.HandleKey(e)) e.Handled = true;
        };
        w.Deactivated += (_, _) =>
        {
            // another app came forward (Alt+Tab): give up rather than leave a frozen screen behind
            if (from is null && !w.IsMouseOver && Win32.GetForegroundWindow() is var fg && !IsOurs(fg)) finish(null);
        };
        return w;
    }

    private static bool IsOurs(IntPtr hwnd)
    {
        _ = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == Environment.ProcessId;
    }

    /// The screen's pixels in this rectangle (BGRA, opaque). Windows marked excluded from capture
    /// (our live-translation panel) are not in it.
    public static byte[]? Grab(ScreenRect r) => Capture(r, bits =>
    {
        var px = new byte[r.Width * r.Height * 4];
        Marshal.Copy(bits, px, 0, px.Length);
        return px;
    });

    /// The whole desktop for the picker, from the screen's bitmap straight into WIC's own copy: no desktop-sized array
    /// on the managed heap, whose room the GC kept long after Lens closed (28 MB for two 2560x1440 monitors).
    internal static BitmapSource? GrabDesktop(ScreenRect r) => Capture(r, bits => FrozenPixels(bits, r.Width, r.Height));

    /// WIC's own frozen copy of the BGRA pixels at `pixels`: that memory may go as soon as this returns.
    internal static BitmapSource FrozenPixels(IntPtr pixels, int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * height * 4, width * 4);
        source.Freeze();
        return source;
    }

    /// The screen's pixels in `r`, made opaque, handed to `use` in GDI's bitmap, which is gone once `use` returns.
    private static T? Capture<T>(ScreenRect r, Func<IntPtr, T> use) where T : class
    {
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        var bi = new Win32.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = r.Width, biHeight = -r.Height, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = Win32.CreateDIBSection(screen, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        try
        {
            if (bmp == IntPtr.Zero) return null;
            IntPtr old = SelectObject(mem, bmp);
            bool ok = BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */);
            SelectObject(mem, old);
            if (!ok) return null;
            Opaque(bits, r.Width * r.Height);
            return use(bits);
        }
        finally
        {
            if (bmp != IntPtr.Zero) Win32.DeleteObject(bmp);
            DeleteDC(mem);
            _ = Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// BitBlt leaves every alpha byte 0: each pixel made opaque where it lies.
    private static unsafe void Opaque(IntPtr bits, int pixels)
    {
        uint* p = (uint*)bits;
        for (int i = 0; i < pixels; i++) p[i] |= 0xFF000000;
    }

    internal static byte[] Crop(BitmapSource src, int x, int y, int w, int h)
    {
        var dst = new byte[w * h * 4];
        // Coordinates are physical desktop pixels, independent of each overlay's DIP scale.
        src.CopyPixels(new Int32Rect(x, y, w, h), dst, w * 4, 0);
        return dst;
    }

    public static byte[] Crop(byte[] src, int srcWidth, int x, int y, int w, int h)
    {
        var dst = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(src, ((y + row) * srcWidth + x) * 4, dst, row * w * 4, w * 4);
        return dst;
    }

    /// Small screen text reads better twice as large (the OCR model was trained on bigger glyphs).
    public static (byte[] bgra, int width, int height, double scale) ForOcr(byte[] bgra, int width, int height)
    {
        if (height > 600 || width * 2 > 4000 || height * 2 > 4000) return (bgra, width, height, 1);
        var src = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var big = new TransformedBitmap(src, new ScaleTransform(2, 2));
        var px = new byte[width * 2 * height * 2 * 4];
        big.CopyPixels(px, width * 2 * 4, 0);
        return (px, width * 2, height * 2, 2);
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
