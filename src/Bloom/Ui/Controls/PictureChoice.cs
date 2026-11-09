using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Bloom.Ui;

/// A choice of how something looks (설정 > 애드온): Segmented's faint well, each option a small picture over its name,
/// the chosen one raised like an on key (ToggleKey) with its picture in the text colour and accent, the others faint.
/// The pictures are drawn again on a pick (a few shapes each), so they follow the theme's brushes.
internal sealed class PictureChoice : SmoothBorder
{
    /// The picture's size in the option; a picture is drawn on a 72 × 44 canvas and scaled into it.
    public const double ArtWidth = 100, ArtHeight = 61;

    private readonly List<(string value, SmoothBorder item, Viewbox art, TextBlock text, Func<bool, FrameworkElement> draw)> items = new();
    private string? hovered;
    public string Value { get; private set; }
    public event Action<string>? Picked;

    public PictureChoice(IEnumerable<(string value, string label, Func<bool, FrameworkElement> picture)> options, string value)
    {
        Value = value;
        CornerRadius = new CornerRadius(10);
        Background = Theme.P.Card;
        Padding = new Thickness(2);
        Margin = new Thickness(0, 10, 0, 0);
        var grid = new Grid();
        foreach (var (v, label, picture) in options)
        {
            int column = items.Count;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var art = new Viewbox { Width = ArtWidth, Height = ArtHeight, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            // the name's ink centred in a box of its own under the picture: up and down by InkText, across by CentreAcross
            var t = Theme.Label(label, 12, Theme.P.TextFaint, Theme.Medium, true, true);
            var across = new TranslateTransform();
            var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Center, RenderTransform = across, Children = { t } };
            var name = new Border { Height = 18, Margin = new Thickness(4, 5, 4, 0), Child = holder };
            name.SizeChanged += (_, _) => Dispatcher.BeginInvoke(() => CentreAcross(t, across, name), DispatcherPriority.Loaded);
            var stack = new StackPanel();
            stack.Children.Add(art);
            stack.Children.Add(name);
            var b = new SmoothBorder
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 8, 4, 6),
                Margin = new Thickness(column > 0 ? 2 : 0, 0, 0, 0),
                Child = stack,
                Cursor = Cursors.Hand,
                ToolTip = label,
            };
            Grid.SetColumn(b, column);
            string vv = v;
            b.MouseLeftButtonUp += (_, e) =>
            {
                Set(vv);
                Picked?.Invoke(vv);
                e.Handled = true;
            };
            b.MouseEnter += (_, _) =>
            {
                hovered = vv;
                Paint();
            };
            b.MouseLeave += (_, _) =>
            {
                hovered = null;
                Paint();
            };
            items.Add((v, b, art, t, picture));
            grid.Children.Add(b);
        }
        Child = grid;
        Set(value);
    }

    public void Set(string v)
    {
        Value = v;
        foreach (var (value, _, art, _, draw) in items) art.Child = draw(value == v);
        Paint();
    }

    private void Paint()
    {
        foreach (var (value, item, _, text, _) in items)
        {
            bool on = value == Value, hover = value == hovered;
            item.Background = on ? hover ? Theme.P.BtnHover : Theme.P.Btn : hover ? Theme.P.Card : Brushes.Transparent;
            item.BorderBrush = on ? Theme.P.BtnEdge : Brushes.Transparent;
            item.TopLine = on ? Theme.P.BtnTop : null;
            item.BottomLine = on ? Theme.P.BtnBottom : null;
            text.Foreground = on ? Theme.P.Text : hover ? Theme.P.TextDim : Theme.P.TextFaint;
        }
    }

    private static readonly Dictionary<(string text, string font, FontWeight weight, double size, double ppd, double k), (int left, int right)> Columns = new();

    /// Moves a laid-out one-line label across (`move`) so the ink it draws, read at its own pixel size as InkText reads
    /// rows, has its middle on `box`'s middle to the nearest device pixel: Hangul and digits carry uneven side bearings,
    /// up to two pixels at 12 px, which centring the advance box leaves.
    private static void CentreAcross(TextBlock t, TranslateTransform move, FrameworkElement box)
    {
        if (t.Text.Length == 0 || t.ActualWidth <= 0 || PresentationSource.FromVisual(t) is null) return;
        double ppd = VisualTreeHelper.GetDpi(t).PixelsPerDip, k = ppd * InkText.LayoutScale(t);
        var key = (t.Text, t.FontFamily.Source, t.FontWeight, t.FontSize, ppd, k);
        if (!Columns.TryGetValue(key, out var ink))
        {
            var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch),
                t.FontSize, Brushes.Black, null, TextOptions.GetTextFormattingMode(t), ppd);
            int w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace * k) + 8, h = (int)Math.Ceiling(ft.Height * k) + 4;
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen()) dc.DrawText(ft, new Point(4 / k, 0));
            var bmp = new RenderTargetBitmap(w, h, 96 * k, 96 * k, PixelFormats.Pbgra32);
            bmp.Render(v);
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            // a column has ink when some pixel in it is a quarter as strong as the strongest (InkText's rows)
            var colMax = new int[w];
            int most = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    colMax[x] = Math.Max(colMax[x], px[(y * w + x) * 4 + 3]);
            foreach (int a in colMax) most = Math.Max(most, a);
            int left = Array.FindIndex(colMax, a => a > 0 && a * 4 >= most), right = Array.FindLastIndex(colMax, a => a > 0 && a * 4 >= most) + 1;
            ink = left < 0 ? (0, 0) : (left - 4, right - 4);
            Columns[key] = ink;
        }
        if (ink.right <= ink.left) return;
        double at = t.TranslatePoint(new Point(0, 0), box).X - move.X; // where the label is laid out, without this move
        double dx = Math.Round(box.ActualWidth / 2 * k - (at * k + (ink.left + ink.right) / 2.0)) / k;
        if (move.X != dx) move.X = dx;
    }

    /// Sets a centred one-line label off to one side so its ink, not its advance box, is in the middle (a picture's
    /// words, drawn scaled, where the outline is what shows): a margin on one side, which a centred element splits in two.
    internal static void CentreInk(TextBlock t)
    {
        var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.Black, 1.0);
        var ink = ft.BuildGeometry(new Point()).Bounds;
        if (ink.IsEmpty) return;
        // a margin of twice the move on one side: the ink's middle goes onto the box's
        double margin = Math.Round((ft.WidthIncludingTrailingWhitespace - (ink.Left + ink.Right)) * 2) / 2;
        var m = t.Margin;
        t.Margin = margin >= 0 ? new Thickness(m.Left + margin, m.Top, m.Right, m.Bottom) : new Thickness(m.Left, m.Top, m.Right - margin, m.Bottom);
    }

    /// Moves the choice by `delta` (←→ on the row) and returns the value it lands on.
    public string Step(int delta)
    {
        int i = items.FindIndex(x => x.value == Value);
        int j = Math.Clamp(i + delta, 0, items.Count - 1);
        Set(items[j].value);
        return items[j].value;
    }

    /// The next option, back to the first after the last (Enter on the row).
    public string Cycle()
    {
        int i = items.FindIndex(x => x.value == Value);
        Set(items[(i + 1) % items.Count].value);
        return Value;
    }
}
