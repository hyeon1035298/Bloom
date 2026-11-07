using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Bloom.Native;

namespace Bloom.Ocr;

/// The screen recording itself, in a process of its own (`Bloom.exe --record x y w h out.mp4 audio`, started by
/// RecorderProcess) so the frames, the encoder and the sound all go back to Windows when it ends. Nothing of Bloom's
/// starts here: no WPF, no settings, no data folder, no log; it speaks only through stdin and stdout.
///   args:   x y w h in physical virtual-screen pixels (w and h rounded down to even), the .mp4's full path, and the
///           sound: "-", "sys" (what the PC plays), "mic" or "sys+mic".
///   stdout: "ready" (set up, nothing captured yet), "started" (after "go"), "done <ms>" (the file is complete),
///           "error <code> <message>" (code encoder, audio, capture, io or args; the partial file is deleted, exit ≠ 0).
///   stdin:  "go" starts, "stop" finishes the file. The end of stdin (Bloom gone) is "stop" once started, else the
///           process ends with no file.
/// One line of figures (frames, drops, milliseconds per frame) goes to stderr at the end, for measuring.
internal static class ScreenRecorder
{
    public const string Arg = "--record";

    /// Sound is mixed this far behind the clock, so packets still on their way are not taken for silence.
    private const int AudioLag = AudioMixer.Rate / 10;

    internal sealed record Request(ScreenRect Area, string File, bool System, bool Mic);

    /// The arguments after "--record", or null when they don't make a recording.
    internal static Request? Parse(string[] args)
    {
        if (args.Length != 7 || args[0] != Arg) return null;
        var n = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(args[i + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n[i])) return null;
        int w = MediaWriter.Even(n[2]), h = MediaWriter.Even(n[3]);
        string file = args[5];
        if (w < 2 || h < 2 || file.Length == 0 || !Path.IsPathFullyQualified(file)) return null;
        (bool sys, bool mic)? sound = args[6] switch
        {
            "-" => (false, false),
            "sys" => (true, false),
            "mic" => (false, true),
            "sys+mic" => (true, true),
            _ => null,
        };
        return sound is { } s ? new Request(new ScreenRect(n[0], n[1], w, h), file, s.sys, s.mic) : null;
    }

    public static int Run(string[] args)
    {
        var output = new Output();
        var request = Parse(args);
        if (request is null)
        {
            output.Say("error args usage: --record <x> <y> <w> <h> <file.mp4> <-|sys|mic|sys+mic>");
            return 2;
        }
        int code = 1;
        // MTA: Media Foundation and WASAPI objects are called from more than one thread
        var worker = new Thread(() => code = Record(request, output)) { Name = "record" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        worker.Join();
        return code;
    }

    private static int Record(Request request, Output output)
    {
        _ = timeBeginPeriod(1); // 1 ms waits for the 33 ms frame clock (this process only)
        MediaWriter? writer = null;
        AudioCapture? audio = null;
        FrameGrabber? grabber = null;
        string phase = "encoder";
        try
        {
            writer = MediaWriter.Create(request.File, request.Area.Width, request.Area.Height, request.System || request.Mic);
            AudioMixer? mixer = null;
            if (writer.Audio)
            {
                phase = "audio";
                mixer = new AudioMixer((request.System ? 1 : 0) + (request.Mic ? 1 : 0));
                audio = AudioCapture.Open(request.System, request.Mic, mixer);
            }
            phase = "capture";
            grabber = new FrameGrabber(request.Area);
            var input = new Input();
            output.Say("ready");
            if (WaitHandle.WaitAny(new WaitHandle[] { input.Go, input.Stop }) != 0)
            {
                // stopped (or Bloom gone) before it started: no recording, no file
                Close(ref writer, ref audio, ref grabber);
                Delete(request.File);
                return 0;
            }

            phase = "encoder";
            long start = Stopwatch.GetTimestamp();
            audio?.Start(Hns(start));
            output.Say("started");
            var sink = new Sink(writer, grabber);
            var timeline = new FrameTimeline(sink);
            var pcm = new short[AudioMixer.Rate / 5 * AudioMixer.Channels];
            long next = 0;
            while (true)
            {
                long now = Elapsed(start);
                if (now >= next) next = timeline.Tick(now);
                if (mixer is not null) WriteAudio(writer, mixer, pcm, now * AudioMixer.Rate / 10_000_000 - AudioLag);
                long wait = (next - Elapsed(start)) / 10_000;
                if (input.Stop.WaitOne((int)Math.Clamp(wait, 0, 50))) break;
            }
            long end = Elapsed(start);
            if (audio is not null && mixer is not null)
            {
                Thread.Sleep(AudioLag * 1000 / AudioMixer.Rate + 20); // the last packets come in
                audio.Stop();
            }
            long length = timeline.End(end);
            if (mixer is not null) WriteAudio(writer, mixer, pcm, length * AudioMixer.Rate / 10_000_000);
            writer.Finish();
            Close(ref writer, ref audio, ref grabber);
            output.Say(FormattableString.Invariant($"done {length / 10_000}"));
            output.Stats(FormattableString.Invariant(
                $"frames {timeline.Captured} dropped {timeline.Dropped} grab {sink.GrabMs:F2} ms write {sink.WriteMs:F2} ms per frame"));
            return 0;
        }
        catch (Exception e)
        {
            int hr = e is COMException c ? c.HResult : 0;
            if (phase == "encoder" && hr != 0 && MediaWriter.IsFileError(hr)) phase = "io";
            if (e is IOException or UnauthorizedAccessException) phase = "io";
            Close(ref writer, ref audio, ref grabber);
            Delete(request.File);
            string message = (hr != 0 ? $"0x{hr:X8} " : "") + e.Message;
            output.Say($"error {phase} {message.ReplaceLineEndings(" ").Trim()}");
            return 1;
        }
        finally
        {
            Close(ref writer, ref audio, ref grabber);
            _ = timeEndPeriod(1);
        }
    }

    /// Mixed sound up to audio frame `until`, in pieces of at most `pcm`'s length.
    private static void WriteAudio(MediaWriter writer, AudioMixer mixer, short[] pcm, long until)
    {
        while (mixer.Mix(until, pcm, out long at) is int frames and > 0)
            writer.WriteAudio(pcm.AsSpan(0, frames * AudioMixer.Channels), at);
    }

    private static void Close(ref MediaWriter? writer, ref AudioCapture? audio, ref FrameGrabber? grabber)
    {
        audio?.Dispose();
        audio = null;
        writer?.Dispose();
        writer = null;
        grabber?.Dispose();
        grabber = null;
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* still held: nothing more to do here */
        }
    }

