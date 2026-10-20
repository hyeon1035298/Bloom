using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Music;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set player`: the mini player's screens and a log (player.txt). BLOOM_QA_AUDIO is a folder with `a.wav` and `b.wav`
/// (short silent files; the default is %TEMP%\bloom-player-test: bloom-test-silence-1.wav and -2.wav). Direct playing runs on
/// the silent files: the file opens and plays, its position moves, the queue goes on. The media sessions Windows lists are only
/// read. Another player is only asked how it stands — unless BLOOM_QA_OPS names it (`foobar,musicbee,jriver`): then
/// the two files are played, queued, paused, resumed, skipped and stopped through it, the answer after each step logged.
/// That moves the player (and its playlist), so it is a run of its own, with the player's owner told.
internal static partial class Captures
{
    private static string PlayerLine() => (MusicPlayer.Artist.Length > 0 ? MusicPlayer.Artist + " — " : "") + MusicPlayer.Title;

    private static async Task PlayerScreens(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        string audio = Environment.GetEnvironmentVariable("BLOOM_QA_AUDIO") ?? Path.Combine(Path.GetTempPath(), "bloom-player-test");
        string one = Path.Combine(audio, File.Exists(Path.Combine(audio, "a.wav")) ? "a.wav" : "bloom-test-silence-1.wav");
        string two = Path.Combine(audio, File.Exists(Path.Combine(audio, "b.wav")) ? "b.wav" : "bloom-test-silence-2.wav");

        log.AppendLine("media sessions Windows lists (read only):");
        foreach (var s in await MediaSession.AllAsync())
            log.AppendLine($"  {s.App} | {s.Status} | {s.Artist} — {s.Title} | position {s.Position:g} / {s.Length:g} | toggle={s.CanToggle} next={s.CanNext} previous={s.CanPrevious} stop={s.CanStop}");

        // direct: the file opens, plays and the next one follows
        Settings.Current.PlayerMode = PlayerWays.Direct;
        MusicPlayer.ModeChanged();
        MusicPlayer.Play(one);
        for (int i = 0; i < 50 && !MusicPlayer.CanSeek; i++) await Task.Delay(100);
        var first = MusicPlayer.Position;
        await Task.Delay(1200);
        log.AppendLine($"direct: opened={MusicPlayer.CanSeek} length={MusicPlayer.Length.TotalSeconds:F1}s position {first.TotalSeconds:F2}s → {MusicPlayer.Position.TotalSeconds:F2}s playing={MusicPlayer.IsPlaying} volume={MusicPlayer.Volume}");
        MusicPlayer.Enqueue(two);
        log.AppendLine($"direct: queue {MusicPlayer.Queue.Items.Count} songs; ogg reject = {AudioFiles.Reject(Path.Combine(audio, "x.ogg"), true, _ => true)}; missing file reject = {AudioFiles.Reject(Path.Combine(audio, "missing.flac"), true, File.Exists)}");
        MusicPlayer.Toggle();
        log.AppendLine($"direct: after pause playing={MusicPlayer.IsPlaying}");
        MusicPlayer.Next();
        await Task.Delay(600);
        log.AppendLine($"direct: next → {MusicPlayer.Title} playing={MusicPlayer.IsPlaying}");

        // Explorer's 연결 프로그램 > Bloom on two selected files, a moment apart: the first plays, the second is queued
        MusicPlayer.Stop();
        MusicPlayer.Queue.Clear();
        bool handedOne = host.OpenHanded(one), handedTwo = host.OpenHanded(two);
        for (int i = 0; i < 50 && !MusicPlayer.CanSeek; i++) await Task.Delay(100);
        await Task.Delay(600);
        log.AppendLine($"handed over: {handedOne}/{handedTwo} → {MusicPlayer.Title} playing={MusicPlayer.IsPlaying} position {MusicPlayer.Position.TotalSeconds:F2}s · queue {string.Join(", ", MusicPlayer.Queue.Items.Select(t => t.Title))} · player window {host.Tools.Find("player") is not null}");
        MusicPlayer.Toggle();
        await Task.Delay(300);

        // 반복 · 무작위: the settings keep them; repeat all goes round, a song that ends under 한 곡 반복 plays again
        var (repeatWas, shuffleWas) = (MusicPlayer.Repeat, MusicPlayer.Shuffle);
        MusicPlayer.Repeat = Repeat.All;
        MusicPlayer.Shuffle = true;
        log.AppendLine($"modes: repeat={MusicPlayer.Repeat} ({Settings.Current.PlayerRepeat}) shuffle={MusicPlayer.Shuffle} ({Settings.Current.PlayerShuffle})");
        MusicPlayer.Queue.Index = MusicPlayer.Queue.Last;
        MusicPlayer.Next();
        await Task.Delay(600);
        log.AppendLine($"modes: next after the last → {MusicPlayer.Title} playing={MusicPlayer.IsPlaying}");
        MusicPlayer.Repeat = Repeat.One;
        string one1 = MusicPlayer.Title;
        for (int i = 0; i < 60 && MusicPlayer.Position < MusicPlayer.Length - TimeSpan.FromMilliseconds(300); i++) await Task.Delay(100);
        await Task.Delay(1200);
        log.AppendLine($"modes: repeat one at the end '{one1}' → '{MusicPlayer.Title}' position {MusicPlayer.Position.TotalSeconds:F2}s playing={MusicPlayer.IsPlaying}");

        // 재생목록: the queue saved, played back in place of it, added after it
        string listName = "QA 재생목록";
        string? saved = Playlists.Save(listName, MusicPlayer.Queue.Items);
        var read = saved is null ? null : Playlists.Read(saved);
        log.AppendLine($"playlist: saved={saved is not null} → {read?.Count} songs · listed {Playlists.All().Count(p => p.Name == listName)}");
        if (saved is not null)
        {
            MusicPlayer.OpenPlaylist(saved);
            await Task.Delay(400);
            log.AppendLine($"playlist: played → queue {MusicPlayer.Queue.Items.Count} · on '{MusicPlayer.Title}'");
            MusicPlayer.OpenPlaylist(saved, true);
            log.AppendLine($"playlist: added → queue {MusicPlayer.Queue.Items.Count}");
        }
        await Task.Delay(1500); // the tags
        log.AppendLine("tags: " + string.Join(" | ", MusicPlayer.Queue.Items.Take(2).Select(t => $"{t.Title} / {t.Artist} / {t.Album} / {t.Duration:F1}s")));

        // a song with tags and a cover in the file (tagged\tagged.mp3), one with the folder's (folderart\plain.wav + folder.jpg)
        string tagged = Path.Combine(audio, "tagged", "tagged.mp3"), plain = Path.Combine(audio, "folderart", "plain.wav");
        if (File.Exists(tagged))
        {
            var t = await Task.Run(() => AudioTags.Read(Track.Of(tagged)));
            log.AppendLine($"tags (file): {t.Title} / {t.Artist} / {t.Album} / {t.Duration:F1}s");
            foreach (string song in new[] { tagged, plain, one })
            {
                var got = new TaskCompletionSource<System.Windows.Media.ImageSource?>();
                Icons.Art(song, 128, img => got.TrySetResult(img));
                var img = await got.Task;
                log.AppendLine($"art: {Path.GetFileName(song)} → {(img is null ? "none" : $"{img.Width:F0}×{img.Height:F0}")}");
            }
            MusicPlayer.Play(tagged);
            await Task.Delay(1000);
            log.AppendLine($"tagged on: {PlayerLine()} · album {MusicPlayer.Album}");
        }

        host.Tools.Open("player");
        w.SetPinned(true);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.Go(Mode.Root);
            await Task.Delay(300);
            await Group(host, w, dir, $"{theme}-p1-player-direct");
        }
        if (host.Tools.Find("player")?.Tool is PlayerTool shown)
        {
            Theme.Apply("dark");
            shown.ShowShelf(true);
            await Task.Delay(300);
            await Group(host, w, dir, "dark-p1b-player-lists");
            shown.ShowShelf(false);
        }
        if (saved is not null) System.IO.File.Delete(saved);
        (MusicPlayer.Repeat, MusicPlayer.Shuffle) = (repeatWas, shuffleWas);
        Theme.Apply("dark");
        // Ctrl+K on an audio file result
        w.Go(Mode.Root, "");
        var file = host.Engine.FromPath(one, false, null);
        w.OpenActions(file);
        await Snap(w, dir, "dark-p2-audio-actions");
        w.CloseActionsForQa();
        log.AppendLine("Ctrl+K on an audio file: " + string.Join(" | ", file.MoreActions!().Select(a => a.Title)));

        // another player: how it stands now (read only), then — only when asked for — the operations
        string[] ops = (Environment.GetEnvironmentVariable("BLOOM_QA_OPS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string way in new[] { PlayerWays.Foobar, PlayerWays.MusicBee, PlayerWays.Jriver })
        {
            Settings.Current.PlayerMode = way;
            MusicPlayer.ModeChanged();
            var asked = DateTime.UtcNow;
            for (int i = 0; i < 100 && MusicPlayer.Note is null && !MusicPlayer.HasSong; i++) await Task.Delay(100); // the answer, or why none
            log.AppendLine($"{way}: answered in {(DateTime.UtcNow - asked).TotalSeconds:F1}s");
            log.AppendLine($"{way}: read-only: song={MusicPlayer.HasSong} '{PlayerLine()}' playing={MusicPlayer.IsPlaying} note={MusicPlayer.Note}");
            await Group(host, w, dir, $"dark-p3-player-{way}");
            if (!ops.Contains(way)) continue;
            async Task Step(string name, Action act, int ms = 2200)
            {
                act();
                await Task.Delay(ms);
                log.AppendLine($"{way}: {name} → song={MusicPlayer.HasSong} '{PlayerLine()}' playing={MusicPlayer.IsPlaying} via={MusicPlayer.Via} note={MusicPlayer.Note}");
            }
            await Step("play a", () => MusicPlayer.Play(one), 3500);
            await Group(host, w, dir, $"dark-p4-player-{way}-after-play");
            await Step("add b", () => MusicPlayer.Enqueue(two));
            await Step("toggle", MusicPlayer.Toggle);
            await Step("toggle", MusicPlayer.Toggle);
            if (way != PlayerWays.Jriver) // JRiver's next / previous would start the user's own songs around the test file
            {
                await Step("next", MusicPlayer.Next);
                await Step("previous", MusicPlayer.Previous);
            }
            await Step("stop", MusicPlayer.Stop);
        }
        Settings.Current.PlayerMode = PlayerWays.Direct;
        MusicPlayer.ModeChanged();
        host.Tools.Close(host.Tools.Find("player")!);

        // 설정 > 도구 > 미니 플레이어
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var sw = new SettingsWindow(host) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            sw.Show();
            sw.Filter(Tr("미니 플레이어", "Mini player"));
            await Task.Delay(400);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{theme}-p5-settings.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            sw.Close();
        }
        Theme.Apply("dark");
        host.Tools.Open("player");
        host.Tools.Close(host.Tools.Find("player")!);
        File.WriteAllText(Path.Combine(dir, "player.txt"), log.ToString());
    }
}
