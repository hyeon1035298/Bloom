using System;
using System.Runtime.InteropServices;

namespace Bloom.Native;

/// Media Foundation's sink writer: H.264 video, and AAC audio when asked, into an MP4 file (the screen recorder,
/// Ocr\ScreenRecorder.cs). Frames go in as top-down RGB32 and the writer converts them itself. The frames live in
/// FrameSlots reused buffers, so memory stays the same however long it records. One thread calls it.
/// Methods are called through their vtable slots (mfobjects.h, mfreadwrite.h order); IUnknown takes 0–2.
internal sealed unsafe class MediaWriter : IDisposable
{
    public const int Fps = 30;
    public const int AudioRate = 48000, AudioChannels = 2;
    /// The sink writer keeps the last frame written until the next one comes (measured), the timeline keeps one
    /// waiting for its length, and one is being filled.
    public const int FrameSlots = 3;

    private IntPtr writer;
    private readonly uint videoStream, audioStream;
    private readonly IntPtr[] samples = new IntPtr[FrameSlots], buffers = new IntPtr[FrameSlots];
    private readonly int frameBytes;

    public int Width { get; }
    public int Height { get; }
    public bool Audio { get; }

    /// H.264 wants even sizes: one pixel less on an odd side.
    public static int Even(int n) => n & ~1;

    /// About 0.1 bit per pixel per frame, kept between 1 and 40 Mbit/s.
    public static int Bitrate(int width, int height) => (int)Math.Clamp((long)width * height * Fps / 10, 1_000_000, 40_000_000);

    /// 100-ns units, the frame's start on the 30 fps grid.
    public static long FrameTime(long frame) => frame * 10_000_000 / Fps;

    /// 100-ns units, the start of an audio frame (48 kHz).
    public static long AudioTime(long frame) => frame * 10_000_000 / AudioRate;

    /// The file, created (or replaced) now. A hardware encoder first, the software one when the hardware one turns the
    /// format down. Throws COMException; a Win32 HRESULT (0x8007xxxx) is the file itself (no folder, no access, full disk).
    public static MediaWriter Create(string file, int width, int height, bool audio)
    {
        Startup();
        try
        {
            return new MediaWriter(file, width, height, audio, true);
        }
        catch (COMException e) when (!IsFileError(e.HResult))
        {
            return new MediaWriter(file, width, height, audio, false);
        }
    }

    public static bool IsFileError(int hr) => (hr & 0xFFFF0000) == 0x80070000;

