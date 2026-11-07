using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bloom.Native;

/// The screen recorder's sound (Ocr\ScreenRecorder.cs): WASAPI in shared mode, the default playback device's loopback
/// ("sys") and the default microphone ("mic"), both asked for 48 kHz float stereo so Windows converts whatever the
/// devices run at. A thread of its own drains them every 10 ms into the AudioMixer, placed by each packet's clock time.
/// Methods are called through their vtable slots (mmdeviceapi.h, audioclient.h order); IUnknown takes 0–2.
internal sealed unsafe class AudioCapture : IDisposable
{
    private readonly List<IntPtr> clients = new(), captures = new();
    private readonly AudioMixer mixer;
    private Thread? thread;
    private volatile bool stop;
    private long start;

    private AudioCapture(AudioMixer mixer) => this.mixer = mixer;

    /// Opens the asked devices (not started), each one a source of `mixer` in this order: system, then mic.
    /// Throws COMException when a device is missing or refuses the format.
    public static AudioCapture Open(bool system, bool mic, AudioMixer mixer)
    {
        var a = new AudioCapture(mixer);
        try
        {
            _ = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED; the recorder's thread is MTA already
            var clsid = CLSID_MMDeviceEnumerator;
            var iid = IID_IMMDeviceEnumerator;
            Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out IntPtr enumerator));
            try
            {
                if (system) a.Add(enumerator, 0 /* eRender */, 0x00020000 /* AUDCLNT_STREAMFLAGS_LOOPBACK */);
                if (mic) a.Add(enumerator, 1 /* eCapture */, 0);
            }
            finally
            {
                Marshal.Release(enumerator);
            }
            return a;
        }
        catch
        {
            a.Dispose();
            throw;
        }
    }

    private void Add(IntPtr enumerator, int flow, uint flags)
    {
        IntPtr device = IntPtr.Zero, client = IntPtr.Zero, capture = IntPtr.Zero;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)WinRt.Slot(enumerator, 4))(enumerator, flow, 0 /* eConsole */, &device));
            var iid = IID_IAudioClient;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)WinRt.Slot(device, 3))(device, &iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, &client));
            var format = new WAVEFORMATEX
            {
                wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
                nChannels = AudioMixer.Channels,
                nSamplesPerSec = AudioMixer.Rate,
                wBitsPerSample = 32,
                nBlockAlign = AudioMixer.Channels * 4,
                nAvgBytesPerSec = AudioMixer.Rate * AudioMixer.Channels * 4,
            };
            // AUTOCONVERTPCM | SRC_DEFAULT_QUALITY: Windows resamples and remixes to the format asked for
            flags |= 0x80000000 | 0x08000000;
            // a second of device buffer: the thread drains it every 10 ms, so a stall of the PC loses nothing
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, WAVEFORMATEX*, Guid*, int>)WinRt.Slot(client, 3))(client, 0 /* shared */, flags, 10_000_000, 0, &format, null));
            var cid = IID_IAudioCaptureClient;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)WinRt.Slot(client, 14))(client, &cid, &capture));
            clients.Add(client);
            captures.Add(capture);
            client = capture = IntPtr.Zero;
        }
        finally
        {
            if (capture != IntPtr.Zero) Marshal.Release(capture);
            if (client != IntPtr.Zero) Marshal.Release(client);
            if (device != IntPtr.Zero) Marshal.Release(device);
        }
    }

    /// Starts the devices; `startTime` is the recording's start in QPC 100-ns units (the clock packets are stamped with).
    public void Start(long startTime)
    {
        start = startTime;
        foreach (var c in clients) Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(c, 10))(c));
        thread = new Thread(Drain) { IsBackground = true, Name = "record audio" };
        thread.Start();
    }

    /// Stops the thread after one last drain.
    public void Stop()
    {
        stop = true;
        thread?.Join();
        thread = null;
        foreach (var c in clients) _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(c, 11))(c);
    }

    private void Drain()
    {
        _ = CoInitializeEx(IntPtr.Zero, 0);
        var dead = new bool[captures.Count];
        while (true)
        {
            bool last = stop;
            for (int i = 0; i < captures.Count; i++)
                if (!dead[i] && !Read(i))
                    dead[i] = true; // the device went away (unplugged, disabled): that source stays silent
            if (last) return;
            Thread.Sleep(10);
        }
    }

    private bool Read(int source)
    {
        IntPtr c = captures[source];
        var next = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)WinRt.Slot(c, 5);
        var get = (delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, ulong*, ulong*, int>)WinRt.Slot(c, 3);
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)WinRt.Slot(c, 4);
        while (true)
        {
            uint packet;
            if (next(c, &packet) < 0) return false;
            if (packet == 0) return true;
            byte* data;
            uint frames, flags;
            ulong position, qpc;
            if (get(c, &data, &frames, &flags, &position, &qpc) < 0) return false;
            long at = ((long)qpc - start) * AudioMixer.Rate / 10_000_000;
            mixer.Push(source, at, new ReadOnlySpan<float>(data, (int)frames * AudioMixer.Channels), (flags & 0x2 /* AUDCLNT_BUFFERFLAGS_SILENT */) != 0);
            if (release(c, frames) < 0) return false;
        }
    }

    public void Dispose()
    {
        if (thread is not null) Stop();
        foreach (var p in captures) Marshal.Release(p);
        foreach (var p in clients) Marshal.Release(p);
        captures.Clear();
        clients.Clear();
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public int nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    private static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
    private static readonly Guid IID_IAudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
    private static readonly Guid IID_IAudioCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint init);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);
}

