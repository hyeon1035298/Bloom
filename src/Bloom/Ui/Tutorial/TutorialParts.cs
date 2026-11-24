using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Core;

namespace Bloom.Ui;

internal enum TileMark { None, Pick, Badge, Switch }

/// A picture with a name under it. The mark says how it is chosen.
internal sealed class PictureTile : Grid
{
    private readonly TileMark mark;
    private readonly SmoothBorder card;
    private readonly TextBlock about;
    private readonly ContentControl link = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private bool on, aboutAccent, dim, off;
    private (string label, Action open)? linkTo;

    public PictureTile(FrameworkElement scene, double sceneHeight, string name, string? about = null,
        TileMark mark = TileMark.None, bool on = false, bool small = false, int nameLines = 1)
    {
        this.mark = mark;
        this.on = on;
        Scene = scene;
        scene.Height = sceneHeight;
        var sp = new StackPanel();
        sp.Children.Add(scene);
        var nameText = Theme.T(name, small ? 12.5 : 13, Theme.P.Text, Theme.Medium);
        nameText.Margin = new Thickness(10, 8, 10, 0);
        if (nameLines > 1)
        {
            nameText.TextWrapping = TextWrapping.Wrap;
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            nameText.MaxHeight = nameLines * nameText.FontSize * 1.4;
        }
        sp.Children.Add(nameText);
        this.about = Theme.T(about ?? "", 11.5, Theme.P.TextFaint);
        this.about.Margin = new Thickness(10, 2, 10, 8);
        this.about.Visibility = about is null ? Visibility.Collapsed : Visibility.Visible;
        sp.Children.Add(this.about);
        link.Margin = new Thickness(10, 2, 10, 8);
        sp.Children.Add(link);
        card = new SmoothBorder { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = sp, Cursor = Cursors.Hand, ClipToBounds = true };
        Children.Add(card);
        MouseLeftButtonUp += (_, e) =>
        {
            if (off) return;
            Press();
            e.Handled = true;
        };
        Draw();
    }

    public FrameworkElement Scene { get; }

    public bool On
    {
        get => on;
        set
        {
            on = value;
            Draw();
        }
    }

    public event Action<bool>? Toggled;
    public Action? Click { get; set; }

    public string? About
    {
        get => about.Visibility == Visibility.Visible ? about.Text : null;
        set
        {
            about.Text = value ?? "";
            about.Visibility = value is null || linkTo is not null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool AboutAccent
    {
        get => aboutAccent;
        set
        {
            aboutAccent = value;
            Draw();
        }
    }

    public (string label, Action open)? Link
    {
        get => linkTo;
        set
        {
            linkTo = value;
            link.Content = value is { } l ? TutorialParts.Link(l.label, l.open) : null;
            link.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            if (value is not null) about.Visibility = Visibility.Collapsed;
        }
    }

    public bool Dim
    {
        get => dim;
        set
        {
            dim = value;
            Scene.Opacity = value ? 0.4 : 1;
        }
    }

    public bool Off
    {
        get => off;
        set
        {
            off = value;
            Opacity = value ? 0.45 : 1;
            IsHitTestVisible = !value;
        }
    }

    /// What a click does, by mark: Switch and Badge flip, Pick turns on once, None runs Click.
    internal void Press()
    {
        switch (mark)
        {
            case TileMark.None:
                Click?.Invoke();
                break;
            case TileMark.Pick:
                if (on) return;
                On = true;
                Toggled?.Invoke(true);
                break;
            default:
                On = !on;
                Toggled?.Invoke(on);
                break;
        }
    }

    private void Draw()
    {
        card.Background = Theme.P.Card;
        card.BorderBrush = on && mark != TileMark.None ? Theme.P.Accent : Theme.P.CardLine;
        about.Foreground = aboutAccent ? Theme.P.AccentText : Theme.P.TextFaint;
        about.FontWeight = aboutAccent ? Theme.Semi : Theme.Regular;
    }
}

/// Equal columns of tiles; every row is as tall as the tallest tile of the grid.
internal sealed class TileGrid : Panel
{
    private readonly int columns;
    private readonly double columnGap, rowGap;
    private double rowHeight;

    public TileGrid(int columns, double columnGap, double rowGap)
    {
        this.columns = Math.Max(1, columns);
        this.columnGap = columnGap;
        this.rowGap = rowGap;
    }

    private IEnumerable<PictureTile> Tiles => Children.OfType<PictureTile>();

    public void Add(PictureTile tile) => Children.Add(tile);

    /// Single choice over Pick tiles: one stays on, `picked` gets its index.
    public void PickOne(Action<int> picked)
    {
        var tiles = Tiles.ToList();
        for (int i = 0; i < tiles.Count; i++)
        {
            int index = i;
            tiles[i].Toggled += v =>
            {
                if (!v) return;
                for (int k = 0; k < tiles.Count; k++)
                    if (k != index) tiles[k].On = false;
                picked(index);
            };
        }
    }

    private double ColumnWidth(double width) => double.IsInfinity(width) ? 0 : Math.Max(0, (width - (columns - 1) * columnGap) / columns);

    protected override Size MeasureOverride(Size available)
    {
        double w = ColumnWidth(available.Width);
        rowHeight = 0;
        double widest = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(w > 0 ? w : double.PositiveInfinity, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, c.DesiredSize.Height);
            widest = Math.Max(widest, c.DesiredSize.Width);
        }
        int rows = (InternalChildren.Count + columns - 1) / columns;
        double width = w > 0 ? available.Width : widest * columns + (columns - 1) * columnGap;
        return new Size(width, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * rowGap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double w = ColumnWidth(final.Width);
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int row = i / columns, col = i % columns;
            InternalChildren[i].Arrange(new Rect(col * (w + columnGap), row * (rowHeight + rowGap), w, rowHeight));
        }
        return final;
    }
}

/// A card row: an optional 64x38 picture, a title, an optional line under it, an optional control at the right.
internal sealed class ThumbRow : SmoothBorder
{
    private readonly TextBlock sub;
    private Switch? switched;
    private bool off;

