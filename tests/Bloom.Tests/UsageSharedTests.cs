using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Bloom.Addons;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// The usage settings the home cards and the taskbar share: each account's logo (Bloom's own copy), the taskbar's bar per
/// account and the 이름표 in the usage file, and 아이콘 색 as one setting that also reaches the add-on's values.
internal static class UsageSharedTests
{
    private static readonly DateTime Fetched = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Local);

    private static List<UsageAccount> Cards() => new()
    {
        new("Claude", "Max", new List<UsageLimit> { new("5시간", 32, null, UsageLimit.SessionMins), new("주간", 47, null, UsageLimit.WeekMins), new("Fable 주간", 8, null, UsageLimit.WeekMins, "Fable") }, Fetched) { Id = "claude" },
        new("ChatGPT", "Pro 200", new List<UsageLimit> { new("주간", 21.5, null, UsageLimit.WeekMins) }, Fetched) { Id = "chatgpt" },
    };

    public static void ExportCarriesTagBarAndIcon()
    {
        using var folder = new SettingsTransferTests.DataFolder();
        var list = Settings.Current.AiAccounts;
        list.Clear();
        var claude = new AiAccount { Id = "claude", Kind = "claude", Bar = "week-fable" };
        var gpt = new AiAccount { Id = "chatgpt", Kind = "chatgpt", Bar = "session" };
        list.Add(claude);
        list.Add(gpt);

        var root = JsonNode.Parse(UsageExport.Build(Cards(), true, 1))!.AsObject();
        Check.True(root["tag"] is null, "이름표 none: left out");
        Check.Eq((string)root["accounts"]![0]!["bar"]!, "week-fable", "the picked limit, by its key");
        Check.True(root["accounts"]![1]!["bar"] is null, "a pick the account doesn't have (no session): left out");
        Check.True(root["accounts"]![0]!["icon"] is null, "no logo: left out");

        string svg = Path.Combine(folder.Directory, "logo.svg");
        File.WriteAllText(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="#d97757"/></svg>""");
        UsageIcons.Pick(claude, svg);
        string copy = Check.NotNull(claude.Icon, "picked");
        Check.True(copy.StartsWith(Path.Combine(Paths.Local, "usage-icons"), StringComparison.OrdinalIgnoreCase) && File.Exists(copy), "Bloom's own copy under Local");
        File.Delete(svg);
        Settings.Current.UsageTag = "plan";
        root = JsonNode.Parse(UsageExport.Build(Cards(), true, 1))!.AsObject();
        Check.Eq((string)root["tag"]!, "plan", "이름표");
        Check.Eq((string)root["accounts"]![0]!["icon"]!, copy, "the copy, though the original moved away");
        Check.NotNull(UsageLogo.Svg(File.ReadAllText(copy)), "the card can draw it");

        UsageIcons.Clear(claude);
        Check.True(claude.Icon is null && !File.Exists(copy), "cleared: the copy goes too");
        Check.True(JsonNode.Parse(UsageExport.Build(Cards(), true, 1))!["accounts"]![0]!["icon"] is null, "and the file has none");
        bool refused = false;
        try
        {
            string txt = Path.Combine(folder.Directory, "logo.txt");
            File.WriteAllText(txt, "x");
            UsageIcons.Pick(claude, txt);
        }
        catch (IOException)
        {
            refused = true;
        }
        Check.True(refused && claude.Icon is null, "only .svg, .png and .ico");
        Settings.Current.UsageTag = "none";
    }

    public static void BarsNamedByKey()
    {
        var bars = UsageExport.Bars(Cards()[0]);
        Check.Eq(string.Join(",", bars.ConvertAll(b => b.key)), "session,week-fable,week", "the file's keys, in its order");
        Check.Eq(bars[0].name, "5시간", "the session");
        Check.Eq(bars[1].name, "Fable 주간", "a model's week");
        Check.Eq(UsageExport.Bars(Cards()[1]).Count, 1, "one limit: nothing to pick");
    }

    /// 아이콘 색 is one setting: the home cards read it, and the taskbar's usage.logoColor follows it.
    public static void LogoColourIsOneSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-usage-logo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string before = Settings.Current.UsageLogo;
        try
        {
            var schema = AddonSchema.Parse("""
                { "groups": [ { "id": "usage", "name": { "ko": "사용량", "en": "Usage" }, "items": [
                  { "key": "usage.logoColor", "type": "choice", "default": "brand", "name": { "ko": "로고 색", "en": "Logo color" },
                    "options": [ { "value": "mono", "name": { "ko": "단색", "en": "Mono" } }, { "value": "brand", "name": { "ko": "원래 색", "en": "Brand" } } ] } ] } ] }
                """);
            var values = new AddonValues(Path.Combine(dir, "settings.json"), Path.Combine(dir, "values.json"), schema);
            SettingsView.SetUsageLogo("mono", values);
            values.Flush();
            Check.Eq(Settings.Current.UsageLogo, "mono", "the home cards' setting");
            Check.Eq((string)JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "values.json")))!["usage.logoColor"]!, "mono", "the taskbar's value");
            SettingsView.SetUsageLogo("brand", values);
            values.Flush();
            Check.True(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "values.json")))!["usage.logoColor"] is null, "back at the add-on's default: dropped");
            SettingsView.SetUsageLogo("mono", null);
            Check.Eq(Settings.Current.UsageLogo, "mono", "without the add-on, the home's alone");
        }
        finally
        {
            Settings.Current.UsageLogo = before;
            Directory.Delete(dir, true);
        }
    }
}
