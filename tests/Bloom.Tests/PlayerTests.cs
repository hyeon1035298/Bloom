using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Music;

namespace Bloom.Tests;

/// The mini player's pure parts: which files may be played, the queue's next / skip, each player's command line, JRiver's
/// web service (the address, the request, the answer), where a player program is, and which media session is whose.
internal static class PlayerTests
{
    private static Track T(string path, string title = "t") => new(path, title, "a", 10);

    // ── which files ─────────────────────────────────────────────────────────────────────────────
    public static void AudioKindsAreByExtension()
    {
        foreach (string ext in new[] { "flac", "mp3", "wav", "m4a", "aac", "wma", "ogg", "opus", "FLAC", "Mp3" })
            Check.True(AudioFiles.IsAudio(@"C:\Music\x." + ext), ext + " is audio");
        foreach (string name in new[] { "x.exe", "x.bat", "x.mp3.exe", "x.txt", "x", "x.md", "mp3", "x.flac.lnk" })
            Check.True(!AudioFiles.IsAudio(@"C:\Music\" + name), name + " is not audio");
        Check.True(AudioFiles.CanPlayDirect("x.M4A") && AudioFiles.CanPlayDirect("x.flac"), "direct kinds");
        Check.True(!AudioFiles.CanPlayDirect("x.ogg") && !AudioFiles.CanPlayDirect("x.opus"), "Media Foundation does not open ogg and opus");
    }

    public static void OnlyFullAudioPathsResolve()
    {
        Check.Eq(AudioFiles.Resolve(@"C:\Music\a.flac"), @"C:\Music\a.flac", "a full path");
        Check.Eq(AudioFiles.Resolve(@"C:\Music\Sub\..\b.wav"), @"C:\Music\b.wav", ".. is resolved");
        Check.Eq(AudioFiles.Resolve(@"\\server\share\a.mp3"), @"\\server\share\a.mp3", "a share");
        Check.Eq(AudioFiles.Resolve(@"D:\x y\(01)_[Artist]_→a→.wav"), @"D:\x y\(01)_[Artist]_→a→.wav", "spaces and symbols");
        Check.True(AudioFiles.Resolve(@"C:\Music\setup.exe") is null, "a program");
        Check.True(AudioFiles.Resolve(@"C:\Music\a.mp3.exe") is null, "an audio name that ends as a program");
        Check.True(AudioFiles.Resolve(@"C:\Music\a.mp3:evil.exe") is null, "an alternate data stream");
        Check.True(AudioFiles.Resolve(@"a.mp3") is null, "a relative path");
        Check.True(AudioFiles.Resolve(@"Music\a.mp3") is null, "a relative path with a folder");
        Check.True(AudioFiles.Resolve(@"\\?\C:\Music\a.mp3") is null, "a device path");
        Check.True(AudioFiles.Resolve("C:\\Music\\a\0.mp3") is null, "a NUL in the name");
        Check.True(AudioFiles.Resolve("") is null && AudioFiles.Resolve(null) is null, "empty");
    }

    public static void RejectGivesTheReason()
    {
        Check.Eq(AudioFiles.Reject(@"C:\Music\a.flac", true, _ => true), null, "playable");
        Check.True(AudioFiles.Reject(@"C:\Music\a.ogg", true, _ => true) is { } ogg && ogg.Contains(".ogg"), "ogg cannot be played directly");
        Check.Eq(AudioFiles.Reject(@"C:\Music\a.ogg", false, _ => true), null, "but another player can");
        Check.True(AudioFiles.Reject(@"C:\Music\a.mp3", true, _ => false) is not null, "a file that is gone");
        Check.True(AudioFiles.Reject(@"C:\Music\a.exe", false, _ => true) is not null, "not audio");
        Check.Eq(Track.Of(@"C:\Music\Dive.flac").Title, "Dive", "the title is the file name");
        Check.Eq(new Track(@"C:\a.mp3", "Dive", "Artist").Line, "Artist — Dive", "the line with an artist");
    }

    // ── the queue ───────────────────────────────────────────────────────────────────────────────
    private static PlayQueue Q(params string[] names)
    {
        var q = new PlayQueue();
        foreach (string n in names) q.Add(T(@"C:\Music\" + n, n));
        return q;
    }

    private static string? Skip(Track t) => t.Path.EndsWith(".ogg") ? "ogg" : t.Path.Contains("gone") ? "gone" : null;

    public static void NextSkipsWhatCannotPlay()
    {
        var q = Q("a.flac", "b.ogg", "gone.mp3", "c.mp3");
        q.Index = 0;
        var skipped = new List<(Track, string)>();
        Check.Eq(q.Find(1, Skip, skipped), 3, "the next that can play");
        Check.Eq(skipped.Count, 2, "two passed over");
        Check.Eq(skipped[0].Item2, "ogg", "the reason");
        Check.Eq(skipped[1].Item2, "gone", "the reason");
    }

    public static void NextAtTheEndAndPreviousAtTheStart()
    {
        var q = Q("a.flac", "b.mp3");
        q.Index = 1;
        Check.Eq(q.Find(1, Skip), -1, "nothing after the last");
        q.Index = 0;
        Check.Eq(q.Find(-1, Skip), -1, "nothing before the first");
        Check.Eq(q.Find(1, Skip), 1, "the next");
        q.Index = 1;
        Check.Eq(q.Find(-1, Skip), 0, "the one before");
        var rest = Q("a.ogg", "b.ogg");
        rest.Index = 0;
        var skipped = new List<(Track, string)>();
        Check.Eq(rest.Find(1, Skip, skipped), -1, "all that follow are skipped: none");
        Check.Eq(skipped.Count, 1, "and told");
    }

    public static void BeforeTheFirstSongTheNextIsTheFirst()
    {
        var q = Q("a.ogg", "b.flac");
        Check.Eq(q.Index, -1, "nothing on yet");
        Check.Eq(q.Find(1, Skip), 1, "the first that can play");
        Check.Eq(new PlayQueue().Find(1, Skip), -1, "an empty queue");
    }

    public static void PlayNowGoesAfterTheSongThatIsOn()
    {
        var q = Q("a.flac", "b.flac", "c.flac");
        q.Index = 0;
        int at = q.InsertNext(T(@"C:\Music\n.flac", "n"));
        Check.Eq(at, 1, "right after the one on");
        Check.Eq(q.Items[2].Title, "b.flac", "the rest follows");
        Check.Eq(new PlayQueue().InsertNext(T(@"C:\Music\n.flac")), 0, "into an empty queue");
        Check.Eq(q.Add(T(@"C:\Music\z.flac", "z")), 4, "add goes to the end");
    }

    public static void RemovingKeepsTheSongOn()
    {
        var q = Q("a.flac", "b.flac", "c.flac", "d.flac");
        q.Index = 2;
        Check.True(!q.RemoveAt(2), "the song on stays");
        Check.True(q.RemoveAt(0), "one before it goes");
        Check.Eq(q.Index, 1, "the index follows");
        Check.Eq(q.Current?.Title, "c.flac", "still the same song");
        Check.True(q.RemoveAt(2), "one after it goes");
        Check.Eq(q.Index, 1, "the index stays");
        Check.True(!q.RemoveAt(9), "out of range");
    }

    // ── the players' command lines ──────────────────────────────────────────────────────────────
    public static void FoobarCommandLines()
    {
        const string F = @"C:\Music\my song.flac";
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Play, F)!), F, "a bare file plays (one argument, the space is kept)");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Add, F)!), "/add|" + F, "/add appends");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Toggle)!), "/playpause", "play / pause");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Next)!), "/next", "next");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Previous)!), "/prev", "previous");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Stop)!), "/stop", "stop");
    }

    public static void MusicBeeCommandLines()
    {
        const string F = @"C:\Music\my song.flac";
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Play, F)!), "/Play|" + F, "/Play <file>");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Add, F)!), "/QueueLast|" + F, "/QueueLast <file>");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Toggle)!), "/PlayPause", "play / pause");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Next)!), "/Next", "next");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Previous)!), "/Previous", "previous");
        Check.Eq(string.Join("|", PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Stop)!), "/Stop", "stop");
    }

    public static void NoCommandLineWithoutAFileOrForOtherWays()
    {
        Check.True(PlayerCommands.Args(PlayerWays.Foobar, PlayerOp.Play) is null, "play needs a file");
        Check.True(PlayerCommands.Args(PlayerWays.MusicBee, PlayerOp.Add, "") is null, "add needs a file");
        Check.True(PlayerCommands.Args(PlayerWays.Direct, PlayerOp.Toggle) is null, "direct has no command line");
        Check.True(PlayerCommands.Args(PlayerWays.Jriver, PlayerOp.Next) is null, "JRiver has a web service instead");
    }

    public static void WaysAreKnownByKey()
    {
        Check.Eq(PlayerWays.Of("foobar"), "foobar", "a known way");
        Check.Eq(PlayerWays.Of("winamp"), "direct", "an unknown one falls back to direct");
        Check.Eq(PlayerWays.Of(null), "direct", "none");
        Check.True(!PlayerWays.IsExternal("direct") && PlayerWays.IsExternal("jriver") && PlayerWays.IsExternal("musicbee"), "which are another program");
        Check.Eq(PlayerWays.NameOf("musicbee"), "MusicBee", "the name shown");
    }

    // ── where the programs are ──────────────────────────────────────────────────────────────────
    private static string? Env(string name) => name switch
    {
        "ProgramFiles" => @"C:\Program Files",
        "ProgramFiles(x86)" => @"C:\Program Files (x86)",
        "LOCALAPPDATA" => @"C:\Users\u\AppData\Local",
        _ => null,
    };

    public static void ProgramPathsAreLookedForInOrder()
    {
        var all = PlayerExe.Candidates(PlayerWays.Foobar, @"""D:\fb\foobar2000.exe""", @"E:\run\foobar2000.exe", Env).ToList();
        Check.Eq(all[0], @"D:\fb\foobar2000.exe", "the setting first (quotes dropped)");
        Check.Eq(all[1], @"E:\run\foobar2000.exe", "then the running one");
        Check.Eq(all[2], @"C:\Program Files\foobar2000\foobar2000.exe", "then Program Files");
        Check.Eq(all[3], @"C:\Program Files (x86)\foobar2000\foobar2000.exe", "then Program Files (x86)");
        Check.Eq(all[4], @"C:\Users\u\AppData\Local\Programs\foobar2000\foobar2000.exe", "then the per-user folder");
        var bee = PlayerExe.Candidates(PlayerWays.MusicBee, "", null, Env).ToList();
        Check.Eq(bee.Count, 3, "no setting, nothing running: the three folders");
        Check.Eq(bee[1], @"C:\Program Files (x86)\MusicBee\MusicBee.exe", "MusicBee's usual folder");
        Check.Eq(PlayerExe.Find(PlayerWays.MusicBee, "", null, Env, p => p.Contains("(x86)")), @"C:\Program Files (x86)\MusicBee\MusicBee.exe", "the first that exists");
        Check.True(PlayerExe.Find(PlayerWays.Foobar, "", null, Env, _ => false) is null, "none exists");
        Check.Eq(PlayerExe.ProcessName(PlayerWays.MusicBee), "MusicBee", "the process to look for");
    }

    // ── JRiver's web service ────────────────────────────────────────────────────────────────────
    public static void McwsAddressIsNormalized()
    {
        Check.Eq(Mcws.Base(""), "http://localhost:52199/MCWS/v1/", "the default");
        Check.Eq(Mcws.Base(null), "http://localhost:52199/MCWS/v1/", "none: the default");
        Check.Eq(Mcws.Base("192.168.0.5:52199"), "http://192.168.0.5:52199/MCWS/v1/", "no scheme");
        Check.Eq(Mcws.Base("http://pc:52199/"), "http://pc:52199/MCWS/v1/", "a trailing slash");
        Check.Eq(Mcws.Base("http://pc:52199/MCWS/v1"), "http://pc:52199/MCWS/v1/", "already the service's address");
        Check.Eq(Mcws.Base("https://pc.example/jr"), "https://pc.example/jr/MCWS/v1/", "https and a path");
        Check.True(Mcws.Base("ftp://pc") is null, "not http");
        Check.True(Mcws.Base("a b c") is null, "not an address");
    }

    public static void McwsRequestsAreEscaped()
    {
        const string B = "http://localhost:52199/MCWS/v1/";
        Check.Eq(Mcws.Url(B, "Playback/Info"), B + "Playback/Info?Zone=-1", "the zone is the active one");
        Check.Eq(Mcws.Request(B, PlayerOp.Toggle), B + "Playback/PlayPause?Zone=-1", "play / pause");
        Check.Eq(Mcws.Request(B, PlayerOp.Next), B + "Playback/Next?Zone=-1", "next");
        Check.Eq(Mcws.Request(B, PlayerOp.Previous), B + "Playback/Previous?Zone=-1", "previous");
        Check.Eq(Mcws.Request(B, PlayerOp.Stop), B + "Playback/Stop?Zone=-1", "stop");
        Check.Eq(Mcws.Request(B, PlayerOp.Play, @"C:\Music\a b&c.flac"), B + "Playback/PlayByFilename?Filenames=C%3A%5CMusic%5Ca%20b%26c.flac&Location=Next&Zone=-1", "a file: every special character escaped, right after the song on");
        Check.Eq(Mcws.PlayByIndex(B, 7), B + "Playback/PlayByIndex?Index=7&Zone=-1", "start the song at a place");
        Check.Eq(Mcws.Request(B, PlayerOp.Add, @"C:\a.flac"), B + "Playback/PlayByFilename?Filenames=C%3A%5Ca.flac&Location=End&Zone=-1", "added at the end");
    }

    public static void McwsLoginIsBasic()
    {
        Check.Eq(Mcws.Basic("admin", "pw"), Convert.ToBase64String("admin:pw"u8.ToArray()), "user:password");
        Check.True(Mcws.Basic("", "pw") is null && Mcws.Basic(null, null) is null, "no user: no login");
        Check.Eq(Mcws.Basic("u", null), Convert.ToBase64String("u:"u8.ToArray()), "no password");
    }

    public static void McwsInfoIsRead()
    {
        const string Playing = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Response Status="OK"><Item Name="ZoneID">0</Item><Item Name="State">2</Item><Item Name="FileKey">42</Item><Item Name="Name">Dive &amp; Rise</Item><Item Name="Artist">Artist</Item><Item Name="PositionMS">65000</Item><Item Name="DurationMS">242500</Item></Response>
            """;
        var p = Mcws.ParseInfo(Playing);
        Check.True(p.Ok && p.Has && p.Playing && !p.Paused, "playing");
        Check.Eq(p.Title, "Dive & Rise", "the name, entities decoded");
        Check.Eq(p.Artist, "Artist", "the artist");
        Check.Eq(p.Position, TimeSpan.FromSeconds(65), "the position");
        Check.Eq(p.Length, TimeSpan.FromSeconds(242.5), "the length");
        Check.True(Mcws.ParseInfo("""<Response Status="OK"><Item Name="State">0</Item><Item Name="PlayingNowPosition">6</Item><Item Name="PlayingNowTracks">20</Item></Response>""") is { Index: 6, Tracks: 20 }, "the place in Playing Now");
        var paused = Mcws.ParseInfo("""<Response Status="OK"><Item Name="State">1</Item><Item Name="Name">x</Item></Response>""");
        Check.True(paused.Has && paused.Paused && !paused.Playing, "paused still has a song");
        var stopped = Mcws.ParseInfo("""<Response Status="OK"><Item Name="State">0</Item><Item Name="Name">x</Item></Response>""");
        Check.True(stopped.Ok && !stopped.Has && !stopped.Playing, "stopped: no song on");
        var byFile = Mcws.ParseInfo("""<Response Status="OK"><Item Name="State">2</Item><Item Name="Filename">C:\a\Dive.flac</Item></Response>""");
        Check.Eq(byFile.Title, "Dive", "no name: the file's");
        Check.True(!Mcws.ParseInfo("""<Response Status="Failure"/>""").Ok, "a failure");
        Check.True(!Mcws.ParseInfo("not xml").Ok, "not xml");
        Check.True(Mcws.Accepted("""<Response Status="OK"/>"""), "accepted");
        Check.True(!Mcws.Accepted("""<Response Status="Failure"/>""") && !Mcws.Accepted("<html>"), "refused");
    }

    // ── Windows' media sessions ─────────────────────────────────────────────────────────────────
    public static void SessionsBelongToTheirPlayer()
    {
        Check.True(MediaSession.Matches(PlayerWays.Foobar, "foobar2000.exe"), "foobar2000");
        Check.True(MediaSession.Matches(PlayerWays.Foobar, "Foobar2000.exe") && !MediaSession.Matches(PlayerWays.Foobar, "FreeTube.exe"), "case; another app");
        Check.True(MediaSession.Matches(PlayerWays.MusicBee, "MusicBee.exe"), "MusicBee");
        Check.True(MediaSession.Matches(PlayerWays.Jriver, "MediaCenter31.exe") && MediaSession.Matches(PlayerWays.Jriver, "JRiver.MediaCenter"), "JRiver");
        Check.True(!MediaSession.Matches(PlayerWays.Direct, "foobar2000.exe") && !MediaSession.Matches(PlayerWays.Foobar, null) && !MediaSession.Matches(PlayerWays.Foobar, ""), "direct has none; no id");
    }

    public static void MusicBeeTitleNamesTheSong()
    {
        Check.Eq(PlayerWindow.SongOf("Dive - MusicBee"), "Dive", "a song");
        Check.Eq(PlayerWindow.SongOf("Artist - Dive - MusicBee"), "Artist - Dive", "the song's own dashes stay");
        Check.Eq(PlayerWindow.SongOf("MusicBee"), "", "nothing on");
        Check.Eq(PlayerWindow.SongOf(" - MusicBee"), "", "an empty song");
        Check.Eq(PlayerWindow.SongOf("Other window"), "", "another window");
        Check.Eq(PlayerWindow.SongOf(null), "", "no title");
    }
}
