using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Native;

/// A short video file decoded on a loop into frames for the screen (the tutorial's first page). Media Foundation's
/// source reader decodes it in software on a thread of its own, its video processor turns each picture into top-down
/// BGRX at the size asked for, and a few frames wait ahead of the one on screen. At the end it seeks back to the start
/// and the frames' times run on, so the loop has no seam. Disposing it ends the thread, which releases the reader, the
/// decoder and its own Media Foundation start; the frames go to the GC, so nothing of it stays in the process (the
/// GPU's decoder would leave its driver's memory behind). The video only: a sound track is never read.
/// Methods are called through their vtable slots (mfreadwrite.h, mfobjects.h, strmif.h order), as MediaWriter does.
internal sealed unsafe class ClipReader : IDisposable
{
    /// Frames decoded ahead of the screen: decoding the start again after the seek at the end takes a few frames' time,
    /// and these cover it.
    public const int Ahead = 3;

    // the decoder's own threads: one per processor by default, each holding pictures of its own (on 16 processors that
    // was about 100 MB more while it played); one decodes a frame of this size in a few milliseconds
    private const int Workers = 1;

    /// One decoded picture (Width × Height × 4 bytes, top-down BGRX). `Time` is when it is due, in 100-ns units from
    /// the first frame and counting on across loops; `At` is where it is in the file.
    public sealed class Frame(byte[] pixels)
    {
        public byte[] Pixels { get; } = pixels;
        public long Time { get; internal set; }
        public long At { get; internal set; }
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

    private readonly string file;
    private readonly Action<string> failed;
    private readonly object gate = new();
    private readonly Queue<Frame> ready = new(); // decoded, in time order
    private readonly Stack<Frame> free = new();  // handed back, to be filled again
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int made;
    private bool stopping;

    private ClipReader(string file, int width, int height, Action<string> failed)
    {
        this.file = file;
        Width = width;
        Height = height;
        this.failed = failed;
    }

