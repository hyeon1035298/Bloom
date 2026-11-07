using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Addons;
using Bloom.Ui;

namespace Bloom.Tests;

/// The taskbar folders' icon set: each icon in both looks as an .ico of every size, the command that hands both over,
/// and shell icons with straight alpha taken in without a light fringe.
internal static class FolderIconSetTests
{
    public static void EveryIconInBothLooks()
    {
        Check.True(FolderIconSet.Names.Count >= 14, "the set's icons");
        foreach (string name in FolderIconSet.Names)
            foreach (bool silver in new[] { true, false })
            {
                byte[] ico = IconFile.SetIcon(name, silver);                int count = BitConverter.ToUInt16(ico, 4);
                Check.Eq(count, 9, $"{name}: sizes");
                var sides = Enumerable.Range(0, count).Select(i => ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i]).ToArray();
                Check.Eq(string.Join(",", sides), "16,20,24,32,40,48,64,96,256", $"{name}: the sizes in order");
                int last = 6 + 16 * (count - 1);
                int offset = BitConverter.ToInt32(ico, last + 12), length = BitConverter.ToInt32(ico, last + 8);
                var frame = BitmapDecoder.Create(new MemoryStream(ico, offset, length), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var px = new byte[256 * 256 * 4];
                bgra.CopyPixels(px, 256 * 4, 0);
                byte A(int x, int y) => px[(y * 256 + x) * 4 + 3];
                byte G(int x, int y) => px[(y * 256 + x) * 4 + 1];
                Check.Eq(A(2, 2), (byte)0, $"{name}: outside the tile");
                Check.Eq(A(40, 128), (byte)255, $"{name}: the tile");
                Check.True(silver ? G(40, 128) > 200 : G(40, 128) < 110, $"{name}: the tile's {(silver ? "silver" : "graphite")}");
                int ink = 0;
                for (int i = 0; i < px.Length; i += 4)
                    if (px[i + 3] == 255 && (silver ? px[i + 1] < 60 : px[i + 1] > 235)) ink++;
                Check.True(ink > 1000, $"{name}: a glyph in the ink of the {(silver ? "silver" : "graphite")} tile ({ink} px)");
            }
    }

    public static void ThemedIconCommand()
    {
        var (cmd, args) = TaskbarCommands.IconThemed("F", @"C:\d.ico", @"C:\l.ico");
        Check.Eq(cmd, "icon", "command");
        Check.Eq(args["kind"]!.GetValue<string>(), "themed", "kind");
        Check.Eq(args["dark"]!.GetValue<string>(), @"C:\d.ico", "for a dark taskbar");
        Check.Eq(args["light"]!.GetValue<string>(), @"C:\l.ico", "for a light taskbar");
    }

    public static void StraightAlphaPremultiplied()
    {
        // white at half alpha, straight: a channel above its alpha
        var straight = new byte[] { 255, 255, 255, 128, 0, 0, 0, 0 };
        Icons.Premultiply(straight);
        Check.Eq((straight[0], straight[1], straight[2], straight[3]), ((byte)128, (byte)128, (byte)128, (byte)128), "premultiplied");
        var already = new byte[] { 100, 50, 128, 128, 255, 255, 255, 255 };
        Icons.Premultiply(already);
        Check.Eq((already[0], already[1], already[2]), ((byte)100, (byte)50, (byte)128), "a premultiplied picture stays");
    }
}
