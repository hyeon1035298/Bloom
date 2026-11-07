using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Bloom.Search;

namespace Bloom.Ui;

/// A short menu of actions in a popup, drawn like a Dropdown's list (the AI chat window's ⋯ and right-click menus): at
/// the mouse or under an element. ↑↓ move, Enter runs, Esc or a click outside closes; the window passes its keys to
/// HandleKey while it is open. An item with an empty title is a dividing line.
internal sealed class PopupMenu
{
    private readonly Popup popup;
    private readonly SmoothBorder card;
    private readonly StackPanel list = new();
    private readonly List<(Row row, ActionItem item)> rows = new();
    private int sel = -1;

    public bool IsOpen => popup.IsOpen;

    public PopupMenu()
    {
        card = new SmoothBorder
        {
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0, 6, 0, 6),
            MinWidth = 220,
            Margin = new Thickness(14, 4, 14, 20), // room for the shadow inside the popup
            Child = list,
        };
        card.LiftMenu();
        card.LayoutTransform = Theme.Zoom; // a popup is a window of its own: Bloom's scale reaches it this way
        popup = new Popup { Child = card, AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.None };
    }

    /// `under`: below that element, its right edges meeting; null: at the mouse.
    public void Open(IReadOnlyList<ActionItem> items, FrameworkElement? under = null)
    {
        list.Children.Clear();
        rows.Clear();
        foreach (var item in items)
        {
            if (item.Title.Length == 0)
            {
                list.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Line, Margin = new Thickness(14, 5, 14, 5) });
                continue;
            }
            var r = new Row(null, 34) { Margin = new Thickness(6, 0, 6, 0) };
            var glyph = Theme.Glyph(item.Glyph, 13, item.Danger ? Theme.P.Danger : Theme.P.TextDim);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 11, 0);
            r.Add(glyph, GridLength.Auto);
            var t = Theme.T(item.Title, 13, item.Danger ? Theme.P.Danger : Theme.P.Text, Theme.Medium);
            t.VerticalAlignment = VerticalAlignment.Center;
            r.Add(t, new GridLength(1, GridUnitType.Star));
            var run = item;
            r.Clicked = () => Run(run);
            rows.Add((r, item));
            list.Children.Add(r);
        }
        sel = -1;
        Paint();
        popup.PlacementTarget = under;
        popup.Placement = under is null ? PlacementMode.MousePoint : PlacementMode.Bottom;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popup.HorizontalOffset = under is null ? -card.Margin.Left : under.ActualWidth - card.DesiredSize.Width + card.Margin.Right;
        popup.VerticalOffset = under is null ? -card.Margin.Top : 0;
        popup.IsOpen = true;
    }

    public void Close() => popup.IsOpen = false;

    private void Run(ActionItem item)
    {
        Close();
        item.Run();
    }

    private void Paint()
    {
        for (int i = 0; i < rows.Count; i++) rows[i].row.Selected = i == sel;
    }

    /// Keys while the menu is open; everything else waits until it is closed.
    public bool HandleKey(Key key)
    {
        if (!IsOpen || rows.Count == 0) return false;
        switch (key)
        {
            case Key.Down:
                sel = (sel + 1) % rows.Count;
                Paint();
                break;
            case Key.Up:
                sel = sel <= 0 ? rows.Count - 1 : sel - 1;
                Paint();
                break;
            case Key.Enter when sel >= 0:
                Run(rows[sel].item);
                break;
            case Key.Escape:
                Close();
                break;
        }
        return true;
    }

    /// QA: the open menu as a PNG.
    public void Capture(string file) => Shots.Save(card, file);
}
