using System;
using System.Windows;
using System.Windows.Controls;

namespace Bloom.Ui;

/// A name and the words after it on one line (TokenViews' rows: a session's title, then where it ran, its folder and
/// effort). With room both are whole, the tail right after the name; short of room the tail gives way first, down to
/// what leaves the name TailGives of the line, then the name is cut too. Both are cut with an ellipsis (Theme.T's).
internal sealed class NameTail : Panel
{
    /// The share of a short line the name keeps before the tail stops giving way.
    public const double TailGives = 0.6;

    private readonly TextBlock name, tail;

    public NameTail(TextBlock name, TextBlock tail)
    {
        this.name = name;
        this.tail = tail;
        Children.Add(name);
        Children.Add(tail);
    }

    /// The name's and the tail's widths on a line `width` wide, from what each wants.
    public static (double name, double tail) Widths(double width, double name, double tail)
    {
        if (!double.IsFinite(width) || name + tail <= width) return (name, tail);
        double kept = Math.Min(name, Math.Max(width - tail, width * TailGives));
        return (kept, Math.Max(0, width - kept));
    }

    protected override Size MeasureOverride(Size available)
    {
        var free = new Size(double.PositiveInfinity, available.Height);
        name.Measure(free);
        tail.Measure(free);
        var (n, t) = Widths(available.Width, name.DesiredSize.Width, tail.DesiredSize.Width);
        name.Measure(new Size(n, available.Height));
        tail.Measure(new Size(t, available.Height));
        return new Size(n + t, Math.Max(name.DesiredSize.Height, tail.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (n, t) = Widths(final.Width, name.DesiredSize.Width, tail.DesiredSize.Width);
        name.Arrange(new Rect(0, 0, n, final.Height));
        tail.Arrange(new Rect(n, 0, t, final.Height));
        return final;
    }
}
