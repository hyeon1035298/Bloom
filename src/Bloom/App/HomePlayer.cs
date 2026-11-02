using System;
using Bloom.Core;
using Bloom.Music;
using Bloom.Ui;

namespace Bloom.App;

/// The home's 플레이어 block: what it shows, kept up while the launcher shows (Music\MusicPlayer.Watch) and never asked
/// for while it is hidden.
internal static class HomePlayer
{
    /// Windows' sessions come back this long after the watch starts again (OtherApps.Forget emptied them when it stopped):
    /// until then, and while nothing asks them (the launcher about to show), the other app's song last drawn stands in,
    /// so a reopened launcher does not flash "nothing playing".
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(1.5);

    private static Launcher? window;
    private static Action? later;
    private static bool demo, watching;
    private static DateTime watchedAt;
    private static string? drawn;
    private static NowPlaying.Song? lastOther, qa;

    /// Host, once, outside QA, after the launcher was built: `window` (watched only while it shows), `later` (the home
    /// drawn again soon), `demo` (the recording mode: a made-up song, nothing asked).
    public static void Start(Launcher window, Action later, bool demo)
    {
        HomePlayer.window = window;
        HomePlayer.later = later;
        HomePlayer.demo = demo;
        window.IsVisibleChanged += (_, _) => Sync();
        MusicPlayer.Changed += Changed;
        NowPlaying.ArtCame += Changed;
    }

    /// Whether the block asks how things stand: the launcher shows, the block is on and open, 플레이어 is on in 설정 > 기능,
    /// and it is not the recording mode.
    public static bool ShouldWatch(bool visible, HomeWidget? block, bool feature, bool demo) =>
        visible && block is { On: true, Collapsed: false } && feature && !demo;

    /// The watch held or let go as ShouldWatch says now (the launcher shown or hidden, the block drawn: just turned on in
    /// 홈 편집, or every change while watched: turned off or folded). The block's settings are read only while the
    /// launcher shows: hidden, the mini player alone still brings a change twice a second.
    public static void Sync()
    {
        if (window is null) return; // QA, or before Start: Prewarm's moment off-screen
        bool want = window.IsVisible && ShouldWatch(true, Home.Of("player"), Features.Shows("player"), demo);
        if (want == watching) return;
        watching = want;
        if (want) watchedAt = WatchStart(MusicPlayer.Watched, DateTime.UtcNow);
        MusicPlayer.Watch(want);
    }

    /// When the block's watch counts as begun for StandsIn: now, or long ago when the mini player already watched
    /// (Windows' sessions are current, nothing to wait for).
    public static DateTime WatchStart(bool watchedBefore, DateTime now) => watchedBefore ? DateTime.MinValue : now;

    /// Every change (twice a second while watched): the home drawn again only when what the block shows changed.
    private static void Changed()
    {
        Sync();
        if (!watching) return;
        string key = Now().Key;
        if (key == drawn) return;
        drawn = key;
        later?.Invoke();
    }

    /// What the block shows now: Bloom's song, another app's, nothing; in the recording mode a made-up song.
    public static NowPlaying.Song Now()
    {
        if (qa is not null) return qa;
        if (demo) return Demo;
        var now = NowPlaying.Now();
        bool standsIn = now.Other is null && StandsIn(now.Has, MusicPlayer.External, MusicPlayer.Watched, watching, DateTime.UtcNow - watchedAt);
        (var shown, lastOther) = Follow(now, lastOther, standsIn);
        return shown;
    }

    /// What shows and the other app's song kept for next time: an app's song is kept, its cover carried over while the
    /// same app's same song has none yet (the sessions just came back, its cover still asked for: no flash of the tile);
    /// with none, the kept one stands in (`standsIn`) or is let go.
    public static (NowPlaying.Song shown, NowPlaying.Song? last) Follow(NowPlaying.Song now, NowPlaying.Song? last, bool standsIn)
    {
        if (now.Other is { } other)
        {
            if (now.Cover is null && last is { Other: { } was, Cover: { } cover } && was.App == other.App && was.Title == other.Title && was.Artist == other.Artist)
                now = now with { Cover = cover };
            return (now, now);
        }
        if (!standsIn) return (now, null);
        return (last ?? now, last);
    }

    /// Whether the other app's song last drawn stands in for "nothing playing": Bloom plays nothing the direct way and
    /// Windows' sessions have not answered yet (`asked`: something watches them; `mine`: the block does, since `since`).
    public static bool StandsIn(bool has, bool external, bool asked, bool mine, TimeSpan since) =>
        !has && !external && (!asked || (mine && since < Settling));

    /// The recording mode's song: made up, its buttons steer nothing.
    private static NowPlaying.Song Demo => new(null, "", Tr("한낮의 정원", "Garden at Noon"), "Bloom Ensemble", true, true, true, true, true, null);

    /// Whether the buttons do anything (not in the recording mode, not on a QA picture's made-up song).
    public static bool Live => qa is null && !demo;

    /// QA: the block shows this song (null: what plays).
    public static void SetForQa(NowPlaying.Song? song) => qa = song;

    /// QA's log: whether the player is still watched once the launcher hid.
    public static bool Watching => watching;
}
