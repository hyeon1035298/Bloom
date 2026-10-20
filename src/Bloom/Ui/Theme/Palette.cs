using System;
using System.Linq;
using System.Windows.Media;

namespace Bloom.Ui;

/// Colour tokens. The brushes are shared and mutated in place, so a theme switch repaints every
/// element that uses them. Bloom's palette (oklch → sRGB): rose-tinted neutrals (hue 350) and one
/// rose accent — #ff7a9e dark, #e04e78 light.
internal sealed class Palette
{
    public readonly SolidColorBrush Tint = new(), Solid = new(), Text = new(), TextDim = new(), TextFaint = new(), Line = new(),
        Hover = new(), Selected = new(), Accent = new(), AccentSoft = new(), AccentText = new(), AccentInk = new(), Card = new(),
        CardLine = new(), Tile = new(), TileFg = new(), Key = new(), KeyFg = new(), Danger = new(), Popup = new(), PopupLine = new(),
        Code = new(), UserBubble = new(), Track = new(), Thumb = new(), Selection = new(), Well = new(), Raised = new();

    public bool Dark { get; private set; }

    private static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// `glassTransparency`: 설정 > 성능 > 유리 투명도, the share (%) of the screen behind that shows through the glass.
    public void Apply(bool dark, AccentPreset? accent, int glassTransparency)
    {
        Base(dark);
        if (accent is { Key: not "rose" } a) Recolor(dark, a);
        Glass(glassTransparency);
    }

    /// The glass windows' fill over the acrylic: the solid background, see-through by the setting. Every glass window
    /// shares this brush, so the slider changes them all as it moves.
    public void Glass(int glassTransparency)
    {
        var c = Solid.Color;
        Tint.Color = Color.FromArgb((byte)Math.Round(2.55 * (100 - Math.Clamp(glassTransparency, 0, 100))), c.R, c.G, c.B);
    }

    /// Another colour from the settings: the accent and its soft / text / ink / selection shades, and the
    /// neutrals turned to its hue (same lightness and saturation, so only the tint changes; gray drops it).
    private void Recolor(bool dark, AccentPreset a)
    {
        var accentOnes = new[] { Accent, AccentSoft, AccentText, AccentInk, UserBubble, Selection, Danger };
        foreach (var b in new[] { Solid, Text, TextDim, TextFaint, Line, Hover, Selected, Card, CardLine, Tile, TileFg, Key, KeyFg, Popup, PopupLine, Code, Track, Thumb, Well, Raised })
        {
            var (h, s, l) = Hsl.From(b.Color);
            b.Color = Hsl.To(b.Color.A, a.Gray ? h : a.Hue, a.Gray ? 0 : s, l);
        }
        var acc = C(dark ? a.Dark : a.Light);
        var (ah, asat, al) = Hsl.From(acc);
        Accent.Color = acc;
        AccentSoft.Color = Color.FromArgb(0x1F, acc.R, acc.G, acc.B);
        UserBubble.Color = Color.FromArgb(0x26, acc.R, acc.G, acc.B);
        Selection.Color = Color.FromArgb((byte)(dark ? 0x59 : 0x4D), acc.R, acc.G, acc.B);
        AccentText.Color = Hsl.To(0xFF, ah, asat, Math.Clamp(al + (dark ? 0.05 : -0.06), 0, 1));
        AccentInk.Color = dark ? Hsl.To(0xFF, ah, a.Gray ? 0 : 0.6, 0.1) : Colors.White;
    }

    private void Base(bool dark)
    {
        Dark = dark;
        if (dark)
        {
            // bg #0E0B0C · bg-2 #090607 · surface-2 #1E191C · surface-3 #292225 · line base #FDEDF4
            Set(Solid, 0xFF0E0B0C);
            Set(Text, 0xFFF1ECEE);
            Set(TextDim, 0xFFC1B8BB);
            Set(TextFaint, 0xFF8C8186);
            Set(Line, 0x13FDEDF4);
            Set(Hover, 0x0DFDEDF4);
            Set(Selected, 0x17FDEDF4);
            Set(Accent, 0xFFFF7A9E);
            Set(AccentSoft, 0x1FFF7A9E);
            Set(AccentText, 0xFFFF8EAE);
            Set(AccentInk, 0xFF2A0914);
            Set(Card, 0x0FFDEDF4);
            Set(CardLine, 0x13FDEDF4);
            Set(Tile, 0x16FDEDF4);
            Set(TileFg, 0xFFD6CED2);
            Set(Key, 0x17FDEDF4);
            Set(KeyFg, 0xFFC1B8BB);
            Set(Danger, 0xFFFF6F6F);
            Set(Popup, 0xF51E191C);
            Set(PopupLine, 0x24FDEDF4);
            Set(Code, 0x66090607);
            Set(UserBubble, 0x26FF7A9E);
            Set(Track, 0x2BFDEDF4);
            Set(Thumb, 0x24FDEDF4);
            Set(Selection, 0x59FF7A9E);
            Set(Well, 0x99090607);
            Set(Raised, 0xFF292225);
        }
        else
        {
            // bg #F7F5F6 · bg-2 #F0EDEE · surface-2 #F5F1F3 · ink #2D1C24
            Set(Solid, 0xFFF7F5F6);
            Set(Text, 0xFF201A1C);
            Set(TextDim, 0xFF534A4E);
            Set(TextFaint, 0xFF7C7377);
            Set(Line, 0x162D1C24);
            Set(Hover, 0x0A2D1C24);
            Set(Selected, 0x122D1C24);
            Set(Accent, 0xFFE04E78);
            Set(AccentSoft, 0x1FE04E78);
            Set(AccentText, 0xFFCC3D68);
            Set(AccentInk, 0xFFFFFFFF);
            Set(Card, 0x99FFFFFF);
            Set(CardLine, 0x162D1C24);
            Set(Tile, 0x0F2D1C24);
            Set(TileFg, 0xFF3B3336);
            Set(Key, 0x102D1C24);
            Set(KeyFg, 0xFF534A4E);
            Set(Danger, 0xFFD94444);
            Set(Popup, 0xFAF5F1F3);
            Set(PopupLine, 0x262D1C24);
            Set(Code, 0x0D2D1C24);
            Set(UserBubble, 0x26E04E78);
            Set(Track, 0x262D1C24);
            Set(Thumb, 0x222D1C24);
            Set(Selection, 0x4DE04E78);
            Set(Well, 0xCCF0EDEE);
            Set(Raised, 0xFFFFFFFF);
        }
    }

