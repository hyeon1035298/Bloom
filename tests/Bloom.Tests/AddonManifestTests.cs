using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bloom.Addons;

namespace Bloom.Tests;

/// addon.json is checked before anything of an add-on is copied or started, and the run copy only ever holds files whose
/// SHA-256 matches it.
internal static class AddonManifestTests
{
    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("host exe"), Dll = Encoding.ASCII.GetBytes("runtime dll");

    private static JsonObject Good(string version = "0.1.0") => new()
    {
        ["id"] = "pill-taskbar",
        ["version"] = version,
        ["license"] = "GPL-3.0-only",
        ["source"] = "https://example.invalid/pill-taskbar",
        ["minBloom"] = "0.18.0",
        ["host"] = new JsonObject { ["exe"] = "PillTaskbarHost.exe", ["protocol"] = AddonProtocol.Version },
        ["files"] = new JsonObject { ["PillTaskbarHost.exe"] = Sha(Exe), ["bin/PillTaskbarRuntime.dll"] = Sha(Dll) },
    };

    private static bool Refused(string json)
    {
        try
        {
            AddonManifest.Parse(json);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    public static void GoodManifest()
    {
        var m = AddonManifest.Parse(Good().ToJsonString());
        Check.Eq(m.Id, "pill-taskbar", "id");
        Check.Eq(m.Version, "0.1.0", "version");
        Check.Eq(m.License, "GPL-3.0-only", "license");
        Check.Eq(m.HostExe, "PillTaskbarHost.exe", "host exe");
        Check.Eq(m.HostProtocol, AddonProtocol.Version, "protocol");
        Check.True(m.Files.ContainsKey(@"bin\PillTaskbarRuntime.dll"), "a path in a subfolder, with Windows' separator");
        Check.True(m.Fits(new Version(0, 18, 0)) && m.Fits(new Version(1, 0, 0)) && !m.Fits(new Version(0, 17, 9)), "minBloom");
        Check.True(Refused("not json") && Refused("[]"), "not an object");
    }

    public static void BadIdsRefused()
    {
        foreach (string id in new[] { "", "Pill", "pill_taskbar", "-pill", "pill taskbar", "../pill", "pill.taskbar", new string('a', 65) })
        {
            var j = Good();
            j["id"] = id;
            Check.True(Refused(j.ToJsonString()), $"id «{id}»");
        }
        foreach (string v in new[] { "..", ".", "0.1/0", @"0\1", "0.1 beta", "" })
        {
            var j = Good();
            j["version"] = v;
            Check.True(Refused(j.ToJsonString()), $"version «{v}»");
        }
    }

    public static void PathsStayInside()
    {
        foreach (string p in new[] { @"C:\x.exe", "C:x.exe", @"\x.exe", "/x.exe", @"..\x.exe", "a/../x.exe", "./x.exe", @"\\server\share\x.exe", "x.exe:stream", "a//x.exe", "x.exe.", "x<.exe" })
        {
            Check.Eq(AddonManifest.SafePath(p), null, $"«{p}» refused");
            var asExe = Good();
            asExe["host"]!["exe"] = p;
            Check.True(Refused(asExe.ToJsonString()), $"host.exe «{p}»");
            var asFile = Good();
            asFile["files"]![p] = Sha(Exe);
            Check.True(Refused(asFile.ToJsonString()), $"file «{p}»");
        }
        Check.Eq(AddonManifest.SafePath("a/b/c.dll"), @"a\b\c.dll", "a nested path");
    }

    public static void HashesAndProtocolChecked()
    {
        var j = Good();
        j["files"]!["PillTaskbarHost.exe"] = "abc";
        Check.True(Refused(j.ToJsonString()), "a hash that is no SHA-256");
        j = Good();
        j["host"]!["protocol"] = AddonProtocol.Version + 1;
        Check.True(Refused(j.ToJsonString()), "another protocol");
        j = Good();
        j["host"]!["exe"] = "Other.exe";
        Check.True(Refused(j.ToJsonString()), "a host exe not among the hashed files");
        j = Good();
        j.Remove("minBloom");
        Check.True(Refused(j.ToJsonString()), "no minBloom");
        Check.Eq(new Addon(AddonProtocol.Id, new AddonStore("x", "y")).StopEvent, AddonProtocol.HostStopEvent, "the Stop event's name matches the protocol's");
    }

    /// A temporary shipped folder and local folder for the store.
    private sealed class Folders : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-addon-test-" + Guid.NewGuid().ToString("N"));
        public string Shipped => Path.Combine(Root, "shipped");
        public string Local => Path.Combine(Root, "local");
        public AddonStore Store => new(Shipped, Local);

        public Folders(JsonObject manifest)
        {
            string dir = Path.Combine(Shipped, "pill-taskbar");
            Directory.CreateDirectory(Path.Combine(dir, "bin"));
            File.WriteAllBytes(Path.Combine(dir, "PillTaskbarHost.exe"), Exe);
            File.WriteAllBytes(Path.Combine(dir, "bin", "PillTaskbarRuntime.dll"), Dll);
            File.WriteAllText(Path.Combine(dir, AddonManifest.FileName), manifest.ToJsonString());
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    public static void RunCopyChecksAndReuses()
    {
        using var f = new Folders(Good());
        var m = Check.NotNull(f.Store.Shipped("pill-taskbar"), "the shipped manifest");
        string copy = f.Store.RunCopy(m);
        Check.Eq(copy, Path.Combine(f.Local, "pill-taskbar", "0.1.0"), "one folder per version under the data folder");
        Check.True(File.ReadAllBytes(Path.Combine(copy, "PillTaskbarHost.exe")).SequenceEqual(Exe) && File.ReadAllBytes(Path.Combine(copy, "bin", "PillTaskbarRuntime.dll")).SequenceEqual(Dll), "the files copied");
        File.WriteAllText(Path.Combine(copy, "marker"), "");
        Check.Eq(f.Store.RunCopy(m), copy, "the same folder again");
        Check.True(File.Exists(Path.Combine(copy, "marker")), "an identical copy is not made again");
        File.WriteAllText(Path.Combine(copy, "PillTaskbarHost.exe"), "changed");
        f.Store.RunCopy(m);
        Check.True(File.ReadAllBytes(Path.Combine(copy, "PillTaskbarHost.exe")).SequenceEqual(Exe), "a copy that no longer matches is made again");
        Check.Eq(Directory.GetDirectories(Path.Combine(f.Local, "pill-taskbar")).Length, 1, "no staging folder left");
        Check.Eq(f.Store.Shipped("other"), null, "an add-on Bloom didn't ship");
    }

    public static void RunCopyRefusesHashMismatch()
    {
        var j = Good("0.2.0");
        j["files"]!["PillTaskbarHost.exe"] = Sha(Encoding.ASCII.GetBytes("something else"));
        using var f = new Folders(j);
        var m = f.Store.Shipped("pill-taskbar")!;
        bool threw = false;
        try
        {
            f.Store.RunCopy(m);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }
        Check.True(threw, "a file whose SHA-256 differs from addon.json");
        string data = Path.Combine(f.Local, "pill-taskbar");
        Check.True(!Directory.Exists(Path.Combine(data, "0.2.0")), "no run copy");
        Check.Eq(Directory.Exists(data) ? Directory.GetDirectories(data).Length : 0, 0, "no staging folder left");
    }

    public static void PruneKeepsDataAndLockedCopies()
    {
        using var f = new Folders(Good());
        var m = f.Store.Shipped("pill-taskbar")!;
        f.Store.RunCopy(m);
        string data = Path.Combine(f.Local, "pill-taskbar");
        foreach (string old in new[] { "0.0.9", "0.0.7" })
        {
            Directory.CreateDirectory(Path.Combine(data, old));
            File.WriteAllText(Path.Combine(data, old, AddonManifest.FileName), "{}");
            File.WriteAllText(Path.Combine(data, old, "PillTaskbar.dll"), "old");
        }
        Directory.CreateDirectory(Path.Combine(data, AddonProtocol.OffsetsDir));
        File.WriteAllText(Path.Combine(data, AddonProtocol.OffsetsDir, "taskbar.dll_X.txt"), "1#");
        Directory.CreateDirectory(Path.Combine(data, ".0.0.8.abc.tmp"));
        using (new FileStream(Path.Combine(data, "0.0.7", "PillTaskbar.dll"), FileMode.Open, FileAccess.Read, FileShare.None)) // as if explorer held it
            f.Store.Prune(m);
        Check.True(!Directory.Exists(Path.Combine(data, "0.0.9")), "an older run copy goes");
        Check.True(!Directory.Exists(Path.Combine(data, ".0.0.8.abc.tmp")), "a staging folder left by a crash goes");
        Check.True(File.Exists(Path.Combine(data, "0.0.7", AddonManifest.FileName)), "a copy still in use stays, still known as a run copy");
        Check.True(File.Exists(Path.Combine(data, AddonProtocol.OffsetsDir, "taskbar.dll_X.txt")), "the data folders stay");
        Check.True(Directory.Exists(Path.Combine(data, "0.1.0")), "the current copy stays");
    }

    /// The status the settings page shows, from the switch, the runner and the host's files.
    public static void StatusFromFacts()
    {
        static AddonHub.Facts On(bool alive = true, int? exit = null, HostState? host = null, RuntimeState? rt = null) =>
            new(true, true, false, true, false, true, alive, false, exit, false, host, rt);
        Check.Eq(AddonHub.Decide(On() with { Offered = false }).State, AddonState.Dev, "a dev copy");
        Check.Eq(AddonHub.Decide(On() with { Shipped = false }).State, AddonState.NotShipped, "not shipped");
        Check.Eq(AddonHub.Decide(On() with { On = false }).State, AddonState.Off, "switched off");
        Check.Eq(AddonHub.Decide(On() with { Skipped = true, Started = false, HostAlive = false }).State, AddonState.Skipped, "Shift at start");
        var symbols = new HostState { Pid = 5, Phase = "symbols", Progress = new HostState.HostProgress { Done = 30, Total = 120 } };
        Check.Eq(AddonHub.Decide(On(host: symbols)), new AddonStatus(AddonState.Starting, 25), "symbols with progress");
        var running = new HostState { Pid = 5, Phase = "running", Loaded = new HostState.HostLoaded { Pid = 77 } };
        Check.Eq(AddonHub.Decide(On(host: running, rt: new RuntimeState { Pid = 77, State = "running" })).State, AddonState.Running, "running in the explorer it loaded into");
        Check.Eq(AddonHub.Decide(On(host: running, rt: new RuntimeState { Pid = 70, State = "running" })).State, AddonState.Starting, "a runtime file of an earlier explorer");
        Check.Eq(AddonHub.Decide(On(host: running)).State, AddonState.Starting, "no runtime file yet");
        Check.Eq(AddonHub.Decide(On(host: new HostState { Pid = 5, Phase = "failed", Reason = "windhawk" })).State, AddonState.Windhawk, "failed: the reason");
        var codes = new Dictionary<int, AddonState> { [10] = AddonState.SafeMode, [11] = AddonState.NoSymbols, [12] = AddonState.Windhawk, [13] = AddonState.Blocked, [14] = AddonState.Download };
        foreach (var (code, state) in codes) Check.Eq(AddonHub.Decide(On(alive: false, exit: code)).State, state, $"exit {code}");
        Check.Eq(AddonHub.Decide(On(alive: false, host: new HostState { SafeMode = true })).State, AddonState.SafeMode, "safe mode from an earlier run");
        Check.Eq(AddonHub.Decide(On(alive: false) with { Failed = true }).State, AddonState.Stopped, "gave up");
        Check.Eq(AddonHub.Decide(On(alive: false) with { Started = false }).State, AddonState.Starting, "about to start");
    }
}
