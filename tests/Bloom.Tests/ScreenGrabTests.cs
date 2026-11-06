using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Native;
using Bloom.Ocr;
using Bloom.Search;

namespace Bloom.Tests;

internal static class ScreenGrabTests
{
    public static void CaptureRenderModeIsLocalToItsHiddenWindow()
    {
        var before = RenderOptions.ProcessRenderMode;
        var desktop = Frozen(Pixels(128, 96), 128, 96);
        var overlay = ScreenGrab.Overlay(desktop, new ScreenRect(-32000, -32000, 128, 96), -32000, -32000,
            "Synthetic capture", _ => { }, null, () => null, Brushes.White);
        var ordinary = new Window { Width = 100, Height = 80, Left = -32000, Top = -32000, ShowActivated = false };
        try
        {
            // Create real HWNDs without showing, activating or sending desktop mouse input.
            var ordinarySource = HwndSource.FromHwnd(new WindowInteropHelper(ordinary).EnsureHandle())!;
            var ordinaryMode = ordinarySource.CompositionTarget.RenderMode;
            var captureSource = HwndSource.FromHwnd(new WindowInteropHelper(overlay).EnsureHandle())!;
            Check.Eq(captureSource.CompositionTarget.RenderMode, RenderMode.SoftwareOnly, "capture avoids the hardware surface");
            Check.Eq(ordinarySource.CompositionTarget.RenderMode, ordinaryMode, "ordinary window is unaffected");
            Check.Eq(RenderOptions.ProcessRenderMode, before, "global rendering preference is unchanged");
            Check.True(!overlay.IsVisible && !ordinary.IsVisible, "native source test never opens an overlay on the desktop");
        }
        finally
        {
            overlay.Close();
            ordinary.Close();
        }
    }

    /// A drag from the lower monitor onto the one above it shows on both: the area is shared in physical pixels and
    /// each overlay draws its own part (it showed only on the monitor it began on, clipped at its top edge).
    public static void DragAcrossStackedMonitorsShowsOnBoth()
    {
        var desktop = Frozen(Pixels(128, 192), 128, 192);
        var upper = new ScreenRect(-32000, -32096, 128, 96);
        var lower = new ScreenRect(-32000, -32000, 128, 96);
        var board = new PickBoard();
        var overlays = new[] { upper, lower }.Select(mon => ScreenGrab.Overlay(desktop, mon, -32000, -32096,
            "Synthetic capture", _ => { }, null, () => null, Brushes.White, board)).ToArray();
        try
        {
            foreach (var o in overlays) new WindowInteropHelper(o).EnsureHandle(); // real windows, never shown
            var area = new ScreenRect(-31990, -32050, 40, 80); // 46 px on the upper monitor, 34 on the lower
            board.Show(area, lower);
            var (upperFrame, upperSize) = Parts(overlays[0]);
            var (lowerFrame, lowerSize) = Parts(overlays[1]);
            Check.Eq(upperFrame.Visibility, Visibility.Visible, "the upper monitor shows its part");
            Check.Eq(lowerFrame.Visibility, Visibility.Visible, "the lower monitor shows its part");
            double s = VisualTreeHelper.GetDpi(overlays[0]).DpiScaleX;
            Check.Near(Canvas.GetTop(upperFrame), 46 / s, 0.01, "the upper part starts 46 px down its monitor");
            Check.Near(Canvas.GetTop(lowerFrame), -50 / s, 0.01, "the lower part runs on from above its top edge");
            Check.Eq(lowerSize.Visibility, Visibility.Visible, "the size shows under the area, on the lower monitor");
            Check.Eq(upperSize.Visibility, Visibility.Collapsed, "and only there");
            board.Show(null, lower);
            Check.Eq(upperFrame.Visibility, Visibility.Collapsed, "a cleared area goes from every monitor");
        }
        finally
        {
            foreach (var o in overlays) o.Close();
        }
    }