    /// QPC ticks as 100-ns units, the clock WASAPI stamps its packets with.
    private static long Hns(long ticks)
    {
        long f = Stopwatch.Frequency;
        return ticks / f * 10_000_000 + ticks % f * 10_000_000 / f;
    }

    private static long Elapsed(long start) => Hns(Stopwatch.GetTimestamp()) - Hns(start);

    /// The frames: the screen grabbed into the encoder's slots.
    private sealed class Sink(MediaWriter writer, FrameGrabber grabber) : IFrameSink
    {
        private long grabTicks, writeTicks, grabs, writes;

        public int Slots => MediaWriter.FrameSlots;
        public double GrabMs => grabs == 0 ? 0 : grabTicks * 1000.0 / Stopwatch.Frequency / grabs;
        public double WriteMs => writes == 0 ? 0 : writeTicks * 1000.0 / Stopwatch.Frequency / writes;

        public bool IsFree(int slot) => writer.SlotFree(slot);

        public void Fill(int slot)
        {
            long t = Stopwatch.GetTimestamp();
            grabber.Grab();
            writer.Fill(slot, grabber.Bits);
            grabTicks += Stopwatch.GetTimestamp() - t;
            grabs++;
        }

        public void Write(int slot, long time, long duration)
        {
            long t = Stopwatch.GetTimestamp();
            writer.WriteVideo(slot, time, duration);
            writeTicks += Stopwatch.GetTimestamp() - t;
            writes++;
        }
    }

    /// stdin, read on a thread of its own: "go" and "stop"; its end counts as "stop".
    private sealed class Input
    {
        public readonly ManualResetEvent Go = new(false), Stop = new(false);

        public Input()
        {
            new Thread(() =>
            {
                try
                {
                    using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                    while (reader.ReadLine() is { } line)
                    {
                        string cmd = line.Trim();
                        if (cmd == "go") Go.Set();
                        else if (cmd == "stop") Stop.Set();
                    }
                }
                catch (IOException)
                {
                    /* a broken pipe ends stdin too */
                }
                Stop.Set();
            })
            { IsBackground = true, Name = "record input" }.Start();
        }
    }

    /// stdout, one flushed line at a time; Bloom gone is no error (the file still gets finished).
    private sealed class Output
    {
        private readonly StreamWriter writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        private readonly object gate = new();

        public void Say(string line)
        {
            lock (gate)
                try
                {
                    writer.WriteLine(line);
                }
                catch (IOException)
                {
                    /* nobody reading */
                }
        }

