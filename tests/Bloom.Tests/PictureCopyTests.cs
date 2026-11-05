using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.App;
using Bloom.Clip;
using Bloom.Ocr;

namespace Bloom.Tests;

/// The big picture copies that are read a band at a time or let go: they give the same bytes as the whole copy did.
internal static class PictureCopyTests
{
    public static void ClipboardPictureHashIsTheWholeArraysHash()
    {
        // one band (small), bands that don't divide the height, and rows wider than a band (one row at a time): the
        // name of a picture's file stays what hashing one array of all its pixels gave
        foreach (var (w, h) in new[] { (17, 13), (4100, 7), (17000, 3), (1, 1) })
        {
            var px = Pixels(w, h);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            string whole = Convert.ToHexString(SHA256.HashData(px), 0, 10).ToLowerInvariant();
            Check.Eq(ClipboardHistory.Hash(bmp), whole, $"{w} × {h}");
        }
    }

    public static void LensPixelsLetGoReadBackFromTheCopy()
    {
        const int w = 123, h = 45;
        var px = Pixels(w, h);
        for (int i = 3; i < px.Length; i += 4) px[i] = 255; // a Lens area is opaque
        string png = Path.Combine(Path.GetTempPath(), "bloom-lens-test-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
            using (var fs = File.Create(png)) enc.Save(fs);
            var held = new LensShot(new ScreenRect(0, 0, w, h), px, png, DateTime.Now);
            Check.True(ReferenceEquals(held.Pixels(), px), "held: the pixels themselves");
            var gone = held with { Bgra = [] };
            Check.True(gone.Pixels().AsSpan().SequenceEqual(px), "let go: the same bytes from the copy");
        }
        finally
        {
            File.Delete(png);
        }
        var lost = new LensShot(new ScreenRect(0, 0, w, h), [], png, DateTime.Now);
        var blank = lost.Pixels();
        Check.Eq(blank.Length, w * h * 4, "copy gone too: a blank picture of the area's size");
        Check.True(blank.All(b => b == 0), "nothing in it");
    }

    private static byte[] Pixels(int width, int height)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 31 + i / 7);
        return px;
    }
}
