using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Bloom.Core;

/// The colours picked from the screen (or typed), newest first (colors.json, 24 kept), and the palettes made from
/// pictures (컬러 팔레트, PalettesKept kept).
internal sealed partial class ColorBook
{
    public List<string> Recent { get; set; } = new();
    public List<SavedPalette> Palettes { get; set; } = new();

    public const int PalettesKept = 12;

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static ColorBook Current { get; private set; } = new();

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<ColorBook>(Paths.In("colors.json")).Cleaned();
        saver = Saver.ForJson(Paths.In("colors.json"), () => Current);
    }

    /// What colors.json gave made safe to show: a list it gave as null is empty; a colour that doesn't read, and a
    /// palette that is null or keeps no colour, are left out; each list stays within what is kept.
    public ColorBook Cleaned()
    {
        Recent = (Recent ?? new()).Where(h => h is not null && Parse(h) is not null).Take(24).ToList();
        Palettes = (Palettes ?? new()).Where(p => p is not null && p.Clean()).Take(PalettesKept).ToList();
        return this;
    }

    /// The colour the colour view is about (the newest one).
    public static Color? Now => Current.Recent.Count > 0 && Parse(Current.Recent[0]) is { } c ? c : null;

    public static void Add(Color c)
    {
        string hex = Hex(c);
        Current.Recent.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        Current.Recent.Insert(0, hex);
        if (Current.Recent.Count > 24) Current.Recent.RemoveRange(24, Current.Recent.Count - 24);
        saver?.Touch();
    }

    /// A palette made, or made again with another count: first in the list, in place of the one made before from the
    /// same picture; the oldest past PalettesKept go.
    public static void AddPalette(SavedPalette p)
    {
        Current.Palettes.RemoveAll(x => ReferenceEquals(x, p) || (p.Source is not null && string.Equals(x.Source, p.Source, StringComparison.OrdinalIgnoreCase)));
        Current.Palettes.Insert(0, p);
        if (Current.Palettes.Count > PalettesKept) Current.Palettes.RemoveRange(PalettesKept, Current.Palettes.Count - PalettesKept);
        saver?.Touch();
    }

    public static void Clear()
    {
        Current.Recent.Clear();
        Current.Palettes.Clear();
        saver?.Touch();
    }

    public static void Flush() => saver?.Flush();

    // ── text ────────────────────────────────────────────────────────────────────────────────────
    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string Rgb(Color c) => $"rgb({c.R}, {c.G}, {c.B})";

    public static string Hsl(Color c)
    {
        var (h, s, l) = ToHsl(c);
        return $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";
    }

    /// A palette's 전체 복사: the HEX codes one a line, or CSS custom properties numbered from 1 (--color-1: #RRGGBB;).
    public static string HexList(IEnumerable<Color> colors) => string.Join("\n", colors.Select(Hex));

    public static string CssVariables(IEnumerable<Color> colors) => string.Join("\n", colors.Select((c, i) => $"--color-{i + 1}: {Hex(c)};"));

    [GeneratedRegex(@"^#?([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.IgnoreCase)]
    private static partial Regex HexForm();

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbForm();

    [GeneratedRegex(@"^hsla?\(\s*(\d{1,3}(?:\.\d+)?)(?:deg)?\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex HslForm();

    /// "#ff5a8a", "ff5a8a" (only with a # or a letter in it, so a number stays a number), "#f5a", "rgb(255, 90, 138)", "hsl(342, 100%, 68%)".
    public static Color? Parse(string text)
    {
        string t = text.Trim();
        if (HexForm().Match(t) is { Success: true } h)
        {
            string v = h.Groups[1].Value;
            if (!t.StartsWith('#') && (v.Length == 3 || v.All(char.IsDigit))) return null;
            if (v.Length == 3) v = string.Concat(v.Select(ch => $"{ch}{ch}"));
            return Color.FromRgb(Byte(v, 0), Byte(v, 2), Byte(v, 4));
        }
        if (RgbForm().Match(t) is { Success: true } r)
        {
            int R = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), G = int.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture), B = int.Parse(r.Groups[3].Value, CultureInfo.InvariantCulture);
            return R > 255 || G > 255 || B > 255 ? null : Color.FromRgb((byte)R, (byte)G, (byte)B);
        }
        if (HslForm().Match(t) is { Success: true } s)
        {
            double H = double.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture), S = double.Parse(s.Groups[2].Value, CultureInfo.InvariantCulture), L = double.Parse(s.Groups[3].Value, CultureInfo.InvariantCulture);
            return S > 100 || L > 100 ? null : FromHsl(H, S / 100, L / 100);
        }
        return null;
    }

    private static byte Byte(string hex, int at) => byte.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // ── maths ───────────────────────────────────────────────────────────────────────────────────
    public static (double h, double s, double l) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9)
        {
            byte v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Part(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
        }
        return Color.FromRgb((byte)Math.Round(Part(p, q, h + 1 / 3.0) * 255), (byte)Math.Round(Part(p, q, h) * 255), (byte)Math.Round(Part(p, q, h - 1 / 3.0) * 255));
    }

    /// WCAG relative luminance and contrast ratio (for "흰 글자 4.6:1").
    public static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// The nearest everyday colour name ("분홍", "남색" …) for the subtitle.
    public static string Name(Color c)
    {
        var (h, s, l) = ToHsl(c);
        if (l < 0.1) return Tr("검정", "Black");
        if (l > 0.94) return Tr("흰색", "White");
        if (s < 0.1) return l < 0.35 ? Tr("짙은 회색", "Dark Gray") : l > 0.7 ? Tr("밝은 회색", "Light Gray") : Tr("회색", "Gray");
        string hue = h switch
        {
            < 12 or >= 345 => Tr("빨강", "Red"),
            < 35 => l < 0.4 ? Tr("갈색", "Brown") : Tr("주황", "Orange"),
            < 55 => Tr("노랑", "Yellow"),
            < 75 => Tr("연두", "Lime"),
            < 160 => Tr("초록", "Green"),
            < 190 => Tr("청록", "Teal"),
            < 205 => Tr("하늘", "Sky"),
            < 250 => Tr("파랑", "Blue"),
            < 275 => Tr("남보라", "Indigo"),
            < 300 => Tr("보라", "Purple"),
            < 330 => Tr("자주", "Magenta"),
            _ => Tr("분홍", "Pink"),
        };
        if (h is >= 330 and < 345 || (hue == Tr("빨강", "Red") && l > 0.7)) hue = Tr("분홍", "Pink");
        return l < 0.28 ? Tr($"짙은 {hue}", $"Dark {hue}") : l > 0.78 ? Tr($"연한 {hue}", $"Light {hue}") : hue;
    }

    // ── gradients ───────────────────────────────────────────────────────────────────────────────
    public static string Css(IReadOnlyList<Color> stops, int angle = 135)
    {
        var parts = stops.Select((c, i) => $"{Hex(c)} {Math.Round(100.0 * i / Math.Max(1, stops.Count - 1))}%");
        return $"linear-gradient({angle}deg, {string.Join(", ", parts)})";
    }

    public static LinearGradientBrush Brush(IReadOnlyList<Color> stops)
    {
        var b = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
        for (int i = 0; i < stops.Count; i++) b.GradientStops.Add(new GradientStop(stops[i], (double)i / Math.Max(1, stops.Count - 1)));
        b.Freeze();
        return b;
    }

    /// A palette as even stripes, left to right, with hard edges (its row's swatch).
    public static LinearGradientBrush Stripes(IReadOnlyList<Color> colors)
    {
        var b = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        for (int i = 0; i < colors.Count; i++)
        {
            b.GradientStops.Add(new GradientStop(colors[i], (double)i / colors.Count));
            b.GradientStops.Add(new GradientStop(colors[i], (double)(i + 1) / colors.Count));
        }
        b.Freeze();
        return b;
    }

    /// Gradients made from one colour: its neighbours, lighter and deeper, its opposite, toward sunset or sea, pastel.
    public static List<(string name, string about, Color[] stops)> Suggest(Color c)
    {
        var (h, s, l) = ToHsl(c);
        var list = new List<(string, string, Color[])>();
        if (s < 0.08)
        {
            // a grey: tones of itself, plus a cool and a warm tint
            list.Add((Tr("명암", "Monochrome"), Tr("같은 회색의 밝고 어두운 쪽", "Lighter and darker shades of the same gray"), new[] { FromHsl(0, 0, Math.Min(l + 0.22, 0.95)), FromHsl(0, 0, Math.Max(l - 0.22, 0.06)) }));
            list.Add((Tr("차가운 회색", "Cool Gray"), Tr("푸른 기운을 조금", "With a subtle blue tint"), new[] { c, FromHsl(215, 0.25, l) }));
            list.Add((Tr("따뜻한 회색", "Warm Gray"), Tr("붉은 기운을 조금", "With a subtle warm tint"), new[] { c, FromHsl(25, 0.25, l) }));
            list.Add((Tr("먹물", "Ink"), Tr("검정으로 가라앉기", "Fading into deep black"), new[] { c, FromHsl(240, 0.1, 0.08) }));
            return list;
        }
        double ls = Math.Clamp(s, 0.35, 1);
        list.Add((Tr("같은 계열", "Analogous"), Tr("색상환 이웃 사이 (±25°)", "Adjacent on the color wheel (±25°)"), new[] { FromHsl(h - 25, ls, l), FromHsl(h + 25, ls, l) }));
        list.Add((Tr("밝게 → 이 색", "Light → This Color"), Tr("같은 색의 밝은 쪽에서", "From a lighter tint of this color"), new[] { FromHsl(h, ls * 0.85, Math.Min(l + 0.25, 0.92)), c }));
        list.Add((Tr("이 색 → 깊게", "This Color → Deep"), Tr("같은 색의 어두운 쪽으로", "To a deeper shade of this color"), new[] { c, FromHsl(h + 8, Math.Min(ls + 0.1, 1), Math.Max(l - 0.28, 0.1)) }));
        list.Add((Tr("보색 대비", "Complementary"), Tr("색상환 반대편 (180°)", "Opposite on the color wheel (180°)"), new[] { c, FromHsl(h + 180, ls, l) }));
        list.Add((Tr("세 색 조화", "Triadic"), Tr("120°씩 떨어진 세 색", "Three colors spaced 120° apart"), new[] { FromHsl(h - 120, ls, l), c, FromHsl(h + 120, ls, l) }));
        list.Add((Tr("노을 쪽으로", "Toward Sunset"), Tr("주황 · 분홍빛으로 번지게", "Blending into orange and pink"), new[] { c, Toward(h, s, l, 18, 0.85, 0.62) }));
        list.Add((Tr("바다 쪽으로", "Toward Ocean"), Tr("청록 · 파랑으로 번지게", "Blending into teal and blue"), new[] { c, Toward(h, s, l, 198, 0.7, 0.48) }));
        list.Add((Tr("파스텔", "Pastel"), Tr("연하고 부드럽게", "Soft and gentle tones"), new[] { FromHsl(h, 0.7, 0.86), FromHsl(h + 40, 0.7, 0.84) }));
        return list;
    }

    /// Halfway in hue toward a target, then the target's saturation and lightness.
    private static Color Toward(double h, double s, double l, double th, double ts, double tl)
    {
        double d = ((th - h + 540) % 360) - 180;
        return FromHsl(h + d * 0.6, (s + ts) / 2, (l + tl) / 2);
    }

    /// Bloom's own named two-colour gradients (made here; no outside list). The view shows the ones nearest the colour's hue.
    public static readonly (string name, Color a, Color b)[] Presets =
    {
        (Tr("벚꽃 잎", "Cherry Blossom"), C(0xFBC2D4), C(0xF06A9A)),
        (Tr("자정 장미", "Midnight Rose"), C(0x3A1C4A), C(0xD8487A)),
        (Tr("복숭아 아침", "Peach Morning"), C(0xFFD3A8), C(0xFF8A7A)),
        (Tr("잘 익은 감", "Ripe Persimmon"), C(0xFFB347), C(0xE8553D)),
        (Tr("레몬 사탕", "Lemon Drop"), C(0xFFF3A0), C(0xFFC93C)),
        (Tr("풋사과", "Green Apple"), C(0xD4F28A), C(0x5DBB63)),
        (Tr("숲속 이끼", "Forest Moss"), C(0x9CC58A), C(0x2F5D3A)),
        (Tr("박하", "Peppermint"), C(0xC9F7E4), C(0x3CBFA0)),
        (Tr("얕은 바다", "Shallow Sea"), C(0x8EE6E0), C(0x2A8FBF)),
        (Tr("새벽 하늘", "Dawn Sky"), C(0xA9C8FF), C(0x5B6EE1)),
        (Tr("깊은 밤", "Deep Night"), C(0x1E2A5A), C(0x6A4FC8)),
        (Tr("라벤더 들판", "Lavender Field"), C(0xD9C8FF), C(0x8D6BE0)),
        (Tr("포도알", "Grape"), C(0xB86BD9), C(0x5A2A8C)),
        (Tr("모래 언덕", "Sand Dune"), C(0xF3E2C3), C(0xC79A6B)),
        (Tr("숯과 재", "Charcoal & Ash"), C(0x5C5C66), C(0x1C1C22)),
        (Tr("오로라", "Aurora"), C(0x5EF2C2), C(0x7A5CF0)),
    };

    private static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static IEnumerable<(string name, Color a, Color b)> PresetsNear(Color c, int count)
    {
        var (h, s, _) = ToHsl(c);
        if (s < 0.08) return Presets.Where(p => ToHsl(p.a).s < 0.2 || p.name == Tr("모래 언덕", "Sand Dune")).Take(count);
        double Dist((string, Color a, Color b) p)
        {
            double ha = ToHsl(p.a).h, hb = ToHsl(p.b).h;
            static double D(double x, double y) => Math.Abs(((x - y + 540) % 360) - 180);
            return Math.Min(D(h, ha), D(h, hb));
        }
        return Presets.OrderBy(Dist).Take(count);
    }
}

