using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

/// The home's blocks in their columns (HomeColumns), and 홈 편집's drag: a block picked up by its header row (≡ and
/// title included) or a hidden block's card from "+ 블록 추가" (Launcher.HomeAdd.cs), put down in a gap of either
/// column. Once the mouse has moved a little it lifts — a block out of its column onto a raised plate, a card alike —
/// and follows the mouse, and a gap opens where it would land: in two columns in the column under the mouse, in one
/// the gap's neighbours choose its column (Home.Drop). The list scrolls near its ends; Esc puts it back. A press let go
/// where it was is a click: on a block's title it folds the block, on a card it adds the card's block.
/// Data that changes meanwhile refreshes the list once the drag is over. Drawn anew under the mouse, the held block lost
/// the mouse, the drag ended, and the button let go then ran the row under it (seen with real input:
/// App\Qa\Captures.HomeInput.cs); a hold that loses the mouse anyway (Esc twice: 홈 편집 ends) leaves that release to
/// nothing.
internal sealed partial class Launcher
{
    private readonly HomeColumns homeGrid = new();

    /// What a press on a block's header or on a card holds. `Source` has the mouse; `Block`: the block (null: a card,
    /// its block still off).
    private sealed class Hold
    {
        public required FrameworkElement Source;
        public required string Key;
        public FrameworkElement? Block;
        public Point From, At; // pressed here, last held over (the columns' coordinates)
        public Point Grab; // … this far into what it lifts
        public Action? Click; // let go where it was pressed
        public bool Moved, Lifted, Cancelled;
        public double Gap; // the gap it opens: as tall as the lifted block shows (LiftMax at most), a card's NewBlock
        public UIElement? Plate, Raised; // lifted: the block's plate; the card's foot drawn over the blocks
    }

    private Hold? hold;
    private bool refreshAfterHold; // data changed while something was held: the list follows once it is let go
    private bool swallowUp; // a hold lost the mouse with the button down: that button's release clicks nothing

    /// How much of a lifted block shows (a long list lifts its top, fading out); the gap a card opens.
    private const double LiftMax = 168, NewBlock = 64;

