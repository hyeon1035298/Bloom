using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Native;

namespace Bloom.Core;

/// Explicit memory diagnostics and cleanup after unusually large operations. Normal hidden/idle
/// periods leave collection to the runtime: forced compacting GC also pauses drawing and keyboard hooks.
internal static class MemoryTrim
{
    private static int running;
    private static int count;

    /// How many trims have run (SummonTrace: an opening after one pages back what it touches).
    public static int Count => Volatile.Read(ref count);

    /// A full collection, the finalizers, one more collection for what they let go, then the working set trimmed.
    /// `aggressive` also hands emptied GC regions back (after something big, like Bloom Space Cleaner's map).
    public static void Now(bool aggressive = false)
    {
        GC.Collect(2, aggressive ? GCCollectionMode.Aggressive : GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        using var me = Process.GetCurrentProcess();
        Win32.SetProcessWorkingSetSize(me.Handle, new IntPtr(-1), new IntPtr(-1));
        Interlocked.Increment(ref count);
    }

    /// Only explicit aggressive cleanup opts into collection. Routine idle requests do no work:
    /// a hidden launcher can coexist with an active chat, screenshot pen, or another Bloom window.
    public static void Soon(bool aggressive = false)
    {
        if (!aggressive) return;
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        new Thread(() =>
        {
            try
            {
                Now(aggressive);
            }
            catch (Exception e)
            {
                Log.Error("memory trim", e);
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        })
        { IsBackground = true, Name = "memory trim", Priority = ThreadPriority.Highest }.Start();
    }

    // ── one collection after big work ───────────────────────────────────────────────────────────
    // Lens and 화면 캡처 (the desktop and an overlay per monitor), the colour picker and a clipboard picture leave tens of
    // MB of dead pictures that wait for a GC, Bloom Space Cleaner's closed window its scanned tree and map, and the GC's
    // emptied room stays committed. Routine idle cleanup stays off
    // (see Soon): a forced GC stops every thread, the Win key's hook too, and an open window's drawing. So the one
    // collection a big job asks for waits until it can't be felt: every Bloom window hidden, no input anywhere for a
    // minute, the CPU idle, and at most one an hour. A Bloom window kept on the screen (설정 left open, a pinned chat)
    // holds it back for ten minutes at most: after that, a minute without input and an idle CPU are enough. It hands
    // the emptied GC regions back and lets the finalizers free the pictures; the working set is never trimmed (the
    // next opening would page it back in and flicker).

    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1), QuietFor = TimeSpan.FromMinutes(1), AtMostEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan WindowHoldsAtMost = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CpuSample = TimeSpan.FromMilliseconds(500);
    private const double BusyCpu = 0.25; // the share of all cores in use above which it waits

    private static readonly object gate = new();
    private static Timer? check; // runs only while a big job waits for its collection
    private static string? asked; // that job
    private static int jobs; // counted, so a check that overlapped a newer job leaves it to its own check
    private static readonly HashSet<string> told = new(); // what it waited for, logged once a job
    private static DateTime last = DateTime.MinValue;
    private static DateTime waiting; // when the job now waiting first asked

    /// A big job is over: one collection later, when nothing would feel it.
    public static void AfterBigWork(string what)
    {
        lock (gate)
        {
            if (asked is null) waiting = DateTime.UtcNow;
            asked = what;
            jobs++;
            check ??= new Timer(_ => Check());
            check.Change(CheckEvery, Timeout.InfiniteTimeSpan);
        }
    }

    private static void Check()
    {
        string? what;
        int job;
        DateTime lastRun, since;
        lock (gate) (what, job, lastRun, since) = (asked, jobs, last, waiting);
        if (what is null) return;
        var (wait, again) = Waits(lastRun, DateTime.UtcNow - since);
        lock (gate)
        {
            if (job != jobs) return; // a newer job set its own check meanwhile
            if (wait is not null)
            {
                if (told.Add(wait.StartsWith("CPU", StringComparison.Ordinal) ? "CPU" : wait)) Log.Write($"memory trim after {what} waits: {wait}");
                check!.Change(Max(again, TimeSpan.FromSeconds(5)), Timeout.InfiniteTimeSpan);
                return;
            }
            if (Interlocked.Exchange(ref running, 1) == 1)
            {
                check!.Change(CheckEvery, Timeout.InfiniteTimeSpan);
                return;
            }
            asked = null;
            told.Clear();
            last = DateTime.UtcNow;
        }
        // its own thread at the highest priority, as Soon's: a blocking GC runs at the priority of the thread that starts it
        new Thread(() =>
        {
            try
            {
                Collect(what);
            }
            catch (Exception e)
            {
                Log.Error("memory trim", e);
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        })
        { IsBackground = true, Name = "memory trim", Priority = ThreadPriority.Highest }.Start();
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// What the collection still waits for and when to look again; null when nothing would feel it now.
    private static (string? wait, TimeSpan again) Waits(DateTime lastRun, TimeSpan waited)
    {
        var since = DateTime.UtcNow - lastRun;
        if (since < AtMostEvery) return ("the last one within the hour", AtMostEvery - since);
        var idle = InputIdle();
        if (idle < QuietFor) return ("input", QuietFor - idle);
        if (waited < WindowHoldsAtMost && BloomWindowShown()) return ("a Bloom window on the screen", CheckEvery);
        double busy = CpuBusy();
        if (busy > BusyCpu) return ($"CPU {busy:P0}", CheckEvery);
        return (null, TimeSpan.Zero);
    }

    /// One aggressive collection (emptied regions go back) and the finalizers (the pictures' native memory goes), in one
    /// log line with the time every thread stood still and the private bytes before and after.
    private static void Collect(string what)
    {
        using var me = Process.GetCurrentProcess();
        long before = me.PrivateMemorySize64;
        var paused = GC.GetTotalPauseDuration();
        var clock = Stopwatch.StartNew();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        double pause = (GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
        me.Refresh();
        Log.Write($"memory trim after {what}: threads stopped {pause:0} ms, private {before / 1048576.0:0} → {me.PrivateMemorySize64 / 1048576.0:0} MB, {clock.ElapsedMilliseconds} ms with the finalizers");
    }

    /// How long since the last key or mouse input anywhere in the session.
    private static TimeSpan InputIdle()
    {
        var info = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        return Win32.GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }

    /// A window of Bloom's on the screen: shown, minimized too (its work may go on), not a QA window drawn off-screen.
    private static bool BloomWindowShown()
    {
        uint me = (uint)Environment.ProcessId;
        bool shown = false;
        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h) || Win32.GetWindowThreadProcessId(h, out uint pid) == 0 || pid != me) return true;
            if (!Win32.GetWindowRect(h, out var r)) return true;
            if (r.Right - r.Left <= 1 || r.Bottom - r.Top <= 1 || r.Left <= -30000) return true;
            shown = true;
            return false;
        }, IntPtr.Zero);
        return shown;
    }

    /// The share of all cores in use over half a second.
    private static double CpuBusy()
    {
        if (!Win32.GetSystemTimes(out long idle0, out long kernel0, out long user0)) return 0;
        Thread.Sleep(CpuSample);
        if (!Win32.GetSystemTimes(out long idle1, out long kernel1, out long user1)) return 0;
        long total = kernel1 - kernel0 + (user1 - user0); // kernel time holds the idle time
        return total <= 0 ? 0 : 1 - (double)(idle1 - idle0) / total;
    }
}
