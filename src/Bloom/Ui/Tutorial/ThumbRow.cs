using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.Core;

namespace Bloom.Ui;

/// A card row: an optional 64x38 picture, a title, an optional line under it, an optional control at the right.
internal sealed class ThumbRow : SmoothBorder
{
    private const double ThumbWidth = 64, ThumbHeight = 38, Radius = 11;
    private readonly TextBlock sub;
    private readonly TutorialParts.EvenLines subLines;
    private readonly Layout layout;
    private Switch? switched;
    private Action? click;
    private bool off, hover;
    private string? subText;

    public ThumbRow(FrameworkElement? thumb, string title, string? sub = null, FrameworkElement? end = null)
    {
        CornerRadius = new CornerRadius(Radius);
        BorderThickness = new Thickness(1);
        Background = Theme.P.Card;
        // a control with a ground or a ring of its own sits a little nearer the edge than a switch: its ink starts inside it
        double right = end switch { Segmented => 10, Swatches => 8, _ => 12 };
        Padding = thumb is null ? new Thickness(14, 4, right, 4) : new Thickness(9, 8, right, 8);
        MinHeight = thumb is null ? 44 : 56;

        FrameworkElement? box = null;
        if (thumb is not null)
        {
            // the picture has no ground of its own: the box only cuts it to its corners
            thumb.Width = ThumbWidth;
            thumb.Height = ThumbHeight;
            box = new Grid { Width = ThumbWidth, Height = ThumbHeight, Clip = Shape(new Rect(0, 0, ThumbWidth, ThumbHeight), new CornerRadius(7)), IsHitTestVisible = false };
            ((Grid)box).Children.Add(thumb);
        }
        var text = new StackPanel();
        text.Children.Add(TutorialParts.Lines(Theme.T(title, 13.5, Theme.P.Text, Theme.Medium), 18));
        this.sub = Theme.T("", 11.5, Theme.P.TextFaint);
        subLines = new TutorialParts.EvenLines(this.sub, 15, 2) { Margin = new Thickness(0, 1, 0, 0) };
        text.Children.Add(subLines);
        Sub = sub;
        Child = layout = new Layout(box, text, end);

        // a choice at the end takes the keyboard itself; the window reads its arrows and Space
        if (end is Segmented seg) TutorialParts.TabStop(seg, Grown(seg.CornerRadius));
        else if (end is Swatches dots) TutorialParts.TabStop(dots, new CornerRadius(15), new Thickness(-2, 0, 0, 0)); // its first dot starts 2 px in

        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => Hover(false);
        MouseLeftButtonUp += (_, e) =>
        {
            if (off || click is null) return;
            click();
            e.Handled = true;
        };
        TutorialParts.TabStop(this, Grown(CornerRadius));
        TutorialParts.Keys(this, () => click?.Invoke());
        Draw();
    }

    public static ThumbRow Switched(FrameworkElement? thumb, string title, string? sub, bool on, Action<bool> changed)
    {
        var sw = new Switch(on);
        var row = new ThumbRow(thumb, title, sub, sw) { switched = sw };
        row.Click = () =>
        {
            sw.On = !sw.On;
            changed(sw.On);
        };
        return row;
    }

    public Action? Click
    {
        get => click;
        set
        {
            click = value;
            Draw();
        }
    }

    public Switch? Switch => switched;

    public string? Sub
    {
        get => subText;
        set
        {
            subText = value;
            sub.Text = Hangul.KeepAll(value ?? "");
            subLines.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool Off
    {
        get => off;
        set
        {
            off = value;
            Opacity = value ? 0.45 : 1;
            IsHitTestVisible = !value;
            IsEnabled = !value; // the control at the end takes no keyboard either
            Draw();
        }
    }

    /// The control lies under the text: beside it, the text would have had too little room (tests).
    internal bool Stacked => layout.Stacked;

    private static CornerRadius Grown(CornerRadius r) => new(r.TopLeft + 2, r.TopRight + 2, r.BottomRight + 2, r.BottomLeft + 2);

    private void Hover(bool over)
    {
        hover = over;
        Draw();
    }

    private void Draw()
    {
        bool takes = !off && click is not null;
        BorderBrush = hover && takes ? Theme.P.EdgeStrong : Theme.P.CardLine;
        Cursor = takes ? Cursors.Hand : null;
        Focusable = takes;
    }

    /// The picture, the text and the control in a line. The control keeps its own width; where that would leave the
    /// text less than it needs to be read, the control goes under the text instead and the row grows.
    private sealed class Layout : Panel
    {
        private const double ThumbGap = 12, EndGap = 12, StackGap = 6, LeastText = 140;
        private readonly FrameworkElement? thumb, end;
        private readonly FrameworkElement text;

        public Layout(FrameworkElement? thumb, FrameworkElement text, FrameworkElement? end)
        {
            (this.thumb, this.text, this.end) = (thumb, text, end);
            if (thumb is not null) Children.Add(thumb);
            Children.Add(text);
            if (end is not null) Children.Add(end);
        }

        public bool Stacked { get; private set; }

        private double TextLeft => thumb is null ? 0 : ThumbWidth + ThumbGap;

        protected override Size MeasureOverride(Size available)
        {
            var open = new Size(double.PositiveInfinity, double.PositiveInfinity);
            thumb?.Measure(open);
            end?.Measure(open);
            double endWidth = end?.DesiredSize.Width ?? 0, endHeight = end?.DesiredSize.Height ?? 0;
            double beside = end is null ? 0 : endWidth + EndGap;
            bool bound = !double.IsInfinity(available.Width);
            Stacked = bound && end is not null && available.Width - TextLeft - beside < LeastText;
            double room = bound ? Math.Max(0, available.Width - TextLeft - (Stacked ? 0 : beside)) : double.PositiveInfinity;
            text.Measure(new Size(room, double.PositiveInfinity));
            double height = Stacked ? text.DesiredSize.Height + StackGap + endHeight : Math.Max(text.DesiredSize.Height, endHeight);
            double width = bound ? available.Width : TextLeft + text.DesiredSize.Width + beside;
            return new Size(width, Math.Max(height, thumb is null ? 0 : ThumbHeight));
        }

        protected override Size ArrangeOverride(Size final)
        {
            thumb?.Arrange(new Rect(0, Math.Round((final.Height - ThumbHeight) / 2), ThumbWidth, ThumbHeight));
            double endWidth = end?.DesiredSize.Width ?? 0, endHeight = end?.DesiredSize.Height ?? 0, textHeight = text.DesiredSize.Height;
            if (Stacked)
            {
                double top = Math.Round((final.Height - (textHeight + StackGap + endHeight)) / 2);
                text.Arrange(new Rect(TextLeft, top, Math.Max(0, final.Width - TextLeft), textHeight));
                end!.Arrange(new Rect(TextLeft, top + textHeight + StackGap, Math.Min(endWidth, Math.Max(0, final.Width - TextLeft)), endHeight));
                return final;
            }
            double beside = end is null ? 0 : endWidth + EndGap;
            text.Arrange(new Rect(TextLeft, Math.Round((final.Height - textHeight) / 2), Math.Max(0, final.Width - TextLeft - beside), textHeight));
            end?.Arrange(new Rect(final.Width - endWidth, Math.Round((final.Height - endHeight) / 2), endWidth, endHeight));
            return final;
        }
    }
}
