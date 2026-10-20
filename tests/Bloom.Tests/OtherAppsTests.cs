using System;
using Bloom.Music;

namespace Bloom.Tests;

/// 다른 앱 in the mini player: an app's name from its id when the Start menu knows none, where its song is between two
/// answers, and Windows' repeat modes as Bloom's 반복.
internal static class OtherAppsTests
{
    public static void AppNamesFromTheirIds()
    {
        Check.Eq(OtherApps.CleanName("Spotify.exe"), "Spotify", "a program's name");
        Check.Eq(OtherApps.CleanName("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"), "Spotify", "a Store app: the part after !");
        Check.Eq(OtherApps.CleanName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"), "ZuneMusic", "its last dotted part");
        Check.Eq(OtherApps.CleanName(@"{6D809377-6AF0-444B-8957-A3773F02200E}\VideoLAN\VLC\vlc.exe"), "Vlc", "a path id: the program");
        Check.Eq(OtherApps.CleanName("chrome"), "Chrome", "capitalized");
        Check.Eq(OtherApps.CleanName("308046B0AF4A39CB"), "308046B0AF4A39CB", "a hash stays as it is");
        Check.Eq(OtherApps.CleanName("!"), "!", "nothing left: the id");
    }

    private static MediaNow Song(MediaStatus status, int position, int length, DateTime at) =>
        new("app", "t", "a", status, TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(length), true, true, true, true) { At = at };

    public static void ThePositionMovesOnWhileItPlays()
    {
        var at = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Check.Eq(OtherApps.PositionOf(Song(MediaStatus.Playing, 30, 200, at), at.AddSeconds(5)), TimeSpan.FromSeconds(35), "five seconds on");
        Check.Eq(OtherApps.PositionOf(Song(MediaStatus.Paused, 30, 200, at), at.AddSeconds(5)), TimeSpan.FromSeconds(30), "paused: where it stopped");
        Check.Eq(OtherApps.PositionOf(Song(MediaStatus.Playing, 198, 200, at), at.AddSeconds(9)), TimeSpan.FromSeconds(200), "never past the end");
        Check.Eq(OtherApps.PositionOf(Song(MediaStatus.Playing, 30, 0, at), at.AddSeconds(5)), TimeSpan.FromSeconds(35), "no length known: still moves");
        Check.Eq(OtherApps.PositionOf(Song(MediaStatus.Playing, 30, 200, at), at.AddSeconds(-3)), TimeSpan.FromSeconds(30), "a clock behind the app's: no going back");
    }

    public static void RepeatModesAreBloomsRepeat()
    {
        Check.Eq(MediaSession.RepeatOfMode(0), Repeat.Off, "none");
        Check.Eq(MediaSession.RepeatOfMode(1), Repeat.One, "track");
        Check.Eq(MediaSession.RepeatOfMode(2), Repeat.All, "list");
        Check.Eq(MediaSession.RepeatOfMode(7), Repeat.Off, "unknown");
        foreach (var r in new[] { Repeat.Off, Repeat.All, Repeat.One })
            Check.Eq(MediaSession.RepeatOfMode(MediaSession.ModeOf(r)), r, "round trip " + r);
    }
}
