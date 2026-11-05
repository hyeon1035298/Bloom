using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// A key that is on or off (the tray icons shown, what the island shows). On: a raised key, its text plain, a lit line in
/// the accent along its foot. Off: only an outline and a dim line, its text faint. The text never takes the accent (too
/// little contrast for text); the line says it is on.
internal sealed class ToggleKey : SmoothBorder
{
    private readonly TextBlock text;
    private readonly Rectangle lit;
    private bool on, hover;

    /// The new state, after a click.
    public event Action<bool>? Changed;

    public ToggleKey(string label, bool value)
    {
        on = value;
        CornerRadius = new CornerRadius(8);
        Height = 28;
        Padding = new Thickness(12, 0, 12, 0);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        text = Theme.Label(label, 12, Theme.P.TextFaint, Theme.Medium, inBorder: true);
        // 10 px in from the key's sides and 3 up from its foot (the border and padding taken back)
        lit = new Rectangle { Height = 2, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-3, 0, -3, 2), IsHitTestVisible = false };
        var g = new Grid();
        g.Children.Add(text);
        g.Children.Add(lit);
        Child = g;
        MouseEnter += (_, _) =>
        {
            hover = true;
            Paint();
        };
        MouseLeave += (_, _) =>
        {
            hover = false;
            Paint();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            on = !on;
            Paint();
            Changed?.Invoke(on);
            e.Handled = true;
        };
        Paint();
    }

    public bool On => on;

    private void Paint()
    {
        if (on)
        {
            Background = hover ? Theme.P.BtnHover : Theme.P.Btn;
            BorderBrush = Theme.P.BtnEdge;
            TopLine = Theme.P.BtnTop;
            BottomLine = Theme.P.BtnBottom;
        }
        else
        {
            Background = hover ? Theme.P.Card : Brushes.Transparent;
            BorderBrush = Theme.P.Edge;
            TopLine = BottomLine = null;
        }
        text.Foreground = on ? Theme.P.Text : hover ? Theme.P.TextDim : Theme.P.TextFaint;
        lit.Fill = on ? Theme.P.Accent : Theme.P.Edge;
    }
}