    /// Physical pixels into an overlay's DIPs, per monitor and scale; the size label goes to the monitor holding the
    /// area's bottom-left corner, or to the one the drag began on when no monitor does.
    public static void SharedAreaMapsToEachMonitor()
    {
        var upper = new ScreenRect(0, -1440, 2560, 1440);
        var lower = new ScreenRect(0, 0, 1920, 1080);
        var area = ScreenGrab.Between(new Win32.POINT { X = 300, Y = 200 }, new Win32.POINT { X = 100, Y = -400 });
        Check.Eq(area, new ScreenRect(100, -400, 200, 600), "either corner first");
        var onUpper = ScreenGrab.ToLocal(area, upper, 1.5);
        Check.Near(onUpper.Y, 1040 / 1.5, 0.001, "upper monitor: from 1040 px down");
        Check.Near(onUpper.Height, 400, 0.001, "600 physical px at 150 %");
        Check.Near(ScreenGrab.ToLocal(area, lower, 1).Y, -400, 0.001, "lower monitor: from above its top");
        var board = new PickBoard();
        board.Join(upper, (_, _) => { });
        board.Join(lower, (_, _) => { });
        Check.Eq(board.LabelMonitor(area, upper), lower, "bottom-left on the lower monitor");
        Check.Eq(board.LabelMonitor(new ScreenRect(2000, -300, 400, 400), upper), upper, "a corner in no monitor: the drag's own");
    }

    /// The hint sits under the island when the island is over its place, and goes to the work area's bottom when the
    /// picked area is under it at the top.
    public static void HintKeepsOffTheIslandAndTheArea()
    {
        var room = new Size(1280, 720);
        var work = new Rect(0, 0, 1280, 672); // a 48-DIP taskbar at the bottom
        var tip = new Size(300, 34);
        Check.Eq(ScreenGrab.TipTop(tip, room, work, null, null), 28.0, "28 under the top");
        var island = new Rect(540, 8, 200, 35);
        Check.Eq(ScreenGrab.TipTop(tip, room, work, island, null), 55.0, "under the island");
        Check.Eq(ScreenGrab.TipTop(tip, room, work, new Rect(40, 8, 200, 35), null), 28.0, "an island off to the side stays where it is");
        var area = new Rect(400, 20, 500, 300);
        Check.Eq(ScreenGrab.TipTop(tip, room, work, island, area), 672.0 - 28 - 34, "to the bottom of the work area, off the area");
        var tall = new Rect(400, 20, 500, 640);
        Check.Eq(ScreenGrab.TipTop(tip, room, work, island, tall), 55.0, "the area covers both: the top");
    }

    private static (FrameworkElement frame, FrameworkElement size) Parts(Window overlay)
    {
        var canvas = ((Grid)overlay.Content).Children.OfType<Canvas>().Single();
        return ((FrameworkElement)canvas.Children[1], (FrameworkElement)canvas.Children[2]);
    }

    public static void BitmapCropMatchesRawBgraAtEveryEdge()
    {
        const int width = 17, height = 13;
        var pixels = Pixels(width, height);
        var desktop = Frozen(pixels, width, height);
        // not frozen (a frozen one could not be let go without a GC): the picture, its overlays and its crops all stay on
        // the one capture thread that made it
        Check.True(!desktop.IsFrozen && desktop.CheckAccess(), "desktop belongs to the thread that made it");
        foreach (var crop in new[]
        {
            (x: 0, y: 0, w: width, h: height),
            (x: 0, y: 0, w: 1, h: 1),
            (x: width - 1, y: 0, w: 1, h: height),
            (x: 0, y: height - 1, w: width, h: 1),
            (x: 3, y: 4, w: 7, h: 5),
        })
        {
            var expected = ScreenGrab.Crop(pixels, width, crop.x, crop.y, crop.w, crop.h);
            var actual = ScreenGrab.Crop(desktop, crop.x, crop.y, crop.w, crop.h);
            Check.True(actual.SequenceEqual(expected), $"all BGRA bytes agree at {crop}");
        }
    }