/// The recording's sound on one timeline: each source's samples sit in a ring at their place in time (48 kHz frames
/// since the start), and Mix hands out the sum, clamped, as 16-bit PCM. A source with nothing there (loopback while
/// nothing plays, a late packet) counts as silence, so the track runs on with the clock. Each ring holds two seconds;
/// what would go past that is dropped, so nothing grows.
internal sealed class AudioMixer
{
    public const int Rate = MediaWriter.AudioRate, Channels = MediaWriter.AudioChannels;
    /// Packets this close to where the source's last one ended join on without a gap (clock jitter, device drift).
    public const int Slack = Rate / 100;

    private readonly float[][] rings;
    private readonly long[] ends; // per source: the frame after its last sample
    private readonly int capacity;
    private readonly object gate = new();
    private long mixed; // frames handed out so far

    public AudioMixer(int sources, int capacity = Rate * 2)
    {
        this.capacity = capacity;
        rings = new float[sources][];
        for (int i = 0; i < sources; i++) rings[i] = new float[capacity * Channels];
        ends = new long[sources];
    }

    public int Sources => rings.Length;

    /// Frames handed out so far: where the next Mix starts.
    public long Position
    {
        get
        {
            lock (gate) return mixed;
        }
    }

    /// One packet of interleaved float stereo from `source`, its first frame due at `at`; `silent` packets count as zeros.
    public void Push(int source, long at, ReadOnlySpan<float> data, bool silent)
    {
        int frames = data.Length / Channels;
        lock (gate)
        {
            float[] ring = rings[source];
            long end = Math.Max(ends[source], mixed);
            long place = Math.Abs(at - ends[source]) <= Slack ? ends[source] : at;
            // a gap: zeros up to where this packet goes
            for (long f = end; f < Math.Min(place, mixed + capacity); f++) Put(ring, f, 0, 0);
            int skip = (int)Math.Clamp(mixed - place, 0, frames); // already mixed: too late for these
            long last = Math.Min(place + frames, mixed + capacity); // beyond the ring: dropped
            for (long f = place + skip; f < last; f++)
            {
                int i = (int)(f - place) * Channels;
                if (silent) Put(ring, f, 0, 0);
                else Put(ring, f, data[i], data[i + 1]);
            }
            ends[source] = Math.Max(end, last);
        }
    }

    /// Mixes the frames from Position up to `until` (at most what `pcm` holds) into `pcm`; returns the frames written
    /// and, in `at`, the frame they start at.
    public int Mix(long until, Span<short> pcm, out long at)
    {
        lock (gate)
        {
            at = mixed;
            int frames = (int)Math.Clamp(until - mixed, 0, Math.Min(pcm.Length / Channels, capacity));
            for (int n = 0; n < frames; n++)
            {
                long f = mixed + n;
                int r = (int)(f % capacity) * Channels;
                float left = 0, right = 0;
                for (int s = 0; s < rings.Length; s++)
                {
                    if (f >= ends[s]) continue;
                    left += rings[s][r];
                    right += rings[s][r + 1];
                }
                pcm[n * 2] = Sample(left);
                pcm[n * 2 + 1] = Sample(right);
            }
            mixed += frames;
            return frames;
        }
    }

    private void Put(float[] ring, long frame, float left, float right)
    {
        int r = (int)(frame % capacity) * Channels;
        ring[r] = left;
        ring[r + 1] = right;
    }

    private static short Sample(float v) => (short)(Math.Clamp(v, -1f, 1f) * 32767f);
}
