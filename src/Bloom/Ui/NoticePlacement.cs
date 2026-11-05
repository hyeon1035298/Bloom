using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Bloom.Native;

namespace Bloom.Ui;

/// Where the notice window goes, as pure arithmetic in DIPs (no window, no screen). A place is a pair of fractions of
/// the room the work area leaves the window: x 0 is against the left edge, 1 against the right, 0.5 centred, and y the
/// same from top to bottom. So a place keeps its meaning when the screen, the card size or the number of cards
/// changes. The six named spots of 설정 > 알림 are six such pairs; a dragged place is `free` with its own pair.
internal static class NoticePlacement
{
    /// From each edge of the work area.
    public const double Edge = 4;

    /// How near (DIP) a dragged window must come to a centre line or an edge to stick to it.
    public const double Reach = 12;

    /// Between the island and the window under it; the window's own transparent margin adds to it, so the card shows
    /// as far under the island as it does under the screen's top edge.
    public const double IslandGap = 4;

    /// The island is never taller than this (60 at most, plus room); a taller region is the island turning into or
    /// out of the taskbar, and only its top band is the island.
    public const double IslandTallest = 64;

    /// A taskbar showing no more than this (DIP) at the edge is one hidden there (auto-hide leaves a sliver).
    public const double TaskbarSliver = 8;

    public static readonly (string Id, double X, double Y)[] Spots =
    {
        ("top-left", 0, 0), ("top-center", 0.5, 0), ("top-right", 1, 0),
        ("bottom-left", 0, 1), ("bottom-center", 0.5, 1), ("bottom-right", 1, 1),
    };

    /// The fractions of a setting: a named spot's pair, a free place's own (kept within 0–1), else bottom right.
    public static (double X, double Y) Of(string spot, double x, double y)
    {
        if (spot == "free") return (Unit(x), Unit(y));
        foreach (var s in Spots)
            if (s.Id == spot) return (s.X, s.Y);
        return (1, 1);
    }

    /// The named spot these fractions are, else `free`.
    public static string Name(double x, double y)
    {
        foreach (var s in Spots)
            if (Math.Abs(s.X - x) < 0.001 && Math.Abs(s.Y - y) < 0.001) return s.Id;
        return "free";
    }

    /// Cards in the upper half come in at the top and stack down; elsewhere they come in at the bottom and stack up.
    public static bool AtTop(double y) => y < 0.5;

    /// The window's top-left corner for these fractions.
    public static Point At(Rect work, Size window, double x, double y) =>
        new(work.Left + Edge + (Unit(x) * Room(work.Width, window.Width)), work.Top + Edge + (Unit(y) * Room(work.Height, window.Height)));

    /// The fractions of a top-left corner (inside the work area or not), rounded to four places.
    public static (double X, double Y) Fractions(Rect work, Size window, Point at) =>
        (Fraction(at.X - work.Left - Edge, Room(work.Width, window.Width)), Fraction(at.Y - work.Top - Edge, Room(work.Height, window.Height)));

    /// The corner kept inside the work area's edges.
    public static Point Clamp(Rect work, Size window, Point at) =>
        new(Math.Clamp(at.X, work.Left + Edge, work.Left + Edge + Room(work.Width, window.Width)),
            Math.Clamp(at.Y, work.Top + Edge, work.Top + Edge + Room(work.Height, window.Height)));

    /// What a line of the work area a dragged window sticks to on one axis.
    public enum Line { None, Start, Center, End }

    /// A dragged corner kept inside the work area and stuck to the nearest centre line or edge within Reach on each
    /// axis (`free`: no sticking). Against the island the top edge is under it: a window held just under the island
    /// sticks to the top, and shows under the island again (Avoid).
    public static (Point At, Line X, Line Y) Snap(Rect work, Size window, Point at, Rect? island, bool free)
    {
        at = Clamp(work, window, at);
        if (free) return (at, Line.None, Line.None);
        double left = work.Left + Edge, right = left + Room(work.Width, window.Width), centreX = work.Left + ((work.Width - window.Width) / 2);
        double top = work.Top + Edge, bottom = top + Room(work.Height, window.Height), centreY = work.Top + ((work.Height - window.Height) / 2);
        var (x, lineX) = Nearest(at.X, (left, Line.Start), (centreX, Line.Center), (right, Line.End));
        var (y, lineY) = Nearest(at.Y, (top, Line.Start), (centreY, Line.Center), (bottom, Line.End));
        if (lineY == Line.None && island is { } i && Across(x, window.Width, i) && Math.Abs(at.Y - (i.Bottom + IslandGap)) <= Reach)
            (y, lineY) = (top, Line.Start);
        return (new Point(x, y), lineX, lineY);
    }

