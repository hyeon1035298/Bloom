using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// 설정 > 애드온 past the taskbar add-on's switch: the add-on's settings drawn from the schema it ships (a small hand-made one here),
/// the values written back, and the folder editor's two files (state.json in, commands out and their answers).
internal static class TaskbarPageTests
{
    /// The add-on's setting keys (its interface, from settings.schema.json): the page has a place for each.
    private static readonly string[] AddonKeys =
    {
        "pill.enabled", "pill.style", "pill.cornerRadius", "pill.blur", "pill.tintOpacity",
        "tray.hideVolume", "tray.hideNetwork", "tray.hideBattery", "tray.hideMicrophone", "tray.hideLocation", "tray.hideStudioEffects",
        "tray.hideRecall", "tray.hideLanguageBar", "tray.hideLanguageExtras", "tray.hideCopilot", "tray.bell", "tray.clock",
        "size.enabled", "size.taskbarHeight", "size.iconSize", "size.iconSpacing",
        "start.mode",
        "autohide.enabled", "autohide.revealZone", "autohide.showDelay", "autohide.hideDelay", "autohide.animation", "autohide.keepHiddenForBloom",
        "autohide.keepHiddenOnFlash",
        "media.enabled", "media.maxWidth",
        "usage.enabled", "usage.logoColor", "usage.gapBefore", "usage.gapAfter",
        "island.enabled", "island.showClock", "island.showMedia", "island.showUsage", "island.height", "island.width", "island.reveal", "island.hoverTransparency",
        "folders.enabled", "folders.foldersPath", "folders.mergeDelay", "folders.popupScale", "folders.showLabels", "folders.iconSize", "folders.appSpacing",
        "folders.popupSide", "folders.popupGap", "folders.cornerRadius", "folders.cornerSmoothing", "folders.autoRunning", "folders.runningName",
    };

