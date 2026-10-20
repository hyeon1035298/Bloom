using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Bloom.Music;

/// A song a program tells Windows about (the volume overlay and the media keys show the same).
internal enum MediaStatus { Closed, Opened, Changing, Stopped, Playing, Paused }

internal sealed record MediaNow(string App, string Title, string Artist, MediaStatus Status, TimeSpan Position, TimeSpan Length, bool CanToggle, bool CanNext, bool CanPrevious, bool CanStop)
{
    public string Album { get; init; } = "";

    /// When the app last told its position (it moves on from there while it plays).
    public DateTime At { get; init; } = DateTime.UtcNow;

    public bool CanSeek { get; init; }
    public bool CanShuffle { get; init; }
    public bool CanRepeat { get; init; }

    /// The app's own 무작위 and 반복, when it tells them.
    public bool? Shuffle { get; init; }
    public Repeat? Repeat { get; init; }
}

/// Windows' media sessions (Windows.Media.Control: what the volume flyout lists): the song, the state and the buttons of
/// a player that tells Windows about itself, and pressing those buttons. The WinRT classes are called through their COM
/// interfaces here instead of the Windows SDK projection (the net10.0-windows10.0.x target framework), which would add
/// a 25 MB assembly to Bloom for these few calls. Every call blocks: use the Async ones, which run off the UI thread.
internal static class MediaSession
{
    /// Whether a session's app (AppUserModelId, e.g. "foobar2000.exe") is that player's.
    public static bool Matches(string way, string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return false;
        bool Has(string s) => appId.Contains(s, StringComparison.OrdinalIgnoreCase);
        return way switch
        {
            PlayerWays.Foobar => Has("foobar2000"),
            PlayerWays.MusicBee => Has("musicbee"),
            PlayerWays.Jriver => Has("mediacenter") || Has("jriver") || Has("j river"),
            _ => false,
        };
    }

    /// The player's session, or null: none (it is off, or it tells Windows nothing).
    public static Task<MediaNow?> ReadAsync(string way) => Task.Run(() => Safe(() => Sessions().Find(s => Matches(way, s.App))));

    /// Every session Windows lists (QA: which players tell Windows about themselves).
    public static Task<List<MediaNow>> AllAsync() => Task.Run(() => Safe(Sessions) ?? new List<MediaNow>());

    /// Presses a button on the player's session; false: no session, the button is off, or Windows refused.
    public static Task<bool> ControlAsync(string way, PlayerOp op) => Task.Run(() => Safe(() => Press(id => Matches(way, id), s => Button(s, op))));

    // ── another app's session, by its exact app id (다른 앱: Spotify, a browser …) ─────────────────────────────────
    public static Task<bool> PressAsync(string app, PlayerOp op) => Task.Run(() => Safe(() => Press(id => id == app, s => Button(s, op))));

    public static Task<bool> SeekAsync(string app, TimeSpan to) =>
        Task.Run(() => Safe(() => Press(id => id == app, s => (s.TryChangePlaybackPositionAsync(Math.Max(0, to.Ticks), out IntPtr op), op))));

    public static Task<bool> ShuffleAsync(string app, bool on) =>
        Task.Run(() => Safe(() => Press(id => id == app, s => (s.TryChangeShuffleActiveAsync(on, out IntPtr op), op))));

    public static Task<bool> RepeatAsync(string app, Repeat repeat) =>
        Task.Run(() => Safe(() => Press(id => id == app, s => (s.TryChangeAutoRepeatModeAsync(ModeOf(repeat), out IntPtr op), op))));

    /// The picture the app gives for its song (a cover, a video's thumbnail): the file's bytes, or null.
    public static Task<byte[]?> ThumbnailAsync(string app) => Task.Run(() => Safe(() => Thumbnail(app)));

    /// MediaPlaybackAutoRepeatMode (None 0, Track 1, List 2) ↔ 반복.
    public static Repeat RepeatOfMode(int mode) => mode switch { 1 => Repeat.One, 2 => Repeat.All, _ => Repeat.Off };

    public static int ModeOf(Repeat repeat) => repeat switch { Repeat.One => 1, Repeat.All => 2, _ => 0 };