    public static void MonitorOffsetsAndBitmapDpiKeepPhysicalPixels()
    {
        var virtualDesktop = new ScreenRect(-24, -12, 64, 48);
        var pixels = Pixels(virtualDesktop.Width, virtualDesktop.Height);
        // Picks from the monitor left/above the primary origin and the primary monitor.
        var picks = new[] { new ScreenRect(-20, -8, 12, 16), new ScreenRect(4, 6, 24, 20) };
        foreach (double dpi in new[] { 96.0, 120.0, 144.0, 192.0 })
        {
            var desktop = BitmapSource.Create(virtualDesktop.Width, virtualDesktop.Height, dpi, dpi,
                PixelFormats.Bgra32, null, pixels, virtualDesktop.Width * 4);
            desktop.Freeze();
            foreach (var pick in picks)
            {
                int x = pick.X - virtualDesktop.X, y = pick.Y - virtualDesktop.Y;
                var expected = ScreenGrab.Crop(pixels, virtualDesktop.Width, x, y, pick.Width, pick.Height);
                var actual = ScreenGrab.Crop(desktop, x, y, pick.Width, pick.Height);
                Check.True(actual.SequenceEqual(expected), $"physical crop at {dpi} DPI and origin {pick.X},{pick.Y}");
            }
        }
    }

    /// The capture's GDI bitmap is deleted right after DesktopPicture: WPF holds its own copy.
    public static void FrozenDesktopOwnsPixelsAfterTheirMemoryIsGone()
    {
        var expected = Pixels(23, 19);
        IntPtr memory = Marshal.AllocHGlobal(expected.Length);
        Marshal.Copy(expected, 0, memory, expected.Length);
        var desktop = ScreenGrab.DesktopPicture(memory, 23, 19);
        Marshal.Copy(new byte[expected.Length], 0, memory, expected.Length);
        Marshal.FreeHGlobal(memory);
        Check.True(ScreenGrab.Crop(desktop, 0, 0, 23, 19).SequenceEqual(expected), "WIC storage does not borrow the capture's memory");
    }

    /// The picker's desktop goes from the screen's bitmap straight into WIC: no desktop-sized array on the managed heap
    /// (28 MB for two 2560x1440 monitors, whose room the GC kept long after Lens closed), every pixel opaque. Reads the
    /// real screen; opens no window.
    public static void DesktopGrabLeavesNoDesktopSizedArray()
    {
        const int width = 640, height = 480;
        ScreenGrab.GrabDesktop(new ScreenRect(0, 0, 8, 8)); // first use: WPF's own one-time setup is not counted
        long before = GC.GetAllocatedBytesForCurrentThread();
        var desktop = ScreenGrab.GrabDesktop(new ScreenRect(0, 0, width, height));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(desktop is not null, "the screen can be read");
        if (desktop is null) return;
        Check.True(allocated < width * height * 4 / 8, $"{allocated} managed bytes for a {width * height * 4}-byte picture");
        var px = ScreenGrab.Crop(desktop, 0, 0, width, height);
        Check.True(Enumerable.Range(0, width * height).All(i => px[i * 4 + 3] == 255), "every pixel opaque");
    }

    /// A clipboard picture goes as CF_DIB (and its PNG), not DataObject.SetImage, whose GDI+ bitmap .NET 10 never
    /// deletes: a 40-byte header, then the rows bottom-up, the bytes as they are.
    public static void ClipboardPictureIsABottomUpDib()
    {
        const int width = 5, height = 3;
        var pixels = Pixels(width, height);
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var prior = Launch.SetClipboard;
        object? put = null;
        try
        {
            Launch.SetClipboard = d => put = d;
            Check.True(Launch.CopyDrawn(picture), "copied");
        }
        finally
        {
            Launch.SetClipboard = prior;
        }
        var obj = Check.NotNull(put as DataObject, "a data object");
        Check.True(obj.GetDataPresent("PNG", false), "with its PNG");
        Check.True(!obj.GetDataPresent(DataFormats.Bitmap, false), "no WPF bitmap to render through GDI+");
        var dib = Check.NotNull(obj.GetData(DataFormats.Dib, false) as System.IO.MemoryStream, "a DIB stream").ToArray();
        Check.Eq(dib.Length, 40 + width * height * 4, "header and pixels");
        Check.Eq(BitConverter.ToInt32(dib, 0), 40, "BITMAPINFOHEADER");
        Check.Eq(BitConverter.ToInt32(dib, 4), width, "width");
        Check.Eq(BitConverter.ToInt32(dib, 8), height, "positive height: bottom-up");
        Check.Eq(BitConverter.ToInt16(dib, 14), (short)32, "32 bits a pixel");
        for (int y = 0; y < height; y++)
            Check.True(dib.AsSpan(40 + y * width * 4, width * 4).SequenceEqual(pixels.AsSpan((height - 1 - y) * width * 4, width * 4)), $"row {y} is the picture's row {height - 1 - y}");
    }

