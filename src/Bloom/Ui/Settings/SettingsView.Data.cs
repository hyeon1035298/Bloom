using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 데이터: what Bloom keeps on this PC, by kind, each line with its size and a button that asks first, then deletes
/// it the way its store does (a file deleted under a running store would be written back from memory). The sizes are
/// measured once in the background when the page opens and again after each deletion; the recording mode deletes nothing.
internal sealed partial class SettingsView
{
    private Dictionary<string, long>? dataSizes; // by line key; null until measured
    private int dataRun; // the newest measuring; an older one's result is dropped

    /// The tests: 설정 > 데이터's lines as the launcher's settings search records them.
    internal List<SettingsCatalog.Line> CollectDataForTests() => Collect(PageData);
    private bool dataMeasuring;

    /// QA runs (`--qa-set datawipe`): each line's button by its key (or title) as the page was last built, and the
    /// sizes once measured.
    internal Dictionary<string, Action>? DataRunsForQa;
    internal bool DataSettledForQa => !dataMeasuring && dataSizes is not null;
    internal IReadOnlyDictionary<string, long>? DataSizesForQa => dataSizes;

    private void PageData(Settings s, string filter)
    {
        Page("data", Tr("데이터", "Data"), G.Save, Tr("Bloom이 이 PC에 저장한 것을 보고 지웁니다. 크기는 이 페이지를 열 때 잽니다.", "View and clear data stored by Bloom on this PC. Sizes are calculated when you open this page."), filter);
        if (collected is null && filter.Length == 0 && current != "data")
        {
            dataSizes = null; // measured again at the next visit
            return;
        }
        if (collected is null && dataSizes is null && !dataMeasuring) MeasureData();
        var added = Host.AddedHomes();
        bool live = collected is null; // collecting reads no disk and no counts

        Header(Tr("저장 위치", "Storage Locations"));
        string where = Demo.On // the recording mode: an installed copy's folders, not this run's
            ? $"{Short(Path.Combine(Paths.AppData, "Bloom"))} · {Short(Path.Combine(Paths.LocalAppData, "Bloom"))}"
            : $"{Short(Paths.Data)} · {Short(Paths.Local)}";
        DataOpen("data", Tr("Bloom 데이터", "Bloom Data"), where, Paths.Data, filter);
        if (live && filter.Length == 0 && dataSizes is { } sizes) Card(filter).Children.Add(SpaceBar(sizes));
        // the add-ons' files are in use while their hosts run (a run copy loaded in Explorer): shown, not deleted here
        if (!live || DataParts.Addons.Any(Directory.Exists))
            DataOpen("addons", Tr("애드온 데이터", "Add-on Data"), Tr("애드온의 설정, 상태, 실행 사본", "Add-on settings, state, and runtime copies"), DataParts.Addons[0], filter);

        Header(Tr("대화와 기록", "Chats & History"));
        int chats = live ? host.Chats.Threads.Count : 0;
        Clearable("chats", Tr("AI 채팅 기록", "AI Chat History"), live ? Tr($"대화 {chats}개와 첨부 파일", $"{chats} chats and attachments") : Tr("대화와 첨부 파일", "Chats and attachments"), chats == 0,
            Tr($"AI 채팅 대화 {chats}개와 첨부 파일을 지웁니다. 열린 AI 채팅 창은 닫힙니다. Claude Code와 Codex가 자기 폴더에 저장한 기록은 남습니다. 되돌릴 수 없습니다.", $"Clear {chats} AI Chat conversations and their attachments. Open AI Chat windows will close. History saved by Claude Code and Codex in their own folders will remain. This cannot be undone."),
            () => Task.FromResult(host.ClearChats()), filter);
        if (added.Count > 0)
        {
            string cli = SizeOf("cli");
            Clearable("cli", Tr("추가 계정의 CLI 기록", "CLI History for Added Accounts"), Tr("Bloom에서 추가한 계정이 Claude Code와 Codex에 남긴 대화", "Conversations left in Claude Code and Codex by accounts added in Bloom"), null,
                Tr($"Bloom에서 추가한 계정으로 나눈 대화가 Claude Code와 Codex 쪽에 남긴 기록 {cli}을 지웁니다. 그 대화는 더 이상 이어 갈 수 없습니다. 기본 계정의 기록은 남습니다. 되돌릴 수 없습니다.", $"Clear {cli} of history left in Claude Code and Codex by accounts added in Bloom. Those conversations can no longer be resumed. History for default accounts will remain. This cannot be undone."),
                host.ClearCliHistory, filter);
        }
        int clips = live ? host.Clipboard.Items.Count(e => !e.Pinned) : 0;
        Clearable("clipboard", Tr("클립보드 기록", "Clipboard History"), live ? Tr($"복사한 내용 {clips}개 · 고정한 항목은 남음", $"{clips} copied items · Pinned items remain") : Tr("복사한 내용 · 고정한 항목은 남음", "Copied items · Pinned items remain"), clips == 0,
            Tr($"복사한 내용 {clips}개를 지웁니다. 고정한 항목은 남습니다. 되돌릴 수 없습니다.", $"Clear {clips} copied items. Pinned items will remain. This cannot be undone."),
            Done(() => host.Clipboard.Clear()), filter);
        int notes = Notes.Current.Items.Count;
        Clearable("notes", Tr("메모", "Notes"), live ? Tr($"메모 {notes}개", $"{notes} notes") : Tr("메모", "Notes"), notes == 0,
            Tr($"메모 {notes}개를 휴지통으로 보냅니다. 휴지통에서 복원할 수 있습니다.", $"Move {notes} notes to the Recycle Bin. You can restore them from the Recycle Bin."),
            Done(() => Notes.DeleteAll()), filter, trash: true);
        int timers = Timers.Current.Items.Count;
        Clearable("timers", Tr("타이머 · 알림", "Timers · Reminders"), live ? Tr($"진행 중 {timers}개", $"{timers} active") : Tr("진행 중인 타이머와 알림", "Active timers and reminders"), timers == 0,
            Tr($"진행 중인 타이머와 알림 {timers}개를 취소합니다. 되돌릴 수 없습니다.", $"Cancel {timers} active timers and reminders. This cannot be undone."),
            Done(() => Timers.Clear()), filter);
        Clearable("usage", Tr("최근 사용", "Recent Items"), Tr("최근 사용 목록과 실행 횟수", "Recent items list and launch counts"), Usage.Current.Items.Count == 0,
            Tr("최근 사용 목록과 실행 횟수를 지웁니다. 검색 결과 순서가 처음 상태로 돌아갑니다. 되돌릴 수 없습니다.", "Clear the recent items list and launch counts. Search result ranking will reset to default. This cannot be undone."),
            Done(() =>
            {
                Usage.Clear();
                host.Window.Refresh(true);
            }), filter);
        int learned = live ? QueryMemory.Count : 0;
        Clearable("learned", Tr("검색 학습", "Search Learning"), live ? Tr($"고른 결과 기록 {learned}개", $"{learned} selected result records") : Tr("고른 결과 기록", "Selected result records"), learned == 0,
            Tr($"고른 결과 기록 {learned}개를 지웁니다. 되돌릴 수 없습니다.", $"Clear {learned} selected result records. This cannot be undone."),
            Done(QueryMemory.Clear), filter);
        int colours = ColorBook.Current.Recent.Count, palettes = ColorBook.Current.Palettes.Count;
        Clearable("colors", Tr("최근 색 · 팔레트", "Recent Colors · Palettes"), live ? Tr($"최근 색 {colours}개와 팔레트 {palettes}개", $"{colours} recent colors and {palettes} palettes") : Tr("최근 색과 팔레트", "Recent colors and palettes"), colours + palettes == 0,
            Tr($"최근 색 {colours}개와 팔레트 {palettes}개를 지웁니다. 되돌릴 수 없습니다.", $"Clear {colours} recent colors and {palettes} palettes. This cannot be undone."),
            Done(ColorBook.Clear), filter);
        long? shots = dataSizes is { } ls && ls.TryGetValue("lens#", out long n) ? n : null;
        Clearable("lens", Tr("Bloom Lens 캡처", "Bloom Lens Captures"), shots is { } k ? Tr($"캡처 {k}장", $"{k} captures") : Tr("캡처한 그림", "Captured images"), shots is null ? null : shots == 0,
            Tr($"캡처 {shots}장을 지웁니다. 화면에 고정한 그림은 남고, AI 채팅의 그림 미리보기는 사라집니다. 되돌릴 수 없습니다.", $"Clear {shots} captures. Images pinned to the screen will remain, while image previews in AI Chat will be removed. This cannot be undone."),
            () =>
            {
                host.ClearLensFiles();
                return Task.FromResult(Host.LensFiles().count);
            }, filter, extra: live ? OpenButton(Host.LensDir) : null);

        Header(Tr("만든 항목", "Created Items"));
        int favs = Favorites.All().Count(e => e.Kind != Favorites.GroupKind);
        Clearable("favorites", Tr("즐겨찾기", "Favorites"), live ? Tr($"즐겨찾기 {favs}개와 폴더 묶음, 그린 아이콘", $"{favs} favorites, folder groups, and custom icons") : Tr("즐겨찾기와 폴더 묶음, 그린 아이콘", "Favorites, folder groups, and custom icons"), Favorites.Current.Items.Count == 0,
            Tr($"즐겨찾기 {favs}개와 폴더 묶음, 그린 아이콘을 지웁니다. 항목 단축키는 남습니다. 되돌릴 수 없습니다.", $"Clear {favs} favorites, folder groups, and custom icons. Item shortcuts will remain. This cannot be undone."),
            Done(() =>
            {
                Favorites.Clear();
                host.Window.Refresh(true);
            }), filter);
        int links = Quicklinks.Current.Items.Count;
        Clearable("quicklinks", Tr("사이트 바로 가기", "Site Shortcuts"), live ? Tr($"바로 가기 {links}개", $"{links} shortcuts") : Tr("직접 만든 바로 가기", "Custom shortcuts"), false,
            Tr("직접 만든 바로 가기를 지우고 기본 바로 가기로 되돌립니다. 되돌릴 수 없습니다.", "Clear custom shortcuts and restore default shortcuts. This cannot be undone."),
            Done(Quicklinks.Reset), filter);
        int commands = UserCommands.Current.Items.Count;
        Clearable("commands", Tr("사용자 명령", "Custom Commands"), live ? Tr($"스크립트, 상용구, 묶음 {commands}개", $"{commands} scripts, snippets, and bundles") : Tr("스크립트, 상용구, 묶음", "Scripts, snippets, and bundles"), commands == 0,
            Tr($"스크립트, 상용구, 묶음 {commands}개와 그 단축키를 지웁니다. 되돌릴 수 없습니다.", $"Clear {commands} scripts, snippets, and bundles along with their shortcuts. This cannot be undone."),
            Done(() =>
            {
                UserCommands.Clear();
                host.ApplyHotkeys();
            }), filter);
        int names = live ? AppNames.Count : 0;
        Clearable("names", Tr("바꾼 앱 이름", "Renamed Apps"), live ? Tr($"앱 이름 {names}개", $"{names} app names") : Tr("Bloom에서 바꾼 앱 이름", "App names renamed in Bloom"), names == 0,
            Tr($"앱 이름 {names}개를 원래 이름으로 되돌립니다.", $"Restore {names} app names to their original names."),
            Done(() =>
            {
                AppNames.ClearAll();
                host.AppsChanged();
            }), filter);
        var lists = live ? Playlists.All() : new();
        if (lists.Count > 0)
            Clearable("playlists", Tr("재생목록", "Playlists"), Tr($"재생목록 {lists.Count}개", $"{lists.Count} playlists"), false,
                Tr($"재생목록 {lists.Count}개를 휴지통으로 보냅니다.", $"Move {lists.Count} playlists to the Recycle Bin."),
                () => Task.FromResult(lists.Count(l => !Playlists.Delete(l))), filter, trash: true);
        if (live && HasEntries(DataParts.Recovered))
            DataLine("recovered", Tr("복구한 문서", "Recovered Documents"), Tr("저장하지 못한 문서 편집분", "Unsaved document edits"), false, filter, ClearRecovered, Tr("휴지통으로", "To the Recycle Bin"));

        Header(Tr("로그인 정보", "Sign-in Info"));
        int accounts = AiAccounts.All.Count(a => !a.IsDefault);
        Clearable("accounts", Tr("추가한 AI 계정", "Added AI Accounts"), Tr($"계정 {accounts}개의 로그인", $"{accounts} account sign-ins"), accounts == 0,
            Tr($"Bloom에서 추가한 계정 {accounts}개에서 로그아웃하고 그 로그인 폴더를 휴지통으로 보냅니다. Claude Code와 Codex의 기본 계정은 그대로입니다.", $"Sign out of {accounts} accounts added in Bloom and move their sign-in folders to the Recycle Bin. Default accounts for Claude Code and Codex will remain untouched."),
            async () =>
            {
                await host.SignOutAdded(TimeSpan.FromSeconds(45));
                return 0;
            }, filter, trash: true);
        int keys = live ? ApiProviders.All.Count(p => ApiKeys.Has(p.Id)) : 0; // collecting reads no keys
        Clearable("apikeys", Tr("API 키", "API Keys"), live ? Tr($"키 {keys}개", $"{keys} keys") : Tr("API 제공자의 키", "API provider keys"), keys == 0,
            Tr($"저장한 API 키 {keys}개를 지웁니다. API 대화와 API 번역을 쓰려면 키를 다시 넣어야 합니다. 되돌릴 수 없습니다.", $"Clear {keys} saved API keys. To use API chat and API translation, you will need to enter your keys again. This cannot be undone."),
            () => ClearApiKeys(s), filter);
        if (live && ApiKeys.Has(MusicPlayer.JriverKey))
            Clearable(null, Tr("JRiver 비밀번호", "JRiver Password"), Tr("미니 플레이어가 JRiver에 보내는 비밀번호", "Password sent to JRiver by Mini Player"), false,
                Tr("저장한 JRiver 비밀번호를 지웁니다.", "Clear the saved JRiver password."),
                async () =>
                {
                    await StoreJriverPassword("");
                    return 0;
                }, filter);

        Header(Tr("설정", "Settings"));
        Add(Tr("설정 내보내기", "Export Settings"), Tr("설정, 즐겨찾기, 앱 이름, 사이트 바로 가기, 최근 색을 파일 하나로", "Save settings, favorites, app names, site shortcuts, and recent colors to a single file"),
            DataButton(Tr("내보내기", "Export"), () => host.ExportSettings(Window.GetWindow(this))), G.Send, () => host.ExportSettings(Window.GetWindow(this)), null, filter);
        Add(Tr("설정 불러오기", "Import Settings"), Tr("내보낸 설정 파일에서 불러옵니다. 지금 설정은 데이터 폴더에 백업합니다.", "Import from an exported settings file. Current settings are backed up to the data folder."),
            DataButton(Tr("불러오기", "Import"), () => host.ImportSettings(Window.GetWindow(this))), G.Import, () => host.ImportSettings(Window.GetWindow(this)), null, filter);
        DataLine("settings", Tr("설정 기본값으로", "Reset Settings to Default"), Tr("즐겨찾기, 대화, 계정, API 키는 남습니다", "Favorites, chats, accounts, and API keys will remain"), false, filter, () =>
        {
            if (Demo.On) return;
            if (AskData(Tr("설정 기본값으로", "Reset Settings to Default"),
                    Tr("모든 설정을 기본값으로 되돌리고 Bloom을 다시 시작합니다. Windows 시작 시 실행도 다시 켜집니다. 즐겨찾기, 대화, 추가한 계정, API 키, 항목 단축키, omp 모델 목록은 남습니다.", "Reset all settings to their defaults and restart Bloom. Launch at Windows startup will also be re-enabled. Favorites, chats, added accounts, API keys, item shortcuts, and the omp model list will remain."),
                    Tr("기본값으로 되돌리기", "Reset to Default"), ButtonTone.Danger))
                host.RestoreDefaults(Window.GetWindow(this));
        }, Tr("기본값으로", "Reset"));
        if (live && File.Exists(DataParts.Prices))
            Clearable("prices", Tr("고친 가격표", "Custom Price Table"), Tr("직접 고친 토큰 가격", "Custom token pricing"), false,
                Tr("직접 고친 토큰 가격을 지우고 내장 가격으로 되돌립니다.", "Clear custom token pricing and restore built-in pricing."),
                () =>
                {
                    int left = DataWipe.Delete(DataParts.Prices);
                    TokenPrices.Reload();
                    TokenStats.Refresh();
                    return Task.FromResult(left);
                }, filter);
        int backups = live ? BackupCount() : 0;
        if (backups > 0)
            Clearable("backups", Tr("설정 백업", "Settings Backups"), Tr($"옛 설정 사본 {backups}개", $"{backups} previous settings backups"), false,
                Tr($"설정을 불러오기 전에 만든 옛 설정 사본 {backups}개를 지웁니다.", $"Clear {backups} previous settings backups created before importing settings."),
                () => Task.Run(() => DataWipe.Delete(DataParts.Backups().ToList())), filter);

        Header(Tr("캐시와 로그", "Cache & Logs"));
        Clearable("cache", Tr("캐시", "Cache"), Tr("모델 목록, 환율, ChatGPT 연결 데이터", "Model lists, exchange rates, and ChatGPT connection data"), null,
            Tr("다시 만들 수 있는 파일을 지웁니다. 모델 목록, 환율, ChatGPT 연결 데이터입니다. 다음 ChatGPT 대화의 첫 답은 기록을 다시 읽느라 수십 초 늦을 수 있습니다.", "Clear files that can be regenerated, including model lists, exchange rates, and ChatGPT connection data. The first reply in your next ChatGPT conversation may take longer while history is reloaded."),
            host.ClearCache, filter, step: Tr("비우기", "Clear"), heading: Tr("캐시 비우기", "Clear Cache"), tone: ButtonTone.Plain);
        Clearable("tokens", Tr("토큰 집계", "Token Counts"), Tr("사용량 화면의 토큰 숫자", "Token counts shown in Usage"), null,
            Tr("Bloom이 센 토큰 숫자를 지우고 남은 기록에서 다시 셉니다. Claude Code가 이미 지운 30일 넘은 기록은 돌아오지 않습니다.", "Clear token counts calculated by Bloom and recount from remaining history. Logs older than 30 days already deleted by Claude Code cannot be recovered."),
            Done(() =>
            {
                TokenStats.Reset();
                host.Window.Refresh(true);
            }), filter, heading: Tr("토큰 집계 지우기", "Clear Token Counts"));
        Clearable("logs", Tr("로그", "Logs"), Tr("문제 보고에 쓰는 기록", "Diagnostic logs used for reporting issues"), null,
            Tr("문제 보고에 쓰는 기록을 비웁니다.", "Clear diagnostic logs used for reporting issues."),
            () => Task.FromResult((Log.Clear() ? 0 : 1) + DataWipe.Delete(Paths.In("memory.txt"))), filter, step: Tr("비우기", "Clear"), heading: Tr("로그 비우기", "Clear Logs"));

        Header(Tr("전체", "All Data"));
        Add(Tr("Bloom 초기화", "Reset Bloom"), Tr("모든 데이터를 지우고 처음 설정 화면부터 다시 시작합니다", "Clear all data and restart from initial setup"),
            DataButton(Tr("초기화…", "Reset…"), ResetBloom), G.Delete, ResetBloom, null, filter);
        Add(Tr("Bloom 삭제", "Uninstall Bloom"), Tr("이 PC에서 Bloom을 삭제합니다. 데이터를 남길지 고릅니다", "Uninstall Bloom from this PC. You can choose whether to keep your data"),
            DataButton(Tr("삭제…", "Uninstall…"), UninstallBloom), G.Delete, UninstallBloom, null, filter);
    }

