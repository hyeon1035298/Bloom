using System.Windows;
using System.Windows.Controls;

namespace Bloom.Ui;

internal static class Keys
{
    /// "Ctrl+Shift+Enter" → [Ctrl][Shift][↵] key caps.
    public static StackPanel Caps(string combo)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string part in combo.Split('+'))
        {
            string label = part switch { "Enter" => "↵", "Up" => "↑", "Down" => "↓", "Left" => "←", "Right" => "→", "Backspace" => "⌫", "Tab" => "Tab", _ => part };
            sp.Children.Add(Cap(label));
        }
        return sp;
    }

    public static Border Cap(string label)
    {
        var t = Theme.Label(label, 11, Theme.P.KeyFg, Theme.Medium);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        if (label is "↵" or "↑" or "↓" or "←" or "→" or "⌫") t.FontSize = 12;
        return new SmoothBorder
        {
            Background = Theme.P.Key,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 0),
            MinWidth = 19,
            Height = 19,
            Margin = new Thickness(3, 0, 0, 0),
            Child = t,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
