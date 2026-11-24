using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Bloom.Ui;

/// What the pictures drawn in code share (the add-ons' moving ones, the tutorial's still ones): brushes and pens kept
/// by colour, colour maths, keyframes, a shadow without an effect, the mouse pointer, and words kept while they repeat.
internal static class ArtKit
{
    // ---- colours: 0xAARRGGBB, the accent read as the theme has it now

    public static uint Accent => Argb(Theme.P.Accent.Color);

    /// The ink on the accent (dark or light, whichever reads).
    public static uint AccentInk => Argb(Theme.P.AccentInk.Color);

    public static uint Argb(Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    public static uint Fade(uint argb, double share) => (uint)Math.Round((argb >> 24) * share) << 24 | argb & 0xFFFFFF;

    public static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t) << s;
        return Ch(24) | Ch(16) | Ch(8) | Ch(0);
    }

    private static readonly Dictionary<uint, SolidColorBrush> brushes = new();
    private static readonly Dictionary<(uint, double), Pen> pens = new();

    public static SolidColorBrush B(uint argb)
    {
        if (brushes.TryGetValue(argb, out var b)) return b;
        b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return brushes[argb] = b;
    }

    public static Pen P(uint argb, double thickness)
    {
        if (pens.TryGetValue((argb, thickness), out var p)) return p;
        p = new Pen(B(argb), thickness);
        p.Freeze();
        return pens[(argb, thickness)] = p;
    }

    private static readonly Dictionary<(uint, double), Pen> roundPens = new();

    /// A pen with round ends, for strokes drawn as marks (the spark, the check).
    public static Pen Round(uint argb, double thickness)
    {
        if (roundPens.TryGetValue((argb, thickness), out var p)) return p;
        p = new Pen(B(argb), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return roundPens[(argb, thickness)] = p;
    }

    // ---- shapes

    /// A soft shadow under a floating shape, without an effect (two widening layers of `shade`).
    public static void Shadow(DrawingContext dc, Rect r, double radius, uint shade)
    {
        for (int i = 1; i <= 2; i++)
        {
            var s = new Rect(r.Left - i * .5, r.Top + 1 + i, r.Width + i, r.Height);
            dc.DrawRoundedRectangle(B(shade), null, s, radius + i, radius + i);
        }
    }

    private static readonly Geometry arrow = MakeArrow();

    private static Geometry MakeArrow()
    {
        var g = Geometry.Parse("M1,1 L1,16 L5,12 L8,19 L10.5,18 L7.5,11 L13,11 Z").Clone();
        g.Transform = new ScaleTransform(8 / 14.0, 12 / 20.0);
        g.Freeze();
        return g;
    }

    /// The mouse pointer with its tip at (x, y).
    public static void Pointer(DrawingContext dc, double x, double y, uint fill, uint edge, double edgeWidth)
    {
        dc.PushTransform(new TranslateTransform(x, y));
        dc.DrawGeometry(B(fill), P(edge, edgeWidth), arrow);
        dc.Pop();
    }

    // ---- time

    /// The value at p between keyframes (share of the loop, value), eased; held before the first and after the last.
    public static double K(double p, params (double At, double V)[] keys)
    {
        if (p <= keys[0].At) return keys[0].V;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].At)
            {
                var (a, b) = (keys[i - 1], keys[i]);
                double t = (p - a.At) / (b.At - a.At);
                return a.V + (b.V - a.V) * t * t * (3 - 2 * t);
            }
        return keys[^1].V;
    }

    public static Rect Lerp(Rect a, Rect b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    // ---- words, kept while the same words, size, colour and font come again

    /// One picture's words. The screen's scale is read from `owner` each time new words are made, never when the picture
    /// is: it is in no window yet then.
    internal sealed class Words(Visual owner)
    {
        private readonly Dictionary<(string, double, FontWeight, uint, FontFamily), FormattedText> words = new();

        public FormattedText Text(string s, double size, FontWeight weight, uint argb) => Of(s, size, weight, argb, Theme.Sans);

        public FormattedText Glyph(char g, double size, uint argb) => Of(g.ToString(), size, FontWeights.Normal, argb, Theme.IconFont);

        public FormattedText Of(string s, double size, FontWeight weight, uint argb, FontFamily family)
        {
            var key = (s, size, weight, argb, family);
            if (words.TryGetValue(key, out var t)) return t;
            if (words.Count > 200) words.Clear();
            return words[key] = Make(s, size, weight, argb, family);
        }

        /// The words on one line, ending in an ellipsis where they pass `max`. Not kept: the width is this place's alone.
        public FormattedText Trimmed(string s, double size, FontWeight weight, uint argb, FontFamily family, double max)
        {
            var t = Make(s, size, weight, argb, family);
            t.MaxTextWidth = max;
            t.MaxLineCount = 1;
            t.Trimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        /// The kept words were made for another scale of the screen.
        public void Forget() => words.Clear();

        private FormattedText Make(string s, double size, FontWeight weight, uint argb, FontFamily family) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, B(argb),
                VisualTreeHelper.GetDpi(owner).PixelsPerDip);
    }
}
