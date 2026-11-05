using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Clip;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// 설정 > 데이터 run for real, on a dev --data folder only (the sets do nothing on the real data):
/// `--qa-set datawipe` puts something in every line, presses each line's button with the question answered 확인, and
/// writes each line's size before and after, the time and the card it ended with to datawipe.txt. One file held open
/// shows the failure card; the cache's codex-app-server\sessions junction (made before the run, pointing into
/// CODEX_HOME) must go without what it points to. `--qa-set datadefaults` presses 설정 기본값으로 and
/// `--qa-set datareset` Bloom 초기화; both end this Bloom and start the next one, checked from outside.
internal static partial class Captures
{
    private static async Task DataWipeScreens(Host host, Launcher w, string dir)
    {
        if (!Paths.IsDev) return;
        string l = Lang.En ? "en" : "ko";
        Notices.Quiet = true;
        Notices.QaResults = new();
        var report = new StringBuilder($"data: {Paths.Data}\n");
        SeedData(host);
        await Task.Delay(2500); // the stores' savers write
        string paste = Path.Combine(Paths.Local, "ai", "paste", "qa.png");
        FileStream? held = new(paste, FileMode.Open, FileAccess.Read, FileShare.None); // the first 대화 기록 run fails on it
        string junction = Path.Combine(Paths.Local, "codex-app-server", "sessions");
        string? target = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codex ? Path.Combine(codex, "sessions") : null;
        string Linked() => $"junction {(Directory.Exists(junction) ? "있음" : "없음")}, 대상 파일 {(target is not null && Directory.Exists(target) ? Directory.GetFiles(target).Length : -1)}개";
        report.AppendLine("캐시 전: " + Linked());

        var sw = DataWindow(host);
        var view = sw.ViewForQa;
        if (!await Until(() => view.DataSettledForQa)) report.AppendLine("! 처음 크기를 재지 못함");
        await SaveSettings(sw, Path.Combine(dir, $"{l}-w01-before.png"));

        string? shoot = null;
        var asked = new List<string>();
        ConfirmWindow.QaAnswer = cw =>
        {
            asked.Add(cw.Title);
            if (shoot is { } name) SaveConfirm(cw, Path.Combine(dir, $"{l}-w02-{name}.png"));
            shoot = null;
            return MessageBoxResult.OK;
        };
        var rows = new (string key, string? shot, Func<string>? check)[]
        {
            ("chats", "chats", () => $"대화 {host.Chats.Threads.Count}개, paste {(File.Exists(paste) ? "남음" : "없음")}"),
            ("chats", null, () => $"대화 {host.Chats.Threads.Count}개, paste {(File.Exists(paste) ? "남음" : "없음")}"),
            ("clipboard", null, () => $"남은 항목 {host.Clipboard.Items.Count}개 (고정 {host.Clipboard.Items.Count(e => e.Pinned)})"),
            ("notes", "notes", () => $"메모 {Notes.Current.Items.Count}개"),
            ("timers", null, () => $"타이머 {Timers.Current.Items.Count}개"),
            ("usage", null, () => $"최근 사용 {Usage.Current.Items.Count}개"),
            ("learned", null, () => $"학습 {QueryMemory.Count}개"),
            ("colors", null, () => $"색 {ColorBook.Current.Recent.Count}개, 팔레트 {ColorBook.Current.Palettes.Count}개"),
            ("lens", null, () => $"캡처 {Host.LensFiles().count}장"),
            ("favorites", null, () => $"즐겨찾기 {Favorites.Current.Items.Count}개"),
            ("quicklinks", null, () => $"qa 바로 가기 {(Quicklinks.Current.Items.Any(q => q.Keyword == "qa") ? "남음" : "없음")}, {Quicklinks.Current.Items.Count}개"),
            ("commands", null, () => $"명령 {UserCommands.Current.Items.Count}개"),
            ("names", null, () => $"앱 이름 {AppNames.Count}개"),
            ("recovered", null, () => $"복구 폴더 {(Directory.Exists(DataParts.Recovered) && Directory.EnumerateFileSystemEntries(DataParts.Recovered).Any() ? "남음" : "비었음")}"),
            ("prices", null, () => $"prices.json {(File.Exists(DataParts.Prices) ? "남음" : "없음")}"),
            ("backups", null, () => $"backups {(Directory.Exists(Paths.In("backups")) ? "남음" : "없음")}, qa.broken {(File.Exists(Paths.In("qa.broken")) ? "남음" : "없음")}"),
            ("cache", "cache", Linked),
            ("tokens", null, null),
            ("logs", null, null),
        };
        foreach (var (key, shot, check) in rows)
        {
            if (view.DataRunsForQa?.GetValueOrDefault(key) is not { } run)
            {
                report.AppendLine($"{key}: 줄 없음");
                continue;
            }
            long before = view.DataSizesForQa?.GetValueOrDefault(key) ?? -1;
            int cards = Notices.QaResults.Count;
            shoot = shot;
            var took = Stopwatch.StartNew();
            run();
            bool done = await Until(() => Notices.QaResults.Count > cards && view.DataSettledForQa, 60000);
            long after = view.DataSizesForQa?.GetValueOrDefault(key) ?? -1;
            string card = Notices.QaResults.Count > cards ? string.Join(" / ", Notices.QaResults.Skip(cards)) : "카드 없음";
            report.AppendLine($"{key}: {before} → {after} B · {took.ElapsedMilliseconds} ms · {card}{(done ? "" : " · 시간 초과")}{(check is null ? "" : " · " + check())}");
            held?.Dispose();
            held = null;
        }
        report.AppendLine("물은 창: " + string.Join(" | ", asked));
        await SaveSettings(sw, Path.Combine(dir, $"{l}-w03-after.png"));
        sw.Close();
        ConfirmWindow.QaAnswer = null;
        Notices.QaResults = null;
        File.WriteAllText(Path.Combine(dir, "datawipe.txt"), report.ToString());
    }

