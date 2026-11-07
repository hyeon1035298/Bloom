using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace Bloom.Ui;

/// The taskbar folders' icon set (아이콘 바꾸기 › 아이콘), from Assets/folder-icons.json (tools/folder-icons.py writes it):
/// one glyph on a squircle tile with a diagonal gradient, in two looks — silver with a near-black glyph, for a dark
/// taskbar, and graphite with a white one, for a light taskbar. A glyph is layers of one tone each (1 the ink, 2–4
/// lighter), filled or stroked with round ends, less what their cuts cover. 16 and 24 px have drawings of their own.
internal static class FolderIconSet
{
    private sealed record Look(Color Top, Color Bottom, Brush[] Tones);

    private sealed record Spec(double Curve, Dictionary<int, double> Insets, Look Silver, Look Graphite, List<(string Name, Dictionary<int, JsonObject> Sizes)> Icons);

    private static readonly Lazy<Spec> Data = new(Load);
    private static readonly Dictionary<(string, bool, int), Drawing> Drawn = new();

    public static IReadOnlyList<string> Names => Data.Value.Icons.Select(i => i.Name).ToList();

    /// The drawing (on a 256 canvas) of `name` for `px` pixels: the 16 or 24 one at those sizes, else the full one.
    public static Drawing Draw(string name, bool silver, int px)
    {
        var spec = Data.Value;
        var sizes = spec.Icons.First(i => i.Name == name).Sizes;
        int size = sizes.ContainsKey(px) ? px : 256;
        if (Drawn.TryGetValue((name, silver, size), out var known)) return known;
        var look = silver ? spec.Silver : spec.Graphite;
        double inset = spec.Insets[size];
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new LinearGradientBrush(look.Top, look.Bottom, new Point(inset, inset), new Point(256 - inset, 256 - inset)) { MappingMode = BrushMappingMode.Absolute },
            null, Tile(256 / 2.0 - inset, spec.Curve)));
        var v = sizes[size];
        var glyph = new DrawingGroup();
        if (v["t"] is JsonArray t)
        {
            double k = (double)t[0]!, cx = (double)t[1]!, cy = (double)t[2]!;
            glyph.Transform = new MatrixTransform(k, 0, 0, k, cx - 128 * k, cy - 128 * k);
        }
        foreach (var layer in v["layers"]!.AsArray().Cast<JsonObject>())
        {
            var shape = Shape(layer);
            if (layer["cut"] is JsonArray cuts)
                foreach (var cut in cuts.Cast<JsonObject>()) shape = Geometry.Combine(shape, Shape(cut), GeometryCombineMode.Exclude, null);
            glyph.Children.Add(new GeometryDrawing(look.Tones[(int)layer["tone"]! - 1], null, shape));
        }
        group.Children.Add(glyph);
        group.Freeze();
        return Drawn[(name, silver, size)] = group;
    }

    /// The icon to show in Bloom: the full drawing (the small ones are for the taskbar's own sizes).
    public static ImageSource Image(string name, bool silver)
    {
        var image = new DrawingImage(Draw(name, silver, 256));
        image.Freeze();
        return image;
    }

    /// A layer or a cut: its path filled, or stroked with round ends and joins.
    private static Geometry Shape(JsonObject part)
    {
        if (part["fill"] is { } fill) return Geometry.Parse((part["rule"]?.GetValue<string>() == "evenodd" ? "F0 " : "F1 ") + fill.GetValue<string>());
        var pen = new Pen(Brushes.Black, (double)part["w"]!) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        return Geometry.Parse("F1 " + part["stroke"]!.GetValue<string>()).GetWidenedPathGeometry(pen).GetOutlinedPathGeometry();
    }

    /// The superellipse |x|ⁿ + |y|ⁿ = rⁿ around the canvas's middle.
    private static Geometry Tile(double r, double n)
    {
        const int Steps = 360;
        var points = new Point[Steps];
        for (int i = 0; i < Steps; i++)
        {
            double a = 2 * Math.PI * i / Steps, c = Math.Cos(a), s = Math.Sin(a);
            points[i] = new Point(128 + r * Math.CopySign(Math.Pow(Math.Abs(c), 2 / n), c), 128 + r * Math.CopySign(Math.Pow(Math.Abs(s), 2 / n), s));
        }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            ctx.PolyLineTo(points.Skip(1).ToList(), false, false);
        }
        g.Freeze();
        return g;
    }

    private static Spec Load()
    {
        using var stream = typeof(FolderIconSet).Assembly.GetManifestResourceStream("Bloom.Assets.folder-icons.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();
        Look LookOf(string key)
        {
            var o = root[key]!.AsObject();
            Color C(JsonNode? n) => (Color)ColorConverter.ConvertFromString(n!.GetValue<string>());
            var tones = o["tones"]!.AsArray().Select(n => (Brush)new SolidColorBrush(C(n))).ToArray();
            foreach (var b in tones) b.Freeze();
            return new Look(C(o["top"]), C(o["bottom"]), tones);
        }
        var icons = root["icons"]!.AsObject().Select(i => (i.Key, i.Value!.AsObject().ToDictionary(s => int.Parse(s.Key, System.Globalization.CultureInfo.InvariantCulture), s => s.Value!.AsObject()))).ToList();
        var insets = root["tile"]!.AsObject().ToDictionary(s => int.Parse(s.Key, System.Globalization.CultureInfo.InvariantCulture), s => (double)s.Value!);
        return new Spec((double)root["curve"]!, insets, LookOf("silver"), LookOf("graphite"), icons);
    }
}
