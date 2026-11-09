using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Addons;

/// How an add-on words and lays out its own settings page, from the "easy" parts of its settings.schema.json: a plainer
/// name and line than the schema's, a line shown only while another setting calls for it, rarely changed lines under
/// 자세히, pictures of the options. All optional; a part that isn't right is left out, never the page.
internal sealed record AddonEasyItem(AddonText? Name, AddonText? About, bool NoAbout, AddonWhen? When, bool More, bool Minutes, AddonPicture? Thumb)
{
    /// An item's "easy": {"name", "about" ("" for no line), "when", "more", "minutes", "thumb"}.
    public static AddonEasyItem? Of(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        return new AddonEasyItem(AddonText.Of(o["name"]), AddonText.Of(o["about"]), IsEmpty(o["about"]), AddonWhen.Of(o["when"]),
            o["more"]?.GetValueKind() == JsonValueKind.True, o["minutes"]?.GetValueKind() == JsonValueKind.True, AddonPicture.Of(o["thumb"], 60, 36));
    }

    internal static bool IsEmpty(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) && s.Length == 0;
}

/// A group's "easy": {"name", "about" ("" for no line), "folded" (all its lines under one 자세히)}.
internal sealed record AddonEasyGroup(AddonText? Name, AddonText? About, bool NoAbout, bool Folded)
{
    public static AddonEasyGroup? Of(JsonNode? node) => node is JsonObject o
        ? new AddonEasyGroup(AddonText.Of(o["name"]), AddonText.Of(o["about"]), AddonEasyItem.IsEmpty(o["about"]), o["folded"]?.GetValueKind() == JsonValueKind.True)
        : null;
}

/// When a line shows: while the switch `Key` is on, or while the choice `Key` is `Is` (or isn't `Not`).
internal sealed record AddonWhen(string Key, string? Is, string? Not)
{
    public bool Holds(AddonValues v) => Is is not null ? v.Text(Key) == Is : Not is not null ? v.Text(Key) != Not : v.Bool(Key);

    public static AddonWhen? Of(JsonNode? node) =>
        node is JsonObject o && Str(o, "key") is { Length: > 0 } key ? new AddonWhen(key, Str(o, "is"), Str(o, "not")) : null;

    private static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}

/// A picture an add-on ships for its page (an option's, a switch's thumb): shapes on a W × H canvas that Bloom paints in
/// its own colours (Pic), the chosen option bright and the others faint.
internal sealed record AddonPicture(double W, double H, IReadOnlyList<AddonShape> Shapes)
{
    public const int MaxShapes = 64;

    /// {"size": [w, h], "shapes": [...]}; null when it has no shape or one of them isn't right.
    public static AddonPicture? Of(JsonNode? node, double w = 72, double h = 44)
    {
        if (node is not JsonObject o || o["shapes"] is not JsonArray list || list.Count is 0 or > MaxShapes) return null;
        if (o["size"] is JsonArray size)
        {
            if (size.Count != 2 || AddonShape.Num(size[0]) is not (> 0 and <= 600 and var sw) || AddonShape.Num(size[1]) is not (> 0 and <= 600 and var sh)) return null;
            (w, h) = (sw, sh);
        }
        var shapes = new List<AddonShape>(list.Count);
        foreach (var s in list)
        {
            if (AddonShape.Of(s) is not { } shape) return null;
            shapes.Add(shape);
        }
        return new AddonPicture(w, h, shapes);
    }
}

/// One shape in canvas units: "box" (X, Y, W, H, corner R; Left: only its left corners round; Dash: a dashed outline),
/// "dot" (centre X, Y, radius R), "path" (Data, path markup) or "text" (Words centred on X, Y in a box W wide). Fill and
/// Stroke name a paint: ink (the text colour), accent, popup (the window's), btn (words on the accent) or a #RRGGBB of
/// the add-on's own; a shape with neither is filled with ink.
internal sealed record AddonShape(string Kind, double X, double Y, double W = 0, double H = 0)
{
    public double R { get; init; }
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public double Width { get; init; } = 1;
    public double Alpha { get; init; } = 1;
    public bool Left { get; init; }
    public bool Dash { get; init; }
    public string? Data { get; init; }
    public AddonText? Words { get; init; }
    public double Size { get; init; } = 11;
    public bool Bold { get; init; } = true;

    public static AddonShape? Of(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        AddonShape? s = null;
        if (o["box"] is JsonArray box && Nums(box, 4) is { } b) s = new AddonShape("box", b[0], b[1], b[2], b[3]);
        else if (o["dot"] is JsonArray dot && Nums(dot, 3) is { } d) s = new AddonShape("dot", d[0], d[1]) { R = d[2] };
        else if (o["path"] is JsonValue pv && pv.TryGetValue(out string? data) && data.Length is > 0 and <= 2000) s = new AddonShape("path", 0, 0) { Data = data };
        else if (o["text"] is { } t && TextOf(t) is { } words && o["at"] is JsonArray at && Nums(at, 2) is { } a)
            s = new AddonShape("text", a[0], a[1], Num(o["width"]) ?? 64) { Words = words, Size = Num(o["size"]) ?? 11, Bold = o["bold"]?.GetValueKind() != JsonValueKind.False };
        if (s is null) return null;
        string? fill = Paint(o["fill"]), stroke = Paint(o["stroke"]);
        if (fill == "" || stroke == "") return null; // a paint it doesn't know
        if (s.Kind == "box" && o["r"] is { } r) s = s with { R = Num(r) ?? -1 };
        if (s.R < 0 || s.Kind == "text" && (s.Size is <= 0 or > 200 || s.W <= 0)) return null;
        return s with
        {
            Fill = fill,
            Stroke = stroke,
            Width = Num(o["width"]) is { } w && s.Kind != "text" ? w : 1,
            Alpha = Math.Clamp(Num(o["alpha"]) ?? 1, 0, 1),
            Left = o["corners"] is JsonValue c && c.TryGetValue(out string? corners) && corners == "left",
            Dash = o["dash"]?.GetValueKind() == JsonValueKind.True,
        };
    }

    /// Text the same in every language, or one per language ({"ko", "en", "ja", "zh"}).
    private static AddonText? TextOf(JsonNode n) =>
        n is JsonValue v && v.TryGetValue(out string? s) && s.Length is > 0 and <= 80 ? new AddonText(s, s) : AddonText.Of(n);

    /// A paint's name as Bloom keeps it; null for none, "" for one it doesn't know.
    private static string? Paint(JsonNode? n)
    {
        if (n is null) return null;
        if (n is not JsonValue v || !v.TryGetValue(out string? s)) return "";
        if (s is "ink" or "accent" or "popup" or "btn") return s;
        return s.Length == 7 && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit) ? s.ToUpperInvariant() : "";
    }

    private static double[]? Nums(JsonArray a, int n)
    {
        if (a.Count != n) return null;
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (Num(a[i]) is not { } d) return null;
            x[i] = d;
        }
        return x;
    }

    internal static double? Num(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) && Math.Abs(d) <= 10_000 ? d : null;
}
