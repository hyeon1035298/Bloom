using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// Ctrl+K (더보기): every action of the selected item, filterable, run with Enter or its own shortcut.
/// It opens on the selected row itself (under it, or over it near the bottom) with the filter on top, growing out of
/// the row in 150 ms; without a row (chat, questions) it sits at the footer's corner. Closes instantly.
internal sealed class ActionPanel : SmoothBorder
{
    private readonly Launcher owner;
    private readonly StackPanel listPanel = new();
    private readonly TextBlock heading;
    private readonly TextBox filter;
    private readonly TextBlock filterHint;
    private List<ActionItem> all = new();
    private readonly List<(Row row, ActionItem act)> rows = new();
    private int sel;
    private readonly ScaleTransform scale = new(1, 1);
    private readonly ScrollViewer scroller;

    // the list's usual cap, one row, and the gaps to the body's edges (the footer corner sits a little higher)
    private const double ListMax = 300, RowHeight = 36, Edge = 8, FooterGap = 10;

    public bool IsOpen => Visibility == Visibility.Visible;

    /// The open panel's title (what opened it).
    public string Heading => heading.Text;

    public ActionPanel(Launcher owner)
    {
        this.owner = owner;
        Width = 330;
        Background = Theme.P.Popup;
        BorderBrush = Theme.P.PopupLine;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(0, 8, 0, 0);
        Lift(28, 6, 0.28);
        RenderTransformOrigin = new Point(1, 1);
        RenderTransform = scale;
        Padding = new Thickness(0);
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        heading = Theme.T("", 11.5, Theme.P.TextFaint, Theme.Semi);
        heading.Margin = new Thickness(16, 9, 16, 4);
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(heading, 2);
        g.Children.Add(heading);
        scroller = new ScrollViewer { Content = listPanel, MaxHeight = ListMax, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(0, 0, 0, 6) };
        scroller.Template = Launcher.ThinScroll();
        Grid.SetRow(scroller, 3);
        g.Children.Add(scroller);
        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(line, 1);
        g.Children.Add(line);
        filter = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = 13.5,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 14, 0),
        };
        filterHint = Theme.T(Tr("할 일 찾기…", "Find an action…"), 13.5, Theme.P.TextFaint);
        filterHint.Margin = new Thickness(16, 0, 14, 0);
        filterHint.IsHitTestVisible = false;
        var fg = new Grid();
        fg.Children.Add(filterHint);
        fg.Children.Add(filter);
        g.Children.Add(fg);
        Child = g;
        filter.TextChanged += (_, _) =>
        {
            filterHint.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Build();
        };
    }

    /// `select`: the row to start on (a menu opens on its current choice).
    /// `anchor`: the selected row's box in the launcher body (null: the footer's corner).
    public void Open(string title, List<ActionItem> actions, int select = 0, Rect? anchor = null)
    {
        all = actions;
        heading.Text = title;
        filter.Text = "";
        Build();
        if (select > 0 && select < rows.Count)
        {
            sel = select;
            Paint();
        }
        Visibility = Visibility.Visible;
        Place(anchor);
        if (Motion.Moves)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Grow(0.96));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Grow(0.96));
        }
        BeginAnimation(OpacityProperty, Motion.Fades ? Motion.Paced(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }) : null);
        if (sel > 0) Dispatcher.BeginInvoke(Paint, System.Windows.Threading.DispatcherPriority.Loaded); // scroll to it once laid out
        filter.Focus();
        Keyboard.Focus(filter);
    }

    private void Place(Rect? anchor)
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        var body = Parent as FrameworkElement;
        double room = body?.ActualHeight ?? 0;
        double h = Fit(room);
        if (anchor is not { } a || body is null || room <= 0)
        {
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 12, FooterGap);
            RenderTransformOrigin = new Point(1, 1);
            return;
        }
        double top;
        if (a.Bottom + 4 + h <= room - Edge)
        {
            top = a.Bottom + 4; // under the row, growing down from it
            RenderTransformOrigin = new Point(1, 0);
        }
        else if (a.Top - 4 - h >= Edge)
        {
            top = a.Top - 4 - h; // over it, growing up
            RenderTransformOrigin = new Point(1, 1);
        }
        else
        {
            top = Math.Max(Edge, room - h - Edge);
            RenderTransformOrigin = new Point(1, 0.5);
        }
        VerticalAlignment = VerticalAlignment.Top;
        // its right edge meets the row's, but a row in a split view's left column ends near the middle: kept inside the
        // body's left edge there, where the panel was cut off (more so at a larger size, the body being fewer dips wide)
        double right = Math.Max(12, body.ActualWidth - a.Right + 10);
        Margin = new Thickness(0, top, Math.Max(0, Math.Min(right, body.ActualWidth - Width - Edge)), 0);
    }

    /// The panel's height once it fits the body. A long list scrolls inside the panel instead of running past the
    /// launcher's bottom edge, where it was cut off: the list keeps what the body leaves after the filter line, the
    /// title and the edges (one row at least). room ≤ 0: the body isn't laid out yet, the list keeps its usual cap.
    private double Fit(double room)
    {
        Margin = new Thickness(0); // DesiredSize counts the margin, and the last opening's offset isn't the panel's height
        scroller.MaxHeight = ListMax;
        Measure(new Size(Width, double.PositiveInfinity));
        double over = DesiredSize.Height - (room - Edge - FooterGap);
        if (room <= 0 || over <= 0) return DesiredSize.Height;
        scroller.MaxHeight = Math.Max(RowHeight, scroller.DesiredSize.Height - scroller.Margin.Bottom - over);
        Measure(new Size(Width, double.PositiveInfinity));
        return DesiredSize.Height;
    }

    private static DoubleAnimation Grow(double from) => Motion.Paced(new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });

    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        owner.ActionsClosed();
    }

    private void Build()
    {
        listPanel.Children.Clear();
        rows.Clear();
        var q = new Query(filter.Text);
        foreach (var a in all)
        {
            if (!q.IsEmpty && Matcher.Score(q, new MatchTarget(a.Title)) <= 0) continue;
            var r = new Row(null, RowHeight) { Margin = new Thickness(6, 0, 6, 0) };
            var tile = Icons.Tile(a.Glyph == '\0' ? G.App : a.Glyph, a.Glyph == G.Sparkle, 22);
            tile.Margin = new Thickness(0, 0, 10, 0);
            r.Add(tile, GridLength.Auto);
            var t = Theme.T(a.Title, 13.5, a.Danger ? Theme.P.Danger : Theme.P.Text, Theme.Medium);
            r.Add(t, new GridLength(1, GridUnitType.Star));
            if (a.Keys is { } k) r.Add(Keys.Caps(k), GridLength.Auto);
            var act = a;
            r.Clicked = () => owner.RunAction(act);
            rows.Add((r, a));
            listPanel.Children.Add(r);
        }
        sel = 0;
        Paint();
    }

    private void Paint()
    {
        for (int i = 0; i < rows.Count; i++) rows[i].row.Selected = i == sel;
        if (sel >= 0 && sel < rows.Count) rows[sel].row.BringIntoView();
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Close();
                return true;
            case Key.Down:
                if (rows.Count > 0) sel = (sel + 1) % rows.Count;
                Paint();
                return true;
            case Key.Up:
                if (rows.Count > 0) sel = (sel - 1 + rows.Count) % rows.Count;
                Paint();
                return true;
            case Key.Enter:
                if (sel >= 0 && sel < rows.Count) owner.RunAction(rows[sel].act);
                return true;
            case Key.K when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                Close();
                return true;
        }
        return false;
    }
}