    /// The overlays draw their monitor's part of the desktop through one brush (no CroppedBitmap, no converted copy):
    /// drawn at its own size, every pixel is the desktop's, none blended with a neighbour.
    public static void FrozenPartDrawsItsPixelsExactly()
    {
        const int width = 37, height = 29;
        var pixels = Pixels(width, height);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // the screen is opaque
        var desktop = Frozen(pixels, width, height);
        var part = new Int32Rect(7, 5, 19, 13);
        var fill = ScreenGrab.Frozen(desktop, part);
        fill.Measure(new Size(part.Width, part.Height));
        fill.Arrange(new Rect(0, 0, part.Width, part.Height));
        var drawn = new RenderTargetBitmap(part.Width, part.Height, 96, 96, PixelFormats.Pbgra32);
        drawn.Render(fill);
        var got = new byte[part.Width * part.Height * 4];
        drawn.CopyPixels(got, part.Width * 4, 0);
        Check.True(got.SequenceEqual(ScreenGrab.Crop(desktop, part.X, part.Y, part.Width, part.Height)), "the part's pixels, exactly");
        Check.Eq(desktop.Format, PixelFormats.Pbgra32, "a renderer format: WPF keeps no converted copy beside it");
    }

    /// The picker's desktop goes as soon as its windows are closed, not at a GC: LetGo closes the handles WPF holds it
    /// by (two buffers, 32 MB for this 2048 x 2048 picture), with no collection in between.
    public static void DesktopPictureGoesWithoutAGc()
    {
        const int side = 2048;
        var pixels = new byte[side * side * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var picture = Frozen(pixels, side, side);
        var fill = ScreenGrab.Frozen(picture, new Int32Rect(0, 0, side, side));
        fill.Measure(new Size(64, 64));
        fill.Arrange(new Rect(0, 0, 64, 64));
        // drawn once; RenderTargetBitmap's synchronous channel, not the overlays' window (their release, after the
        // renderer lets go, was measured on a running build)
        new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32).Render(fill);
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        me.Refresh();
        long before = me.PrivateMemorySize64;
        int collections = GC.CollectionCount(0);
        ScreenGrab.LetGo(picture);
        me.Refresh();
        long freed = before - me.PrivateMemorySize64;
        Check.Eq(GC.CollectionCount(0), collections, "no collection ran");
        Check.True(freed > 24L << 20, $"{freed >> 20} MB handed back at once");
        GC.KeepAlive(fill);
    }

    /// DesktopPicture of a managed array, pinned only for the call.
    private static WriteableBitmap Frozen(byte[] pixels, int width, int height)
    {
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return ScreenGrab.DesktopPicture(pin.AddrOfPinnedObject(), width, height);
        }
        finally
        {
            pin.Free();
        }
    }

    private static byte[] Pixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = (byte)(x * 17 + y * 11);
                pixels[at + 1] = (byte)(x * 7 + y * 29);
                pixels[at + 2] = (byte)(x * 23 + y * 3);
                pixels[at + 3] = (byte)(x * 13 + y * 5); // check alpha bytes as well as RGB
            }
        return pixels;
    }
}
