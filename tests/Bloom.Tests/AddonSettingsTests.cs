using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// An add-on other than the taskbar one: its page drawn from its settings.schema.json, and the settings its schema puts
/// in a file of its own (prefs.json, which its window writes too) read from and written to that file.
internal static class AddonSettingsTests
{
    private const string Schema = """
        { "schema": 1, "groups": [
          { "id": "widget", "name": { "ko": "위젯", "en": "Widget" }, "items": [
            { "key": "widget.icon", "type": "choice", "file": "prefs.json", "field": "icon", "default": 1,
              "options": [ { "value": 0, "name": { "ko": "선", "en": "Line" } }, { "value": 1, "name": { "ko": "채움", "en": "Fill" } } ],
              "name": { "ko": "작업표시줄 아이콘", "en": "Taskbar icon" } },
            { "key": "widget.refreshSeconds", "type": "int", "default": 2, "min": 1, "max": 10, "name": { "ko": "새로 고침 간격(초)", "en": "Refresh interval (s)" } },
            { "key": "auto.percent", "type": "int", "file": "prefs.json", "field": "autoPercent", "default": 90, "min": 50, "max": 99,
              "name": { "ko": "기준 사용률 (%)", "en": "Memory use limit (%)" } } ] },
          { "id": "clean", "name": { "ko": "정리할 곳", "en": "What to clean" }, "items": [
            { "key": "clean.areas", "type": "flags", "file": "prefs.json", "field": "areas", "default": 3, "min": 1,
              "options": [ { "value": 1, "name": { "ko": "작업 집합", "en": "Working sets" } }, { "value": 2, "name": { "ko": "캐시", "en": "Cache" } } ],
              "name": { "ko": "정리할 영역", "en": "Areas to clean" } },
            { "key": "exclude.programs", "type": "list", "file": "prefs.json", "field": "exclude", "default": ["bloom.exe"], "max": 200, "suffix": ".exe",
              "name": { "ko": "예외 프로그램", "en": "Programs left alone" } } ] },
          { "id": "keys", "name": { "ko": "단축키", "en": "Hotkeys" }, "items": [
            { "key": "keys.up", "type": "hotkey", "file": "prefs.json", "field": "keyUp", "default": { "mods": 3, "vk": 38 }, "name": { "ko": "밝게", "en": "Brighter" } },
            { "key": "ranges.monitors", "type": "records", "file": "prefs.json", "field": "monitors", "default": {}, "label": "name",
              "fields": [ { "key": "lo", "type": "int", "default": 0, "min": 0, "max": 99, "name": { "ko": "최저 밝기 (%)", "en": "Lowest (%)" } },
                          { "key": "hidden", "type": "bool", "default": false, "name": { "ko": "창에서 숨기기", "en": "Hidden in the window" } } ],
              "name": { "ko": "모니터별 범위", "en": "Ranges per monitor" } } ] },
          { "id": "pill", "name": { "ko": "모양", "en": "Look" }, "items": [
            { "key": "pill.glass", "type": "bool", "default": true, "name": { "ko": "유리 효과", "en": "Glass effect" } } ] } ] }
        """;

    private static string Temp() => Path.Combine(Path.GetTempPath(), "bloom-addon-settings-" + Guid.NewGuid().ToString("N"));

