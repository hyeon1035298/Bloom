using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Core;

namespace Bloom.Ui;

/// What the tutorial's pages are put together from besides the tiles and the rows: the header, the small texts and
/// the link. The keyboard side is in TutorialFocus.cs.
internal static partial class TutorialParts
{
    /// Title, one line under it, an optional tag above and an optional control at the title's right.
    public static FrameworkElement Header(string title, string? sub, string? tag = null, FrameworkElement? end = null)
    {
        var sp = new StackPanel();
        if (tag is not null)
        {
            var mark = Theme.Sparkle(10, Theme.P.AccentText);
            mark.Margin = new Thickness(0, 0, 5, 0);
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(mark);
            words.Children.Add(Theme.Label(tag, 11.5, Theme.P.AccentText, Theme.Semi, inBorder: true));
            sp.Children.Add(new SmoothBorder
            {
                Child = words,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 0, 10),
                Background = Theme.P.AccentSoft,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
        var name = Lines(Theme.T(title, 24, Theme.P.Text, Theme.Semi), 30);
        if (end is null) sp.Children.Add(name);
        else
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(name);
            end.VerticalAlignment = VerticalAlignment.Center;
            end.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(end, 1);
            line.Children.Add(end);
            sp.Children.Add(line);
        }
        if (sub is not null)
        {
            sp.Children.Add(new EvenLines(Theme.T(Hangul.KeepAll(sub), 13.5, Theme.P.TextDim), 20, 2) { Margin = new Thickness(0, 6, 0, 0) });
        }
        return sp;
    }

    /// A faint line with a ringed "i", wrapping to at most `lines` lines.
    public static FrameworkElement Note(string text, int lines = 2)
    {
        const double line = 17;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // the ring keeps to the first line when the text takes two
        g.Children.Add(new InfoMark { Width = InfoMark.Size, Height = line, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Top });
        var t = new EvenLines(Theme.T(Hangul.KeepAll(text), 12, Theme.P.TextFaint), line, lines) { VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    /// A faint section label.
    public static FrameworkElement Label(string text)
    {
        var t = Lines(Theme.T(text, 12.5, Theme.P.TextFaint, Theme.Semi), 17);
        t.Margin = new Thickness(2, 0, 2, 8);
        return t;
    }

    /// A text link that takes keyboard focus; Enter or Space runs it. `color` null = Theme.P.AccentText.
    public static FrameworkElement Link(string text, Action open, double size = 12.5, Brush? color = null)
    {
        var ink = color ?? Theme.P.AccentText;
        // in a colour it is underlined, which tells it from a status line in the same colour; in one of the text's own
        // grays (the footer's skip) it is a quiet one, underlined only under the pointer and with the keyboard on it
        bool quiet = ReferenceEquals(ink, Theme.P.TextFaint) || ReferenceEquals(ink, Theme.P.TextDim) || ReferenceEquals(ink, Theme.P.Text);
        var t = Theme.T(text, size, ink, Theme.Medium, false);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.Background = Brushes.Transparent; // the whole line box takes the click, not only the glyphs' ink
        t.Cursor = Cursors.Hand;
        bool over = false;
        void Underline() => t.TextDecorations = !quiet || over || RingShown(t) ? TextDecorations.Underline : null;
        t.MouseEnter += (_, _) =>
        {
            over = true;
            Underline();
        };
        t.MouseLeave += (_, _) =>
        {
            over = false;
            Underline();
        };
        // the ring a little wider than the text's box, which ends right at the first and the last glyph
        TabStop(t, new CornerRadius(5), new Thickness(4, 1, 4, 1), _ => Underline());
        Keys(t, open, enter: true);
        t.MouseLeftButtonUp += (_, e) =>
        {
            open();
            e.Handled = true;
        };
        Underline();
        return t;
    }

    /// A one-line text slot with its line height fixed, so a part is as tall in every language's font; what does not
    /// fit ends in an ellipsis.
    internal static TextBlock Lines(TextBlock t, double lineHeight)
    {
        t.LineHeight = lineHeight;
        t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        t.TextWrapping = TextWrapping.NoWrap;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    /// A check mark drawn in a `size` box: the tiles' marks and the "installed" line.
    internal static FrameworkElement Tick(double size, Brush stroke, double thickness)
    {
        double k = size / 16;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(3.2 * k, 8.4 * k), false, false);
            c.PolyLineTo([new Point(6.2 * k, 11.4 * k), new Point(12.8 * k, 4.6 * k)], true, true);
        }
        g.Freeze();
        return new Path
        {
            Data = g,
            Width = size,
            Height = size,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    /// A text slot of up to `Most` lines of a fixed height, then an ellipsis. Text that takes more than one line is
    /// set in the narrowest width that takes no more lines, so its lines come out about as long as each other and the
    /// last is not left with one short word.
    internal sealed class EvenLines : Decorator
    {
        private readonly TextBlock text;
        private double lineHeight, width;
        private int most;

        public EvenLines(TextBlock text, double lineHeight, int most)
        {
            Child = this.text = text;
            text.VerticalAlignment = VerticalAlignment.Top;
            text.HorizontalAlignment = HorizontalAlignment.Left;
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            Set(lineHeight, most);
        }

        public TextBlock Text => text;

        /// The lines the text takes as it is laid out (tests).
        internal int Shown => (int)Math.Round(text.DesiredSize.Height / lineHeight);

        public void Set(double lineHeight, int most)
        {
            (this.lineHeight, this.most) = (lineHeight, Math.Max(1, most));
            text.LineHeight = lineHeight;
            text.TextWrapping = this.most > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            double full = constraint.Width, cap = most * lineHeight;
            text.Measure(new Size(full, double.PositiveInfinity));
            int lines = (int)Math.Round(text.DesiredSize.Height / lineHeight);
            width = full;
            if (lines > most) text.Measure(new Size(full, cap)); // too long: the last line that fits ends in the ellipsis
            else if (lines > 1 && !double.IsInfinity(full))
            {
                // never narrower than its longest word: a word cut in two can still leave as many lines ("Hom / e row")
                double low = Math.Min(full, Math.Max(text.DesiredSize.Width / lines, LongestWord())), high = full;
                while (high - low > 2)
                {
                    double mid = (low + high) / 2;
                    text.Measure(new Size(mid, double.PositiveInfinity));
                    if (text.DesiredSize.Height <= lines * lineHeight + 0.5) high = mid;
                    else low = mid;
                }
                // a little room to spare: a width found to the pixel breaks elsewhere once the scale changes
                width = Math.Min(full, Math.Ceiling(high * 1.03) + 2);
                text.Measure(new Size(width, cap));
            }
            return text.DesiredSize;
        }

        /// The width of the longest run the text does not break inside (a word; one letter or so in Japanese and Chinese).
        private double LongestWord() =>
            new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Transparent, VisualTreeHelper.GetDpi(text).PixelsPerDip).MinWidth;

        protected override Size ArrangeOverride(Size final)
        {
            // at the width it was measured for: given more, the text would break its lines again
            text.Arrange(new Rect(0, 0, Math.Min(final.Width, double.IsInfinity(width) ? final.Width : Math.Max(width, text.DesiredSize.Width)), final.Height));
            return final;
        }
    }

    /// The note's mark: an "i" in a ring, drawn so it is the same in every font, in the middle of the first line.
    private sealed class InfoMark : FrameworkElement
    {
        public const double Size = 14;

        protected override void OnRender(DrawingContext dc)
        {
            var ink = Theme.P.TextFaint;
            double cx = Size / 2, cy = RenderSize.Height / 2;
            dc.DrawEllipse(null, new Pen(ink, 1.2), new Point(cx, cy), Size / 2 - 0.6, Size / 2 - 0.6);
            dc.DrawEllipse(ink, null, new Point(cx, cy - 2.7), 0.85, 0.85);
            dc.DrawLine(new Pen(ink, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(cx, cy - 0.4), new Point(cx, cy + 2.9));
        }
    }
}
