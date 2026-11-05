using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Addons;

/// What the settings page shows for an add-on.
internal enum AddonState
{
    Dev, // a dev or QA copy: hosts are never started from one (they would reach the user's real taskbar)
    NotShipped, // this Bloom came without the add-on
    Broken, // its addon.json is wrong, or it needs a newer Bloom
    Off,
    Skipped, // Shift was held while Bloom started
    Starting,
    Running,
    SafeMode, // 10: explorer kept crashing
    NoSymbols, // 11
    Windhawk, // 12
    Blocked, // 13
    Download, // 14
    Stopped, // ended some other way, or could not be started
}

/// The state, and while the symbol files download how far along (0-100; -1 when unknown).
internal readonly record struct AddonStatus(AddonState State, int Percent = -1);

/// host-state.json and runtime-state.json as far as Bloom reads them (protocol §1).
internal sealed class HostState
{
    public int Pid { get; set; }
    public string? Version { get; set; }
    public string? Phase { get; set; }
    public string? Reason { get; set; }
    public HostProgress? Progress { get; set; }
    public bool SafeMode { get; set; }
    public HostLoaded? Loaded { get; set; }

    internal sealed class HostProgress
    {
        public long Done { get; set; }
        public long Total { get; set; }
    }

    internal sealed class HostLoaded
    {
        public int Pid { get; set; }
    }
}

internal sealed class RuntimeState
{
    public int Pid { get; set; }
    public string? State { get; set; }
    public string? Reason { get; set; }
}

/// One shipped add-on: its manifest, its run copy and host, and its status for the settings page.
internal sealed class Addon
{
    private readonly AddonStore store;
    private readonly object gate = new();
    private volatile AddonRunner? runner;
    private FileSystemWatcher? watcher;
    private volatile bool problem;

