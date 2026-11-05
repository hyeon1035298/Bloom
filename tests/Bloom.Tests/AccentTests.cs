using System;
using System.Windows.Media;
using Bloom.Ui;

namespace Bloom.Tests;

/// 설정 > 일반 > 색 > 직접: the #RRGGBB the user types and the dark / light pair made from it.
internal static class AccentTests
{
    public static void ColourCodesAreParsed()
    {
        foreach (var (text, rgb) in new[] { ("#3A7BD5", 0x3A7BD5), ("3a7bd5", 0x3A7BD5), ("  #3a7BD5 ", 0x3A7BD5), ("#000000", 0), ("#FFFFFF", 0xFFFFFF) })
        {
            Check.True(Accents.TryParse(text, out var c, out string why), $"«{text}» is a colour code ({why})");
            Check.Eq(c.R << 16 | c.G << 8 | c.B, rgb, $"«{text}»'s colour");
            Check.Eq(why, "", $"«{text}» has no complaint");
        }
    }

    public static void NotColourCodesAreRefusedWithAReason()
    {
        foreach (var text in new[] { null, "", "   ", "#", "#3A7BD", "#3A7BD55", "#GGGGGG", "##3A7BD5", "rgb(1,2,3)", "#3A7 BD" })
        {
            Check.True(!Accents.TryParse(text, out _, out string why), $"«{text}» is no colour code");
            Check.True(why.Length > 0, $"«{text}» says why");
        }
    }

    public static void LightIsTheTypedColourAndDarkTheSameHueLifted()
    {
        var typed = Color.FromRgb(0x3A, 0x7B, 0xD5);
        var a = Accents.Custom(typed);
        Check.Eq(a.Key, Accents.CustomKey, "the key");
        Check.Eq(Accents.Swatch(a, false), typed, "light: the colour as typed");
        var (h, s, l) = Hsl.From(typed);
        var (dh, ds, dl) = Hsl.From(Accents.Swatch(a, true));
        Check.Near(dh, h, 1.5, "dark keeps the hue");
        Check.Near(ds, s, 0.02, "dark keeps the saturation");
        Check.Near(dl, l + 0.18, 0.01, "dark is 0.18 lighter");
        Check.True(!a.Gray, "a blue is not gray");
    }

    public static void DarkNeverPassesTheCeilingAndGrayStaysGray()
    {
        var pale = Accents.Custom(Color.FromRgb(0xF2, 0xF2, 0xF2));
        Check.True(pale.Gray, "a gray code is a gray accent");
        Check.True(Hsl.From(Accents.Swatch(pale, true)).l >= Hsl.From(Accents.Swatch(pale, false)).l, "dark is never darker than light");
        var white = Accents.Custom(Colors.White);
        Check.Eq(Accents.Swatch(white, true), Colors.White, "white stays white");
        var black = Accents.Custom(Colors.Black);
        Check.Near(Hsl.From(Accents.Swatch(black, true)).l, 0.18, 0.01, "black is lifted for the dark theme");
    }

    public static void TheSettingsNameTheAccent()
    {
        Check.Eq(Accents.Resolve("rose", null)?.Key, "rose", "a preset by its key");
        Check.Eq(Accents.Resolve("mint", "#3A7BD5")?.Key, "mint", "the code is only read for custom");
        Check.Eq(Accents.Resolve("custom", "#3A7BD5")?.Key, "custom", "custom with a code");
        Check.True(Accents.Resolve("custom", null) is null, "custom with no code falls back to the default");
        Check.True(Accents.Resolve("custom", "nope") is null, "custom with a bad code falls back to the default");
        Check.True(Accents.Resolve("no-such", null) is null, "an unknown key falls back to the default");
    }

    public static void PaletteWearsTheCustomColourInBothThemes()
    {
        var typed = Color.FromRgb(0x3A, 0x7B, 0xD5);
        var custom = Accents.Custom(typed);
        var p = new Palette();
        p.Apply(false, custom, 0);
        Check.Eq(p.Accent.Color, typed, "light accent");
        var lightText = p.Text.Color;
        var lightSolid = p.Solid.Color;
        p.Apply(true, custom, 0);
        Check.Eq(p.Accent.Color, Accents.Swatch(custom, true), "dark accent");
        Check.True(p.Dark, "dark");
        Check.True(p.Text.Color != lightText, "the text differs between the themes");
        p.Apply(false, null, 0);
        Check.Eq(p.Accent.Color, Color.FromRgb(0xE0, 0x4E, 0x78), "no accent is Bloom's rose");
        Check.Eq(p.Solid.Color, lightSolid, "the grays are the same whatever the accent");
    }

