using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// Add-ons the user puts in <Paths.Local>\user-addons themselves: found by folder, kept apart from the shipped one,
/// named from their addon.json and listed on 설정 > 애드온 with a switch each.
internal static class UserAddonTests
{
    private static JsonObject Manifest(string id, JsonNode? name = null)
    {
        var m = new JsonObject
        {
            ["id"] = id,
            ["version"] = "1.0.0",
            ["license"] = "MIT",
            ["source"] = "",
            ["minBloom"] = "0.1.0",
            ["host"] = new JsonObject { ["exe"] = "Host.exe", ["protocol"] = AddonProtocol.Version },
            ["files"] = new JsonObject { ["Host.exe"] = new string('a', 64) },
        };
        if (name is not null) m["name"] = name;
        return m;
    }

    private static void Put(string root, string folder, JsonObject? manifest)
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        if (manifest is not null) File.WriteAllText(Path.Combine(dir, AddonManifest.FileName), manifest.ToJsonString());
    }

    public static void NameByLanguage()
    {
        var m = AddonManifest.Parse(Manifest("my-addon", new JsonObject { ["ko"] = "내 애드온", ["en"] = "My add-on" }).ToJsonString());
        Check.Eq(m.Title("ko"), "내 애드온", "Korean");
        Check.Eq(m.Title("ja"), "My add-on", "a language it lacks: English");
        var bare = AddonManifest.Parse(Manifest("my-addon").ToJsonString());
        Check.Eq(bare.Title("ko"), "my-addon", "no name: the id");
        var odd = AddonManifest.Parse(Manifest("my-addon", new JsonObject { ["ko"] = 5, ["en"] = "  " }).ToJsonString());
        Check.Eq(odd.Title("ko"), "my-addon", "a name that isn't text is left out, not refused");
        var notObject = AddonManifest.Parse(Manifest("my-addon", "My add-on").ToJsonString());
        Check.Eq(notObject.Title("en"), "my-addon", "a plain string name is left out");
    }

    public static void FolderListsOnlyAddons()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-user-addons-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AddonStore(root, Path.Combine(root, "local"));
            Check.Eq(store.Ids().Count, 0, "no folder yet");
            Put(root, "b-addon", Manifest("b-addon"));
            Put(root, "a-addon", Manifest("a-addon"));
            Put(root, "Bad_Name", Manifest("bad-name"));
            Put(root, "empty", null);
            File.WriteAllText(Path.Combine(root, "note.txt"), "x");
            Check.Eq(string.Join(",", store.Ids()), "a-addon,b-addon", "folders with an addon.json and an id's name, by name");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    public static void ShippedIdsWin()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-user-addons-" + Guid.NewGuid().ToString("N"));
        try
        {
            string shipped = Path.Combine(root, "shipped"), user = Path.Combine(root, "user"), local = Path.Combine(root, "local");
            Directory.CreateDirectory(shipped);
            Put(user, AddonProtocol.Id, Manifest(AddonProtocol.Id));
            Put(user, "my-addon", Manifest("my-addon", new JsonObject { ["en"] = "Mine" }));
            Put(user, "wrong-id", Manifest("other-id"));
            var list = AddonHub.Discover(new AddonStore(shipped, local), new AddonStore(user, local));
            Check.Eq(string.Join(",", list.Select(a => a.Id)), $"{AddonProtocol.Id},my-addon,wrong-id", "the shipped one first, then the user's in id order");
            Check.True(!list[0].User && list[0].Manifest is null, "the shipped one stays the shipped one (absent here)");
            Check.True(list[1].User && list[1].Manifest?.Title("en") == "Mine" && !list[1].Broken, "the user's own, read from its folder");
            Check.True(list[2].User && list[2].Broken, "an addon.json naming another id is broken");
            Check.Eq(list[1].DataDir, Path.Combine(local, "my-addon"), "data beside the shipped ones");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    public static void PageLinesPerAddon()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-user-addons-" + Guid.NewGuid().ToString("N"));
        try
        {
            Put(root, "my-addon", Manifest("my-addon", new JsonObject { ["ko"] = "내 애드온", ["en"] = "My add-on" }));
            using (new SettingsTransferTests.DataFolder())
            {
                var addon = new Addon("my-addon", new AddonStore(root, Path.Combine(Paths.Local, "addons")), user: true);
                var lines = new SettingsView(new Host(new Options())).CollectAddonsForTests(new[] { addon });
                Check.True(lines.Count == 1 && lines[0].Title == "내 애드온" && lines[0].PageKey == "addons", "one line, by its name, on its page");
                var none = new SettingsView(new Host(new Options())).CollectAddonsForTests(Array.Empty<Addon>());
                Check.Eq(none.Count, 0, "no page without one");
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
