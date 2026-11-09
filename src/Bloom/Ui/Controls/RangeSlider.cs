using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// A span along a thin track (a monitor's brightness range): LevelSlider's track and knobs, two of them, the track
/// filled between them, the span written beside it. A press moves the nearer knob there and dragging moves it, never
/// past the other (`gap` apart at least); letting go keeps it (Picked).
internal sealed class RangeSlider : StackPanel
{
    private const double TrackWidth = 150, KnobSize = 16;
    private readonly int min, max, gap;
    private readonly Func<int, int, string> label;
    private readonly TextBlock text;
    private readonly Border fill;
    private readonly Grid low, high, track;
    private bool dragging, movingHigh;
    public int Low { get; private set; }
    public int High { get; private set; }
    public event Action<int, int>? Picked;

    public RangeSlider(int low, int high, int min, int max, int gap, Func<int, int, string> label)
    {
        this.min = min;
        this.max = max;
        this.gap = gap;
        this.label = label;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        text = Theme.T("", 12, Theme.P.TextDim, Theme.Medium, false);
        text.MinWidth = 38;
        text.TextAlignment = TextAlignment.Right;
        text.Margin = new Thickness(0, 0, 10, 0);
        Children.Add(text);
        track = new Grid { Width = TrackWidth, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        track.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Track, VerticalAlignment = VerticalAlignment.Center });
        fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(fill);
        this.low = Knob();
        this.high = Knob();
        track.Children.Add(this.low);
        track.Children.Add(this.high);
        Children.Add(track);
        track.MouseLeftButtonDown += (_, e) =>
        {
            double x = e.GetPosition(track).X;
            movingHigh = Math.Abs(x - Center(High)) < Math.Abs(x - Center(Low)) || (Low == High && x > Center(High));
            dragging = track.CaptureMouse();
            Follow(e);
            if (!dragging) Picked?.Invoke(Low, High);
            e.Handled = true;
        };
        track.MouseMove += (_, e) =>
        {
            if (dragging && e.LeftButton == MouseButtonState.Pressed) Follow(e);
        };
        track.MouseLeftButtonUp += (_, e) =>
        {
            track.ReleaseMouseCapture();
            e.Handled = true;
        };
        track.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            Picked?.Invoke(Low, High);
        };
        Show(Math.Clamp(low, min, max - gap), Math.Clamp(high, low + gap, max));
    }

    /// LevelSlider's knob: a raised disc with the accent in its middle.
    private static Grid Knob()
    {
        var knob = new Grid { Width = KnobSize, Height = KnobSize, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        knob.Children.Add(new Ellipse { Fill = Theme.P.Raised, Stroke = Theme.P.Line, StrokeThickness = 1 });
        knob.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.P.Accent });
        return knob;
    }

    private double X(int v) => (TrackWidth - KnobSize) * (v - min) / Math.Max(1, max - min);

    private double Center(int v) => X(v) + KnobSize / 2;

    private void Follow(MouseEventArgs e)
    {
        if (PresentationSource.FromVisual(track) is null) return;
        double share = Math.Clamp((e.GetPosition(track).X - KnobSize / 2) / (TrackWidth - KnobSize), 0, 1);
        int v = min + (int)Math.Round(share * (max - min));
        if (movingHigh) Show(Low, Math.Clamp(v, Low + gap, max));
        else Show(Math.Clamp(v, min, High - gap), High);
    }

    private void Show(int lo, int hi)
    {
        Low = lo;
        High = hi;
        low.Margin = new Thickness(X(lo), 0, 0, 0);
        high.Margin = new Thickness(X(hi), 0, 0, 0);
        fill.Margin = new Thickness(Center(lo), 0, 0, 0);
        fill.Width = Math.Max(0, Center(hi) - Center(lo));
        text.Text = label(lo, hi);
    }
}
