using System.Windows;

namespace Bloom.Ui;

/// The look buttons share (and keys that are on): a raised fill, lighter under the pointer; in the dark theme a light
/// line along its top, in the light theme a hairline round it and a darker line along its bottom (white on a light
/// card needs an edge). The brushes change with the theme, so one look serves both.
internal static class ButtonLook
{
    public static T Apply<T>(T b, bool hover = true) where T : SmoothBorder
    {
        b.Background = Theme.P.Btn;
        b.BorderBrush = Theme.P.BtnEdge;
        b.BorderThickness = new Thickness(1);
        b.TopLine = Theme.P.BtnTop;
        b.BottomLine = Theme.P.BtnBottom;
        if (hover)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.BtnHover;
            b.MouseLeave += (_, _) => b.Background = Theme.P.Btn;
        }
        return b;
    }
}
