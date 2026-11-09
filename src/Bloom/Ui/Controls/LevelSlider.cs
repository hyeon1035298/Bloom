using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// A value along a thin track (settings): the track filled up to it, a round knob, the value written beside it.
/// A press on the track puts the knob there and dragging moves it (Moved, every change); letting go keeps it (Picked).
internal sealed class LevelSlider : StackPanel
{
    private const double TrackWidth = 150, KnobSize = 16;
    private readonly int min, max;
    private readonly Func<int, string> label;
    private readonly TextBlock text;
    private readonly Border fill;
    private readonly Grid knob, track;
    private bool dragging;
    public int Value { get; private set; }
    public event Action<int>? Moved, Picked;

    public LevelSlider(int value, int min, int max, Func<int, string> label)
    {
        this.min = min;
        this.max = max;
        this.label = label;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        text = Theme.T("", 12, Theme.P.TextDim, Theme.Medium, false);
        text.Width = 38;
        text.TextAlignment = TextAlignment.Right;
        text.Margin = new Thickness(0, 0, 10, 0);
        Children.Add(text);
        track = new Grid { Width = TrackWidth, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        track.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Track, VerticalAlignment = VerticalAlignment.Center });
        fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(fill);
        // a raised disc with the accent in its middle
        knob = new Grid { Width = KnobSize, Height = KnobSize, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        knob.Children.Add(new Ellipse { Fill = Theme.P.Raised, Stroke = Theme.P.Line, StrokeThickness = 1 });
        knob.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.P.Accent });
        track.Children.Add(knob);
        Children.Add(track);
        track.MouseLeftButtonDown += (_, e) =>
        {
            dragging = track.CaptureMouse(); // without the capture no release would end it
            Follow(e);
            if (!dragging) Picked?.Invoke(Value);
            e.Handled = true;
        };
        track.MouseMove += (_, e) =>
        {
            if (dragging && e.LeftButton == MouseButtonState.Pressed) Follow(e);
        };
        track.MouseLeftButtonUp += (_, e) =>
        {
            track.ReleaseMouseCapture(); // LostMouseCapture keeps it
            e.Handled = true;
        };
        track.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            Picked?.Invoke(Value);
        };
        Show(Math.Clamp(value, min, max));
    }

    /// A thin upright mark on the track at `v` (where a reading is now, beside the value being set).
    public void Mark(int v)
    {
        double x = (TrackWidth - KnobSize) * (Math.Clamp(v, min, max) - min) / Math.Max(1, max - min) + KnobSize / 2;
        track.Children.Insert(2, new Border
        {
            Width = 2,
            Height = 14,
            CornerRadius = new CornerRadius(1),
            Background = Theme.P.Text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(x - 1, 0, 0, 0),
            IsHitTestVisible = false,
        });
    }

    /// Moves it by `delta` (←→ on the row) and returns where it went.
    public int Step(int delta)
    {
        Show(Math.Clamp(Value + delta, min, max));
        return Value;
    }

    private void Follow(MouseEventArgs e)
    {
        // a slider its page dropped while it was dragged has no place on screen: the pointer would read as its start
        if (PresentationSource.FromVisual(track) is null) return;
        double share = Math.Clamp((e.GetPosition(track).X - KnobSize / 2) / (TrackWidth - KnobSize), 0, 1);
        int v = min + (int)Math.Round(share * (max - min));
        if (v == Value) return;
        Show(v);
        Moved?.Invoke(v);
    }

    private void Show(int v)
    {
        Value = v;
        double x = (TrackWidth - KnobSize) * (v - min) / Math.Max(1, max - min);
        knob.Margin = new Thickness(x, 0, 0, 0);
        fill.Width = x + KnobSize / 2;
        text.Text = label(v);
    }
}