    private static void Set(SolidColorBrush b, uint argb) => b.Color = C(argb);
}

/// A UI colour the user can pick (설정 > 일반 > 색): the accent for dark and light; the neutrals follow its hue.
internal sealed record AccentPreset(string Key, string Name, uint Dark, uint Light, bool Gray = false)
{
    public double Hue => Hsl.From(Color.FromArgb(0xFF, (byte)(Dark >> 16), (byte)(Dark >> 8), (byte)Dark)).h;
}

internal static class Accents
{
    public static readonly AccentPreset[] All =
    {
        new("rose", Tr("장미", "Rose"), 0xFFFF7A9E, 0xFFE04E78),
        new("lavender", Tr("라벤더", "Lavender"), 0xFFB49CFF, 0xFF7B5BDB),
        new("sky", Tr("하늘", "Sky"), 0xFF72B6FF, 0xFF2F72D6),
        new("mint", Tr("민트", "Mint"), 0xFF5ED3AE, 0xFF168F6C),
        new("tangerine", Tr("귤", "Tangerine"), 0xFFFF9B5E, 0xFFD9652A),
        new("lemon", Tr("레몬", "Lemon"), 0xFFEDC94F, 0xFFA87C00),
        new("gray", Tr("그레이", "Gray"), 0xFFE4E4E4, 0xFF2E2E2E, true),
    };

    public static AccentPreset? Find(string? key) => Array.Find(All, a => a.Key == key);

    public const string CustomKey = "custom";

    /// The accent the settings name: a preset, or "custom" (a #RRGGBB the user typed); null when there is none (Rose).
    public static AccentPreset? Current => Resolve(Core.Settings.Current.Accent, Core.Settings.Current.AccentCustom);

    public static AccentPreset? Resolve(string? key, string? custom) =>
        key == CustomKey ? (TryParse(custom, out var c, out _) ? Custom(c) : null) : Find(key);

    /// "#3A7BD5" (the # may be left out) → its colour; otherwise why not, in the UI's language.
    public static bool TryParse(string? text, out Color color, out string error)
    {
        color = default;
        string t = (text ?? "").Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.Length == 0)
        {
            error = Tr("색 코드를 적어 주세요 (예: #3A7BD5)", "Type a colour code (like #3A7BD5)");
            return false;
        }
        if (t.Length != 6 || !t.All(Uri.IsHexDigit))
        {
            error = Tr("#과 16진수 6자리(0-9, A-F)로 적어 주세요 (예: #3A7BD5)", "Use # and 6 hex digits (0-9, A-F), like #3A7BD5");
            return false;
        }
        uint v = Convert.ToUInt32(t, 16);
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        error = "";
        return true;
    }

    /// The typed colour is the light theme's accent; the dark one is the same hue, lifted (the presets' pairs are 0.15-0.22 apart in lightness).
    public static AccentPreset Custom(Color c)
    {
        var (h, s, l) = Hsl.From(c);
        var dark = Hsl.To(0xFF, h, s, Math.Max(l, Math.Min(0.86, l + 0.18)));
        uint U(Color x) => 0xFF000000u | (uint)x.R << 16 | (uint)x.G << 8 | x.B;
        return new AccentPreset(CustomKey, Tr("직접", "Custom"), U(dark), U(c), s < 0.06);
    }

    public static Color Swatch(AccentPreset a, bool dark)
    {
        uint v = dark ? a.Dark : a.Light;
        return Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}

internal static class Hsl
{
    public static (double h, double s, double l) From(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color To(byte a, double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = h / 360;
        double Ch(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return s < 1e-9 ? Color.FromArgb(a, B(l), B(l), B(l)) : Color.FromArgb(a, B(Ch(hk + 1.0 / 3)), B(Ch(hk)), B(Ch(hk - 1.0 / 3)));
    }
}
