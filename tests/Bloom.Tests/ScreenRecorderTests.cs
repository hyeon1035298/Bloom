using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Native;
using Bloom.Ocr;

namespace Bloom.Tests;

internal static class ScreenRecorderTests
{
    private const long Frame = 10_000_000 / MediaWriter.Fps;

    /// H.264 wants even sizes: an odd side loses one pixel; monitors left of and above the primary give negative x, y.
    public static void ArgumentsRoundSizesDownToEven()
    {
        string file = Path.Combine(Path.GetTempPath(), "x.mp4");
        var r = Check.NotNull(ScreenRecorder.Parse(new[] { "--record", "-10", "-1440", "101", "75", file, "sys+mic" }), "parsed");
        Check.Eq(r.Area, new ScreenRect(-10, -1440, 100, 74), "odd sides rounded down");
        Check.True(r.System && r.Mic, "both sounds");
        Check.Eq(MediaWriter.Even(2561), 2560, "even");
        Check.True(ScreenRecorder.Parse(new[] { "--record", "0", "0", "64", "64", file, "loud" }) is null, "unknown sound");
        Check.True(ScreenRecorder.Parse(new[] { "--record", "0", "0", "64", "64", "x.mp4", "-" }) is null, "relative path");
        Check.True(ScreenRecorder.Parse(new[] { "--record", "0", "0", "1", "64", file, "-" }) is null, "too narrow");
        Check.True(ScreenRecorder.Parse(new[] { "--record", "0", "0", "64", file, "-" }) is null, "missing argument");
    }

    /// A slow encoder: the frames due while it holds the pictures are dropped and the one on screen lasts longer, so
    /// the timeline has no holes and a picture in use is never overwritten.
    public static void TimelineDropsFramesWhileTheEncoderIsBehind()
    {
        var sink = new SlowSink();
        var timeline = new FrameTimeline(sink);
        long now = 0, next = 0, ticked = 0;
        while (now < 10 * 10_000_000L)
        {
            sink.Now = now;
            sink.HoldFor = now is >= 20_000_000 and < 40_000_000 ? 100 * 10_000L : 5 * 10_000L; // 2–4 s: 100 ms per frame
            if (now >= next)
            {
                next = timeline.Tick(now);
                ticked = now;
            }
            // 6 s: the loop wakes 200 ms late once
            now = now < 60_000_000 && next >= 60_000_000 ? next + 200 * 10_000L : Math.Max(next, now + 10_000);
        }
        long length = timeline.End(now);
        Check.True(timeline.Dropped > 10, $"frames dropped ({timeline.Dropped})");
        Check.Eq(timeline.Captured + timeline.Dropped, FrameTimeline.FrameAt(ticked) + 1, "every frame of the grid taken or dropped");
        Check.Eq(sink.Writes[0].Time, 0L, "starts at 0");
        long sum = 0;
        bool longer = false;
        for (int i = 0; i < sink.Writes.Count; i++)
        {
            var (time, duration) = sink.Writes[i];
            if (i > 0) Check.Eq(time, sink.Writes[i - 1].Time + sink.Writes[i - 1].Duration, $"frame {i} follows the one before");
            Check.True(duration >= Frame - 1, $"frame {i} lasts a frame at least");
            longer |= duration > Frame + 1;
            sum += duration;
        }
        Check.True(longer, "a dropped frame lengthens the one before it");
        Check.Eq(sum, length, "the lengths add up to the video");
        Check.Eq(length, now, "the video lasts as long as the clock ran");
    }

    public static void MixerSumsAndClamps()
    {
        var m = new AudioMixer(2);
        m.Push(0, 0, Fill(100, 0.75f), false);
        m.Push(1, 0, Fill(100, 0.5f), false);
        m.Push(0, 100, Fill(100, -0.75f), false);
        m.Push(1, 100, Fill(100, -0.5f), false);
        m.Push(0, 200, Fill(100, 0.25f), false);
        m.Push(1, 200, Fill(100, 0.25f), true); // a silent packet counts as zeros
        var pcm = new short[600];
        Check.Eq(m.Mix(300, pcm, out long at), 300, "frames mixed");
        Check.Eq(at, 0L, "from the start");
        Check.Eq(pcm[0], (short)32767, "0.75 + 0.5 clamps to full scale");
        Check.Eq(pcm[399], (short)-32767, "and on the negative side");
        Check.Near(pcm[500], 0.25 * 32767, 1, "a silent packet adds nothing");
        Check.Eq(m.Mix(300, pcm, out _), 0, "nothing twice");
    }

