namespace Bloom.Addons;

/// What Bloom and the taskbar add-on's host (PillTaskbarHost.exe, a separate GPL program shipped beside Bloom) agree on:
/// file names, named objects, arguments and exit codes. The add-on keeps the same values in its own header; change both
/// together and raise Version. Bloom only starts the host and talks to it through these: files, arguments, one event and
/// the exit code. It never loads any of the add-on's code.
internal static class AddonProtocol
{
    public const string Id = "pill-taskbar";

    /// addon.json host.protocol. Raised when a name, file or code below changes meaning.
    public const int Version = 1;

    // In the add-on's data folder, <Paths.Local>\addons\pill-taskbar.
    public const string ValuesFile = "values.json";
    public const string HostStateFile = "host-state.json";
    public const string RuntimeStateFile = "runtime-state.json";
    public const string HostLog = "host.log";
    public const string RuntimeLog = "runtime.log";
    public const string OffsetsDir = "offsets";
    public const string SymbolsDir = "symbols";

    // The taskbar's pins and folders (the mod writes state.json) and the folder commands Bloom sends: inbox\<id>.json in,
    // outbox\<id>.json back, one at a time in name order.
    public const string StateFile = "state.json";
    public const string InboxDir = "inbox";
    public const string OutboxDir = "outbox";

    // Next to the host exe, in the run copy Bloom makes of the shipped folder.
    public const string RuntimeDll = "PillTaskbarRuntime.dll";
    public const string ModDll = "PillTaskbar.dll";
    public const string SymbolList = "symbols.txt";

    // Named objects. %u is the explorer process id (the explorer-side names never carry a version).
    public const string HostMutex = @"Local\Bloom.Addon.pill-taskbar.Host";
    public const string HostStopEvent = @"Local\Bloom.Addon.pill-taskbar.Stop";
    public const string HostWindowClass = "Bloom.Addon.pill-taskbar.Host";
    public const string ConfigMapping = @"Local\PillTaskbar.Config.%u";
    public const string LoadedEvent = @"Local\PillTaskbar.Loaded.%u";
    public const string UnloadEvent = @"Local\PillTaskbar.Unload.%u";
    public const string UnloadedEvent = @"Local\PillTaskbar.Unloaded.%u";
    public const string RuntimeMutex = @"Local\PillTaskbar.Runtime.%u";

    /// WM_COPYDATA from a second host start to the running one ('PTH1').
    public const uint CopyDataCommand = 0x50544831;

    // The host's command line (Bloom starts it with --serve).
    public const string ServeArg = "--serve";
    public const string BloomArg = "--bloom";
    public const string BloomLocalArg = "--bloom-local";
    public const string ResetSafeModeArg = "--reset-safe-mode";

    // Host exit codes.
    public const int ExitOk = 0;
    public const int ExitError = 1; // bad arguments, missing files, anything unexpected
    public const int ExitCrashes = 10; // explorer kept crashing after the load: safe mode is on
    public const int ExitNoSymbols = 11; // a required taskbar symbol is not in this Windows build
    public const int ExitWindhawk = 12; // the same mod is on in Windhawk
    public const int ExitBlocked = 13; // code integrity (Smart App Control) kept the DLL out
    public const int ExitDownload = 14; // the symbol files could not be downloaded
    public const int ExitRestart = 15; // an earlier mod or runtime image is still in explorer: it must restart first

    /// A code the host ends with on purpose: starting it again would only end the same way, so Bloom waits for the user.
    public static bool IsFinal(int code) => code is >= ExitCrashes and <= ExitRestart;
}
