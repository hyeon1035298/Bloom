using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Core;

namespace Bloom.Ui;

/// 컬러 팔레트's own drawing: its row of swatches in the launcher, and the picture 전체 복사 puts on the clipboard.
internal static class PaletteViews
{
    private static readonly SolidColorBrush Ink = Frozen(Color.FromRgb(0x20, 0x1A, 0x1C));

    /// The palette in one row: each swatch its colour (its share on it) with its HEX and RGB under it, the one picked
    /// ringed in the accent; a click on one is `pick`. Above them a hint and, while its picture is there to make it again
    /// (`setCount`), − n + for how many colours. `busy`: it is being made again, so the swatches shown are faded.
    public static FrameworkElement Strip(IReadOnlyList<PaletteColor> colors, int selected, int count, bool busy, Action<int>? setCount, Action<int> pick)
    {
        var root = new StackPanel { Margin = new Thickness(22, 0, 22, 8) };
        var top = new Grid { MinHeight = 26, Margin = new Thickness(0, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Theme.T(Tr("클릭하면 HEX 복사 · ← → 색 고르기", "Click to copy HEX · ← → Select color"), 11.5, Theme.P.TextFaint));
        if (setCount is not null)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var label = Theme.T(Tr("색 개수", "Color Count"), 11.5, Theme.P.TextFaint, null, false);
            label.Margin = new Thickness(10, 0, 8, 0);
            right.Children.Add(label);
            right.Children.Add(HomeViews.Stepper(count, PaletteExtract.MinCount, PaletteExtract.MaxCount, setCount,
                (Tr($"색 개수 ({PaletteExtract.MinCount}–{PaletteExtract.MaxCount}) · − + 키", $"Color Count ({PaletteExtract.MinCount}–{PaletteExtract.MaxCount}) · − + keys"), Tr("한 색 줄이기", "Fewer colors"), Tr("한 색 늘리기", "More colors"))));
            Grid.SetColumn(right, 1);
            top.Children.Add(right);
        }
        root.Children.Add(top);
        var row = new Grid { Opacity = busy ? 0.45 : 1 };
        for (int i = 0; i < colors.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tile = Swatch(colors[i], i == selected);
            tile.Margin = new Thickness(i == 0 ? 0 : 2, 0, i == colors.Count - 1 ? 0 : 2, 0); // ten fit at 760 with "255, 255, 255"
            int at = i;
            tile.MouseLeftButtonDown += (_, e) => e.Handled = true; // the search line keeps the focus
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                pick(at);
            };
            Grid.SetColumn(tile, i);
            row.Children.Add(tile);
        }
        root.Children.Add(row);
        return root;
    }

    /// "34%", "<1%".
    public static string Percent(double share) => share < 0.01 ? "<1%" : Math.Round(share * 100).ToString(CultureInfo.InvariantCulture) + "%";

    private static StackPanel Swatch(PaletteColor p, bool picked)
    {
        var c = p.Color;
        string hex = ColorBook.Hex(c);
        bool light = ColorBook.Contrast(c, Colors.Black) >= ColorBook.Contrast(c, Colors.White);
        var share = Theme.T(Percent(p.Share), 10.5, light ? Ink : Palette.OffWhiteBrush, Theme.Medium, false);
        share.Opacity = 0.8;
        share.HorizontalAlignment = HorizontalAlignment.Left;
        share.VerticalAlignment = VerticalAlignment.Bottom;
        share.Margin = new Thickness(7, 0, 0, 4);
        var block = new SmoothBorder { Height = 52, CornerRadius = new CornerRadius(8), Background = Frozen(c), BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), Child = share };
        var ring = new SmoothBorder { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(2), Padding = new Thickness(2), BorderBrush = picked ? Theme.P.Accent : Brushes.Transparent, Child = block };
        var code = Theme.T(hex, 12, picked ? Theme.P.Text : Theme.P.TextDim, Theme.Medium);
        code.Margin = new Thickness(2, 5, 0, 0);
        var rgb = Theme.T($"{c.R}, {c.G}, {c.B}", 10.5, Theme.P.TextFaint);
        rgb.Margin = new Thickness(2, 1, 0, 0);
        Typography.SetNumeralAlignment(rgb, FontNumeralAlignment.Tabular);
        var tile = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"{hex} · {ColorBook.Rgb(c)} · {ColorBook.Name(c)}" };
        tile.Children.Add(ring);
        tile.Children.Add(code);
        tile.Children.Add(rgb);
        if (!picked)
        {
            tile.MouseEnter += (_, _) => ring.BorderBrush = Theme.P.Track;
            tile.MouseLeave += (_, _) => ring.BorderBrush = Brushes.Transparent;
        }
        return tile;
    }

    /// 전체 복사 › 팔레트 이미지: the swatches side by side on white, each with its HEX under it.
    public static BitmapSource Picture(IReadOnlyList<Color> colors)
    {
        const int Side = 160, Label = 44;
        int width = Math.Max(1, colors.Count) * Side, height = Side + Label;
        var face = new Typeface(Theme.Sans, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Palette.OffWhiteBrush, null, new Rect(0, 0, width, height));
            for (int i = 0; i < colors.Count; i++)
            {
                dc.DrawRectangle(Frozen(colors[i]), null, new Rect(i * Side, 0, Side, Side));
                var text = new FormattedText(ColorBook.Hex(colors[i]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 17, Ink, 1.0);
                dc.DrawText(text, new Point(i * Side + (Side - text.Width) / 2, Side + (Label - text.Height) / 2));
            }
        }
        var picture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        picture.Render(dv);
        picture.Freeze();
        return picture;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
