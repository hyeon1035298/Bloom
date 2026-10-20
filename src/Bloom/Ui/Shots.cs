using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Native;

namespace Bloom.Ui;

/// Small things every Bloom window shares: the off-screen PNG for QA, the window icon, the
/// title bar in the window's own colour.
internal static class Shots
{
    /// QA: pictures drawn this many times larger than the window's DPI (2 for promo-grade captures).
    public static double Scale = 1;

    /// `el` drawn at the window's DPI (× Scale) into a PNG (no acrylic in such pictures: give a solid background).
    public static void Save(FrameworkElement el, string file)
    {
        el.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(el);
        double sx = dpi.DpiScaleX * Scale, sy = dpi.DpiScaleY * Scale;
        int w = (int)Math.Round(el.ActualWidth * sx), h = (int)Math.Round(el.ActualHeight * sy);
        var rtb = new RenderTargetBitmap(w, h, 96 * sx, 96 * sy, PixelFormats.Pbgra32);
        rtb.Render(el);
        App.DesignCheck.Look(el, rtb, sx, file);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// Several windows in one PNG, each where it is on the screen relative to the others (QA: the launcher with its
    /// attached tools): `parts` are window contents and their window rectangles in physical pixels.
    public static void SaveGroup(System.Collections.Generic.IReadOnlyList<(FrameworkElement el, Win32.RECT r)> parts, string file)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var (_, r) in parts)
        {
            minX = Math.Min(minX, r.Left);
            minY = Math.Min(minY, r.Top);
            maxX = Math.Max(maxX, r.Right);
            maxY = Math.Max(maxY, r.Bottom);
        }
        const int pad = 16;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x84)), null, new Rect(0, 0, maxX - minX + pad * 2, maxY - minY + pad * 2));
            foreach (var (el, r) in parts)
            {
                el.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(el);
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                rtb.Render(el);
                App.DesignCheck.Look(el, rtb, dpi.DpiScaleX, file);
                dc.DrawImage(rtb, new Rect(r.Left - minX + pad, r.Top - minY + pad, w, h));
            }
        }
        var all = new RenderTargetBitmap(maxX - minX + pad * 2, maxY - minY + pad * 2, 96, 96, PixelFormats.Pbgra32);
        all.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(all));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    private static BitmapSource? icon;
    private static string? iconFor;

    /// The Bloom mark as a window icon (drawn once per accent).
    public static BitmapSource AppIcon()
    {
        string accent = Core.Settings.Current.Accent + "|" + Core.Settings.Current.AccentCustom;
        if (icon is not null && iconFor == accent) return icon;
        var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Theme.MarkVisual(64));
        rtb.Freeze();
        iconFor = accent;
        return icon = rtb;
    }

    /// The window's icon is the mark in the current accent, and follows it when the accent changes.
    public static void TrackIcon(Window w)
    {
        w.Icon = AppIcon();
        void Paint() => w.Icon = AppIcon();
        Theme.Changed += Paint;
        w.Closed += (_, _) => Theme.Changed -= Paint;
    }

    /// A normal window's title bar: light or dark, filled with the window's solid colour.
    public static void SolidCaption(Window w)
    {
        IntPtr h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        int dark = Theme.P.Dark ? 1 : 0;
        _ = Win32.DwmSetWindowAttribute(h, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        var c = Theme.P.Solid.Color;
        int caption = c.R | c.G << 8 | c.B << 16;
        _ = Win32.DwmSetWindowAttribute(h, Win32.DWMWA_CAPTION_COLOR, ref caption, 4);
    }
}
