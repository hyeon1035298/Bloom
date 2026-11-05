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
        foreach (int code in new[] { 10, 11, 12, 13, 14, 15 })
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
        var lines = new Ui.SettingsView(new Host(new Options())).CollectAddonsForTests();
        Check.True(lines.Any(l => l.Title == "작업표시줄" && l.PageKey == "addons" && l.PageTitle == "애드온" && l.Group == "애드온 목록"), "the switch on 설정 > 애드온");
        Check.True(lines.All(l => l.PageKey == "addons"), "nothing else");
        Check.True(!lines.Any(l => l.Group == "작업표시줄 모양"), "its settings only while it is on");
    }
}
