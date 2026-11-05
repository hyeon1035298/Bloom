using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using Bloom.Core;

namespace Bloom.Ui;

/// An account's logo (Ai\UsageIcons.Shown) drawn small: a .png or .ico as it is, an .svg from its shapes (paths,
/// circles, rectangles with flat fills: what a logo file holds; gradients, text and transforms are not drawn). 아이콘 색
/// 단색 draws it in the text colour, keeping only its outline, and so does 원래 색 a logo in one grey (black, grey or
/// white, as a company draws its one-colour mark in the text colour of a dark page). Null when the file is gone or
/// unreadable.
internal static class UsageLogo
{
    private static readonly Dictionary<string, (ImageSource? Src, bool Grey)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static FrameworkElement? Of(string? file, double size)
    {
        if (file is null || Read(file) is not ({ } src, bool grey)) return null;
        if (Settings.Current.UsageLogo != "mono" && !grey) return new Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform };
        return new Rectangle { Width = size, Height = size, Fill = Theme.P.Text, OpacityMask = new ImageBrush(src) { Stretch = Stretch.Uniform } };
    }

    private static (ImageSource?, bool) Read(string file)
    {
        if (Cache.TryGetValue(file, out var known)) return known;
        ImageSource? src = null;
        try
        {
            if (File.Exists(file))
                src = file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? Svg(File.ReadAllText(file))
                    : BitmapFrame.Create(new Uri(file), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            src?.Freeze();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or XmlException or FormatException or InvalidOperationException)
        {
            Log.Error("usage logo", e);
            src = null;
        }
        var read = (src, src is not null && OneGrey(src));
        Cache[file] = read;
        return read;
    }

    /// Every shape, or every pixel that is more than half there, of one grey: black, grey or white, one of them.
    internal static bool OneGrey(ImageSource src)
    {
        int low = 255, high = 0;
        bool Grey(byte r, byte g, byte b)
        {
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            low = Math.Min(low, min);
            high = Math.Max(high, max);
            return max - min <= 24; // else a hue
        }
        if (src is DrawingImage { Drawing: DrawingGroup group })
        {
            foreach (var d in group.Children.OfType<GeometryDrawing>())
                if (d.Brush is not SolidColorBrush { Color: var c } || c.A > 0 && !Grey(c.R, c.G, c.B)) return false; // the viewBox's transparent frame is no colour
        }
        else if (src is BitmapSource bitmap)
        {
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
            for (int i = 0; i < px.Length; i += 4)
                if (px[i + 3] >= 128 && !Grey(px[i + 2], px[i + 1], px[i])) return false;
        }
        return low <= high && high - low <= 48; // nothing there is no grey logo; black on a white disc is two
    }

    /// The SVG's flat shapes in its viewBox; null when it has none.
    internal static DrawingImage? Svg(string text)
    {
        var root = XDocument.Parse(text).Root;
        if (root is null) return null;
        double[] box = (Attr(root, "viewBox") ?? $"0 0 {Attr(root, "width") ?? "24"} {Attr(root, "height") ?? "24"}")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => Num(n.Replace("px", "", StringComparison.Ordinal))).ToArray();
        if (box.Length != 4 || box[2] <= 0 || box[3] <= 0) return null;
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(box[0], box[1], box[2], box[3])) };
        // a transparent frame keeps the viewBox's size and place however little the shapes cover
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(box[0], box[1], box[2], box[3]))));
        foreach (var e in root.Descendants())
        {
            if (Inherited(e, "fill") is "none") continue;
            Geometry? g = e.Name.LocalName switch
            {
                "path" when Attr(e, "d") is { } d => Geometry.Parse(d),
                "circle" => new EllipseGeometry(new Point(Num(Attr(e, "cx")), Num(Attr(e, "cy"))), Num(Attr(e, "r")), Num(Attr(e, "r"))),
                "ellipse" => new EllipseGeometry(new Point(Num(Attr(e, "cx")), Num(Attr(e, "cy"))), Num(Attr(e, "rx")), Num(Attr(e, "ry"))),
                "rect" => new RectangleGeometry(new Rect(Num(Attr(e, "x")), Num(Attr(e, "y")), Num(Attr(e, "width")), Num(Attr(e, "height"))), Num(Attr(e, "rx")), Num(Attr(e, "ry") ?? Attr(e, "rx"))),
                _ => null,
            };
            if (g is null) continue;
            if (g is PathGeometry or StreamGeometry && Inherited(e, "fill-rule") == "evenodd") g = PathGeometry.CreateFromGeometry(g) is { } pg ? WithRule(pg) : g;
            group.Children.Add(new GeometryDrawing(Fill(Inherited(e, "fill")), null, g));
        }
        return group.Children.Count > 1 ? new DrawingImage(group) : null;
    }

    private static PathGeometry WithRule(PathGeometry g)
    {
        g.FillRule = FillRule.EvenOdd;
        return g;
    }

    /// An attribute, or the same property in its style="…".
    private static string? Attr(XElement e, string name) =>
        e.Attribute(name)?.Value ?? e.Attribute("style")?.Value.Split(';').Select(p => p.Split(':', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Trim() == name)?[1].Trim();

    /// A presentation attribute as the element gets it: its own, else its nearest group's.
    private static string? Inherited(XElement e, string name)
    {
        for (var x = e; x is not null; x = x.Parent)
            if (Attr(x, name) is { } v) return v;
        return null;
    }

    private static Brush Fill(string? value)
    {
        if (value is null or "currentColor") return Brushes.Black;
        try
        {
            return new BrushConverter().ConvertFromInvariantString(value) as Brush ?? Brushes.Black;
        }
        catch (FormatException)
        {
            return Brushes.Black; // a gradient (url(#…)) or a colour WPF doesn't name
        }
    }

    private static double Num(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}
