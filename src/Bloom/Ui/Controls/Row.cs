using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Search;

namespace Bloom.Ui;

/// One selectable line in a list (results, actions, settings).
internal sealed class Row : SmoothBorder
{
    public ResultItem? Item { get; private set; } // this opening's (Reuse puts the new one in)
    public readonly Shape Bar;
    public bool Selectable = true;
    private bool selected, hover;
    public Action? Clicked, RightClicked;
    public Action? Tick; // once a second while shown (a live accessory)
    public Action<ActionItem>? SideClicked; // the item's side button (관리자로 실행) was pressed
    private static Row? pressed; // the row the left button last went down on: only a release on it clicks it
    private FrameworkElement? side;
    public readonly Grid Cells;
    private TextBlock? title; // a result's title (Result): up to its share of the row, the subtitle takes the rest
    private double titleShare;

    // the title's share of a result row: 400 or, with no subtitle, 600 of the 728 px a row has in a launcher of the
    // default size, so a wider launcher shows more of both
    private const double TitleShare = 400.0 / 728, LoneTitleShare = 600.0 / 728;

    /// A press anywhere in a window forgets the row pressed before (a row's own press, nearer the mouse, comes after it):
    /// a row is clicked by a press and a release both on it.
    static Row() => EventManager.RegisterClassHandler(typeof(Window), PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => pressed = null), true);

    public Row(ResultItem? item, double height = 42)
    {
        Item = item;
        Height = height;
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(8, 0, 8, 0);
        Padding = new Thickness(0, 0, 16, 0);
        Background = Brushes.Transparent;
        Cells = new Grid();
        Cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        Child = Cells;
        Bar = new Rectangle { Width = 3, Height = 18, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.P.Accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), SnapsToDevicePixels = true, Visibility = Visibility.Hidden };
        Cells.Children.Add(Bar);
        MouseEnter += (_, _) =>
        {
            hover = true;
            Paint();
        };
        MouseLeave += (_, _) =>
        {
            hover = false;
            Paint();
        };
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => pressed = this), true);
        MouseLeftButtonUp += (_, e) =>
        {
            bool mine = pressed == this; // not a button held on from what is gone (a list drawn anew under it)
            pressed = null;
            if (mine) Clicked?.Invoke();
            e.Handled = true;
        };
        MouseRightButtonUp += (_, e) =>
        {
            RightClicked?.Invoke();
            e.Handled = true;
        };
    }

    /// A result's title gets its share of the width the row has now, before the row's cells are measured.
    protected override Size MeasureOverride(Size constraint)
    {
        if (title is not null && !double.IsInfinity(constraint.Width))
            title.MaxWidth = Math.Max(0, constraint.Width - Padding.Left - Padding.Right) * titleShare;
        return base.MeasureOverride(constraint);
    }

    public void Add(UIElement el, GridLength width)
    {
        Cells.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Grid.SetColumn(el, Cells.ColumnDefinitions.Count - 1);
        Cells.Children.Add(el);
    }

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            Paint();
        }
    }

    /// Shown again as it was (the launcher's kept home): no hover or selection left from before (a hidden window's rows
    /// may never hear the mouse leave), a live accessory up to date. `fresh` is this opening's item, shown alike: what
    /// the row runs is that one (the old one's actions may hang on what is gone, a clip copied again).
    public void Reuse(ResultItem? fresh)
    {
        Item = fresh;
        hover = false;
        selected = false;
        Paint();
        Tick?.Invoke();
    }

    private void Paint()
    {
        if (CalcFrame is not null)
        {
            CalcFrame.BorderBrush = selected ? Theme.P.Accent : Theme.P.CardLine;
            Background = Brushes.Transparent;
            return;
        }
        Background = selected ? Theme.P.Selected : hover && Selectable ? Theme.P.Hover : Brushes.Transparent;
        Bar.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        if (side is not null) side.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The item's side action as a small button at the row's end, shown only on the selected row (not on hover).
    private void AddSide(ResultItem item)
    {
        if (item.Side is not { } act) return;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(act.Glyph, 11, Theme.P.TextDim);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 5, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.T(Tr("관리자로 실행", "Run as admin"), 12, Theme.P.TextDim, Theme.Medium, false));
        var b = new SmoothBorder
        {
            Child = sp,
            Background = Theme.P.Key,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 9, 4),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = act.Keys,
            Visibility = Visibility.Collapsed,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            SideClicked?.Invoke(Item?.Side ?? act);
            e.Handled = true;
        };
        side = b;
        Add(b, GridLength.Auto);
    }

    /// 홈 편집: the item's ✕ (ResultItem.Remove) at the row's end, always shown; a press takes the row off its block
    /// and never runs the row.
    private void AddRemove()
    {
        if (Item?.Remove is null) return;
        var x = HomeViews.EditButton(G.Close, Tr("이 블록에서 빼기", "Remove from this block"), true, () => Item?.Remove?.Invoke());
        x.Margin = new Thickness(8, 0, 0, 0);
        x.VerticalAlignment = VerticalAlignment.Center;
        Add(x, GridLength.Auto);
    }

    /// The standard result line: icon, title, dim subtitle, right accessory.
    public static Row Result(ResultItem item)
    {
        var r = new Row(item);
        var icon = Icons.Build(item.Icon);
        icon.Margin = new Thickness(0, 0, 12, 0);
        r.Add(icon, GridLength.Auto);
        var title = Theme.T(Core.Hangul.KeepAll(item.Title), 14, Theme.P.Text, Theme.Medium);
        r.title = title;
        r.titleShare = item.Subtitle is null ? LoneTitleShare : TitleShare;
        r.Add(title, GridLength.Auto);
        var sub = Theme.T(item.Subtitle ?? "", 12.5, Theme.P.TextFaint);
        sub.Margin = new Thickness(10, 1, 8, 0);
        r.Add(sub, new GridLength(1, GridUnitType.Star));
        if ((item.Accessory ?? item.Live?.Invoke()) is { } acc)
        {
            var a = Theme.T(acc, 12, Theme.P.TextFaint, Theme.Regular, false);
            r.Add(a, GridLength.Auto);
            if (item.Live is not null) r.Tick = () => a.Text = r.Item?.Live?.Invoke() ?? a.Text;
        }
        r.AddSide(item);
        r.AddRemove();
        return r;
    }

    /// The narrow line next to the preview pane: icon and title (the pane shows the rest).
    public static Row Compact(ResultItem item, bool accessory)
    {
        var r = new Row(item);
        var icon = Icons.Build(item.Icon);
        icon.Margin = new Thickness(0, 0, 12, 0);
        r.Add(icon, GridLength.Auto);
        r.Add(Theme.T(Core.Hangul.KeepAll(item.Title), 14, Theme.P.Text, Theme.Medium), new GridLength(1, GridUnitType.Star));
        if (accessory && (item.Accessory ?? item.Live?.Invoke()) is { } acc)
        {
            var a = Theme.T(acc, 11.5, Theme.P.TextFaint, Theme.Regular, false);
            a.Margin = new Thickness(8, 0, 0, 0);
            r.Add(a, GridLength.Auto);
            if (item.Live is not null) r.Tick = () => a.Text = r.Item?.Live?.Invoke() ?? a.Text;
        }
        r.AddSide(item);
        r.AddRemove();
        return r;
    }

    /// 홈 편집's "+ 블록 추가" at a column's end (Launcher.HomeAdd.cs): a dashed outline, the + and its words in the
    /// accent, the subtitle beside them, a chevron saying whether its cards show (`open`).
    public static Row AddTile(ResultItem item, bool open)
    {
        var r = new Row(item, 44) { CornerRadius = new CornerRadius(10), Margin = new Thickness(16, 8, 16, 4), Padding = new Thickness(0), Cursor = Cursors.Hand };
        var frame = new Grid();
        r.Child = frame;
        frame.Children.Add(HomeViews.Dashes(Theme.P.Track));
        r.Cells.Margin = new Thickness(0, 0, 14, 0);
        frame.Children.Add(r.Cells);
        var plus = Theme.Glyph(G.Add, 11, Theme.P.AccentText);
        plus.Margin = new Thickness(0, 1, 10, 0);
        r.Add(plus, GridLength.Auto);
        r.Add(Theme.T(item.Title, 13.5, Theme.P.AccentText, Theme.Medium), GridLength.Auto);
        var sub = Theme.T(item.Subtitle ?? "", 12, Theme.P.TextFaint);
        sub.Margin = new Thickness(10, 1, 8, 0);
        r.Add(sub, new GridLength(1, GridUnitType.Star));
        r.Add(Theme.Glyph(open ? G.Up : G.Down, 9, Theme.P.TextFaint), GridLength.Auto);
        return r;
    }

    /// A hidden block's card under "+ 블록 추가": its glyph, its name and what it shows, a + at the end; outlined, filled
    /// when hovered or selected. Pressed it adds its block, dragged it goes where it is let go (Launcher.HomeDrag.cs).
    public static Row Card(ResultItem item)
    {
        var r = new Row(item, 54) { CornerRadius = new CornerRadius(10), BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), Margin = new Thickness(16, 3, 16, 3), Cursor = Cursors.Hand };
        var icon = Icons.Build(item.Icon);
        icon.Margin = new Thickness(0, 0, 12, 0);
        r.Add(icon, GridLength.Auto);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Theme.T(item.Title, 13.5, Theme.P.Text, Theme.Medium));
        if (item.Subtitle is { Length: > 0 } subtitle) // a command with no words under its name: the name alone, centred
        {
            var about = Theme.T(subtitle, 12, Theme.P.TextFaint);
            about.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(about);
        }
        r.Add(words, new GridLength(1, GridUnitType.Star));
        var plus = Theme.Glyph(G.Add, 11, Theme.P.TextDim);
        plus.Margin = new Thickness(10, 0, 0, 0);
        r.Add(plus, GridLength.Auto);
        return r;
    }

    /// The calculator answer: the expression on the left, the result large on the right.
    public static Row Calc(ResultItem item)
    {
        var r = new Row(item, 96) { CornerRadius = new CornerRadius(12), Margin = new Thickness(8, 4, 8, 6), Padding = new Thickness(0) };
        r.Cells.Children.Remove(r.Bar);
        r.Cells.ColumnDefinitions.Clear();
        var card = new Grid();
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
        var expr = Theme.T(item.CalcExpr ?? "", 17, Theme.P.TextDim);
        left.Children.Add(expr);
        var cap1 = Theme.T(item.CalcCaption ?? Tr("수식", "Expression"), 11.5, Theme.P.TextFaint);
        cap1.Margin = new Thickness(0, 6, 0, 0);
        left.Children.Add(cap1);
        card.Children.Add(left);
        var arrow = Theme.Glyph('', 14, Theme.P.TextFaint);
        Grid.SetColumn(arrow, 1);
        card.Children.Add(arrow);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        var val = Theme.T(item.CalcValue ?? "", item.CalcValue is { Length: > 18 } ? 21 : 28, Theme.P.Text, Theme.Medium);
        Typography.SetNumeralAlignment(val, FontNumeralAlignment.Tabular);
        right.Children.Add(val);
        var cap2 = Theme.T(item.CalcDetail ?? Tr("결과 · Enter로 복사", "Result · Enter copies"), 11.5, Theme.P.TextFaint);
        cap2.Margin = new Thickness(0, 4, 0, 0);
        right.Children.Add(cap2);
        Grid.SetColumn(right, 2);
        card.Children.Add(right);
        var frame = new SmoothBorder { Background = Theme.P.Card, BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = card };
        r.Child = frame;
        r.CalcFrame = frame;
        return r;
    }

    public Border? CalcFrame;
}

