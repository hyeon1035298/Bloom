using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Addons;

namespace Bloom.Ui;

/// A small drawing on a canvas (an option's picture, a switch's thumb, a preview): boxes, dots, lines and words in the
/// text colour at a few strengths, and the accent for the part that matters. `on` = the chosen option or a switch that
/// is on: off, the text colour goes faint and the accent turns to it at half strength. The shapes are AddonShapes, the
/// same an add-on's schema ships (Draw); every one is placed on canvas units around the canvas's middle, so a picture
/// sits centred in its box; words are InkText, centred by their ink.
internal sealed class Pic
{
    private readonly List<AddonShape> shapes = [];
    private readonly double width, height;
    public readonly bool On;

    public Pic(bool on, double width = 72, double height = 44)
    {
        On = on;
        this.width = width;
        this.height = height;
    }

    /// Another shape on top.
    public Pic Put(AddonShape s)
    {
        shapes.Add(s);
        return this;
    }

    /// A rounded box of the text colour at `alpha`.
    public Pic Box(double x, double y, double w, double h, double r, double alpha) => Put(new("box", x, y, w, h) { R = r, Fill = "ink", Alpha = alpha });

    /// A box with only its left corners round (a fill up to a value inside a round box).
    public Pic BoxLeft(double x, double y, double w, double h, double r, bool accent, double alpha) =>
        Put(new("box", x, y, w, h) { R = r, Left = true, Fill = accent ? "accent" : "ink", Alpha = alpha });

    /// A box in the accent.
    public Pic Acc(double x, double y, double w, double h, double r) => Put(new("box", x, y, w, h) { R = r, Fill = "accent" });

    /// A box of a colour of its own (a brand's logo), dimmed when off.
    public Pic Paint(double x, double y, double w, double h, double r, Color c) => Put(new("box", x, y, w, h) { R = r, Fill = Hex(c) });

    /// A box's outline: the text colour at `alpha`, or the accent.
    public Pic Outline(double x, double y, double w, double h, double r, double thickness = 1, double alpha = 0.45, bool accent = false) =>
        Put(new("box", x, y, w, h) { R = r, Stroke = accent ? "accent" : "ink", Width = thickness, Alpha = accent ? 1 : alpha });

    /// A screen, a window, a monitor: a faint fill in a faint outline.
    public Pic Screen(double x, double y, double w, double h, double r) => Box(x, y, w, h, r, 0.05).Outline(x, y, w, h, r);

    /// A dashed outline: a place left empty.
    public Pic Dashed(double x, double y, double w, double h, double r) => Put(new("box", x, y, w, h) { R = r, Stroke = "ink", Dash = true, Alpha = 0.55 });

    public Pic Dot(double cx, double cy, double r, double alpha = 1, bool accent = false, Color? color = null) =>
        Put(new("dot", cx, cy) { R = r, Fill = color is { } c ? Hex(c) : accent ? "accent" : "ink", Alpha = color is not null || accent ? 1 : alpha });

    /// A ring (a clock's face).
    public Pic Ring(double cx, double cy, double r, double alpha = 0.45) => Put(new("dot", cx, cy) { R = r, Stroke = "ink", Alpha = alpha });

    /// Lines along a path (path markup, canvas units).
    public Pic Stroke(string data, double width = 1.3, double alpha = 1, bool accent = false) =>
        Put(new("path", 0, 0) { Data = data, Stroke = accent ? "accent" : "ink", Width = width, Alpha = accent ? 1 : alpha });

    /// A filled shape (path markup, canvas units).
    public Pic Shape(string data, double alpha = 1, bool accent = false) => Put(new("path", 0, 0) { Data = data, Fill = accent ? "accent" : "ink", Alpha = accent ? 1 : alpha });

    /// The pointer's arrow with its tip at (x, y), edged in the window's colour so it reads over anything.
    public Pic Pointer(double x, double y, bool accent = false) =>
        Put(new("path", 0, 0) { Data = $"M {N(x)},{N(y)} v 9 l 2.4,-2.2 1.8,3.6 1.4,-0.7 -1.8,-3.5 h 3.2 z", Fill = accent ? "accent" : "ink", Stroke = "popup", Width = 0.8 });

