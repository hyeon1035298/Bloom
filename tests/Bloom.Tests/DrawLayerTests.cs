using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Bloom.Ocr;

namespace Bloom.Tests;

internal static class DrawLayerTests
{
    /// The bar goes below the area, above it, or inside its bottom, skipping a place the hint or the island covers.
    public static void BarKeepsOffTheHintAndTheIsland()
    {
        var room = new Size(1000, 600);
        var bar = new Size(500, 42);
        var sel = new Rect(100, 300, 400, 200); // room below (510) and above (248)
        Check.Eq(DrawLayer.BarAt(sel, room, bar, []), (100.0, 510.0), "below the area when nothing is in the way");
        var island = new Rect(300, 520, 300, 40); // over the place below
        Check.Eq(DrawLayer.BarAt(sel, room, bar, [island]), (100.0, 248.0), "above the area when the place below is covered");
        var hint = new Rect(250, 240, 500, 40); // and over the place above
        Check.Eq(DrawLayer.BarAt(sel, room, bar, [island, hint]), (100.0, 448.0), "inside the area's bottom when both are covered");
        var low = new Rect(100, 20, 400, 560); // no room below or above: inside, covered or not
        Check.Eq(DrawLayer.BarAt(low, room, bar, [new Rect(0, 500, 1000, 100)]), (100.0, 528.0), "inside when nothing else fits");
    }

    public static void PreferencesStayLocalAndUndoRedoKeepExportPixels()
    {
        var initial = new DrawPreferences("pen", 0, 1, 1, 1);
        var saved = new List<DrawPreferences>();
        var canvas = new Canvas();
        var layer = new DrawLayer(canvas, new Rect(12, 18, 64, 48), new Size(300, 200), _ => { }, initial, DrawTheme.Capture(), saved.Add);
        var ink = canvas.Children.OfType<InkCanvas>().Single();
        byte[] background = Enumerable.Repeat((byte)255, 128 * 96 * 4).ToArray();
        Check.True(ReferenceEquals(layer.Merge(background, 128, 96), background), "no ink returns original pixels");
        layer.AddForQa("pen", 2, new Point(8, 16), new Point(48, 16));
        var pen = layer.Merge(background, 128, 96);
        Check.True(!pen.SequenceEqual(background), "DIP ink is exported at the physical-pixel scale");
        Check.Eq(ink.DefaultDrawingAttributes.Width, 4.0, "pen keeps saved width");
        Check.True(ink.DefaultDrawingAttributes.FitToCurve, "existing pen smoothing stays enabled");
        Check.Eq(initial.PenColor, 0, "caller snapshot never changes");
        Check.Eq(saved.Last().PenColor, 2, "changed preference is a new immutable snapshot");
        Step(layer, true);
        Check.True(ReferenceEquals(layer.Merge(background, 128, 96), background), "undo restores plain capture");
        Step(layer, false);
        Check.True(layer.Merge(background, 128, 96).SequenceEqual(pen), "redo restores identical exported pixels");
        layer.AddForQa("mark", 1, new Point(8, 32), new Point(48, 32));
        Check.True(ink.DefaultDrawingAttributes.IsHighlighter, "highlighter behavior stays native");
        Check.Eq(ink.DefaultDrawingAttributes.StylusTip, StylusTip.Rectangle, "highlighter retains rectangular tip");
        Check.Eq(saved.Last().Tool, "mark", "tool preference remains independent of caller settings");
        var both = layer.Merge(background, 128, 96);
        ink.Strokes.Remove(ink.Strokes[0]); // same StrokesChanged contract as whole-stroke eraser
        Check.Eq(ink.Strokes.Count, 1, "whole-stroke erase removes one stroke");
        Step(layer, true);
        Check.True(layer.Merge(background, 128, 96).SequenceEqual(both), "undo erase restores identical export");
    }

    private static void Step(DrawLayer layer, bool back)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(DrawLayer);
        object undo = type.GetField("undo", flags)!.GetValue(layer)!;
        object redo = type.GetField("redo", flags)!.GetValue(layer)!;
        type.GetMethod("Step", flags)!.Invoke(layer, new[] { back ? undo : redo, back ? redo : undo, (object)back });
    }
}