/// One palette kept in colors.json: its colours (HEX) and their shares, the largest first; the count asked for; when its
/// picture was picked; and that picture (its Bloom Lens copy under Local\lens, while kept there), to make it again from.
internal sealed class SavedPalette
{
    public List<string> Colors { get; set; } = new();
    public List<double> Shares { get; set; } = new();
    public int Count { get; set; } = PaletteExtract.DefaultCount;
    public string? Source { get; set; }
    public DateTime At { get; set; }

    /// After colors.json: only the colours that read (as #RRGGBB), each with its share (0–1; 0 when there is none), the
    /// count kept to 3–10; false when no colour is left.
    public bool Clean()
    {
        var colors = new List<string>();
        var shares = new List<double>();
        if (Colors is { } given)
            for (int i = 0; i < given.Count; i++)
            {
                if (given[i] is not { } text || ColorBook.Parse(text) is not { } c) continue;
                colors.Add(ColorBook.Hex(c));
                shares.Add(Shares is { } s && i < s.Count ? Math.Clamp(s[i], 0, 1) : 0);
            }
        Colors = colors;
        Shares = shares;
        Count = Math.Clamp(Count, PaletteExtract.MinCount, PaletteExtract.MaxCount);
        return colors.Count > 0;
    }

    /// Its colours read back with their shares (a HEX that doesn't read is left out).
    public List<PaletteColor> Read()
    {
        var list = new List<PaletteColor>();
        for (int i = 0; i < Colors.Count; i++)
            if (ColorBook.Parse(Colors[i]) is { } c) list.Add(new PaletteColor(c, i < Shares.Count ? Shares[i] : 0));
        return list;
    }
}
