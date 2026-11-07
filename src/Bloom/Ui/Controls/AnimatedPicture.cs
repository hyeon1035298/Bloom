using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Bloom.Ui;

/// An animated WebP (바뀐 점's pictures), played while it is on screen. Windows' WebP codec hands out every frame
/// already put together on the whole picture but not how long each one shows, so the times are read from the file's
/// ANMF chunks. One frame is decoded at a time into one bitmap: a long loop holds no more memory than a still. With
/// animations off it shows one moment of the loop and plays while the pointer is on it (as AddonArt does).
internal sealed class AnimatedPicture : Image
{
    /// The moment of the loop shown while it doesn't play (0–1): most pictures show their point a little before the middle.
    private const double Still = 0.45;

    private readonly BitmapDecoder decoder;
    private readonly int[] times;
    private readonly WriteableBitmap canvas;
    private readonly byte[] pixels;
    private int frame = -1;
    private DispatcherTimer? timer;
    private bool pointed;

    public AnimatedPicture(byte[] webp)
    {
        decoder = BitmapDecoder.Create(new MemoryStream(webp, false), BitmapCreateOptions.None, BitmapCacheOption.None);
        times = FrameTimes(webp, decoder.Frames.Count);
        var first = decoder.Frames[0];
        canvas = new WriteableBitmap(first.PixelWidth, first.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
        pixels = new byte[first.PixelWidth * first.PixelHeight * 4];
        Show(StillFrame());
        Source = canvas;
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(RenderSize), 10, 10);
        Loaded += (_, _) => Run();
        Unloaded += (_, _) => Run();
        IsVisibleChanged += (_, _) => Run();
        MouseEnter += (_, _) => Point(true);
        MouseLeave += (_, _) => Point(false);
    }

    public int Frames => times.Length;

    /// How long each frame shows, in milliseconds: the ANMF chunks' durations, in order (a 24-bit number 12 bytes into
    /// each). 40 ms a frame when the file has another count of them than the codec gave.
    internal static int[] FrameTimes(byte[] webp, int count)
    {
        var found = new System.Collections.Generic.List<int>();
        if (webp.Length >= 12 && webp[0] == 'R' && webp[1] == 'I' && webp[2] == 'F' && webp[3] == 'F' && webp[8] == 'W' && webp[9] == 'E' && webp[10] == 'B' && webp[11] == 'P')
        {
            for (long at = 12; at + 8 <= webp.Length;)
            {
                long size = BitConverter.ToUInt32(webp, (int)at + 4);
                if (webp[at] == 'A' && webp[at + 1] == 'N' && webp[at + 2] == 'M' && webp[at + 3] == 'F' && size >= 16 && at + 8 + size <= webp.Length)
                {
                    int d = (int)at + 8 + 12;
                    found.Add(webp[d] | webp[d + 1] << 8 | webp[d + 2] << 16);
                }
                at += 8 + size + (size & 1);
            }
        }
        if (found.Count != count) return Array.ConvertAll(new int[Math.Max(count, 1)], _ => 40);
        // a frame shorter than a screen refresh would never be seen; browsers stretch such ones too
        return found.ConvertAll(t => Math.Max(t, 20)).ToArray();
    }

    /// The frame showing at Still of the loop.
    private int StillFrame()
    {
        long total = 0, at = 0;
        foreach (int t in times) total += t;
        for (int i = 0; i < times.Length; i++)
        {
            at += times[i];
            if (at > total * Still) return i;
        }
        return 0;
    }

    private void Show(int i)
    {
        if (i == frame) return;
        frame = i;
        BitmapSource f = decoder.Frames[i];
        if (f.Format != PixelFormats.Bgra32) f = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
        int w = Math.Min(f.PixelWidth, canvas.PixelWidth), h = Math.Min(f.PixelHeight, canvas.PixelHeight);
        var rect = new Int32Rect(0, 0, w, h);
        f.CopyPixels(rect, pixels, canvas.PixelWidth * 4, 0);
        canvas.WritePixels(rect, pixels, canvas.PixelWidth * 4, 0);
    }

    private void Point(bool on)
    {
        if (pointed == on) return;
        pointed = on;
        Run();
    }

    /// Plays while on screen and things may move (or it is pointed at); otherwise stops on its still moment.
    private void Run()
    {
        bool moves = Motion.Moves, go = IsLoaded && IsVisible && times.Length > 1 && (moves || pointed);
        if (go && timer is null)
        {
            if (!moves) Show(0); // pointed at with animations off: from the start each time
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(times[frame]) };
            timer.Tick += (_, _) =>
            {
                Show((frame + 1) % times.Length);
                timer!.Interval = TimeSpan.FromMilliseconds(times[frame]);
            };
            timer.Start();
        }
        else if (!go && timer is not null)
        {
            timer.Stop();
            timer = null;
            if (!moves) Show(StillFrame());
        }
    }
}