    /// Loopback sends nothing while nothing plays: the gap is silence and the next sound lands at its own time.
    public static void MixerFillsGapsWithSilence()
    {
        var m = new AudioMixer(1);
        m.Push(0, 0, Fill(100, 0.5f), false);
        m.Push(0, 24_000, Fill(100, 0.5f), false); // half a second later
        var pcm = new short[48_000 * 2];
        Check.Eq(m.Mix(24_200, pcm, out _), 24_200, "mixed up to the clock even past the data");
        Check.Near(pcm[2 * 99], 0.5 * 32767, 1, "first packet");
        Check.Eq(pcm[2 * 100], (short)0, "the gap is silent");
        Check.Eq(pcm[2 * 23_999 + 1], (short)0, "up to the next packet");
        Check.Near(pcm[2 * 24_000], 0.5 * 32767, 1, "the next packet at its own time");
        Check.Eq(pcm[2 * 24_150], (short)0, "after it, silence again");

        // a packet a little off where the last one ended joins on without a gap (clock jitter)
        var j = new AudioMixer(1);
        j.Push(0, 0, Fill(100, 0.5f), false);
        j.Push(0, 110, Fill(100, 0.5f), false);
        var p = new short[400];
        j.Mix(200, p, out _);
        Check.Near(p[2 * 105], 0.5 * 32767, 1, "jitter leaves no click of silence");

        // bounded: a packet far past the ring is dropped instead of growing it
        var b = new AudioMixer(1, 1000);
        b.Push(0, 5000, Fill(100, 0.5f), false);
        var q = new short[2000];
        Check.Eq(b.Mix(1000, q, out _), 1000, "mixes at most the ring");
        Check.Eq(q[1999], (short)0, "nothing stale");
    }

    /// The writer as the recorder drives it, on a made-up clock: 2 s of synthetic frames into an MP4 that reads back.
    public static void WriterMakesAPlayableVideo() => WriteAndProbe(audio: false);

    public static void WriterAddsTheAudioTrack() => WriteAndProbe(audio: true);

    private static void WriteAndProbe(bool audio)
    {
        string dir = TempDir();
        string file = Path.Combine(dir, audio ? "av.mp4" : "v.mp4");
        int w = MediaWriter.Even(321), h = MediaWriter.Even(241);
        IntPtr picture = Marshal.AllocHGlobal(w * h * 4);
        try
        {
            var px = new byte[w * h * 4];
            using (var writer = MediaWriter.Create(file, w, h, audio))
            {
                var sink = new WriterSink(writer, picture, px, w, h);
                var timeline = new FrameTimeline(sink);
                var pcm = new short[1600 * 2];
                long audioAt = 0;
                for (long t = 0; t < 2 * 10_000_000L; t += Frame)
                {
                    sink.WaitForSlot();
                    timeline.Tick(t);
                    if (!audio) continue;
                    for (int i = 0; i < 1600; i++)
                        pcm[2 * i] = pcm[2 * i + 1] = (short)(Math.Sin((audioAt + i) * 2 * Math.PI * 440 / 48_000) * 8000);
                    writer.WriteAudio(pcm, audioAt);
                    audioAt += 1600;
                }
                long length = timeline.End(2 * 10_000_000L);
                Check.Eq(timeline.Dropped, 0L, "no frame dropped on a clock that waits");
                Check.Eq(length, 2 * 10_000_000L, "two seconds");
                writer.Finish();
            }
            var info = Check.NotNull(MediaWriter.Probe(file), "the file reads back");
            Check.Near(info.Duration.TotalSeconds, 2, 0.1, "duration");
            Check.Eq((info.Width, info.Height), (320, 240), "frame size");
            Check.Eq(info.HasAudio, audio, "audio stream");
            if (audio) Check.Eq((info.AudioRate, info.AudioChannels), (48_000, 2), "48 kHz stereo");
        }
        finally
        {
            Marshal.FreeHGlobal(picture);
            Directory.Delete(dir, true);
        }
    }

