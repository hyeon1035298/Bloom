using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// Ctrl+K › 검색에서 숨기기: the store, the root list passing over hidden rows, the sections below it, and the settings line.
internal static class HiddenResultsTests
{
    /// Runs `body` in a data folder of its own with an empty store, then puts the store back.
    private static void Isolated(Action body)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var saverField = typeof(HiddenResults).GetField("saver", flags)!;
        var priorSaver = saverField.GetValue(null);
        var priorCurrent = HiddenResults.Current;
        using var data = new SettingsTransferTests.DataFolder();
        saverField.SetValue(null, null);
        try
        {
            HiddenResults.Load();
            body();
        }
        finally
        {
            (saverField.GetValue(null) as IDisposable)?.Dispose();
            saverField.SetValue(null, priorSaver);
            typeof(HiddenResults).GetProperty("Current")!.SetValue(null, priorCurrent);
            typeof(HiddenResults).GetMethod("Rebuild", flags)!.Invoke(null, null);
        }
    }

    private static ResultItem AppItem(string id) => new() { Id = id, Kind = ItemKind.App, Title = id, Subtitle = "sub" };

    public static void StoreKeepsAndForgets() => Isolated(() =>
    {
        Check.True(!HiddenResults.Any && !HiddenResults.Has("app:a"), "starts empty");
        HiddenResults.Add(AppItem("app:a"));
        HiddenResults.Add(AppItem("app:a"));
        Check.Eq(HiddenResults.Count, 1, "the same item twice is one entry");
        Check.True(HiddenResults.Has("app:a"), "hidden");
        HiddenResults.Add(new ResultItem { Id = "calc", Kind = ItemKind.Calc, Title = "3" });
        Check.True(!HiddenResults.Has("calc"), "a calculator answer can't be hidden");
        HiddenResults.Flush();
        HiddenResults.Load();
        Check.True(HiddenResults.Has("app:a"), "kept in hidden.json");
        Check.Eq(HiddenResults.List()[0].Title, "app:a", "with its name for the settings list");
        HiddenResults.Remove("app:a");
        Check.True(!HiddenResults.Has("app:a"), "brought back");
    });

    public static void RootListStillFillsNine() => Isolated(() =>
    {
        var candidates = Enumerable.Range(0, 12).Select(i => new RootCandidate(AppItem($"app:{i}"), 1000 - i)).ToList();
        HiddenResults.Add(AppItem("app:0"));
        HiddenResults.Add(AppItem("app:4"));
        var output = new List<ResultItem>();
        RootRanking.AppendTop(candidates, output, source => (ResultItem)source);
        Check.Eq(output.Count, 9, "nine rows");
        Check.Eq(string.Join(",", output.Select(x => x.Id[4..])), "1,2,3,5,6,7,8,9,10", "the hidden ones passed over, the next ones in");
    });

    public static void StripLeavesTheTypedLinesAbove() => Isolated(() =>
    {
        HiddenResults.Add(AppItem("app:x"));
        var typed = new Section("typed") { Items = { AppItem("app:x") } };
        var files = new Section("files") { Items = { AppItem("app:x") } };
        var recent = new Section("recent") { Items = { AppItem("app:x"), AppItem("app:y") } };
        var sections = new List<Section> { typed, files, recent };
        HiddenResults.Strip(sections, 1);
        Check.True(sections.Contains(typed) && typed.Items.Count == 1, "above `from`: untouched");
        Check.True(!sections.Contains(files), "a list left empty goes");
        Check.Eq(string.Join(",", recent.Items.Select(x => x.Id)), "app:y", "the rest stay");
    });

    public static void SettingsSearchFindsTheList() => Isolated(() =>
    {
        var lines = new Ui.SettingsView(new Host(new Options())).Collect();
        Check.True(lines.Any(l => l.Title == "숨긴 검색 결과" && l.PageKey == "launcher"), "설정 › 런처 has the line the search finds");
    });
}
