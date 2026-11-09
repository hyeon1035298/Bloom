using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// A small drawing on a canvas (an option's picture, a switch's thumb): boxes, dots, lines and words in the text colour
/// at a few strengths, and the accent for the part that matters. `on` = the chosen option or a switch that is on: off,
/// the text colour goes faint and the accent turns to it at half strength. Every shape is placed on whole canvas units
/// around the canvas's middle, so a picture sits centred in its box; words are InkText, centred by their ink.
internal sealed class Pic
{
    public readonly Canvas Canvas;
    public readonly bool On;

    public Pic(bool on, double width = 72, double height = 44)
    {
        On = on;
        Canvas = new Canvas { Width = width, Height = height };
    }

    private Brush Ink => On ? Theme.P.Text : Theme.P.TextFaint;
    private Brush Accent => On ? Theme.P.Accent : Theme.P.TextFaint;
    private double AccentAlpha => On ? 1 : 0.5;

    private Pic Put(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Canvas.Children.Add(e);
        return this;
    }

    /// A rounded box of the text colour at `alpha`.
    public Pic Box(double x, double y, double w, double h, double r, double alpha) =>
        Put(new SmoothBorder { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = Ink, Opacity = alpha }, x, y);

    /// A box with only its left corners round (a fill up to a value inside a round box).
    public Pic BoxLeft(double x, double y, double w, double h, double r, bool accent, double alpha) =>
        Put(new SmoothBorder { Width = w, Height = h, CornerRadius = new CornerRadius(r, 0, 0, r), Background = accent ? Accent : Ink, Opacity = accent ? AccentAlpha * alpha : alpha }, x, y);

    /// A box in the accent.
    public Pic Acc(double x, double y, double w, double h, double r) =>
        Put(new SmoothBorder { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = Accent, Opacity = AccentAlpha }, x, y);

    /// A box of a colour of its own (a brand's logo), dimmed when off.
    public Pic Paint(double x, double y, double w, double h, double r, Color c) =>
        Put(new SmoothBorder { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = new SolidColorBrush(c), Opacity = On ? 1 : 0.55 }, x, y);

