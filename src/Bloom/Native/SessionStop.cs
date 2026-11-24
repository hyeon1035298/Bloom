using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Native;

/// The Run Card add-on's 정지: Esc into the console of a Claude Code typed in a terminal, which stops its turn as Esc
/// typed there does. Only a session whose own file says it runs from the CLI is stopped (ClaudeWatch.InTerminal); the
/// key goes in from a helper run of Bloom, because the process that attaches to a console also gets its Ctrl+C and
/// closing (SessionWindow's helper, the same way).
internal static class SessionStop
{
    /// `Bloom.exe --console-esc <pid>` (Program): the helper run that sends the key.
    public const string EscArg = "--console-esc";

    /// A second Esc right after the first opens Claude Code's rewind: one stop per session in this time.
    private static readonly TimeSpan Again = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan HelperWait = TimeSpan.FromSeconds(3);
    private static readonly Dictionary<int, DateTime> sent = new();

    /// Off the UI thread (the session file is read, the helper waited for). False when it is not stopped: not a terminal
    /// session, stopped a moment ago, or the key could not be sent.
    public static bool Stop(string session, int pid)
    {
        if (!ClaudeWatch.InTerminal(session, pid))
        {
            Log.Write($"run card: stop not sent, pid {pid} is not that session in a terminal");
            return false;
        }
        lock (sent)
        {
            if (sent.TryGetValue(pid, out var at) && DateTime.UtcNow - at < Again) return false;
            sent[pid] = DateTime.UtcNow;
        }
        bool ok = Helper(pid);
        if (!ok) Log.Write($"run card: stop for pid {pid} not sent");
        return ok;
    }

    private static bool Helper(int pid)
    {
        if (Environment.ProcessPath is not { } exe) return false;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(EscArg);
            start.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            using var p = ChildProcess.Spawn(start);
            if (p is null) return false;
            if (!p.WaitForExit(HelperWait))
            {
                p.Kill();
                return false;
            }
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// The helper run's whole work (Program, before anything else starts): 0 when the key went in, 1 otherwise.
    public static int Esc(string pid) =>
        int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 && Win32.SendEscape(id) ? 0 : 1;
}
