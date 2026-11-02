using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

/// The home's 명령 block (Core\HomeCommands.cs): its default list, a command taken off and put back, one left off by
/// default put on, the picks through the settings file, and the kept home's key (Ui\Launcher\Launcher.Kept.cs).
internal static class HomeCommandsTests
{
    private static string Ids(List<SysEntry> list) => string.Join(" ", list.Select(e => e.Id));

    /// `check` over the settings file `json`'s picks, every feature on; the picks and features as they were after.
    private static void From(string json, Action check)
    {
        var s = Settings.Current;
        var features = s.FeaturesOff.ToList();
        s.FeaturesOff.Clear();
        s.HomeCommands = JsonSerializer.Deserialize<Settings>(json, Json.Options)!.HomeCommands;
        try
        {
            check();
        }
        finally
        {
            s.HomeCommands = null;
            s.FeaturesOff.Clear();
            s.FeaturesOff.AddRange(features);
        }
    }

    public static void DefaultsAreTodaysList()
    {
        From("{}", () =>
        {
            string all = Ids(SystemItems.Commands.Where(e => e.Id is not ("cmd:reindex" or "cmd:lock" or "cmd:restart" or "cmd:shutdown" or "cmd:quit")).ToList());
            Check.Eq(Ids(HomeCommands.Shown()), all, "every command but refresh, lock, restart, shut down and quit");
            Check.Eq(Ids(HomeCommands.Missing()), "cmd:reindex cmd:lock cmd:restart cmd:shutdown cmd:quit", "those five offered");
            Check.True(Settings.Current.HomeCommands is null, "nothing kept");
        });
    }

    public static void RemoveAndAddBack()
    {
        From("{}", () =>
        {
            string was = Ids(HomeCommands.Shown());
            HomeCommands.Set("cmd:color", false);
            Check.True(!HomeCommands.Shown().Exists(e => e.Id == "cmd:color"), "taken off the block");
            Check.Eq(HomeCommands.Missing()[0].Id, "cmd:color", "offered, in Bloom's order");
            Check.Eq(Settings.Current.HomeCommands!["cmd:color"], false, "kept as a pick");
            HomeCommands.Set("cmd:color", true);
            Check.Eq(Ids(HomeCommands.Shown()), was, "back where Bloom lists it");
            Check.True(Settings.Current.HomeCommands is null, "a pick that is its default goes");
            HomeCommands.Set("cmd:lock", true);
            Check.True(HomeCommands.Shown().Exists(e => e.Id == "cmd:lock") && !HomeCommands.Missing().Exists(e => e.Id == "cmd:lock"), "one off by default put on");
            Check.Eq(Settings.Current.HomeCommands!["cmd:lock"], true, "kept as a pick");
            HomeCommands.Set("cmd:nope", false);
            Check.True(!Settings.Current.HomeCommands.ContainsKey("cmd:nope"), "an unknown command is not kept");
        });
    }

    public static void PicksThroughTheSettingsFile()
    {
        From("{}", () =>
        {
            HomeCommands.Set("cmd:emoji", false);
            HomeCommands.Set("cmd:quit", true);
            string json = JsonSerializer.Serialize(Settings.Current, Json.Options);
            Check.True(json.Contains("\"homeCommands\"", StringComparison.Ordinal), "written");
            var back = JsonSerializer.Deserialize<Settings>(json, Json.Options)!.HomeCommands!;
            Check.Eq(string.Join(" ", back.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")), "cmd:emoji=False cmd:quit=True", "read back");
        });
        From("{}", () => Check.True(!JsonSerializer.Serialize(Settings.Current, Json.Options).Contains("homeCommands", StringComparison.Ordinal), "untouched: not written"));
    }

    public static void UnknownAndDefaultPicksDropped()
    {
        From("""{"homeCommands":{"cmd:nope":false,"cmd:ai":false,"cmd:files":true,"cmd:lock":false}}""", () =>
        {
            Check.True(!HomeCommands.Shown().Exists(e => e.Id == "cmd:ai"), "a kept pick holds");
            Check.Eq(string.Join(" ", Settings.Current.HomeCommands!.Keys), "cmd:ai", "a command Bloom lacks and picks that are defaults go");
        });
        From("""{"homeCommands":{"cmd:nope":true}}""", () =>
        {
            Check.Eq(HomeCommands.Shown().Count, SystemItems.Commands.Count - HomeCommands.Off.Count, "the default list");
            Check.True(Settings.Current.HomeCommands is null, "nothing left: as if never edited");
        });
    }

    public static void FeatureOffStaysHidden()
    {
        From("""{"homeCommands":{"cmd:lens":true,"cmd:color":false}}""", () =>
        {
            Features.Set("lens", false);
            Features.Set("color", false);
            Check.True(!HomeCommands.Shown().Exists(e => e.Id is "cmd:lens" or "cmd:pin" or "cmd:capture"), "not shown while its feature is off");
            Check.True(!HomeCommands.Missing().Exists(e => e.Id is "cmd:lens" or "cmd:color"), "nor offered");
            Features.Set("color", true);
            Check.True(HomeCommands.Missing().Exists(e => e.Id == "cmd:color"), "its feature on again: offered as picked");
        });
    }

    public static void KeptKeyFollowsTheList()
    {
        static Section Block(bool removable, params string[] ids)
        {
            var s = new Section("명령") { HomeKey = "commands", HomeRight = true };
            foreach (string id in ids) s.Items.Add(new ResultItem { Id = id, Title = id, Remove = removable ? () => { } : null });
            return s;
        }
        string Key(Section s) => Launcher.SectionKey(s, out _);
        Check.True(Key(Block(false, "cmd:ai", "cmd:files")) != Key(Block(false, "cmd:ai")), "a command taken off changes the key");
        Check.True(Key(Block(false, "cmd:ai")) != Key(Block(true, "cmd:ai")), "the rows' ✕ changes the key");
        Check.Eq(Key(Block(true, "cmd:ai")), Key(Block(true, "cmd:ai")), "the same list, the same key");
    }
}