    private MediaWriter(string file, int width, int height, bool audio, bool hardware)
    {
        Width = width;
        Height = height;
        Audio = audio;
        frameBytes = width * height * 4;
        IntPtr attrs = IntPtr.Zero, type = IntPtr.Zero;
        try
        {
            Check(MFCreateAttributes(out attrs, 2));
            SetU32(attrs, MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, hardware ? 1u : 0u);
            SetGuid(attrs, MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_MPEG4);
            // MF_SINK_WRITER_DISABLE_THROTTLING stays off: WriteSample waits rather than queueing frames without end
            Check(MFCreateSinkWriterFromURL(file, IntPtr.Zero, attrs, out writer));

            type = MediaType(MFMediaType_Video, MFVideoFormat_H264);
            SetU32(type, MF_MT_AVG_BITRATE, (uint)Bitrate(width, height));
            SetVideoShape(type);
            SetU32(type, MF_MT_MPEG2_PROFILE, 100); // eAVEncH264VProfile_High
            uint index;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)WinRt.Slot(writer, 3))(writer, type, &index));
            videoStream = index;
            Release(ref type);

            type = MediaType(MFMediaType_Video, MFVideoFormat_RGB32);
            SetVideoShape(type);
            SetU32(type, MF_MT_DEFAULT_STRIDE, (uint)(width * 4)); // positive: top-down, as GDI's DIB is laid out
            Release(ref attrs);
            Check(MFCreateAttributes(out attrs, 1));
            // low latency: no B-frames or look-ahead, so the encoder holds a few frames instead of dozens
            SetU32(attrs, CODECAPI_AVLowLatencyMode, 1);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)WinRt.Slot(writer, 4))(writer, videoStream, type, attrs));
            Release(ref type);

            if (audio)
            {
                type = MediaType(MFMediaType_Audio, MFAudioFormat_AAC);
                SetU32(type, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                SetU32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
                SetU32(type, MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
                SetU32(type, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000); // 192 kbit/s, one of the encoder's four rates
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)WinRt.Slot(writer, 3))(writer, type, &index));
                audioStream = index;
                Release(ref type);

                // 16-bit PCM in: the AAC encoder takes nothing else, and the mixer clamps anyway
                type = MediaType(MFMediaType_Audio, MFAudioFormat_PCM);
                SetU32(type, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                SetU32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
                SetU32(type, MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
                SetU32(type, MF_MT_AUDIO_BLOCK_ALIGNMENT, AudioChannels * 2);
                SetU32(type, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AudioRate * AudioChannels * 2);
                SetU32(type, MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)WinRt.Slot(writer, 4))(writer, audioStream, type, IntPtr.Zero));
                Release(ref type);
            }

            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(writer, 5))(writer)); // BeginWriting

            for (int i = 0; i < FrameSlots; i++)
            {
                Check(MFCreateMemoryBuffer((uint)frameBytes, out buffers[i]));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)WinRt.Slot(buffers[i], 6))(buffers[i], (uint)frameBytes)); // SetCurrentLength
                Check(MFCreateSample(out samples[i]));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRt.Slot(samples[i], 42))(samples[i], buffers[i])); // AddBuffer
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Release(ref type);
            Release(ref attrs);
        }

        void SetVideoShape(IntPtr t)
        {
            SetU32(t, MF_MT_INTERLACE_MODE, 2); // progressive
            SetU64(t, MF_MT_FRAME_SIZE, ((ulong)(uint)width << 32) | (uint)height);
            SetU64(t, MF_MT_FRAME_RATE, ((ulong)Fps << 32) | 1);
            SetU64(t, MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
        }
    }

    /// The slot is no longer held by the writer (only this object and the sample hold its buffer).
    public bool SlotFree(int slot)
    {
        Marshal.AddRef(samples[slot]);
        int sample = Marshal.Release(samples[slot]);
        Marshal.AddRef(buffers[slot]);
        int buffer = Marshal.Release(buffers[slot]);
        return sample == 1 && buffer == 2;
    }

    /// Copies one frame (Width × Height × 4 bytes, top-down BGRX) into the slot.
    public void Fill(int slot, IntPtr bits)
    {
        IntPtr b = buffers[slot];
        byte* dst;
        uint max, cur;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)WinRt.Slot(b, 3))(b, &dst, &max, &cur));
        try
        {
            Buffer.MemoryCopy((void*)bits, dst, max, frameBytes);
        }
        finally
        {
            _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(b, 4))(b);
        }
    }

    /// The slot's frame shown from `time` for `duration` (100-ns units). May wait while the encoder catches up.
    public void WriteVideo(int slot, long time, long duration) => Write(videoStream, samples[slot], time, duration);

    /// Interleaved 16-bit stereo starting at audio frame `at` (48 kHz frames since the start).
    public void WriteAudio(ReadOnlySpan<short> pcm, long at)
    {
        if (!Audio || pcm.Length < AudioChannels) return;
        int frames = pcm.Length / AudioChannels, bytes = frames * AudioChannels * 2;
        IntPtr buffer = IntPtr.Zero, sample = IntPtr.Zero;
        try
        {
            Check(MFCreateMemoryBuffer((uint)bytes, out buffer));
            byte* dst;
            uint max, cur;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)WinRt.Slot(buffer, 3))(buffer, &dst, &max, &cur));
            fixed (short* src = pcm) Buffer.MemoryCopy(src, dst, max, bytes);
            _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(buffer, 4))(buffer);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)WinRt.Slot(buffer, 6))(buffer, (uint)bytes));
            Check(MFCreateSample(out sample));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRt.Slot(sample, 42))(sample, buffer));
            long time = AudioTime(at);
            Write(audioStream, sample, time, AudioTime(at + frames) - time);
        }
        finally
        {
            Release(ref sample);
            Release(ref buffer);
        }
    }

    private void Write(uint stream, IntPtr sample, long time, long duration)
    {
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)WinRt.Slot(sample, 36))(sample, time));
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)WinRt.Slot(sample, 38))(sample, duration));
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)WinRt.Slot(writer, 6))(writer, stream, sample));
    }

    /// Finalize: the file is complete and playable once this returns.
    public void Finish() => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(writer, 11))(writer));

    public void Dispose()
    {
        for (int i = 0; i < FrameSlots; i++)
        {
            Release(ref samples[i]);
            Release(ref buffers[i]);
        }
        Release(ref writer);
    }

    /// What an MP4 holds, read back with the source reader (the tests and the recorder's checks), or null.
    public static MediaInfo? Probe(string file)
    {
        Startup();
        if (MFCreateSourceReaderFromURL(file, IntPtr.Zero, out IntPtr reader) < 0) return null;
        IntPtr type = IntPtr.Zero;
        try
        {
            var key = MF_PD_DURATION;
            var pv = new PropVariant();
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, PropVariant*, int>)WinRt.Slot(reader, 12))(reader, 0xFFFFFFFF, &key, &pv) < 0) return null;
            long duration = pv.Vt == 21 /* VT_UI8 */ ? (long)pv.Pointer : 0;
            _ = PropertyStore.PropVariantClear(ref pv);

            int width = 0, height = 0;
            var getType = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)WinRt.Slot(reader, 6);
            IntPtr t;
            if (getType(reader, 0xFFFFFFFC /* MF_SOURCE_READER_FIRST_VIDEO_STREAM */, &t) >= 0)
            {
                type = t;
                ulong size;
                var k = MF_MT_FRAME_SIZE;
                if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)WinRt.Slot(type, 8))(type, &k, &size) >= 0)
                {
                    width = (int)(size >> 32);
                    height = (int)(size & 0xFFFFFFFF);
                }
                Release(ref type);
            }
            int rate = 0, channels = 0;
            if (getType(reader, 0xFFFFFFFD /* MF_SOURCE_READER_FIRST_AUDIO_STREAM */, &t) >= 0)
            {
                type = t;
                rate = (int)GetU32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND);
                channels = (int)GetU32(type, MF_MT_AUDIO_NUM_CHANNELS);
                Release(ref type);
            }
            return new MediaInfo(TimeSpan.FromTicks(duration), width, height, rate, channels);
        }
        finally
        {
            Release(ref type);
            Release(ref reader);
        }
    }

    private static bool started;
    private static readonly object StartGate = new();

    private static void Startup()
    {
        lock (StartGate)
        {
            if (started) return;
            Check(MFStartup(0x00020070 /* MF_VERSION */, 0));
            started = true; // never shut down: the recorder is its own process, and the tests open several files
        }
    }

    private static IntPtr MediaType(Guid major, Guid subtype)
    {
        Check(MFCreateMediaType(out IntPtr t));
        SetGuid(t, MF_MT_MAJOR_TYPE, major);
        SetGuid(t, MF_MT_SUBTYPE, subtype);
        return t;
    }

    // IMFAttributes: GetUINT32 7, GetUINT64 8, SetUINT32 21, SetUINT64 22, SetGUID 24
    private static uint GetU32(IntPtr attrs, Guid key)
    {
        uint v;
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)WinRt.Slot(attrs, 7))(attrs, &key, &v) >= 0 ? v : 0;
    }

    private static void SetU32(IntPtr attrs, Guid key, uint v) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)WinRt.Slot(attrs, 21))(attrs, &key, v));

    private static void SetU64(IntPtr attrs, Guid key, ulong v) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)WinRt.Slot(attrs, 22))(attrs, &key, v));

    private static void SetGuid(IntPtr attrs, Guid key, Guid v) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)WinRt.Slot(attrs, 24))(attrs, &key, &v));

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static void Release(ref IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.Release(p);
        p = IntPtr.Zero;
    }

    private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    private static readonly Guid MFTranscodeContainerType_MPEG4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    private static readonly Guid CODECAPI_AVLowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private static readonly Guid MF_PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, uint size);
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
}

/// An MP4's length, picture size and audio track (rate and channels 0 when it has none).
internal sealed record MediaInfo(TimeSpan Duration, int Width, int Height, int AudioRate, int AudioChannels)
{
    public bool HasAudio => AudioRate > 0;
}
