using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A small segmented choice (settings): the options on a faint well, the chosen one on a lighter fill with a light
/// line along its top. Given a palette of its own (the tutorial's dark first page, over a picture) it draws from that
/// one whatever the theme, a size larger and with a plain raised fill, as it is the page's one control.
internal sealed class Segmented : SmoothBorder
{
    private readonly List<(string value, SmoothBorder item, TextBlock text, FrameworkElement? icon)> items = new();
    private readonly Palette? look;
    public string Value { get; private set; }
    public event Action<string>? Picked;

    public Segmented(IEnumerable<(string value, string label)> options, string value, Palette? look = null)
        : this(options.Select(o => (o.value, o.label, (FrameworkElement?)null)), value, look)
    {
    }

    /// `icon` sits before an option's label; `look` = a fixed palette to draw with in place of Theme.P.
    public Segmented(IEnumerable<(string value, string label, FrameworkElement? icon)> options, string value, Palette? look = null)
    {
        this.look = look;
        Value = value;
        bool own = look is not null;
        CornerRadius = new CornerRadius(own ? 9 : 8);
        // the fixed palette's well is its text's colour, thin: it lies on a picture, not on a card
        Background = look is null ? Theme.P.Card : new SolidColorBrush(Color.FromArgb(0x14, look.Text.Color.R, look.Text.Color.G, look.Text.Color.B));
        Padding = new Thickness(own ? 3 : 2);
        VerticalAlignment = VerticalAlignment.Center;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (v, label, icon) in options)
        {
            var t = Theme.Label(label, own ? 12.5 : 12, Theme.P.TextFaint, Theme.Medium, inBorder: true);
            if (t is InkText ink) ink.TieDown = true;
            FrameworkElement child = t;
            if (icon is not null)
            {
                icon.Width = icon.Height = IconSize;
                icon.Margin = new Thickness(0, 0, 6, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                var both = new StackPanel { Orientation = Orientation.Horizontal };
                both.Children.Add(icon);
                both.Children.Add(t);
                child = both;
            }
            var b = new SmoothBorder
            {
                CornerRadius = new CornerRadius(own ? 7 : 6),
                Height = own ? 27 : 24,
                Padding = new Thickness(own ? 12 : 10, 0, own ? 12 : 10, 0),
                Margin = new Thickness(items.Count > 0 && !own ? 2 : 0, 0, 0, 0),
                Child = child,
                Cursor = Cursors.Hand,
            };
            // a drawn mark takes the text colour of what it is in: the palette's, on every option (its opacity tells
            // the chosen one)
            if (icon is not null) TextElement.SetForeground(b, (look ?? Theme.P).Text);
            string vv = v;
            b.MouseLeftButtonUp += (_, e) =>
            {
                Set(vv);
                Picked?.Invoke(vv);
                e.Handled = true;
            };
            items.Add((v, b, t, icon));
            sp.Children.Add(b);
        }
        Child = sp;
        Set(value);
    }

    private const double IconSize = 14;

    public void Set(string v)
    {
        Value = v;
        foreach (var (value, item, text, icon) in items)
        {
            bool on = value == v;
            if (look is null)
            {
                item.Background = on ? Theme.P.Edge : Brushes.Transparent;
                item.TopLine = on ? Theme.P.Hilite : null;
                text.Foreground = on ? Theme.P.Text : Theme.P.TextFaint;
            }
            else
            {
                item.Background = on ? look.Raised : Brushes.Transparent;
                text.Foreground = on ? look.Text : look.TextDim;
            }
            if (icon is not null) icon.Opacity = on ? 1 : 0.7;
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
