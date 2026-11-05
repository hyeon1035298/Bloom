using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Clip;

/// Clipboard history. Listens with AddClipboardFormatListener on a message-only
/// window, skips what password managers mark as private (the formats Windows' own Win+V history
/// honours), and keeps the list DPAPI-encrypted in Local\clipboard (pictures as PNG files there).
/// Enter in its view puts an entry back and pastes it (Ctrl+V) into the window that was in front.
internal sealed class ClipboardHistory : IDisposable
{
    private const int MaxItems = 500; // unpinned
    private const int MaxText = 100_000;
    private const long MaxTotalText = 4_000_000;
    private const long MaxPixels = 40_000_000;
    private static readonly string[] PrivateApps = { "keepass", "keepassxc", "1password", "bitwarden", "enpass", "lastpass", "dashlane", "nordpass", "proton pass" };

    private readonly Dispatcher ui;
    private readonly object gate = new();
    private ClipFile data;
    private readonly bool loadedWhole;
    private HwndSource? window;
    private bool listening;
    private readonly DispatcherTimer settle, expiry;
    private IntPtr winEvent;
    private WinEventProc? winEventProc; // kept alive while hooked
    private IntPtr lastForeground;
    private readonly Saver saver;
    private readonly Dictionary<uint, string?> appNames = new();
    private Task work = Task.CompletedTask; // the copies' entries being made, one after another (Capture)
    private int busyTries;
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(150), BusyRetry = TimeSpan.FromMilliseconds(40);

    /// Raised on the UI thread when the list changed.
    public event Action? Changed;