    /// Words with their ink's middle on (cx, cy) (InkText in a box of their own); `paint` another than the text colour
    /// or the accent (Paint's names: "accentInk", "popup", "btn").
    public Pic Text(string text, double cx, double cy, double size, bool bold = true, double alpha = 1, bool accent = false, string? paint = null, double width = 64) =>
        Put(new("text", cx, cy, width) { Words = new AddonText(text, text), Size = size, Bold = bold, Fill = paint ?? (accent ? "accent" : "ink"), Alpha = accent && paint is null ? 1 : alpha });

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

    public AddonPicture Picture => new(width, height, shapes.ToArray());

    public FrameworkElement Done() => Draw(Picture, On);

    /// A picture in the theme's colours; a shape whose path doesn't parse is left out.
    public static FrameworkElement Draw(AddonPicture picture, bool on)
    {
        var canvas = new Canvas { Width = picture.W, Height = picture.H };
        foreach (var s in picture.Shapes)
        {
            if (Element(s, on) is not { } placed) continue;
            var (e, x, y) = placed;
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }
        return canvas;
    }

    private static (FrameworkElement e, double x, double y)? Element(AddonShape s, bool on)
    {
        Brush? fill = Paint(s.Fill, on), stroke = Paint(s.Stroke, on);
        if (fill is null && stroke is null) fill = Paint("ink", on);
        // off, the accent is the faint text colour at half strength and a colour of the add-on's own a little dimmer
        double dim = on ? 1 : s.Fill == "accent" || s.Stroke == "accent" ? 0.5 : IsHex(s.Fill) || IsHex(s.Stroke) ? 0.55 : 1;
        double opacity = s.Alpha * dim, line = stroke is null ? 0 : s.Width;
        switch (s.Kind)
        {
            case "box" when s.Dash:
                return (new Rectangle { Width = s.W, Height = s.H, RadiusX = s.R, RadiusY = s.R, Fill = s.Fill is null ? null : fill, Stroke = stroke, StrokeThickness = line, StrokeDashArray = new DoubleCollection { 2.5, 2 }, Opacity = opacity }, s.X, s.Y);
            case "box":
                return (new SmoothBorder
                {
                    Width = s.W,
                    Height = s.H,
                    CornerRadius = s.Left ? new CornerRadius(s.R, 0, 0, s.R) : new CornerRadius(s.R),
                    Background = fill,
                    BorderBrush = stroke,
                    BorderThickness = new Thickness(line),
                    Opacity = opacity,
                }, s.X, s.Y);
            case "dot":
                return (new Ellipse { Width = s.R * 2, Height = s.R * 2, Fill = fill, Stroke = stroke, StrokeThickness = line, Opacity = opacity }, s.X - s.R, s.Y - s.R);
            case "path":
                Geometry data;
                try
                {
                    data = Geometry.Parse(s.Data);
                }
                catch (FormatException)
                {
                    return null;
                }
                return (new Path
                {
                    Data = data,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = line,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = opacity,
                }, 0, 0);
            case "text" when s.Words is { } words:
                var t = Theme.Label(words.Shown, s.Size, fill!, s.Bold ? Theme.Semi : Theme.Regular, false, true);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                PictureChoice.CentreInk(t);
                t.Opacity = opacity;
                double h = Math.Ceiling(s.Size * 1.6);
                return (new Border { Width = s.W, Height = h, Child = t }, s.X - s.W / 2, s.Y - h / 2);
            default:
                return null;
        }
    }

    private static Brush? Paint(string? name, bool on) => name switch
    {
        null => null,
        "ink" => on ? Theme.P.Text : Theme.P.TextFaint,
        "accent" => on ? Theme.P.Accent : Theme.P.TextFaint,
        "accentInk" => Theme.P.AccentInk,
        "popup" => Theme.P.Popup,
        "btn" => Theme.P.Btn,
        _ when IsHex(name) => Frozen(new SolidColorBrush(Color.FromRgb(Byte(name, 1), Byte(name, 3), Byte(name, 5)))),
        _ => null,
    };

    private static bool IsHex(string? s) => s is { Length: 7 } && s[0] == '#';

    private static byte Byte(string hex, int at) => byte.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static SolidColorBrush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
}