    public ThumbRow(FrameworkElement? thumb, string title, string? sub = null, FrameworkElement? end = null)
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Background = Theme.P.Card;
        BorderBrush = Theme.P.CardLine;
        Padding = new Thickness(12, 9, 12, 9);
        Margin = new Thickness(0, 0, 0, 8);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (thumb is not null)
        {
            thumb.Width = 64;
            thumb.Height = 38;
            thumb.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(thumb);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(title, 13.5, Theme.P.Text, Theme.Medium));
        this.sub = Theme.T(sub ?? "", 12, Theme.P.TextFaint);
        this.sub.TextWrapping = TextWrapping.Wrap;
        this.sub.Margin = new Thickness(0, 2, 12, 0);
        this.sub.Visibility = sub is null ? Visibility.Collapsed : Visibility.Visible;
        text.Children.Add(this.sub);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (end is not null)
        {
            end.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(end, 2);
            g.Children.Add(end);
        }
        Child = g;
        MouseLeftButtonUp += (_, e) =>
        {
            if (off || Click is null) return;
            Click();
            e.Handled = true;
        };
    }

    public static ThumbRow Switched(FrameworkElement? thumb, string title, string? sub, bool on, Action<bool> changed)
    {
        var sw = new Switch(on);
        var row = new ThumbRow(thumb, title, sub, sw) { Cursor = Cursors.Hand };
        row.switched = sw;
        row.Click = () =>
        {
            sw.On = !sw.On;
            changed(sw.On);
        };
        return row;
    }

    public Action? Click { get; set; }
    public Switch? Switch => switched;

    public string? Sub
    {
        get => sub.Visibility == Visibility.Visible ? sub.Text : null;
        set
        {
            sub.Text = value ?? "";
            sub.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool Off
    {
        get => off;
        set
        {
            off = value;
            Opacity = value ? 0.45 : 1;
            IsHitTestVisible = !value;
        }
    }
}

internal static class TutorialParts
{
    /// Title, one line under it, an optional tag above and an optional control at the title's right.
    public static FrameworkElement Header(string title, string? sub, string? tag = null, FrameworkElement? end = null)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        if (tag is not null)
        {
            var t = Theme.T(tag, 11.5, Theme.P.AccentText, Theme.Semi);
            t.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(t);
        }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Theme.T(title, 24, Theme.P.Text, Theme.Semi));
        if (end is not null)
        {
            end.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(end, 1);
            g.Children.Add(end);
        }
        sp.Children.Add(g);
        if (sub is not null)
        {
            var s = Theme.T(Hangul.KeepAll(sub), 13.5, Theme.P.TextDim);
            s.TextWrapping = TextWrapping.Wrap;
            s.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(s);
        }
        return sp;
    }

    /// A faint line with a ringed "i".
    public static FrameworkElement Note(string text)
    {
        var t = Theme.T(Hangul.KeepAll(text), 12, Theme.P.TextFaint);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(0, 10, 0, 0);
        return t;
    }

    /// A faint section label.
    public static FrameworkElement Label(string text)
    {
        var t = Theme.T(text, 12, Theme.P.TextFaint, Theme.Semi);
        t.Margin = new Thickness(0, 0, 0, 8);
        return t;
    }

    /// A text link that takes keyboard focus; Enter or Space runs it. `color` null = Theme.P.AccentText.
    public static FrameworkElement Link(string text, Action open, double size = 12.5, Brush? color = null)
    {
        var t = Theme.T(text, size, color ?? Theme.P.AccentText, Theme.Medium, false);
        t.Cursor = Cursors.Hand;
        t.MouseLeftButtonUp += (_, e) =>
        {
            open();
            e.Handled = true;
        };
        return t;
    }

    /// Makes an element a Tab stop with the tutorial's focus ring; Space runs `act`, and Enter too when `enter`
    /// (links and buttons; on a tile or a row Enter stays the window's "next page").
    public static void Focusable(FrameworkElement e, Action act, CornerRadius ring, bool enter = false)
    {
        e.MouseLeftButtonUp += (_, args) =>
        {
            act();
            args.Handled = true;
        };
    }

    /// The element with keyboard focus runs on Enter itself (a link, or something made Focusable with `enter`).
    public static bool TakesEnter(object? focused) => false;
}
