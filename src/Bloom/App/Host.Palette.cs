using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The palette the palette view shows: its colours by share, the swatch picked, the count asked for, its entry in the
/// colour history, and its picture's points to make it again from (PaletteExtract.Sample: a fresh Lens area's taken
/// from its pixels; read from its Lens copy the first time − n + is used on one opened from the history).
internal sealed class PaletteShow
{
    public required SavedPalette Saved { get; init; }
    public List<PaletteColor> Colors { get; set; } = new();

    /// The count asked for last: − n + and the keys go on from it before its colours come.
    public int Count { get; set; } = PaletteExtract.DefaultCount;

    public int Selected { get; set; }
    public Task<byte[]?>? Points { get; set; }

    /// Making it again is taking a while: the view says so over the colours it still shows.
    public bool Busy { get; set; }

    /// The newest making-again asked for, and the last one done: an older one's colours, when they come, are dropped.
    public int Asked { get; set; }

    public int Done { get; set; }

    /// − n + can make it again: its points are here (or on their way), or its Lens copy still is.
    public bool CanRemake => Points is not null || File.Exists(Saved.Source);
}

/// 컬러 팔레트: a picture's colours, made on this PC (Core\PaletteExtract.cs) away from the UI thread, and kept in the
/// colour history.
internal sealed partial class Host
{
    /// What the palette view shows (null until a palette is made or opened).
    public PaletteShow? Palette { get; private set; }

    /// How long making a palette again goes before the view says 색 추출 중.
    private static readonly TimeSpan PaletteBusyAfter = TimeSpan.FromMilliseconds(150);

    /// Bloom Lens › 7 (and the drawing bar's 팔레트, the ink in it): the area's colours, kept in the colour history,
    /// in the palette view.
    private async Task MakePalette(LensShot l)
    {
        var p = new PaletteShow { Saved = new SavedPalette { Source = l.Png, At = l.At }, Asked = 1 };
        // pixels gone (an earlier action): the Lens copy read a row at a time, as − n + does
        p.Points = Task.Run(() => l.Bgra.Length > 0 ? PaletteExtract.Sample(l.Bgra, l.Rect.Width, l.Rect.Height) : ReadPoints(l.Png));
        if (await Extract(p, PaletteExtract.DefaultCount, 1) is not { } colors) return;
        Keep(p, colors, PaletteExtract.DefaultCount);
        Palette = p;
        OpenView(Mode.Palette);
    }

    /// A palette from the colour history shown again (and newest in it again).
    public void OpenPalette(SavedPalette saved)
    {
        Palette = new PaletteShow { Saved = saved, Colors = saved.Read(), Count = Math.Clamp(saved.Count, PaletteExtract.MinCount, PaletteExtract.MaxCount) };
        ColorBook.AddPalette(saved);
        OpenView(Mode.Palette);
    }

    /// − n +: the same picture made again with `count` colours (3–10), in its place in the history, made away from the
    /// UI thread; the view shows them when they come (or, when that takes a while, 색 추출 중 and the count over the old
    /// ones). A count asked for before the last one's colours came drops them.
    public async Task SetPaletteCount(int count)
    {
        if (Palette is not { } p) return;
        count = Math.Clamp(count, PaletteExtract.MinCount, PaletteExtract.MaxCount);
        if (count == p.Count) return;
        int ticket = ++p.Asked;
        p.Count = count;
        _ = BusySoon(p, ticket);
        List<PaletteColor>? colors = null;
        try
        {
            colors = await Extract(p, count, ticket);
        }
        catch (Exception e)
        {
            Log.Error("palette", e);
        }
        if (ticket != p.Asked || Palette != p) return; // a newer count, or another palette, came meanwhile
        p.Done = ticket;
        p.Busy = false;
        if (colors is not null) Keep(p, colors, count);
        else
        {
            p.Points = null; // read again next time; while its Lens copy is gone − n + goes
            p.Count = p.Saved.Count;
            Window.ShowToast(Tr("원본 그림이 지워져 다시 만들 수 없습니다", "The picture is gone, so it can't be made again"));
        }
        RefreshPalette(p);
    }

    /// A swatch picked (a click, ← →): its gradients show under it; a click also copies its HEX.
    public void PickSwatch(int i, bool copy)
    {
        if (Palette is not { Colors.Count: > 0 } p) return;
        p.Selected = Math.Clamp(i, 0, p.Colors.Count - 1);
        if (copy)
        {
            string hex = ColorBook.Hex(p.Colors[p.Selected].Color);
            Window.ShowToast(Launch.Copied(Launch.Copy(hex), Tr($"{hex} 복사 완료", $"Copied {hex}")));
        }
        RefreshPalette(p);
    }

    private void RefreshPalette(PaletteShow p)
    {
        if (Palette == p && Window.Mode == Mode.Palette) Window.Refresh(true);
    }

    /// 색 추출 중 when making `ticket` again is still going after a moment.
    private async Task BusySoon(PaletteShow p, int ticket)
    {
        await Task.Delay(PaletteBusyAfter);
        if (ticket != p.Asked || p.Done == ticket) return;
        p.Busy = true;
        RefreshPalette(p);
    }

    /// `p`'s colours, `count` of them, made away from the UI thread (its points read first, once); null when its picture
    /// is gone or doesn't read, or when `ticket` is no longer the newest asked for.
    private static async Task<List<PaletteColor>?> Extract(PaletteShow p, int count, int ticket)
    {
        string? png = p.Saved.Source;
        var points = await (p.Points ??= Task.Run(() => ReadPoints(png)));
        if (points is null || ticket != p.Asked) return null;
        var colors = await Task.Run(() => PaletteExtract.Of(points, count));
        return ticket == p.Asked ? colors : null;
    }

    /// The colours shown, and kept in the colour history in place of those made before from the same picture.
    private static void Keep(PaletteShow p, List<PaletteColor> colors, int count)
    {
        p.Colors = colors;
        p.Count = count;
        p.Selected = Math.Clamp(p.Selected, 0, Math.Max(0, colors.Count - 1));
        if (colors.Count == 0) return; // nothing opaque in it: nothing to keep
        p.Saved.Colors = colors.ConvertAll(c => ColorBook.Hex(c.Color));
        p.Saved.Shares = colors.ConvertAll(c => Math.Round(c.Share, 4));
        p.Saved.Count = count;
        ColorBook.AddPalette(p.Saved);
    }

    /// A Lens copy's points (PaletteExtract.Sample's, the same as from its pixels), decoded a row at a time and only the
    /// rows the grid looks at kept, so even an 8K copy is never in memory whole; null when it is gone or doesn't read.
    internal static byte[]? ReadPoints(string? png)
    {
        if (png is null || !File.Exists(png)) return null;
        try
        {
            using var fs = File.OpenRead(png);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth;
            return PaletteExtract.Sample(w, bgra.PixelHeight, (y, row) => bgra.CopyPixels(new Int32Rect(0, y, w, 1), row, w * 4, 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            Log.Error("palette picture", e);
            return null;
        }
    }
}
