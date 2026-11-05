using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Clip;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// ram-copies: the memory the big temporary copies take, on a made-up 3840 × 2160 picture (the same one every run) and
/// the real desktop (read, never shown or kept): a clipboard picture made into its entry (PNG bytes as a browser copies
/// them, and a frozen bitmap as the DIB fallback hands it on), the Lens pixels still held after a Lens action, and the
/// colour picker's desktop. The entries' ids show that the picture files keep their names. Written to ram-copies.txt;
/// nothing goes to the clipboard.
internal static partial class Captures
{
    private static async Task RamCopies(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        log.AppendLine($"ram-copies · {DateTime.Now:yyyy-MM-dd HH:mm} · .NET {Environment.Version}");
        log.AppendLine("Times in ms, the median of 3 runs. Memory in MiB: what the work allocated (managed), and how far the managed heap");
        log.AppendLine("and the private bytes rose above where they stood before it, at their peak (the most of the 3 runs).");
        log.AppendLine();

        // the clipboard history's own steps, as Capture runs them (private there: reached by name)
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
        var read = typeof(ClipboardHistory).GetMethod("Read", Hidden)!;
        var picture = typeof(ClipboardHistory).GetMethod("Picture", Hidden)!;
        string clipDir = Path.GetDirectoryName(Paths.InLocal(@"clipboard\history.dat"))!;
        void Fresh()
        {
            // each run writes its PNG again (an existing file is never encoded twice)
            Directory.CreateDirectory(clipDir);
            foreach (string f in Directory.GetFiles(clipDir, "*.png")) File.Delete(f);
        }

        const int W = 3840, H = 2160;
        var source = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, Desktop(W, H), W * 4);
        source.Freeze();
        byte[] png;
        using (var ms = new MemoryStream())
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            enc.Save(ms);
            png = ms.ToArray();
        }
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png));
        log.AppendLine($"── clipboard picture {W}×{H} · pixels {W * H * 4 / 1048576.0:0.0} MiB · PNG {png.Length / 1048576.0:0.0} MiB");
        var fromPng = await Measure(() =>
        {
            Fresh();
            var work = (Func<ClipEntry?>)read.Invoke(null, [data])!;
            return work()!;
        });
        log.AppendLine($"  PNG bytes (Read + its work)            {Row(fromPng)} · {fromPng.Value.Id}");
        var fromBitmap = await Measure(() =>
        {
            Fresh();
            return (ClipEntry)picture.Invoke(null, [source])!;
        });
        log.AppendLine($"  frozen bitmap (Picture)                {Row(fromBitmap)} · {fromBitmap.Value.Id}");
        log.AppendLine();

        // Lens: what the last area still holds once a Lens action is done with it
        const int LW = 2560, LH = 1440;
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, LW, LH), Desktop(LW, LH));
        await host.LensDo("palette");
        long heapAfter = GC.GetTotalMemory(true);
        log.AppendLine($"── Lens {LW}×{LH} · pixels {LW * LH * 4 / 1048576.0:0.0} MiB");
        log.AppendLine($"  pixels held after Lens › palette       {(host.LastLens?.Bgra.Length ?? 0) / 1048576.0:0.0} MiB · managed heap after a GC {heapAfter / 1048576.0:0.0} MiB");
        log.AppendLine();

        // the colour picker's desktop: the way it was (an array, then a bitmap of it) and WIC's copy alone
        var screen = new Ocr.ScreenRect(Native.Win32.GetSystemMetrics(76), Native.Win32.GetSystemMetrics(77), Native.Win32.GetSystemMetrics(78), Native.Win32.GetSystemMetrics(79));
        log.AppendLine($"── colour picker desktop {screen.Width}×{screen.Height} · pixels {(long)screen.Width * screen.Height * 4 / 1048576.0:0.0} MiB");
        var arrayWay = await Measure(() =>
        {
            var shot = Ocr.ScreenGrab.Grab(screen)!;
            var bmp = BitmapSource.Create(screen.Width, screen.Height, 96, 96, PixelFormats.Bgra32, null, shot, screen.Width * 4);
            bmp.Freeze();
            return bmp;
        });
        log.AppendLine($"  array + bitmap of it                   {Row(arrayWay)}");
        var wicWay = await Measure(() => Ocr.ScreenGrab.GrabDesktop(screen)!);
        log.AppendLine($"  WIC's copy alone (GrabDesktop)         {Row(wicWay)}");

        Fresh();
        await File.WriteAllTextAsync(Path.Combine(dir, "ram-copies.txt"), log.ToString());
    }
}
