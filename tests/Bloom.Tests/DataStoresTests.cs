using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// 설정 > 데이터's store functions: each empties its store in memory and in its file (a file deleted under a running store
/// would be written back), and keeps what it says it keeps.
internal static class DataStoresTests
{
    /// The stores' Current and saver as they were, put back after `body` runs in a data folder of its own.
    private static void Isolated(Action body, params Type[] stores)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var saved = stores.Select(t => (type: t, saver: t.GetField("saver", flags)!, priorSaver: t.GetField("saver", flags)!.GetValue(null), current: t.GetProperty("Current")!.GetValue(null))).ToList();
        using var data = new SettingsTransferTests.DataFolder();
        foreach (var s in saved) s.saver.SetValue(null, null); // Load would dispose the one put back after
        try
        {
            body();
        }
        finally
        {
            foreach (var s in saved)
            {
                (s.saver.GetValue(null) as IDisposable)?.Dispose();
                s.saver.SetValue(null, s.priorSaver);
                s.type.GetProperty("Current")!.SetValue(null, s.current);
            }
        }
    }

    public static void UsageClear() => Isolated(() =>
    {
        Usage.Load();
        Usage.Touch("app:a");
        Usage.Clear();
        Usage.Flush();
        Check.Eq(Usage.Current.Items.Count, 0, "the counts go");
        Check.Eq(Json.Load<Usage>(Paths.In("usage.json")).Items.Count, 0, "and the file says so");
        Check.Eq(Usage.Boost("app:a"), 0, "the ranking starts over");
    }, typeof(Usage));

    public static void TimersClear() => Isolated(() =>
    {
        Timers.Load();
        Timers.Add("timer", "tea", DateTime.Now.AddMinutes(5));
        Timers.Add("reminder", "call", DateTime.Now.AddHours(1));
        int changed = 0;
        void Count() => changed++;
        Timers.Changed += Count;
        try
        {
            Check.Eq(Timers.Clear(), 2, "both go");
        }
        finally
        {
            Timers.Changed -= Count;
        }
        Timers.Flush();
        Check.True(changed == 1, "the lists that show them are told");
        Check.Eq(Json.Load<Timers>(Paths.In("timers.json")).Items.Count, 0, "the file is empty, so none rings at the next start");
        Check.Eq(Timers.Clear(), 0, "nothing to clear: nothing happens");
    }, typeof(Timers));

    public static void NotesDeleteAll() => Isolated(() =>
    {
        Notes.Load();
        Notes.New("");
        Notes.New("   "); // empty notes go without a copy in the Recycle Bin
        Check.Eq(Notes.DeleteAll(), 2, "every note goes");
        Notes.Flush();
        Check.Eq(Notes.Current.Items.Count, 0, "none left");
        Check.Eq(Json.Load<Notes>(Paths.In("notes.json")).Items.Count, 0, "and the file says so");
    }, typeof(Notes));

    public static void FavoritesClear() => Isolated(() =>
    {
        Favorites.Load();
        Favorites.Add(new Favorites.Entry { Id = "app:a", Kind = "app", Title = "A", Mono = "pic:sparkle" });
        Favorites.AddToGroup(Favorites.AddGroup("Work").Id, new Favorites.Entry { Id = "app:b", Kind = "app", Title = "B" });
        Favorites.Clear();
        Favorites.Flush();
        Check.True(!Favorites.All().Any(), "favorites, folders and what is in them go");
        Check.Eq(Json.Load<Favorites>(Paths.In("favorites.json")).Items.Count, 0, "and the file says so");
    }, typeof(Favorites));

    public static void UserCommandsClearTakesTheirShortcuts() => Isolated(() =>
    {
        typeof(Settings).GetProperty("Current")!.SetValue(null, new Settings());
        UserCommands.Load();
        var c = UserCommands.New("snippet", "sign", "Regards", "sg");
        UserCommands.Upsert(c);
        ItemKeys.Set(new Favorites.Entry { Id = "my:" + c.Id, Kind = "cmd", Title = "sign" }, "ctrl+alt+s");
        ItemKeys.Set(new Favorites.Entry { Id = "app:x", Kind = "app", Title = "X" }, "ctrl+alt+x");
        Check.Eq(UserCommands.Clear(), 1, "the command goes");
        UserCommands.Flush();
        Check.Eq(UserCommands.Current.Items.Count, 0, "none left");
        Check.Eq(Json.Load<UserCommands>(Paths.In("usercommands.json")).Items.Count, 0, "and the file says so");
        Check.Eq(string.Join(" ", ItemKeys.All.Select(k => k.Item.Id)), "app:x", "its shortcut goes, another item's stays");
    }, typeof(UserCommands), typeof(Settings));

    public static void QuicklinksReset() => Isolated(() =>
    {
        Quicklinks.Load();
        int builtIn = Quicklinks.Current.Items.Count;
        foreach (var q in Quicklinks.Current.Items.ToList()) Quicklinks.Remove(q);
        Quicklinks.Upsert(Quicklinks.New("Mine", "https://example.com/?q={query}", "mi"));
        Quicklinks.Reset();
        Quicklinks.Flush();
        Check.Eq(Quicklinks.Current.Items.Count, builtIn, "the built-in ones come back");
        Check.True(Quicklinks.Current.Items.All(q => q.Name != "Mine"), "the user's own go");
        Check.True(Quicklinks.Typed("yt cats") is not null, "a built-in keyword works again");
        Check.Eq(Json.Load<Quicklinks>(Paths.In("quicklinks.json")).Items.Count, builtIn, "and the file says so");
    }, typeof(Quicklinks));

    public static void LogClear()
    {
        using var data = new SettingsTransferTests.DataFolder();
        Log.Write("one");
        File.WriteAllText(Paths.In("log.old.txt"), "older");
        Check.True(Log.Clear(), "both go");
        Check.True(!File.Exists(Paths.In("log.txt")) && !File.Exists(Paths.In("log.old.txt")), "no log file left");
        Log.Write("two");
        Check.True(File.ReadAllText(Paths.In("log.txt")).Contains("two", StringComparison.Ordinal), "the next line starts a new file");
    }

    public static void DataPageLines() => Isolated(() =>
    {
        typeof(Settings).GetProperty("Current")!.SetValue(null, new Settings());
        static string Titles() => string.Join(" | ", new Ui.SettingsView(new App.Host(new App.Options())).CollectDataForTests().Select(l => l.Title));
        string plain = Titles();
        foreach (string t in new[] { "Bloom 데이터", "AI 채팅 기록", "클립보드 기록", "메모", "즐겨찾기", "추가한 AI 계정", "API 키", "설정 내보내기", "설정 불러오기", "설정 기본값으로", "캐시", "토큰 집계", "로그", "Bloom 초기화", "Bloom 삭제" })
            Check.True(plain.Contains(t, StringComparison.Ordinal), $"the page lists {t}: {plain}");
        Check.True(!plain.Contains("추가 계정의 CLI 기록", StringComparison.Ordinal), "no added account: no CLI history line");
        Settings.Current.AiAccounts.Add(new AiAccount { Id = "claude-1", Kind = "claude", Home = Path.Combine(Paths.Local, "accounts", "claude-1") });
        Check.True(Titles().Contains("추가 계정의 CLI 기록", StringComparison.Ordinal), "an added account: its CLI history line");
    }, typeof(Settings));

    public static void SettingsDefaultsKeepTheUsersData() => Isolated(() =>
    {
        var s = new Settings { Theme = "light", Autostart = false, WinKeyAdmin = true, SeenVersion = "1.2.3", Onboarded = true, ClaudeChatAccount = "claude-1", AiProvider = "api" };
        s.AiAccounts.Add(new AiAccount { Id = "claude-1", Kind = "claude", Home = @"C:\b\accounts\claude-1" });
        s.ItemKeys.Add(new ItemKey { Combo = "ctrl+alt+m", Item = new Favorites.Entry { Id = "app:m", Kind = "app", Title = "M" } });
        s.OmpModels.Add(new OmpModel { Slug = "provider/model" });
        typeof(Settings).GetProperty("Current")!.SetValue(null, s);
        Settings.WriteDefaults();
        Settings.Save();
        Settings.Flush(); // saving has stopped: nothing in memory goes over the defaults
        var fresh = Json.Load<Settings>(Paths.In("settings.json"));
        var plain = new Settings();
        Check.Eq(fresh.Theme, plain.Theme, "a choice goes back to its default");
        Check.Eq(fresh.AiProvider, plain.AiProvider, "so does the AI new chats use");
        Check.True(fresh.Autostart, "Start with Windows turns on again");
        Check.True(fresh.WinKeyAdmin, "the Win-key helper's state stays (it is still installed)");
        Check.Eq(fresh.SeenVersion, "1.2.3", "the release notes are not shown again");
        Check.True(fresh.Onboarded == true, "the first-run screens are not shown again");
        Check.Eq(fresh.AiAccounts.Single().Id, "claude-1", "the added accounts stay");
        Check.Eq(fresh.ClaudeChatAccount, "claude-1", "and the one AI chat uses");
        Check.Eq(fresh.ItemKeys.Single().Combo, "ctrl+alt+m", "item shortcuts stay");
        Check.Eq(fresh.OmpModels.Single().Slug, "provider/model", "omp's model list stays");
        Settings.KeepCurrent();
        Check.Eq(Json.Load<Settings>(Paths.In("settings.json")).Theme, "light", "a restart that didn't start: the settings in memory go back");
    }, typeof(Settings));
}
