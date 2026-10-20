using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A thin bar for the mini player's progress and volume: a click or a drag puts the value anywhere on it (0 … 1);
/// `Chosen` comes when the mouse lets go. Not enabled: only shows (JRiver's progress).
internal sealed class SeekBar : Grid
{
    private readonly SmoothBorder track, fill, thumb;
    private double value;
    private bool dragging;

    /// The place the user chose (mouse released).
    public event Action<double>? Chosen;

    public SeekBar()
    {
        Height = 18;
        Background = Brushes.Transparent; // hit-testing over the whole height
        track = new SmoothBorder { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Track, VerticalAlignment = VerticalAlignment.Center };
        fill = new SmoothBorder { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        thumb = new SmoothBorder { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Children.Add(track);
        Children.Add(fill);
        Children.Add(thumb);
        SizeChanged += (_, _) => Paint();
        MouseEnter += (_, _) => UpdateThumb();
        MouseLeave += (_, _) => UpdateThumb();
        MouseLeftButtonDown += (_, e) =>
        {
            if (!IsEnabled) return;
            dragging = true;
            CaptureMouse();
            Set(e);
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (dragging) Set(e);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            ReleaseMouseCapture();
            Set(e);
            Chosen?.Invoke(value);
            UpdateThumb();
            e.Handled = true;
        };
        IsEnabledChanged += (_, _) =>
        {
            Opacity = IsEnabled ? 1 : 0.55;
            Cursor = IsEnabled ? Cursors.Hand : null;
            UpdateThumb();
        };
        Cursor = Cursors.Hand;
    }

    /// 0 … 1. A drag in progress is not moved from outside.
    public double Value
    {
        get => value;
        set
        {
            if (dragging) return;
            this.value = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            Paint();
        }
    }

    private void Set(MouseEventArgs e)
    {
        double w = ActualWidth;
        value = w <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / w, 0, 1);
        Paint();
    }

    private void Paint()
    {
        double w = ActualWidth;
        fill.Width = Math.Max(0, w * value);
        thumb.Margin = new Thickness(Math.Clamp(w * value - 5, 0, Math.Max(0, w - 10)), 0, 0, 0);
    }

    private void UpdateThumb() => thumb.Visibility = IsEnabled && (IsMouseOver || dragging) ? Visibility.Visible : Visibility.Collapsed;
}
