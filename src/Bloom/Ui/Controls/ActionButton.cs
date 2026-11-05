using System;
using System.Windows;
using System.Windows.Input;

namespace Bloom.Ui;

/// How a window's button looks: plain, the accent (the window's primary step), or the red of a step that can't be undone.
internal enum ButtonTone
{
    Plain,
    Accent,
    Danger,
}

/// A window's own button (처음 설정's 이전 · 다음, 바뀐 점's 확인): the primary one in the accent, the other a plain button.
internal static class ActionButton
{
    public static SmoothBorder Make(string label, bool primary, Action click) => Make(label, primary ? ButtonTone.Accent : ButtonTone.Plain, click);

    public static SmoothBorder Make(string label, ButtonTone tone, Action click)
    {
        var ink = tone switch { ButtonTone.Accent => Theme.P.AccentInk, ButtonTone.Danger => Theme.P.DangerInk, _ => Theme.P.Text };
        var b = new SmoothBorder
        {
            Child = Theme.Label(label, 13, ink, Theme.Medium, inBorder: true),
            CornerRadius = new CornerRadius(8),
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        switch (tone)
        {
            case ButtonTone.Accent:
                b.Background = Theme.P.Accent;
                break;
            case ButtonTone.Danger:
                b.Background = Theme.P.DangerFill;
                b.MouseEnter += (_, _) => b.Opacity = 0.88;
                b.MouseLeave += (_, _) => b.Opacity = 1;
                break;
            default:
                ButtonLook.Apply(b);
                break;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }
}
