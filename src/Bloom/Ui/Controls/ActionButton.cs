using System;
using System.Windows;
using System.Windows.Input;

namespace Bloom.Ui;

/// A window's own button (처음 설정's 이전 · 다음, 바뀐 점's 확인): the primary one in the accent, the other a plain button.
internal static class ActionButton
{
    public static SmoothBorder Make(string label, bool primary, Action click)
    {
        var b = new SmoothBorder
        {
            Child = Theme.Label(label, 13, primary ? Theme.P.AccentInk : Theme.P.Text, Theme.Medium, inBorder: true),
            CornerRadius = new CornerRadius(8),
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        if (primary) b.Background = Theme.P.Accent;
        else ButtonLook.Apply(b);
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }
}
