using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Xml.Linq;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Tests;

/// The add-on host's life without explorer: this test program stands in for the host (FakeAddonHost) and ends with a
/// given code or waits for a Stop event of the test's own (never the real one, which a host on this PC listens to).
internal static class AddonRunnerTests
{
    private static readonly TimeSpan[] Quick = { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50) };

    private static string TestEvent() => @"Local\Bloom.Tests.Addon." + Guid.NewGuid().ToString("N") + ".Stop";

    /// The runner for the stand-in: this program (with its dll first when it runs under dotnet).
    private static AddonRunner Runner(string stopEvent, TimeSpan? stopWait = null)
    {
        string exe = Environment.ProcessPath!;
        var lead = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { typeof(AddonRunnerTests).Assembly.Location } : null;
        return new AddonRunner(exe, stopEvent, null, Quick, stopWait, lead);
    }

    private static bool Until(Func<bool> done, int ms = 15000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > ms) return false;
            Thread.Sleep(20);
        }
        return true;
    }

    private static void With(string mode, Action run, string? argsFile = null)
    {
        Environment.SetEnvironmentVariable(FakeAddonHost.Mode, mode);
        Environment.SetEnvironmentVariable(FakeAddonHost.ArgsFile, argsFile);
        try
        {
            run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(FakeAddonHost.Mode, null);
            Environment.SetEnvironmentVariable(FakeAddonHost.ArgsFile, null);
        }
    }

    public static void FinalCodesNotRestarted()
    {
        foreach (int code in new[] { 10, 11, 12, 13, 14 })
            With($"exit:{code}", () =>
            {
                using var r = Runner(TestEvent());
                Check.True(r.Start(), "started");
                Check.True(Until(() => r.LastExit == code && !r.Running), $"ended with {code}");
                Thread.Sleep(200); // longer than the quick retry delay
                Check.Eq(r.Spawns, 1, $"{code}: not started again");
                Check.True(!r.Waiting && !r.GaveUp, $"{code}: no retry waiting");
            });
    }

    public static void ErrorsRetriedTwiceThenGiveUp()
    {
        With("exit:1", () =>
        {
            using var r = Runner(TestEvent());
            r.Start();
            Check.True(Until(() => r.GaveUp), "gave up");
            Check.Eq(r.Spawns, 3, "the first start and two more");
            Thread.Sleep(200);
            Check.Eq(r.Spawns, 3, "then no more");
            Check.Eq(r.LastExit, 1, "the code");
        });
        Check.Eq(AddonRunner.Retries.Length, 2, "two retries");
        Check.Eq(AddonRunner.Retries[0], TimeSpan.FromSeconds(5), "after 5 s");
        Check.Eq(AddonRunner.Retries[1], TimeSpan.FromSeconds(30), "then after 30 s");
    }

    public static void NormalEndNotRestarted()
    {
        With("exit:0", () =>
        {
            using var r = Runner(TestEvent());
            r.Start();
            Check.True(Until(() => r.LastExit == 0 && !r.Running), "ended");
            Thread.Sleep(200);
            Check.True(r.Spawns == 1 && !r.Waiting && !r.GaveUp, "0 is a normal end (asked to stop, or handed over)");
        });
    }

    public static void StopThroughTheEvent()
    {
        string ev = TestEvent();
        string args = Path.Combine(Path.GetTempPath(), "bloom-addon-args-" + Guid.NewGuid().ToString("N"));
        With("wait:" + ev, () =>
        {
            using var r = Runner(ev);
            Check.True(r.Start(resetSafeMode: true), "started");
            Check.True(Until(() => File.Exists(args)), "the stand-in is up");
            var expect = AddonRunner.Arguments(Environment.ProcessId, Environment.ProcessPath ?? "", Paths.Local, true);
            Check.Eq(string.Join("|", File.ReadAllLines(args).TakeLast(expect.Count)), string.Join("|", expect), "the protocol's arguments");
            Check.Eq(expect[0] + expect[2] + expect[4] + expect[^1], "--serve--bloom--bloom-local--reset-safe-mode", "their names");
            var clock = Stopwatch.StartNew();
            r.Stop();
            Check.True(clock.Elapsed < TimeSpan.FromSeconds(8), "ended by the event, not by force");
            Check.True(Until(() => r.LastExit == 0 && !r.Running), "ended with 0");
            Thread.Sleep(200);
            Check.Eq(r.Spawns, 1, "not started again after a stop");
        }, args);
        File.Delete(args);
    }

    public static void StopEndsAHostThatDoesNotListen()
    {
        With("hang", () =>
        {
            using var r = Runner(TestEvent(), TimeSpan.FromMilliseconds(500));
            r.Start();
            Thread.Sleep(300);
            r.Stop();
            Check.True(Until(() => !r.Running), "ended by force after the wait");
            Thread.Sleep(200);
            Check.Eq(r.Spawns, 1, "and not started again");
        });
    }

    /// Dev and QA copies (--data) never start a host or touch the add-on's files: the same rule as the elevated Win-key
    /// helper's (not a dev copy, the main instance).
    public static void NoHostFromADevCopy()
    {
        string shipped = Path.Combine(Path.GetTempPath(), "bloom-addon-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(shipped, AddonProtocol.Id));
        File.WriteAllText(Path.Combine(shipped, AddonProtocol.Id, "PillTaskbarHost.exe"), "x");
        var manifest = new JsonObject
        {
            ["id"] = AddonProtocol.Id,
            ["version"] = "0.1.0",
            ["license"] = "GPL-3.0-only",
            ["source"] = "https://example.invalid",
            ["minBloom"] = "0.1.0",
            ["host"] = new JsonObject { ["exe"] = "PillTaskbarHost.exe", ["protocol"] = AddonProtocol.Version },
            ["files"] = new JsonObject { ["PillTaskbarHost.exe"] = AddonStore.Sha256(Path.Combine(shipped, AddonProtocol.Id, "PillTaskbarHost.exe")) },
        };
        File.WriteAllText(Path.Combine(shipped, AddonProtocol.Id, AddonManifest.FileName), manifest.ToJsonString());
        try
        {
            using (var folder = new SettingsTransferTests.DataFolder())
            {
                Check.True(Paths.IsDev, "a dev copy");
                Check.True(!AddonHub.Offered, "offered to no dev copy");
                Settings.Current.AddonsOn.Add(AddonProtocol.Id);
                var addon = new Addon(AddonProtocol.Id, new AddonStore(shipped, Path.Combine(Paths.Local, "addons")));
                Check.NotNull(addon.Manifest, "shipped and valid");
                addon.Start();
                addon.Stop();
                Check.True(!Directory.Exists(addon.DataDir), "no run copy, no values.json");
                Check.Eq(addon.Status().State, AddonState.Dev, "the page says why");
                AddonHub.Begin(autostart: true); // returns at once
                Settings.Current.AddonsOn.Remove(AddonProtocol.Id);
            }
            Check.Eq(AddonHub.Offered, !Paths.IsDev && Paths.InstanceId == "Bloom", "the rule");
        }
        finally
        {
            Directory.Delete(shipped, true);
        }
    }

    /// Windhawk keeps the mod's numbers as REG_DWORD: 0xFFFFFFFF is the -1 the mod reads.
    public static void WindhawkValuesMapped()
    {
        var src = new FakeSource(new()
        {
            new("pill.enabled", 1),
            new("autohide.showDelay", 0xFFFFFFFFu),
            new("size.iconSize", -1),
            new("pill.style", "windowGlass"),
            new("binary", new byte[] { 1, 2 }),
            new("multi", new[] { "a", "b" }),
        });
        var map = Check.NotNull(WindhawkImport.Map(src), "values");
        Check.Eq(map.Count, 4, "numbers and text only");
        Check.Eq(map["autohide.showDelay"]!.GetValue<int>(), -1, "0xFFFFFFFF is -1");
        Check.Eq(map["size.iconSize"]!.GetValue<int>(), -1, "a DWORD the registry hands over as -1");
        Check.Eq(map["pill.enabled"]!.GetValue<int>(), 1, "a number");
        Check.Eq(map["pill.style"]!.GetValue<string>(), "windowGlass", "text");
        string dir = Path.Combine(Path.GetTempPath(), "bloom-windhawk-" + Guid.NewGuid().ToString("N"));
        try
        {
            string settings = Path.Combine(dir, "data", "settings.json"), values = Path.Combine(dir, "local", "values.json");
            Check.Eq(WindhawkImport.Import(src, settings, values), 4, "imported");
            foreach (string f in new[] { settings, values })
                Check.Eq(JsonNode.Parse(File.ReadAllText(f))!["autohide.showDelay"]!.GetValue<int>(), -1, Path.GetFileName(f) + " written");
            Check.Eq(WindhawkImport.Import(new FakeSource(null), settings + "2", values + "2"), -1, "no key: nothing");
            Check.True(!File.Exists(settings + "2"), "and nothing written");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private sealed class FakeSource : ISettingsSource
    {
        private readonly List<KeyValuePair<string, object?>>? values;

        public FakeSource(List<KeyValuePair<string, object?>>? values) => this.values = values;

        public IReadOnlyList<KeyValuePair<string, object?>>? Read() => values;
    }

    /// A settings file from elsewhere never switches an add-on on (it loads code into explorer) or skips its notice.
    public static void ImportedSettingsNeverTurnAnAddonOn()
    {
        Check.True(SettingsTransfer.Local.Contains("addonsOn") && SettingsTransfer.Local.Contains("addonsConfirmed"), "kept local");
        using var folder = new SettingsTransferTests.DataFolder();
        string archive = Path.Combine(folder.Directory, "incoming.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("bloom-settings.json").Open())) w.Write("""{"app":"Bloom"}""");
            using (var w = new StreamWriter(zip.CreateEntry("settings.json").Open())) w.Write("""{"theme":"light","addonsOn":["pill-taskbar"],"addonsConfirmed":["pill-taskbar"]}""");
        }
        SettingsTransfer.Import(archive);
        var fresh = (JsonObject)JsonNode.Parse(File.ReadAllText(Paths.In("settings.json")))!;
        Check.True(!fresh.ContainsKey("addonsOn") && !fresh.ContainsKey("addonsConfirmed"), "dropped on import");
        Check.Eq(fresh["theme"]!.GetValue<string>(), "light", "the rest comes in");
    }

    public static void LogonTaskXml()
    {
        const string sid = "S-1-5-21-1004336348-1177238915-682003330-1001";
        var doc = XDocument.Parse(LogonTask.Xml(sid, @"PC\A&B", @"C:\Users\x\AppData\Local\BloomLauncher\current\Bloom.exe"));
        XNamespace t = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string V(params string[] path) => path.Aggregate(doc.Root!, (e, n) => e.Element(t + n)!).Value;
        Check.Eq(V("Triggers", "LogonTrigger", "UserId"), @"PC\A&B", "at this user's sign-in only (escaped)");
        Check.Eq(V("Triggers", "LogonTrigger", "Enabled"), "true", "on");
        Check.Eq(V("Principals", "Principal", "UserId"), sid, "as this user");
        Check.Eq(V("Principals", "Principal", "LogonType"), "InteractiveToken", "in the user's session");
        Check.Eq(V("Principals", "Principal", "RunLevel"), "LeastPrivilege", "not elevated");
        Check.Eq(V("Settings", "ExecutionTimeLimit"), "PT0S", "no time limit");
        Check.Eq(V("Settings", "DisallowStartIfOnBatteries"), "false", "starts on battery");
        Check.Eq(V("Settings", "StopIfGoingOnBatteries"), "false", "keeps running on battery");
        Check.Eq(V("Settings", "MultipleInstancesPolicy"), "IgnoreNew", "one at a time");
        Check.Eq(V("Actions", "Exec", "Command"), "\"C:\\Users\\x\\AppData\\Local\\BloomLauncher\\current\\Bloom.exe\"", "this exe");
        Check.Eq(V("Actions", "Exec", "Arguments"), "--autostart", "as a start at sign-in");
        Check.Eq(LogonTask.Name(sid), "BloomLogon-" + sid, "one task per user");
    }

    /// The launcher's settings search finds the page's lines (collect mode), without reading the host's files.
    public static void TaskbarPageCollected()
    {
        var lines = new Ui.SettingsView(new Host(new Options())).CollectTaskbarForTests();
        Check.True(lines.Any(l => l.Title == "작업표시줄 꾸미기" && l.PageKey == "taskbar" && l.PageTitle == "작업표시줄" && l.Group == "PillTaskbar"), "the switch on 설정 > 작업표시줄");
        Check.True(lines.All(l => l.PageKey == "taskbar"), "nothing else");
    }
}
