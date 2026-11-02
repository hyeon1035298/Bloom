using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// The thin handle between the home's two columns (HomeColumns lays it over the gap): pointed at, a ↔ cursor and a
/// short line; dragged, it tells where it is (Moved, in its panel's coordinates) and when it is let go (Done); a double
/// click puts the columns back as they were (Reset). A press that does not move past the drag distance (a bare click, the
/// first click of a double click) tells neither. The search line keeps the focus: a press here goes no further.
internal sealed class HomeSplitter : FrameworkElement
{
    /// How wide it is: within the blocks' own side margins (a row's 8 on each side), so it takes no row's room.
    public const double Thick = 12;

    public event Action<double>? Moved;

    public event Action? Done;

    public event Action? Reset;

    private bool dragging;
    private double grab; // where in it the press was
    private Point pressed; // … in its panel
    private bool moved; // the drag went past the system's drag distance
    private bool lit; // QA: drawn as pointed at

    public HomeSplitter()
    {
        Cursor = Cursors.SizeWE;
        ToolTip = Tr("끌어서 두 열의 너비 조절 · 두 번 눌러 처음대로", "Drag to resize the columns · double-click to reset");
        ToolTipService.SetInitialShowDelay(this, 900);
    }

    /// Being dragged now.
    public bool Dragging => dragging;

    /// QA: its line drawn as the mouse over it shows it.
    public bool LitForQa
    {
        get => lit;
        set
        {
            lit = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(RenderSize);
        dc.DrawRectangle(Brushes.Transparent, null, box); // the whole strip takes the mouse
        if (!dragging && !IsMouseOver && !lit) return;
        double h = Math.Max(0, box.Height - 16);
        if (h <= 0) return;
        dc.DrawRoundedRectangle(dragging ? Theme.P.Accent : Theme.P.TextFaint, null, new Rect(box.Width / 2 - 1, 8, 2, h), 1, 1);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true; // the search line keeps the focus, and nothing under it takes the press
        if (e.ClickCount == 2)
        {
            Reset?.Invoke();
            return;
        }
        if (!CaptureMouse()) return;
        grab = e.GetPosition(this).X - ActualWidth / 2;
        pressed = VisualTreeHelper.GetParent(this) is UIElement panel ? e.GetPosition(panel) : default;
        moved = false;
        dragging = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || VisualTreeHelper.GetParent(this) is not UIElement panel) return;
        var at = e.GetPosition(panel);
        if (!moved && Math.Abs(at.X - pressed.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        moved = true;
        Moved?.Invoke(at.X - grab);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging) return;
        e.Handled = true;
        ReleaseMouseCapture(); // Done comes from losing it
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!dragging) return;
        dragging = false;
        InvalidateVisual();
        if (moved) Done?.Invoke();
    }
}