    /// A few settings of each kind, written for these tests.
    private static readonly string Fixture = new JsonObject
    {
        ["groups"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "size",
                ["name"] = Names("크기", "Size"),
                ["about"] = Names("설명", "About"),
                ["items"] = new JsonArray
                {
                    Item("size.enabled", "bool", true, "크기 바꾸기", "Change size"),
                    Item("size.taskbarHeight", "int", 56, "작업표시줄 높이 (px)", "Taskbar height (px)", 6, 600),
                    Item("size.iconSize", "int", 32, "아이콘 크기 (px)", "Icon size (px)", 8, 128),
                    Item("size.iconSpacing", "int", 14, "아이콘 간격 (px)", "Icon spacing (px)", 0, 128),
                },
            },
            new JsonObject
            {
                ["id"] = "tray",
                ["name"] = Names("트레이", "Tray"),
                ["items"] = new JsonArray
                {
                    Item("tray.hideVolume", "bool", true, "볼륨 아이콘 숨기기", "Hide volume icon"),
                    Choice("tray.clock", "time", "시계", "Clock", ("timeDate", "시간과 날짜", "Time and date"), ("time", "시간만", "Time only"), ("hidden", "숨김", "Hidden")),
                },
            },
            new JsonObject
            {
                ["id"] = "folders",
                ["name"] = Names("폴더", "Folders"),
                ["items"] = new JsonArray
                {
                    Item("folders.enabled", "bool", true, "작업표시줄 폴더", "Taskbar folders"),
                    Item("folders.mergeDelay", "int", 250, "합치기 지연 (ms)", "Merge delay (ms)", 100, 3000),
                    Item("folders.autoRunning", "bool", true, "실행 중 폴더", "Running folder"),
                    Item("folders.runningName", "text", "실행 중", "실행 중 폴더 이름", "Running folder name"),
                },
            },
        },
    }.ToJsonString();

    private static JsonObject Names(string ko, string en) => new() { ["ko"] = ko, ["en"] = en };

    private static JsonObject Item(string key, string type, JsonNode def, string ko, string en, int? min = null, int? max = null)
    {
        var o = new JsonObject { ["key"] = key, ["type"] = type, ["default"] = def, ["name"] = Names(ko, en) };
        if (min is { } a) o["min"] = a;
        if (max is { } b) o["max"] = b;
        return o;
    }

    private static JsonObject Choice(string key, string def, string ko, string en, params (string value, string ko, string en)[] options) => new()
    {
        ["key"] = key,
        ["type"] = "choice",
        ["default"] = def,
        ["name"] = Names(ko, en),
        ["options"] = new JsonArray(options.Select(o => (JsonNode)new JsonObject { ["value"] = o.value, ["name"] = Names(o.ko, o.en) }).ToArray()),
    };

    private static string TempDir(string what)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bloom-{what}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void MapHasEveryKeyOnce()
    {
        var mapped = TaskbarMap.Entries.Select(e => e.Key).ToList();
        Check.Eq(AddonKeys.Length, 56, "the add-on's 56 keys");
        Check.Eq(mapped.Count, mapped.Distinct().Count(), "no key twice on the page");
        Check.True(mapped.ToHashSet().SetEquals(AddonKeys), "every key has its place, and none the add-on lacks");
        foreach (var (key, _, _, _) in TaskbarMap.Sizes) Check.True(key is "s" or "m" or "l", "three size presets");
        Check.Eq(TaskbarMap.Special("autohide.showDelay", -1), "Windows 기본", "-1: the Windows default, in words");
        Check.Eq(TaskbarMap.Special("autohide.showDelay", 400), (string?)null, "a set delay as its number");
        Check.Eq(TaskbarMap.Special("island.width", 0), "내용에 맞춤", "0: fits the content");
        Check.Eq(TaskbarMap.OptionLabel("pill.style", "glass"), "빛 받는 유리", "the page's word for an option");
        Check.Eq(TaskbarMap.OptionLabel("tray.clock", "time"), (string?)null, "else the schema's");
    }

    public static void SchemaParsed()
    {
        var s = AddonSchema.Parse(Fixture);
        Check.Eq(s.Groups.Count, 3, "groups");
        Check.Eq(s.Groups[0].About?.Ko, "설명", "a group's about");
        var h = Check.NotNull(s["size.taskbarHeight"], "by its key");
        Check.Eq((h.Min, h.Max, AddonSchema.Int(h.Default)), ((int?)6, (int?)600, (int?)56), "range and default");
        Check.Eq(h.Split(), ("작업표시줄 높이", "px"), "the unit leaves the name");
        Check.Eq(s["tray.clock"]!.Options.Count, 3, "options");
        Check.Eq(s.Keys.Count(), 10, "keys");
        static bool Refused(JsonObject item)
        {
            try
            {
                AddonSchema.Parse(new JsonObject { ["groups"] = new JsonArray { new JsonObject { ["id"] = "g", ["name"] = Names("g", "g"), ["items"] = new JsonArray { item } } } }.ToJsonString());
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }
        Check.True(Refused(Item("a.b", "int", 700, "x", "x", 0, 600)), "a default outside its range");
        Check.True(Refused(Item("a.b", "bool", 1, "x", "x")), "a switch's default not true or false");
        Check.True(Refused(Choice("a.b", "nope", "x", "x", ("one", "1", "1"))), "a choice's default not among its options");
        Check.True(Refused(Item("a.b", "colour", "x", "x", "x")), "an unknown kind");
        Check.True(Refused(new JsonObject { ["key"] = "a.b", ["type"] = "bool", ["default"] = true, ["name"] = new JsonObject { ["ko"] = "x" } }), "no English name");
        Check.True(!Refused(Item("a.b", "bool", true, "x", "x")), "a good one");
    }

    public static void ValuesKeepOnlyChanges()
    {
        string dir = TempDir("addon-values");
        try
        {
            string user = Path.Combine(dir, "user", "settings.json"), runtime = Path.Combine(dir, "values.json");
            var v = new AddonValues(user, runtime, AddonSchema.Parse(Fixture));
            Check.Eq(v.Int("size.iconSize"), 32, "the default before any change");
            v.Set("size.iconSize", 40);
            v.Set("size.enabled", true); // its default
            v.Set("folders.runningName", "앱");
            v.Flush();
            var written = (JsonObject)JsonNode.Parse(File.ReadAllText(runtime))!;
            Check.Eq(string.Join(",", written.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)), "folders.runningName,size.iconSize", "changed keys only");
            Check.Eq(File.ReadAllText(user), File.ReadAllText(runtime), "the user's file and the runtime's alike");
            v.Set("size.iconSize", 32);
            v.Flush();
            Check.True(JsonNode.Parse(File.ReadAllText(runtime))!["size.iconSize"] is null, "back at its default: dropped");
            File.WriteAllText(user, """{ "size.enabled": 0 }""");
            v.Reload();
            Check.True(!v.Bool("size.enabled"), "a 0 reads as off");
            Check.Eq(v.Default("folders.runningName"), "실행 중", "a text's default");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// The launcher's settings search finds the schema's lines by their names (collect mode), under their groups.
    public static void PageCollectsSchemaLines()
    {
        string shipped = TempDir("addon-schema");
        try
        {
            string dir = Path.Combine(shipped, AddonProtocol.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "PillTaskbarHost.exe"), "x");
            File.WriteAllText(Path.Combine(dir, "settings.schema.json"), Fixture);
            var manifest = new JsonObject
            {
                ["id"] = AddonProtocol.Id,
                ["version"] = "0.1.0",
                ["license"] = "GPL-3.0-only",
                ["source"] = "",
                ["minBloom"] = "0.1.0",
                ["host"] = new JsonObject { ["exe"] = "PillTaskbarHost.exe", ["protocol"] = AddonProtocol.Version },
                ["settings"] = "settings.schema.json",
                ["files"] = new JsonObject
                {
                    ["PillTaskbarHost.exe"] = AddonStore.Sha256(Path.Combine(dir, "PillTaskbarHost.exe")),
                    ["settings.schema.json"] = AddonStore.Sha256(Path.Combine(dir, "settings.schema.json")),
                },
            };
            File.WriteAllText(Path.Combine(dir, AddonManifest.FileName), manifest.ToJsonString());
            using (new SettingsTransferTests.DataFolder())
            {
                var addon = new Addon(AddonProtocol.Id, new AddonStore(shipped, Path.Combine(Paths.Local, "addons")));
                Check.NotNull(addon.Schema, "the schema its manifest names, hash checked");
                Settings.Current.AddonsOn.Add(addon.Id); // its settings show only while it is on
                var lines = new SettingsView(new Host(new Options())).CollectAddonsForTests(addon);
                Settings.Current.AddonsOn.Remove(addon.Id);
                Check.True(lines.Any(l => l.Title == "작업표시줄 높이" && l.Group == "작업표시줄 모양"), "작업표시줄 높이 under 작업표시줄 모양, folded or not");
                Check.True(lines.Any(l => l.Group == "작업표시줄 폴더" && l.Title == "작업표시줄 폴더"), "작업표시줄 폴더");
                Check.True(lines.Any(l => l.Title == "폴더 편집"), "the folder editor by a line of its own");
                Check.True(lines.Any(l => l.Title == "보일 아이콘" && l.Sub.Contains("볼륨", StringComparison.Ordinal)), "a tray chip by its name");
                Check.True(lines.All(l => l.PageKey == "addons"), "nothing else");
                File.WriteAllText(Path.Combine(dir, "settings.schema.json"), Fixture + " ");
                Check.True(new Addon(AddonProtocol.Id, new AddonStore(shipped, Path.Combine(Paths.Local, "addons"))).Schema is null, "a changed schema isn't read");
            }
        }
        finally
        {
            Directory.Delete(shipped, true);
        }
    }

    public static void ManifestSettingsAndSource()
    {
        var files = new JsonObject { ["Host.exe"] = new string('a', 64), ["settings.schema.json"] = new string('b', 64) };
        JsonObject M(string? settings) => new()
        {
            ["id"] = "pill-taskbar",
            ["version"] = "0.1.0",
            ["license"] = "GPL-3.0-only",
            ["source"] = "",
            ["minBloom"] = "0.1.0",
            ["host"] = new JsonObject { ["exe"] = "Host.exe", ["protocol"] = AddonProtocol.Version },
            ["settings"] = settings,
            ["files"] = files.DeepClone(),
        };
        var m = AddonManifest.Parse(M("settings.schema.json").ToJsonString());
        Check.Eq(m.Settings, "settings.schema.json", "the schema's file");
        Check.Eq(m.Source, "", "no source address yet");
        bool threw = false;
        try
        {
            AddonManifest.Parse(M("other.json").ToJsonString());
        }
        catch (InvalidDataException)
        {
            threw = true;
        }
        Check.True(threw, "a schema not among the hashed files");
    }

    public static void NamesFromTheSchema()
    {
        var s = AddonSchema.Parse(Fixture);
        Check.Eq(SettingsView.ChipName(s["tray.hideVolume"]!), "볼륨", "a tray chip");
        Lang.Force(true);
        Check.Eq(SettingsView.ChipName(s["tray.hideVolume"]!), "Volume", "a tray chip in English");
        Lang.Force(false);
        Check.Eq(SettingsView.OptionName("Bloom 버튼 (누르면 Bloom을 열고 닫습니다)"), ("Bloom 버튼", (string?)"누르면 Bloom을 열고 닫습니다"), "an option's name and detail");
        Check.Eq(SettingsView.OptionName("시간만"), ("시간만", (string?)null), "no detail");
        Check.Eq(TaskbarEditor.ErrorText("이름 바꾸기", "exists"), "이름 바꾸기 실패 · 같은 이름의 폴더가 이미 있습니다", "the mod's error said");
        Check.True(TaskbarEditor.ErrorText("x", CommandResult.Timeout).Contains("5초", StringComparison.Ordinal), "no answer");
    }

    public static void StateRead()
    {
        string dir = TempDir("taskbar-state");
        try
        {
            File.WriteAllText(Path.Combine(dir, AddonProtocol.StateFile), """
                { "v": 1, "pid": 42, "writtenAt": "2026-01-01T00:00:00Z",
                  "folders": { "enabled": true, "path": "C:\\F" },
                  "pinned": [ { "kind": "app", "name": "A", "path": "C:\\a.lnk" }, { "kind": "folder", "name": "Two" }, { "kind": "running" }, { "kind": "folder", "name": "One" } ],
                  "items": {
                    "One": { "icon": { "kind": "file", "path": "C:\\i.ico", "index": 2 }, "apps": [ { "file": "x.lnk", "name": "X", "path": "C:\\F\\One\\x.lnk" } ] },
                    "Two": { "icon": { "kind": "auto" }, "apps": [ { "file": "y.lnk", "name": "Y", "appId": "Y!App" } ] },
                    "Three": { "icon": { "kind": "auto" }, "apps": [] } },
                  "running": { "enabled": true, "name": "실행 중", "apps": [ { "name": "Z", "appId": "Z!App" } ] } }
                """);
            var s = Check.NotNull(TaskbarState.Read(dir), "read");
            Check.Eq(s.Pid, 42, "pid");
            Check.Eq(string.Join(",", s.FolderNames), "Two,One,Three", "pinned order, then the rest");
            Check.Eq((s.Items["One"].Icon!.Kind, s.Items["One"].Icon!.Index), ("file", 2), "an icon from a file");
            Check.Eq(s.Items["Two"].Apps[0].IconPath, @"shell:AppsFolder\Y!App", "an app by its id");
            Check.Eq(s.Running!.Apps.Count, 1, "the running folder");
            Check.True(TaskbarState.Read(Path.Combine(dir, "none")) is null, "no file: null");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// A command goes to inbox whole, in the protocol's shape, and the mod's answer comes back from outbox.
    public static void CommandAnswered()
    {
        string dir = TempDir("taskbar-commands");
        try
        {
            JsonObject? seen = null;
            var mod = new Thread(() =>
            {
                string inbox = Path.Combine(dir, AddonProtocol.InboxDir);
                for (int i = 0; i < 200 && seen is null; i++, Thread.Sleep(10))
                    foreach (string f in Directory.Exists(inbox) ? Directory.GetFiles(inbox, "*.json") : Array.Empty<string>())
                    {
                        seen = (JsonObject)JsonNode.Parse(File.ReadAllText(f))!;
                        File.Delete(f);
                        Directory.CreateDirectory(Path.Combine(dir, AddonProtocol.OutboxDir));
                        File.WriteAllText(Path.Combine(dir, AddonProtocol.OutboxDir, Path.GetFileName(f)), new JsonObject { ["id"] = seen["id"]!.GetValue<string>(), ["ok"] = false, ["error"] = "exists", ["at"] = "x" }.ToJsonString());
                    }
            });
            mod.Start();
            var c = new TaskbarCommands(dir, TimeSpan.FromSeconds(3));
            var r = c.Send(TaskbarCommands.Create("New", new[] { @"C:\a.lnk", @"C:\b.lnk" })).Result;
            mod.Join();
            Check.Eq(r, new CommandResult(false, "exists"), "the mod's answer");
            var cmd = Check.NotNull(seen, "the mod got it");
            Check.Eq(cmd["cmd"]!.GetValue<string>(), "create", "cmd");
            Check.True(cmd["id"]!.GetValue<string>().Length > 0, "an id");
            Check.Eq(cmd["args"]!["name"]!.GetValue<string>(), "New", "args.name");
            Check.Eq(cmd["args"]!["apps"]!.AsArray().Count, 2, "args.apps");
            Check.True(!c.TimedOut, "answered");
            Check.True(Directory.GetFiles(Path.Combine(dir, AddonProtocol.OutboxDir)).Length == 0, "the answer is taken");
            Check.Eq(TaskbarCommands.Order("F", new[] { "a", "b" }).Item2["names"]!.AsArray().Count, 2, "order: names");
            Check.Eq(TaskbarCommands.IconFile("F", @"C:\i.ico", 3).Item2["index"]!.GetValue<int>(), 3, "icon: index");
            Check.Eq(TaskbarCommands.TakeOut("F", "x.lnk").Item1, "takeOut", "takeOut");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// No answer within the wait: the command is taken back and the editor goes read only until the mod writes again.
    public static void CommandTimesOut()
    {
        string dir = TempDir("taskbar-timeout");
        try
        {
            Check.Eq(TaskbarCommands.AnswerWait, TimeSpan.FromSeconds(5), "5 s in the page");
            var c = new TaskbarCommands(dir, TimeSpan.FromMilliseconds(200));
            var r = c.Send(TaskbarCommands.Dissolve("F")).Result;
            Check.Eq(r, new CommandResult(false, CommandResult.Timeout), "timed out");
            Check.True(c.TimedOut, "read only");
            Check.True(Directory.GetFiles(Path.Combine(dir, AddonProtocol.InboxDir)).Length == 0, "taken back from inbox");
            c.Heard(DateTime.UtcNow.AddMinutes(-1));
            Check.True(c.TimedOut, "a state.json from before doesn't count");
            c.Heard(DateTime.UtcNow.AddSeconds(1));
            Check.True(!c.TimedOut, "the mod wrote again");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void IcoHeader()
    {
        var pngs = new List<byte[]> { new byte[] { 1, 2, 3 }, new byte[] { 4, 5 } };
        byte[] ico = IconFile.Ico(pngs, new[] { 32, 256 });
        Check.Eq((ico[0], ico[1], ico[2], ico[3], ico[4], ico[5]), ((byte)0, (byte)0, (byte)1, (byte)0, (byte)2, (byte)0), "ICONDIR: an icon of two images");
        Check.Eq(ico[6], (byte)32, "the first image's side");
        Check.Eq(ico[6 + 16], (byte)0, "256 is written as 0");
        Check.Eq(BitConverter.ToInt32(ico, 6 + 12), 6 + 32, "the first image after the entries");
        Check.Eq(BitConverter.ToInt32(ico, 6 + 16 + 12), 6 + 32 + 3, "the second after the first");
        Check.Eq(ico.Length, 6 + 32 + 5, "the images at the end");
    }
}
