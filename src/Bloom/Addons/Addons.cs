using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    NotShipped, // not downloaded yet, and not in the install folder
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
    Restart, // 15: explorer must restart before the add-on can load again
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

/// One add-on, shipped or the user's own: its manifest, its run copy and host, and its status for the settings page.
internal sealed class Addon
{
    private readonly AddonStore store;
    private readonly object gate = new();
    private volatile AddonRunner? runner;
    private FileSystemWatcher? watcher;
    private volatile bool problem;

    public Addon(string id, AddonStore store, bool user = false)
    {
        Id = id;
        User = user;
        this.store = store;
        Catalog = user ? null : AddonCatalog.Find(id);
        Load();
    }

    /// The Bloom this is, as an add-on's minBloom is compared with it.
    internal static Version Bloom
    {
        get
        {
            var v = typeof(Addon).Assembly.GetName().Version ?? new Version(0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// The manifest from the source folder (again after a download), and whether this Bloom can use it.
    private void Load()
    {
        AddonManifest? m = null;
        bool broken = false;
        try
        {
            m = store.Shipped(Id);
            if (m is not null && !m.Fits(Bloom))
            {
                Log.Write($"add-on {Id}: needs Bloom {m.MinBloom}");
                broken = true;
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("add-on " + Id, e);
            broken = true;
        }
        Broken = broken;
        Manifest = m;
        schemaRead = false;
        values = null;
    }

    public string Id { get; }

    /// One the user put in the user-addons folder (AddonStore.User), not one of Bloom's.
    public bool User { get; }

    /// The add-on's manifest, shipped or downloaded; null while it is neither (or it couldn't be read).
    public AddonManifest? Manifest { get; private set; }

    public bool Broken { get; private set; }

    /// The download on the page: how far it is, or why the last one failed.
    public AddonFetch Fetch { get; } = new();

    /// The version the host was last started from this run.
    public string? StartedVersion { get; private set; }

    /// Its line in Bloom's catalog (where its releases come from); null for the user's own add-ons. The tests set one.
    public AddonCatalog.Entry? Catalog { get; internal set; }

    /// Its latest release as last read from its repository; null while none was read. The tests set one.
    public AddonRelease? Latest { get; internal set; }

    private long lookedAt = long.MinValue;

    /// The last read of its latest release began within `age` (on the clock Environment.TickCount64 keeps), and a new one
    /// is marked as begun now when it didn't: the settings page reads it once in that time, however often it is drawn.
    internal bool LookedWithin(TimeSpan age)
    {
        long now = Environment.TickCount64, was = Interlocked.Read(ref lookedAt);
        if (was != long.MinValue && now - was < (long)age.TotalMilliseconds) return true;
        return Interlocked.CompareExchange(ref lookedAt, now, was) != was;
    }

    /// The release to download: the latest one when it is later than the version here (a downloaded or bundled one; any
    /// when it has none or a broken one), not before the catalog's minVersion and not needing a newer Bloom. Null
    /// otherwise, and for the user's own add-ons.
    public AddonRelease? Offer() => User || store.DownloadRoot is null || Catalog is not { } c ? null : Pick(Latest, c, Broken ? null : Manifest?.Version, Bloom);

    /// Offer's rule: a release is never one before what is here (a downgrade) or before the catalog's floor.
    internal static AddonRelease? Pick(AddonRelease? latest, AddonCatalog.Entry catalog, string? have, Version bloom) =>
        latest is { } r && r.Id == catalog.Id && r.Fits(bloom) && AddonCatalog.Compare(r.Version, catalog.MinVersion) >= 0
            && (have is null || AddonCatalog.Newer(r.Version, have)) ? r : null;

    /// Reads its latest release and, when that is one to download (Offer), downloads it, checks it and puts it in as this
    /// add-on's source: Fetch shows it busy at once, the rest runs on a worker thread. A new version is put to work at once
    /// while the add-on is on: its host, when one runs, is stopped and started again from it.
    public Task Update(HttpClient http)
    {
        if (Catalog is not { } c || store.DownloadRoot is not { } root || !Fetch.Begin()) return Task.CompletedTask;
        AddonHub.Notify();
        return Task.Run(() => Updated(http, c, root));
    }

    private async Task Updated(HttpClient http, AddonCatalog.Entry c, string root)
    {
        AddonFailure? failed = null;
        try
        {
            LookedWithin(TimeSpan.Zero);
            Latest = await AddonLatest.Fetch(http, c).ConfigureAwait(false);
            if (Offer() is { } e) await Fetched(http, e, root).ConfigureAwait(false);
        }
        catch (AddonDownloadException x)
        {
            Log.Write($"add-on {Id}: update failed ({x.Reason}): {x.Message}");
            failed = x.Reason;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            Log.Error("add-on " + Id, x);
            failed = AddonFailure.Disk;
        }
        catch (Exception x) when (x is InvalidOperationException or OperationCanceledException or HttpRequestException)
        {
            Log.Error("add-on " + Id, x);
            failed = AddonFailure.Network;
        }
        finally
        {
            Fetch.End(failed);
            AddonHub.Notify();
        }
    }

    /// Reads its latest release only (the settings page, for the 업데이트 it may offer); a failure is logged and leaves the
    /// last one read.
    public async Task Look(HttpClient http)
    {
        if (Catalog is not { } c) return;
        try
        {
            Latest = await AddonLatest.Fetch(http, c).ConfigureAwait(false);
        }
        catch (AddonDownloadException x)
        {
            Log.Write($"add-on {Id}: latest release not read ({x.Reason}): {x.Message}");
        }
        catch (Exception x) when (x is InvalidOperationException or HttpRequestException)
        {
            Log.Error("add-on " + Id, x);
        }
        AddonHub.Notify();
    }

    private async Task Fetched(HttpClient http, AddonRelease e, string root)
    {
        string staging = await AddonDownload.Prepare(http, e, root, (done, total) =>
        {
            if (Fetch.Report(done, total)) AddonHub.Notify();
        }, CancellationToken.None).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                AddonDownload.Commit(staging, e, root);
                Load();
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        Log.Write($"add-on {Id}: {e.Version} downloaded");
        if (!On || AddonHub.SkippedByShift) return;
        // at once: the host of the version before is stopped (explorer lets go of its DLLs) and this one starts
        if (StartedVersion is { } was && was != e.Version)
        {
            Log.Write($"add-on {Id}: from {was} to {e.Version} now");
            Stop();
        }
        Start();
    }

    public string DataDir => store.DataDir(Id);

    /// The Stop event of this add-on's host.
    public string StopEvent => $@"Local\Bloom.Addon.{Id}.Stop";

    public bool On => Settings.Current.AddonsOn.Contains(Id);

    /// QA pictures: the status to show instead of the real one.
    public AddonStatus? QaStatus { get; set; }

    private AddonSchema? schema;
    private AddonValues? values;
    private TaskbarCommands? commands;
    private bool schemaRead;

    /// The settings schema it comes with (read once per manifest; null without one, or when its hash doesn't match).
    public AddonSchema? Schema
    {
        get
        {
            if (!schemaRead)
            {
                schema = Broken || Manifest is not { } m ? null : AddonSchema.Load(store.SourceDir(Id), m);
                schemaRead = true;
            }
            return schema;
        }
    }

    /// The user's values for the schema's settings (null without a schema).
    public AddonValues? Values => values ??= Schema is { } s ? new AddonValues(AddonHub.UserValuesFile(Id), Path.Combine(DataDir, AddonProtocol.ValuesFile), s) : null;

    /// Folder commands to the mod in this add-on's data folder.
    public TaskbarCommands Commands => commands ??= new TaskbarCommands(DataDir);

    /// The taskbar's pins and folders as the mod last wrote them.
    public TaskbarState? State()
    {
        var s = TaskbarState.Read(DataDir);
        if (s is not null) Commands.Heard(File.GetLastWriteTimeUtc(Path.Combine(DataDir, AddonProtocol.StateFile)));
        return s;
    }

    /// Makes the run copy and values.json and starts the host (a worker thread: copying and hashing read the disk).
    public void Start(bool resetSafeMode = false)
    {
        if (!AddonHub.Offered) return;
        lock (gate)
        {
            if (Broken || Manifest is not { } m) return;
            try
            {
                string dir = store.RunCopy(m, runner is { Running: true } ? null : StepDown);
                store.Prune(m);
                // from the setting, not the UI's theme: at a start from sign-in this runs before the UI has made it
                AddonHub.FollowTheme(this, SystemTheme.Dark(Settings.Current.Theme));
                AddonHub.WriteValues(this);
                Watch();
                // a version downloaded since the last start runs from its own copy: a runner of the old one that has
                // stopped is let go
                if (runner is { Running: false } old && StartedVersion != m.Version)
                {
                    old.Changed -= AddonHub.Notify;
                    old.Dispose();
                    runner = null;
                }
                if (runner is null)
                {
                    var r = new AddonRunner(Path.Combine(dir, m.HostExe), StopEvent, OtherHost);
                    r.Changed += AddonHub.Notify;
                    runner = r;
                }
                problem = false;
                bool wasRunning = runner.Running; // then Start leaves it, and its version, as they are
                runner.Start(resetSafeMode);
                if (!wasRunning) StartedVersion = m.Version;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Error("add-on " + Id, e);
                problem = true;
            }
        }
        AddonHub.Notify();
    }

    /// The run copy of this version is another build of it (a local install over the downloaded release, or the other way
    /// round) and the host the last Bloom left running runs from it, with explorer holding its DLLs. A start would only
    /// hand that host this Bloom's process and keep the other build; the copy can't be replaced while it runs, and without
    /// a start the host ends once its wait for a new Bloom is over. So it is stopped first, the way a host of another
    /// version steps down for a new one, and this build starts in its place.
    private void StepDown()
    {
        Log.Write($"add-on {Id}: the run copy of {Manifest?.Version} is another build and in use; stopping its host");
        AddonRunner.StopHost(StopEvent, null, OtherHost, AddonRunner.StopWait);
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

/// Bloom's add-ons (the taskbar one and the others its catalog offers, downloaded from Settings > Add-ons or in a local
/// install's folder) and the user's own, their start with Bloom and their status.
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

    /// Read once a run: an add-on put in the user's folder later shows after Bloom restarts.
    public static IReadOnlyList<Addon> All => LazyInitializer.EnsureInitialized(ref all, () => Discover(AddonStore.Default, AddonStore.User, AddonCatalog.BuiltIn).ToArray());

    public static Addon Taskbar => All[0];

    /// Every add-on draws in Bloom's theme, the taskbar one or not (a widget add-on works on Windows' own taskbar too):
    /// its pill.theme is kept to the one Bloom shows and its pill.glass to 유리 효과 (Settings.Transparency), on each
    /// theme change and as it starts.
    public static void FollowTheme(bool dark)
    {
        foreach (var a in All) FollowTheme(a, dark);
    }

    internal static void FollowTheme(Addon a, bool dark) => FollowTheme(a.Schema, a.Values, dark, Settings.Current.Transparency != "off");

    /// Each key only while the add-on's schema has it (an older add-on has no pill.glass); written once when either changed.
    internal static void FollowTheme(AddonSchema? schema, AddonValues? v, bool dark, bool glass)
    {
        if (schema is null || v is null) return;
        bool changed = false;
        string theme = dark ? "dark" : "light";
        if (schema["pill.theme"] is not null && v.Text("pill.theme") != theme)
        {
            v.Set("pill.theme", theme);
            changed = true;
        }
        if (schema["pill.glass"] is not null && v.Bool("pill.glass") != glass)
        {
            v.Set("pill.glass", glass);
            changed = true;
        }
        if (changed) v.Flush();
    }

    /// Bloom's add-ons besides the taskbar one: the others its catalog offers, in the catalog's order.
    public static IEnumerable<Addon> Listed => All.Skip(1).Where(a => !a.User);

    /// The user's own add-ons, in id order.
    public static IEnumerable<Addon> Users => All.Where(a => a.User);

    /// The shipped taskbar add-on first, then the others the catalog offers (downloaded into Bloom's own store as the
    /// taskbar one is), then each one in the user's folder; a user one with an id already listed is left out (it would
    /// share that one's data folder and switch).
    internal static List<Addon> Discover(AddonStore shipped, AddonStore user, IEnumerable<AddonCatalog.Entry>? catalog = null)
    {
        var list = new List<Addon> { new(AddonProtocol.Id, shipped) };
        foreach (var e in catalog ?? Enumerable.Empty<AddonCatalog.Entry>())
            if (list.All(a => a.Id != e.Id)) list.Add(new Addon(e.Id, shipped) { Catalog = e });
        foreach (string id in user.Ids())
            if (list.All(a => a.Id != id)) list.Add(new Addon(id, user, user: true));
        return list;
    }

    /// How long after Bloom's start, and then how often while it runs, the add-ons switched on look for a new release.
    internal static readonly TimeSpan FirstLook = TimeSpan.FromSeconds(20), Every = TimeSpan.FromHours(24);

    /// Looks at the add-ons' releases by themselves: only while 설정 > 정보 > 켤 때 업데이트 확인 is on (one who turned it
    /// off wants no look at GitHub Bloom didn't ask for), and never in a dev or QA copy.
    private static bool AutoLook => Offered && Settings.Current.UpdateCheck;

    private static Timer? daily;

    /// Bloom starts: the add-ons switched on start on a worker thread, each with the version it has, and look for a new
    /// release a little later and then once a day (one found is put to work at once, Addon.Update). `autostart`: started at
    /// sign-in (logged with the time since then, for comparing the ways Bloom is started at sign-in).
    public static void Begin(bool autostart)
    {
        if (!Offered) return;
        daily ??= new Timer(_ => LookAll(), null, FirstLook, Every);
        if (Settings.Current.AddonsOn.Count == 0) return;
        if ((Native.Win32.GetAsyncKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0)
        {
            SkippedByShift = true;
            Log.Write("add-ons: Shift held at start, none started");
            return;
        }
        _ = Task.Run(() => Task.WhenAll(All.Where(a => a.On).Select(async a =>
        {
            await Bring(a, fresh: false).ConfigureAwait(false);
            if (autostart) Log.Write($"add-on {a.Id}: started from --autostart, {LogonTask.SinceSignIn()} after sign-in");
        })));
    }

    /// The timer's look: every add-on switched on updates (not this run when Shift was held at the start).
    private static void LookAll()
    {
        if (!AutoLook || SkippedByShift) return;
        foreach (var a in All.Where(a => a.On && !a.User)) _ = Update(a);
    }

    /// The switch on the settings page: saved at once, the host started (after its latest release is read, and downloaded
    /// when newer, Bring) or stopped on a worker thread.
    public static Task Set(Addon a, bool on)
    {
        var list = Settings.Current.AddonsOn;
        list.Remove(a.Id);
        if (on) list.Add(a.Id);
        Settings.Save();
        LogonTask.SyncLater();
        Notify();
        return on ? Task.Run(() => Bring(a, fresh: true)) : Task.Run(a.Stop);
    }

    /// Starts the add-on. Just switched on (`fresh`) or with nothing to run yet, its latest release is read (and
    /// downloaded when newer) first, so it starts once, with the newest; at Bloom's start the version it has starts at
    /// once (the taskbar isn't held up) and the timer's look comes later. A look that fails leaves the version it has,
    /// which starts as before.
    private static async Task Bring(Addon a, bool fresh)
    {
        if (fresh || a.Manifest is null || a.Broken) await Update(a).ConfigureAwait(false);
        a.Start();
    }

    /// 다시 켜기 (safe mode cleared) and 다시 시도.
    public static Task Retry(Addon a, bool resetSafeMode) => Task.Run(() => a.Start(resetSafeMode));

    /// The client the add-ons' releases are read and downloaded over: the API chats' shared one.
    private static HttpClient Http => Ai.ApiClient.Shared;

    /// 받기, 업데이트 and 다시 받기, the switch and the timer: its latest release read and, when newer, downloaded and put to
    /// work (Addon.Update), on a worker thread. Also in a dev or QA copy (into its own data folder), where nothing is
    /// started from it.
    public static Task Update(Addon a) => a.Update(Http);

    /// The settings page is drawn: each of `addons` whose latest release wasn't read in the last day reads it (no
    /// download), so the page can offer 업데이트 for one that is off too.
    public static void Look(IEnumerable<Addon> addons)
    {
        foreach (var a in addons)
            if (a.Catalog is not null && !a.Fetch.Busy && !a.LookedWithin(Every)) _ = Task.Run(() => a.Look(Http));
    }

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

    /// The user's values for the add-on, kept with Bloom's settings.
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

    /// A code the protocol adds later gets its line here, an AddonState and a status text, and joins IsFinal when the user
    /// has to act before a start can work; until then it reads as Stopped.
    private static AddonState FromCode(int code) => code switch
    {
        AddonProtocol.ExitCrashes => AddonState.SafeMode,
        AddonProtocol.ExitNoSymbols => AddonState.NoSymbols,
        AddonProtocol.ExitWindhawk => AddonState.Windhawk,
        AddonProtocol.ExitBlocked => AddonState.Blocked,
        AddonProtocol.ExitDownload => AddonState.Download,
        AddonProtocol.ExitRestart => AddonState.Restart,
        _ => AddonState.Stopped,
    };

    private static AddonState FromReason(string? reason) => reason switch
    {
        "crashes" => AddonState.SafeMode,
        "noSymbols" => AddonState.NoSymbols,
        "windhawk" => AddonState.Windhawk,
        "blocked" => AddonState.Blocked,
        "download" => AddonState.Download,
        "restart" => AddonState.Restart,
        _ => AddonState.Stopped,
    };
}
