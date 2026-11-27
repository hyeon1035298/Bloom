using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ui;

namespace Bloom.App;

/// draw: 화면 캡처's pen and highlighter (and 직선 · 사각형) — the frozen area with ink and the bar under it (d01), the bar near the
/// screen's bottom edge going above the area (d02), the merged picture that is copied (d03) and the settings rows for
/// the saved captures (d04).
internal static partial class Captures
{
    private static async Task DrawScreens(Host host, Launcher w, string dir)
    {
        var (px, pw, ph) = Message(dir);
        var shot = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, px, pw * 4);
        string l = En ? "en" : "ko";
        Ocr.DrawLayer? last = null;
        foreach (var (name, top) in new[] { ("d01-draw-bar", 90.0), ("d02-draw-bar-above", 330.0) })
        {
            var canvas = new Canvas { Width = 900, Height = 480, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x63)) };
            var sel = new Rect(120, top, pw, ph);
            var image = new System.Windows.Controls.Image { Source = shot, Width = pw, Height = ph };
            Canvas.SetLeft(image, sel.X);
            Canvas.SetTop(image, sel.Y);
            canvas.Children.Add(image);
            var frame = new System.Windows.Shapes.Rectangle { Width = pw, Height = ph, Stroke = Theme.P.Accent, StrokeThickness = 1.5 };
            Canvas.SetLeft(frame, sel.X);
            Canvas.SetTop(frame, sel.Y);
            canvas.Children.Add(frame);
            var layer = new Ocr.DrawLayer(canvas, sel, new Size(900, 480), _ => { });
            layer.AddForQa("mark", 1, new Point(200, 38), new Point(330, 38));
            layer.AddForQa("pen", 0, new Point(16, 80), new Point(60, 66), new Point(200, 64), new Point(300, 76), new Point(230, 98), new Point(60, 96), new Point(20, 82));
            layer.AddShapeForQa("rect", 2, new Point(12, 12), new Point(160, 56));
            layer.AddShapeForQa("line", 3, new Point(250, 106), new Point(340, 64));
            last = layer;
            canvas.Measure(new Size(900, 480));
            canvas.Arrange(new Rect(0, 0, 900, 480));
            canvas.UpdateLayout();
            await Task.Delay(50);
            var rtb = new RenderTargetBitmap(1350, 720, 144, 144, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            Png(rtb, Path.Combine(dir, $"{l}-{name}.png"));
        }
        var merged = last!.Merge(px, pw, ph);
        Png(BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, merged, pw * 4), Path.Combine(dir, $"{l}-d03-merged.png"));

        // 설정 › Bloom Lens › 캡처 저장본: the folder row (count · size) and 모두 삭제, found by searching
        host.SetLensForQa(new Ocr.ScreenRect(0, 0, pw, ph), merged);
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Filter(Tr("캡처 저장본", "saved captures"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-d04-settings-saved.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        // 모두 삭제 empties the folder of this QA data dir (never the user's)
        int gone = host.ClearLensFiles();
        await File.WriteAllTextAsync(Path.Combine(dir, $"{l}-d05-cleared.txt"), $"deleted {gone}, left {Host.LensFiles().count}");
    }

    private static void Png(BitmapSource picture, string file)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(picture));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
