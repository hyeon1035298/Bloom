using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// The home's two columns shared by a dragged handle (Ui\Controls\HomeColumns.cs, Ui\Controls\HomeSplitter.cs): the
/// share kept sane and clamped, the columns' widths from it, the default as the home always was, and the setting kept.
internal static class HomeSplitTests
{
    public static void ShareAndClamp()
    {
        Check.Eq(HomeColumns.Share(0.4), 0.4, "a kept share as it is");
        foreach (double bad in new[] { 0, 1, -0.2, 1.5, double.NaN, double.PositiveInfinity })
            Check.Eq(HomeColumns.Share(bad), HomeColumns.DefaultSplit, $"{bad}: the default");
        // each column at least ColumnMin
        Check.Eq(HomeColumns.LeftWidth(1000, 0.5), 500.0, "half");
        Check.Eq(HomeColumns.LeftWidth(1000, 0.1), HomeColumns.ColumnMin, "the left column no narrower than the least");
        Check.Eq(HomeColumns.LeftWidth(1000, 0.9), 1000 - HomeColumns.ColumnMin, "… nor the right one");
        Check.Eq(HomeColumns.LeftWidth(1300, 0.4), 520.0, "room enough: the share as it is");
        Check.Eq(HomeColumns.LeftWidth(700, 0.3), 350.0, "too narrow for two at their least: halves");
        // where the handle is put: the share it gives, clamped alike
        Check.Eq(HomeColumns.SplitAt(450, 1000), 0.45, "at 450 of 1000");
        Check.Eq(HomeColumns.SplitAt(-50, 1000), HomeColumns.ColumnMin / 1000, "dragged past the left edge");
        Check.Eq(HomeColumns.SplitAt(5000, 1000), (1000 - HomeColumns.ColumnMin) / 1000, "… past the right one");
        Check.Eq(HomeColumns.SplitAt(double.NaN, 1000), HomeColumns.DefaultSplit, "nowhere: the default");
        Check.True(HomeColumns.TwoColumnWidth >= 2 * HomeColumns.ColumnMin, "two columns come only where both have their least");
    }

    public static void ColumnsFollowTheSplit()
    {
        var grid = new HomeColumns();
        Border Block(double height, bool right)
        {
            var b = new Border { Height = height };
            HomeColumns.SetRight(b, right);
            grid.Children.Add(b);
            return b;
        }
        var a = Block(100, false);
        var c = Block(80, true);
        void Lay(double width)
        {
            grid.Measure(new Size(width, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
        }
        static string Box(UIElement e)
        {
            var at = System.Windows.Media.VisualTreeHelper.GetOffset(e);
            return FormattableString.Invariant($"{at.X},{at.Y} {e.RenderSize.Width}");
        }
        static string Handle(HomeColumns g)
        {
            var s = g.SplitterForQa;
            var at = System.Windows.Media.VisualTreeHelper.GetOffset(s);
            return FormattableString.Invariant($"{at.X} {s.RenderSize.Width}x{s.RenderSize.Height}");
        }

        Check.Eq(grid.Split, HomeColumns.DefaultSplit, "a new home: half");
        Check.True(grid.Children.Count == 2, "the handle is no block");
        Lay(1100);
        Check.Eq(Box(a) + " / " + Box(c), "0,0 550 / 550,0 550", "the default: halves, as before the handle");
        Check.Eq(Handle(grid), "544 12x100", "the handle over the gap, as tall as the columns");

        grid.Split = 0.4;
        Lay(1100);
        Check.Eq(Box(a) + " / " + Box(c), "0,0 440 / 440,0 660", "40 %");
        Check.Eq(Handle(grid), "434 12x100", "… the handle with it");
        var (right, _, below) = grid.DropAt(new Point(450, 10));
        Check.True(right == true && below == c, "a drop just past the split: the right column");
        (right, _, _) = grid.DropAt(new Point(430, 10));
        Check.True(right == false, "… just before it: the left one");
        grid.OpenGap(true, null, 30);
        Lay(1100);
        Check.Eq(grid.GapBox, new Rect(440, 80, 660, 30), "a gap as wide as its column");
        grid.OpenGap(false, null, 0);

        grid.Split = 0.1;
        Lay(1100);
        Check.Eq(Box(a) + " / " + Box(c), "0,0 360 / 360,0 740", "kept at the least");
        grid.Split = double.NaN;
        Check.Eq(grid.Split, HomeColumns.DefaultSplit, "nonsense: the default");

        grid.Split = 0.7;
        Lay(760);
        Check.True(!grid.Two && Box(a) == "0,0 760" && Box(c) == "0,100 760", "one column: the split leaves it be");
        Check.Eq(grid.SplitterForQa.RenderSize, new Size(0, 0), "… and there is no handle");
    }

    public static void KeptInSettings()
    {
        Check.Eq(new Settings().HomeSplit, HomeColumns.DefaultSplit, "a new settings file: half");
        var old = JsonSerializer.Deserialize<Settings>("{\"Width\":1100}", Json.Options)!;
        Check.Eq(old.HomeSplit, HomeColumns.DefaultSplit, "a file from before the handle: half");
        var kept = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { HomeSplit = 0.37 }, Json.Options), Json.Options)!;
        Check.Eq(kept.HomeSplit, 0.37, "a dragged share comes back");
        var bad = JsonSerializer.Deserialize<Settings>("{\"HomeSplit\":3}", Json.Options)!;
        Check.Eq(HomeColumns.Share(bad.HomeSplit), HomeColumns.DefaultSplit, "one edited out of range lays out as half");
    }