        public void Stats(string line)
        {
            try
            {
                using var err = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                err.WriteLine(line);
            }
            catch (IOException)
            {
                /* nobody reading */
            }
        }
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
}

/// What FrameTimeline drives: the encoder's frame slots (or a stand-in in the tests).
internal interface IFrameSink
{
    int Slots { get; }
    /// The slot is not held by the encoder any more.
    bool IsFree(int slot);
    /// The picture of now into the slot.
    void Fill(int slot);
    /// The slot's picture shown from `time` for `duration` (100-ns units).
    void Write(int slot, long time, long duration);
}

/// The 30 fps timeline. A frame is written only once the next one is taken, so its length is known: when capture or
/// the encoder falls behind, the frames due meanwhile are dropped and the one on screen just lasts longer. The video
/// has no holes, and no more than Slots pictures are ever held.
internal sealed class FrameTimeline(IFrameSink sink)
{
    private int pending = -1; // the slot whose length isn't known yet
    private long pendingFrame, last = -1;

    public long Captured { get; private set; }
    public long Dropped { get; private set; }

    public static long FrameAt(long time) => time * MediaWriter.Fps / 10_000_000;

    /// The clock is at `time` (100 ns since the start): the frame due now, unless it was taken already. Returns when
    /// the next one is due.
    public long Tick(long time)
    {
        long frame = FrameAt(time);
        if (frame <= last) return MediaWriter.FrameTime(last + 1);
        if (last >= 0) Dropped += frame - last - 1; // late: the ones in between are gone
        last = frame;
        int slot = -1;
        for (int i = 0; i < sink.Slots && slot < 0; i++)
            if (i != pending && sink.IsFree(i)) slot = i;
        if (slot < 0)
        {
            Dropped++; // the encoder still holds the other picture: the one on screen goes on
            return MediaWriter.FrameTime(frame + 1);
        }
        sink.Fill(slot);
        Captured++;
        if (pending >= 0) sink.Write(pending, MediaWriter.FrameTime(pendingFrame), MediaWriter.FrameTime(frame) - MediaWriter.FrameTime(pendingFrame));
        pending = slot;
        pendingFrame = frame;
        return MediaWriter.FrameTime(frame + 1);
    }

    /// The end at `time`: the last picture lasts until then (one frame at least). Returns the video's length.
    public long End(long time)
    {
        if (pending < 0) return 0;
        long from = MediaWriter.FrameTime(pendingFrame);
        long end = Math.Max(time, MediaWriter.FrameTime(pendingFrame + 1));
        sink.Write(pending, from, end - from);
        pending = -1;
        return end;
    }
}

/// GDI grabs of one screen rectangle into one reused bitmap, the mouse pointer drawn in. Windows that keep out of
/// captures (WDA_EXCLUDEFROMCAPTURE: the recording's own frame and bar) are left out by Windows itself.
internal sealed class FrameGrabber : IDisposable
{
    private readonly ScreenRect area;
    private IntPtr screen, mem, bmp, old;
    private IntPtr cursor;
    private int hotX, hotY;

    public IntPtr Bits { get; }

    public FrameGrabber(ScreenRect area)
    {
        this.area = area;
        screen = Win32.GetDC(IntPtr.Zero);
        mem = screen == IntPtr.Zero ? IntPtr.Zero : CreateCompatibleDC(screen);
        var bi = new Win32.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = area.Width, biHeight = -area.Height, biPlanes = 1, biBitCount = 32 };
        IntPtr bits = IntPtr.Zero;
        if (mem != IntPtr.Zero) bmp = Win32.CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
        if (bmp == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("no screen bitmap");
        }
        Bits = bits;
        old = SelectObject(mem, bmp);
    }

    /// The rectangle now. A failed grab (the secure desktop of a UAC prompt, a locked PC) keeps the last picture.
    public void Grab()
    {
        if (!BitBlt(mem, 0, 0, area.Width, area.Height, screen, area.X, area.Y, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */)) return;
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & 1 /* CURSOR_SHOWING */) == 0 || ci.hCursor == IntPtr.Zero) return;
        int x = ci.ptScreenPos.X - area.X, y = ci.ptScreenPos.Y - area.Y;
        if (x < 0 || y < 0 || x >= area.Width || y >= area.Height) return;
        if (ci.hCursor != cursor)
        {
            // the hot spot, read once per pointer shape (GetIconInfo makes two bitmaps each time)
            if (!GetIconInfo(ci.hCursor, out var ii)) return;
            if (ii.hbmMask != IntPtr.Zero) Win32.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Win32.DeleteObject(ii.hbmColor);
            cursor = ci.hCursor;
            hotX = ii.xHotspot;
            hotY = ii.yHotspot;
        }
        _ = DrawIconEx(mem, x - hotX, y - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
    }

    public void Dispose()
    {
        if (old != IntPtr.Zero) SelectObject(mem, old);
        if (bmp != IntPtr.Zero) Win32.DeleteObject(bmp);
        if (mem != IntPtr.Zero) DeleteDC(mem);
        if (screen != IntPtr.Zero) _ = Win32.ReleaseDC(IntPtr.Zero, screen);
        old = bmp = mem = screen = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public Win32.POINT ptScreenPos; }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out Win32.ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