internal static class Section2
{
    /// The mark on a header's title when it folds its block (`fold`): 홈 편집's handle tells a press there (a click folds)
    /// from one beside it (Launcher.HomeDrag.cs).
    public static readonly object FoldTag = new();

    /// A list header: the title, a small note on the right and, when given, a ↻ after the note. `fold` (a home block):
    /// a chevron left of the title, and a click on either folds the block or (`folded`) opens it again.
    public static FrameworkElement Header(string title, string? note, Action? reload = null, Action? fold = null, bool folded = false)
    {
        var g = new Grid { Margin = new Thickness(22, 12, 22, 5) };
        var name = Theme.T(title, 11.5, Theme.P.TextFaint, Theme.Semi);
        if (fold is null) g.Children.Add(name);
        else
        {
            // the chevron in the margin: the title stays where it is on any home
            var chevron = Theme.Glyph(folded ? G.Right : G.Down, 8.5, Theme.P.TextFaint);
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Margin = new Thickness(0, 1, 5, 0);
            var toggle = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-14, 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = folded ? Tr("펼치기", "Expand") : Tr("접기", "Collapse"), Tag = FoldTag };
            toggle.Children.Add(chevron);
            toggle.Children.Add(name);
            toggle.MouseEnter += (_, _) => chevron.Foreground = name.Foreground = Theme.P.Text;
            toggle.MouseLeave += (_, _) => chevron.Foreground = name.Foreground = Theme.P.TextFaint;
            toggle.MouseLeftButtonDown += (_, e) => e.Handled = true; // the search line keeps the focus
            toggle.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                fold();
            };
            g.Children.Add(toggle);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (note is not null)
        {
            var n = Theme.T(note, 11.5, Theme.P.TextFaint);
            n.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(n);
        }
        if (reload is not null)
        {
            var icon = Theme.Glyph(G.Refresh, 11, Theme.P.TextFaint);
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(7, 2, 0, 2), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = Tr("새로고침", "Refresh"), Child = icon };
            b.MouseEnter += (_, _) => icon.Foreground = Theme.P.Text;
            b.MouseLeave += (_, _) => icon.Foreground = Theme.P.TextFaint;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true; // the search line keeps the focus
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                reload();
            };
            right.Children.Add(b);
        }
        g.Children.Add(right);
        return g;
    }
}