    private void ResetBloom()
    {
        if (!Demo.On) host.ResetAll(Window.GetWindow(this));
    }

    private void UninstallBloom()
    {
        if (!Demo.On) host.UninstallBloom(Window.GetWindow(this));
    }

    /// The top lines of 저장 위치: a folder's size and 폴더 열기 (Enter opens it too); nothing to delete there.
    private void DataOpen(string key, string title, string what, string folder, string filter)
    {
        if (Record(title, what, filter)) return;
        string sub = dataSizes is { } d ? Join(what, Engine.Size(d.GetValueOrDefault(key))) : Join(what, Tr("크기 계산 중", "Calculating size"));
        Add(title, sub, OpenButton(folder), G.Folder, () => OpenFolder(folder), null, filter);
    }

    /// A line that asks, deletes and says how it went (Wipe). `empty`: nothing to delete ("없음", the button off); null
    /// judges by the measured size, the button off until it is known. The step's button is red unless the line sends to
    /// the Recycle Bin (`trash`) or a `tone` says otherwise.
    private void Clearable(string? key, string title, string what, bool? empty, string body, Func<Task<int>> delete, string filter,
        bool trash = false, string? step = null, string? heading = null, ButtonTone? tone = null, FrameworkElement? extra = null)
    {
        string label = step ?? (trash ? Tr("휴지통으로", "To the Recycle Bin") : Tr("지우기", "Clear"));
        DataLine(key, title, what, empty, filter, () =>
        {
            if (Demo.On) return; // the recording mode shows the page and deletes nothing
            if (AskData(heading ?? Tr($"{title} 지우기", $"Clear {title}"), body, label, tone ?? (trash ? ButtonTone.Plain : ButtonTone.Danger))) Wipe(key, title, trash, delete);
        }, label, extra);
    }