    /// Starts decoding `file` scaled to `width` × `height`. When the file cannot be read or decoded the thread stops
    /// and `failed` is called once, on that thread, with why (never after Dispose).
    public static ClipReader Start(string file, int width, int height, Action<string> failed)
    {
        var r = new ClipReader(file, width, height, failed);
        // Media Foundation's objects are free-threaded; the thread joins the multithreaded apartment they live in
        var t = new Thread(r.Run) { IsBackground = true, Name = "clip reader" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        return r;
    }

    /// Done once the thread has let go of the reader and of its Media Foundation start (tests).
    internal Task Ended => ended.Task;

    /// When the first frame waiting is due, or null while none is.
    public long? Next
    {
        get
        {
            lock (gate) return ready.Count > 0 ? ready.Peek().Time : null;
        }
    }

    /// The newest frame due by `time` (those before it are passed over), or null when none is. Hand it back with
    /// Return once its pixels are copied.
    public Frame? Due(long time)
    {
        lock (gate)
        {
            Frame? due = null;
            while (ready.Count > 0 && ready.Peek().Time <= time)
            {
                if (due is not null)
                {
                    free.Push(due);
                    Monitor.PulseAll(gate);
                }
                due = ready.Dequeue();
            }
            return due;
        }
    }

    public void Return(Frame frame)
    {
        lock (gate)
        {
            if (stopping) return;
            free.Push(frame);
            Monitor.PulseAll(gate);
        }
    }

    /// Does not wait: the thread lets go of everything within a frame's decoding.
    public void Dispose()
    {
        lock (gate)
        {
            stopping = true;
            ready.Clear();
            free.Clear();
            Monitor.PulseAll(gate);
        }
    }

    private bool Stopping
    {
        get
        {
            lock (gate) return stopping;
        }
    }

    private void Run()
    {
        IntPtr attrs = IntPtr.Zero, reader = IntPtr.Zero, type = IntPtr.Zero;
        bool started = false;
        try
        {
            WinRt.Check(MFStartup(0x00020070 /* MF_VERSION */, 1 /* MFSTARTUP_LITE: no sockets */));
            started = true;
            WinRt.Check(MFCreateAttributes(out attrs, 1));
            // the video processor after the decoder: colour conversion and scaling to the size on screen
            SetU32(attrs, MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
            WinRt.Check(MFCreateSourceReaderFromURL(file, attrs, out reader));
            Release(ref attrs);

            var select = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)WinRt.Slot(reader, 4);
            WinRt.Check(select(reader, AllStreams, 0));
            WinRt.Check(select(reader, FirstVideo, 1));

            // the file's own frame rate, kept through the processor; it also gives a frame's length when a sample has none
            var current = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)WinRt.Slot(reader, 6);
            IntPtr t;
            WinRt.Check(current(reader, FirstVideo, &t));
            type = t;
            ulong rate = GetU64(type, MF_MT_FRAME_RATE);
            Release(ref type);
            uint num = (uint)(rate >> 32), den = (uint)rate;
            long frameLength = num > 0 && den > 0 ? 10_000_000L * den / num : 333_333;

            WinRt.Check(MFCreateMediaType(out type));
            SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGuid(type, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            SetU64(type, MF_MT_FRAME_SIZE, ((ulong)(uint)Width << 32) | (uint)Height);
            SetU64(type, MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
            SetU32(type, MF_MT_INTERLACE_MODE, 2); // progressive
            SetU32(type, MF_MT_DEFAULT_STRIDE, (uint)Stride); // positive: top-down
            if (rate != 0) SetU64(type, MF_MT_FRAME_RATE, rate);
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, int>)WinRt.Slot(reader, 7))(reader, FirstVideo, null, type));
            Release(ref type);
            OneWorker(reader);

            // the rows as it settled on them (Lock2D says so per frame where the buffer can)
            WinRt.Check(current(reader, FirstVideo, &t));
            type = t;
            int pitch = (int)GetU32(type, MF_MT_DEFAULT_STRIDE, (uint)Stride);
            Release(ref type);

            var read = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)WinRt.Slot(reader, 9);
            long start = 0, end = 0; // when this pass's first frame is due, and when the last frame read ends
            long first = -1;         // this pass's first sample time in the file, -1 before it
            while (!Stopping)
            {
                uint index, flags;
                long stamp;
                IntPtr sample;
                WinRt.Check(read(reader, FirstVideo, 0, &index, &flags, &stamp, &sample));
                if ((flags & ReaderError) != 0) throw new InvalidDataException("the video stream failed");
                if ((flags & CurrentTypeChanged) != 0)
                {
                    WinRt.Check(current(reader, FirstVideo, &t));
                    type = t;
                    pitch = (int)GetU32(type, MF_MT_DEFAULT_STRIDE, (uint)Stride);
                    Release(ref type);
                }
                if (sample != IntPtr.Zero)
                {
                    try
                    {
                        if (first < 0) first = stamp;
                        long length;
                        if (((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)WinRt.Slot(sample, 37))(sample, &length) < 0 || length <= 0) length = frameLength;
                        var f = Take();
                        if (f is null) return;
                        Copy(sample, f.Pixels, pitch);
                        f.At = stamp - first;
                        f.Time = start + f.At;
                        end = f.Time + length;
                        lock (gate)
                        {
                            if (stopping) return;
                            ready.Enqueue(f);
                        }
                    }
                    finally
                    {
                        Release(ref sample);
                    }
                }
                if ((flags & EndOfStream) != 0)
                {
                    if (first < 0) throw new InvalidDataException("no pictures");
                    // the start again; its first frame follows the last one by that frame's length
                    start = end;
                    first = -1;
                    var zero = new PropVariant { Vt = 20 /* VT_I8 */ };
                    var format = Guid.Empty; // 100-ns units
                    WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PropVariant*, int>)WinRt.Slot(reader, 8))(reader, &format, &zero));
                }
            }
        }
        catch (Exception e)
        {
            if (!Stopping) failed(e.Message);
        }
        finally
        {
            Release(ref type);
            Release(ref attrs);
            Release(ref reader);
            if (started) _ = MFShutdown();
            Dispose();
            ended.TrySetResult();
        }
    }

    // asks the decoder (the reader's first transform) for one thread of its own; a decoder that will not is left as it is
    private static void OneWorker(IntPtr reader)
    {
        IntPtr ex = IntPtr.Zero, decoder = IntPtr.Zero, codec = IntPtr.Zero;
        try
        {
            var iid = IID_IMFSourceReaderEx;
            if (Marshal.QueryInterface(reader, in iid, out ex) < 0) return;
            Guid category;
            IntPtr t;
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, Guid*, IntPtr*, int>)WinRt.Slot(ex, 16))(ex, FirstVideo, 0, &category, &t) < 0) return; // GetTransformForStream
            decoder = t;
            iid = IID_ICodecAPI;
            if (Marshal.QueryInterface(decoder, in iid, out codec) < 0) return;
            var key = CODECAPI_AVDecNumWorkerThreads;
            var v = new PropVariant { Vt = 3 /* VT_I4 */, Pointer = Workers };
            _ = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PropVariant*, int>)WinRt.Slot(codec, 9))(codec, &key, &v); // SetValue
        }
        finally
        {
            Release(ref codec);
            Release(ref decoder);
            Release(ref ex);
        }
    }

    // a frame to fill: one handed back, a new one while fewer than Ahead exist, or null once stopping
    private Frame? Take()
    {
        lock (gate)
        {
            while (free.Count == 0 && made >= Ahead && !stopping) Monitor.Wait(gate);
            if (stopping) return null;
            if (free.Count > 0) return free.Pop();
            made++;
        }
        return new Frame(new byte[Stride * Height]);
    }

    // the sample's picture, row by row when the processor's rows are laid out otherwise (bottom-up, or padded)
    private void Copy(IntPtr sample, byte[] into, int pitch)
    {
        IntPtr buffer;
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)WinRt.Slot(sample, 41))(sample, &buffer)); // ConvertToContiguousBuffer
        IntPtr flat = IntPtr.Zero;
        try
        {
            var iid = IID_IMF2DBuffer;
            byte* rows;
            if (Marshal.QueryInterface(buffer, in iid, out flat) >= 0)
            {
                int p;
                WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, int*, int>)WinRt.Slot(flat, 3))(flat, &rows, &p)); // Lock2D
                try
                {
                    CopyRows(rows, p, into);
                }
                finally
                {
                    _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(flat, 4))(flat);
                }
                return;
            }
            uint max, length;
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)WinRt.Slot(buffer, 3))(buffer, &rows, &max, &length)); // Lock
            try
            {
                if (length < (long)Math.Abs(pitch) * Height) throw new InvalidDataException("a picture shorter than its size");
                CopyRows(pitch < 0 ? rows + (long)-pitch * (Height - 1) : rows, pitch, into);
            }
            finally
            {
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(buffer, 4))(buffer);
            }
        }
        finally
        {
            Release(ref flat);
            Release(ref buffer);
        }
    }

    // `top` is the first row on screen, `pitch` the step to the next one (negative bottom-up)
    private void CopyRows(byte* top, int pitch, byte[] into)
    {
        int stride = Stride;
        fixed (byte* dst = into)
        {
            if (pitch == stride)
            {
                Buffer.MemoryCopy(top, dst, into.Length, (long)stride * Height);
                return;
            }
            for (int y = 0; y < Height; y++) Buffer.MemoryCopy(top + (long)pitch * y, dst + (long)stride * y, stride, stride);
        }
    }

    // IMFAttributes: GetUINT32 7, GetUINT64 8, SetUINT32 21, SetUINT64 22, SetGUID 24
    private static uint GetU32(IntPtr attrs, Guid key, uint fallback)
    {
        uint v;
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)WinRt.Slot(attrs, 7))(attrs, &key, &v) >= 0 ? v : fallback;
    }

    private static ulong GetU64(IntPtr attrs, Guid key)
    {
        ulong v;
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)WinRt.Slot(attrs, 8))(attrs, &key, &v) >= 0 ? v : 0;
    }

    private static void SetU32(IntPtr attrs, Guid key, uint v) =>
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)WinRt.Slot(attrs, 21))(attrs, &key, v));

    private static void SetU64(IntPtr attrs, Guid key, ulong v) =>
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)WinRt.Slot(attrs, 22))(attrs, &key, v));

    private static void SetGuid(IntPtr attrs, Guid key, Guid v) =>
        WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)WinRt.Slot(attrs, 24))(attrs, &key, &v));

    private static void Release(ref IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.Release(p);
        p = IntPtr.Zero;
    }

    private const uint AllStreams = 0xFFFFFFFE, FirstVideo = 0xFFFFFFFC;
    private const uint ReaderError = 0x1, EndOfStream = 0x2, CurrentTypeChanged = 0x20; // MF_SOURCE_READERF_*

    private static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid IID_IMF2DBuffer = new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
    private static readonly Guid IID_IMFSourceReaderEx = new("7b981cf0-560e-4116-9875-b099895f23d7");
    private static readonly Guid IID_ICodecAPI = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
    private static readonly Guid CODECAPI_AVDecNumWorkerThreads = new("9561c3e8-ea9e-4435-9b1e-a93e691894d8");

    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, uint size);
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
}
