using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Bloom.Ui;

/// A one-line control label (a button, key cap, segmented option, chip, nav row) with its ink, not its line box, in the
/// middle of its container. The line box follows the first font's ascent and descent, which leaves Segoe capitals and
/// digits low and Hangul a little high; this label finds the ink of its scripts instead (H for Latin and digits, 한 for
/// Hangul, 国 for kana and Han, the text itself when it is only symbols such as ↵), so a descender doesn't lift it.
/// The ink is read from the glyphs drawn at the label's own pixel size: hinting moves their edges by up to a pixel, so
/// the outlines alone (BuildGeometry) put some labels a pixel off. It stretches to its container's height, so the
/// container's padding must be even; the move is whole device pixels, a tie going up. Not for paragraphs, wrapped text
/// or icon glyphs.
internal sealed class InkText : TextBlock
{
    private static readonly Dictionary<(string font, FontWeight weight, FontStyle style, FontStretch stretch, double size, string probe, double k, TextFormattingMode mode, string culture), (int top, int bottom)> Ink = new();
    private readonly TranslateTransform shift = new();

    static InkText()
    {
        // TextBlock seals OnPropertyChanged: a new text or font moves the ink through these instead
        foreach (var p in new[] { TextProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty })
            p.OverrideMetadata(typeof(InkText), new FrameworkPropertyMetadata((d, _) => ((InkText)d).Place()));
    }

    public InkText()
    {
        VerticalAlignment = VerticalAlignment.Stretch;
        RenderTransform = shift;
        // the move is rounded to the device pixels Bloom's scale makes: a new scale moves it again (once laid out anew)
        Loaded += (_, _) =>
        {
            Theme.ScaleChanged -= Rescaled;
            Theme.ScaleChanged += Rescaled;
            Place();
        };
        Unloaded += (_, _) => Theme.ScaleChanged -= Rescaled;
    }

    private void Rescaled(double from, double to) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Place();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Place();
    }

    /// The ink's middle onto the middle of the label's height.
    private void Place()
    {
        double dy = 0, h = RenderSize.Height - Padding.Top - Padding.Bottom;
        if (Text.Length > 0 && h > 0)
        {
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip, k = ppd * Scale();
            var (top, bottom) = Rows(ppd, k);
            if (bottom > top) dy = Math.Ceiling((Math.Round(h * k) - top - bottom) / 2 - 0.5) / k;
        }
        if (shift.Y != dy) shift.Y = dy;
    }

    /// Bloom's scale where this label is (1 in the windows it doesn't reach).
    private double Scale()
    {
        if (PresentationSource.FromVisual(this)?.RootVisual is not { } root || ReferenceEquals(root, this)) return 1;
        double s = TransformToAncestor(root).TransformBounds(new Rect(0, 0, 0, 1)).Height;
        return s > 0.01 ? s : 1;
    }

    /// The first and past-the-last pixel rows with ink, from the top of the line, for this font, size and kind of text
    /// drawn at `k` pixels per dip (kept per kind: a few small pictures in all).
    private (int top, int bottom) Rows(double ppd, double k)
    {
        string probe = Probe(Text);
        var mode = TextOptions.GetTextFormattingMode(this);
        var culture = CultureInfo.CurrentUICulture;
        var key = (FontFamily.Source, FontWeight, FontStyle, FontStretch, FontSize, probe, k, mode, culture.Name);
        if (Ink.TryGetValue(key, out var ink)) return ink;
        var ft = new FormattedText(probe, culture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.Black, null, mode, ppd);
        int w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace * k) + 4, h = (int)Math.Ceiling(ft.Height * k) + 4;
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen()) dc.DrawText(ft, new Point(2 / k, 0));
        var bmp = new RenderTargetBitmap(w, h, 96 * k, 96 * k, PixelFormats.Pbgra32);
        bmp.Render(v);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        // a row has ink when some pixel in it is a quarter as strong as the strongest
        var rowMax = new int[h];
        int most = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                rowMax[y] = Math.Max(rowMax[y], px[(y * w + x) * 4 + 3]);
        foreach (int a in rowMax) most = Math.Max(most, a);
        int top = Array.FindIndex(rowMax, a => a > 0 && a * 4 >= most), bottom = Array.FindLastIndex(rowMax, a => a > 0 && a * 4 >= most) + 1;
        ink = top < 0 ? (0, 0) : (top, bottom);
        Ink[key] = ink;
        return ink;
    }

    /// The letters whose ink stands for the text's: one per script in it.
    private static string Probe(string text)
    {
        bool latin = false, hangul = false, han = false;
        foreach (char c in text)
        {
            if (c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏') hangul = true;
            else if (c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿') han = true;
            else if (char.IsLetterOrDigit(c)) latin = true;
        }
        if (!latin && !hangul && !han) return text;
        return (latin ? "H" : "") + (hangul ? "한" : "") + (han ? "国" : "");
    }
}