    /// A thumbnail larger than this is not read.
    private const int MaxThumbnail = 4 * 1024 * 1024;

    private static T? Safe<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch (Exception e) when (e is COMException or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            Core.Log.Write("media session: " + e.Message);
            manager = null; // asked again next time
            return default;
        }
    }

    // ── the COM interfaces, in the order of their methods. The runtime does not marshal IInspectable itself: the three
    //    methods it adds to IUnknown are declared at the top of every interface below ────────────────────────────────
    [ComImport, Guid("2050c4ee-11a0-57de-aed7-c97c70338245"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IManagerStatics
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int RequestAsync(out IntPtr operation);
    }

    [ComImport, Guid("cace8eac-e86e-504a-ab31-5ff8ff1bce49"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IManager
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int GetCurrentSession(out IntPtr session);
        [PreserveSig] int GetSessions(out IntPtr sessions);
    }

    [ComImport, Guid("7148c835-9b14-5ae2-ab85-dc9b1c14e1a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISession
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_SourceAppUserModelId(out IntPtr id);
        [PreserveSig] int TryGetMediaPropertiesAsync(out IntPtr operation);
        [PreserveSig] int GetTimelineProperties(out IntPtr timeline);
        [PreserveSig] int GetPlaybackInfo(out IntPtr info);
        [PreserveSig] int TryPlayAsync(out IntPtr operation);
        [PreserveSig] int TryPauseAsync(out IntPtr operation);
        [PreserveSig] int TryStopAsync(out IntPtr operation);
        [PreserveSig] int TryRecordAsync(out IntPtr operation);
        [PreserveSig] int TryFastForwardAsync(out IntPtr operation);
        [PreserveSig] int TryRewindAsync(out IntPtr operation);
        [PreserveSig] int TrySkipNextAsync(out IntPtr operation);
        [PreserveSig] int TrySkipPreviousAsync(out IntPtr operation);
        [PreserveSig] int TryChangeChannelUpAsync(out IntPtr operation);
        [PreserveSig] int TryChangeChannelDownAsync(out IntPtr operation);
        [PreserveSig] int TryTogglePlayPauseAsync(out IntPtr operation);
        [PreserveSig] int TryChangeAutoRepeatModeAsync(int mode, out IntPtr operation);
        [PreserveSig] int TryChangePlaybackRateAsync(double rate, out IntPtr operation);
        [PreserveSig] int TryChangeShuffleActiveAsync([MarshalAs(UnmanagedType.U1)] bool on, out IntPtr operation);
        [PreserveSig] int TryChangePlaybackPositionAsync(long ticks, out IntPtr operation);
    }

    [ComImport, Guid("68856cf6-adb4-54b2-ac16-05837907acb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaProperties
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_Title(out IntPtr title);
        [PreserveSig] int get_Subtitle(out IntPtr subtitle);
        [PreserveSig] int get_AlbumArtist(out IntPtr albumArtist);
        [PreserveSig] int get_Artist(out IntPtr artist);
        [PreserveSig] int get_AlbumTitle(out IntPtr album);
        [PreserveSig] int get_TrackNumber(out int number);
        [PreserveSig] int get_Genres(out IntPtr genres);
        [PreserveSig] int get_AlbumTrackCount(out int count);
        [PreserveSig] int get_PlaybackType(out IntPtr type);
        [PreserveSig] int get_Thumbnail(out IntPtr reference); // IRandomAccessStreamReference
    }

    [ComImport, Guid("94b4b6cf-e8ba-51ad-87a7-c10ade106127"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPlaybackInfo
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_Controls(out IntPtr controls);
        [PreserveSig] int get_PlaybackStatus(out int status);
        [PreserveSig] int get_PlaybackType(out IntPtr type);
        [PreserveSig] int get_AutoRepeatMode(out IntPtr mode); // IReference<MediaPlaybackAutoRepeatMode>
        [PreserveSig] int get_PlaybackRate(out IntPtr rate);
        [PreserveSig] int get_IsShuffleActive(out IntPtr on); // IReference<bool>
    }

    [ComImport, Guid("6501a3e6-bc7a-503a-bb1b-68f158f3fb03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IControls
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_IsPlayEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsPauseEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsStopEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsRecordEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsFastForwardEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsRewindEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsNextEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsPreviousEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsChannelUpEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsChannelDownEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsPlayPauseToggleEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsShuffleEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsRepeatEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsPlaybackRateEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
        [PreserveSig] int get_IsPlaybackPositionEnabled([MarshalAs(UnmanagedType.U1)] out bool on);
    }

    [ComImport, Guid("ede34136-6f25-588d-8ecf-ea5b6735aaa5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITimeline
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_StartTime(out long ticks);
        [PreserveSig] int get_EndTime(out long ticks);
        [PreserveSig] int get_MinSeekTime(out long ticks);
        [PreserveSig] int get_MaxSeekTime(out long ticks);
        [PreserveSig] int get_Position(out long ticks);
        [PreserveSig] int get_LastUpdatedTime(out long universalTime); // DateTime: 100 ns since 1601, UTC
    }

    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
    [DllImport("ole32.dll")] private static extern int CoIncrementMTAUsage(out IntPtr cookie);
    [DllImport("shcore.dll")] private static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, ref Guid iid, out IntPtr stream);

    private static readonly object Gate = new();
    private static IManager? manager;
    private static bool mta;

    // ── the calls that cannot go through an interface: IVectorView<T> and IAsyncOperation<T> are generic ────────────
    private static unsafe int Call(IntPtr obj, int slot, uint arg, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)obj)[slot])(obj, arg, &r);
        result = r;
        return hr;
    }

    private static unsafe int Call(IntPtr obj, int slot, out int result)
    {
        int r = 0;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)(*(IntPtr**)obj)[slot])(obj, &r);
        result = r;
        return hr;
    }

    private static unsafe int Call(IntPtr obj, int slot, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)(*(IntPtr**)obj)[slot])(obj, &r);
        result = r;
        return hr;
    }

    private static unsafe int Call(IntPtr obj, int slot, out byte result)
    {
        byte r = 0;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)(*(IntPtr**)obj)[slot])(obj, &r);
        result = r;
        return hr;
    }

    /// Waits for an IAsyncOperation<T> (at most 3 s); false when it did not finish well. The operation stays held.
    private static bool Finished(IntPtr op)
    {
        var iid = new Guid("00000036-0000-0000-C000-000000000046"); // IAsyncInfo
        if (Marshal.QueryInterface(op, in iid, out IntPtr info) != 0) return false;
        try
        {
            for (int i = 0; i < 300; i++)
            {
                if (Call(info, 7, out int status) != 0) return false; // get_Status: 0 started, 1 completed, 2 canceled, 3 error
                if (status == 1) return true;
                if (status != 0) return false;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    /// The result of an IAsyncOperation<interface>: its pointer (the caller releases it), or zero. Releases `op`.
    private static IntPtr Result(int hr, IntPtr op)
    {
        if (hr != 0 || op == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            return Finished(op) && Call(op, 8, out IntPtr r) == 0 ? r : IntPtr.Zero; // GetResults
        }
        finally
        {
            Marshal.Release(op);
        }
    }

    /// The result of an IAsyncOperation<bool>. Releases `op`.
    private static bool Accepted(int hr, IntPtr op)
    {
        if (hr != 0 || op == IntPtr.Zero) return false;
        try
        {
            return Finished(op) && Call(op, 8, out byte r) == 0 && r != 0;
        }
        finally
        {
            Marshal.Release(op);
        }
    }

    private static string Text(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero) return "";
        try
        {
            IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
            return buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(buffer, (int)length) ?? "";
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static T? As<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero) return null;
        try
        {
            return (T)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static IManager? Manager()
    {
        lock (Gate)
        {
            if (manager is not null) return manager;
            if (!mta)
            {
                CoIncrementMTAUsage(out _); // these threads are the thread pool's: the process's multithreaded apartment
                mta = true;
            }
            string name = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
            if (WindowsCreateString(name, name.Length, out IntPtr id) != 0) return null;
            try
            {
                var iid = typeof(IManagerStatics).GUID;
                if (RoGetActivationFactory(id, ref iid, out IntPtr factory) != 0) return null;
                var statics = As<IManagerStatics>(factory);
                if (statics is null) return null;
                int hr = statics.RequestAsync(out IntPtr op);
                return manager = As<IManager>(Result(hr, op));
            }
            finally
            {
                WindowsDeleteString(id);
            }
        }
    }

    /// Every session, in Windows' order.
    private static List<MediaNow> Sessions()
    {
        var list = new List<MediaNow>();
        foreach (var session in Open())
        {
            try
            {
                list.Add(Describe(session));
            }
            finally
            {
                Marshal.ReleaseComObject(session);
            }
        }
        return list;
    }

    private static List<ISession> Open()
    {
        var sessions = new List<ISession>();
        if (Manager() is not { } m || m.GetSessions(out IntPtr vector) != 0 || vector == IntPtr.Zero) return sessions;
        try
        {
            if (Call(vector, 7, out int count) != 0) return sessions; // get_Size
            for (uint i = 0; i < count; i++)
                if (Call(vector, 6, i, out IntPtr item) == 0 && As<ISession>(item) is { } s) sessions.Add(s); // GetAt
        }
        finally
        {
            Marshal.Release(vector);
        }
        return sessions;
    }

    private static MediaNow Describe(ISession s)
    {
        s.get_SourceAppUserModelId(out IntPtr id);
        string app = Text(id), title = "", artist = "", album = "";
        var status = MediaStatus.Closed;
        bool toggle = false, next = false, previous = false, stop = false, seek = false, canShuffle = false, canRepeat = false;
        bool? shuffle = null;
        Repeat? repeat = null;
        TimeSpan position = TimeSpan.Zero, length = TimeSpan.Zero;
        var at = DateTime.UtcNow;
        if (s.GetPlaybackInfo(out IntPtr infoPtr) == 0 && As<IPlaybackInfo>(infoPtr) is { } info)
        {
            try
            {
                if (info.get_PlaybackStatus(out int st) == 0) status = (MediaStatus)Math.Clamp(st, 0, 5);
                if (info.get_AutoRepeatMode(out IntPtr modePtr) == 0 && Int(modePtr) is { } mode) repeat = RepeatOfMode(mode);
                if (info.get_IsShuffleActive(out IntPtr shufflePtr) == 0) shuffle = Bool(shufflePtr);
                if (info.get_Controls(out IntPtr controlsPtr) == 0 && As<IControls>(controlsPtr) is { } c)
                {
                    try
                    {
                        c.get_IsPlayPauseToggleEnabled(out toggle);
                        c.get_IsNextEnabled(out next);
                        c.get_IsPreviousEnabled(out previous);
                        c.get_IsStopEnabled(out stop);
                        c.get_IsShuffleEnabled(out canShuffle);
                        c.get_IsRepeatEnabled(out canRepeat);
                        c.get_IsPlaybackPositionEnabled(out seek);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(c);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(info);
            }
        }
        if (s.GetTimelineProperties(out IntPtr timelinePtr) == 0 && As<ITimeline>(timelinePtr) is { } t)
        {
            try
            {
                if (t.get_Position(out long p) == 0) position = TimeSpan.FromTicks(Math.Max(0, p));
                if (t.get_EndTime(out long e) == 0 && t.get_StartTime(out long b) == 0) length = TimeSpan.FromTicks(Math.Max(0, e - b));
                if (t.get_LastUpdatedTime(out long told) == 0 && told > 0 && told < DateTime.MaxValue.ToFileTimeUtc()) at = DateTime.FromFileTimeUtc(told);
            }
            finally
            {
                Marshal.ReleaseComObject(t);
            }
        }
        int hr = s.TryGetMediaPropertiesAsync(out IntPtr op); // fails while the player has no song open: the title stays empty
        if (As<IMediaProperties>(Result(hr, op)) is { } props)
        {
            try
            {
                props.get_Title(out IntPtr titlePtr);
                title = Text(titlePtr);
                props.get_Artist(out IntPtr artistPtr);
                artist = Text(artistPtr);
                props.get_AlbumTitle(out IntPtr albumPtr);
                album = Text(albumPtr);
            }
            finally
            {
                Marshal.ReleaseComObject(props);
            }
        }
        return new MediaNow(app, title, artist, status, position, length, toggle, next, previous, stop)
        {
            Album = album,
            At = at,
            CanSeek = seek,
            CanShuffle = canShuffle,
            CanRepeat = canRepeat,
            Shuffle = shuffle,
            Repeat = repeat,
        };
    }

    /// An IReference<int> (a WinRT value that may be missing): its value, or null. Releases it.
    private static int? Int(IntPtr reference)
    {
        if (reference == IntPtr.Zero) return null;
        try
        {
            return Call(reference, 6, out int v) == 0 ? v : null; // get_Value
        }
        finally
        {
            Marshal.Release(reference);
        }
    }

    /// An IReference<bool>: its value, or null. Releases it.
    private static bool? Bool(IntPtr reference)
    {
        if (reference == IntPtr.Zero) return null;
        try
        {
            return Call(reference, 6, out byte v) == 0 ? v != 0 : null;
        }
        finally
        {
            Marshal.Release(reference);
        }
    }

    /// Presses something on the first session `whose` app id it is; false: none, or the app said no.
    private static bool Press(Func<string, bool> whose, Func<ISession, (int hr, IntPtr op)> act)
    {
        var sessions = Open();
        try
        {
            foreach (var session in sessions)
            {
                session.get_SourceAppUserModelId(out IntPtr id);
                if (!whose(Text(id))) continue;
                var (hr, op) = act(session);
                return Accepted(hr, op);
            }
            return false;
        }
        finally
        {
            foreach (var session in sessions) Marshal.ReleaseComObject(session);
        }
    }

    private static (int hr, IntPtr op) Button(ISession session, PlayerOp op)
    {
        IntPtr call;
        int hr = op switch
        {
            PlayerOp.Toggle => session.TryTogglePlayPauseAsync(out call),
            PlayerOp.Next => session.TrySkipNextAsync(out call),
            PlayerOp.Previous => session.TrySkipPreviousAsync(out call),
            PlayerOp.Stop => session.TryStopAsync(out call),
            _ => Nothing(out call),
        };
        return (hr, call);
    }

    private static int Nothing(out IntPtr call)
    {
        call = IntPtr.Zero;
        return -1;
    }

    private static byte[]? Thumbnail(string app)
    {
        var sessions = Open();
        try
        {
            foreach (var session in sessions)
            {
                session.get_SourceAppUserModelId(out IntPtr id);
                if (Text(id) != app) continue;
                int hr = session.TryGetMediaPropertiesAsync(out IntPtr op);
                if (As<IMediaProperties>(Result(hr, op)) is not { } props) return null;
                try
                {
                    if (props.get_Thumbnail(out IntPtr reference) != 0 || reference == IntPtr.Zero) return null;
                    IntPtr stream;
                    try
                    {
                        stream = Call(reference, 6, out IntPtr read) == 0 ? Result(0, read) : IntPtr.Zero; // OpenReadAsync
                    }
                    finally
                    {
                        Marshal.Release(reference);
                    }
                    if (stream == IntPtr.Zero) return null;
                    try
                    {
                        return Bytes(stream);
                    }
                    finally
                    {
                        Marshal.Release(stream);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(props);
                }
            }
            return null;
        }
        finally
        {
            foreach (var session in sessions) Marshal.ReleaseComObject(session);
        }
    }

    /// An IRandomAccessStream's bytes (at most MaxThumbnail), read through the IStream Windows wraps around it.
    private static unsafe byte[]? Bytes(IntPtr randomAccess)
    {
        var iid = new Guid("0000000c-0000-0000-C000-000000000046"); // IStream
        if (CreateStreamOverRandomAccessStream(randomAccess, ref iid, out IntPtr pointer) != 0 || pointer == IntPtr.Zero) return null;
        var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        try
        {
            using var all = new System.IO.MemoryStream();
            var buffer = new byte[64 * 1024];
            int read = 0;
            do
            {
                stream.Read(buffer, buffer.Length, (IntPtr)(&read));
                all.Write(buffer, 0, read);
                if (all.Length > MaxThumbnail) return null;
            }
            while (read > 0);
            return all.Length > 0 ? all.ToArray() : null;
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }
}