    /// The real child process on a 64×64 corner of the screen for a second (it reads pixels, opens no window).
    public static void RecorderProcessRecordsASmallArea()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Bloom.exe");
        if (!File.Exists(exe))
        {
            Console.WriteLine($"  skip  RecorderProcessRecordsASmallArea: no Bloom.exe in {AppContext.BaseDirectory}");
            return;
        }
        string dir = TempDir();
        try
        {
            RecorderProcess.ExePath = exe;
            string file = Path.Combine(dir, "screen.mp4");
            using (var r = RecorderProcess.Start(new ScreenRect(0, 0, 65, 64), file, false, false))
            {
                Check.True(r.Ready.Wait(20_000), "ready");
                string? failed = null;
                r.Failed += (code, message) => failed = code + " " + message;
                r.Go();
                Thread.Sleep(1000);
                var result = r.Stop().GetAwaiter().GetResult();
                Check.True(result.Ok, $"done ({result.ErrorCode} {result.Message})");
                Check.Near(result.Duration.TotalSeconds, 1, 0.4, "about a second");
                Check.True(failed is null, "no failure reported");
            }
            var info = Check.NotNull(MediaWriter.Probe(file), "the file reads back");
            Check.Eq((info.Width, info.Height), (64, 64), "65 rounded down to 64");
            Check.Near(info.Duration.TotalSeconds, 1, 0.4, "duration");

            string never = Path.Combine(dir, "never.mp4");
            using (var r = RecorderProcess.Start(new ScreenRect(0, 0, 64, 64), never, false, false))
            {
                Check.True(r.Ready.Wait(20_000), "ready");
                var result = r.Stop().GetAwaiter().GetResult();
                Check.Eq(result.ErrorCode, "cancelled", "stopped before go");
            }
            Check.True(!File.Exists(never), "no file without go");

            using (var r = RecorderProcess.Start(new ScreenRect(0, 0, 64, 64), Path.Combine(dir, "no-such-folder", "x.mp4"), false, false))
            {
                var e = Check.NotNull(Catch(() => r.Ready.Wait(20_000)), "a folder that isn't there fails");
                Check.Eq((e.InnerException as RecorderException)?.Code, "io", "as io");
            }
        }
        finally
        {
            RecorderProcess.ExePath = null;
            Directory.Delete(dir, true);
        }
    }

    private static Exception? Catch(Action a)
    {
        try
        {
            a();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-record-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static float[] Fill(int frames, float v)
    {
        var a = new float[frames * 2];
        Array.Fill(a, v);
        return a;
    }

    /// An encoder like the sink writer: it keeps the last picture until the next one comes, and each one for at least
    /// HoldFor (a made-up clock).
    private sealed class SlowSink : IFrameSink
    {
        private readonly long[] freeAt = new long[MediaWriter.FrameSlots];
        private readonly bool[] waiting = new bool[MediaWriter.FrameSlots]; // filled, not written yet
        private int lastWritten = -1;

        public long Now, HoldFor;
        public readonly List<(long Time, long Duration)> Writes = new();

        public int Slots => MediaWriter.FrameSlots;

        public bool IsFree(int slot) => slot != lastWritten && freeAt[slot] <= Now;

        public void Fill(int slot)
        {
            Check.True(!waiting[slot] && IsFree(slot), "a picture still in use was overwritten");
            waiting[slot] = true;
        }

        public void Write(int slot, long time, long duration)
        {
            Check.True(waiting[slot], "only a filled picture is written");
            waiting[slot] = false;
            Writes.Add((time, duration));
            freeAt[slot] = Now + HoldFor;
            lastWritten = slot;
        }
    }

    /// The real writer with a moving synthetic picture.
    private sealed class WriterSink(MediaWriter writer, IntPtr picture, byte[] px, int w, int h) : IFrameSink
    {
        private int frame, filled = -1;

        public int Slots => MediaWriter.FrameSlots;

        public bool IsFree(int slot) => writer.SlotFree(slot);

        /// The encoder works on its own threads: give it a moment to let go of the picture before the next tick.
        public void WaitForSlot()
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (true)
            {
                for (int i = 0; i < Slots; i++)
                    if (i != filled && writer.SlotFree(i)) return;
                if (DateTime.UtcNow > until) throw new InvalidOperationException("the writer never let go of a frame");
                Thread.Sleep(1);
            }
        }

        public void Fill(int slot)
        {
            int bar = frame * 4 % w;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool on = Math.Abs(x - bar) < 8;
                    px[i] = (byte)(on ? 255 : x);
                    px[i + 1] = (byte)(on ? 255 : y);
                    px[i + 2] = (byte)(on ? 255 : frame * 4);
                    px[i + 3] = 255;
                }
            Marshal.Copy(px, 0, picture, px.Length);
            writer.Fill(slot, picture);
            filled = slot;
            frame++;
        }

        public void Write(int slot, long time, long duration) => writer.WriteVideo(slot, time, duration);
    }
}