    /// A home whose columns come or go while it is edited gets its tools anew, looked at as that comes (a drag on by
    /// then, begun after the flip too: once it is let go, its drop taken against the columns as they are then).
    /// The release of a hold that lost the mouse (EndHold) goes to nothing, wherever it comes; a new press ends that.
    /// The columns' share is kept once the handle between them is let go or double-clicked (read as the home is drawn).
    private void WireHomeGrid()
    {
        homeGrid.SplitSet += share =>
        {
            Settings.Current.HomeSplit = share;
            Settings.Save();
        };
        homeGrid.Flipped += () => Dispatcher.BeginInvoke(() =>
        {
            if (!HomeEditing || Mode != Mode.Root || box.Text.Length > 0) return;
            if (hold is not null) refreshAfterHold = true;
            else Refresh(true);
        }, DispatcherPriority.Background);
        PreviewMouseLeftButtonDown += (_, _) => swallowUp = false;
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!swallowUp) return;
            swallowUp = false;
            e.Handled = true;
        };
    }

    /// 홈 편집: a block's header row as its handle (its tools sit on it). Pressed anywhere on it, ≡ included, and moved,
    /// the block is picked up; let go where it was, a press on the title folds it (`fold`). The tools' buttons and the
    /// header's ↻ keep their clicks.
    private FrameworkElement HeaderHandle(FrameworkElement header, Grid block, FrameworkElement tools, Action? fold)
    {
        var row = new Border { Background = Brushes.Transparent, Padding = header.Margin, Cursor = Cursors.SizeAll, Child = header };
        header.Margin = new Thickness(0);
        block.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var on = e.OriginalSource as DependencyObject;
            if (!Up(on).Any(d => d == row || d == tools)) return; // below the header: its rows, a widget's own buttons
            bool title = Up(on).TakeWhile(d => d != block).Any(d => d is FrameworkElement { Tag: var t } && ReferenceEquals(t, Section2.FoldTag));
            if (!title && OnButton(on, block)) return; // ▲ ▼ ‹ › ✕ − + ↻ click as they do
            e.Handled = true; // the search line keeps the focus; the title waits to see a click or a drag
            Press(block, (string)block.Tag, block, e, title ? fold : null);
        };
        WireHold(block);
        return row;
    }

    /// A hidden block's card under "+ 블록 추가" (Row.Card): pressed and moved, it is held over the columns; let go
    /// where it was, the row's own click adds its block at its column's end.
    private void WireCard(Row card, string key)
    {
        card.PreviewMouseLeftButtonDown += (_, e) => Press(card, key, null, e, null);
        WireHold(card);
    }

    private void Press(FrameworkElement source, string key, FrameworkElement? block, MouseButtonEventArgs e, Action? click)
    {
        if (!source.CaptureMouse()) return;
        var from = e.GetPosition(homeGrid);
        hold = new Hold { Source = source, Key = key, Block = block, From = from, At = from, Grab = e.GetPosition(block ?? source), Click = click };
    }

    /// The mouse on what was pressed (it has the mouse till let go): past the system's drag distance the press is a
    /// drag; let go, a drag puts it down (Put), a press is a click.
    private void WireHold(FrameworkElement source)
    {
        source.MouseMove += (_, e) =>
        {
            if (hold is not { Cancelled: false } h || h.Source != source) return;
            var p = e.GetPosition(homeGrid);
            if (!h.Moved)
            {
                if (Math.Abs(p.X - h.From.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - h.From.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                Lift(h);
            }
            HoldOver(h, p);
        };
        source.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (hold is not { } h || h.Source != source) return;
            // where it is let go, against the columns as they are now (they may have come or gone since the last move)
            var to = h.Moved && !h.Cancelled ? DropTarget(e.GetPosition(homeGrid)) : default;
            EndHold(); // before the mouse goes: let go, it is not lost
            source.ReleaseMouseCapture();
            if (h.Moved || h.Cancelled) e.Handled = true; // a drag, or one Esc took back, is no click
            if (h.Cancelled) return;
            if (h.Moved) Put(h, to);
            else if (h.Click is { } click)
            {
                e.Handled = true;
                click();
            }
        };
        // lost with the button still down (the list drawn anew under it: Esc twice ends 홈 편집): its release, when it
        // comes, clicks nothing — not the row it lands on
        source.LostMouseCapture += (_, _) =>
        {
            if (hold?.Source != source) return;
            swallowUp = true;
            EndHold();
        };
    }

    /// The press became a drag: what it holds lifts — a block out of its column onto a raised plate, a card over the
    /// blocks — and the gap it opens is as tall as it shows.
    private void Lift(Hold h)
    {
        h.Moved = h.Lifted = true;
        var lifted = h.Block ?? h.Source;
        if (h.Block is Grid block)
        {
            h.Gap = Math.Min(block.ActualHeight, LiftMax);
            h.Plate = new SmoothBorder { Background = Theme.P.Raised, BorderBrush = Theme.P.PopupLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 0, 8, 0), Height = h.Gap, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            block.Children.Insert(0, h.Plate);
            // a long list lifts its top: the rest fades out
            if (block.ActualHeight > LiftMax)
                block.OpacityMask = new LinearGradientBrush(Colors.Black, Colors.Transparent, new Point(0, LiftMax - 40), new Point(0, LiftMax)) { MappingMode = BrushMappingMode.Absolute };
            homeGrid.Lift(block);
        }
        else
        {
            h.Gap = NewBlock;
            h.Raised = Up(h.Source).OfType<UIElement>().First(d => VisualTreeHelper.GetParent(d) == homeGrid);
            Panel.SetZIndex(h.Raised, 1);
            ((Row)h.Source).Background = Theme.P.Raised;
        }
        lifted.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.35, Color = Colors.Black };
        lifted.Opacity = 0.96;
        lifted.RenderTransform = new TranslateTransform();
        Panel.SetZIndex(lifted, 1);
    }

    /// What is held over `p` (the columns' coordinates): the gap where it would land, it under the mouse where it was
    /// taken; near the list's top or bottom edge the list scrolls on.
    private void HoldOver(Hold h, Point p)
    {
        h.At = p;
        double y = homeGrid.TranslatePoint(p, scroll).Y;
        if (y < 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 16);
        else if (y > scroll.ActualHeight - 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 16);
        var (right, _, below) = homeGrid.DropAt(p);
        homeGrid.OpenGap(right is true, below, h.Gap);
        // laid out first: a card's foot moves down as the gap opens above it; in one column it goes up and down only
        homeGrid.UpdateLayout();
        var lifted = h.Block ?? h.Source;
        var move = (TranslateTransform)lifted.RenderTransform;
        var at = lifted.TranslatePoint(new Point(-move.X, -move.Y), homeGrid);
        move.X = homeGrid.Two ? p.X - h.Grab.X - at.X : 0;
        move.Y = p.Y - h.Grab.Y - at.Y;
    }

    /// Where what is held would land let go at `p`, in the columns as laid out now: the blocks around the gap and its
    /// column (HomeColumns.DropAt).
    private (string? above, string? below, bool? right) DropTarget(Point p)
    {
        var (right, above, below) = homeGrid.DropAt(p);
        return ((above as FrameworkElement)?.Tag as string, (below as FrameworkElement)?.Tag as string, right);
    }

    /// Let go after a drag: the block where the gap was, or the card's block on there.
    private void Put(Hold h, (string? above, string? below, bool? right) to)
    {
        if (h.Block is null)
        {
            AddBlock(h.Key, null, to);
            return;
        }
        Home.Drop(h.Key, to.above, to.below, to.right);
        Refresh(true);
    }

    /// Back as it was (no lift, no gap).
    private void Unlift(Hold h)
    {
        if (!h.Lifted) return;
        h.Lifted = false;
        var lifted = h.Block ?? h.Source;
        lifted.RenderTransform = Transform.Identity;
        lifted.Effect = null;
        lifted.Opacity = 1;
        Panel.SetZIndex(lifted, 0);
        if (h.Block is Grid block)
        {
            block.Children.Remove(h.Plate);
            block.OpacityMask = null;
            homeGrid.Lift(null);
            return;
        }
        homeGrid.OpenGap(false, null, 0);
        if (h.Raised is not null) Panel.SetZIndex(h.Raised, 0);
        var card = (Row)h.Source;
        card.Selected = card.Selected; // its own fill again
    }

    /// The mouse let go of what was held (or lost it); a refresh put off meanwhile comes now.
    private void EndHold()
    {
        if (hold is not { } h) return;
        hold = null;
        Unlift(h);
        if (!refreshAfterHold) return;
        refreshAfterHold = false;
        Dispatcher.BeginInvoke(() => DataChanged(), DispatcherPriority.Background);
    }

    /// Esc while something is held: it goes back, and the button let go does nothing.
    private bool CancelHold()
    {
        if (hold is not { Cancelled: false } h) return false;
        h.Cancelled = true;
        Unlift(h);
        return true;
    }

    /// QA: the home's columns (real input finds its blocks there).
    public HomeColumns HomeForQa => homeGrid;

    /// QA: home block `key` picked up by its header (`card`: the card of hidden block `key`, under an open "+ 블록 추가")
    /// and held over a point of the list as it shows (`fx`, `fy`: fractions of its width and height), as the mouse does it.
    public void HoldForQa(string key, bool card, double fx, double fy)
    {
        FrameworkElement? source = card ? rows.Find(r => r.Item?.Data is HomeAdd { Key: var k } && k == key)
            : homeGrid.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag as string == key);
        if (source is null) return;
        var grab = new Point(60, 14);
        hold = new Hold { Source = source, Key = key, Block = card ? null : source, From = source.TranslatePoint(grab, homeGrid), Grab = grab };
        Lift(hold);
        HoldOver(hold, scroll.TranslatePoint(new Point(scroll.ActualWidth * fx, scroll.ActualHeight * fy), homeGrid));
    }

    /// QA: what is held let go where it is.
    public void DropForQa()
    {
        if (hold is not { } h) return;
        var to = DropTarget(h.At);
        EndHold();
        Put(h, to);
    }
}
