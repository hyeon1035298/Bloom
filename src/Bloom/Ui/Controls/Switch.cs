using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// On/off switch (settings): an outlined track with a ring for a knob; on, the knob fills with the accent and moves
/// right, and a short lit line in the accent shows where it came from.
internal sealed class Switch : SmoothBorder
{
    private const double KnobSize = 14, Travel = 18;
    private readonly Ellipse knob, glow;
    private readonly Rectangle lit, litGlow;
    private bool on;

    public Switch(bool value)
    {
        Width = 40;
        Height = 22;
        CornerRadius = new CornerRadius(11); // half the height: a pill, its ends round
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent; // the whole track takes the click
        var canvas = new Canvas();
        // the glows: the accent fading out round the knob and the line, drawn rather than an Effect (cheaper in lists)
        glow = new Ellipse { Width = KnobSize + 10, Height = KnobSize + 10, IsHitTestVisible = false };
        litGlow = new Rectangle { Width = Travel + 4, Height = 6, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false };
        lit = new Rectangle { Width = Travel, Height = 2, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false };
        knob = new Ellipse { Width = KnobSize, Height = KnobSize, StrokeThickness = 1.5 };
        Canvas.SetLeft(litGlow, 4);
        Canvas.SetTop(litGlow, 7);
        Canvas.SetLeft(lit, 6);
        Canvas.SetTop(lit, 9);
        Canvas.SetTop(knob, 3);
        Canvas.SetTop(glow, -2);
        canvas.Children.Add(litGlow);
        canvas.Children.Add(lit);
        canvas.Children.Add(glow);
        canvas.Children.Add(knob);
        Child = canvas;
        VerticalAlignment = VerticalAlignment.Center;
        On = value;
    }

    public bool On
    {
        get => on;
        set
        {
            on = value;
            BorderBrush = on ? Theme.P.Edge : Theme.P.EdgeStrong;
            double x = on ? 3 + Travel : 3;
            Canvas.SetLeft(knob, x);
            Canvas.SetLeft(glow, x - 5);
            knob.Fill = on ? Theme.P.Accent : Brushes.Transparent;
            knob.Stroke = on ? Theme.P.Accent : Theme.P.TextFaint;
            lit.Fill = Theme.P.Accent;
            var shown = on ? Visibility.Visible : Visibility.Hidden;
            lit.Visibility = litGlow.Visibility = glow.Visibility = shown;
            if (on)
            {
                var c = Theme.P.Accent.Color;
                glow.Fill = Glow(c, 0x8C);
                litGlow.Fill = Glow(c, 0x60);
            }
        }
    }

    private static Brush Glow(Color c, byte alpha)
    {
        var b = new RadialGradientBrush(Color.FromArgb(alpha, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
