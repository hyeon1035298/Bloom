using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bloom.Ui;

/// .ico files Bloom makes for the taskbar folders: an icon of the set in either look, a symbol drawn on a light or dark
/// rounded tile, or a picture the user picked (.png) brought into an .ico. Each size is a PNG inside the .ico, which
/// Windows reads from Vista on.
internal static class IconFile
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 256 };
    private static readonly int[] SetSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 256 };

    /// An icon of the set (FolderIconSet) on its silver tile (`silver`, for a dark taskbar) or its graphite one, each
    /// size from the drawing made for it.
    public static byte[] SetIcon(string name, bool silver)
    {
        var frames = new List<byte[]>();
        foreach (int px in SetSizes)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(px / 256.0, px / 256.0));
                dc.DrawDrawing(FolderIconSet.Draw(name, silver, px));
            }
            frames.Add(Png(Render(v, px)));
        }
        return Ico(frames, SetSizes);
    }

    /// The symbol `glyph` (Segoe Fluent Icons) on a tile: dark ground with a white symbol, or light ground with a dark one.
    public static byte[] Glyph(char glyph, bool dark)
    {
        var frames = new List<byte[]>();
        var ground = new SolidColorBrush(dark ? Color.FromRgb(0x27, 0x27, 0x2A) : Color.FromRgb(0xF4, 0xF4, 0xF5));
        var ink = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x27, 0x27, 0x2A));
        foreach (int px in Sizes)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                double r = px * 0.22;
                dc.DrawGeometry(ground, null, SmoothBorder.Shape(new Rect(0, 0, px, px), new CornerRadius(r)));
                var text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Theme.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), px * 0.56, ink, 1.0);
                dc.DrawText(text, new Point((px - text.Width) / 2, (px - text.Height) / 2));
            }
            frames.Add(Png(Render(v, px)));
        }
        return Ico(frames, Sizes);
    }

    /// A picture file (.png, or anything WPF decodes) as an .ico: its first frame fitted into a 256 square.
    public static byte[] FromPicture(string file)
    {
        var src = BitmapFrame.Create(new Uri(file), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
        double scale = 256.0 / Math.Max(src.PixelWidth, src.PixelHeight);
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            double w = src.PixelWidth * scale, h = src.PixelHeight * scale;
            dc.DrawImage(src, new Rect((256 - w) / 2, (256 - h) / 2, w, h));
        }
        return Ico(new List<byte[]> { Png(Render(v, 256)) }, new[] { 256 });
    }

    /// Writes `bytes` under `dir` named by their content (the same icon twice is one file); returns the path.
    public static string Save(string dir, string prefix, byte[] bytes)
    {
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{prefix}-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..12]}.ico");
        if (!File.Exists(file)) File.WriteAllBytes(file, bytes);
        return file;
    }

    private static RenderTargetBitmap Render(Visual v, int px)
    {
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        return bmp;
    }

    private static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// ICONDIR, one ICONDIRENTRY per image, then the PNGs.
    internal static byte[] Ico(IReadOnlyList<byte[]> pngs, IReadOnlyList<int> sizes)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1); // an icon
        w.Write((ushort)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        for (int i = 0; i < pngs.Count; i++)
        {
            byte side = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 is 256
            w.Write(side);
            w.Write(side);
            w.Write((byte)0); // no palette
            w.Write((byte)0);
            w.Write((ushort)1); // planes
            w.Write((ushort)32); // bits per pixel
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
        w.Flush();
        return ms.ToArray();
    }
}
