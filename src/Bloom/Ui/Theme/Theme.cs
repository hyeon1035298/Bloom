using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Bloom.Ui;

internal static class Theme
{
    public static readonly Palette P = new();
    /// Windows' own UI fonts: hinted, so with TextFormattingMode.Display (every window) small text stays sharp on 100% screens.
    public static readonly FontFamily Sans = new("Segoe UI, Segoe UI Emoji, Segoe UI Symbol, " + Cjk);
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, " + Cjk);

    /// Hangul, kana and Han characters: the interface language's own font first (a Japanese or Chinese character
    /// drawn from a Korean font takes the Korean shape).
    private static string Cjk => Core.Lang.Code switch
    {
        "ja" => "Yu Gothic UI, Meiryo UI, Malgun Gothic",
        "zh" => "Microsoft YaHei UI, Malgun Gothic",
        _ => "Malgun Gothic",
    };
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static event Action? Changed;

    /// 설정 > 일반 > 크기 (Ctrl+= / Ctrl+- in the launcher): the launcher, its tools, the chat and settings windows draw
    /// their content through this one transform, and their window sizes follow it.
    public static readonly ScaleTransform Zoom = new(1, 1);
    public static readonly double[] Scales = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5 };
    public static double Scale => Zoom.ScaleX;

    /// The scale changed: (old, new).
    public static event Action<double, double>? ScaleChanged;

    /// At start: the scale the settings keep.
    public static void InitScale() => Zoom.ScaleX = Zoom.ScaleY = Math.Clamp(Core.Settings.Current.Scale, Scales[0], Scales[^1]);

    public static void SetScale(double s)
    {
        s = Math.Clamp(Math.Round(s, 2), Scales[0], Scales[^1]);
        double old = Scale;
        Core.Settings.Current.Scale = s;
        Core.Settings.Save();
        if (Math.Abs(old - s) < 0.001) return;
        Zoom.ScaleX = Zoom.ScaleY = s;
        ScaleChanged?.Invoke(old, s);
    }

    /// One step up or down the scales (Ctrl+= / Ctrl+-).
    public static void StepScale(int d)
    {
        int i = Array.FindIndex(Scales, x => x >= Scale - 0.001);
        SetScale(Scales[Math.Clamp((i < 0 ? 2 : i) + d, 0, Scales.Length - 1)]);
    }

    /// A window's content at Bloom's scale: its size (and least size) grow with it, now and on every change.
    public static void Scaled(Window w, FrameworkElement content)
    {
        content.LayoutTransform = Zoom;
        double s = Scale;
        if (Math.Abs(s - 1) > 0.001) Resize(w, 1, s);
        Action<double, double> on = (o, n) => Resize(w, o, n);
        ScaleChanged += on;
        w.Closed += (_, _) => ScaleChanged -= on;
    }

    private static void Resize(Window w, double from, double to)
    {
        double k = to / from;
        if (w.MinWidth > 0) w.MinWidth *= k;
        if (w.MinHeight > 0) w.MinHeight *= k;
        if (!double.IsNaN(w.Width)) w.Width *= k;
        if (!double.IsNaN(w.Height)) w.Height *= k;
    }

    /// Transparency effects on → DWM acrylic under our tint; off → a solid background.
    public static bool Acrylic { get; private set; } = true;

    public static FontWeight Regular => FontWeights.Normal;
    public static FontWeight Medium => FontWeights.Medium;
    public static FontWeight Semi => FontWeights.SemiBold;

    public static bool SystemDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool SystemTransparency()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    public static void Apply(string setting)
    {
        bool dark = setting switch { "dark" => true, "light" => false, _ => SystemDark() };
        Acrylic = Core.Settings.Current.Transparency != "off" && SystemTransparency() && Environment.OSVersion.Version.Build >= 22621;
        P.Apply(dark, Accents.Current, Core.Settings.Current.GlassTransparency);
        // a ResourceDictionary freezes what it holds, so the scroll thumb gets a fresh copy each time
        if (Application.Current is { } app) app.Resources["AL.Thumb"] = P.Thumb.CloneCurrentValue();
        Changed?.Invoke();
    }

    // ── text helpers ────────────────────────────────────────────────────────────────────────────
    public static TextBlock T(string text, double size, Brush color, FontWeight? weight = null, bool ellipsis = true)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Sans,
            FontSize = size,
            FontWeight = weight ?? Regular,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = ellipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        return t;
    }

    public static TextBlock Glyph(char g, double size, Brush color) => new()
    {
        Text = g.ToString(),
        FontFamily = IconFont,
        FontSize = size,
        Foreground = color,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    // ── the mark: a line and a caret ("한 줄에서 시작") ─────────────────────────────────────────
    /// The four-point sparkle used for AI (vector, so it stays crisp at any size).
    public static Geometry SparkleGeometry()
    {
        var g = Geometry.Parse("M 12,1.5 C 12.9,7.4 16.6,11.1 22.5,12 C 16.6,12.9 12.9,16.6 12,22.5 C 11.1,16.6 7.4,12.9 1.5,12 C 7.4,11.1 11.1,7.4 12,1.5 Z");
        g.Freeze();
        return g;
    }

    public static FrameworkElement Sparkle(double size, Brush fill) => new System.Windows.Shapes.Path
    {
        Data = SparkleGeometry(),
        Fill = fill,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// Bloom's mark at `size` px — "한 줄이 피어난다": the old mark's one white line opened into five
    /// petals (round heads, pointed bases; capsules read as an asterisk), the rose caret become their
    /// centre, on a dark rose-tinted tile. tools\make_icon.ps1 draws the same for Assets\icon.ico.
    public static DrawingVisual MarkVisual(double size, bool tile = true)
    {
        var v = new DrawingVisual();
        using var dc = v.RenderOpen();
        double s = size;
        // the settings' accent (the dark theme's tone, as the tile is dark): Rose is the mark as drawn; another colour
        // turns the tile and the petals to its hue (gray drops it, like the neutrals of the UI) and the centre to the accent
        var a = Accents.Current is { Key: not "rose" } x ? x : null;
        Color Tint(Color c)
        {
            if (a is null) return c;
            var (_, sat, l) = Hsl.From(c);
            return Hsl.To(c.A, a.Hue, a.Gray ? 0 : sat, l);
        }
        if (tile)
        {
            var bg = new LinearGradientBrush(Tint(Color.FromRgb(0x2E, 0x25, 0x29)), Tint(Color.FromRgb(0x12, 0x0E, 0x10)), 90);
            dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), s * 0.23, s * 0.23);
        }
        var white = new SolidColorBrush(Tint(Color.FromRgb(0xF1, 0xEC, 0xEE)));
        var rose = new SolidColorBrush(a is null ? Color.FromRgb(0xFF, 0x7A, 0x9E) : Accents.Swatch(a, true));
        double cx = s / 2, cy = s * 0.515; // the flower's box (one petal up) centred on the tile
        double hw = Math.Max(1.1, s * 0.085), len = s * 0.28, gap = s * 0.06;
        double yb = cy - gap, yt = cy - gap - len;
        var petal = new StreamGeometry();
        using (var g = petal.Open())
        {
            g.BeginFigure(new Point(cx, yb), true, true);
            g.BezierTo(new Point(cx - hw * 0.3, yb - len * 0.3), new Point(cx - hw, yt + hw * 1.6), new Point(cx - hw, yt + hw), true, true);
            g.ArcTo(new Point(cx + hw, yt + hw), new Size(hw, hw), 0, false, SweepDirection.Clockwise, true, true);
            g.BezierTo(new Point(cx + hw, yt + hw * 1.6), new Point(cx + hw * 0.3, yb - len * 0.3), new Point(cx, yb), true, true);
        }
        petal.Freeze();
        for (int i = 0; i < 5; i++)
        {
            dc.PushTransform(new RotateTransform(i * 72, cx, cy));
            dc.DrawGeometry(white, null, petal);
            dc.Pop();
        }
        double r = Math.Max(1.5, s * 0.062);
        dc.DrawEllipse(rose, null, new Point(cx, cy), r, r);
        return v;
    }
}