    /// One line of 데이터: what it holds, then its size once measured; "없음" and its button off when `empty` (null: by
    /// the size, off while it is measured). Collecting records the title and what it holds only.
    private void DataLine(string? key, string title, string what, bool? empty, string filter, Action run, string button, FrameworkElement? extra = null)
    {
        if (Record(title, what, filter)) return;
        if (DataRunsForQa is { } runs) runs[key ?? title] = run;
        long? bytes = key is not null && dataSizes is { } d && d.TryGetValue(key, out long b) ? b : null;
        bool measuring = key is not null && dataSizes is null;
        bool none = empty ?? bytes is 0;
        bool off = none || empty is null && measuring;
        string sub = none ? Tr("없음", "None") : measuring ? Join(what, Tr("크기 계산 중", "Calculating size")) : bytes is { } size ? Join(what, Engine.Size(size)) : what;
        var act = DataButton(button, run);
        if (off)
        {
            act.Opacity = 0.4;
            act.IsHitTestVisible = false;
        }
        FrameworkElement control = act;
        if (extra is not null)
        {
            var both = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            both.Children.Add(extra);
            both.Children.Add(act);
            control = both;
        }
        Add(title, sub, control, G.Delete, off ? null : run, null, filter);
    }

    /// Deletes (the user already said yes), measures again and says how it went: "{분류} 삭제 · {크기} 확보", "{분류}
    /// 휴지통으로 이동", or what stayed and what to do. A failure is a card, never a crash.
    private async void Wipe(string? key, string title, bool trash, Func<Task<int>> delete)
    {
        long? before = key is not null && dataSizes is { } d && d.TryGetValue(key, out long b) ? b : null;
        int left;
        try
        {
            left = await delete();
        }
        catch (Exception e)
        {
            Log.Error("data " + (key ?? title), e);
            left = 1;
        }
        host.SaveAll(); // a store writes its emptied file some 400 ms later: now, so the size measured is the one left
        int run = ++dataRun;
        dataMeasuring = true;
        dataSizes = null;
        DataRebuild();
        var added = Host.AddedHomes();
        var fresh = await Task.Run(() => SafeMeasure(added));
        if (run == dataRun)
        {
            dataMeasuring = false;
            dataSizes = fresh;
            DataRebuild();
        }
        string head = Tr("Bloom · 데이터", "Bloom · Data");
        if (left > 0)
            Notices.Result(false, head, Tr($"{title} 일부를 지우지 못함", $"Could not clear part of {title}"), Tr("다른 프로그램이 쓰고 있는 파일이 남았습니다. 그 프로그램을 닫고 다시 시도하세요.", "Some files are in use by another program. Close that program and try again."));
        else if (trash) Notices.Result(true, head, Tr($"{title} 휴지통으로 이동", $"Moved {title} to Recycle Bin"), null);
        else if (before is { } was && key is not null)
        {
            string freed = Engine.Size(Math.Max(0, was - fresh.GetValueOrDefault(key)));
            Notices.Result(true, head, Tr($"{title} 삭제 · {freed} 확보", $"Deleted {title} · {freed} freed"), null);
        }
        else Notices.Result(true, head, Tr($"{title} 삭제", $"Deleted {title}"), null);
    }

