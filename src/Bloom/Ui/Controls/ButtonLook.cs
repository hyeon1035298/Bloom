using System.Windows;

namespace Bloom.Ui;

/// The look buttons share (and keys that are on): a raised fill, lighter under the pointer; in the dark theme a light
/// line along its top, in the light theme a hairline round it and a darker line along its bottom (white on a light
/// card needs an edge). The brushes change with the theme, so one look serves both.
internal static class ButtonLook
{
    public static T Apply<T>(T b, bool hover = true) where T : SmoothBorder => Apply(b, Theme.P, hover);

    /// In another palette's colours (the island keeps its own black or white, whatever Bloom's theme).
    public static T Apply<T>(T b, Palette p, bool hover = true) where T : SmoothBorder
    {
        b.Background = p.Btn;
        b.BorderBrush = p.BtnEdge;
        b.BorderThickness = new Thickness(1);
        b.TopLine = p.BtnTop;
        b.BottomLine = p.BtnBottom;
        if (hover)
        {
            b.MouseEnter += (_, _) => b.Background = p.BtnHover;
            b.MouseLeave += (_, _) => b.Background = p.Btn;
        }
        return b;
    }
}