    /// 설정 기본값으로, answered: the settings are written as the defaults and Bloom starts again (--restart-from).
    private static async Task DataDefaultsScreens(Host host, Launcher w, string dir)
    {
        if (!Paths.IsDev) return;
        var s = Settings.Current;
        s.Theme = new Settings().Theme == "dark" ? "light" : "dark"; // a setting that is not the default, to see it go
        Settings.Save();
        Favorites.Add(new Favorites.Entry { Id = "app:qa-keep", Kind = "app", Title = "QA 남을 즐겨찾기" }); // and one thing that stays
        var sw = DataWindow(host);
        await Until(() => sw.ViewForQa.DataRunsForQa?.ContainsKey("settings") == true);
        ConfirmWindow.QaAnswer = cw =>
        {
            SaveConfirm(cw, Path.Combine(dir, $"{(Lang.En ? "en" : "ko")}-w04-defaults.png"));
            return MessageBoxResult.OK;
        };
        sw.ViewForQa.DataRunsForQa!["settings"]();
        await Task.Delay(10000); // this Bloom has quit meanwhile
    }

    /// Bloom 초기화, answered 초기화: the next Bloom deletes the data folder (--reset-from) and starts from the first-run screens.
    private static async Task DataResetScreens(Host host, Launcher w, string dir)
    {
        if (!Paths.IsDev) return;
        var sw = DataWindow(host);
        await Task.Delay(500);
        ConfirmWindow.QaAnswer = cw =>
        {
            SaveConfirm(cw, Path.Combine(dir, $"{(Lang.En ? "en" : "ko")}-w05-reset.png"));
            return MessageBoxResult.OK;
        };
        host.ResetAll(sw);
        await Task.Delay(30000); // this Bloom has quit meanwhile
    }

    private static SettingsWindow DataWindow(Host host)
    {
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, Height = 2600 };
        sw.ViewForQa.DataRunsForQa = new();
        sw.Show();
        sw.Page("data");
        return sw;
    }

    /// Something in every line of 데이터: the stores through their own calls, the rest as files.
    private static void SeedData(Host host)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 1; i <= 3; i++)
            host.Clipboard.Add(new ClipEntry { Id = $"clip:qa{i}", Kind = "text", Text = $"QA 복사 {i}", Copied = now - i * 1000, Pinned = i == 3 });
        Notes.New("QA 메모 1");
        Notes.New("QA 메모 2");
        Timers.Add("timer", "QA 타이머", DateTime.Now.AddHours(2));
        Usage.Touch("app:qa1");
        Usage.Touch("app:qa2");
        QueryMemory.Learn("qa", "app:qa1");
        ColorBook.Add(Color.FromRgb(0xFF, 0x7A, 0x9E));
        ColorBook.AddPalette(new SavedPalette { Colors = { "#FF7A9E", "#3A7BD5" }, Shares = { 0.5, 0.5 }, Count = 2, At = DateTime.Now });
        Favorites.Add(new Favorites.Entry { Id = "app:qa1", Kind = "app", Title = "QA 앱" });
        Quicklinks.Upsert(Quicklinks.New("QA 사이트", "https://example.com/?q={query}", "qa"));
        UserCommands.Upsert(UserCommands.New("snippet", "QA 서명", "Regards", "qs"));
        AppNames.Set("app:qa1", "QA 앱 이름");
        TokenPrices.EnsureOverride();
        Zeros(Path.Combine(Host.LensDir, "qa-1.png"), 40_000);
        Zeros(Path.Combine(Host.LensDir, "qa-2.png"), 60_000);
        Zeros(Path.Combine(DataParts.Recovered, "qa.md"), 3_000);
        Zeros(Path.Combine(Paths.In("backups"), "qa", "settings.json"), 5_000);
        Zeros(Paths.In("qa.broken"), 1_000);
        Zeros(Path.Combine(Paths.Local, "update-tried.txt"), 100);
        Zeros(Path.Combine(Paths.Local, "codex-app-server", "qa-cache.bin"), 50_000);
        Zeros(Path.Combine(Paths.Local, "tokens", "qa-seed.bin"), 30_000);
        Zeros(Path.Combine(Paths.Local, "ai", "paste", "qa.png"), 20_000);
    }

    private static void Zeros(string file, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[bytes]);
    }

    private static async Task<bool> Until(Func<bool> done, int ms = 30000)
    {
        for (int t = 0; t < ms && !done(); t += 100) await Task.Delay(100);
        return done();
    }

    private static Task SaveSettings(SettingsWindow sw, string file) =>
        sw.Dispatcher.InvokeAsync(() => sw.Capture(file), System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task;

    /// The question as it would show, drawn off-screen and closed (the answer is the run's).
    private static void SaveConfirm(Window cw, string file)
    {
        cw.WindowStartupLocation = WindowStartupLocation.Manual;
        cw.Left = cw.Top = -32000;
        cw.ShowActivated = false;
        cw.Show();
        cw.UpdateLayout();
        Shots.Save((FrameworkElement)cw.Content, file);
        cw.Close();
    }
}
