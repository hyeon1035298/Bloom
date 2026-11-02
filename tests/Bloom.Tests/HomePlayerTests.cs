using System;
using Bloom.App;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;
using Bloom.Ui;

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

    public static void TheMiniPlayersWatchNeedsNoSettling()
    {
        var now = DateTime.UtcNow;
        Check.True(!HomePlayer.StandsIn(false, false, true, true, now - HomePlayer.WatchStart(true, now)), "the mini player already watched: current");
        Check.True(HomePlayer.StandsIn(false, false, true, true, now - HomePlayer.WatchStart(false, now)), "the block's own watch just began");
    }

    public static void TheLastAppSongIsKeptAndLetGo()
    {
        var cover = new System.Windows.Media.DrawingImage();
        var song = NowPlaying.Of(App(), cover);
        var nothing = Own(has: false, playing: false, title: "");

        var (shown, last) = HomePlayer.Follow(song, null, false);
        Check.True(ReferenceEquals(shown, song) && ReferenceEquals(last, song), "an app's song shows and is kept");
        (shown, last) = HomePlayer.Follow(nothing, song, true);
        Check.True(ReferenceEquals(shown, song) && ReferenceEquals(last, song), "the sessions not back yet: it stands in");
        (shown, last) = HomePlayer.Follow(nothing, null, true);
        Check.True(ReferenceEquals(shown, nothing) && last is null, "nothing kept: nothing playing");
        (shown, last) = HomePlayer.Follow(nothing, song, false);
        Check.True(ReferenceEquals(shown, nothing) && last is null, "the sessions answered nothing: let go");

        var back = NowPlaying.Of(App(position: 95), null);
        (shown, last) = HomePlayer.Follow(back, song, false);
        Check.True(ReferenceEquals(shown.Cover, cover) && ReferenceEquals(last!.Cover, cover), "back without its cover yet: the kept one carries over");
        Check.Eq(shown.Key, song.Key, "so the block is not drawn again");
        Check.True(ReferenceEquals(shown.Other, back.Other), "the buttons steer the session that came back");
        (shown, _) = HomePlayer.Follow(NowPlaying.Of(App(title: "next"), null), song, false);
        Check.Eq(shown.Cover, null, "another song: no cover carried over");
        (shown, _) = HomePlayer.Follow(NowPlaying.Of(App(app: "chrome"), null), song, false);
        Check.Eq(shown.Cover, null, "another app: no cover carried over");
        var own = new System.Windows.Media.DrawingImage();
        (shown, _) = HomePlayer.Follow(NowPlaying.Of(App(), own), song, false);
        Check.True(ReferenceEquals(shown.Cover, own), "its own cover came: that one");
    }

    /// The kept home's key (Ui\Launcher.SectionKey) over the block as drawn: the buttons' look changes it, the position not.
    public static void TheKeptKeyFollowsWhatShows()
    {
        static string Key(NowPlaying.Song s) =>
            Launcher.SectionKey(new Section("플레이어") { HomeKey = "player", Widget = () => PlayerViews.Block(s, () => { }, _ => { }) }, out _);
        // a faded button differs by its glyph's colour: the palette's brushes are all alike until a theme paints them
        if (Theme.P.TextDim.Color == Theme.P.TextFaint.Color) Theme.P.Apply(false, null, 0);
        string key = Key(NowPlaying.Of(App(position: 30), null));
        Check.Eq(Key(NowPlaying.Of(App(position: 95), null)), key, "only the position moved: kept");
        Check.True(Key(NowPlaying.Of(App(status: MediaStatus.Paused), null)) != key, "paused: play shows instead of pause");
        Check.True(Key(NowPlaying.Of(App(next: false), null)) != key, "next no longer taken: its button faded");
        Check.True(Key(NowPlaying.Of(App(title: "other"), null)) != key, "another song");
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
