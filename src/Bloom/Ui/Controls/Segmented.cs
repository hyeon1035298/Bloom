using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A small segmented choice (settings), drawn as a sunken well with a hairline,
/// the chosen option on a raised surface with a soft shadow.
internal sealed class Segmented : SmoothBorder
{
    private readonly List<(string value, Border item, TextBlock text)> items = new();
    private static readonly System.Windows.Media.Effects.DropShadowEffect Lift = MakeLift();
    public string Value { get; private set; }
    public event Action<string>? Picked;

    private static System.Windows.Media.Effects.DropShadowEffect MakeLift()
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.25, Color = Colors.Black };
        e.Freeze();
        return e;
    }

    public Segmented(IEnumerable<(string value, string label)> options, string value)
    {
        Value = value;
        CornerRadius = new CornerRadius(10);
        Background = Theme.P.Well;
        BorderBrush = Theme.P.Line;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (v, label) in options)
        {
            var t = Theme.Label(label, 12, Theme.P.TextFaint, Theme.Medium);
            var b = new SmoothBorder { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 0, 10, 0), MinHeight = 20, Child = t, Cursor = Cursors.Hand };
            string vv = v;
            b.MouseLeftButtonUp += (_, e) =>
            {
                Set(vv);
                Picked?.Invoke(vv);
                e.Handled = true;
            };
            items.Add((v, b, t));
            sp.Children.Add(b);
        }
        Child = sp;
        Set(value);
    }

    public void Set(string v)
    {
        Value = v;
        foreach (var (value, item, text) in items)
        {
            bool on = value == v;
            item.Background = on ? Theme.P.Raised : Brushes.Transparent;
            item.Effect = on ? Lift : null;
            text.Foreground = on ? Theme.P.Text : Theme.P.TextFaint;
        }
    }

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
