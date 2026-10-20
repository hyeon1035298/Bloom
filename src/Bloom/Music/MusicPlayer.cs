using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Music;

/// Segoe Fluent Icons of the player (G has the rest).
internal static class PlayerGlyph
{
    public const char Music = '\uE8D6', Play = '\uE768', Pause = '\uE769', Next = '\uE893', Previous = '\uE892', Volume = '\uE767', Mute = '\uE74F';
}

/// 미니 플레이어's player, one for the whole app (the window is only a view of it; closing it leaves the music on) and
/// the one place that plays a file: `Play` and `Enqueue`. Four ways (설정 > 미니 플레이어): 직접 — Bloom opens the file
/// itself (WPF MediaPlayer = Media Foundation, read only; the queue is Bloom's own); JRiver — its web service (MCWS);
/// foobar2000 and MusicBee — their command lines, the song and the state read from Windows' media session (foobar2000)
/// or the window's title (MusicBee). A music file is only ever read, by Bloom or by the player. UI thread only.
internal static class MusicPlayer
{
    public static readonly PlayQueue Queue = new();

    /// The song, the state or the position changed (also every half second while a view is watching).
    public static event Action? Changed;

    public static string Way => PlayerWays.Of(Settings.Current.PlayerMode);

    public static bool External => PlayerWays.IsExternal(Way);

    // ── direct ──────────────────────────────────────────────────────────────────────────────────
    private static MediaPlayer? media;
    private static bool playing, paused, opened;
    private static TimeSpan length;

    // ── another player: what it last said ───────────────────────────────────────────────────────
    private static HttpClient? http;
    private static bool polling, exHas, exPlaying;
    private static string exTitle = "", exArtist = "";
    private static TimeSpan exPosition, exLength;
    private static DateTime exAt;
    private static string? jrAuth;
    private static bool jrAuthRead;

    /// A line under the song: why nothing plays, what was skipped.
    public static string? Note { get; private set; }

    /// How the last request reached another player: "media session", "command line" or "MCWS" (QA's log).
    public static string? Via { get; private set; }

    // ── what the view shows ─────────────────────────────────────────────────────────────────────
    public static bool HasSong => External ? exHas : Queue.Current is not null;

    public static bool IsPlaying => External ? exHas && exPlaying : playing;

    public static string Title => External ? exTitle : Queue.Current?.Title ?? "";

    public static string Artist => External ? exArtist : Queue.Current?.Artist ?? "";

    public static TimeSpan Length => External ? exLength : length;

    public static TimeSpan Position
    {
        get
        {
            if (External) return exPlaying && exLength > TimeSpan.Zero ? Min(exPosition + (DateTime.UtcNow - exAt), exLength) : exPosition;
            return media is null || !opened ? TimeSpan.Zero : media.Position;
        }
    }

    /// Dragging the progress bar works when Bloom plays the file itself.
    public static bool CanSeek => !External && opened && length > TimeSpan.Zero;

    /// The volume is Bloom's own only when it plays (the others have their own).
    public static bool CanVolume => !External;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => b > TimeSpan.Zero && a > b ? b : a;

    public static int Volume
    {
        get => Math.Clamp(Settings.Current.PlayerVolume, 0, 100);
        set
        {
            Settings.Current.PlayerVolume = Math.Clamp(value, 0, 100);
            if (media is not null) media.Volume = Volume / 100.0;
        }
    }

    // ── watching (a view is open) ───────────────────────────────────────────────────────────────
    private static DispatcherTimer? timer;
    private static int watchers, ticks;

    /// A view starts or stops watching: while one does, Changed comes every half second (and another player is asked every second).
    public static void Watch(bool on)
    {
        watchers = Math.Max(0, watchers + (on ? 1 : -1));
        if (watchers == 0)
        {
            timer?.Stop();
            return;
        }
        timer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick -= OnTick;
        timer.Tick += OnTick;
        timer.Start();
        if (External) _ = Poll();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (External && ++ticks % 2 == 0) _ = Poll();
        Changed?.Invoke();
    }

