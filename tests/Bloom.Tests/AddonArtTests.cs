using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ui;

namespace Bloom.Tests;

/// The moving pictures on 설정 > 애드온's lines (AddonArt).
internal static class AddonArtTests
{
    private static readonly string[] Ids = ["pill-taskbar", "alt-tab-per-monitor", "brightness", "memory-clean", "keep-awake"];

    public static void EachKnownAddonHasAPicture()
    {
        foreach (var id in Ids) Check.True(AddonArt.For(id) is not null, $"{id} has a picture");
        Check.True(AddonArt.For("someone-elses") is null, "one Bloom has no picture of shows its glyph");
    }

    /// Every picture draws at every moment of its loop, in both themes. BLOOM_ART_OUT=<folder> keeps them as PNGs.
    public static void EveryMomentDraws()
    {
        string? keep = Environment.GetEnvironmentVariable("BLOOM_ART_OUT");
        bool dark = Theme.P.Dark;
        try
        {
            foreach (bool d in new[] { false, true })
            {
                Theme.P.Apply(d, null, 0);
                foreach (var id in Ids)
                    for (double p = 0; p < 1; p += 0.05)
                    {
                        var art = AddonArt.For(id)!;
                        art.Hold = p;
                        art.Measure(new Size(AddonArt.W, AddonArt.H));
                        art.Arrange(new Rect(0, 0, AddonArt.W, AddonArt.H));
                        var bmp = new RenderTargetBitmap((int)AddonArt.W * 2, (int)AddonArt.H * 2, 192, 192, PixelFormats.Pbgra32);
                        bmp.Render(art);
                        var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
                        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
                        int drawn = 0;
                        for (int i = 3; i < px.Length; i += 4) if (px[i] > 0) drawn++;
                        Check.True(drawn > 2000, $"{id} at {p:0.00} draws ({drawn} pixels)");
                        if (keep is null) continue;
                        Directory.CreateDirectory(keep);
                        var png = new PngBitmapEncoder();
                        png.Frames.Add(BitmapFrame.Create(bmp));
                        using var f = File.Create(Path.Combine(keep, $"{id}-{(d ? "dark" : "light")}-{(int)Math.Round(p * 100):00}.png"));
                        png.Save(f);
                    }
            }
        }
        finally { Theme.P.Apply(dark, null, 0); }
    }
}
