using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Ocr;

/// The colour picker: the screen frozen under one window per monitor; the arrow's tip is the pixel picked, and under the
/// arrow a magnifier (11 × 11 pixels, ×10, a cross on that pixel) with its colour; click picks, ←↑↓→ nudge one pixel,
/// Enter picks, Esc / right click cancels.
internal static class ColorPick
{
    private const int Span = 11, Zoom = 10;

    public static async Task<Color?> Pick()
    {
        int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77), vw = Win32.GetSystemMetrics(78), vh = Win32.GetSystemMetrics(79);
        // WIC's copy alone (no desktop-sized array beside it, 28 MB for two 2560 × 1440 monitors); a pixel is read from it
        var desktop = ScreenGrab.GrabDesktop(new ScreenRect(vx, vy, vw, vh));
        if (desktop is null) return null;
        var one = new byte[4];
        var done = new TaskCompletionSource<Color?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<Window>();
        void Finish(Color? c)
        {
            if (!done.TrySetResult(c)) return;
            foreach (var w in windows.ToArray())
                try
                {
                    w.Close();
                }
                catch (InvalidOperationException)
                {
                    // already closing
                }
        }
        Color At(int x, int y)
        {
            x = Math.Clamp(x - vx, 0, vw - 1);
            y = Math.Clamp(y - vy, 0, vh - 1);
            desktop.CopyPixels(new Int32Rect(x, y, 1, 1), one, 4, 0);
            return Color.FromRgb(one[2], one[1], one[0]);
        }
        foreach (IntPtr mon in Launcher.Monitors())
        {
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (!Win32.GetMonitorInfo(mon, ref info)) continue;
            var m = info.rcMonitor;
            windows.Add(Overlay(desktop, new ScreenRect(m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top), vx, vy, At, Finish));
        }
        foreach (var w in windows) w.Show();
        Win32.GetCursorPos(out var cur);
        foreach (var w in windows)
            if (w.Tag is ScreenRect r && cur.X >= r.X && cur.X < r.X + r.Width && cur.Y >= r.Y && cur.Y < r.Y + r.Height)
            {
                w.Activate();
                Win32.ForceForeground(new WindowInteropHelper(w).Handle);
            }
        var picked = await done.Task;
        Core.MemoryTrim.AfterBigWork("colour picker"); // the desktop and the overlays wait for a GC now
        return picked;
    }

    private static Window Overlay(BitmapSource desktop, ScreenRect mon, int vx, int vy, Func<int, int, Color> at, Action<Color?> finish)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Background = Brushes.Black,
            Cursor = Cursors.Arrow, // its tip is the pixel: nothing to guess
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
        root.Children.Add(canvas);

        // the magnifier: zoomed pixels, a cross on the middle one, the colour and its hex under it
        var zoomed = new Image { Width = Span * Zoom, Height = Span * Zoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(zoomed, BitmapScalingMode.NearestNeighbor);
        var lens = new Grid { Width = Span * Zoom, Height = Span * Zoom, ClipToBounds = true };
        lens.Children.Add(zoomed);
        lens.Children.Add(new Rectangle { Width = Zoom + 2, Height = Zoom + 2, Stroke = Brushes.White, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        lens.Children.Add(new Rectangle { Width = Zoom + 4, Height = Zoom + 4, Stroke = Brushes.Black, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 });
        var swatch = new SmoothBorder { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0) };
        var hex = Theme.T("", 12.5, Brushes.White, Theme.Semi, false);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 7) };
        line.Children.Add(swatch);
        line.Children.Add(hex);
        var card = new StackPanel();
        card.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true, Child = lens });
        card.Children.Add(line);
        var mag = new SmoothBorder
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x19, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Child = card,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(mag);
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
            Child = Theme.T(Tr("색을 찍을 곳을 누르세요 · 방향키로 한 칸씩 · Esc 취소", "Click where to pick · arrow keys move one pixel · Esc cancels"), 13, Brushes.White, Theme.Medium, false),
        };
        root.Children.Add(tip);
        w.Content = root;

        // physical pixels per WPF unit on this monitor, from the window's own size (right after a move between monitors
        // of different scaling, the reported DPI can still be the old one)
        double Scale() => root.ActualWidth > 0 ? mon.Width / root.ActualWidth : VisualTreeHelper.GetDpi(w).DpiScaleX;
        int px = mon.X + mon.Width / 2, py = mon.Y + mon.Height / 2; // the physical pixel under the arrow's tip
        void Show()
        {
            int sx = Math.Clamp(px - vx - Span / 2, 0, desktop.PixelWidth - Span), sy = Math.Clamp(py - vy - Span / 2, 0, desktop.PixelHeight - Span);
            zoomed.Source = new CroppedBitmap(desktop, new Int32Rect(sx, sy, Span, Span));
            var c = at(px, py);
            swatch.Background = new SolidColorBrush(c);
            hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            // the magnifier under the arrow, centred on its tip; above it near the bottom edge
            double scale = Scale(), lx = (px - mon.X) / scale, ly = (py - mon.Y) / scale;
            double mw = mag.ActualWidth > 0 ? mag.ActualWidth : Span * Zoom + 2, mh = mag.ActualHeight > 0 ? mag.ActualHeight : Span * Zoom + 34;
            double arrow = SystemParameters.CursorHeight * 0.7; // the arrow takes the top of its cursor box
            double left = Math.Clamp(lx - mw / 2, 8, Math.Max(8, w.ActualWidth - mw - 8));
            double top = ly + arrow + 6 + mh > w.ActualHeight - 8 ? ly - mh - 12 : ly + arrow + 6;
            Canvas.SetLeft(mag, left);
            Canvas.SetTop(mag, top);
        }
        w.SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            Glass.ToolWindow(w);
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, mon.X, mon.Y, mon.Width, mon.Height, Win32.SWP_NOACTIVATE);
        };
        // a monitor with another scale: WPF resizes the window by the scale ratio on the move, so cover the monitor again
        w.DpiChanged += (_, _) => Win32.SetWindowPos(new WindowInteropHelper(w).Handle, Win32.HWND_TOPMOST, mon.X, mon.Y, mon.Width, mon.Height, Win32.SWP_NOACTIVATE);
        w.Loaded += (_, _) =>
        {
            Win32.GetCursorPos(out var c);
            if (c.X >= mon.X && c.X < mon.X + mon.Width && c.Y >= mon.Y && c.Y < mon.Y + mon.Height)
            {
                px = c.X;
                py = c.Y;
            }
            Show();
        };
        w.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(root);
            double scale = Scale();
            px = Math.Clamp(mon.X + (int)Math.Floor(p.X * scale), mon.X, mon.X + mon.Width - 1);
            py = Math.Clamp(mon.Y + (int)Math.Floor(p.Y * scale), mon.Y, mon.Y + mon.Height - 1);
            tip.Visibility = Visibility.Collapsed;
            Show();
        };
        w.MouseLeftButtonUp += (_, _) => finish(at(px, py));
        w.MouseRightButtonUp += (_, _) => finish(null);
        w.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    finish(null);
                    return;
                case Key.Enter:
                    finish(at(px, py));
                    return;
                case Key.Left: px--; break;
                case Key.Right: px++; break;
                case Key.Up: py--; break;
                case Key.Down: py++; break;
                default: return;
            }
            px = Math.Clamp(px, mon.X, mon.X + mon.Width - 1);
            py = Math.Clamp(py, mon.Y, mon.Y + mon.Height - 1);
            Win32.SetCursorPos(px, py);
            Show();
            e.Handled = true;
        };
        w.SizeChanged += (_, _) => Show(); // the window reached the monitor's size: place the magnifier again
        w.Deactivated += (_, _) =>
        {
            if (!w.IsMouseOver && Win32.GetForegroundWindow() is var fg && !Ours(fg)) finish(null);
        };
        return w;
    }

    private static bool Ours(IntPtr hwnd)
    {
        _ = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == Environment.ProcessId;
    }
}