    /// The settings' way of playing changed (or the JRiver address, user or password): what the other way was playing
    /// stops (another player's own music is left alone).
    public static void ModeChanged()
    {
        StopDirect();
        Note = null;
        exHas = exPlaying = false;
        exTitle = exArtist = "";
        exPosition = exLength = TimeSpan.Zero;
        jrAuthRead = false;
        if (External) _ = Poll();
        Changed?.Invoke();
    }

    /// JRiver's address, user or password changed in the settings: it is asked again with them.
    public static void JriverChanged()
    {
        jrAuthRead = false;
        if (Way != PlayerWays.Jriver) return;
        Note = null;
        _ = Poll();
    }

    // ── actions ─────────────────────────────────────────────────────────────────────────────────
    /// Plays the file now. Direct: it goes in right after the song that is on, the queue around it stays. Another
    /// player: its own "play this file".
    public static void Play(string path) => Play(Track.Of(path));

    public static void Play(Track t)
    {
        if (Reject(t) is { } why)
        {
            Skipped(new List<(Track, string)> { (t, why) }, false);
            return;
        }
        Note = null;
        if (External)
        {
            exTitle = t.Title;
            exArtist = t.Artist;
            _ = Run(PlayerOp.Play, AudioFiles.Resolve(t.Path));
            return;
        }
        Start(Queue.InsertNext(t));
    }

    /// At the end of the queue; when nothing is on (direct) it begins. Another player: its own "add to the queue".
    public static void Enqueue(string path) => Enqueue(Track.Of(path));

    public static void Enqueue(Track t)
    {
        if (Reject(t) is { } why)
        {
            Skipped(new List<(Track, string)> { (t, why) }, false);
            return;
        }
        Note = null;
        if (External)
        {
            _ = Run(PlayerOp.Add, AudioFiles.Resolve(t.Path));
            return;
        }
        Queue.Add(t);
        if (!playing && !paused) Advance(1);
        else Changed?.Invoke();
    }

    public static void Toggle()
    {
        if (External)
        {
            _ = Run(PlayerOp.Toggle, null);
            return;
        }
        if (media is not null && playing)
        {
            media.Pause();
            playing = false;
            paused = true;
        }
        else if (media is not null && paused)
        {
            media.Play();
            playing = true;
            paused = false;
        }
        else if (Queue.Current is not null) Start(Queue.Index); // it ended: again
        else if (Queue.Items.Count > 0) Advance(1);
        Changed?.Invoke();
    }

    public static void Next()
    {
        if (External) _ = Run(PlayerOp.Next, null);
        else Advance(1);
    }

    /// The song before; more than 3 seconds into this one (or nothing before), this one from its start.
    public static void Previous()
    {
        if (External)
        {
            _ = Run(PlayerOp.Previous, null);
            return;
        }
        if (Position > TimeSpan.FromSeconds(3) || Queue.Index <= 0)
        {
            Seek(0);
            return;
        }
        Advance(-1);
    }

    /// Stops playing (direct: Bloom's own; another player: its stop button).
    public static void Stop()
    {
        if (External)
        {
            _ = Run(PlayerOp.Stop, null);
            return;
        }
        StopDirect();
        Changed?.Invoke();
    }

    /// Direct: to a place in the song, 0 … 1.
    public static void Seek(double fraction)
    {
        if (media is null || !opened) return;
        media.Position = length > TimeSpan.Zero ? TimeSpan.FromTicks((long)(length.Ticks * Math.Clamp(fraction, 0, 1))) : TimeSpan.Zero;
        Changed?.Invoke();
    }

    /// Direct: the song at that place of the queue.
    public static void PlayAt(int i)
    {
        if (External || i < 0 || i >= Queue.Items.Count) return;
        if (Reject(Queue.Items[i]) is { } why)
        {
            Skipped(new List<(Track, string)> { (Queue.Items[i], why) }, false);
            return;
        }
        Start(i);
    }

    public static void Remove(int i)
    {
        if (Queue.RemoveAt(i)) Changed?.Invoke();
    }

    /// Everything but the song that is on.
    public static void ClearRest()
    {
        if (Queue.Current is { } now)
        {
            Queue.Clear();
            Queue.Add(now);
            Queue.Index = 0;
        }
        else Queue.Clear();
        Changed?.Invoke();
    }

    // ── direct playing ──────────────────────────────────────────────────────────────────────────
    private static string? Reject(Track t) => AudioFiles.Reject(t.Path, !External, File.Exists);

