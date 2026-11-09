using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.App;

/// Windows' notification banners as Bloom's cards (설정 > 알림 > Windows 알림). PillTaskbar, in explorer, keeps a banner
/// hidden when a window has asked for that on the primary taskbar window (the property Bloom.ToastCards) and posts it
/// PillTaskbar.ToastBanner (docs/addon-protocol.md 8 in PillTaskbar). Windows only shows a banner when its own settings
/// allow (방해 금지, per-app banners), so a card comes exactly where a banner would have. The notification itself is read
/// from Windows' notification store (wpndatabase.db, read only, through the system's winsqlite3.dll) and stays in
/// Windows' notification center, which a click on the card opens.
internal static partial class WindowsToasts
{
    private const string Prop = "Bloom.ToastCards";
    private static readonly string Store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db");
    private static HwndSource? window;
    private static uint bannerMsg;
    private static DispatcherTimer? keep, settle;
    private static long seen = -1; // the newest notification already known; -1 until read once
    private static int reading;
    private static bool again;
    private static readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    /// Host at start and 설정 > 알림 on a change: asks PillTaskbar while 알림 and Windows 알림 are on.
    public static void Apply(Dispatcher ui)
    {
        bool on = Settings.Current.Notices && Settings.Current.NoticeWindows;
        if (on == (window is not null)) return;
        if (!on)
        {
            keep?.Stop();
            Unask();
            window?.Dispose();
            window = null;
            return;
        }
        bannerMsg = RegisterWindowMessage("PillTaskbar.ToastBanner");
        window = new HwndSource(new HwndSourceParameters("Bloom.ToastCards") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }); // message-only
        window.AddHook(OnMessage);
        Native.Win32.ChangeWindowMessageFilterEx(window.Handle, bannerMsg, 1, IntPtr.Zero); // explorer may run below an elevated Bloom
        settle = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(60) };
        settle.Tick += (_, _) =>
        {
            settle!.Stop();
            _ = Read(retry: true);
        };
        // A new explorer has a new taskbar window without the property: asked again every few seconds.
        keep ??= new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(4) };
        keep.Tick -= Ask;
        keep.Tick += Ask;
        keep.Start();
        Ask(null, EventArgs.Empty);
        seen = -1;
        _ = Read(retry: false); // the newest one now: older ones never come as cards
    }

    /// Bloom quits: PillTaskbar lets the banners show again.
    public static void Stop() => Unask();

    private static void Ask(object? sender, EventArgs e)
    {
        if (window is null) return;
        IntPtr taskbar = Native.Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero && GetProp(taskbar, Prop) != window.Handle) SetProp(taskbar, Prop, window.Handle);
    }

    private static void Unask()
    {
        if (window is null) return;
        IntPtr taskbar = Native.Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero && GetProp(taskbar, Prop) == window.Handle) RemoveProp(taskbar, Prop);
    }

    private static IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0 && (uint)msg == bannerMsg)
        {
            handled = true;
            settle?.Stop(); // several come as a banner grows: one read after the last
            settle?.Start();
        }
        return IntPtr.Zero;
    }

    /// The notifications that came since the last read, as cards. One that isn't in the store yet is looked for once more.
    /// A signal while a read runs (a second notification right after the first) reads again after it. UI thread only.
    private static async Task Read(bool retry)
    {
        if (reading == 1)
        {
            again = true;
            return;
        }
        reading = 1;
        try
        {
            long from = seen;
            var (newest, found) = await Task.Run(() => Since(from));
            if (newest < 0) return;
            if (seen < 0 || newest > seen) seen = newest;
            if (from < 0) return;
            if (found.Count == 0 && retry)
            {
                await Task.Delay(300);
                (newest, found) = await Task.Run(() => Since(from));
                if (newest > seen) seen = newest;
            }
            foreach (var t in found)
                if (!BloomTells(t.Aumid)) Notices.Windows(t.App, t.Title, t.Detail, OpenCenter);
        }
        catch (Exception e)
        {
            Log.Error("windows notices", e);
        }
        finally
        {
            reading = 0;
            if (again)
            {
                again = false;
                _ = Read(retry: true);
            }
        }
    }

    internal sealed record Toast(string Aumid, string App, string Title, string? Detail);

    /// Apps whose news Bloom's own cards already tell (the Claude and Codex apps' Claude Code and Codex turns): their
    /// Windows notifications stay in the notification center only, so one thing never comes as two cards.
    private static readonly string[] Told = { "Claude_pzs8sxrjxfjjc!", "OpenAI.Codex_2p2nqsd0c76g0!" };

    private static bool BloomTells(string aumid)
    {
        var s = Settings.Current;
        if (!(s.NoticeStart || s.NoticeDone || s.NoticeWaiting)) return false;
        foreach (var prefix in Told)
            if (aumid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// The newest notification's id, and the toasts after `after` (none when after is -1), oldest first, at most 4.
    private static (long newest, List<Toast> found) Since(long after)
    {
        var found = new List<Toast>();
        if (sqlite3_open_v2(Store, out IntPtr db, 1 /* READONLY */, IntPtr.Zero) != 0)
        {
            sqlite3_close(db);
            return (-1, found);
        }
        try
        {
            sqlite3_busy_timeout(db, 500);
            long newest = -1;
            Query(db, "select max(Id) from Notification", s => newest = sqlite3_column_int64(s, 0));
            if (after < 0 || newest <= after) return (newest, found);
            Query(db, "select h.PrimaryId, n.Payload from Notification n join NotificationHandler h on h.RecordId = n.HandlerId " +
                      $"where n.Id > {after} and n.Type = 'toast' order by n.Id limit 4", s =>
            {
                string app = Text(s, 0) ?? "";
                int size = sqlite3_column_bytes(s, 1);
                IntPtr blob = sqlite3_column_blob(s, 1);
                string xml = size > 0 && blob != IntPtr.Zero ? Marshal.PtrToStringUTF8(blob, size) : "";
                if (Parse(app, xml) is { } t) found.Add(t);
            });
            return (newest, found);
        }
        finally
        {
            sqlite3_close(db);
        }
    }

    private static void Query(IntPtr db, string sql, Action<IntPtr> row)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out IntPtr s, IntPtr.Zero) != 0) throw new InvalidDataException("wpndatabase: " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
        try
        {
            while (sqlite3_step(s) == 100 /* ROW */) row(s);
        }
        finally
        {
            sqlite3_finalize(s);
        }
    }

    private static string? Text(IntPtr s, int col) => sqlite3_column_text(s, col) is var p && p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : null;

    [GeneratedRegex(@"<text\b([^>]*)>(.*?)</text>", RegexOptions.Singleline)]
    private static partial Regex TextTag();

    /// The toast's lines: the first is the title, the rest the line under it; the attribution line (a website's name) left out.
    private static Toast? Parse(string app, string xml)
    {
        var lines = new List<string>();
        foreach (Match m in TextTag().Matches(xml))
        {
            if (m.Groups[1].Value.Contains("attribution")) continue;
            string line = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (line.Length > 0) lines.Add(line);
        }
        string name = NameOf(app);
        if (lines.Count == 0) return null;
        return new Toast(app, name, lines[0], lines.Count > 1 ? string.Join(" ", lines.GetRange(1, lines.Count - 1)) : null);
    }

    /// The app as the Start menu names it, else from its id.
    private static string NameOf(string app)
    {
        lock (names)
            if (names.TryGetValue(app, out var known)) return known;
        string name = Music.OtherApps.ShellName(app) ?? Music.OtherApps.CleanName(app);
        lock (names) names[app] = name;
        return name;
    }

    /// The card's click: Windows' notification center (PillTaskbar puts it under the tray).
    private static void OpenCenter()
    {
        try
        {
            Native.Win32.AllowSetForegroundWindow(-1);
            Process.Start(new ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error("notification center", e);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)] private static partial uint RegisterWindowMessage(string name);
    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr GetProp(IntPtr hwnd, string name);
    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetProp(IntPtr hwnd, string name, IntPtr data);
    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr RemoveProp(IntPtr hwnd, string name);

    // the system's SQLite (Windows' own, no package)
    [LibraryImport("winsqlite3.dll", StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_open_v2(string file, out IntPtr db, int flags, IntPtr vfs);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_close(IntPtr db);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_busy_timeout(IntPtr db, int ms);
    [LibraryImport("winsqlite3.dll", StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_prepare_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_step(IntPtr stmt);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_finalize(IntPtr stmt);
    [LibraryImport("winsqlite3.dll")] private static partial long sqlite3_column_int64(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_column_blob(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_column_bytes(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_errmsg(IntPtr db);
}
