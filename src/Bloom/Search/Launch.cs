using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

/// Starting things the way Explorer would: ShellExecute keeps a shortcut's arguments, working
/// folder and run-as settings; AppsFolder entries go through `shell:AppsFolder\<AUMID>`.
internal static class Launch
{
    /// Starts it and returns at once; a failure is logged. ShellExecuteEx returns only when the start is done (a Store
    /// app's activation can take seconds, a UAC prompt waits for its answer), so it runs on a thread of its own: on
    /// the UI thread Bloom stopped answering meanwhile.
    public static void Open(string target, string? args = null, string verb = "open", string? dir = null) => _ = Start(target, args, verb, dir);

    /// Open for a caller that needs the answer: true once it started, false when it couldn't (UAC declined, the file
    /// gone). The answer comes back on the caller's context (the UI thread for UI code).
    public static Task<bool> Start(string target, string? args = null, string verb = "open", string? dir = null)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // not a background thread: a Bloom quitting right after (설정 › 제거 shows its exe in Explorer, then quits) still
        // finishes the start
        var thread = new Thread(() => started.SetResult(Execute(target, args, verb, dir))) { IsBackground = false, Name = "Bloom launch" };
        thread.SetApartmentState(ApartmentState.STA); // ShellExecuteEx may hand the start to COM shell extensions
        thread.Start();
        return started.Task;
    }

    public static void RunAsAdmin(string target) => Open(target, verb: "runas");

    /// ShellExecuteEx, waiting until the start is done (SEE_MASK_NOASYNC: the thread may end right after).
    private static bool Execute(string target, string? args, string verb, string? dir)
    {
        var info = new Win32.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<Win32.SHELLEXECUTEINFO>(),
            fMask = Win32.SEE_MASK_FLAG_NO_UI | Win32.SEE_MASK_NOASYNC,
            lpVerb = verb,
            lpFile = target,
            lpParameters = args,
            lpDirectory = dir,
            nShow = Win32.SW_SHOW,
        };
        try
        {
            // not while another program is being started with inheritable handles (ChildProcess.Launching); an elevation prompt
            // can wait for a person, so it is left out of the gate
            if (verb == "runas" ? Win32.ShellExecuteEx(ref info) : Ai.ChildProcess.Launching(() => Win32.ShellExecuteEx(ref info))) return true;
            Log.Write($"ShellExecute failed {Marshal.GetLastWin32Error()} {verb} {target}");
        }
        catch (Exception e)
        {
            Log.Error("launch", e);
        }
        return false;
    }

    /// Explorer with the item selected (falls back to opening the folder).
    public static void Reveal(string path)
    {
        try
        {
            IntPtr pidl = Win32.ILCreateFromPath(path);
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    if (Win32.SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0) return;
                }
                finally
                {
                    Win32.ILFree(pidl);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("reveal", e);
        }
        string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (dir is not null) Open(dir);
    }

    /// Text on the clipboard; false when it couldn't be put there (another app kept holding it).
    public static bool Copy(string text) => Put(text, "clipboard");

    /// The toast after a copy: `done`, or that the clipboard couldn't be had.
    public static string Copied(bool ok, string done) => ok ? done : Tr("클립보드에 복사하지 못했습니다. 다시 시도하세요.", "Could not copy to the clipboard. Try again.");

    /// What puts data on the clipboard (to stay after Bloom quits); a test puts in one that fails.
    internal static Action<object> SetClipboard { get; set; } = data => Clipboard.SetDataObject(data, true);

    /// A picture to paste anywhere: the bitmap for every app, plus "PNG" (transparency, browsers, Office) when its file is given.
    public static bool CopyImage(BitmapSource picture, string? png = null) =>
        PutImage(picture, png is not null && File.Exists(png) ? File.ReadAllBytes(png) : null);

    /// A picture drawn here (컬러 팔레트's): the bitmap plus its "PNG", encoded in memory.
    public static bool CopyDrawn(BitmapSource picture)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(picture));
        using var png = new MemoryStream();
        enc.Save(png);
        return PutImage(picture, png.ToArray());
    }

    private static bool PutImage(BitmapSource picture, byte[]? png)
    {
        byte[] dib;
        try
        {
            dib = Dib(picture);
        }
        catch (Exception e) // too big for one array, a picture WIC can't convert: said as a failed copy, as before
        {
            Log.Error("clipboard image", e);
            return false;
        }
        var obj = new DataObject();
        obj.SetData(DataFormats.Dib, new MemoryStream(dib, false));
        if (png is not null) obj.SetData("PNG", new MemoryStream(png));
        return Put(obj, "clipboard image");
    }

    /// The picture as CF_DIB: a BITMAPINFOHEADER and its 32-bit BGRA rows, bottom-up. Windows makes CF_BITMAP and
    /// CF_DIBV5 from it for the apps that ask for those. Not DataObject.SetImage: .NET 10's WPF renders that through a
    /// GDI+ bitmap it never deletes (WpfOleServices.GetCompatibleBitmap), so every copy left a picture-sized block and
    /// a GDI handle behind until Bloom quit.
    internal static byte[] Dib(BitmapSource picture)
    {
        BitmapSource src = picture.Format == System.Windows.Media.PixelFormats.Bgra32 ? picture : new FormatConvertedBitmap(picture, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        const int head = 40;
        var dib = new byte[head + stride * h];
        var span = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, head); // biSize
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], h); // positive: bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1); // biPlanes
        BinaryPrimitives.WriteInt16LittleEndian(span[14..], 32); // biBitCount; biCompression 0 = BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], stride * h); // biSizeImage
        for (int y = 0; y < h; y++) src.CopyPixels(new Int32Rect(0, h - 1 - y, w, 1), dib, stride, head + y * stride);
        return dib;
    }

    /// Clipboard.SetDataObject, tried a few times while another app holds the clipboard.
    private static bool Put(object data, string what)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                SetClipboard(data);
                return true;
            }
            catch (COMException)
            {
                System.Threading.Thread.Sleep(30); // clipboard held by another app
            }
            catch (Exception e)
            {
                Log.Error(what, e);
                return false;
            }
        }
        return false;
    }
}
