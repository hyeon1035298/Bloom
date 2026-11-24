using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Screen = Bloom.Native.MonitorNames.Screen;

namespace Bloom.Ui;

/// 작업 카드's page (설정 > 애드온 > 작업 카드): the chosen
/// monitor as a screen with its eight places, the cards drawn at the place picked with the margin and size set, every
/// place a click. A still picture, made again with the page.
internal static class RunCardPictures
{
    /// The places in the add-on's order (settings.schema.json): across and down, 0 left or top, 1 middle, 2 right or bottom.
    public static readonly (string value, int col, int row)[] Places =
    [
        ("topLeft", 0, 0), ("topCenter", 1, 0), ("topRight", 2, 0), ("middleLeft", 0, 1), ("middleRight", 2, 1),
        ("bottomLeft", 0, 2), ("bottomCenter", 1, 2), ("bottomRight", 2, 2),
    ];

    /// The monitor's number in a value ("display2" → 2), 0 for "primary" or anything else.
    public static int Number(string value) =>
        value.StartsWith("display", StringComparison.Ordinal) && int.TryParse(value.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

    /// The monitor a value stands for: "primary" the main one, "display<n>" that number; null when it isn't connected
    /// (the add-on then uses the main monitor).
    public static Screen? Chosen(string value, IReadOnlyList<Screen> screens) =>
        value == "primary" ? screens.FirstOrDefault(s => s.Primary) : screens.FirstOrDefault(s => s.Number == Number(value) && s.Number > 0);

    public static string NameOf(Screen s) => s.Name.Length > 0 ? s.Name : Tr("이름 없는 모니터", "Unnamed monitor");

    // ── 자리 ───────────────────────────────────────────────────────────────────────────────────
    // The add-on's measures (rc_draw.h, run-card.wh.cpp) in its units (a DIP at 100 %): a card's width, a pill's height,
    // the gap between stacked ones; the card here is a working one with three steps, the pill a short title (as wide
    // as its words, FitPillW; with 알약 길이 같은 길이 a card's width, or in a row RowPillW).
    private const double CardW = 340, CardH = 132, FitPillW = 220, RowPillW = 220, PillH = 38, Gap = 8;

    /// The screen of `screen` (the main one's shape when null) on a dark bezel, at most 520 × 292, with a soft wallpaper:
    /// the cards (a pill, then a card further in, as two sessions stack) at `place`, a dashed slot at each other place;
    /// pointing at one names it, a click picks it. The cards and the margin are drawn twice their share of the screen
    /// (less when that would make a card wider than 42 % of it), so they read; `margin` and `size` (%) change them.
    /// `row`: 늘어놓기 가로 (a top or bottom place lays them in a row); `samePill`: 알약 길이 같은 길이.
    public static FrameworkElement Stage(Screen? screen, string place, Func<string, string> name, int margin, int size, bool row, bool samePill, Action<string> pick, string caption)
    {
        double sw = screen is { Width: > 0 } ? screen.Width : 2560, sh = screen is { Height: > 0 } ? screen.Height : 1440, scale = screen?.Scale ?? 1;
        double k = Math.Min(520 / sw, 292 / sh), w = Math.Round(sw * k), h = Math.Round(sh * k);
        double card = CardW * scale * size / 100; // a card's width in the monitor's pixels
        double zoom = Math.Min(2, 0.42 * sw / card);
        double u = scale * size / 100 * k * zoom, m = margin * scale * k * zoom;

        var desk = new Canvas { Width = w, Height = h, Background = Wallpaper(), Clip = new RectangleGeometry(new Rect(0, 0, w, h), 5, 5) };
        desk.Children.Add(new Rectangle { Width = w, Height = h, Fill = Glow(0.22, 0.78, Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), w / h) });
        desk.Children.Add(new Rectangle { Width = w, Height = h, Fill = Glow(0.80, 0.18, Color.FromArgb(0x40, 0x28, 0x28, 0x50), w / h) });
        var area = new Rect(0, 0, w, h);
        var tags = new List<UIElement>();
        foreach (var (value, col, line) in Places)
        {
            bool on = value == place, across = row && line != 1;
            double pill = !samePill ? FitPillW : across ? RowPillW : CardW;
            var sizes = on ? new[] { new Size(pill * u, PillH * u), new Size(CardW * u, CardH * u) } : [new Size(pill * u, PillH * u)];
            var boxes = across ? Row(sizes, col, line, area, m, Gap * u) : Stack(sizes, col, line, area, m, Gap * u);
            Rectangle? slot = null;
            if (on)
            {
                desk.Children.Add(Put(Mini(pill: true, pill), boxes[0]));
                desk.Children.Add(Put(Mini(pill: false), boxes[1]));
            }
            else
            {
                slot = new Rectangle
                {
                    Width = boxes[0].Width,
                    Height = boxes[0].Height,
                    RadiusX = boxes[0].Height / 2,
                    RadiusY = boxes[0].Height / 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                };
                desk.Children.Add(Put(slot, boxes[0]));
            }
            var all = boxes.Aggregate(Rect.Union);
            var tag = Tag(name(value));
            tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var t = tag.DesiredSize;
            double tx = col == 0 ? all.Left : col == 2 ? all.Right - t.Width : all.Left + (all.Width - t.Width) / 2;
            double ty = line == 2 ? all.Top - t.Height - 5 : all.Bottom + 5;
            Canvas.SetLeft(tag, Math.Clamp(tx, 2, Math.Max(2, w - t.Width - 2)));
            Canvas.SetTop(tag, Math.Clamp(ty, 2, Math.Max(2, h - t.Height - 2)));
            tags.Add(tag);

            all.Inflate(4, 4);
            var hit = new Border { Width = all.Width, Height = all.Height, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            hit.MouseEnter += (_, _) =>
            {
                tag.Visibility = Visibility.Visible;
                if (slot is null) return;
                slot.StrokeDashArray = null;
                slot.Stroke = Brushes.White;
                slot.Fill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            };
            hit.MouseLeave += (_, _) =>
            {
                tag.Visibility = Visibility.Collapsed;
                if (slot is null) return;
                slot.StrokeDashArray = new DoubleCollection { 3, 2 };
                slot.Stroke = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
                slot.Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            };
            string picked = value;
            hit.MouseLeftButtonUp += (_, e) =>
            {
                pick(picked);
                e.Handled = true;
            };
            desk.Children.Add(Put(hit, all));
        }
        foreach (var tag in tags) desk.Children.Add(tag);

        var bezel = new SmoothBorder
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Theme.P.Dark ? Color.FromRgb(0x0B, 0x0B, 0x0D) : Color.FromRgb(0x1F, 0x1F, 0x23)),
            Child = desk,
        };
        var fit = new Viewbox { Child = bezel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center };
        var under = Theme.T(caption, 12, Theme.P.TextFaint);
        under.HorizontalAlignment = HorizontalAlignment.Center;
        under.Margin = new Thickness(0, 8, 0, 0);
        var all2 = new StackPanel { Margin = new Thickness(0, 12, 0, 2) };
        all2.Children.Add(fit);
        all2.Children.Add(under);
        return all2;
    }

    /// The boxes of cards stacked from a place (rc_place.h Stack): top places down, bottom places up, the middle ones
    /// centred on the middle; across, a card keeps to its place's side.
    internal static Rect[] Stack(Size[] sizes, int col, int row, Rect area, double margin, double gap)
    {
        double total = sizes.Sum(s => s.Height) + gap * Math.Max(0, sizes.Length - 1);
        double y = row == 0 ? area.Top + margin : row == 2 ? area.Bottom - margin : area.Top + Math.Max(margin, (area.Height - total) / 2);
        var boxes = new Rect[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            var s = sizes[i];
            double x = col == 0 ? area.Left + margin : col == 2 ? area.Right - margin - s.Width : area.Left + (area.Width - s.Width) / 2;
            if (row == 2)
            {
                boxes[i] = new Rect(x, y - s.Height, s.Width, s.Height);
                y = boxes[i].Top - gap;
            }
            else
            {
                boxes[i] = new Rect(x, y, s.Width, s.Height);
                y = boxes[i].Bottom + gap;
            }
        }
        return boxes;
    }

    /// The boxes of cards in one row from a place (rc_place.h Row): the oldest at the place's corner, each next one
    /// further in; a top place's cards align their tops, a bottom place's their bottoms; at a centre place the first is
    /// in the middle and the next go to its right and left in turn. A middle place stacks.
    internal static Rect[] Row(Size[] sizes, int col, int row, Rect area, double margin, double gap)
    {
        if (row == 1) return Stack(sizes, col, row, area, margin, gap);
        var boxes = new Rect[sizes.Length];
        double Top(Size s) => row == 0 ? area.Top + margin : area.Bottom - margin - s.Height;
        if (col != 1)
        {
            double x = col == 0 ? area.Left + margin : area.Right - margin;
            for (int i = 0; i < sizes.Length; i++)
            {
                var s = sizes[i];
                boxes[i] = new Rect(col == 0 ? x : x - s.Width, Top(s), s.Width, s.Height);
                x = col == 0 ? boxes[i].Right + gap : boxes[i].Left - gap;
            }
            return boxes;
        }
        // left to right: the even ones past the first, furthest first, then the first, then the odd ones
        var order = new List<int>();
        for (int i = 2; i < sizes.Length; i += 2) order.Insert(0, i);
        if (sizes.Length > 0) order.Add(0);
        for (int i = 1; i < sizes.Length; i += 2) order.Add(i);
        double cx = area.Left + (area.Width - (sizes.Sum(s => s.Width) + gap * Math.Max(0, sizes.Length - 1))) / 2;
        foreach (int i in order)
        {
            boxes[i] = new Rect(cx, Top(sizes[i]), sizes[i].Width, sizes[i].Height);
            cx = boxes[i].Right + gap;
        }
        return boxes;
    }

    private static FrameworkElement Put(FrameworkElement e, Rect at)
    {
        e.Width = at.Width;
        e.Height = at.Height;
        Canvas.SetLeft(e, at.X);
        Canvas.SetTop(e, at.Y);
        return e;
    }

    /// A soft wallpaper: blue-grey to lilac to peach.
    private static Brush Wallpaper() => new LinearGradientBrush
    {
        StartPoint = new Point(0.1, 0),
        EndPoint = new Point(0.9, 1),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0x7F, 0x9C, 0xC4), 0),
            new GradientStop(Color.FromRgb(0xA9, 0xA6, 0xCC), 0.42),
            new GradientStop(Color.FromRgb(0xE3, 0xBB, 0xA8), 1),
        },
    };

    /// A round soft light (or shade) on the wallpaper at (x, y) of its box, `aspect` = width / height.
    private static Brush Glow(double x, double y, Color c, double aspect) => new RadialGradientBrush
    {
        Center = new Point(x, y),
        GradientOrigin = new Point(x, y),
        RadiusX = 0.45,
        RadiusY = 0.45 * aspect,
        GradientStops = { new GradientStop(c, 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
    };

    /// A place's name on a dark chip, shown while it is pointed at.
    private static Border Tag(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x11, 0x11, 0x11)),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 2, 7, 3),
        Child = Theme.T(text, 11, Brushes.White),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

    /// A card (a working one: the mark, its title and time, the steps' bar, three steps) or a pill, in the add-on's
    /// units on the window's colours, its words as bars; fitted to the box it is put in.
    private static FrameworkElement Mini(bool pill, double pillW = FitPillW)
    {
        double cw = pill ? pillW : CardW, ch = pill ? PillH : CardH;
        var c = new Canvas { Width = cw, Height = ch };
        void Add(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }
        void Bar(double x, double y, double bw, double bh, double alpha) =>
            Add(new SmoothBorder { Width = bw, Height = bh, CornerRadius = new CornerRadius(bh / 2), Background = Theme.P.Text, Opacity = alpha }, x, y);
        Add(new SmoothBorder { Width = cw, Height = ch, CornerRadius = new CornerRadius(pill ? ch / 2 : 12), Background = Theme.P.Popup, BorderBrush = Theme.P.PopupLine, BorderThickness = new Thickness(2) }, 0, 0);
        double my = pill ? ch / 2 : 25;
        Add(new Ellipse { Width = 22, Height = 22, Fill = Theme.P.Hover }, 9, my - 11);
        Add(new Ellipse { Width = 11, Height = 11, Fill = Theme.P.Accent }, 14.5, my - 5.5);
        if (pill)
        {
            Bar(38, my - 4, cw - 108, 8, 0.7);
            Bar(cw - 36, my - 4, 24, 8, 0.4);
            return new Viewbox { Child = c, Stretch = Stretch.Fill };
        }
        Bar(40, 21, 160, 8, 0.7);
        Bar(298, 21, 28, 8, 0.4);
        Bar(40, 37, 70, 6, 0.3);
        Bar(296, 37, 30, 6, 0.3);
        Bar(14, 54, 98, 4, 0.8);
        Bar(116, 54, 98, 4, 0.8);
        Bar(218, 54, 52, 4, 0.8);
        Bar(274, 54, 52, 4, 0.12);
        double[] widths = [120, 160, 110];
        for (int i = 0; i < widths.Length; i++)
        {
            double y = 72 + i * 20;
            Bar(14, y, 8, 7, 0.35);
            Bar(28, y, widths[i], 7, i == 2 ? 0.3 : 0.5);
            Bar(300, y, 26, 7, 0.3);
        }
        return new Viewbox { Child = c, Stretch = Stretch.Fill };
    }
}
