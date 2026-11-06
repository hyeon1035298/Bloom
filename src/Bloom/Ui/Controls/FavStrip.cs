using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Search;

namespace Bloom.Ui;

/// The favorites as one row of icons with their names (the empty view, under the search line).
internal sealed class FavStrip : ScrollViewer
{
    public readonly List<ResultItem> Items;
    private readonly List<(Border tile, Rectangle bar)> tiles = new();
    private int selected = -1;
    private int hovered = -1;
    public Action<int>? Clicked, RightClicked;

    /// Dragged with the mouse: (from, insert before) — the second is Items.Count for "at the end".
    public Action<int, int>? Reordered;

    private readonly System.Windows.Controls.Primitives.UniformGrid panel;
    private readonly Rectangle marker = new() { Width = 3, Height = 58, RadiusX = 1.5, RadiusY = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private int pressAt = -1, dropAt = -1;
    private Point pressPoint;
    private bool dragging;

    public FavStrip(List<ResultItem> items)
    {
        Items = items;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        Margin = new Thickness(10, 8, 10, 2);
        // one equal cell per favorite across the whole row (the icon in the middle of its cell); when they no
        // longer fit at 84 px a cell, the row scrolls from the left
        panel = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = Math.Max(1, items.Count) };
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var g = new Grid();
            var icon = FolderKids(item) is { } kids ? FolderIcon(kids) : Icons.Build(item.Icon, 30);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, 9, 0, 0);
            g.Children.Add(icon);
            // the name on up to two lines ("Bloom Space Cleaner" was "Bloom Spa…"), clear of the selection bar
            var label = Theme.T(Core.Hangul.KeepWords(item.Title), 11.5, Theme.P.TextDim, Theme.Medium);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.TextAlignment = TextAlignment.Center;
            label.TextWrapping = TextWrapping.Wrap;
            label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            label.LineHeight = 13.5;
            label.MaxHeight = 27;
            label.MaxWidth = 76;
            label.Margin = new Thickness(2, 45, 2, 0);
            g.Children.Add(label);
            var bar = new Rectangle { Width = 16, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3), Visibility = Visibility.Hidden };
            g.Children.Add(bar);
            var tile = new SmoothBorder
            {
                Width = 80,
                Height = 80,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = g,
                ToolTip = i < 9 ? $"{item.Title} · Ctrl+{i + 1}" : item.Title,
            };
            int at = i;
            tile.MouseEnter += (_, _) =>
            {
                hovered = at;
                Paint();
            };
            tile.MouseLeave += (_, _) =>
            {
                hovered = -1;
                Paint();
            };
            // press: a click, or the start of a drag once the mouse moves (the strip takes the mouse till release)
            tile.MouseLeftButtonDown += (_, e) =>
            {
                pressAt = at;
                pressPoint = e.GetPosition(panel);
                CaptureMouse();
                e.Handled = true;
            };
            tile.MouseRightButtonUp += (_, e) =>
            {
                RightClicked?.Invoke(at);
                e.Handled = true;
            };
            tiles.Add((tile, bar));
            panel.Children.Add(new SmoothBorder { Child = tile, Background = Brushes.Transparent });
        }
        // the drop line floats over the row (no layout shift while dragging)
        var layer = new Canvas { IsHitTestVisible = false };
        layer.Children.Add(marker);
        var stage = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        stage.Children.Add(panel);
        stage.Children.Add(layer);
        Content = stage;
        SizeChanged += (_, _) => stage.Width = Math.Max(ActualWidth, items.Count * 84.0);
        MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) Drag(e.GetPosition(panel));
        };
        MouseLeftButtonUp += (_, e) => Release(e.GetPosition(panel));
        LostMouseCapture += (_, _) => EndDrag();
        // the wheel scrolls sideways only when the icons do not fit
        PreviewMouseWheel += (_, e) =>
        {
            if (ExtentWidth <= ViewportWidth) return;
            ScrollToHorizontalOffset(HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };
    }

    /// A favorites folder's own items when its tile shows them (FolderIcon), else null.
    public static List<ResultItem>? FolderKids(ResultItem item) => item.Kind == ItemKind.Group && item.Data is List<ResultItem> kids ? kids : null;

    /// The items a folder's tile shows (FolderIcon; none: the folder glyph).
    public static IEnumerable<ResultItem> FolderShown(List<ResultItem> kids) => kids.Take(4);

    /// A favorites folder: up to four of its icons, small, on one tile (an empty one shows the folder glyph).
    private static FrameworkElement FolderIcon(List<ResultItem> kids)
    {
        var tile = new SmoothBorder { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Theme.P.Tile, Padding = new Thickness(3) };
        if (kids.Count == 0)
        {
            tile.Child = Theme.Glyph(G.Folder, 15, Theme.P.TileFg);
            return tile;
        }
        var grid = new UniformGrid { Rows = 2, Columns = 2 };
        foreach (var k in FolderShown(kids))
        {
            var ic = Icons.Build(k.Icon, 12);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(ic);
        }
        tile.Child = grid;
        return tile;
    }

    private void Drag(Point p)
    {
        if (pressAt < 0) return;
        if (!dragging)
        {
            if (Math.Abs(p.X - pressPoint.X) < 6 && Math.Abs(p.Y - pressPoint.Y) < 6) return;
            dragging = true;
            var t = tiles[pressAt].tile;
            t.Opacity = 0.85;
            t.Background = Theme.P.Selected;
            Panel.SetZIndex(t, 1);
            t.RenderTransform = new TranslateTransform();
            marker.Fill = Theme.P.Accent;
        }
        ((TranslateTransform)tiles[pressAt].tile.RenderTransform).X = p.X - pressPoint.X;
        // insert before the first tile whose middle is right of the mouse (the dragged one counts where it was)
        int k = tiles.Count;
        for (int i = 0; i < tiles.Count; i++)
        {
            var left = tiles[i].tile.TranslatePoint(new Point(-tiles[i].tile.RenderTransform.Value.OffsetX, 0), panel).X;
            if (p.X < left + tiles[i].tile.ActualWidth / 2)
            {
                k = i;
                break;
            }
        }
        dropAt = k;
        bool still = k == pressAt || k == pressAt + 1;
        marker.Visibility = still ? Visibility.Collapsed : Visibility.Visible;
        if (still) return;
        var edge = k < tiles.Count
            ? tiles[k].tile.TranslatePoint(new Point(-tiles[k].tile.RenderTransform.Value.OffsetX, 0), panel).X - 3.5
            : tiles[^1].tile.TranslatePoint(new Point(tiles[^1].tile.ActualWidth - tiles[^1].tile.RenderTransform.Value.OffsetX, 0), panel).X + 0.5;
        Canvas.SetLeft(marker, edge);
        Canvas.SetTop(marker, 10);
    }

    /// QA: tile `from` pressed and moved `dx` to the right; with `drop`, released there.
    public void DragForQa(int from, double dx, bool drop)
    {
        if (!(dragging && pressAt == from))
        {
            var t = tiles[from].tile;
            pressAt = from;
            pressPoint = t.TranslatePoint(new Point(t.ActualWidth / 2, 30), panel);
        }
        var to = new Point(pressPoint.X + dx, pressPoint.Y);
        Drag(to);
        if (drop) Release(to);
    }

    private void Release(Point p)
    {
        int from = pressAt, to = dropAt;
        bool wasDrag = dragging;
        if (IsMouseCaptured) ReleaseMouseCapture(); // → EndDrag
        EndDrag();
        if (from < 0) return;
        if (!wasDrag)
        {
            Clicked?.Invoke(from);
            return;
        }
        if (to >= 0 && to != from && to != from + 1) Reordered?.Invoke(from, to);
    }

    private void EndDrag()
    {
        if (dragging && pressAt >= 0 && pressAt < tiles.Count)
        {
            var t = tiles[pressAt].tile;
            t.RenderTransform = Transform.Identity;
            t.Opacity = 1;
            Panel.SetZIndex(t, 0);
        }
        dragging = false;
        pressAt = -1;
        dropAt = -1;
        marker.Visibility = Visibility.Collapsed;
        Paint();
    }

    /// Shown again as it was (the launcher's kept home): nothing hovered, selected or half dragged from before. `fresh`
    /// are this opening's items, shown alike, in their place (the list the strip's handlers hold is the same one).
    public void Reuse(List<ResultItem> fresh)
    {
        if (!ReferenceEquals(fresh, Items))
        {
            Items.Clear();
            Items.AddRange(fresh);
        }
        hovered = selected = -1;
        EndDrag();
    }

    /// -1 = none (the selection is in the list below).
    public void Select(int i)
    {
        selected = i;
        Paint();
        if (i >= 0 && i < tiles.Count) tiles[i].tile.BringIntoView();
    }

    private void Paint()
    {
        for (int k = 0; k < tiles.Count; k++)
        {
            var (tile, bar) = tiles[k];
            tile.Background = k == selected ? Theme.P.Selected : k == hovered ? Theme.P.Hover : Brushes.Transparent;
            bar.Visibility = k == selected ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