    public Addon(string id, AddonStore store)
    {
        Id = id;
        this.store = store;
        try
        {
            Manifest = store.Shipped(id);
            var v = typeof(Addon).Assembly.GetName().Version ?? new Version(0, 0);
            if (Manifest is not null && !Manifest.Fits(new Version(v.Major, v.Minor, Math.Max(0, v.Build))))
            {
                Log.Write($"add-on {id}: needs Bloom {Manifest.MinBloom}");
                Broken = true;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("add-on " + id, e);
            Broken = true;
        }
    }

    public string Id { get; }

    /// The shipped add-on's manifest; null when Bloom came without it (or it couldn't be read).
    public AddonManifest? Manifest { get; }

    public bool Broken { get; }

    public string DataDir => store.DataDir(Id);

    /// The Stop event of this add-on's host.
    public string StopEvent => $@"Local\Bloom.Addon.{Id}.Stop";

    public bool On => Settings.Current.AddonsOn.Contains(Id);

    /// QA pictures: the status to show instead of the real one.
    public AddonStatus? QaStatus { get; set; }

    /// Makes the run copy and values.json and starts the host (a worker thread: copying and hashing read the disk).
    public void Start(bool resetSafeMode = false)
    {
        if (!AddonHub.Offered || Broken || Manifest is not { } m) return;
        lock (gate)
        {
            try
            {
                string dir = store.RunCopy(m);
                store.Prune(m);
                AddonHub.WriteValues(this);
                Watch();
                if (runner is null)
                {
                    var r = new AddonRunner(Path.Combine(dir, m.HostExe), StopEvent, OtherHost);
                    r.Changed += AddonHub.Notify;
                    runner = r;
                }
                problem = false;
                runner.Start(resetSafeMode);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Error("add-on " + Id, e);
                problem = true;
            }
        }
        AddonHub.Notify();
    }

    /// Stops the host and waits for it (up to AddonRunner.StopWait: a worker thread). Also one this Bloom didn't start.
    public void Stop()
    {
        if (!AddonHub.Offered || Manifest is null) return;
        lock (gate)
        {
            if (runner is { } r) r.Stop();
            else AddonRunner.StopHost(StopEvent, null, OtherHost, AddonRunner.StopWait);
        }
        AddonHub.Notify();
    }

    /// Bloom quits: the host is let go, not stopped. It watches Bloom's process and unloads a little after Bloom is gone,
    /// unless the next Bloom (a restart for an update or a language) takes it over first — the taskbar doesn't flash.
    public void Detach()
    {
        runner?.Dispose();
        watcher?.Dispose();
        watcher = null;
    }

    public AddonStatus Status()
    {
        if (QaStatus is { } qa) return qa;
        var r = runner;
        HostState? host = null;
        RuntimeState? runtime = null;
        bool alive = false;
        if (AddonHub.Offered && Manifest is not null && !Broken && On)
        {
            host = AddonHub.ReadState<HostState>(Path.Combine(DataDir, AddonProtocol.HostStateFile));
            runtime = AddonHub.ReadState<RuntimeState>(Path.Combine(DataDir, AddonProtocol.RuntimeStateFile));
            alive = r?.Running == true || (host is { Pid: > 0 } && IsHost(host.Pid));
        }
        return AddonHub.Decide(new AddonHub.Facts(AddonHub.Offered, Manifest is not null, Broken, On, AddonHub.SkippedByShift, r is not null,
            alive, r?.Waiting == true, r?.LastExit, r?.GaveUp == true || problem, host, runtime));
    }

    /// The process id in host-state.json while that process is this add-on's host.
    private int? OtherHost() =>
        AddonHub.ReadState<HostState>(Path.Combine(DataDir, AddonProtocol.HostStateFile)) is { Pid: > 0 } h && IsHost(h.Pid) ? h.Pid : null;

    private bool IsHost(int pid)
    {
        if (Manifest is null) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && string.Equals(p.ProcessName, Path.GetFileNameWithoutExtension(Manifest.HostExe), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// The state files the host and runtime write: the settings page follows them while it is open.
    private void Watch()
    {
        if (watcher is not null) return;
        Directory.CreateDirectory(DataDir);
        var w = new FileSystemWatcher(DataDir, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        w.Changed += (_, _) => AddonHub.Notify();
        w.Created += (_, _) => AddonHub.Notify();
        w.Renamed += (_, _) => AddonHub.Notify();
        w.EnableRaisingEvents = true;
        watcher = w;
    }
}

/// The add-ons Bloom ships (for now the taskbar one), their start with Bloom and their status.
internal static class AddonHub
{
    /// Only the user's own Bloom starts hosts: never a dev or QA copy (--data, which every QA mode needs) or another
    /// instance — the same rule as the elevated Win-key helper (ElevatedWinKey.Offered). A host reaches the real explorer.
    public static bool Offered => !Paths.IsDev && Paths.InstanceId == "Bloom";

    /// Shift was held as Bloom started: no add-on starts this run (the way out of an add-on that breaks the taskbar).
    public static bool SkippedByShift { get; private set; }

    /// A status may have changed (any thread; bursts arrive as one, a moment later).
    public static event Action? Changed;

    private static Addon[]? all;

    public static IReadOnlyList<Addon> All => LazyInitializer.EnsureInitialized(ref all, () => new[] { new Addon(AddonProtocol.Id, AddonStore.Default) });

    public static Addon Taskbar => All[0];

    /// Bloom starts: the add-ons switched on start on a worker thread. `autostart`: started at sign-in (logged with the
    /// time since then, for comparing the ways Bloom is started at sign-in).
    public static void Begin(bool autostart)
    {
        if (!Offered || Settings.Current.AddonsOn.Count == 0) return;
        if ((Native.Win32.GetAsyncKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0)
        {
            SkippedByShift = true;
            Log.Write("add-ons: Shift held at start, none started");
            return;
        }
        _ = Task.Run(() =>
        {
            foreach (var a in All.Where(a => a.On))
            {
                a.Start();
                if (autostart) Log.Write($"add-on {a.Id}: started from --autostart, {LogonTask.SinceSignIn()} after sign-in");
            }
        });
    }

    /// The switch on the settings page: saved at once, the host started or stopped on a worker thread.
    public static Task Set(Addon a, bool on)
    {
        var list = Settings.Current.AddonsOn;
        list.Remove(a.Id);
        if (on) list.Add(a.Id);
        Settings.Save();
        LogonTask.SyncLater();
        Notify();
        return Task.Run(() =>
        {
            if (on) a.Start();
            else a.Stop();
        });
    }

    /// 다시 켜기 (safe mode cleared) and 다시 시도.
    public static Task Retry(Addon a, bool resetSafeMode) => Task.Run(() => a.Start(resetSafeMode));

    /// Bloom quits: every host let go, none stopped (Addon.Detach).
    public static void Quit()
    {
        if (all is null) return;
        foreach (var a in all) a.Detach();
    }

    /// Before the data is deleted (reset, uninstall): every host stopped and waited for, so explorer lets go of the
    /// run copy and the host of its files. Blocks; never on the UI thread.
    public static void StopAll()
    {
        foreach (var a in All) a.Stop();
    }

    /// The user's values for the add-on, kept with Bloom's settings (Windhawk's, once brought in).
    public static string UserValuesFile(string id) => Path.Combine(Paths.Data, "addons", id, "settings.json");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ValuesText(JsonObject values) => values.ToJsonString(Indented);

    /// values.json for the runtime: the user's values as they are ({} while none were brought in: the runtime then uses
    /// the mod's defaults). Written only when it differs; the runtime reads it again on each change.
    public static void WriteValues(Addon a)
    {
        string text = "{}";
        string user = UserValuesFile(a.Id);
        try
        {
            if (File.Exists(user) && JsonNode.Parse(File.ReadAllText(user)) is JsonObject o) text = ValuesText(o);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Log.Error("add-on values", e);
        }
        Directory.CreateDirectory(a.DataDir);
        string values = Path.Combine(a.DataDir, AddonProtocol.ValuesFile);
        if (File.Exists(values) && File.ReadAllText(values) == text) return;
        Json.SaveText(values, text);
    }

    /// A state file the host or runtime writes; null when it is missing, being replaced or not readable as one.
    public static T? ReadState<T>(string file) where T : class
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(s, Json.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static int queued;

    public static void Notify()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        _ = Task.Delay(250).ContinueWith(_ =>
        {
            Volatile.Write(ref queued, 0);
            Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    /// What the status is decided from: the switch, the runner, and the host's and runtime's state files.
    internal readonly record struct Facts(bool Offered, bool Shipped, bool Broken, bool On, bool Skipped, bool Started,
        bool HostAlive, bool Waiting, int? LastExit, bool Failed, HostState? Host, RuntimeState? Runtime);

    internal static AddonStatus Decide(in Facts f)
    {
        if (!f.Offered) return new(AddonState.Dev);
        if (!f.Shipped) return new(AddonState.NotShipped);
        if (f.Broken) return new(AddonState.Broken);
        if (!f.On) return new(AddonState.Off);
        if (f.Skipped && !f.Started) return new(AddonState.Skipped);
        if (f.LastExit is { } code && AddonProtocol.IsFinal(code) && !f.HostAlive && !f.Waiting) return new(FromCode(code));
        if (f.HostAlive && f.Host is { } h)
        {
            if (h.SafeMode) return new(AddonState.SafeMode);
            switch (h.Phase)
            {
                case "symbols":
                    return new(AddonState.Starting, h.Progress is { Total: > 0 } p ? (int)Math.Clamp(p.Done * 100 / p.Total, 0, 100) : -1);
                case "running":
                    // the runtime's file speaks for the explorer the host loaded into, not for one that came before it
                    return f.Runtime is { State: "running" } r && r.Pid == h.Loaded?.Pid ? new(AddonState.Running) : new(AddonState.Starting);
                case "failed":
                    return new(FromReason(h.Reason));
            }
        }
        if (f.HostAlive || f.Waiting) return new(AddonState.Starting);
        if (f.Failed) return new(AddonState.Stopped);
        if (f.Host is { SafeMode: true }) return new(AddonState.SafeMode);
        if (f.Host is { Phase: "failed" } failed) return new(FromReason(failed.Reason));
        return new(f.Started ? AddonState.Stopped : AddonState.Starting); // not started yet this run: it is about to
    }

    private static AddonState FromCode(int code) => code switch
    {
        AddonProtocol.ExitCrashes => AddonState.SafeMode,
        AddonProtocol.ExitNoSymbols => AddonState.NoSymbols,
        AddonProtocol.ExitWindhawk => AddonState.Windhawk,
        AddonProtocol.ExitBlocked => AddonState.Blocked,
        AddonProtocol.ExitDownload => AddonState.Download,
        _ => AddonState.Stopped,
    };

    private static AddonState FromReason(string? reason) => reason switch
    {
        "crashes" => AddonState.SafeMode,
        "noSymbols" => AddonState.NoSymbols,
        "windhawk" => AddonState.Windhawk,
        "blocked" => AddonState.Blocked,
        "download" => AddonState.Download,
        _ => AddonState.Stopped,
    };
}
