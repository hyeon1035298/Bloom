using System.Windows;
using Bloom.Native;
using Bloom.Ocr;
using Bloom.Ui;

namespace Bloom.Tests;

internal static class RecordUiTests
{
    /// Pill taskbar cuts the taskbar window to its pill with a region; Windows' own taskbar has none, or a full one.
    public static void PillIsTheTaskbarsRegion()
    {
        var window = new Win32.RECT { Left = 0, Top = 1392, Right = 2560, Bottom = 1440 };
        var pill = new Win32.RECT { Left = 980, Top = 0, Right = 1580, Bottom = 48 };
        Check.Eq(RecordSurface.Pill(window, 2, pill), new Rect(980, 1392, 600, 48), "the pill on the screen");
        Check.Eq(RecordSurface.Pill(window, 0, pill), null, "no region: Windows' own taskbar");
        Check.Eq(RecordSurface.Pill(window, 1, pill), null, "an empty region: hidden");
        Check.Eq(RecordSurface.Pill(window, 2, new Win32.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 48 }), null, "a region as wide as the window");
    }

    /// The plain bar goes under the area, over it when the work area ends, inside its bottom when neither fits.
    public static void PlainBarKeepsInsideTheWorkArea()
    {
        var work = new ScreenRect(0, 0, 2560, 1392);
        Check.Eq(RecordBar.PlainTop(new ScreenRect(100, 100, 800, 600), work, 60, 9), 709, "under the area");
        Check.Eq(RecordBar.PlainTop(new ScreenRect(100, 700, 800, 650), work, 60, 9), 631, "over it, the taskbar's room below");
        Check.Eq(RecordBar.PlainTop(new ScreenRect(0, 0, 2560, 1392), work, 60, 9), 1323, "inside its bottom for the whole screen");
    }

    public static void FrameLineIsTwoDips()
    {
        Check.Eq(RecordingFrame.Thickness(1), 2, "100 %");
        Check.Eq(RecordingFrame.Thickness(1.5), 3, "150 %");
        Check.Eq(RecordingFrame.Thickness(0.5), 2, "never thinner than 2 px");
    }

    /// Each surface's bar builds, and goes through counting, recording and saving without being shown (no window on
    /// the screen).
    public static void BarBuildsForEverySurface()
    {
        var area = new ScreenRect(-32000, -32000, 400, 300);
        var light = new Palette();
        light.Apply(false, null, 0);
        foreach (var s in new[]
        {
            new RecordSurface(RecordSurfaceKind.Plain, Rect.Empty),
            new RecordSurface(RecordSurfaceKind.Pill, new Rect(-32000, -31900, 300, 48)),
            new RecordSurface(RecordSurfaceKind.Island, new Rect(-32000, -32000, 190, 35)),
        })
        {
            var bar = new RecordBar(s, area, light, true, false);
            try
            {
                Check.True(bar.Width > 200 && bar.Height > 40, $"{s.Kind}: room for the menu ({bar.Width} × {bar.Height})");
                bool asked = false;
                bar.SoundsChanged += (_, _) => asked = true;
                bar.Counting();
                bar.Recording();
                bar.Saving();
                Check.True(!asked && bar.SystemAudio && !bar.Mic, $"{s.Kind}: the sounds as given");
            }
            finally
            {
                bar.Close();
            }
        }
    }
}
