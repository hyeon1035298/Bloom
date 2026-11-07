using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Bloom.Ui;

/// A list row dragged up or down among the rows of its panel whose items can move (ResultItem.MoveTo: the 상용구 list).
/// The row follows the mouse, an accent line marks where it lands, the list scrolls while the mouse is near its top or
/// bottom edge, and the release hands the row it lands next to over to MoveTo. A press that moves less than a few
/// pixels stays a click.
internal static class RowReorder
{
    // how far a press moves before it is a drag (as the favorites strip); the band at the list's edges that scrolls it,
    // and how far per tick
    private const double Threshold = 6, EdgeBand = 28, ScrollStep = 14;

    private static Drag? drag;

    /// `moved`: after a drop that changed the order, with the moved item's id (the list is drawn again around it).
    public static void Attach(Row row, Action<string> moved)
    {
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            drag?.End();
            if (row.Item?.MoveTo is null || VisualTreeHelper.GetParent(row) is not Panel panel) return;
            drag = new Drag(row, panel, e.GetPosition(panel), moved, true);
        };
        row.PreviewMouseMove += (_, e) =>
        {
            if (drag?.Row != row) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                drag.End();
                return;
            }
            drag.Move(e.GetPosition(drag.Panel));
        };
        row.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (drag?.Row != row) return;
            var d = drag;
            if (d.Started) e.Handled = true; // the row's click (paste) stays out of a drag
            d.Drop();
        };
        row.LostMouseCapture += (_, _) =>
        {
            if (drag?.Row == row && drag.Started) drag.End();
        };
    }

    /// QA: `row` pressed at its middle and moved `dy` down (up when negative), the line drawn; with `drop`, released
    /// there as the mouse would (→ the item's MoveTo and `moved`).
    public static void DragForQa(Row row, double dy, bool drop, Action<string> moved)
    {
        if (drag?.Row != row)
        {
            drag?.End();
            if (VisualTreeHelper.GetParent(row) is not Panel panel) return;
            drag = new Drag(row, panel, row.TranslatePoint(new Point(row.ActualWidth / 2, row.ActualHeight / 2), panel), moved, false);
        }
        var d = drag;
        d.Move(new Point(d.Press.X, d.Press.Y + dy));
        if (drop) d.Drop();
    }

    private sealed class Drag
    {
        public readonly Row Row;
        public readonly Panel Panel;
        public bool Started;
        public readonly Point Press;
        private readonly Action<string> moved;
        private readonly bool mouse; // a real one (QA's drag has none: the pointer is the user's, elsewhere)
        private List<Row> peers = new();
        private int from = -1, to = -1;
        private DropLine? line;
        private ScrollViewer? scroll;
        private DispatcherTimer? edge;

        public Drag(Row row, Panel panel, Point press, Action<string> moved, bool mouse)
        {
            this.mouse = mouse;
            Row = row;
            Panel = panel;
            Press = press;
            this.moved = moved;
        }

        /// `p`: the mouse in the panel.
        public void Move(Point p)
        {
            if (!Started)
            {
                if (Math.Abs(p.Y - Press.Y) < Threshold && Math.Abs(p.X - Press.X) < Threshold) return;
                peers = Panel.Children.OfType<Row>().Where(r => r.Item?.MoveTo is not null && r.IsVisible).ToList();
                from = peers.IndexOf(Row);
                if (from < 0 || peers.Count < 2)
                {
                    End();
                    return;
                }
                Started = true;
                Row.Lifted = true;
                Panel.SetZIndex(Row, 1);
                Row.RenderTransform = new TranslateTransform();
                if (AdornerLayer.GetAdornerLayer(Panel) is { } layer)
                {
                    line = new DropLine(Panel);
                    layer.Add(line);
                }
                if (mouse)
                {
                    scroll = Ancestor<ScrollViewer>(Panel);
                    edge = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => Edge(), Row.Dispatcher);
                    edge.Start();
                    // the moves and the release past the row's own edges; last, as taking the mouse sends a move at
                    // once (into this method again, which may end the drag)
                    Row.CaptureMouse();
                    if (!Started) return;
                }
            }
            if (Row.RenderTransform is TranslateTransform shift) shift.Y = p.Y - Press.Y;
            // in front of the first row whose middle is below the mouse (the dragged one counts where it was)
            to = peers.Count;
            for (int i = 0; i < peers.Count; i++)
            {
                if (p.Y < Top(peers[i]) + peers[i].ActualHeight / 2)
                {
                    to = i;
                    break;
                }
            }
            bool still = to == from || to == from + 1;
            line?.Show(still ? null : to < peers.Count ? Top(peers[to]) - 1 : Top(peers[^1]) + peers[^1].ActualHeight + 1);
        }

        /// The row's top in the panel as laid out, without the drag's shift.
        private double Top(Row r) => r.TranslatePoint(new Point(0, -r.RenderTransform.Value.OffsetY), Panel).Y;

        /// Near the list's top or bottom edge the list scrolls on, the row and the line with it.
        private void Edge()
        {
            if (scroll is null || !Started) return;
            double y = Mouse.GetPosition(scroll).Y;
            double by = y < EdgeBand ? -ScrollStep : y > scroll.ViewportHeight - EdgeBand ? ScrollStep : 0;
            if (by == 0) return;
            double before = scroll.VerticalOffset;
            scroll.ScrollToVerticalOffset(before + by);
            scroll.UpdateLayout();
            if (scroll.VerticalOffset != before) Move(Mouse.GetPosition(Panel));
        }

        public void Drop()
        {
            int a = from, b = to;
            bool started = Started;
            var list = peers;
            End();
            if (!started || b < 0 || b == a || b == a + 1 || Row.Item is not { MoveTo: { } move } item) return;
            var (target, after) = b < list.Count ? (list[b], false) : (list[^1], true);
            if (target.Item is not { } t) return;
            move(t.Id, after);
            moved(item.Id);
        }

        public void End()
        {
            if (drag == this) drag = null;
            edge?.Stop();
            edge = null;
            if (line is not null) AdornerLayer.GetAdornerLayer(Panel)?.Remove(line);
            line = null;
            if (Started)
            {
                Started = false;
                Row.RenderTransform = Transform.Identity;
                Row.Lifted = false;
                Panel.SetZIndex(Row, 0);
                if (Row.IsMouseCaptured) Row.ReleaseMouseCapture();
            }
        }

        private static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
                if (p is T t) return t;
            return null;
        }
    }

    /// Where the dragged row lands: an accent line across the panel, inset as the rows are.
    private sealed class DropLine : Adorner
    {
        private double? y;

        public DropLine(UIElement panel) : base(panel) => IsHitTestVisible = false;

        public void Show(double? at)
        {
            if (y == at) return;
            y = at;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (y is not { } at) return;
            dc.DrawRoundedRectangle(Theme.P.Accent, null, new Rect(12, at - 1, Math.Max(0, AdornedElement.RenderSize.Width - 24), 2), 1, 1);
        }
    }
}