    public ClipboardHistory(Dispatcher ui)
    {
        this.ui = ui;
        data = Load(out loadedWhole);
        saver = new Saver(Save, 600);
        settle = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = SettleTime };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            settle.Interval = SettleTime;
            Capture();
        };
        expiry = new DispatcherTimer(DispatcherPriority.Background, ui);
        expiry.Tick += (_, _) =>
        {
            expiry.Stop();
            if (Prune()) Changed?.Invoke();
        };
    }

    public IReadOnlyList<ClipEntry> Items
    {
        get
        {
            lock (gate) return data.Items.ToList();
        }
    }

    private static string Dir => Path.GetDirectoryName(Paths.InLocal(@"clipboard\history.dat"))!;

    public static string ImagePath(ClipEntry e) => Path.Combine(Dir, e.Image ?? "");

    // ── start / stop ────────────────────────────────────────────────────────────────────────────
    public void Start()
    {
        window = new HwndSource(new HwndSourceParameters("BloomClipboard") { ParentWindow = Win32.HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(WndProc);
        // the window that was in front before the launcher: where a paste goes
        winEventProc = (_, _, hwnd, _, _, _, _) =>
        {
            if (hwnd != IntPtr.Zero) lastForeground = hwnd;
        };
        winEvent = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        IntPtr fg = Win32.GetForegroundWindow();
        _ = Win32.GetWindowThreadProcessId(fg, out uint pid);
        if (pid != Environment.ProcessId) lastForeground = fg;
        Apply();
        Prune();
        if (loadedWhole) Sweep();
    }

    /// Pictures no entry names (Bloom closed while a copy's entry was being made, a crash) and half-written ones: a
    /// screenshot shouldn't stay on disk with nothing in the history to show or remove it. Only files from before this
    /// start, so a copy saved meanwhile stays.
    private void Sweep()
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (gate)
            foreach (var e in data.Items)
                if (e.Image is { } file) named.Add(file);
        var started = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            int held = 0;
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (string f in Directory.EnumerateFiles(Dir))
                {
                    string name = Path.GetFileName(f);
                    bool stray = name.EndsWith(".png.tmp", StringComparison.OrdinalIgnoreCase) || (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !named.Contains(name));
                    try
                    {
                        if (stray && File.GetLastWriteTimeUtc(f) < started) File.Delete(f);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        held++; // open in a viewer, read-only: the next start tries again, the others go now
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("clipboard sweep", e);
            }
            if (held > 0) Log.Write($"clipboard sweep: {held} in use, left for the next start");
        });
    }

    /// Recording on/off, from the settings (its shortcut is one of Host's Hotkeys).
    public void Apply()
    {
        if (window is null) return;
        bool want = Settings.Current.ClipHistory;
        if (want && !listening) listening = AddClipboardFormatListener(window.Handle);
        else if (!want && listening)
        {
            RemoveClipboardFormatListener(window.Handle);
            listening = false;
        }
    }

    private IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            // apps put several formats one after another: read once they are done
            busyTries = 0;
            settle.Stop();
            settle.Interval = SettleTime;
            settle.Start();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        expiry.Stop();
        saver.Flush();
        if (winEvent != IntPtr.Zero) UnhookWinEvent(winEvent);
        winEvent = IntPtr.Zero;
        if (window is null) return;
        if (listening) RemoveClipboardFormatListener(window.Handle);
        window.Dispose();
        window = null;
    }

    public void Flush() => saver.Flush();

    // ── reading the clipboard ───────────────────────────────────────────────────────────────────
    /// The copy is read here, on the UI thread (the clipboard's data object belongs to it); its entry is made off it,
    /// one copy after another so the list keeps their order: decoding, hashing and saving a screenshot took 150–400 ms
    /// on the UI thread (estimated), which stopped the launcher and the keyboard hook with it.
    private void Capture()
    {
        if (!Settings.Current.ClipHistory) return;
        try
        {
            IDataObject? d;
            try
            {
                d = Clipboard.GetDataObject();
            }
            catch (COMException)
            {
                // another app still holds it: again in a moment, a few times (it slept here, holding the UI thread)
                if (++busyTries > 3) return;
                settle.Interval = BusyRetry;
                settle.Start();
                return;
            }
            if (d is null || Private(d)) return;
            string? source = SourceApp(out bool privateApp);
            if (privateApp) return;
            long copied = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Read(d) is not { } make) return;
            work = work.ContinueWith(_ =>
            {
                ClipEntry? e = null;
                try
                {
                    e = make();
                }
                catch (Exception ex)
                {
                    Log.Error("clipboard read", ex);
                }
                if (e is null) return;
                e.Source = source;
                e.Copied = copied;
                ui.BeginInvoke(() => Add(e));
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log.Error("clipboard read", ex);
        }
    }

    /// The marks Windows' clipboard history and cloud clipboard honour (password managers set them).
    private static bool Private(IDataObject d)
    {
        var formats = d.GetFormats(false);
        if (formats.Contains("ExcludeClipboardContentFromMonitorProcessing") || formats.Contains("Clipboard Viewer Ignore")) return true;
        // a DWORD 0 here = "keep out of clipboard history"
        return formats.Contains("CanIncludeInClipboardHistory") && d.GetData("CanIncludeInClipboardHistory") is MemoryStream { Length: >= 4 } ms
            && BitConverter.ToInt32(ms.ToArray(), 0) == 0;
    }

    private string? SourceApp(out bool privateApp)
    {
        privateApp = false;
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero) owner = Win32.GetForegroundWindow();
        if (owner == IntPtr.Zero) return null;
        _ = Win32.GetWindowThreadProcessId(owner, out uint pid);
        if (pid == 0) return null;
        if (!appNames.TryGetValue(pid, out string? name))
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                string exe = p.ProcessName;
                string? desc = null;
                try
                {
                    desc = p.MainModule?.FileVersionInfo.FileDescription;
                }
                catch
                {
                    /* elevated or 32-bit: the process name will do */
                }
                name = string.IsNullOrWhiteSpace(desc) ? exe : desc!.Trim();
                if (PrivateApps.Any(a => exe.Contains(a.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) || name.Contains(a, StringComparison.OrdinalIgnoreCase))) name = "\0private";
            }
            catch
            {
                name = null;
            }
            if (appNames.Count > 200) appNames.Clear();
            appNames[pid] = name;
        }
        if (name == "\0private")
        {
            privateApp = true;
            return null;
        }
        return pid == Environment.ProcessId ? "Bloom" : name;
    }

    /// Files first (Explorer), then text (Office puts a picture of the cells next to the text), then a picture. What the
    /// copy holds is taken from the data object here; the returned work makes the entry on any thread.
    private static Func<ClipEntry?>? Read(IDataObject d)
    {
        if (d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            return () => new ClipEntry { Kind = "files", Files = files.ToList(), Id = "clip:" + Hash(Encoding.UTF8.GetBytes(string.Join("\n", files).ToLowerInvariant())) };
        if (d.GetDataPresent(DataFormats.UnicodeText) && d.GetData(DataFormats.UnicodeText) is string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxText) return null;
            return () => new ClipEntry { Kind = "text", Text = text, Id = "clip:" + Hash(Encoding.UTF8.GetBytes(text)) };
        }
        try
        {
            // "PNG" keeps transparency (browsers, the Snipping Tool): its bytes, decoded by the work as it reads them (a
            // band of rows at a time for the hash, again for the file), never held decoded whole
            if (d.GetDataPresent("PNG") && d.GetData("PNG") is MemoryStream png)
            {
                byte[] bytes = png.ToArray();
                return () => Picture(new PngBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0]);
            }
            // the DIB is the fallback: its bitmap belongs to this thread, so the work gets it frozen (a copy only when
            // it can't be frozen)
            if (d.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } bmp)
            {
                if ((long)bmp.PixelWidth * bmp.PixelHeight > MaxPixels) return null;
                BitmapSource frozen = bmp.CanFreeze ? bmp : new WriteableBitmap(bmp);
                frozen.Freeze();
                return () => Picture(frozen);
            }
        }
        catch (Exception e)
        {
            Log.Error("clipboard image", e);
        }
        return null;
    }

    /// A picture's entry: its pixels' hash names the PNG saved in Local\clipboard (once: the same picture copied again
    /// finds its file).
    private static ClipEntry? Picture(BitmapSource bmp)
    {
        if ((long)bmp.PixelWidth * bmp.PixelHeight > MaxPixels) return null;
        var bgra = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        string hash = Hash(bgra);
        string file = hash + ".png";
        string path = Path.Combine(Dir, file);
        if (!File.Exists(path))
        {
            // whole or not at all: Bloom may close while the pool writes it, and a cut file would keep its name
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bgra));
            using (var fs = File.Create(path + ".tmp")) enc.Save(fs);
            File.Move(path + ".tmp", path, true);
        }
        return new ClipEntry { Kind = "image", Image = file, Width = bmp.PixelWidth, Height = bmp.PixelHeight, Id = "clip:" + hash };
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes), 0, 10).ToLowerInvariant();

    /// Rows read at a time for a picture's hash: under the large-object heap's 85 KB, whose room the GC keeps long after.
    private const int HashBandBytes = 64 * 1024;

    /// The pixels' hash, the same as Hash of one array of them all (BGRA, row after row, so the files keep their names),
    /// read a band of rows at a time: no picture-sized array (a 4K picture is 32 MB).
    internal static string Hash(BitmapSource bgra)
    {
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        int rows = Math.Max(1, HashBandBytes / stride);
        var band = new byte[stride * Math.Min(rows, height)];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int y = 0; y < height; y += rows)
        {
            int n = Math.Min(rows, height - y);
            bgra.CopyPixels(new Int32Rect(0, y, width, n), band, stride, 0);
            sha.AppendData(band, 0, n * stride);
        }
        return Convert.ToHexString(sha.GetHashAndReset(), 0, 10).ToLowerInvariant();
    }

    // ── the list ────────────────────────────────────────────────────────────────────────────────
    /// Also the QA captures' sample entries.
    public void Add(ClipEntry e)
    {
        lock (gate)
        {
            int at = data.Items.FindIndex(x => x.Id == e.Id);
            if (at >= 0)
            {
                e.Pinned = data.Items[at].Pinned;
                e.Source ??= data.Items[at].Source;
                data.Items.RemoveAt(at);
            }
            data.Items.Insert(0, e);
        }
        Prune();
        saver.Touch();
        Changed?.Invoke();
    }

    public void SetPinned(ClipEntry e, bool pinned)
    {
        lock (gate)
        {
            e.Pinned = pinned;
            // the one in the list (copied again, it is another object with the same id)
            if (data.Items.Find(x => x.Id == e.Id) is { } kept) kept.Pinned = pinned;
        }
        saver.Touch();
        Changed?.Invoke();
    }

    public void Delete(ClipEntry e)
    {
        lock (gate) data.Items.RemoveAll(x => x.Id == e.Id);
        DropImage(e);
        saver.Touch();
        Changed?.Invoke();
    }

    /// Everything but the pinned entries.
    public int Clear()
    {
        List<ClipEntry> gone;
        lock (gate)
        {
            gone = data.Items.Where(x => !x.Pinned).ToList();
            data.Items.RemoveAll(x => !x.Pinned);
        }
        foreach (var e in gone) DropImage(e);
        saver.Touch();
        Changed?.Invoke();
        return gone.Count;
    }

    /// 보관 기간 changed (설정): what is now too old goes at once, and the wake-up moves.
    public void KeepChanged()
    {
        if (Prune()) Changed?.Invoke();
    }

    /// Unpinned entries older than the kept minutes, beyond the newest 500, or past 4 M characters of text in all (the
    /// history lives in the launcher's memory all day); then one wake-up for when the next one comes due, so a short
    /// 보관 기간 holds with nothing new copied. True: some went.
    private bool Prune()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long keep = Settings.Current.ClipKeepMinutes * 60_000L;
        long cut = keep > 0 ? now - keep : long.MinValue;
        long due = long.MaxValue; // the oldest unpinned entry that stays, when it comes due
        var gone = new List<ClipEntry>();
        lock (gate)
        {
            int kept = 0;
            long chars = 0;
            foreach (var e in data.Items)
            {
                if (e.Pinned) continue;
                chars += e.Text?.Length ?? 0;
                if (e.Copied < cut || ++kept > MaxItems || chars > MaxTotalText) gone.Add(e);
                else if (keep > 0) due = Math.Min(due, e.Copied + keep);
            }
            if (gone.Count > 0)
            {
                var ids = gone.Select(x => x.Id).ToHashSet();
                data.Items.RemoveAll(x => ids.Contains(x.Id));
            }
        }
        ui.BeginInvoke(() =>
        {
            expiry.Stop();
            if (due == long.MaxValue) return;
            expiry.Interval = TimeSpan.FromMilliseconds(Math.Clamp(due - now + 500, 1000, int.MaxValue));
            expiry.Start();
        });
        if (gone.Count == 0) return false;
        foreach (var e in gone) DropImage(e);
        saver.Touch();
        return true;
    }

    private void DropImage(ClipEntry e)
    {
        if (e.Image is null) return;
        lock (gate)
            if (data.Items.Any(x => x.Image == e.Image)) return;
        try
        {
            File.Delete(ImagePath(e));
        }
        catch
        {
            /* in use by a preview: the next start's Sweep removes it */
        }
    }

    /// Search: every word in the text, the file names or the app it came from.
    public List<ClipEntry> Find(string query)
    {
        var all = Items;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return all.ToList();
        return all.Where(e =>
        {
            string hay = e.Kind switch
            {
                "files" => string.Join(" ", e.Files ?? new()),
                "image" => $"이미지 image {e.Width}x{e.Height}",
                _ => e.Text ?? "",
            } + " " + e.Source;
            return words.All(w => hay.Contains(w, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    // ── putting it back ─────────────────────────────────────────────────────────────────────────
    public bool Put(ClipEntry e)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                switch (e.Kind)
                {
                    case "files":
                        var files = new StringCollection();
                        files.AddRange((e.Files ?? new()).ToArray());
                        Clipboard.SetFileDropList(files);
                        break;
                    case "image":
                        string path = ImagePath(e);
                        if (!File.Exists(path)) return false;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path);
                        bmp.EndInit();
                        bmp.Freeze();
                        return Launch.CopyImage(bmp, path);
                    default:
                        Clipboard.SetDataObject(e.Text ?? "", true);
                        break;
                }
                return true;
            }
            catch (COMException)
            {
                System.Threading.Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Log.Error("clipboard put", ex);
                return false;
            }
        }
        return false;
    }

    /// The window that was in front before the launcher (where a paste goes).
    public IntPtr LastForeground => lastForeground;

    /// Text onto the clipboard and Ctrl+V into the window that was in front (emoji, AI results).
    public Task<bool> PasteText(string text, IntPtr? to = null) => Paste(new ClipEntry { Kind = "text", Text = text }, to);

    /// Back on the clipboard, then Ctrl+V into the window that was in front of the launcher.
    public async Task<bool> Paste(ClipEntry e, IntPtr? to = null)
    {
        if (!Put(e)) return false;
        IntPtr target = to ?? lastForeground;
        if (target == IntPtr.Zero || !IsWindow(target)) return true;
        await Task.Delay(30);
        Win32.SetForegroundWindow(target);
        // the keys that opened the launcher or picked the entry must be up first
        for (int i = 0; i < 30 && Win32.ModifiersDown(); i++) await Task.Delay(20);
        await Task.Delay(60);
        if (Win32.GetForegroundWindow() != target) return true; // focus went elsewhere: it stays on the clipboard
        Win32.SendChord(0x11, 0x56); // Ctrl+V
        return true;
    }

    // ── display ─────────────────────────────────────────────────────────────────────────────────
    public static string Title(ClipEntry e)
    {
        switch (e.Kind)
        {
            case "image":
                return Tr($"이미지 {e.Width}×{e.Height}", $"Image {e.Width}×{e.Height}");
            case "files":
                var f = e.Files ?? new();
                string first = Path.GetFileName(f.FirstOrDefault()?.TrimEnd('\\') ?? "");
                return f.Count > 1 ? Tr($"{first} 외 {f.Count - 1}개", $"{first} and {f.Count - 1} more") : first;
            default:
                string t = e.Text ?? "";
                string line = t.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
                line = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return line.Length > 90 ? line[..90] + "…" : line;
        }
    }

    public static string Detail(ClipEntry e)
    {
        string what = e.Kind switch
        {
            "image" => Tr("이미지", "Image"),
            "files" => (e.Files?.Count ?? 0) > 1 ? Tr($"파일 {e.Files!.Count}개", $"{e.Files!.Count} files") : Tr("파일", "File"),
            _ => Lines(e.Text ?? "") is var n and > 1 ? Tr($"{(e.Text ?? "").Length:#,0}자 · {n}줄", $"{(e.Text ?? "").Length:#,0} chars · {n} lines") : Tr($"{(e.Text ?? "").Length:#,0}자", $"{(e.Text ?? "").Length:#,0} chars"),
        };
        return e.Source is { Length: > 0 } s ? $"{s} · {what}" : what;
    }

    private static int Lines(string s) => s.TrimEnd('\r', '\n').Split('\n').Length;

    /// A web address on its own, for the link icon and "open".
    public static string? Url(ClipEntry e)
    {
        string t = (e.Text ?? "").Trim();
        if (e.Kind != "text" || t.Length > 2000 || t.Contains(' ') || t.Contains('\n')) return null;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") ? u.ToString() : null;
    }

    // ── storage (DPAPI, current user) ───────────────────────────────────────────────────────────
    /// `read`: the history file was read whole, or there was none (Sweep goes only then).
    private static ClipFile Load(out bool read)
    {
        string file = Paths.InLocal(@"clipboard\history.dat");
        read = false;
        try
        {
            if (!File.Exists(file))
            {
                read = true;
                return new ClipFile();
            }
            byte[]? plain = Dpapi(File.ReadAllBytes(file), false);
            if (plain is null) return new ClipFile();
            var loaded = JsonSerializer.Deserialize<ClipFile>(plain, Json.Options);
            read = loaded is not null;
            return loaded ?? new ClipFile();
        }
        catch (Exception e)
        {
            Log.Error("clipboard load", e);
            return new ClipFile();
        }
    }

    private void Save()
    {
        byte[] json;
        lock (gate) json = JsonSerializer.SerializeToUtf8Bytes(data, Json.Options);
        byte[]? sealedBytes = Dpapi(json, true);
        if (sealedBytes is null) return;
        string file = Paths.InLocal(@"clipboard\history.dat");
        File.WriteAllBytes(file + ".tmp", sealedBytes);
        File.Move(file + ".tmp", file, true);
    }

    private static byte[]? Dpapi(byte[] data, bool protect)
    {
        var input = new Win32.DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            bool ok = protect
                ? Win32.CryptProtectData(ref input, "Bloom clipboard", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out var output)
                : Win32.CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out output);
            if (!ok) return null;
            var bytes = new byte[output.cbData];
            Marshal.Copy(output.pbData, bytes, 0, output.cbData);
            Win32.LocalFree(output.pbData);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    // ── native ──────────────────────────────────────────────────────────────────────────────────
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

}
