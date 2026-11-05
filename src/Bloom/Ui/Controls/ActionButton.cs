using System;
using System.Windows;
using System.Windows.Input;

namespace Bloom.Ui;

/// A window's own button (처음 설정's 이전 · 다음, 바뀐 점's 확인): the primary one in the accent, the other on a key tile.
internal static class ActionButton
{
    public static SmoothBorder Make(string label, bool primary, Action click)
    {
        var b = new SmoothBorder
        {
            Child = Theme.Label(label, 13.5, primary ? Theme.P.AccentInk : Theme.P.Text, Theme.Medium, inBorder: true),
            Background = primary ? Theme.P.Accent : Theme.P.Key,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(18, 8, 18, 9),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }
}