    /// 복구한 문서: its copy may be the only one, so the default is to open the folder and look first.
    private void ClearRecovered()
    {
        if (Demo.On) return;
        string dir = DataParts.Recovered;
        var pick = ConfirmWindow.Ask(Window.GetWindow(this), Tr("복구한 문서 지우기", "Clear Recovered Documents"),
            Tr("저장하지 못한 문서 편집분입니다. 이 사본이 유일할 수 있습니다. 보내기 전에 열어 확인하세요.", "These are unsaved document edits and may be your only copy. Open the folder to check them before moving to the Recycle Bin."),
            MessageBoxResult.Yes,
            (Tr("폴더 열기", "Open Folder"), MessageBoxResult.Yes, ButtonTone.Plain),
            (Tr("휴지통으로", "To the Recycle Bin"), MessageBoxResult.OK, ButtonTone.Danger),
            (Tr("취소", "Cancel"), MessageBoxResult.Cancel, ButtonTone.Plain));
        if (pick == MessageBoxResult.Yes) OpenFolder(dir);
        else if (pick == MessageBoxResult.OK)
            Wipe("recovered", Tr("복구한 문서", "Recovered Documents"), true, () => Task.FromResult(DataWipe.Inside(dir) && Native.ShellFiles.Recycle(dir, IntPtr.Zero) ? 0 : 1));
    }

