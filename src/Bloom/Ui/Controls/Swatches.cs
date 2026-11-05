using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// Colour dots (설정 > 색): click or ←→ picks, Enter goes round; the picked one wears a ring. The `rainbow` one (직접)
/// is a ring of hues round its colour.
internal sealed class Swatches : StackPanel
{
    private readonly List<(string value, Border ring)> items = new();
    private readonly Dictionary<string, SolidColorBrush> fills = new();
    public string Value { get; private set; }
    public event Action<string>? Picked;

    public Swatches(IEnumerable<(string value, string name, Color color)> options, string value, string? rainbow = null)
    {
        Value = value;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (var (v, name, color) in options)
        {
            var fill = new SolidColorBrush(color);
            fills[v] = fill;
            var dot = new SmoothBorder { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = fill };
            if (v == rainbow)
            {
                dot.Background = HueRing;
                dot.Child = new SmoothBorder { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            var ring = new SmoothBorder { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2), Child = dot, Cursor = Cursors.Hand, ToolTip = name, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent };
            string vv = v;
            ring.MouseLeftButtonUp += (_, e) =>
            {
                Set(vv);
                Picked?.Invoke(vv);
                e.Handled = true;
            };
            items.Add((v, ring));
            Children.Add(ring);
        }
        Set(value);
    }

    /// A dot's colour, changed in place (직접's while its colour is dragged).
    public void SetColour(string v, Color c)
    {
        if (fills.TryGetValue(v, out var fill)) fill.Color = c;
    }

    /// The hues round a circle, clockwise from red at the top, soft enough to sit among the presets.
    private static readonly ImageBrush HueRing = MakeHueRing();

    private static ImageBrush MakeHueRing()
    {
        const int n = 36;
        var px = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double a = Math.Atan2(x + 0.5 - n / 2.0, n / 2.0 - y - 0.5) * 180 / Math.PI;
                var c = Hsl.To(0xFF, (a + 360) % 360, 0.85, 0.66);
                int i = (y * n + x) * 4;
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = (c.B, c.G, c.R, 0xFF);
            }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp);
        brush.Freeze();
        return brush;
    }

    public void Set(string v)
    {
        Value = v;
        foreach (var (value, ring) in items) ring.BorderBrush = value == v ? Theme.P.Text : Brushes.Transparent;
    }

    public string Step(int delta)
    {
        int i = items.FindIndex(x => x.value == Value);
        Set(items[Math.Clamp(i + delta, 0, items.Count - 1)].value);
        return Value;
    }

    public string Cycle()
    {
        int i = items.FindIndex(x => x.value == Value);
        Set(items[(i + 1) % items.Count].value);
        return Value;
    }
}