    /// A box's outline: the text colour at `alpha`, or the accent.
    public Pic Outline(double x, double y, double w, double h, double r, double thickness = 1, double alpha = 0.45, bool accent = false) =>
        Put(new SmoothBorder
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(r),
            BorderBrush = accent ? Accent : Ink,
            BorderThickness = new Thickness(thickness),
            Opacity = accent ? AccentAlpha : alpha,
        }, x, y);

    /// A screen, a window, a monitor: a faint fill in a faint outline.
    public Pic Screen(double x, double y, double w, double h, double r) => Box(x, y, w, h, r, 0.05).Outline(x, y, w, h, r);

    /// A dashed outline: a place left empty.
    public Pic Dashed(double x, double y, double w, double h, double r) =>
        Put(new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Stroke = Ink, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2.5, 2 }, Opacity = 0.55 }, x, y);

    public Pic Dot(double cx, double cy, double r, double alpha = 1, bool accent = false, Color? color = null) =>
        Put(new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = color is { } c ? new SolidColorBrush(c) : accent ? Accent : Ink,
            Opacity = color is not null ? On ? 1 : 0.55 : accent ? AccentAlpha : alpha,
        }, cx - r, cy - r);

    /// A ring (a clock's face).
    public Pic Ring(double cx, double cy, double r, double alpha = 0.45) =>
        Put(new Ellipse { Width = r * 2, Height = r * 2, Stroke = Ink, StrokeThickness = 1, Opacity = alpha }, cx - r, cy - r);

    /// Lines along a path (path markup, canvas units).
    public Pic Stroke(string data, double width = 1.3, double alpha = 1, bool accent = false) =>
        Put(new Path
        {
            Data = Geometry.Parse(data),
            Stroke = accent ? Accent : Ink,
            StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = accent ? AccentAlpha : alpha,
        }, 0, 0);

    /// A filled shape (path markup, canvas units).
    public Pic Shape(string data, double alpha = 1, bool accent = false) =>
        Put(new Path { Data = Geometry.Parse(data), Fill = accent ? Accent : Ink, Opacity = accent ? AccentAlpha : alpha }, 0, 0);

    /// The pointer's arrow with its tip at (x, y), edged in the window's colour so it reads over anything.
    public Pic Pointer(double x, double y, bool accent = false) =>
        Put(new Path
        {
            Data = Geometry.Parse($"M {N(x)},{N(y)} v 9 l 2.4,-2.2 1.8,3.6 1.4,-0.7 -1.8,-3.5 h 3.2 z"),
            Fill = accent ? Accent : Ink,
            Stroke = Theme.P.Popup,
            StrokeThickness = 0.8,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = accent ? AccentAlpha : 1,
        }, 0, 0);

    /// Words with their ink's middle on (cx, cy) (InkText in a box of their own).
    public Pic Text(string text, double cx, double cy, double size, bool bold = true, double alpha = 1, bool accent = false, Brush? brush = null, double width = 64)
    {
        var t = Theme.Label(text, size, brush ?? (accent ? Accent : Ink), bold ? Theme.Semi : Theme.Regular, false, true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        PictureChoice.CentreInk(t);
        t.Opacity = accent ? AccentAlpha : alpha;
        double h = Math.Ceiling(size * 1.6);
        return Put(new Border { Width = width, Height = h, Child = t }, cx - width / 2, cy - h / 2);
    }

    /// Five petals round a point: Bloom's flower at the size of an app icon.
    public Pic Flower(double cx, double cy, double r, bool accent = true)
    {
        for (int i = 0; i < 5; i++)
        {
            double a = (-90 + i * 72) * Math.PI / 180;
            Dot(cx + Math.Cos(a) * r * 1.55, cy + Math.Sin(a) * r * 1.55, r, 1, accent);
        }
        return this;
    }

    /// The Windows logo: four squares round a point.
    public Pic Windows(double cx, double cy, double s, double gap)
    {
        foreach (var (dx, dy) in new[] { (-1, -1), (0, -1), (-1, 0), (0, 0) })
            Acc(cx + dx * s + (dx < 0 ? -gap / 2 : gap / 2), cy + dy * s + (dy < 0 ? -gap / 2 : gap / 2), s, s, s * 0.17);
        return this;
    }

    private static string N(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public FrameworkElement Done() => Canvas;
}

/// The pictures of the add-on pages (설정 > 애드온): an option's (PictureChoice), a switch's thumb on its row's left, and
/// what the choice they stand for looks like on screen; null where an option has none.
internal static class AddonPictures
{
    private static readonly Color Claude = Color.FromRgb(0xD9, 0x77, 0x57), Gpt = Color.FromRgb(0x10, 0xA3, 0x7F);

    /// An option's picture, by the add-on, the setting's key and the option's value (a pseudo key for a page's own
    /// choices: "size", "usage.tag", "usage.logo", "clean.strength", "awake.duration").
    public static Func<bool, FrameworkElement>? Option(string addon, string key, string value) => (addon, key, value) switch
    {
        // ── 작업표시줄 ──
        ("pill-taskbar", "size", "s") => on => Sized(on, 18, 32, 36, 8, 4, 34, 7),
        ("pill-taskbar", "size", "m") => on => Sized(on, 13, 29, 46, 11, 6, 31.5, 9),
        ("pill-taskbar", "size", "l") => on => Sized(on, 7, 25, 58, 15, 8, 28.5, 12),
        ("pill-taskbar", "size", "custom") => on => Sliders(new Pic(on)).Done(),
        ("pill-taskbar", "pill.style", _) => on => Glass(on, value),
        ("pill-taskbar", "start.mode", "windows") => on => Apps(new Pic(on).Box(6, 14, 60, 16, 8, 0.18).Windows(16, 22, 3.6, 0.8), 26).Done(),
        ("pill-taskbar", "start.mode", "hidden") => on => Apps(new Pic(on).Box(12, 14, 48, 16, 8, 0.18), 17.5, 4).Done(),
        ("pill-taskbar", "start.mode", "bloom") => on => Apps(new Pic(on).Box(6, 14, 60, 16, 8, 0.18).Flower(16, 22, 1.8), 26).Done(),
        ("pill-taskbar", "pill.edge", "windows") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Windows(36, 18, 5.5, 1).Box(20, 33, 32, 7, 3.5, 0.18).Done(),
        ("pill-taskbar", "pill.edge", "top") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Acc(20, 4, 32, 7, 3.5).Done(),
        ("pill-taskbar", "pill.edge", "bottom") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Acc(20, 33, 32, 7, 3.5).Done(),
        ("pill-taskbar", "pill.align", "left") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Acc(5, 33, 30, 7, 3.5).Done(),
        ("pill-taskbar", "pill.align", "center") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Acc(21, 33, 30, 7, 3.5).Done(),
        ("pill-taskbar", "pill.align", "right") => on => new Pic(on).Screen(1, 1, 70, 42, 4).Acc(37, 33, 30, 7, 3.5).Done(),
        ("pill-taskbar", "tray.clock", "timeDate") => on => new Pic(on).Text("21:41", 36, 17, 11).Text("2026-11-09", 36, 29, 8, false, 0.75).Done(),
        ("pill-taskbar", "tray.clock", "time") => on => new Pic(on).Text("21:41", 36, 22, 12, accent: true).Done(),
        ("pill-taskbar", "tray.clock", "hidden") => on => new Pic(on).Dashed(20, 13, 32, 18, 4).Done(),
        ("pill-taskbar", "usage.tag", "none") => on => Bars(new Pic(on).Dot(16, 22, 5, color: Claude), 26, 36).Done(),
        ("pill-taskbar", "usage.tag", "name") => on => Bars(new Pic(on).Dot(7, 22, 4.5, color: Claude).Text("Claude", 29, 22, 8.5, width: 34), 48, 20).Done(),
        ("pill-taskbar", "usage.tag", "plan") => on => Bars(new Pic(on).Dot(9, 22, 4.5, color: Claude).Text("Max", 26, 22, 8.5, width: 22), 41, 24).Done(),
        ("pill-taskbar", "usage.logo", "brand") => on => new Pic(on).Dot(26, 22, 8, color: Claude).Dot(46, 22, 8, color: Gpt).Done(),
        ("pill-taskbar", "usage.logo", "mono") => on => new Pic(on).Dot(26, 22, 8, 0.45).Dot(46, 22, 8, 0.45).Done(),
        ("pill-taskbar", "island.reveal", "hover") => on => Island(new Pic(on)).Pointer(47, 30, true).Done(),
        ("pill-taskbar", "island.reveal", "click") => on => Island(new Pic(on)).Pointer(37, 33, true).Stroke("M 33,31 L 31,29 M 37,29.5 V 27 M 41,31 L 43,29", 1.2, 1, true).Done(),

        // ── 모니터별 Alt+Tab ──
        ("alt-tab-per-monitor", "switcher.style", "windows") => on => new Pic(on).Box(3, 7, 66, 30, 6, 0.18).Outline(6.5, 11.5, 19, 21, 3, 1.5, accent: true)
            .Screen(9, 14, 14, 11, 1.5).Screen(29, 14, 14, 11, 1.5).Screen(49, 14, 14, 11, 1.5)
            .Box(9, 27.5, 10, 2, 1, 0.45).Box(29, 27.5, 10, 2, 1, 0.45).Box(49, 27.5, 10, 2, 1, 0.45).Done(),
        ("alt-tab-per-monitor", "switcher.style", "icons") => on => new Pic(on).Box(3, 11, 66, 22, 6, 0.18).Outline(7.5, 14.5, 15, 15, 3.5, 1.5, accent: true)
            .Acc(10, 17, 10, 10, 2.5).Box(24, 17, 10, 10, 2.5, 0.45).Box(38, 17, 10, 10, 2.5, 0.45).Box(52, 17, 10, 10, 2.5, 0.45).Done(),
        ("alt-tab-per-monitor", "switcher.iconSize", _) => on => IconRow(on, value switch { "small" => 6, "medium" => 9, "large" => 12, _ => 15 }),
        ("alt-tab-per-monitor", "switcher.titles", _) => on => Titles(on, value),
        ("alt-tab-per-monitor", "monitor.altTabLocation" or "monitor.winAltTabLocation", "cursor") => on => Monitors(new Pic(on)).Acc(47, 16, 18, 7, 2).Pointer(60, 22).Done(),
        ("alt-tab-per-monitor", "monitor.altTabLocation" or "monitor.winAltTabLocation", "primary") => on => Monitors(new Pic(on)).Acc(9, 14, 22, 7, 2).Pointer(60, 22).Done(),
        ("alt-tab-per-monitor", "monitor.altTabWindows" or "monitor.winAltTabWindows", _) => on => Listed(on, value),

        // ── 메모리 정리 ──
        ("memory-clean", "widget.icon", _) => on => Ram(new Pic(on), value).Done(),
        ("memory-clean", "widget.show", "0") => on => new Pic(on).Text("62%", 36, 18, 13, accent: true).Text(Tr("메모리", "Memory"), 36, 31, 7.5, false, 0.75).Done(),
        ("memory-clean", "widget.show", "1") => on => new Pic(on).Text("12%", 36, 18, 13, accent: true).Text("CPU", 36, 31, 7.5, false, 0.75).Done(),
        ("memory-clean", "widget.show", "2") => on => new Pic(on).Text("62%", 21, 18, 11.5, accent: true, width: 30).Text(Tr("메모리", "Memory"), 21, 31, 7, false, 0.75, width: 30)
            .Text("12%", 51, 18, 11.5, accent: true, width: 30).Text("CPU", 51, 31, 7, false, 0.75, width: 30).Done(),
        ("memory-clean", "clean.strength", "light") => on => Freed(on, 40, 8, Tr("조금 비움", "A little")),
        ("memory-clean", "clean.strength", "normal") => on => Freed(on, 28, 20, Tr("알맞게 비움", "Some")),
        ("memory-clean", "clean.strength", "strong") => on => Freed(on, 14, 34, Tr("많이 비움", "A lot")),
        ("memory-clean", "clean.strength", "custom") => on => Sliders(new Pic(on)).Done(),

        // ── 모니터 밝기 조정 ──
        ("brightness", "taskbar.number", "0") => on => new Pic(on).Screen(3, 4, 28, 19, 3).Screen(41, 4, 28, 19, 3).Text("70", 17, 13.5, 9, accent: true, width: 24).Text("40", 55, 13.5, 9, alpha: 0.55, width: 24)
            .Outline(1.5, 2.5, 31, 22, 4, 1.5, accent: true).Text("70", 36, 36, 10, accent: true).Done(),
        ("brightness", "taskbar.number", "1") => on => new Pic(on).Screen(3, 4, 28, 19, 3).Screen(41, 4, 28, 19, 3).Text("70", 17, 13.5, 9, accent: true, width: 24).Text("40", 55, 13.5, 9, accent: true, width: 24)
            .Outline(1.5, 2.5, 31, 22, 4, 1.5, accent: true).Outline(39.5, 2.5, 31, 22, 4, 1.5, accent: true).Text("55", 36, 36, 10, accent: true).Done(),

        // ── 커피 ──
        ("keep-awake", "awake.display", "display") => on => Pc(new Pic(on).Acc(6, 6, 38, 25, 3).Outline(6, 6, 38, 25, 3)).Done(),
        ("keep-awake", "awake.display", "system") => on => Pc(new Pic(on).Box(6, 6, 38, 25, 3, 0.55).Text("z z", 25, 18.5, 9, brush: Theme.P.Btn, width: 30)).Done(),
        _ => null,
    };

    /// A switch's picture on its row's left, by the add-on and the switch's key (null: none).
    public static Func<bool, FrameworkElement>? Thumb(string addon, string key) => (addon, key) switch
    {
        ("pill-taskbar", "media.enabled") => on => Media(new Pic(on, 60, 36), 4, 10).Done(),
        ("pill-taskbar", "usage.enabled") => on => new Pic(on, 60, 36).Dot(12, 18, 5, color: Claude).Box(21, 12.5, 32, 3.5, 1.75, 0.18).Acc(21, 12.5, 20, 3.5, 1.75)
            .Box(21, 20, 32, 3.5, 1.75, 0.18).Acc(21, 20, 10, 3.5, 1.75).Done(),
        ("pill-taskbar", "island.enabled") => on => new Pic(on, 60, 36).Screen(1, 1, 58, 34, 4).Box(20, 28.5, 20, 4.5, 2.25, 1).Done(),
        ("pill-taskbar", "folders.enabled") => on => new Pic(on, 60, 36).Box(4, 11, 52, 14, 7, 0.18).Outline(10, 13, 10, 10, 2.5, 1.2, accent: true)
            .Acc(11.8, 14.8, 2.8, 2.8, 0.6).Acc(15.4, 14.8, 2.8, 2.8, 0.6).Acc(11.8, 18.4, 2.8, 2.8, 0.6).Acc(15.4, 18.4, 2.8, 2.8, 0.6)
            .Box(25, 14.5, 7, 7, 1.5, 0.45).Box(35, 14.5, 7, 7, 1.5, 0.45).Box(45, 14.5, 7, 7, 1.5, 0.45).Done(),
        ("memory-clean", "auto.percentOn") => on => new Pic(on, 60, 36).Box(6, 13, 48, 10, 3, 0.18).Box(6, 13, 43, 10, 3, 0.45).Stroke("M 49.2,9 V 27", 1.6, 1, true).Done(),
        ("memory-clean", "auto.intervalOn") => on => new Pic(on, 60, 36).Ring(20, 18, 11).Stroke("M 20,11 V 18 L 25,21").Stroke("M 36,18 H 50 M 46,14 L 50,18 46,22").Done(),
        ("brightness", "taskbar.link") => on => new Pic(on, 60, 36).Screen(3, 8, 21, 15, 2.5).Screen(36, 8, 21, 15, 2.5).Stroke("M 25.5,15.5 H 34.5", 1.3, 1, true)
            .Dot(25.5, 15.5, 1.4, accent: true).Dot(34.5, 15.5, 1.4, accent: true).Box(10, 25, 7, 1.6, 0.8, 0.18).Box(43, 25, 7, 1.6, 0.8, 0.18).Done(),
        _ => null,
    };

    /// A switch's thumb: the picture in a faint tile, 80 × 50.
    public static FrameworkElement Tile(Func<bool, FrameworkElement> draw, bool on) => new SmoothBorder
    {
        Width = 80,
        Height = 50,
        CornerRadius = new CornerRadius(8),
        Background = Theme.P.Card,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Viewbox { Width = 74, Height = 44, Stretch = Stretch.Uniform, Child = draw(on) },
    };

    // ── shared pieces ───────────────────────────────────────────────────────────────────────────
    /// The taskbar at a size: the pill and four icons `step` apart, centred on the screen.
    private static FrameworkElement Sized(bool on, double x, double y, double w, double h, double icon, double iy, double step)
    {
        var p = new Pic(on).Screen(1, 1, 70, 42, 4).Box(x, y, w, h, h / 2, 0.18);
        double first = 36 - (3 * step + icon) / 2;
        for (int i = 0; i < 4; i++) p.Acc(first + i * step, iy, icon, icon, icon / 4);
        return p.Done();
    }

    private static FrameworkElement Glass(bool on, string style)
    {
        var p = new Pic(on).Dot(22, 15, 12, 0.18).Box(42, 5, 22, 18, 4, 0.18);
        p.Canvas.Children.Add(Placed(new SmoothBorder
        {
            Width = 60,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = Theme.P.Popup,
            Opacity = 0.85,
            BorderBrush = style == "bloom" ? on ? Theme.P.Text : Theme.P.TextFaint : null,
            BorderThickness = new Thickness(style == "bloom" ? 0.6 : 0),
        }, 6, 19));
        if (style == "glass") p.Stroke("M 15,19.8 H 57", 1.4).Stroke("M 7.2,24 Q 6.8,27 8.6,30.5", 1, 0.5);
        for (int i = 0; i < 4; i++) p.Acc(18 + i * 10, 24, 6, 6, 1.5);
        return p.Done();
    }

    private static FrameworkElement Placed(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    /// App icons along a pill from `x`: three 11 apart, or four 10 apart.
    private static Pic Apps(Pic p, double x, int n = 3)
    {
        for (int i = 0; i < n; i++) p.Box(x + i * (n == 4 ? 10 : 11), 18.5, 7, 7, 1.5, 0.45);
        return p;
    }

    /// Three sliders: a value set by hand (직접).
    private static Pic Sliders(Pic p) => p.Stroke("M 14,13 H 58 M 14,22 H 58 M 14,31 H 58", 1.3, 0.6).Dot(26, 13, 3.2, accent: true).Dot(46, 22, 3.2, accent: true).Dot(33, 31, 3.2, accent: true);

    /// A usage logo's two bars from `x`, `w` long.
    private static Pic Bars(Pic p, double x, double w) =>
        p.Box(x, 17, w, 3.5, 1.75, 0.18).Acc(x, 17, Math.Round(w * 0.6), 3.5, 1.75).Box(x, 24, w, 3.5, 1.75, 0.18).Acc(x, 24, Math.Round(w * 0.3), 3.5, 1.75);

    /// The screen with the island at its bottom edge and where the taskbar comes out.
    private static Pic Island(Pic p) => p.Screen(1, 1, 70, 42, 4).Dashed(18, 27, 36, 7, 3.5).Box(28, 38, 16, 3.5, 1.75, 1);

    /// The now-playing chip: album art, title, play.
    private static Pic Media(Pic p, double x, double y) => p.Box(x, y, 52, 16, 5, 0.18).Acc(x + 3, y + 2.5, 11, 11, 2)
        .Box(x + 17, y + 4, 18, 2.6, 1.3, 0.45).Box(x + 17, y + 9.2, 12, 2.2, 1.1, 0.18).Shape($"M {x + 41},{y + 4.2} l 6,3.8 -6,3.8 z");

    /// Three app icons of `s` on the switcher's panel, the first chosen.
    private static FrameworkElement IconRow(bool on, double s)
    {
        var p = new Pic(on).Box(6, 10, 60, 24, 5, 0.18);
        double step = s + 3, y = 22 - s / 2;
        for (int i = -1; i <= 1; i++) p.Acc(36 + i * step - s / 2, y, s, s, s * 0.23);
        return p.Done();
    }

    private static FrameworkElement Titles(bool on, string value)
    {
        bool none = value == "none";
        double y = none ? 17 : 13;
        var p = new Pic(on).Box(4, 9, 64, 26, 5, 0.18).Acc(10, y, 10, 10, 2.5).Box(24, y, 10, 10, 2.5, 0.45).Box(38, y, 10, 10, 2.5, 0.45).Box(52, y, 10, 10, 2.5, 0.45);
        if (value == "selected") p.Box(7, 27, 16, 2.4, 1.2, 1);
        if (value == "all")
            for (int i = 0; i < 4; i++) p.Box(9 + i * 14, 27, 12, 2.4, 1.2, 1);
        return p.Done();
    }

    /// Two monitors: 1, the main one, and 2.
    private static Pic Monitors(Pic p) => p.Screen(2, 5, 36, 25, 3).Box(16, 31, 8, 2, 1, 0.18).Text("1", 7, 10, 6.5, false, 0.6, width: 8)
        .Screen(42, 9, 28, 20, 3).Box(52, 30, 8, 2, 1, 0.18).Text("2", 47, 14, 6.5, false, 0.6, width: 8);

    /// The windows Alt+Tab lists (accent) on the two monitors, the pointer on 2.
    private static FrameworkElement Listed(bool on, string value)
    {
        var p = new Pic(on).Screen(2, 5, 36, 25, 3).Screen(42, 9, 28, 20, 3);
        bool main = value is "allMonitors" or "cursorMonitorExceptPrimary", second = value != "cursorMonitorExceptPrimary";
        if (main) p.Acc(6, 9, 13, 9, 1.5).Acc(21, 16, 13, 10, 1.5);
        else p.Box(6, 9, 13, 9, 1.5, 0.45).Box(21, 16, 13, 10, 1.5, 0.45);
        if (second) p.Acc(47, 13, 14, 10, 1.5);
        else p.Box(47, 13, 14, 10, 1.5, 0.45);
        if (value == "cursorMonitorExceptPrimary") p.Text(Tr("주 모니터", "Main"), 20, 38, 6.5, false, 0.75, width: 36);
        else p.Pointer(60, 20);
        return p.Done();
    }

    /// The memory widget's RAM module (Bloom's design system: 22.7 × 13.3 body, six feet), at twice its taskbar size.
    private static Pic Ram(Pic p, string style)
    {
        double x = 13.3, y = 5.9, w = 45.4, h = 26.6, r = 7.2;
        if (style == "2") p.Box(x, y, w, h, r, 1);
        else
        {
            if (style == "1") p.BoxLeft(x, y, Math.Round(w * 0.62, 1), h, r, true, 0.32);
            p.Outline(x, y, w, h, r, 1.4, 1);
        }
        foreach (double fx in new[] { 20.3, 25.5, 30.7, 37.9, 43.1, 48.3 }) p.Box(fx, 32.5, 3.4, 4.8, 0.8, 1);
        return p.Text("62", 36, y + h / 2, 18, brush: style == "2" ? Theme.P.Btn : null);
    }

    /// Memory before a clean: what stays (`kept` long) and what is freed (accent, `freed` long).
    private static FrameworkElement Freed(bool on, double kept, double freed, string words) =>
        new Pic(on).Box(6, 14, 60, 12, 3, 0.18).BoxLeft(6, 14, kept, 12, 3, false, 0.45).Acc(6 + kept, 14, freed, 12, 0).Text(words, 36, 36, 7, false, 0.75).Done();

    /// A monitor (its screen drawn by the caller) and a PC tower with its power light.
    private static Pic Pc(Pic p) => p.Box(21, 33, 8, 2.5, 1, 0.18).Screen(50, 6, 16, 31, 3).Dot(58, 12, 2, accent: true);
}