    /// API 키: every API provider's key and model list goes (JRiver's password is not one); a side that was the API goes
    /// back to Claude and the API models picked are forgotten.
    private async Task<int> ClearApiKeys(Settings s)
    {
        foreach (var p in ApiProviders.All.Where(p => ApiKeys.Has(p.Id)).ToList())
        {
            ApiCatalog.Forget(p.Id); // at once: a refresh under way publishes nothing
            await ApiKeys.RemoveAsync(p.Id);
            ApiBalances.Forget(p.Id);
        }
        if (s.AiProvider == "api") s.AiProvider = "claude";
        if (s.TranslateProvider == "api") s.TranslateProvider = "claude";
        s.ApiModel = null;
        s.TranslateApiModel = null;
        Settings.Save();
        _ = AiUsage.RefreshBalances();
        return 0;
    }

    private bool AskData(string heading, string body, string step, ButtonTone tone) =>
        ConfirmWindow.Ask(Window.GetWindow(this), heading, body, MessageBoxResult.Cancel,
            (step, MessageBoxResult.OK, tone), (Tr("취소", "Cancel"), MessageBoxResult.Cancel, ButtonTone.Plain)) == MessageBoxResult.OK;

    /// The sizes, measured off the UI thread; the page shows "크기 계산 중" meanwhile and follows when they come.
    private void MeasureData()
    {
        int run = ++dataRun;
        dataMeasuring = true;
        var added = Host.AddedHomes();
        _ = Task.Run(() => SafeMeasure(added)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (run != dataRun) return;
            dataMeasuring = false;
            dataSizes = t.Result;
            DataRebuild();
        }), TaskScheduler.Default);
    }

    /// The page again where it is scrolled to, while it shows (or a filter lists its lines).
    private void DataRebuild()
    {
        if (!IsLoaded || current != "data" && lastFilter.Length == 0) return;
        double y = VerticalOffset;
        Build(lastFilter);
        ScrollToVerticalOffset(y);
    }

    private static Dictionary<string, long> SafeMeasure(List<(string kind, string home)> added)
    {
        try
        {
            return Measure(added);
        }
        catch (Exception e)
        {
            Log.Error("data sizes", e);
            return new();
        }
    }

    /// Every line's bytes on disk, by its key ("lens#" is the number of captures).
    private static Dictionary<string, long> Measure(List<(string kind, string home)> added)
    {
        static long S(IEnumerable<string> paths) => DataWipe.Size(paths);
        long cli = S(DataParts.CliHistory(added));
        string data = Path.TrimEndingDirectorySeparator(Paths.Data), local = Path.TrimEndingDirectorySeparator(Paths.Local);
        bool nested = local.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); // a dev --data folder holds local\
        return new()
        {
            ["data"] = DataWipe.Size(data) + (nested ? 0 : DataWipe.Size(local)),
            ["addons"] = S(DataParts.Addons),
            ["chats"] = S(DataParts.Chats),
            ["cli"] = cli,
            ["clipboard"] = S(DataParts.Clipboard),
            ["notes"] = S(DataParts.File("notes.json")),
            ["timers"] = S(DataParts.File("timers.json")),
            ["usage"] = S(DataParts.File("usage.json")),
            ["learned"] = S(DataParts.File("learned.json")),
            ["colors"] = S(DataParts.File("colors.json")),
            ["lens"] = S(DataParts.Lens),
            ["lens#"] = Directory.Exists(DataParts.Lens[0]) ? Directory.GetFiles(DataParts.Lens[0], "*.png").Length : 0,
            ["favorites"] = S(DataParts.File("favorites.json")),
            ["quicklinks"] = S(DataParts.File("quicklinks.json")),
            ["commands"] = S(DataParts.File("usercommands.json")),
            ["names"] = S(DataParts.File("names.json")),
            ["playlists"] = S(DataParts.Playlists),
            ["recovered"] = DataWipe.Size(DataParts.Recovered),
            ["accounts"] = Math.Max(0, added.Sum(a => DataWipe.Size(a.home)) - cli),
            ["apikeys"] = S(DataParts.File("api-keys.json")),
            ["settings"] = S(DataParts.File("settings.json")),
            ["prices"] = DataWipe.Size(DataParts.Prices),
            ["backups"] = S(DataParts.Backups()),
            ["cache"] = S(DataParts.Cache()),
            ["tokens"] = S(DataParts.Tokens),
            ["logs"] = S(DataParts.Logs),
        };
    }

    /// Under 저장 위치: one bar of the data's size, the big parts first in steps of the text colour, and their sizes under it.
    private static FrameworkElement SpaceBar(Dictionary<string, long> sizes)
    {
        long total = Math.Max(1, sizes.GetValueOrDefault("data"));
        var parts = new List<(string name, long bytes)>
        {
            (Tr("추가 계정의 CLI 기록", "CLI History for Added Accounts"), sizes.GetValueOrDefault("cli")),
            (Tr("캐시", "Cache"), sizes.GetValueOrDefault("cache")),
            (Tr("추가한 AI 계정", "Added AI Accounts"), sizes.GetValueOrDefault("accounts")),
            (Tr("토큰 집계", "Token Counts"), sizes.GetValueOrDefault("tokens")),
            (Tr("Bloom Lens 캡처", "Bloom Lens Captures"), sizes.GetValueOrDefault("lens")),
        };
        parts.Add((Tr("나머지", "Other"), Math.Max(0, total - parts.Sum(p => p.bytes))));
        double[] tones = { 0.62, 0.44, 0.3, 0.2, 0.13, 0.08 };
        var bar = new Grid { Height = 6 };
        var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].bytes <= 0) continue;
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].bytes, GridUnitType.Star) });
            var seg = new Border { Background = Theme.P.Text, Opacity = tones[i] };
            Grid.SetColumn(seg, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(seg);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };
            item.Children.Add(new SmoothBorder { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = Theme.P.Text, Opacity = tones[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            item.Children.Add(Theme.T($"{parts[i].name} {Engine.Size(parts[i].bytes)}", 12, Theme.P.TextFaint));
            legend.Children.Add(item);
        }
        var box = new StackPanel { Margin = new Thickness(12, 2, 12, 12) };
        box.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(3), Background = Theme.P.Track, Child = bar, ClipToBounds = true });
        box.Children.Add(legend);
        return box;
    }

    /// A neutral 32-high button at the end of a line.
    private static SmoothBorder DataButton(string label, Action click)
    {
        var b = ActionButton.Make(label, ButtonTone.Plain, click);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private static FrameworkElement OpenButton(string folder)
    {
        var b = ToolUi.TextButton(Tr("폴더 열기", "Open Folder"), () => OpenFolder(folder));
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Launch.Open(folder);
    }

    private static Func<Task<int>> Done(Action a) => () =>
    {
        a();
        return Task.FromResult(0);
    };

    private static string Join(string what, string more) => what.Length == 0 ? more : what + " · " + more;

    /// The measured size of a line in words, for the question before it goes.
    private string SizeOf(string key) => dataSizes is { } d ? Engine.Size(d.GetValueOrDefault(key)) : Tr("전부", "all");

    /// A data folder as %APPDATA%\Bloom rather than with the user's name in it.
    private static string Short(string path)
    {
        foreach (var (root, name) in new[] { (Paths.LocalAppData, "%LOCALAPPDATA%"), (Paths.AppData, "%APPDATA%") })
            if (root.Length > 0 && path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return name + path[Path.TrimEndingDirectorySeparator(root).Length..];
        return path;
    }

    private static bool HasEntries(string dir)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// The old settings copies: one folder each in backups\, and each *.broken file.
    private static int BackupCount()
    {
        try
        {
            string dir = Paths.In("backups");
            return (Directory.Exists(dir) ? Directory.GetDirectories(dir).Length + Directory.GetFiles(dir).Length : 0) + DataParts.Backups().Count(File.Exists);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
