using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Native;

namespace Bloom.Music;

/// 다른 앱: what other programs tell Windows they play (Spotify, a browser playing YouTube, …), shown in the mini player
/// and steered through their own media buttons — Bloom never plays it. Asked once a second while the mini player is open
/// with the direct way (MusicPlayer's watch), forgotten when it closes; nothing is kept or sent. UI thread only.
internal static class OtherApps
{
    /// The sessions that have a song, in Windows' order.
    public static IReadOnlyList<MediaNow> Sessions { get; private set; } = Array.Empty<MediaNow>();

    /// The one the player shows on top when Bloom itself plays nothing: one that plays, else the first.
    public static MediaNow? Current => Sessions.FirstOrDefault(s => s.Status == MediaStatus.Playing) ?? (Sessions.Count > 0 ? Sessions[0] : null);

    private static bool polling;

    /// Each app's picture: for which song, the picture (null while missing), how often it was asked for.
    private static readonly Dictionary<string, (string song, ImageSource? image, int asked)> Arts = new();

    /// A picture an app has not given yet (a browser loads it a moment after the title) is asked for this often.
    private const int ArtTries = 3;

    /// The thumbnail's height in pixels: sharp on the 56-dip square up to 225 % scaling.
    private const int ArtPixels = 128;

    /// The apps' names as the Start menu shows them, read once each.
    private static readonly ConcurrentDictionary<string, string> Names = new();

    public static async Task Poll()
    {
        if (polling) return;
        polling = true;
        try
        {
            var all = await MediaSession.AllAsync();
            if (!MusicPlayer.Watched || MusicPlayer.External) return; // closed, or another player chosen, meanwhile
            var fresh = all.Where(s => s.Title.Length > 0 && s.Status != MediaStatus.Closed).ToList();
            var unnamed = fresh.Select(s => s.App).Where(a => !Names.ContainsKey(a)).Distinct().ToList();
            if (unnamed.Count > 0) await Task.Run(() => unnamed.ForEach(a => Names[a] = ShellName(a) ?? CleanName(a)));
            Sessions = fresh;
            foreach (var s in fresh) await Picture(s);
            foreach (string gone in Arts.Keys.Where(a => !fresh.Any(s => s.App == a)).ToList()) Arts.Remove(gone);
        }
        finally
        {
            polling = false;
        }
    }

    /// The mini player closed: nothing is held (the pictures neither).
    public static void Forget()
    {
        Sessions = Array.Empty<MediaNow>();
        Arts.Clear();
    }

    private static string Song(MediaNow s) => s.Title + "\n" + s.Artist + "\n" + s.Album;

    private static async Task Picture(MediaNow s)
    {
        string song = Song(s);
        if (Arts.TryGetValue(s.App, out var had) && had.song == song && (had.image is not null || had.asked >= ArtTries)) return;
        int asked = had.song == song ? had.asked + 1 : 1;
        Arts[s.App] = (song, null, asked);
        var bytes = await MediaSession.ThumbnailAsync(s.App);
        if (bytes is not null && Arts.TryGetValue(s.App, out var now) && now.song == song) Arts[s.App] = (song, Decode(bytes), asked);
    }

    /// The thumbnail's bytes as a picture ArtPixels high, or null (not a picture WPF reads).
    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = ArtPixels;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    /// The app's picture for the song it plays now, or null (none, or not come yet).
    public static ImageSource? ArtOf(MediaNow s) => Arts.TryGetValue(s.App, out var a) && a.song == Song(s) ? a.image : null;

    public static string NameOf(string app) => Names.TryGetValue(app, out string? name) ? name : CleanName(app);

    /// Where the song is now: where the app last said, moved on by the time since while it plays.
    public static TimeSpan PositionOf(MediaNow s, DateTime now)
    {
        var p = s.Position;
        if (s.Status == MediaStatus.Playing && now > s.At) p += now - s.At;
        if (s.Length > TimeSpan.Zero && p > s.Length) p = s.Length;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p;
    }

    // ── the app's buttons; asked again a moment later for how it stands ──────────────────────────
    public static void Press(MediaNow s, PlayerOp op) => _ = After(MediaSession.PressAsync(s.App, op));

    public static void Seek(MediaNow s, double fraction) => _ = After(MediaSession.SeekAsync(s.App, TimeSpan.FromTicks((long)(s.Length.Ticks * Math.Clamp(fraction, 0, 1)))));

    public static void ToggleShuffle(MediaNow s) => _ = After(MediaSession.ShuffleAsync(s.App, s.Shuffle != true));

    /// Off → all → one → off, as Bloom's own.
    public static void CycleRepeat(MediaNow s) => _ = After(MediaSession.RepeatAsync(s.App, s.Repeat switch { Repeat.Off => Repeat.All, Repeat.All => Repeat.One, _ => Repeat.Off }));

    private static async Task After(Task<bool> press)
    {
        await press;
        await Task.Delay(350);
        await Poll();
    }

    /// A readable name from an app's id when the Start menu knows none: "Spotify.exe" → "Spotify", "Pkg_hash!App" →
    /// "App", "{folder}\VLC\vlc.exe" → "Vlc", "chrome" → "Chrome".
    public static string CleanName(string app)
    {
        string s = app;
        int bang = s.LastIndexOf('!');
        if (bang >= 0) s = s[(bang + 1)..];
        int slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
        if (slash >= 0) s = s[(slash + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        int dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        s = s.Trim();
        return s.Length == 0 ? app : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// The name the Start menu shows for the app (shell:AppsFolder), or null. Off the UI thread.
    private static string? ShellName(string app)
    {
        if (ShellCom.SHCreateItemFromParsingName(@"shell:AppsFolder\" + app, IntPtr.Zero, ShellCom.IID_IShellItem, out IntPtr pointer) != 0 || pointer == IntPtr.Zero) return null;
        try
        {
            var item = (IShellItem)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                string? name = ShellCom.Name(item, ShellCom.SIGDN_NORMALDISPLAY);
                return string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains("AppsFolder", StringComparison.OrdinalIgnoreCase) ? null : name.Trim();
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }
}
