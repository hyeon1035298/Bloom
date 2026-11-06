using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Bloom.Search;

namespace Bloom.Native;

/// The window a CLI session (Claude Code, Codex) runs in — Windows Terminal, VS Code, a classic console, the desktop
/// app — found from the CLI's process when a notice card is clicked, never on a poll.
internal static class SessionWindow
{
    private const int MaxHops = 12, GwOwner = 4;

    /// `Bloom.exe --console-window <pid>` (Program): the helper run that asks for a console window.
    public const string ProbeArg = "--console-window";

    /// How long the helper may take (a cold start of the exe, then one question).
    private static readonly TimeSpan ProbeWait = TimeSpan.FromSeconds(3);

    /// Brings the window that hosts the process to the front (restored when minimized); false when there is none to
    /// find or the process is gone. Called on the UI thread; the looking runs off it.
    public static async Task<bool> Raise(int pid)
    {
        IntPtr hwnd = await Locate(pid);
        if (hwnd == IntPtr.Zero) return false;
        OpenWindows.Switch(hwnd);
        return true;
    }

    /// The window that hosts the process (zero: none to find). Called on the UI thread; the looking runs off it.
    public static async Task<IntPtr> Locate(int pid)
    {
        var open = (await OpenWindows.Refresh()).ConvertAll(w => w.Handle);
        return await Task.Run(() => Find(pid, open));
    }

    /// Among the windows a user could switch to (`open`, front to back): the console's own window — a classic console
    /// is a window itself, and a Windows Terminal tab's hidden one is owned by the terminal's window — else the first
    /// window up the process's parents.
    private static IntPtr Find(int pid, List<IntPtr> open)
    {
        IntPtr console = ConsoleWindowOf(pid);
        if (console != IntPtr.Zero)
            foreach (IntPtr h in new[] { console, Win32.GetWindow(console, GwOwner) })
                if (open.Contains(h)) return h;
        var byProcess = new Dictionary<int, IntPtr>(); // a program with several windows: the one used last
        foreach (IntPtr h in open)
            if (Win32.GetWindowThreadProcessId(h, out uint owner) != 0) byProcess.TryAdd((int)owner, h);
        return Climb(pid, Win32.ProcessTree(), byProcess, ParentIsOlder);
    }

    /// The console window of the process, asked of a helper run of Bloom: the process that attaches to a console gets its
    /// Ctrl+C, Ctrl+Break and closing, which would end it — the helper, never the running Bloom. A helper
    /// ended that way leaves an exit code that is no open window, and Find drops it.
    private static IntPtr ConsoleWindowOf(int pid)
    {
        if (Environment.ProcessPath is not { } exe) return IntPtr.Zero;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(ProbeArg);
            start.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            using var p = Bloom.Ai.ChildProcess.Spawn(start);
            if (p is null) return IntPtr.Zero;
            if (!p.WaitForExit(ProbeWait))
            {
                p.Kill();
                return IntPtr.Zero;
            }
            return new IntPtr(p.ExitCode);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    /// The helper run's whole work (Program, before anything else starts): the console window of `pid` as the exit code
    /// (window handles fit in 32 bits), 0 for none.
    public static int Probe(string pid) =>
        int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? unchecked((int)(long)Win32.ConsoleWindowOf(id)) : 0;

    /// The first window of the process or, going up through its parents, of the nearest one that has any (zero: none).
    /// The shell (explorer.exe) ends the climb — its windows are folders, not the session's. `linked` says a parent id
    /// is still the child's real parent (ids are reused once a parent ends).
    internal static IntPtr Climb(int pid, IReadOnlyDictionary<int, (int parent, string exe)> tree, IReadOnlyDictionary<int, IntPtr> windows, Func<int, int, bool> linked)
    {
        int at = pid;
        for (int hop = 0; hop <= MaxHops; hop++)
        {
            if (!tree.TryGetValue(at, out var node) || node.exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
            if (windows.TryGetValue(at, out IntPtr hwnd)) return hwnd;
            if (node.parent <= 0 || node.parent == at || !linked(at, node.parent)) return IntPtr.Zero;
            at = node.parent;
        }
        return IntPtr.Zero;
    }

    /// The parent began before the child (a reused id began after it).
    private static bool ParentIsOlder(int child, int parent)
    {
        try
        {
            using var c = Process.GetProcessById(child);
            using var p = Process.GetProcessById(parent);
            return p.StartTime <= c.StartTime;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false; // gone, or not ours to look into
        }
    }
}
