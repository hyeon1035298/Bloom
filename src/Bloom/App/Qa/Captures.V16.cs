using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ui;

namespace Bloom.App;

/// v16: a typed path, 창 배치, dates and other cities' clocks, Bloom Lens with 화면에 고정 (and the pinned picture),
/// an item's 단축키 지정 in Ctrl+K, 앱 설치 (a real, read-only winget search) and the settings rows for them — in the
/// language of the run (`--lang ko|en`).
internal static partial class Captures
{
    private static async Task V16Screens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            string p = $"{l}-{theme}-";
            await Shot(w, dir, p + "p01-path", Mode.Root, @"C:\Windows\");
            await Shot(w, dir, p + "p02-path-start", Mode.Root, @"C:\Pro");
            await Shot(w, dir, p + "w01-layout-root", Mode.Root, Tr("왼쪽 절반", "left half"));
            await Shot(w, dir, p + "w02-layout-view", Mode.Layout, "");
            await Shot(w, dir, p + "d01-offset", Mode.Root, Tr("100일 뒤", "in 100 days"));
            await Shot(w, dir, p + "d02-until", Mode.Root, Tr("크리스마스까지", "days until christmas"));
            await Shot(w, dir, p + "d03-clock", Mode.Root, Tr("뉴욕 오후 3시", "new york 3pm"));
            await Shot(w, dir, p + "d04-now", Mode.Root, Tr("도쿄 시간", "tokyo time"));
        }
        Theme.Apply("dark");
        string d = $"{l}-dark-";

        // Bloom Lens: a drawn message as the picked area, the menu with 5, and the pinned picture itself
        var shot = Message(dir);
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, shot.w, shot.h), shot.px);
        await Shot(w, dir, d + "l01-lens-menu", Mode.Lens, "", 800);
        var pin = new Ocr.PinWindow(host, host.LastLens!) { ShowActivated = false };
        pin.Show();
        await Task.Delay(300);
        await pin.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)pin.Content, Path.Combine(dir, d + "l02-pinned.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        pin.Close();

        // Ctrl+K on a command: 단축키 지정
        w.Go(Mode.Root, Tr("창 배치", "window layout"));
        await Task.Delay(400);
        w.OpenActions();
        await Snap(w, dir, d + "k01-actions");
        w.CloseActionsForQa();

        // 앱 설치: winget's own search (nothing is installed)
        w.Go(Mode.Root, Tr("설치 vlc", "install vlc"));
        await Task.Delay(400);
        await Snap(w, dir, d + "g01-root");
        await Shot(w, dir, d + "g02-view", Mode.Winget, "vlc", 9000);

        // settings: 기능 (the new switches) and 단축키 (item shortcuts)
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (var (page, name) in new[] { ("features", "s01-features"), ("shortcuts", "s02-shortcuts") })
        {
            sw.Page(page);
            await Task.Delay(400);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, d + name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
    }

    /// A small chat-like message drawn as the screen area Bloom Lens picked.
    private static (byte[] px, int w, int h) Message(string dir)
    {
        const int W = 360, H = 120;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xF2)), null, new Rect(0, 0, W, H));
            var face = new Typeface("Segoe UI");
            double ppd = 1;
            dc.DrawText(new FormattedText("Design review moved to 3:30 PM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 18, Brushes.Black, ppd), new Point(18, 26));
            dc.DrawText(new FormattedText("Room 4B · bring the poster drafts", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 15, Brushes.DimGray, ppd), new Point(18, 64));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[W * H * 4];
        rtb.CopyPixels(px, W * 4, 0);
        using (var fs = File.Create(Path.Combine(dir, "lens-message.png")))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(fs);
        }
        return (px, W, H);
    }
}
