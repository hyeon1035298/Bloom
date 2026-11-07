using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A small segmented choice (settings): the options on a faint well, the chosen one on a lighter fill with a light
/// line along its top.
internal sealed class Segmented : SmoothBorder
{
    private readonly List<(string value, SmoothBorder item, TextBlock text)> items = new();
    public string Value { get; private set; }
    public event Action<string>? Picked;

    public Segmented(IEnumerable<(string value, string label)> options, string value)
    {
        Value = value;
        CornerRadius = new CornerRadius(8);
        Background = Theme.P.Card;
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (v, label) in options)
        {
            var t = Theme.Label(label, 12, Theme.P.TextFaint, Theme.Medium, inBorder: true);
            if (t is InkText ink) ink.TieDown = true;
            var b = new SmoothBorder { CornerRadius = new CornerRadius(6), Height = 24, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(items.Count > 0 ? 2 : 0, 0, 0, 0), Child = t, Cursor = Cursors.Hand };
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
            item.Background = on ? Theme.P.Edge : Brushes.Transparent;
            item.TopLine = on ? Theme.P.Hilite : null;
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
