using System;
using System.Windows.Media;
using Bloom.Ui;

namespace Bloom.Music;

/// What plays now, as the mini player and the home's 플레이어 block show it: Bloom's own song (or the other player the
/// settings chose), or, while Bloom itself plays nothing the direct way, another app's (OtherApps.Current); its three
/// buttons steer whichever is shown, and Bloom's own song's cover is asked for once per song and shared. UI thread only.
internal static class NowPlaying
{
    /// One look at the song a view shows. Other: the app's session it came from (null: Bloom's own). App: where it plays
    /// when not Bloom ("" for Bloom). Has: a song at all. Cover: its picture, null while missing.
    internal sealed record Song(MediaNow? Other, string App, string Title, string Artist, bool Has, bool Playing,
        bool CanPrevious, bool CanToggle, bool CanNext, ImageSource? Cover)
    {
        /// What it looks like, without where in the song it is: the same while only the position moves.
        public string Key => $"{Other?.App}|{App}|{Title}|{Artist}|{Has}|{Playing}|{CanPrevious}{CanToggle}{CanNext}|{Cover is not null}";
    }

    /// The cover's side in pixels: sharp on the mini player's 56-dip square up to 225 % scaling.
    private const int ArtPixels = 128;

    /// The song whose cover is asked for, and its cover once come.
    private static string? artFor;
    private static ImageSource? ownArt;

    /// Bloom's own song's cover came (a moment after the song began).
    public static event Action? ArtCame;

    /// Which source a view shows: another app's song only while Bloom plays nothing itself the direct way (`external`:
    /// another player is chosen, and it is Bloom's side); null: Bloom's own.
    public static MediaNow? Pick(bool external, bool playing, bool paused, MediaNow? other) => external || playing || paused ? null : other;

    /// The source shown now.
    public static MediaNow? Shown => Pick(MusicPlayer.External, MusicPlayer.IsPlaying, MusicPlayer.IsPaused, OtherApps.Current);

    /// A button of the shown source: the app's own (its session), else Bloom's player.
    public static void Press(MediaNow? other, PlayerOp op)
    {
        if (other is not null)
        {
            OtherApps.Press(other, op);
            return;
        }
        switch (op)
        {
            case PlayerOp.Toggle: MusicPlayer.Toggle(); break;
            case PlayerOp.Previous: MusicPlayer.Previous(); break;
            case PlayerOp.Next: MusicPlayer.Next(); break;
        }
    }

    /// The shown source's cover: the app's, or Bloom's song's (asked for when the song changed; ArtCame when it comes).
    public static ImageSource? ArtOf(MediaNow? other) => other is null ? OwnArt() : OtherApps.ArtOf(other);

    private static ImageSource? OwnArt()
    {
        string? song = MusicPlayer.External ? null : MusicPlayer.Queue.Current?.Path;
        if (!string.Equals(song, artFor, StringComparison.OrdinalIgnoreCase))
        {
            artFor = song;
            ownArt = null;
            if (song is not null)
                Icons.Art(song, ArtPixels, img =>
                {
                    if (!string.Equals(song, artFor, StringComparison.OrdinalIgnoreCase)) return;
                    ownArt = img;
                    ArtCame?.Invoke();
                });
        }
        return ownArt;
    }

    /// Another app's song as a view shows it, with its cover.
    public static Song Of(MediaNow other, ImageSource? cover) =>
        new(other, OtherApps.NameOf(other.App), other.Title, other.Artist, true, other.Status == MediaStatus.Playing,
            other.CanPrevious, other.CanToggle, other.CanNext, cover);

    /// What a view shows now: the shown source's song, its buttons and cover.
    public static Song Now()
    {
        if (Shown is { } other) return Of(other, OtherApps.ArtOf(other));
        string app = MusicPlayer.External ? PlayerWays.NameOf(MusicPlayer.Way) : "";
        return new(null, app, MusicPlayer.Title, MusicPlayer.Artist, MusicPlayer.HasSong, MusicPlayer.IsPlaying, true, true, true, OwnArt());
    }
}
