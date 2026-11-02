using System;
using Bloom.App;
using Bloom.Core;
using Bloom.Music;

namespace Bloom.Tests;

/// The home's 플레이어 block (App\HomePlayer, Music\NowPlaying): when it is drawn again (the song's look, not its
/// position), which source it shows (the mini player's rule), when it asks Windows and the player how things stand, and
/// the other app's song standing in while the sessions come back.
internal static class HomePlayerTests
{
    private static MediaNow App(string app = "spotify", string title = "t", MediaStatus status = MediaStatus.Playing, int position = 30, bool next = true) =>
        new(app, title, "a", status, TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(200), true, next, true, true);

    private static NowPlaying.Song Own(bool has = true, bool playing = true, string title = "song") =>
        new(null, "", title, "artist", has, playing, true, true, true, null);

    public static void TheKeyLeavesThePositionOut()
    {
        string key = NowPlaying.Of(App(position: 30), null).Key;
        Check.Eq(NowPlaying.Of(App(position: 95), null).Key, key, "only the position moved: the same");
        Check.True(NowPlaying.Of(App(title: "other"), null).Key != key, "another song");
        Check.True(NowPlaying.Of(App(app: "chrome"), null).Key != key, "another app");
        Check.True(NowPlaying.Of(App(status: MediaStatus.Paused), null).Key != key, "paused");
        Check.True(NowPlaying.Of(App(next: false), null).Key != key, "a button the app no longer takes");
        var cover = new System.Windows.Media.DrawingImage();
        Check.True(NowPlaying.Of(App(), cover).Key != key, "its cover came");
        Check.True(Own().Key != Own(playing: false).Key, "Bloom's own: paused");
        Check.True(Own().Key != Own(has: false, title: "").Key, "nothing on");
        Check.True(Own().Key != NowPlaying.Of(App(title: "song"), null).Key, "the same words from an app");
    }

    public static void TheSourceIsTheMiniPlayersRule()
    {
        var other = App();
        Check.Eq(NowPlaying.Pick(false, true, false, other), null, "direct, Bloom playing: Bloom's");
        Check.Eq(NowPlaying.Pick(false, false, true, other), null, "direct, Bloom paused: Bloom's");
        Check.Eq(NowPlaying.Pick(false, false, false, other), other, "direct, Bloom stopped: the app's");
        Check.Eq(NowPlaying.Pick(false, false, false, null), null, "no app plays: Bloom's (nothing)");
        Check.Eq(NowPlaying.Pick(true, false, false, other), null, "another player chosen: its side");
    }

    public static void ItWatchesOnlyWhenSeen()
    {
        var on = new HomeWidget { Key = "player", On = true };
        Check.True(HomePlayer.ShouldWatch(true, on, true, false), "shown, on, open, the feature on");
        Check.True(!HomePlayer.ShouldWatch(false, on, true, false), "the launcher hidden");
        Check.True(!HomePlayer.ShouldWatch(true, new HomeWidget { Key = "player", On = false }, true, false), "the block off");
        Check.True(!HomePlayer.ShouldWatch(true, new HomeWidget { Key = "player", On = true, Collapsed = true }, true, false), "the block folded");
        Check.True(!HomePlayer.ShouldWatch(true, null, true, false), "no block");
        Check.True(!HomePlayer.ShouldWatch(true, on, false, false), "플레이어 off in 설정 > 기능");
        Check.True(!HomePlayer.ShouldWatch(true, on, true, true), "the recording mode");
    }

    public static void TheLastAppSongStandsInUntilTheSessionsAnswer()
    {
        var soon = TimeSpan.FromSeconds(0.4);
        var late = TimeSpan.FromSeconds(3);
        Check.True(HomePlayer.StandsIn(false, false, false, false, late), "nothing asks the sessions (about to show)");
        Check.True(HomePlayer.StandsIn(false, false, true, true, soon), "the block's watch just began");
        Check.True(!HomePlayer.StandsIn(false, false, true, true, late), "the sessions had time to answer");
        Check.True(!HomePlayer.StandsIn(false, false, true, false, soon), "the mini player watches: they are what they are");
        Check.True(!HomePlayer.StandsIn(true, false, false, false, soon), "Bloom plays a song");
        Check.True(!HomePlayer.StandsIn(false, true, false, false, soon), "another player chosen");
    }

    public static void TheBlockKeepsTheDefaults()
    {
        var kind = Array.Find(Home.Kinds, k => k.key == "player");
        Check.Eq(kind.key, "player", "a kind of its own");
        Check.True(!kind.on, "off by default");
        Check.Eq(kind.count, 0, "no rows");
        Check.Eq(Features.Of("player"), "player", "the player feature's");
    }
}