    public static void HsvGoesBothWays()
    {
        foreach (var c in new[] { Color.FromRgb(0x3A, 0x7B, 0xD5), Color.FromRgb(0xFF, 0x7A, 0x9E), Colors.Black, Colors.White, Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0x00, 0xFF, 0x00) })
        {
            var (h, s, v) = Hsv.From(c);
            Check.Eq(Hsv.To(h, s, v), c, $"{c} through HSV");
        }
        var (rh, rs, rv) = Hsv.From(Colors.Red);
        Check.Near(rh, 0, 0.01, "red's hue");
        Check.Near(rs, 1, 0.01, "red's saturation");
        Check.Near(rv, 1, 0.01, "red's value");
        Check.Eq(Hsv.To(240, 1, 1), Colors.Blue, "hue 240 is blue");
        Check.Eq(Hsv.To(360, 1, 1), Colors.Red, "hue 360 is red again");
    }

    public static void TheOtherThemesColourIsDerived()
    {
        var light = Color.FromRgb(0x3A, 0x7B, 0xD5);
        Check.Eq(Accents.DarkOf(light), Accents.Swatch(Accents.Custom(light), true), "dark of a light colour is Custom's");
        var picked = Color.FromRgb(0x72, 0xB6, 0xFF);
        var (_, _, l) = Hsl.From(picked);
        Check.Near(Hsl.From(Accents.LightOf(picked)).l, l - 0.18, 0.01, "a dark pick is 0.18 darker for the light theme");
        Check.Near(Hsl.From(Accents.LightOf(Color.FromRgb(0x30, 0x10, 0x10))).l, 0.08, 0.01, "never darker than 0.08");
        var pale = Color.FromRgb(0xF0, 0xF4, 0xFF);
        Check.Eq(Accents.LightOf(pale), pale, "above 0.86 it stays as it is");
        Check.Eq(Accents.Stored(picked, false), picked, "picked in the light theme: kept as it is");
        Check.Eq(Accents.Stored(picked, true), Accents.LightOf(picked), "picked in the dark theme: its light colour is kept");
        var back = Accents.DarkOf(Accents.Stored(picked, true));
        Check.Near(Hsl.From(back).l, l, 0.01, "the dark theme then shows about what was picked");
    }

    public static void ContrastIsWcags()
    {
        Check.Near(Accents.Contrast(Colors.Black, Colors.White), 21, 0.01, "black on white");
        Check.Near(Accents.Contrast(Colors.White, Colors.White), 1, 0.001, "white on white");
        Check.Near(Accents.Luminance(Colors.White), 1, 0.0001, "white's luminance");
        Check.Near(Accents.Contrast(Color.FromRgb(0x76, 0x76, 0x76), Colors.White), 4.54, 0.02, "#767676 on white");
        var p = new Palette();
        p.Apply(true, null, 0);
        Check.Eq(p.Solid.Color, Accents.DarkGround, "the dark ground is the dark window's");
        p.Apply(false, null, 0);
        Check.Eq(p.Solid.Color, Accents.LightGround, "the light ground is the light window's");
    }

    public static void MakeVisibleLiftsBothSidesOverThreeToOne()
    {
        foreach (var c in new[] { Color.FromRgb(0xF2, 0xE1, 0x5A), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x10, 0x10, 0x40), Color.FromRgb(0x80, 0xFF, 0x80), Color.FromRgb(0xE4, 0xE4, 0xE4) })
        {
            Check.True(!Accents.Visible(c), $"{c} starts hard to see");
            var fixedC = Accents.MakeVisible(c);
            var (d, l) = Accents.Contrasts(fixedC);
            Check.True(d >= Accents.MinContrast && l >= Accents.MinContrast, $"{c} → {fixedC}: dark {d:0.00}, light {l:0.00}");
            var (h0, s0, _) = Hsl.From(c);
            var (h1, s1, _) = Hsl.From(fixedC);
            if (s0 > 0.1) Check.Near(h1, h0, 3, $"{c} keeps its hue");
            Check.Near(s1, s0, 0.08, $"{c} keeps its saturation");
        }
        var fine = Color.FromRgb(0xE0, 0x4E, 0x78);
        Check.True(Accents.Visible(fine), "rose stands out");
        Check.Eq(Accents.MakeVisible(fine), fine, "a colour that stands out stays as it is");
    }
}
