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
        p.Apply(true, custom, 0);
        Check.Eq(p.Accent.Color, Accents.Swatch(custom, true), "dark accent");
        Check.True(p.Dark, "dark");
        Check.Near(Hsl.From(p.Solid.Color).h, custom.Hue, 8, "the neutrals take the hue (their low saturation rounds to 8 bits)");
        Check.True(p.Text.Color != lightText, "the text differs between the themes");
        p.Apply(false, null, 0);
        Check.Eq(p.Accent.Color, Color.FromRgb(0xE0, 0x4E, 0x78), "no accent is Bloom's rose");
    }
}