    /// The corner moved down under the island when the window would cover it (or come nearer than IslandGap), kept
    /// on the work area; elsewhere the corner as it is.
    public static Point Avoid(Rect work, Size window, Point at, Rect? island)
    {
        if (island is not { } i || !Across(at.X, window.Width, i) || at.Y >= i.Bottom + IslandGap || at.Y + window.Height <= i.Top) return at;
        double under = i.Bottom + IslandGap;
        double lowest = work.Bottom - Edge - window.Height;
        return new Point(at.X, Math.Max(at.Y, Math.Min(under, lowest)));
    }

    /// The corner kept under every box above it (the island, the taskbar showing at the top), each as Avoid; the lowest
    /// last, so the corner ends under all of those it would cover.
    public static Point AvoidAll(Rect work, Size window, Point at, IEnumerable<Rect> above)
    {
        foreach (var box in above.OrderBy(b => b.Bottom)) at = Avoid(work, window, at, box);
        return at;
    }

    /// The taskbar's box in DIPs while it shows at the top of the monitor (rectangle px, at its scale): the part of its
    /// window its region leaves (`regionKind` as GetWindowRgnBox answers) on the monitor. Null when it is hidden (an
    /// empty region, or a sliver at the edge), on another monitor, or not against the top (a taskbar at the bottom).
    /// While it slides in or out the box is the part on the screen, so a window kept under it follows it.
    public static Rect? Taskbar(Win32.RECT window, int regionKind, Win32.RECT regionBox, Win32.RECT monitor, double scale)
    {
        if (regionKind == 1 || scale <= 0) return null;
        int left = window.Left, top = window.Top, right = window.Right, bottom = window.Bottom;
        if (regionKind is 2 or 3)
        {
            left = window.Left + regionBox.Left;
            top = window.Top + regionBox.Top;
            right = window.Left + regionBox.Right;
            bottom = window.Top + regionBox.Bottom;
        }
        left = Math.Max(left, monitor.Left);
        top = Math.Max(top, monitor.Top);
        right = Math.Min(right, monitor.Right);
        bottom = Math.Min(bottom, monitor.Bottom);
        if (right <= left || bottom <= top || top > monitor.Top || (bottom - top) / scale <= TaskbarSliver) return null;
        return new Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);
    }

    /// The island's box in DIPs from its window: the window rectangle and the box of its region (relative to the
    /// window), `regionKind` as GetWindowRgnBox answers (0 no region: the whole window; 1 an empty one: nothing
    /// shows), on the monitor whose rectangle (px) is given at its scale. Null when nothing of it is on that monitor.
    public static Rect? Island(Win32.RECT window, int regionKind, Win32.RECT regionBox, Win32.RECT monitor, double scale)
    {
        if (regionKind == 1 || scale <= 0) return null;
        int left = window.Left, top = window.Top, right = window.Right, bottom = window.Bottom;
        if (regionKind is 2 or 3)
        {
            left = window.Left + regionBox.Left;
            top = window.Top + regionBox.Top;
            right = window.Left + regionBox.Right;
            bottom = window.Top + regionBox.Bottom;
        }
        bottom = Math.Min(bottom, top + (int)Math.Ceiling(IslandTallest * scale));
        if (right <= left || bottom <= top) return null;
        if (right <= monitor.Left || left >= monitor.Right || bottom <= monitor.Top || top >= monitor.Bottom) return null;
        return new Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);
    }

    private static bool Across(double x, double width, Rect island) => x < island.Right + IslandGap && x + width > island.Left - IslandGap;

    private static (double, Line) Nearest(double v, params (double At, Line Line)[] lines)
    {
        (double at, Line line) best = (v, Line.None);
        double gap = Reach;
        foreach (var l in lines)
            if (Math.Abs(v - l.At) <= gap)
            {
                gap = Math.Abs(v - l.At);
                best = (l.At, l.Line);
            }
        return best;
    }

    private static double Room(double span, double size) => Math.Max(0, span - (2 * Edge) - size);

    private static double Fraction(double offset, double room) => room < 0.5 ? 0.5 : Math.Round(Unit(offset / room), 4);

    private static double Unit(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0.5;
}