    public static void SchemaKinds()
    {
        var s = AddonSchema.Parse(Schema);
        Check.True(s["widget.icon"] is { File: "prefs.json", Field: "icon", Numeric: true } icon && icon.Options[1].Value == "1", "a numbered choice in prefs.json");
        Check.True(s["widget.refreshSeconds"] is { File: null }, "a values.json one");
        Check.True(s["clean.areas"] is { Type: "flags", Min: 1 } f && f.Options.Select(o => o.Value).SequenceEqual(["1", "2"]), "bits");
        Check.Eq(s["exclude.programs"]!.Suffix, ".exe", "a list's suffix");
        Check.True(s["ranges.monitors"] is { Label: "name" } r && r.Fields.Select(x => x.Key).SequenceEqual(["lo", "hidden"]), "a record's fields");
        Check.Eq(s["auto.percent"]!.Split(), ("기준 사용률", "%"), "a unit in brackets");
        Check.Eq(s["widget.refreshSeconds"]!.Split(), ("새로 고침 간격", "초"), "a Korean unit right after the name");

        static void Bad(string item, string why)
        {
            string json = $$"""{ "groups": [ { "id": "g", "name": { "en": "G" }, "items": [ {{item}} ] } ] }""";
            try
            {
                AddonSchema.Parse(json);
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException(why + ": accepted");
        }
        Bad("""{ "key": "a.b", "type": "list", "default": [], "name": { "en": "A" } }""", "a list kept in values.json");
        Bad("""{ "key": "a.b", "type": "bool", "file": "values.json", "field": "x", "default": true, "name": { "en": "A" } }""", "values.json as the file");
        Bad("""{ "key": "a.b", "type": "bool", "file": "..\\x.json", "field": "x", "default": true, "name": { "en": "A" } }""", "a path as the file");
        Bad("""{ "key": "a.b", "type": "bool", "file": "prefs.json", "default": true, "name": { "en": "A" } }""", "a file without a field");
        Bad("""{ "key": "a.b", "type": "choice", "default": 0, "options": [ { "value": 0, "name": { "en": "Z" } } ], "name": { "en": "A" } }""", "numbered options in values.json");
        Bad("""{ "key": "a.b", "type": "records", "file": "p.json", "field": "m", "default": {}, "fields": [ { "key": "c", "type": "text", "default": "", "name": { "en": "C" } } ], "name": { "en": "A" } }""",
            "a record's text field");
        Bad("""{ "key": "a.b", "type": "flags", "file": "p.json", "field": "m", "default": 1, "options": [ { "value": 0, "name": { "en": "Z" } } ], "name": { "en": "A" } }""", "a bit of 0");
    }

    public static void PrefsFile()
    {
        string dir = Temp();
        try
        {
            Directory.CreateDirectory(dir);
            string prefs = Path.Combine(dir, "prefs.json"), runtime = Path.Combine(dir, "values.json");
            File.WriteAllText(prefs, """{"v":2,"autoPercent":80,"lastFreed":5,"monitors":{"\\\\?\\DISPLAY#A":{"lo":10,"hi":90,"name":"Left"}}}""");
            var v = new AddonValues(Path.Combine(dir, "user", "settings.json"), runtime, AddonSchema.Parse(Schema));
            Check.Eq(v.Int("auto.percent"), 80, "read from prefs.json");
            Check.Eq(v.Text("widget.icon"), "1", "a numbered choice's default as text");
            Check.True(v.List("exclude.programs").SequenceEqual(["bloom.exe"]), "a list's default");
            Check.Eq(v.Key("keys.up"), (3u, 38u), "a hotkey's default");

            v.Set("auto.percent", 70);
            v.Set("widget.icon", "0");
            v.Set("clean.areas", 2);
            v.SetList("exclude.programs", ["bloom.exe", "game.exe"]);
            v.SetKey("keys.up", 6, 0x70);
            var (id, name) = v.Records("ranges.monitors").Single();
            Check.Eq(name, "Left", "a record by its label");
            v.SetRecord("ranges.monitors", id, "lo", JsonValue.Create(25));
            v.Set("widget.refreshSeconds", 5);
            Check.Eq(v.Int("auto.percent"), 70, "the change before it is written");
            v.Flush();

            var o = (JsonObject)JsonNode.Parse(File.ReadAllText(prefs))!;
            Check.Eq(o["autoPercent"]!.GetValue<int>(), 70, "written");
            Check.Eq(o["icon"]!.GetValue<int>(), 0, "a numbered choice written as a number");
            Check.Eq(o["areas"]!.GetValue<int>(), 2, "bits");
            Check.Eq(o["exclude"]!.ToJsonString(), """["bloom.exe","game.exe"]""", "a list");
            Check.Eq(o["keyUp"]!.ToJsonString(), """{"mods":6,"vk":112}""", "a hotkey");
            var mon = o["monitors"]![id]!;
            Check.True(mon["lo"]!.GetValue<int>() == 25 && mon["hi"]!.GetValue<int>() == 90 && mon["name"]!.GetValue<string>() == "Left", "one field of a record, the others kept");
            Check.True(o["v"]!.GetValue<int>() == 2 && o["lastFreed"]!.GetValue<int>() == 5, "the add-on's other fields kept");
            var values = (JsonObject)JsonNode.Parse(File.ReadAllText(runtime))!;
            Check.True(values.ContainsKey("widget.refreshSeconds") && !values.ContainsKey("auto.percent"), "values.json gets only its own");

            // the add-on's window writes the file: read again; a later change of Bloom's keeps what it wrote
            File.WriteAllText(prefs, """{"v":2,"autoPercent":95,"areas":1,"lastFreed":6}""");
            Check.Eq(v.Int("auto.percent"), 95, "the add-on's change seen");
            v.Set("clean.areas", 3);
            v.Flush();
            o = (JsonObject)JsonNode.Parse(File.ReadAllText(prefs))!;
            Check.True(o["autoPercent"]!.GetValue<int>() == 95 && o["lastFreed"]!.GetValue<int>() == 6 && o["areas"]!.GetValue<int>() == 3, "merged into the add-on's file");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void ListEntries()
    {
        Check.Eq(SettingsView.ListEntry(" Chrome.EXE ", ".exe"), "chrome.exe", "lower-cased");
        Check.Eq(SettingsView.ListEntry("chrome", ".exe"), "", "without the suffix");
        Check.Eq(SettingsView.ListEntry(".exe", ".exe"), "", "the suffix alone");
        Check.Eq(SettingsView.ListEntry("C:\\x\\a.exe", ".exe"), "", "a path");
        Check.Eq(Hotkeys.Combo(3, 38), "ctrl+alt+up", "a hotkey as Bloom writes it");
        Check.Eq(Hotkeys.Parse(Hotkeys.Combo(6, 0x70)), ((uint, uint)?)(6u, 0x70u), "and back");
        Check.Eq(Hotkeys.Combo(3, 0), "off", "none");
    }

    /// The page: a line for each setting, under its group, and none for the look Bloom keeps to its own.
    public static void PageLines()
    {
        string root = Temp();
        try
        {
            string dir = Path.Combine(root, "my-widget");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "settings.schema.json");
            File.WriteAllText(file, Schema);
            var m = new JsonObject
            {
                ["id"] = "my-widget",
                ["version"] = "1.0.0",
                ["license"] = "MIT",
                ["source"] = "",
                ["minBloom"] = "0.1.0",
                ["host"] = new JsonObject { ["exe"] = "Host.exe", ["protocol"] = AddonProtocol.Version },
                ["files"] = new JsonObject { ["Host.exe"] = new string('a', 64), ["settings.schema.json"] = AddonStore.Sha256(file) },
                ["settings"] = "settings.schema.json",
            };
            File.WriteAllText(Path.Combine(dir, AddonManifest.FileName), m.ToJsonString());
            using (new SettingsTransferTests.DataFolder())
            {
                var addon = new Addon("my-widget", new AddonStore(root, Path.Combine(Paths.Local, "addons")), user: true);
                Directory.CreateDirectory(addon.DataDir);
                File.WriteAllText(Path.Combine(addon.DataDir, "prefs.json"), """{"monitors":{"m1":{"name":"Left"}}}""");
                var lines = new SettingsView(new Host(new Options())).CollectAddonsForTests(users: new[] { addon })
                    .Where(l => l.PageKey == SettingsView.AddonPageKey("my-widget")).ToList();
                foreach (var (title, group) in new[] { ("작업표시줄 아이콘", "위젯"), ("기준 사용률", "위젯"), ("정리할 영역", "정리할 곳"), ("bloom.exe", "정리할 곳"),
                             ("예외 프로그램 추가", "정리할 곳"), ("밝게", "단축키"), ("최저 밝기", "Left"), ("창에서 숨기기", "Left") })
                    Check.True(lines.Any(l => l.Title == title && l.Group == group), $"{title} under {group}");
                Check.True(lines.All(l => l.Title != "유리 효과"), "the look left to Bloom");
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
