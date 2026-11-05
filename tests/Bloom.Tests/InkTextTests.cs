using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Ui;

namespace Bloom.Tests;

internal static class InkTextTests
{
    private static readonly double[] Dpis = { 1.0, 1.25, 1.5, 1.75, 2.0 };

    /// Laid out as a window would: Display text, whole device pixels, at `dpi` and Bloom's scale `zoom`.
    private static void Lay(FrameworkElement el, double dpi, double zoom = 1)
    {
        var root = new Border { Child = el, LayoutTransform = new ScaleTransform(zoom, zoom), UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }

    private static Border Chip(TextBlock label) =>
        new() { Padding = new Thickness(8, 2, 8, 3), BorderThickness = new Thickness(1), Child = label };

    private static double Nudge(TextBlock t) => t.RenderTransform is TranslateTransform s ? s.Y : 0;

    public static void BorderKeepsItsHeightAtEveryDpi()
    {
        foreach (double dpi in Dpis)
            foreach (string text in new[] { "일반", "Apply", "Ctrl" })
            {
                var plain = Chip(Theme.T(text, 12, Brushes.Black, Theme.Medium, false));
                var ink = Chip(Theme.Label(text, 12, Brushes.Black, Theme.Medium, inBorder: true));
                Lay(plain, dpi);
                Lay(ink, dpi);
                Check.Eq(ink.ActualHeight, plain.ActualHeight, $"{text} at {dpi:0.##}x");
            }
    }

    public static void LayoutScaleLeavesRenderTransformsOut()
    {
        var label = new InkText { Text = "H" };
        var growing = new Border { Child = label, RenderTransform = new ScaleTransform(0.96, 0.96) };
        _ = new Border { Child = growing, LayoutTransform = new ScaleTransform(1.25, 1.25) };
        Check.Near(InkText.LayoutScale(label), 1.25, 1e-9, "zoom 1.25 under a 0.96 grow-in");
    }

    public static void NudgeIsTheSameWhileGrowingIn()
    {
        foreach (double dpi in Dpis)
            foreach (double zoom in new[] { 1.0, 1.25, 1.5 })
            {
                var still = Theme.Label("Ctrl", 11, Brushes.Black, Theme.Medium, inBorder: true);
                var growing = Theme.Label("Ctrl", 11, Brushes.Black, Theme.Medium, inBorder: true);
                Lay(Chip(still), dpi, zoom);
                Lay(new Border { Child = Chip(growing), RenderTransform = new ScaleTransform(0.96, 0.96) }, dpi, zoom);
                Check.Eq(Nudge(growing), Nudge(still), $"{dpi:0.##}x at zoom {zoom:0.##}");
            }
    }
}
