using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Search;

/// One top-level window the user could switch to.
internal sealed class OpenWindow
{
    public IntPtr Handle;
    public string Title = "";
    public string? Exe; // the program's path (for the icon), the real app for Store apps
    public string App = ""; // the program's own name ("Google Chrome"), else its file name
    public bool Minimized;
    public MatchTarget Match = null!;
}

/// The windows Alt+Tab would show, front to back: visible, titled, not owned, not tool windows, not cloaked
/// (other virtual desktops, suspended Store apps), never Bloom's own. Read again at most every 1.5 s.
internal static class OpenWindows
{
    private static readonly BackgroundSnapshot<List<OpenWindow>> snapshot = new(new(), Scan, 1500, "open windows");
    private static readonly Dictionary<uint, (string? exe, string app)> programs = new();

    public static int Count => snapshot.Current.Count;
    public static event Action? Updated { add => snapshot.Updated += value; remove => snapshot.Updated -= value; }

    public static List<OpenWindow> List(bool fresh = false)
    {
        if (Demo.On) return new(); // recording mode: no window titles of this PC
        if (fresh) snapshot.Invalidate();
        return snapshot.Read();
    }

    public static Task<List<OpenWindow>> Refresh() => Demo.On ? Task.FromResult(new List<OpenWindow>()) : snapshot.Refresh();

    private static List<OpenWindow> Scan()
    {
        var list = new List<OpenWindow>();
        uint me = (uint)Environment.ProcessId;
        var seenPids = new HashSet<uint>();
        Win32.EnumWindows((h, _) =>
        {
            if (!Switchable(h)) return true;
            if (Win32.GetWindowThreadProcessId(h, out uint pid) == 0 || pid == me) return true;
            string title = Text(h);
            if (title.Length == 0) return true;
            var (exe, app) = Program(h, pid, seenPids);
            list.Add(new OpenWindow
            {
                Handle = h,
                Title = title,
                Exe = exe,
                App = app,
                Minimized = Win32.IsIconic(h),
                Match = new MatchTarget(title, new[] { app, exe is null ? "" : Path.GetFileNameWithoutExtension(exe) }),
            });
            return true;
        }, IntPtr.Zero);
        // programs that ended leave the name cache (pids are reused)
        foreach (uint pid in new List<uint>(programs.Keys))
            if (!seenPids.Contains(pid)) programs.Remove(pid);
        return list;
    }

    /// Forgets the list so the next one is read now (after closing a window).
    public static void Invalidate() => snapshot.Invalidate();

    private static bool Switchable(IntPtr h)
    {
        if (!Win32.IsWindowVisible(h)) return false;
        if (Win32.GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return false;
        long ex = Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE).ToInt64();
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
        if ((ex & Win32.WS_EX_NOACTIVATE) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
        if (Win32.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var cls = new StringBuilder(64);
        _ = Win32.GetClassName(h, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static string Text(IntPtr h)
    {
        int n = Win32.GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new StringBuilder(n + 1);
        _ = Win32.GetWindowText(h, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    /// The program behind a window; a Store app's frame (ApplicationFrameHost) is looked through to the app inside.
    /// Marks the pids it used as seen, so the name cache keeps them.
    private static (string? exe, string app) Program(IntPtr h, uint pid, HashSet<uint> seen)
    {
        seen.Add(pid);
        if (programs.TryGetValue(pid, out var known)) return known;
        string? exe = Win32.ProcessPath(pid);
        if (exe is not null && Path.GetFileName(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            // one frame process hosts every Store app, so its pid is never cached: the app inside is found per window
            // on each scan, and cached under the app's own pid
            string? inner = null;
            uint innerPid = 0;
            Win32.EnumChildWindows(h, (c, _) =>
            {
                if (Win32.GetWindowThreadProcessId(c, out uint cpid) == 0 || cpid == pid) return true;
                inner = Win32.ProcessPath(cpid);
                innerPid = cpid;
                return inner is null;
            }, IntPtr.Zero);
            if (inner is null) return (exe, Describe(exe));
            seen.Add(innerPid);
            if (programs.TryGetValue(innerPid, out var hosted)) return hosted;
            (pid, exe) = (innerPid, inner);
        }
        string app = exe is null ? "" : Describe(exe);
        programs[pid] = (exe, app);
        return (exe, app);
    }

    private static string Describe(string exe)
    {
        string bare = Path.GetFileNameWithoutExtension(exe);
        try
        {
            string? d = FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
            return string.IsNullOrEmpty(d) || d.Length > 48 ? bare : d;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return bare;
        }
    }

    /// To the front (restored first when minimized).
    public static void Switch(IntPtr h)
    {
        if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
        Win32.ForceForeground(h);
    }

    /// Asks the window to close (the app may ask to save first).
    public static void Close(IntPtr h)
    {
        _ = Win32.PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        Invalidate();
    }

    public static void Minimize(IntPtr h) => Win32.ShowWindow(h, 6 /* SW_MINIMIZE */);

    public static void Maximize(IntPtr h)
    {
        Win32.ShowWindow(h, 3 /* SW_MAXIMIZE */);
        Win32.ForceForeground(h);
    }

    public static bool Alive(IntPtr h) => Win32.IsWindow(h);
}