    private static MediaPlayer Media()
    {
        if (media is not null) return media;
        media = new MediaPlayer { Volume = Volume / 100.0 };
        media.MediaOpened += (_, _) =>
        {
            opened = true;
            if (media!.NaturalDuration.HasTimeSpan) length = media.NaturalDuration.TimeSpan;
            Changed?.Invoke();
        };
        media.MediaEnded += (_, _) => Advance(1);
        media.MediaFailed += (_, e) =>
        {
            var t = Queue.Current;
            playing = paused = false;
            if (t is not null) Skipped(new List<(Track, string)> { (t, Tr("열 수 없는 파일이에요", "The file cannot be opened") + (e.ErrorException is { } ex ? " · " + ex.Message : "")) }, true);
            Advance(1);
        };
        return media;
    }

    /// Plays the song at that place (already checked: an audio file Media Foundation opens, there).
    private static void Start(int i)
    {
        Queue.Index = i;
        var t = Queue.Items[i];
        var m = Media();
        length = t.Duration > 0 ? TimeSpan.FromSeconds(t.Duration) : TimeSpan.Zero;
        opened = false;
        playing = true;
        paused = false;
        m.Open(new Uri(AudioFiles.Resolve(t.Path)!));
        m.Volume = Volume / 100.0;
        m.Play();
        Changed?.Invoke();
    }

    /// The next (1) or previous (-1) song that can be played; what was passed over is told once. None: stops at the end.
    private static void Advance(int step)
    {
        var skipped = new List<(Track, string)>();
        int i = Queue.Find(step, Reject, skipped);
        Skipped(skipped, true);
        if (i >= 0) Start(i);
        else if (step > 0)
        {
            StopDirect();
            if (Queue.Items.Count > 0) Queue.Index = Queue.Items.Count - 1;
            Changed?.Invoke();
        }
        else Seek(0);
    }

    private static void StopDirect()
    {
        media?.Stop();
        playing = paused = opened = false;
    }

    /// One card for a run of songs that were not played (and the line under the song).
    private static void Skipped(List<(Track track, string reason)> list, bool passedOver)
    {
        if (list.Count == 0) return;
        string title = list.Count == 1 ? list[0].track.Line : Tr($"{list.Count}곡", $"{list.Count} songs");
        string head = passedOver ? Tr("건너뛰었어요", "Skipped") : Tr("재생하지 않았어요", "Not played");
        Note = list[0].reason;
        Notices.Result(false, Tr("미니 플레이어", "Mini player"), $"{head} · {title}", list[0].reason);
        Changed?.Invoke();
    }

    // ── another player ──────────────────────────────────────────────────────────────────────────
    /// Sends one request to the player and asks again how it stands a moment later (it needs that long to answer).
    private static async Task Run(PlayerOp op, string? file)
    {
        string way = Way;
        bool ok = way == PlayerWays.Jriver ? await JriverRun(op, file) : await CommandRun(way, op, file);
        if (ok)
        {
            Note = null;
            if (way != PlayerWays.Jriver) Guess(op);
        }
        Changed?.Invoke();
        await Task.Delay(600);
        await Poll();
    }

    /// What a player that does not say whether it plays (MusicBee) is taken to do after a command of ours.
    private static void Guess(PlayerOp op)
    {
        exPlaying = op switch { PlayerOp.Toggle => !exPlaying, PlayerOp.Stop => false, PlayerOp.Add => exPlaying, _ => true };
        exAt = DateTime.UtcNow;
    }

    private static string Name => PlayerWays.NameOf(Way);

    private static bool IsRunning(string way) => Process.GetProcessesByName(PlayerExe.ProcessName(way)).Length > 0;

