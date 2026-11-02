using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bloom.Ui;

/// The home's blocks: one column, or two side by side once the list is TwoColumnWidth wide (text keeps its size, so a
/// zoomed launcher needs more room for them). A block's column is Right; the blocks come in with the left column's
/// first (Core\Home.cs), so in one column they stack in that order. A Foot (홈 편집's "+ 블록 추가") ends its column.
/// While 홈 편집 drags, the block held is out of its column (Lift: drawn where it was picked up, its RenderTransform
/// takes it along) and a gap opens where it would land (OpenGap), drawn as a dashed slot.
/// Two columns share the width by Split (the left one's share; half by default), each at least ColumnMin wide. A handle
/// over the gap between them (HomeSplitter) drags it live and a double click puts it back at half; either way SplitSet
/// tells the share to keep. It is the panel's own visual, not one of its Children (the blocks).
internal sealed class HomeColumns : Panel
{
    /// The list's width (dip, after Bloom's zoom) from which the home has two columns: the default launcher (760) has
    /// one, one about 1000 wide two.
    public const double TwoColumnWidth = 960;

    public static readonly DependencyProperty RightProperty = DependencyProperty.RegisterAttached("Right", typeof(bool), typeof(HomeColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetRight(UIElement e) => (bool)e.GetValue(RightProperty);

    public static void SetRight(UIElement e, bool right) => e.SetValue(RightProperty, right);

    public static readonly DependencyProperty FootProperty = DependencyProperty.RegisterAttached("Foot", typeof(bool), typeof(HomeColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetFoot(UIElement e) => (bool)e.GetValue(FootProperty);

    public static void SetFoot(UIElement e, bool foot) => e.SetValue(FootProperty, foot);

    public static bool Wide(double width) => width >= TwoColumnWidth;

    /// The left column's share of the width as it has always been: half.
    public const double DefaultSplit = 0.5;

    /// The narrowest a column gets (dip): a row's icon, title and accessory, a widget's cards, the edit tools' pill.
    public const double ColumnMin = 360;

    /// A kept share as one to lay out by: between 0 and 1 (not at either end), else the default (a settings file from
    /// before the handle has none, or one edited by hand).
    public static double Share(double split) => double.IsFinite(split) && split > 0 && split < 1 ? split : DefaultSplit;

    /// The left column's width in a list `width` wide: its share, kept so each column is at least ColumnMin wide (a list
    /// too narrow for two of them: halves).
    public static double LeftWidth(double width, double split) =>
        width < 2 * ColumnMin ? width / 2 : Math.Clamp(width * Share(split), ColumnMin, width - ColumnMin);

    /// The share the handle gives put at `x` in a list `width` wide, kept as LeftWidth keeps it.
    public static double SplitAt(double x, double width) =>
        width < 2 * ColumnMin || !double.IsFinite(x) ? DefaultSplit : Math.Clamp(x, ColumnMin, width - ColumnMin) / width;

    private double split = DefaultSplit;

    /// The left column's share when there are two (Share keeps it sane).
    public double Split
    {
        get => split;
        set
        {
            value = Share(value);
            if (value == split) return;
            split = value;
            InvalidateMeasure();
        }
    }

    /// The handle being dragged (the share not kept yet).
    public bool Splitting => splitter.Dragging;

    /// The handle let go or double-clicked: the share to keep.
    public event Action<double>? SplitSet;

    private readonly HomeSplitter splitter = new();

    public HomeColumns()
    {
        AddVisualChild(splitter);
        splitter.Moved += x => Split = SplitAt(x, ActualWidth);
        splitter.Done += () => SplitSet?.Invoke(split);
        splitter.Reset += ResetSplit;
    }

    /// Back at half (the handle double-clicked), told to be kept.
    public void ResetSplit()
    {
        Split = DefaultSplit;
        SplitSet?.Invoke(split);
    }

    /// The blocks, then the handle over them.
    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) => index == base.VisualChildrenCount ? splitter : base.GetVisualChild(index);

    /// QA: the handle between the columns.
    public HomeSplitter SplitterForQa => splitter;

    /// QA: a column's width as laid out (two columns: the right one's or the left one's).
    public double ColumnWidthForQa(bool right) => ColumnBox(right ? 1 : 0, ActualWidth, Two).width;

    /// Two columns as last laid out; Flipped when that changes (the edit tools' ▲ ▼ go by it).
    public bool Two { get; private set; }

    public event Action? Flipped;

    private UIElement? held; // the block 홈 편집 holds: out of its column
    private Point heldAt; // … drawn here (its place when it was picked up)
    private (bool right, UIElement? below, double height)? gap; // the room where it would land: before `below`, else at its column's end
    private Rect gapBox; // … as laid out

    /// A block picked up (its column closes up behind it) or, null, the one held put back; either way no gap.
    public void Lift(UIElement? block)
    {
        held = block;
        if (block is not null) heldAt = (Point)VisualTreeHelper.GetOffset(block);
        gap = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// A gap `height` tall before `below`, or (null) at the end of its column (`right`, when there are two); 0: none.
    public void OpenGap(bool right, UIElement? below, double height)
    {
        (bool, UIElement?, double)? to = height > 0 ? (right, below, height) : null;
        if (gap.Equals(to)) return;
        gap = to;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// The gap as last laid out (Rect.Empty: none).
    public Rect GapBox => gap is null ? Rect.Empty : gapBox;

    private static int Column(UIElement c, bool two) => two && GetRight(c) ? 1 : 0;

    /// Where each column starts and how wide it is, in a list `width` wide.
    private (double x, double width) ColumnBox(int col, double width, bool two)
    {
        if (!two) return (0, width);
        double left = LeftWidth(width, split);
        return col == 0 ? (0, left) : (left, width - left);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool two = !double.IsInfinity(availableSize.Width) && Wide(availableSize.Width);
        if (two != Two)
        {
            Two = two;
            Flipped?.Invoke();
        }
        double widest = 0;
        var tall = new double[2];
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(ColumnBox(Column(c, two), availableSize.Width, two).width, double.PositiveInfinity));
            widest = Math.Max(widest, c.DesiredSize.Width);
            if (c != held) tall[Column(c, two)] += c.DesiredSize.Height;
        }
        if (gap is { } g) tall[two && g.right ? 1 : 0] += g.height;
        splitter.Measure(new Size(HomeSplitter.Thick, double.PositiveInfinity));
        return new Size(double.IsInfinity(availableSize.Width) ? widest : availableSize.Width, Math.Max(tall[0], tall[1]));
    }

    /// Each column's blocks in order, the gap among them, then its foot; the held block where it was picked up; the
    /// handle over the gap between two columns (none in one).
    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = new double[2];
        void Open(int col)
        {
            var (x, width) = ColumnBox(col, finalSize.Width, Two);
            gapBox = new Rect(x, y[col], width, gap!.Value.height);
            y[col] += gap.Value.height;
        }
        void Place(UIElement c)
        {
            int col = Column(c, Two);
            var (x, width) = ColumnBox(col, finalSize.Width, Two);
            c.Arrange(new Rect(x, y[col], width, c.DesiredSize.Height));
            y[col] += c.DesiredSize.Height;
        }
        foreach (UIElement c in InternalChildren)
        {
            if (c == held || GetFoot(c)) continue;
            if (gap is { } g && g.below == c) Open(Column(c, Two));
            Place(c);
        }
        if (gap is { } end && end.below is null) Open(Two && end.right ? 1 : 0);
        foreach (UIElement c in InternalChildren)
            if (c != held && GetFoot(c)) Place(c);
        if (held is not null) held.Arrange(new Rect(heldAt.X, heldAt.Y, ColumnBox(Column(held, Two), finalSize.Width, Two).width, held.DesiredSize.Height));
        double between = ColumnBox(1, finalSize.Width, Two).x;
        splitter.Arrange(Two ? new Rect(between - HomeSplitter.Thick / 2, 0, HomeSplitter.Thick, finalSize.Height) : new Rect(0, 0, 0, 0));
        return finalSize;
    }

    /// The gap as a dashed slot in the accent, under the blocks (the held one floats over it).
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (gap is null || gapBox.Width < 40 || gapBox.Height < 16) return;
        var line = new Pen(Theme.P.Accent, 1.5) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
        dc.DrawRoundedRectangle(Theme.P.AccentSoft, line, new Rect(gapBox.X + 12, gapBox.Y + 4, gapBox.Width - 24, gapBox.Height - 8), 10, 10);
    }

    /// Where a block (or a card) let go at `p` would land, in the columns as they would be without the held block and the
    /// gap (so the gap opening does not move it on): its column when there are two (the one under `p`; null in one), and
    /// the blocks just above and below the gap nearest `p` there.
    public (bool? right, UIElement? above, UIElement? below) DropAt(Point p)
    {
        bool right = Two && p.X >= ColumnBox(1, ActualWidth, true).x;
        double y = 0;
        UIElement? above = null;
        foreach (UIElement c in InternalChildren)
        {
            if (c == held || GetFoot(c) || c.Visibility != Visibility.Visible || (Two && GetRight(c) != right)) continue;
            double h = c.DesiredSize.Height;
            if (p.Y < y + h / 2) return (Two ? right : null, above, c);
            above = c;
            y += h;
        }
        return (Two ? right : null, above, null);
    }
}
