using System;
using System.Windows;
using Bloom.Native;
using Bloom.Ui;
using Line = Bloom.Ui.NoticePlacement.Line;

namespace Bloom.Tests;

/// Where the notice cards go (Ui\NoticePlacement.cs): places as fractions, the named spots, sticking while dragged, and
/// the island window (one a taskbar add-on keeps at the top middle of a screen) the cards stay under.
internal static class NoticePlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private static readonly Size Window = new(350, 120);

    private static Win32.RECT R(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    public static void NamedSpotsAreWhereTheyWere()
    {
        // the six spots as they were placed before fractions: 4 from the edges, centred across
        Check.Eq(NoticePlacement.At(Work, Window, 0, 0), new Point(4, 4), "top left");
        Check.Eq(NoticePlacement.At(Work, Window, 1, 1), new Point(1920 - 350 - 4, 1040 - 120 - 4), "bottom right");
        Check.Eq(NoticePlacement.At(Work, Window, 0.5, 0), new Point((1920 - 350) / 2.0, 4), "top centre");
        var (x, y) = NoticePlacement.Of("bottom-center", 0, 0);
        Check.Eq((x, y), (0.5, 1.0), "bottom centre's fractions");
        Check.Eq(NoticePlacement.Of("free", 0.3, 1.7), (0.3, 1.0), "a free place kept within 0–1");
        Check.Eq(NoticePlacement.Of("somewhere", 0.3, 0.3), (1.0, 1.0), "an unknown spot: bottom right");
        Check.Eq(NoticePlacement.Name(0.5, 0), "top-center", "back to its name");
        Check.Eq(NoticePlacement.Name(0.5, 0.5), "free", "the middle has no name");
        Check.True(NoticePlacement.AtTop(0.2) && !NoticePlacement.AtTop(0.5) && !NoticePlacement.AtTop(1), "upper half stacks down, else up");
    }

    public static void FractionsGoBothWays()
    {
        var shifted = new Rect(-1280, 200, 1280, 680); // a work area that doesn't start at 0
        foreach (var (x, y) in new[] { (0.0, 0.0), (0.25, 0.75), (0.5, 0.5), (1.0, 0.1) })
            Check.Eq(NoticePlacement.Fractions(shifted, Window, NoticePlacement.At(shifted, Window, x, y)), (x, y), $"({x}, {y}) placed and read again");
        Check.Eq(NoticePlacement.Fractions(Work, Window, new Point(-500, 5000)), (0.0, 1.0), "past the edges: the edges");
        Check.Eq(NoticePlacement.Fractions(new Rect(0, 0, 300, 100), Window, new Point(0, 0)), (0.5, 0.5), "no room: the middle");
        // the same place means the same thing at another size: centred stays centred, bottom stays bottom
        var big = new Size(420, 400);
        var (fx, fy) = NoticePlacement.Fractions(Work, Window, NoticePlacement.At(Work, Window, 0.5, 1));
        Check.Eq(NoticePlacement.At(Work, big, fx, fy), new Point((1920 - 420) / 2.0, 1040 - 400 - 4), "a taller stack, still centred at the bottom");
    }

    public static void SticksToTheMiddleAndTheEdges()
    {
        double centreX = (1920 - 350) / 2.0, centreY = (1040 - 120) / 2.0;
        var (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(centreX + 9, 15), null, false);
        Check.Eq((at, lx, ly), (new Point(centreX, 4), Line.Center, Line.Start), "near the top middle: stuck to it");
        (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(centreX - 5, 1040 - 120 - 4 - 11), null, false);
        Check.Eq((at, lx, ly), (new Point(centreX, 916), Line.Center, Line.End), "bottom middle");
        (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(10, centreY + 12), null, false);
        Check.Eq((at, lx, ly), (new Point(4, centreY), Line.Start, Line.Center), "left middle");
        (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(1920 - 350 - 4 + 30, centreY - 3), null, false);
        Check.Eq((at, lx, ly), (new Point(1566, centreY), Line.End, Line.Center), "past the right edge: kept in, stuck right");
        (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(centreX + 13, 300), null, false);
        Check.Eq((at, lx, ly), (new Point(centreX + 13, 300), Line.None, Line.None), "out of reach: where it was let go");
        (at, lx, ly) = NoticePlacement.Snap(Work, Window, new Point(centreX + 3, 10), null, true);
        Check.Eq((at, lx, ly), (new Point(centreX + 3, 10), Line.None, Line.None), "Ctrl held: nothing sticks");
        Check.Eq(NoticePlacement.Snap(Work, Window, new Point(-40, -40), null, true).At, new Point(4, 4), "Ctrl held: still kept in");
    }

    public static void StaysUnderTheIsland()
    {
        var island = new Rect(785, 0, 350, 35); // a pill at the top middle
        double centreX = (1920 - 350) / 2.0;
        var top = NoticePlacement.At(Work, Window, 0.5, 0);
        Check.Eq(NoticePlacement.Avoid(Work, Window, top, island), new Point(centreX, 35 + NoticePlacement.IslandGap), "top centre: under the island");
        Check.Eq(NoticePlacement.Avoid(Work, Window, new Point(4, 4), island), new Point(4, 4), "top left: beside it, left alone");
        Check.Eq(NoticePlacement.Avoid(Work, Window, new Point(centreX, 200), island), new Point(centreX, 200), "lower down: left alone");
        Check.Eq(NoticePlacement.Avoid(Work, Window, top, null), top, "no island");
        Check.Eq(NoticePlacement.Avoid(Work, Window, new Point(785 - 350 - 2, 4), island), new Point(785 - 350 - 2, 35 + NoticePlacement.IslandGap), "nearer its side than the gap: under it");
        var tall = new Size(350, 1030); // four large cards: kept on the screen, as low as it goes
        Check.Eq(NoticePlacement.Avoid(Work, tall, new Point(centreX, 4), island), new Point(centreX, 1040 - 4 - 1030), "a stack too tall to go under: on the screen");
        // dragged just under the island it sticks to the top (and shows under the island)
        var (at, _, ly) = NoticePlacement.Snap(Work, Window, new Point(centreX, 35 + NoticePlacement.IslandGap + 7), island, false);
        Check.Eq((at, ly), (new Point(centreX, 4), Line.Start), "held under the island: the top spot");
        Check.Eq(NoticePlacement.Name(NoticePlacement.Fractions(Work, Window, at).X, NoticePlacement.Fractions(Work, Window, at).Y), "top-center", "kept as 가운데 위");
    }

    public static void ReadsTheIslandsBox()
    {
        var monitor = R(0, 0, 2560, 1440);
        // the window spans the island and the taskbar pill; the region (relative) is the island
        var box = NoticePlacement.Island(R(783, 0, 1778, 56), 3, R(337, 0, 657, 35), monitor, 1);
        Check.Eq(box, (Rect?)new Rect(1120, 0, 320, 35), "the region's box on the screen");
        Check.Eq(NoticePlacement.Island(R(783, 0, 1778, 56), 2, R(337, 0, 657, 35), monitor, 1.5), (Rect?)new Rect(1120 / 1.5, 0, 320 / 1.5, 35 / 1.5), "in DIPs at 150 %");
        Check.Eq(NoticePlacement.Island(R(783, 0, 1778, 56), 1, default, monitor, 1), (Rect?)null, "an empty region: hidden in the taskbar");
        Check.Eq(NoticePlacement.Island(R(1100, 0, 1460, 40), 0, default, monitor, 1), (Rect?)new Rect(1100, 0, 360, 40), "no region: the window");
        Check.Eq(NoticePlacement.Island(R(838, -1440, 1723, -1384), 3, R(282, 0, 602, 35), monitor, 1), (Rect?)null, "on the other monitor: not this one's");
        // turning into the taskbar the region is the whole window, down to the taskbar: only its top band counts
        var morph = NoticePlacement.Island(R(700, 0, 1860, 1440), 2, R(0, 0, 1160, 1440), monitor, 1);
        Check.Eq(morph, (Rect?)new Rect(700, 0, 1160, NoticePlacement.IslandTallest), "a morph frame: its top band");
    }

    public static void StaysUnderATaskbarAtTheTop()
    {
        var monitor = R(0, 0, 2560, 1440);
        // auto-hide at the top: hidden, only its 2 px sliver on the screen
        Check.Eq(NoticePlacement.Taskbar(R(0, -54, 2560, 2), 2, R(0, 54, 2560, 56), monitor, 1), (Rect?)null, "hidden: not in the way");
        var shown = NoticePlacement.Taskbar(R(0, 0, 2560, 56), 2, R(0, 0, 2560, 56), monitor, 1);
        Check.Eq(shown, (Rect?)new Rect(0, 0, 2560, 56), "shown: its box");
        Check.Eq(NoticePlacement.Taskbar(R(0, -30, 2560, 26), 2, R(0, 0, 2560, 56), monitor, 1), (Rect?)new Rect(0, 0, 2560, 26), "sliding: the part on the screen");
        Check.Eq(NoticePlacement.Taskbar(R(0, 1384, 2560, 1440), 0, default, monitor, 1), (Rect?)null, "at the bottom: not above anything");
        Check.Eq(NoticePlacement.Taskbar(R(0, -1440, 2560, -1384), 0, default, monitor, 1), (Rect?)null, "another monitor's");
        Check.Eq(NoticePlacement.Taskbar(R(0, 0, 2560, 56), 1, default, monitor, 1), (Rect?)null, "an empty region");

        // the cards under the island while it rests, under the taskbar while it shows, whichever is lower
        var island = new Rect(785, 0, 350, 35);
        double centreX = (1920 - 350) / 2.0;
        var top = NoticePlacement.At(Work, Window, 0.5, 0);
        var bar = new Rect(0, 0, 1920, 56);
        Check.Eq(NoticePlacement.AvoidAll(Work, Window, top, new[] { island }), new Point(centreX, 35 + NoticePlacement.IslandGap), "the island alone");
        Check.Eq(NoticePlacement.AvoidAll(Work, Window, top, new[] { bar }), new Point(centreX, 56 + NoticePlacement.IslandGap), "the taskbar alone");
        Check.Eq(NoticePlacement.AvoidAll(Work, Window, top, new[] { bar, island }), new Point(centreX, 56 + NoticePlacement.IslandGap), "both: under the lower");
        Check.Eq(NoticePlacement.AvoidAll(Work, Window, top, Array.Empty<Rect>()), top, "nothing above");
    }
}