    public static void HandleTellsTheShare()
    {
        var grid = new HomeColumns();
        double? set = null;
        grid.SplitSet += s => set = s;
        grid.Split = 0.3;
        // a double click: back at half, told to be kept
        grid.ResetSplit();
        Check.True(grid.Split == HomeColumns.DefaultSplit && set == HomeColumns.DefaultSplit, "double-clicked: half, kept");
        Check.True(!grid.Splitting && !grid.Children.OfType<HomeSplitter>().Any(), "not being dragged; never among the blocks");
    }

    /// A press on the handle let go where it was (a bare click, a double click's first) keeps nothing; one dragged past
    /// the drag distance tells the share once. A raised event reads the mouse where the press read it, so the press is put
    /// at the panel's left edge to stand for a drag.
    public static void OnlyADragIsKept()
    {
        var grid = new HomeColumns();
        foreach (bool right in new[] { false, true })
        {
            var block = new Border { Height = 100 };
            HomeColumns.SetRight(block, right);
            grid.Children.Add(block);
        }
        var w = MdViewTests.Hosted(grid, 1100, 200);
        try
        {
            MdViewTests.Pump(() => grid.Two, "two columns");
            int sets = 0;
            grid.SplitSet += _ => sets++;
            var handle = grid.SplitterForQa;
            void Raise(RoutedEvent ev)
            {
                var e = ev == UIElement.MouseMoveEvent ? new MouseEventArgs(Mouse.PrimaryDevice, 0) : new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                e.RoutedEvent = ev;
                handle.RaiseEvent(e);
            }
            Raise(UIElement.MouseLeftButtonDownEvent);
            Check.True(grid.Splitting, "pressed: being dragged");
            Raise(UIElement.MouseLeftButtonUpEvent);
            Check.True(!grid.Splitting && sets == 0 && grid.Split == HomeColumns.DefaultSplit, "let go unmoved: nothing kept");
            Raise(UIElement.MouseLeftButtonDownEvent);
            typeof(HomeSplitter).GetField("pressed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handle, new Point(0, 0));
            Raise(UIElement.MouseMoveEvent);
            Raise(UIElement.MouseLeftButtonUpEvent);
            Check.Eq(sets, 1, "dragged: the share told once");
        }
        finally
        {
            w.Close();
        }
    }
}
