using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ui;

namespace Bloom.App;

/// drawclip: the frames of 바뀐 점's picture for 직선 · 사각형 (tools\whatsnew\pack.py packs them): a rectangle dragged
/// around the first words of a message, then 직선 in hand and the time underlined, held, and back to the start.
/// Writes drawclip-<lang>\f0000.png… and meta.json.
internal static partial class Captures
{
    private const int ClipFps = 15;
    private const double ClipScale = 1.5; // the picture's pixels per DIP: words readable at half size in 바뀐 점
    private static readonly Size ClipRoom = new(640, 400 / ClipScale);
    private static readonly Rect ClipArea = new(40, 26, 560, 150);

    private static async Task DrawClipScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        string frames = Path.Combine(dir, "drawclip-" + l);
        Directory.CreateDirectory(frames);
        var (screen, box, under) = ClipMessage();
        // the drags, in the area's DIPs: the box's corners, the underline's ends
        var a = new Point(box.Left, box.Top);
        var b = new Point(box.Right, box.Bottom);
        var c = new Point(under.Left, under.Top);
        var d = new Point(under.Right, under.Top);
        const double boxFrom = 0.6, boxTo = 1.6, lineIn = 2.2, lineFrom = 2.8, lineTo = 3.6, back = 6.0, end = 6.4;

        BitmapSource At(double t)
        {
            double r = Ease((t - boxFrom) / (boxTo - boxFrom)), q = Ease((t - lineFrom) / (lineTo - lineFrom));
            var canvas = new Canvas { Width = ClipRoom.Width, Height = ClipRoom.Height, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x63)) };
            var image = new System.Windows.Controls.Image { Source = screen, Width = ClipArea.Width, Height = ClipArea.Height };
            Canvas.SetLeft(image, ClipArea.X);
            Canvas.SetTop(image, ClipArea.Y);
            canvas.Children.Add(image);
            var frame = new System.Windows.Shapes.Rectangle { Width = ClipArea.Width, Height = ClipArea.Height, Stroke = Theme.P.Accent, StrokeThickness = 1.5 };
            Canvas.SetLeft(frame, ClipArea.X);
            Canvas.SetTop(frame, ClipArea.Y);
            canvas.Children.Add(frame);
            // a room wider than the picture keeps the bar under the area's left edge; it runs off the picture's right
            var layer = new Ocr.DrawLayer(canvas, ClipArea, new Size(2000, ClipRoom.Height), _ => { }, new Ocr.DrawPreferences("rect", 0, 1, 1, 1), persist: _ => { });
            if (r > 0.02) layer.AddShapeForQa("rect", 0, a, Lerp(a, b, r));
            if (t >= lineIn) layer.UseForQa("line");
            if (q > 0.02) layer.AddShapeForQa("line", 0, c, Lerp(c, d, q));
            var at = t < boxFrom ? a : t < lineIn ? Lerp(a, b, r) : t < lineFrom ? Lerp(b, c, Ease((t - lineIn) / (lineFrom - lineIn))) : Lerp(c, d, q);
            Crosshair(canvas, new Point(ClipArea.X + at.X, ClipArea.Y + at.Y));
            canvas.Measure(ClipRoom);
            canvas.Arrange(new Rect(ClipRoom));
            canvas.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)Math.Round(ClipRoom.Width * ClipScale), (int)Math.Round(ClipRoom.Height * ClipScale), 96 * ClipScale, 96 * ClipScale, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            return rtb;
        }

        var first = At(0);
        var last = At(back);
        int count = (int)Math.Round(end * ClipFps);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / ClipFps;
            var picture = t < back ? At(t) : Blend(last, first, (t - back) / (end - back));
            Png(picture, Path.Combine(frames, $"f{i:0000}.png"));
            await Task.Delay(1);
        }
        await File.WriteAllTextAsync(Path.Combine(frames, "meta.json"), $"{{\"fps\": {ClipFps}, \"count\": {count}}}");
    }

    /// The message on the frozen screen, drawn as vectors (sharp at the picture's scale), with the box around its second
    /// line and the line under the time in its first (the area's DIPs; the underline's Top is its height).
    private static (DrawingImage screen, Rect box, Rect under) ClipMessage()
    {
        // the sample's words are the picture's, not the app's: no Tr (its tables would want them in every language)
        string before = En ? "Design review moved to " : "디자인 리뷰가 ", time = En ? "3:30 PM" : "오후 3:30", after = En ? "" : "으로 바뀌었습니다";
        string note = En ? "Room 4B · bring the poster drafts" : "4B 회의실 · 포스터 시안 챙겨 오기";
        var family = new FontFamily(En ? "Segoe UI" : "Malgun Gothic");
        var face = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var plain = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23));
        var faint = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x73));
        FormattedText Text(string s, Typeface f, double size, Brush brush) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f, size, brush, ClipScale);
        var first = new Point(32, 30);
        var line = Text(before + time + after, face, 22, ink);
        double timeAt = Text(before, face, 22, ink).WidthIncludingTrailingWhitespace, timeWidth = Text(time, face, 22, ink).Width;
        var second = new Point(first.X, first.Y + 62);
        var small = Text(note, plain, 18, faint);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xF2)), null, new Rect(ClipArea.Size));
            dc.DrawText(line, first);
            dc.DrawText(small, second);
        }
        var box = new Rect(second.X - 10, second.Y - 7, small.Width + 20, small.Height + 14);
        var under = new Rect(first.X + timeAt - 2, first.Y + line.Baseline + 8, timeWidth + 4, 0);
        return (new DrawingImage(group), box, under);
    }

    /// The pointer as the shapes show it: a thin cross, light with a dark rim.
    private static void Crosshair(Canvas canvas, Point at)
    {
        foreach (var (brush, thick) in new[] { (Color.FromRgb(0x20, 0x20, 0x24), 3.0), (Color.FromRgb(0xF4, 0xF4, 0xF5), 1.0) })
        {
            var stroke = new SolidColorBrush(brush);
            canvas.Children.Add(new System.Windows.Shapes.Line { X1 = at.X - 9, Y1 = at.Y, X2 = at.X + 9, Y2 = at.Y, Stroke = stroke, StrokeThickness = thick });
            canvas.Children.Add(new System.Windows.Shapes.Line { X1 = at.X, Y1 = at.Y - 9, X2 = at.X, Y2 = at.Y + 9, Stroke = stroke, StrokeThickness = thick });
        }
    }

    private static BitmapSource Blend(BitmapSource from, BitmapSource to, double k)
    {
        var all = new Rect(0, 0, from.PixelWidth, from.PixelHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(from, all);
            dc.PushOpacity(k);
            dc.DrawImage(to, all);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(from.PixelWidth, from.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    private static Point Lerp(Point a, Point b, double k) => a + (b - a) * k;

    /// 0 → 1 over 0 → 1, slow at both ends; clamped outside.
    private static double Ease(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