    /// The player program's path (the settings', the running one's, the usual folders'), or null.
    public static string? FindExe(string way)
    {
        string? setting = way == PlayerWays.MusicBee ? Settings.Current.MusicBeePath : Settings.Current.FoobarPath;
        string? running = null;
        foreach (var p in Process.GetProcessesByName(PlayerExe.ProcessName(way)))
        {
            try
            {
                running ??= p.MainModule?.FileName;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            p.Dispose();
        }
        return PlayerExe.Find(way, setting, running, Environment.GetEnvironmentVariable, File.Exists);
    }

    /// foobar2000 and MusicBee: a button of the player's Windows media session when it has one, else the program's command line.
    private static async Task<bool> CommandRun(string way, PlayerOp op, string? file)
    {
        bool open = op is PlayerOp.Play or PlayerOp.Add; // the others are for a player that is on
        if (!open)
        {
            if (!IsRunning(way))
            {
                Note = Tr($"{Name}이(가) 꺼져 있어요 — 곡을 고르면 함께 켜져요", $"{Name} is not running — picking a song starts it");
                return false;
            }
            if (await MediaSession.ControlAsync(way, op))
            {
                Via = "media session";
                return true;
            }
        }
        string? exe = FindExe(way);
        if (exe is null)
        {
            Note = Tr($"{Name}을(를) 찾지 못했어요 — 설정 > 미니 플레이어에서 실행 파일 경로를 알려 주세요", $"Could not find {Name} — set its program path in Settings > Mini player");
            return false;
        }
        if (PlayerCommands.Args(way, op, file) is not { } args) return false;
        try
        {
            await Task.Run(() =>
            {
                // no CreateNoWindow: a MusicBee started with it stopped answering the command lines that followed (seen once)
                var start = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (string a in args) start.ArgumentList.Add(a);
                Process.Start(start)?.Dispose();
            });
            Via = "command line";
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Note = Tr($"{Name}을(를) 실행하지 못했어요 · {e.Message}", $"Could not start {Name} · {e.Message}");
            Log.Write("player: " + e.Message);
            return false;
        }
    }

    /// Asks the player how it stands (the song, the state) and keeps the answer for the view.
    public static async Task Poll()
    {
        if (!External || polling) return;
        polling = true;
        string way = Way;
        try
        {
            if (way == PlayerWays.Jriver) await JriverPoll();
            else await SessionPoll(way);
        }
        finally
        {
            polling = false;
        }
        Changed?.Invoke();
    }

    /// foobar2000 and MusicBee: the song and the state from the media session; MusicBee has none, its window's title names the song.
    private static async Task SessionPoll(string way)
    {
        var s = await MediaSession.ReadAsync(way);
        if (way != Way) return; // the way was changed meanwhile
        if (s is not null)
        {
            Note = null;
            exHas = s.Title.Length > 0;
            exTitle = s.Title;
            exArtist = s.Artist;
            exPlaying = s.Status == MediaStatus.Playing;
            exPosition = s.Position;
            exLength = s.Length;
            exAt = DateTime.UtcNow;
            return;
        }
        string title = way == PlayerWays.MusicBee ? await Task.Run(() => PlayerWindow.MusicBeeTitle()) : "";
        if (way != Way) return;
        if (title.Length > 0)
        {
            Note = null;
            exHas = true;
            exTitle = title;
            exArtist = "";
            exPosition = exLength = TimeSpan.Zero;
            return;
        }
        bool running = IsRunning(way);
        exHas = false;
        exPlaying = running && exPlaying; // a player that does not say whether it plays: what we sent holds while it is on (it is starting)
        exTitle = exArtist = "";
        exPosition = exLength = TimeSpan.Zero;
        Note = running ? Tr($"{Name}이(가) 지금 곡을 알려 주지 않아요", $"{Name} does not tell which song is on") : Tr($"{Name}이(가) 꺼져 있어요", $"{Name} is not running");
    }

    // ── JRiver (MCWS) ───────────────────────────────────────────────────────────────────────────
    private static HttpClient Http() => http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

    /// The Authorization header's value: the user and the stored password (DPAPI, ApiKeys), read once until the settings change.
    private static string? JriverAuth()
    {
        if (jrAuthRead) return jrAuth;
        jrAuth = Mcws.Basic(Settings.Current.JriverUser, ApiKeys.Get(JriverKey));
        jrAuthRead = true;
        return jrAuth;
    }

    /// The name the JRiver password is stored under in ApiKeys.
    public const string JriverKey = "jriver-mcws";

    /// The answer's text, or null with the note saying why (JRiver off, a wrong user, a refusal).
    private static async Task<string?> JriverGet(Func<string, string> url)
    {
        if (Mcws.Base(Settings.Current.JriverAddress) is not { } baseUrl)
        {
            Note = Tr("JRiver 주소가 올바르지 않아요 — 설정 > 미니 플레이어", "The JRiver address is not valid — Settings > Mini player");
            return null;
        }
        string? auth = await Task.Run(JriverAuth);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url(baseUrl));
            if (auth is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            using var res = await Http().SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                Note = Tr("JRiver가 사용자 이름이나 비밀번호를 받아 주지 않아요 — 설정 > 미니 플레이어", "JRiver did not accept the user name or password — Settings > Mini player");
                return null;
            }
            if (!res.IsSuccessStatusCode)
            {
                Note = Tr($"JRiver가 요청을 거절했어요 (HTTP {(int)res.StatusCode})", $"JRiver refused the request (HTTP {(int)res.StatusCode})");
                return null;
            }
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Note = Tr("JRiver에 닿지 않아요 — JRiver가 켜져 있고 웹 서비스(Media Network)가 켜져 있나요?", "Cannot reach JRiver — is it running with its web service (Media Network) on?");
            return null;
        }
    }

    private static async Task<bool> JriverRun(PlayerOp op, string? file)
    {
        int start = -1; // Play: the place the new song takes in Playing Now
        if (op == PlayerOp.Play)
        {
            if (await JriverGet(b => Mcws.Url(b, "Playback/Info")) is not { } infoBody) return false;
            var info = Mcws.ParseInfo(infoBody);
            start = info.Tracks == 0 ? 0 : info.Index + 1;
            if (info.Tracks == 0) op = PlayerOp.Add; // an empty list: it goes in, and is the first
        }
        if (await JriverGet(b => Mcws.Request(b, op, file)) is not { } body) return false;
        if (Mcws.Accepted(body) && start >= 0)
        {
            if (await JriverGet(b => Mcws.PlayByIndex(b, start)) is not { } playBody) return false;
            body = playBody;
        }
        if (Mcws.Accepted(body))
        {
            Via = "MCWS";
            return true;
        }
        Note = Tr("JRiver가 이 요청을 받아들이지 않았어요", "JRiver did not accept this request");
        return false;
    }

    private static async Task JriverPoll()
    {
        if (await JriverGet(b => Mcws.Url(b, "Playback/Info")) is not { } body || Way != PlayerWays.Jriver)
        {
            exHas = exPlaying = false;
            exTitle = exArtist = "";
            exPosition = exLength = TimeSpan.Zero;
            return;
        }
        var info = Mcws.ParseInfo(body);
        Note = info.Ok ? null : Tr("JRiver의 답을 읽지 못했어요", "Could not read JRiver's answer");
        exHas = info.Has;
        exPlaying = info.Playing;
        exTitle = info.Title;
        exArtist = info.Artist;
        exPosition = info.Position;
        exLength = info.Length;
        exAt = DateTime.UtcNow;
    }

    /// "1:05" / "1:02:03".
    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}

/// MusicBee's window title: "song - MusicBee" while a song is on (the title format of MusicBee's own, as shipped).
internal static class PlayerWindow
{
    private const string Suffix = " - MusicBee";

    /// The song a MusicBee title names; "" for the bare "MusicBee" (nothing on) or no window.
    public static string SongOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || !title.EndsWith(Suffix, StringComparison.Ordinal)) return "";
        return title[..^Suffix.Length].Trim();
    }

    /// MusicBee's windows, hidden ones too (it sits in the tray): the first title that names a song. Off the UI thread.
    public static string MusicBeeTitle()
    {
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("MusicBee"))
        {
            ids.Add((uint)p.Id);
            p.Dispose();
        }
        if (ids.Count == 0) return "";
        string found = "";
        Native.Win32.EnumWindows((h, _) =>
        {
            Native.Win32.GetWindowThreadProcessId(h, out uint pid);
            if (!ids.Contains(pid)) return true;
            int n = Native.Win32.GetWindowTextLength(h);
            if (n <= 0) return true;
            var sb = new StringBuilder(n + 1);
            Native.Win32.GetWindowText(h, sb, sb.Capacity);
            string song = SongOf(sb.ToString());
            if (song.Length == 0) return true;
            found = song;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
