using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Bloom.Core;

namespace Bloom.Addons;

/// One add-on's host process: started with the protocol's arguments, watched for its exit code, started again after an
/// unexpected end (twice, 5 s then 30 s later) but never after a code that says why it stopped (10-14), and stopped
/// through its named Stop event. A Bloom that quits only lets go of it: the host watches Bloom's process and unloads by
/// itself a little after Bloom is gone, unless a new Bloom (a restart, an update) takes it over in that time.
internal sealed class AddonRunner : IDisposable
{
    public static readonly TimeSpan[] Retries = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };

    /// The host waits up to 10 s for explorer to unload the add-on, so it gets a little more before it is ended by force.
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(12);

    private readonly string exe, stopEvent;
    private readonly IReadOnlyList<string> lead;
    private readonly TimeSpan[] retries;
    private readonly TimeSpan stopWait;
    private readonly Func<int?>? otherHost;
    private readonly object gate = new();
    private Process? process;
    private Timer? retry;
    private int failures;
    private bool stopping, disposed;

    /// `otherHost`: the process id of a host this runner did not start (one a quitting Bloom left running, which this
    /// Bloom's start handed over to), for Stop to wait on. `lead`: arguments before the protocol's (a test's stand-in).
    public AddonRunner(string exe, string stopEvent, Func<int?>? otherHost = null, TimeSpan[]? retries = null, TimeSpan? stopWait = null, IReadOnlyList<string>? lead = null)
    {
        this.exe = exe;
        this.stopEvent = stopEvent;
        this.otherHost = otherHost;
        this.retries = retries ?? Retries;
        this.stopWait = stopWait ?? StopWait;
        this.lead = lead ?? Array.Empty<string>();
    }

    /// Something below changed (any thread).
    public event Action? Changed;

    /// The last exit code seen, null while none.
    public int? LastExit { get; private set; }

    /// It ended unexpectedly more often than it is started again, or could not be started at all.
    public bool GaveUp { get; private set; }

    /// How many processes this runner started (the tests count them).
    public int Spawns { get; private set; }

    public bool Running
    {
        get
        {
            lock (gate) return process is not null;
        }
    }

    /// A start again is waiting for its delay.
    public bool Waiting
    {
        get
        {
            lock (gate) return retry is not null;
        }
    }

    /// The host's arguments after `--serve`: this Bloom's process id, its exe (for the host to open it) and its local
    /// data folder (the add-on's data folder is under it, also for a dev copy's own).
    public static List<string> Arguments(int bloomPid, string bloomExe, string bloomLocal, bool resetSafeMode)
    {
        var a = new List<string> { AddonProtocol.ServeArg, bloomPid.ToString(System.Globalization.CultureInfo.InvariantCulture), AddonProtocol.BloomArg, bloomExe, AddonProtocol.BloomLocalArg, bloomLocal };
        if (resetSafeMode) a.Add(AddonProtocol.ResetSafeModeArg);
        return a;
    }

    /// Starts the host unless it runs; the count of tries starts over (the user asked, or Bloom started). False when it
    /// could not be started.
    public bool Start(bool resetSafeMode = false)
    {
        lock (gate)
        {
            if (disposed) return false;
            if (process is not null) return true;
            retry?.Dispose();
            retry = null;
            stopping = false;
            failures = 0;
            GaveUp = false;
            LastExit = null;
            // a Stop of an earlier run may still be set while some process holds the event open: the new host would end at once
            using (var ev = new EventWaitHandle(false, EventResetMode.ManualReset, stopEvent)) ev.Reset();
            return Spawn(resetSafeMode);
        }
    }

    private bool Spawn(bool resetSafeMode)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            foreach (string a in lead) psi.ArgumentList.Add(a);
            foreach (string a in Arguments(Environment.ProcessId, Environment.ProcessPath ?? "", Paths.Local, resetSafeMode)) psi.ArgumentList.Add(a);
            var p = Ai.ChildProcess.Spawn(psi) ?? throw new InvalidOperationException("not started");
            Spawns++;
            process = p;
            p.EnableRaisingEvents = true; // an exit before this line is still raised
            p.Exited += (_, _) => Ended(p);
            Log.Write($"add-on host started: {Path.GetFileName(exe)} pid {p.Id}");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Error("add-on host", e);
            GaveUp = true;
            Raise();
            return false;
        }
        Raise();
        return true;
    }

    private void Ended(Process p)
    {
        int code;
        try
        {
            code = p.ExitCode;
        }
        catch (InvalidOperationException)
        {
            code = AddonProtocol.ExitError;
        }
        lock (gate)
        {
            if (process != p) return;
            process = null;
            p.Dispose();
            LastExit = code;
            Log.Write($"add-on host ended: {code}");
            // 0 is a normal end: asked to stop, or this start handed its command line to a host already running. 10-14
            // say why it stopped and would only end the same way again: the user decides (다시 켜기, 다시 시도).
            if (!stopping && !disposed && code != AddonProtocol.ExitOk && !AddonProtocol.IsFinal(code))
            {
                if (failures < retries.Length) retry = new Timer(_ => Again(), null, retries[failures++], Timeout.InfiniteTimeSpan);
                else GaveUp = true;
            }
        }
        Raise();
    }

    private void Again()
    {
        lock (gate)
        {
            retry?.Dispose();
            retry = null;
            if (stopping || disposed || process is not null) return;
            Spawn(false);
        }
    }

    /// Asks the host to unload the add-on and end (its Stop event), waits for it, and ends it only when it doesn't. Blocks
    /// for up to StopWait: never on the UI thread.
    public void Stop()
    {
        Process? mine;
        lock (gate)
        {
            stopping = true;
            retry?.Dispose();
            retry = null;
            mine = process;
        }
        StopHost(stopEvent, mine, otherHost, stopWait);
        Raise();
    }

    /// Sets the Stop event and waits for the host: `mine` when this Bloom started it, else the one `otherHost` names.
    public static void StopHost(string stopEvent, Process? mine, Func<int?>? otherHost, TimeSpan wait)
    {
        using var ev = new EventWaitHandle(false, EventResetMode.ManualReset, stopEvent);
        ev.Set();
        Process? other = null;
        try
        {
            var p = mine;
            if (p is null && otherHost?.Invoke() is { } pid)
            {
                try
                {
                    p = other = Process.GetProcessById(pid);
                }
                catch (ArgumentException)
                {
                    // already gone
                }
            }
            if (p is null) return;
            try
            {
                if (p.WaitForExit(wait)) return;
                Log.Write("add-on host did not stop in time: ended");
                p.Kill();
                p.WaitForExit(2000);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // ended meanwhile, or not ours to end
            }
        }
        finally
        {
            other?.Dispose(); // the event stays set for a host not known here; Start clears it before the next one
        }
    }

    /// Lets go of the host without stopping it (Bloom quits): no more starts, no more exit codes.
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            retry?.Dispose();
            retry = null;
            process?.Dispose();
            process = null;
        }
    }

    private void Raise() => Changed?.Invoke();
}
