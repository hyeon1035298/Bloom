using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Browser;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The settings pages, one method each; Build lists them in the sidebar's order and sections.
internal sealed partial class SettingsView
{
    private void PageGeneral(Settings s, string filter)
    {
        Page("general", Tr("일반", "General"), G.Settings, Tr("Bloom을 여는 방법과 모양", "How Bloom opens and looks"), filter);
        Header(Tr("언어", "Language"));
        Choice("언어 · Language", Tr("언어 변경 시 Bloom 다시 시작 · 자동은 Windows 표시 언어 사용", "Restarts Bloom to apply · Auto uses the Windows display language"), G.Globe,
            Lang.Choices, s.Language, v =>
            {
                if (v == s.Language) return;
                s.Language = v;
                Settings.Save();
                if (!Lang.Pending(v)) return; // "auto" that lands on the language already shown
                AskRestart(Tr("언어를 적용하려면 Bloom을 다시 시작해야 합니다.", "Restart Bloom to apply the language."));
            }, filter);
        Header(Tr("열기", "Open"));
        var winSw = Toggle(s.WinKey, v => { s.WinKey = v; host.ApplyHotkeys(); }, out var winT);
        string winSub = Tr("Win 키만 눌렀다 떼면 열립니다 (Win+E 같은 조합은 그대로)", "Tap Win alone to open (combinations like Win+E still work)");
        Add(Tr("Win 키로 열기", "Open with the Win key"), winSub, winSw, G.Keyboard, winT, null, filter);
        if (ElevatedWinKey.Offered)
        {
            // the switch follows the outcome: a declined UAC prompt leaves it off
            Switch? adminSw = null;
            adminSw = Toggle(s.WinKeyAdmin, async v => adminSw!.On = await host.SetWinKeyAdmin(v, Window.GetWindow(this)), out var adminT);
            Add(Tr("관리자 앱 위에서도 Win 키 사용", "Win key over admin apps"), Tr("관리자 권한 앱이 앞에 있어도 Win 키로 열립니다 · 켤 때 관리자 승인 필요", "Opens with Win even over apps run as administrator · admin approval to turn on"), adminSw, G.Shield, adminT, null, filter);
        }
        var altSw = Toggle(s.AltSpace, v => { s.AltSpace = v; host.ApplyHotkeys(); }, out var altT);
        string altSub = host.AltSpaceStatus switch { "taken" => Tr("다른 앱에서 사용 중", "Another app uses it"), "ok" => Tr("켜짐", "On"), _ => Tr("꺼짐", "Off") };
        Add(Tr("Alt + Space로 열기", "Open with Alt + Space"), altSub, altSw, G.Keyboard, altT, null, filter);
        var autoSw = Toggle(s.Autostart, v => { s.Autostart = v; Autostart.Set(v); }, out var autoT);
        Add(Tr("윈도우 시작 시 실행", "Start with Windows"), Paths.IsDev ? Tr("개발 실행에서는 자동 시작 설정 불가", "Autostart unavailable in development runs") : Autostart.IsOn() ? Tr("로그인 시 트레이에서 실행", "Runs in the tray at sign-in") : Tr("등록 안 됨", "Not registered"), autoSw, G.Power, autoT, null, filter);
        Header(Tr("모양", "Look"));
        Choice(Tr("테마", "Theme"), Theme.Acrylic ? Tr("유리 효과 켜짐 (Windows 투명 효과)", "Glass on (Windows transparency effects)") : Tr("Windows 투명 효과 꺼짐 · 단색 배경", "Windows transparency effects are off, so it is solid"), G.Palette,
            new[] { ("system", Tr("시스템", "System")), ("light", Tr("밝게", "Light")), ("dark", Tr("어둡게", "Dark")) }, s.Theme, v =>
            {
                s.Theme = v;
                Theme.Apply(v);
            }, filter);
        var accent = Accents.Current ?? Accents.All[0];
        bool customOk = Accents.TryParse(s.AccentCustom, out var customColour, out _);
        var field = new ColourField(s.AccentCustom) { Done = () => FocusFilter?.Invoke() };
        var dots = Accents.All.Select(a => (a.Key, a.Name, Accents.Swatch(a, Theme.P.Dark)))
            .Append((Accents.CustomKey, Tr("직접", "Custom"), customOk ? customColour : System.Windows.Media.Color.FromRgb(0x8C, 0x8C, 0x90)));
        var sw = new Swatches(dots, s.Accent == Accents.CustomKey && !customOk ? Accents.All[0].Key : accent.Key);
        void PickAccent(string v)
        {
            if (v == Accents.CustomKey && !customOk) // nothing typed yet: the code goes in first
            {
                sw.Set(accent.Key);
                field.FocusBox();
                return;
            }
            s.Accent = v;
            Settings.Save();
            Theme.Apply(s.Theme); // every window repaints; this list is rebuilt by its window
        }
        sw.Picked += PickAccent;
        Add(Tr("색", "Colour"), Tr($"{accent.Name} · 강조색과 전체 톤이 함께 바뀝니다", $"{accent.Name} · the accent and the overall tone change together"), sw, G.Palette, () =>
        {
            string next = sw.Cycle();
            if (next == Accents.CustomKey && !customOk) // nothing typed in: round to the first
            {
                next = Accents.All[0].Key;
                sw.Set(next);
            }
            PickAccent(next);
        }, d =>
        {
            PickAccent(sw.Step(d));
            return true;
        }, filter);
        field.Applied += code =>
        {
            s.AccentCustom = code;
            s.Accent = Accents.CustomKey;
            Settings.Save();
            Theme.Apply(s.Theme);
        };
        Add(Tr("직접 고른 색", "Custom colour"), Tr("#RRGGBB 로 적습니다 (예: #3A7BD5) · 라이트는 그 색, 다크는 같은 색을 밝게", "Type #RRGGBB (like #3A7BD5) · the light theme uses it, the dark one a lighter take"), field, G.Palette, field.FocusBox, null, filter);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Choice(Tr("크기", "Size"), Tr("런처 · 도구 창 · AI 채팅 · 설정 창을 함께 · 런처에서 Ctrl+= / Ctrl+- / Ctrl+0", "The launcher, tool windows, chat and settings together · Ctrl+= / Ctrl+- / Ctrl+0 in the launcher"), G.Screen,
            Theme.Scales.Select(x => (x.ToString(inv), $"{x * 100:0}%")).ToArray(), Theme.Scale.ToString(inv), v => Theme.SetScale(double.Parse(v, inv)), filter);
        void ResetSize()
        {
            host.Window.ResetSize();
            Build(lastFilter); // the size on the line
        }
        var reset = ToolUi.TextButton(Tr("기본 크기로 되돌리기", "Reset to default"), ResetSize);
        reset.VerticalAlignment = VerticalAlignment.Center;
        Add(Tr("런처 창 크기", "Launcher window size"),
            Tr($"지금 {s.Width} × {s.Height} · 기본 {Settings.DefaultWidth} × {Settings.DefaultHeight} · 런처의 가장자리나 모서리를 끌어서 바꿉니다", $"Now {s.Width} × {s.Height} · default {Settings.DefaultWidth} × {Settings.DefaultHeight} · drag the launcher's edges or corners to change it"),
            reset, G.Screen, ResetSize, null, filter);

        Header(Tr("즐겨찾기 아이콘", "Favorite icons"));
        Choice(Tr("아이콘 스타일", "Icon style"), Tr("그림 아이콘 배경", "Picture icon background"), G.Palette,
            new[] { ("soft", Tr("부드러운 장미색", "Soft rose")), ("tile", Tr("중립색", "Neutral")), ("card", Tr("카드", "Card")), ("black", Tr("검정", "Black")) }, s.IconStyle, v =>
            {
                s.IconStyle = v;
                host.Window.Refresh(true);
            }, filter);
        int favs = Favorites.All().Count(), monos = Favorites.MonoCount;
        Add(Tr("그림 아이콘 만들기", "Make picture icons"), Tr($"즐겨찾기 {favs}개에 이름에 맞는 그림과 검정 바탕, 강조색을 적용합니다. 폴더 안의 항목도 포함됩니다.", $"Applies matching pictures, black tiles and the accent color to {favs} favorites, including folder items."), null, G.Star, favs == 0 ? null : () =>
        {
            host.Engine.MakePictureIcons();
            host.Window.Refresh(true);
            Build(lastFilter);
        }, null, filter);
        Add(Tr("AI로 전부 그리기", "Draw them all with AI"), drawing ?? Tr($"즐겨찾기 {favs}개 아이콘을 AI로 생성합니다. 사용량 {favs}회.", $"Creates AI icons for {favs} favorites · {favs} uses."), null, G.Sparkle, favs == 0 || drawing is not null ? null : async () =>
        {
            drawing = Tr("그리는 중…", "Drawing…");
            Build(lastFilter);
            await host.Engine.DrawAllIcons((d, n, bad) =>
            {
                drawing = d < n ? Tr($"그리는 중… {d}/{n}", $"Drawing… {d}/{n}") + (bad > 0 ? Tr($" · 실패 {bad}", $" · {bad} failed") : "") : null;
                Build(lastFilter);
                host.Window.Refresh(true);
            });
            drawing = null;
            Build(lastFilter);
        }, null, filter);
        if (monos > 0)
            Add(Tr("원래 아이콘으로", "Back to the original icon"), Tr($"그림 아이콘 {monos}개를 원래 앱 아이콘으로 복원합니다.", $"Restores original app icons for {monos} picture icons."), null, G.Refresh, () =>
            {
                Favorites.ClearMono();
                host.Window.Refresh(true);
                Build(lastFilter);
            }, null, filter);
    }

    private void PageHome(Settings s, string filter)
    {
        Page("home", Tr("홈 화면", "Home"), G.Star, Tr("검색줄이 비어 있을 때 보이는 블록 — 켜고 끄기, 끌어서 · ▲▼ · Ctrl+↑↓로 순서 (런처의 ‘홈 편집’에서도)", "The blocks under an empty search line — switch them, drag or ▲▼ / Ctrl+↑↓ to order (also ‘Edit home’ in the launcher)"), filter);
        Header(Tr("블록", "Blocks"));
        foreach (var w in Home.Order().ToList())
        {
            var kind = Home.Kinds.First(k => k.key == w.Key);
            string key = w.Key;
            var hsw = Toggle(w.On, v => Home.Set(key, v), out var hToggle);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var grip = Theme.Glyph('\uE700', 12, Theme.P.TextFaint);
            grip.VerticalAlignment = VerticalAlignment.Center;
            grip.Margin = new Thickness(0, 0, 8, 0);
            grip.ToolTip = Tr("끌어서 순서 바꾸기", "Drag to reorder");
            ctl.Children.Add(grip);
            ctl.Children.Add(Arrow('\uE70E', Tr("위로 (Ctrl+↑)", "Up (Ctrl+↑)"), () => MoveHome(key, -1)));
            ctl.Children.Add(Arrow('\uE70D', Tr("아래로 (Ctrl+↓)", "Down (Ctrl+↓)"), () => MoveHome(key, 1)));
            hsw.Margin = new Thickness(10, 0, 0, 0);
            ctl.Children.Add(hsw);
            string about = key != "usage" || collected is not null ? kind.about
                : AiUsage.Available ? Tr("5시간 · 주간 사용량 · 켜면 메모리 약 +2 MB", "5-hour and weekly usage · about +2 MB of memory when on")
                : kind.about + Tr(" — Claude Code · Codex가 없습니다", " — neither Claude Code nor Codex found");
            // only a row that shows gets its key and the drag (other categories add nothing)
            if (Add(kind.name, about, ctl, kind.glyph, () =>
            {
                if (justDragged) justDragged = false; // the release that ends a drag is not a click
                else hToggle();
            }, null, filter) is not { } homeRow) continue;
            homeKeys[rows.Count - 1] = key;
            if (filter.Length == 0) WireDrag(homeRow, key);
        }
        Header(Tr("AI 사용량", "AI usage"));
        Choice(Tr("새로 받는 간격", "Refresh every"), Tr("받을 때만 창 없이: Claude 약 470 MB 4초 · Codex 약 110 MB 1초", "Hidden, only while asked: Claude ~470 MB for 4 s · Codex ~110 MB for 1 s"), G.Refresh,
            new[] { ("5", Tr("5분", "5 min")), ("10", Tr("10분", "10 min")) }, s.UsageMinutes == 5 ? "5" : "10", v =>
            {
                s.UsageMinutes = v == "5" ? 5 : 10;
                AiUsage.Schedule();
            }, filter);
        Choice(Tr("보여 줄 값", "Show"), Tr("쓴 만큼(32%) 또는 남은 만큼(68% 남음) · 막대도 같이 바뀝니다", "What's used (32%) or what's left (68% left) · the bars follow"), G.Model,
            new[] { ("used", Tr("쓴 만큼", "Used")), ("left", Tr("남은 만큼", "Left")) }, s.UsageLeft ? "left" : "used", v =>
            {
                s.UsageLeft = v == "left";
                host.Window.Refresh(true);
            }, filter);
    }

    private void PageSearch(Settings s, string filter)
    {
        Page("search", Tr("검색", "Search"), G.Search, Tr("빈 검색줄에 입력할 때 앱 말고 무엇을 함께 찾을지", "What else to search besides apps as you type"), filter);
        Header(Tr("함께 찾기", "Also search"));
        var filesSw = Toggle(s.FilesInRoot, v => s.FilesInRoot = v, out var filesT);
        Add(Tr("파일도 함께 찾기", "Files too"), host.Everything.Running ? Tr("Everything 결과를 위에서 5개까지", "Up to 5 Everything results") : Tr("Everything이 꺼져 있습니다 — 대신 최근 파일에서 검색합니다", "Everything is not running — recent files are searched instead"), filesSw, G.Search, filesT, null, filter);
        bool ev = host.Everything.Running;
        Add("Everything", ev ? Tr("실행 중 · 파일 이름으로 PC 전체를 바로 검색합니다", "Running · finds any file on the PC by name") : Tr("실행 안 됨 · Enter로 실행", "Not running — Enter starts it"), Status(ev), G.Search, ev ? null : () =>
        {
            if (EverythingClient.ExePath() is { } exe) Launch.Open(exe, "-startup");
        }, null, filter);
        var winsSw = Toggle(s.WindowsInRoot, v => s.WindowsInRoot = v, out var winsT);
        Add(Tr("열린 창도 함께 찾기", "Open windows too"), Tr("제목이 맞는 창을 결과 아래에 3개까지 · Enter로 그 창으로", "Up to 3 windows whose title matches, under the results · Enter switches to it"), winsSw, G.NewWindow, winsT, null, filter);
        Header(Tr("학습", "Learning"));
        var learnSw = Toggle(s.LearnQueries, v => s.LearnQueries = v, out var learnT);
        Add(Tr("선택한 결과 우선 표시", "Prioritize selected results"), Tr("같은 말을 다시 적으면 지난번에 고른 걸 맨 위로 (“c” → Chrome) · 이 PC에만 저장", "Typing the same again puts your last pick first (“c” → Chrome) · kept on this PC only"), learnSw, G.Bulb, learnT, null, filter);
        int learned = QueryMemory.Count;
        Add(Tr("검색 학습 기록 지우기", "Clear search learning history"), learned == 0 ? Tr("기록 없음", "Nothing yet") : Tr($"입력 {learned}개 · Enter로 지우기", $"{learned} typed lines · Enter clears them"), null, G.Delete, learned == 0 ? null : () =>
        {
            QueryMemory.Clear();
            Build(lastFilter);
        }, null, filter);
        Header(Tr("웹 검색", "Web search"));
        Menu(Tr("검색 · 링크를 열 브라우저", "Browser for searches and links"), Tr("이 PC에 설치된 브라우저 중에서 · 웹 검색과 링크를 이 브라우저로 바로 엽니다", "From the browsers on this PC · web searches and links open in it"), G.Globe,
            collected is null ? Web.Choices.Select(c => (c.value, c.label, (string?)null)).ToArray() : [], collected is null ? Web.Picked?.Key ?? "default" : "", v => s.WebBrowser = v, filter); // collecting looks up no browsers
        Choice(Tr("검색 엔진", "Search engine"), null, G.Search, Web.Engines.Select(e => (e.key, e.name)).ToArray(), s.SearchEngine, v => s.SearchEngine = v, filter);
        Add(Tr("사이트 바로 가기", "Site shortcuts"), Tr($"단축어 + 스페이스 + 검색어 (예: yt 고양이) · {Quicklinks.Current.Items.Count}개 · Enter로 런처에서 관리", $"Keyword + space + words (e.g. yt cats) · {Quicklinks.Current.Items.Count} · Enter manages them in the launcher"), null, G.Link, () => host.OpenView(App.Mode.Quicklinks), null, filter);
        Header(Tr("이름", "Names"));
        int renamed = AppNames.Count;
        Add(Tr("Bloom에서 바꾼 앱 이름", "App names changed in Bloom"), renamed == 0 ? Tr("앱에서 Ctrl+K → 이름 바꾸기를 선택하세요. 원래 이름으로도 검색할 수 있습니다.", "On an app: Ctrl+K → Rename (in Bloom only; the original name still finds it)") : Tr($"{renamed}개 · Enter로 모두 원래 이름으로", $"{renamed} · Enter puts all the original names back"), null, G.Tag, renamed == 0 ? null : () =>
        {
            AppNames.ClearAll();
            host.AppsChanged();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageAi(Settings s, string filter)
    {
        Page("ai", Tr("AI 채팅", "AI chat"), G.Chat, Tr("Tab으로 AI 채팅 열기 · 이 PC에서 로그인한 Claude나 ChatGPT 또는 API 키로 답변", "Tab opens AI chat · uses Claude or ChatGPT signed in on this PC, or an API key"), filter);
        Header(Tr("기본", "Default"));
        List<string> apiIds = collected is null ? ApiCatalog.Ids() : []; // collecting reads no keys
        var sides = new List<(string, string)> { ("claude", "Claude"), ("chatgpt", "ChatGPT") };
        // the API side stays listed while chosen, also with no model left: new chats then say so (AiModels.ApiDefault)
        if (apiIds.Count > 0 || s.AiProvider == "api") sides.Add(("api", "API"));
        string side = s.AiProvider is "chatgpt" or "api" ? s.AiProvider : "claude";
        Choice(Tr("새 대화는", "New chats use"), Tr("Ctrl+Shift+M으로 AI 변경 · Ctrl+M으로 모델 선택", "Ctrl+Shift+M switches during a chat · Ctrl+M picks the model"), G.Sparkle,
            sides.ToArray(), side, v =>
            {
                s.AiProvider = v;
                if (v == "api" && AiModels.ApiDefault() is { } api && api != AiModels.ApiUnset) s.ApiModel = api; // the model new chats will use, saved
            }, filter);
        Shortcut("chat", Tr("AI 채팅 창 단축키", "AI chat window shortcut"), Tr("AI 채팅 창 열기 · 앞에 있으면 최소화", "Opens the chat window anywhere · down again when it's in front"), G.Chat, s.ChatHotkey, v => s.ChatHotkey = v, filter);
        var tabSw = Toggle(s.TabContinuesLastThread, v => s.TabContinuesLastThread = v, out var tabT);
        Add(Tr("Tab으로 지난 대화 이어가기", "Tab continues the last chat"), Tr("30분 안에 사용한 대화 이어가기 · Shift+Tab은 새 대화", "Picks up a chat used in the last 30 min · Shift+Tab always starts a new one"), tabSw, G.Chat, tabT, null, filter);
        var titleSw = Toggle(s.AiTitles, v =>
        {
            s.AiTitles = v;
            host.Chats.EndAll(); // Claude reads its instructions when the session starts
        }, out var titleT);
        Duration(Tr("대화 메모리 해제 시간", "Release idle chat memory after"), IdleText, G.Clock, s.AiIdleMinutes, 24 * 60, DurationField.MinutesHours, v => s.AiIdleMinutes = v, filter);
        Add(Tr("AI로 대화 제목 생성", "Generate chat titles with AI"), Tr("[◆ Opus • Code ] 로그인 수정처럼 모델 · 분야 · 작업 · 새 작업을 시작할 때만 바뀝니다", "Model, field and task, like [◆ Opus • Code ] Fix the login · changes only when a new task starts"), titleSw, G.Tag, titleT, null, filter);

        Header("Claude");
        Choice(Tr("Claude 모델", "Claude model"), Tr("새 대화의 기본값 · 대화 중에는 Ctrl+M", "For new chats · Ctrl+M during a chat"), G.Model,
            AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), s.AiModel, v => s.AiModel = v, filter);
        Choice(Tr("Claude 생각 수준", "Claude thinking level"), Tr("빠름은 생각 끔 · Haiku에서는 설정 불가 · 대화 중 Ctrl+E", "Fast = thinking off · not for Haiku · Ctrl+E during a chat"), G.Bulb,
            AiModels.ClaudeEfforts.Select(e => (e, AiModels.EffortName(e))).ToArray(), s.AiEffort, v => s.AiEffort = v, filter);
        string? cli = collected is null ? ClaudeSession.FindCli(s.ClaudePath) : null; // collecting looks nowhere on the PC
        if (cli is not null && Demo.On) cli = Tr("설치됨", "Installed"); // recording mode: no path with the account name
        Add("Claude Code", cli ?? AiErrors.ClaudeMissing, cli is null ? ToolUi.TextButton(Tr("설치 안내", "Installation guide"), () => Launch.Open("https://docs.anthropic.com/en/docs/claude-code")) : null, G.Info, null, null, filter);

        Header("ChatGPT");
        IReadOnlyList<GptCatalog.Model> gpts = collected is null ? GptCatalog.All : [];
        string? gptSlug = collected is not null ? null : s.GptModel is { Length: > 0 } g && gpts.Any(m => m.Slug == g) ? g : GptCatalog.DefaultSlug();
        if (gpts.Count > 0)
            Menu(Tr("ChatGPT 모델", "ChatGPT model"), (GptCatalog.Find(gptSlug) is { } cur ? GptCatalog.About(cur) : Tr("Codex 기본 모델", "Codex's default model")) + Tr(" · 대화 중에는 Ctrl+M", " · Ctrl+M during a chat"), G.Model,
                gpts.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), gptSlug ?? gpts[0].Slug, v =>
                {
                    s.GptModel = v;
                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels and the line under it follow the model
                }, filter);
        else
            Add(Tr("ChatGPT 모델", "ChatGPT model"), Tr("Codex의 모델 목록이 아직 없음 — Codex를 실행하여 목록을 불러오세요", "No Codex model list yet — run Codex once to get it"), null, G.Model, null, null, filter);
        string gptId = gptSlug is null ? "chatgpt" : AiModels.Gpt(gptSlug);
        Choice(Tr("ChatGPT 생각 수준", "ChatGPT thinking level"), Tr("모델마다 단계가 다릅니다 · 대화 중 Ctrl+E", "The levels depend on the model · Ctrl+E during a chat"), G.Bulb,
            collected is null ? AiModels.Efforts(gptId).Select(e => (e, AiModels.EffortName(e))).ToArray() : [], collected is null ? AiModels.Fit(gptId, s.GptEffort) : "", v => s.GptEffort = v, filter); // collecting asks no model catalog
        string? codex = collected is null ? CodexSession.FindCli(s.CodexPath) : null;
        if (codex is not null && Demo.On) codex = Tr("설치됨", "Installed");
        Add("Codex", codex is null ? AiErrors.CodexMissing : Tr($"{codex} · ChatGPT 로그인으로 사용", $"{codex} · uses your ChatGPT login"), codex is null ? ToolUi.TextButton(Tr("설치 안내", "Installation guide"), () => Launch.Open("https://github.com/openai/codex")) : null, G.Info, null, null, filter);

        Header("API");
        if (apiIds.Count > 0)
        {
            string apiModel = AiModels.ApiDefault() is { } d && apiIds.Contains(d) ? d : apiIds[0]; // the one new chats use
            Menu(Tr("API 모델", "API model"), Tr("새 대화의 기본값 · 대화 중에는 Ctrl+M", "For new chats · Ctrl+M during a chat"), G.Model,
                apiIds.Select(id => (id, AiModels.Who(id), (string?)null)).ToArray(), apiModel, v =>
                {
                    s.ApiModel = v;
                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels follow the model's provider
                }, filter);
            if (AiModels.HasEffort(apiModel))
                Choice(Tr("API 생각 수준", "API thinking level"), Tr("기본은 모델 자체 설정 · 추론 모델에서만 사용 · 대화 중 Ctrl+E", "Default = the model's own · reasoning models only · Ctrl+E during a chat"), G.Bulb,
                    AiModels.Efforts(apiModel).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(apiModel, s.ApiEffort), v => s.ApiEffort = v, filter);
        }
        else
            Add(Tr("API 모델", "API model"), Tr("설정 > AI 계정에서 API 키를 추가하면 모델 선택 가능", "Add an API key in Settings > AI accounts to pick a model"), null, G.Model, () => Go("accounts"), null, filter);
    }

    private void PageMcp(Settings s, string filter)
    {
        Page("mcp", Tr("PC 작업 · MCP", "PC actions · MCP"), G.Bolt, Tr("AI 채팅에서 이 PC의 앱 실행, 파일 검색·이동, 타이머 설정을 사용할 수 있습니다.", "Use AI chat to open apps, find and move files, and set timers on this PC."), filter);
        Header(Tr("기본", "Default"));
        var pcSw = Toggle(s.AiTools, v =>
        {
            s.AiTools = v;
            host.Chats.EndAll(); // the next message starts its session with (or without) the tools
        }, out var pcT);
        Add(Tr("Claude 대화에서", "In Claude chats"), Tr("실행 전 AI 채팅에 승인 카드 표시 · 다음 메시지부터 적용", "Approval card in AI chat before actions · applies from the next message"), pcSw, G.Bolt, pcT, null, filter);
        var gptSw = Toggle(s.AiToolsGpt, v => s.AiToolsGpt = v, out var gptT);
        Add(Tr("ChatGPT 대화에서", "In ChatGPT chats"), Tr("실행 전 AI 채팅에 승인 카드 표시 · 다음 메시지부터 적용", "Approval card in AI chat before actions · applies from the next message"), gptSw, G.Bolt, gptT, null, filter);
        var apiSw = Toggle(s.AiToolsApi, v => s.AiToolsApi = v, out var apiT);
        Add(Tr("API 대화에서", "In API chats"), Tr("실행 전 AI 채팅에 승인 카드 표시 · 다음 메시지부터 적용", "Approval card in AI chat before actions · applies from the next message"), apiSw, G.Bolt, apiT, null, filter);
        Header(Tr("권한", "Permissions"));
        (string, string)[] rules = { (ToolPermissions.Ask, Tr("매번 묻기", "Ask each time")), (ToolPermissions.Allow, Tr("항상 허용", "Always allow")), (ToolPermissions.Off, Tr("사용 안 함", "Off")) };
        // `restart`: the rule decides whether a chat gets the tool at all, which a running CLI session fixed when it began
        void Rule(string action, string title, string sub, char glyph, bool restart = false) =>
            Choice(title, sub, glyph, rules, ToolPermissions.Of(s.Permissions, action), v =>
            {
                if (v == ToolPermissions.Ask) s.Permissions.Remove(action);
                else s.Permissions[action] = v;
                if (restart) host.Chats.EndAll();
                // 사용 안 함 ends the running MCP servers and a start under way at once, not at the next message
                if (action == ToolPermissions.Mcp && v == ToolPermissions.Off) ApiSession.CloseMcpAll();
            }, filter);
        Rule(ToolPermissions.ReadOutside, Tr("파일 읽기 (대화 폴더 밖)", "Read files (outside the chat's folder)"), Tr("API 대화 · 대화 폴더 밖만 묻습니다 · 내용 검색은 모든 대화에 적용", "API chats · asks only outside the chat's folder · also covers content search"), G.Document);
        Rule(ToolPermissions.Write, Tr("파일 쓰기", "Write files"), Tr("API 대화 · 새 파일 만들기, 파일 덮어쓰기", "API chats · make a file or replace one"), G.Save);
        Rule(ToolPermissions.Edit, Tr("파일 고치기", "Edit files"), Tr("API 대화 · 파일의 일부 바꾸기", "API chats · change part of a file"), G.Pen);
        Rule(ToolPermissions.Command, Tr("명령 실행", "Run commands"), Tr("API 대화 · 대화 폴더에서 PowerShell 실행", "API chats · PowerShell in the chat's folder"), G.Code);
        Rule(ToolPermissions.Mcp, Tr("다른 MCP 도구", "Other MCP tools"), Tr("API 대화 · 아래에서 켠 MCP 서버의 도구 · 끄면 서버를 시작 안 함", "API chats · tools of the MCP servers switched on below · Off starts none"), G.Plug);
        Rule(ToolPermissions.Pc, Tr("PC 조작 (Bloom 기능)", "PC actions (Bloom's tool)"), Tr("모든 대화 · 열기 · 메모 · 타이머 · 파일 · 클립보드 · 찾기는 묻지 않음", "Every chat · open, notes, timers, files, clipboard · look-ups never ask"), G.Bolt, restart: true);
        Header(Tr("할 수 있는 일", "What it may do"));
        void Group(string key, string title, string sub, char glyph)
        {
            var sw = Toggle(App.Mcp.BloomTools.GroupOn(key), v =>
            {
                if (v) s.AiToolsOff.Remove(key);
                else if (!s.AiToolsOff.Contains(key)) s.AiToolsOff.Add(key);
                host.Chats.EndAll();
            }, out var t);
            Add(title, sub, sw, glyph, t, null, filter);
        }
        Group("look", Tr("찾아보기", "Look up"), Tr("앱 · 파일 · 최근 파일 · 열린 창 · 타이머 · 사용자 명령 — 묻지 않고 바로 검색합니다", "Apps, files, recent files, open windows, timers, your commands — looked up without asking"), G.Search);
        Group("open", Tr("열기 · 실행", "Open · run"), Tr("앱 · 파일 · 사이트 열기, ‘10분 타이머’ ‘볼륨 30’ 같은 Bloom 명령, 사용자 명령 — 하기 전에 확인 창이 표시됩니다", "Open apps, files and sites; Bloom lines like ‘10 min timer’ or ‘volume 30’; your commands — asks first"), G.App);
        Group("bloom", Tr("메모 · 타이머 · 일정", "Notes · timers · calendar"), Tr("Bloom 메모에 적기 · 타이머 · 캘린더 일정 — 하기 전에 확인 창이 표시됩니다 · 적은 메모는 채팅 옆에 표시됩니다", "Write in Bloom's notes, start its timers, put events on its calendar — asks first · the note shows beside the chat"), G.Document);
        Group("files", Tr("파일 정리", "Files"), Tr("이름 바꾸기 · 옮기기 · 복사 · 휴지통 — 하기 전에 확인 창이 표시됩니다 · 휴지통에서 되살릴 수 있습니다", "Rename, move, copy, recycle — asks first · the Recycle Bin brings things back"), G.Folder);
        Group("clip", Tr("클립보드", "Clipboard"), Tr("클립보드 읽기 · 넣기 — 하기 전에 확인 창이 표시됩니다", "Read and write the clipboard — asks first"), G.Paste);
        Header(Tr("이 PC의 다른 MCP 서버", "Other MCP servers on this PC"));
        // read where each program keeps them (Claude Code, Claude Desktop, Cursor, VS Code, Windsurf, Codex, files
        // brought in), once per opening of this page
        mcpFound ??= collected is null ? McpCatalog.Found(s.McpFiles) : []; // collecting reads no program's settings files
        if (mcpFound.Count == 0 && collected is null)
            Add(Tr("등록된 서버 없음", "No servers found"), Tr("Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex에 등록한 MCP가 여기 표시됩니다", "MCP servers set up in Claude Code, Claude Desktop, Cursor, VS Code, Windsurf or Codex show here"), null, G.Plug, null, null, filter);
        foreach (var d in mcpFound)
        {
            string key = d.Key;
            var sw = Toggle(s.McpOn.Contains(key), v =>
            {
                if (v) s.McpOn.Add(key);
                else s.McpOn.Remove(key);
                host.Chats.EndAll(); // the next message starts with (or without) it
            }, out var t);
            string where = McpCatalog.Label(d.Source) + (d.Stdio ? Tr(" · 이 PC에서 실행", " · runs on this PC") : Tr(" · 웹 주소 · ChatGPT에서 사용 불가", " · a web address · not in ChatGPT"));
            Add(d.Name, where, sw, G.Plug, t, null, filter);
        }
        Add(Tr("MCP 설정 파일 가져오기…", "Bring in an MCP settings file…"), Tr("mcpServers가 적힌 .json (예: 프로젝트의 .mcp.json) — 위에서 못 찾은 서버가 있을 때", "A .json with mcpServers (like a project's .mcp.json) — for servers not found above"), null, G.Import, () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Tr("MCP 설정 (*.json)|*.json", "MCP settings (*.json)|*.json") };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            if (!s.McpFiles.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) s.McpFiles.Add(dlg.FileName);
            Settings.Save();
            mcpFound = null;
            Build(lastFilter);
        }, null, filter);
        Add(Tr("켜기 전에", "Before you switch one on"), Tr("대화를 시작할 때 함께 켜져서 첫 답이 조금 늦어지고, 도구 설명만큼 사용량이 늘어납니다 · 도구를 쓸 때마다 채팅에서 확인 창이 표시됩니다", "It starts with each chat (a slower first answer) and its tool list adds usage · every tool call asks in the chat"), null, G.Info, () =>
        {
            mcpFound = null; // Enter reads the list again
            Build(lastFilter);
        }, null, filter);

        Header(Tr("연결", "Connection"));
        Add(Tr("연결 방식", "How it connects"), Tr("이 PC의 Bloom AI 채팅에서 사용", "Used in Bloom AI chat on this PC"), null, G.Shield, null, null, filter);
        Add(Tr("쓰는 양", "What it costs"), Tr("요청마다 Claude 약 +500 · ChatGPT 약 +250 토큰 (켠 항목이 적을수록 줄어듦) · 쓰는 동안 메모리 약 10 MB", "Per request: Claude about +500, ChatGPT about +250 tokens (less with fewer kinds on) · about 10 MB of memory while in use"), null, G.Speed, null, null, filter);
    }

    private void PageFeatures(Settings s, string filter)
    {
        Page("features", Tr("기능", "Features"), G.Sliders, Tr("기능을 끄면 검색 결과, 명령 목록, 홈, 즐겨찾기에서 제외되고 단축키가 해제됩니다. 저장된 데이터는 유지됩니다.", "Switch off what you don't use — it leaves the results, the command list, the home and the favorites row, and lets go of its shortcut. Nothing saved is deleted"), filter);
        foreach (var (group, title) in new[] { ("ai", "AI"), ("search", Tr("검색줄에서", "In the search line")), ("tools", Tr("창 · 도구", "Windows · tools")) })
        {
            Header(title);
            foreach (var f in Features.All.Where(f => f.group == group))
            {
                string key = f.key;
                var sw = Toggle(Features.On(key), v =>
                {
                    Features.Set(key, v);
                    host.ApplyHotkeys();
                    if (key != "docs") return;
                    FileAssoc.Repair(); // Explorer's 연결 프로그램 follows the switch
                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // 기본 앱으로 정하기 with it
                }, out var t);
                Add(f.name, f.about, sw, f.glyph, t, null, filter);
                // Windows lets only the user make an app the default: its page, while Bloom is in its list
                if (key == "docs" && Features.On(key) && FileAssoc.IsRegistered())
                    Add(Tr("기본 앱으로 정하기", "Make it the default app"), Tr("Windows 설정에서 .md 파일을 열 앱으로 Bloom을 고르세요. 탐색기에서 두 번 클릭하면 Bloom 문서 창으로 열립니다.", "Pick Bloom for .md files in Windows Settings, and a double-click in Explorer opens them in Bloom's document window"), null, G.NewWindow, () => Launch.Open(FileAssoc.DefaultsPage), null, filter);
            }
        }
    }

    private void PageClipboard(Settings s, string filter)
    {
        Page("clipboard", Tr("클립보드", "Clipboard"), G.Paste, Tr("복사한 텍스트·이미지·파일을 저장하여 다시 붙여넣을 수 있습니다.", "Keeps the text, pictures and files you copy, to paste again"), filter);
        Header(Tr("기록", "History"));
        var clip = host.Clipboard;
        var clSw = Toggle(s.ClipHistory, v => { s.ClipHistory = v; host.ApplyHotkeys(); }, out var clT);
        Add(Tr("클립보드 기록", "Clipboard history"), s.ClipHistory ? Tr($"켜짐 · {clip.Items.Count}개 · 메모리 약 +2 MB부터, 이미지가 많을수록 증가 · 비밀번호 관리자가 숨긴 내용 제외", $"On · {clip.Items.Count} · from about +2 MB of memory, more with pictures · copies that password managers hide are never kept") : Tr("꺼짐 · 기록 저장 안 함 · 켜면 메모리 약 +2 MB부터", "Off · no history saved · on: from about +2 MB"), clSw, G.Paste, clT, null, filter);
        Duration(Tr("보관 기간", "Keep for"), v => (v <= 0 ? Tr("계속 보관", "Kept for good") : Tr($"{Words(v)} 동안 보관", $"Kept for {Words(v)}")) + Tr(" · 0이면 계속 · 고정한 항목은 늘 유지됩니다 · 최대 500개", " · 0 = for good · pinned ones always stay · 500 at most"),
            G.History, s.ClipKeepMinutes, 525_600, DurationField.MinutesHoursDays, v =>
            {
                s.ClipKeepMinutes = v;
                clip.KeepChanged();
            }, filter);
        Shortcut("clipboard", Tr("단축키", "Shortcut"), Tr("누르면 클립보드 기록이 바로 열립니다", "Opens the clipboard history right away"), G.Keyboard, s.ClipHotkey, v => s.ClipHotkey = v, filter);
        Header(Tr("정리", "Clean up"));
        Add(Tr("기록 모두 지우기", "Clear the history"), Tr("고정된 항목 유지", "Pinned entries stay"), null, G.Delete, () =>
        {
            clip.Clear();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageLens(Settings s, string filter)
    {
        Page("screen", "Bloom Lens", G.Page, Tr("화면 영역을 고른 뒤 할 일을 선택하세요 — AI에게 묻기 · 번역 · 글자 복사 · 실시간 번역 · 화면에 고정 · 캡처 (1~6)", "Pick a screen area, then what to do — ask AI, translate, copy text, live translation, pin, capture (1–6)"), filter);
        Header(Tr("단축키", "Shortcut"));
        Shortcut("lens", "Bloom Lens", Tr("영역을 고르면 할 일 메뉴가 표시됩니다", "After the area: a menu of what to do"), G.Page, s.LensHotkey, v => s.LensHotkey = v, filter);
        Shortcut("ask", Tr("바로 AI에게 묻기", "Ask AI right away"), Tr("영역을 고르면 그 화면을 붙인 채팅이 열립니다 (묻기마다 사용량 1회)", "After the area: a chat with that picture attached (1 use per question)"), G.Sparkle, s.AskHotkey, v => s.AskHotkey = v, filter);
        Shortcut("ocr", Tr("바로 글자 복사", "Copy text right away"), Tr("영역을 고르면 글자를 읽어 복사합니다 (PC 안에서만, 사용량 없음)", "After the area: reads and copies the text (on this PC, no usage)"), G.Copy, s.OcrHotkey, v => s.OcrHotkey = v, filter);
        Shortcut("translate", Tr("바로 번역", "Translate right away"), Tr("영역을 고르면 읽어서 번역합니다 (번역마다 사용량 1회)", "After the area: reads and translates it (1 use each)"), G.Globe, s.TranslateHotkey, v => s.TranslateHotkey = v, filter);
        Shortcut("live", Tr("바로 실시간 번역", "Live translation right away"), Tr("영역을 계속 읽어 바뀔 때만 번역합니다 (사용량 계속)", "Keeps reading the area and translates when it changes (ongoing usage)"), G.Refresh, s.LiveHotkey, v => s.LiveHotkey = v, filter);
        Shortcut("capture", Tr("바로 캡처", "Capture right away"), Tr("영역을 고르면 펜 · 형광펜 막대가 열립니다 — Enter로 복사, Ctrl+V로 붙여넣기", "After the area: a pen and highlighter bar opens — Enter copies, Ctrl+V pastes"), G.Camera, s.CaptureHotkey, v => s.CaptureHotkey = v, filter);
        Header(Tr("글자 읽기", "Reading text"));
        var ocCopy = Toggle(s.OcrCopy, v => s.OcrCopy = v, out var ocT);
        Add(Tr("읽은 글자 바로 복사", "Copy the text at once"), Tr("끄면 런처에서 고른 뒤에 복사합니다", "Off: copy it from the launcher"), ocCopy, G.Copy, ocT, null, filter);
        var langs = collected is null ? Ocr.WinOcr.Languages() : [];
        Choice(Tr("인식 언어", "Recognition language"), langs.Count == 0 ? Tr("설치된 인식 언어 없음 · Windows 언어 설정에서 추가하세요", "No recognition languages installed. Add one in Windows language settings.") : Tr($"설치된 인식기: {string.Join(", ", langs)}", $"Installed: {string.Join(", ", langs)}"), G.Keyboard,
            new[] { ("auto", Tr("자동", "Auto")) }.Concat(langs.Select(l => (l, LangName(l)))).ToArray(), s.OcrLanguage, v => s.OcrLanguage = v, filter);
        if (langs.Count == 0 && collected is null)
            Add(Tr("Windows 언어 설정 열기", "Open Windows language settings"), null, null, G.Keyboard, () => Launch.Open("ms-settings:regionlanguage"), null, filter);
        Header(Tr("번역 · AI 빠른 동작", "Translation · AI actions"));
        // collecting reads no keys or model lists: the CLI sides only
        string tp = collected is null ? Translator.Provider() : s.TranslateProvider == "chatgpt" ? "chatgpt" : "claude";
        string[] engines = collected is null ? Translator.Providers() : new[] { "claude", "chatgpt" };
        Choice(Tr("번역 엔진", "Translation engine"), Tr("AI 빠른 동작도 이 엔진을 사용합니다 · 번역 창에서도 바꿀 수 있습니다", "AI actions use this engine too · also switchable in the translation window"), G.Sparkle,
            engines.Select(e => (e, e == "api" ? "API" : Translator.Label(e))).ToArray(), tp, v => { s.TranslateProvider = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        if (tp == "claude")
            Choice(Tr("번역용 Claude 모델", "Claude model for translation"), Tr("Haiku는 생각 수준을 지원하지 않습니다.", "Haiku has no thinking level setting."), G.Model,
                AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), Translator.Model("claude"), v => { s.TranslateClaudeModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        else if (tp == "api")
            Menu(Tr("번역용 API 모델", "API model for translation"), Tr("가벼운 모델일수록 빠르고 비용이 적습니다", "Lighter models are faster and cost less"), G.Model,
                ApiCatalog.Ids().Select(id => (id, AiModels.Who(id), (string?)null)).ToArray(), Translator.Model("api"), v => { s.TranslateApiModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        else if (GptCatalog.All.Count > 0)
            Menu(Tr("번역용 ChatGPT 모델", "ChatGPT model for translation"), Tr("가벼운 모델일수록 빠르고 사용량이 적습니다", "Lighter models are faster and use less"), G.Model,
                GptCatalog.All.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), AiModels.GptSlug(Translator.Model("chatgpt")) ?? GptCatalog.All[0].Slug, v => { s.TranslateGptModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        var tLevels = Translator.Levels(tp);
        if (tLevels.Length > 0)
            Choice(Tr("번역 생각 수준", "Thinking level for translation"), Tr("낮을수록 빠르고 사용량이 적습니다. 번역 창에서도 변경할 수 있습니다.", "Lower is faster and uses less · also a chip in the translation window"), G.Bulb,
                tLevels.Select(e => (e, AiModels.EffortName(e))).ToArray(), Translator.Effort(tp)!, v => Translator.SetEffort(tp, v), filter);
        Choice(Tr("번역할 언어", "Translate into"), Tr("자동: 한국어는 영어로, 나머지는 한국어로", "Auto: Korean text to English, other text to Korean"), G.Globe, Translator.Targets, s.TranslateTo, v => s.TranslateTo = v, filter);
        Choice(Tr("실시간 번역 간격", "Live translation interval"), Tr("화면을 다시 읽는 간격 (읽기는 사용량 없음 · 글이 바뀌고 멈췄을 때만 번역)", "How often the screen is read again (reading uses nothing · it translates only when the text changed and settled)"), G.History,
            new[] { ("1", Tr("1초", "1 s")), ("1.5", Tr("1.5초", "1.5 s")), ("3", Tr("3초", "3 s")), ("5", Tr("5초", "5 s")) }, s.LiveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), v => s.LiveSeconds = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Header(Tr("캡처 저장본", "Saved captures"));
        var (shots, bytes) = collected is null ? Host.LensFiles() : (0, 0L); // a folder scan
        Add(Tr("캡처 저장본 폴더 열기", "Open the saved captures folder"), Tr($"{shots}장 · {Engine.Size(bytes)} · 이 PC에만 저장 · 최근 {Host.LensKeep}장만 보관", $"Pictures: {shots} · {Engine.Size(bytes)} · on this PC only · the last {Host.LensKeep} kept"), null, G.Folder, () =>
        {
            Directory.CreateDirectory(Host.LensDir);
            Launch.Open(Host.LensDir);
        }, null, filter);
        Add(Tr("캡처 저장본 모두 삭제", "Delete all saved captures"), Tr("화면에 고정한 그림은 유지 · AI 채팅의 그림 미리보기도 삭제", "Pinned pictures stay · picture previews in AI chats go too"), null, G.Delete, () =>
        {
            int n = host.ClearLensFiles();
            Build(lastFilter);
            Notices.Result(true, "Bloom Lens", Tr($"캡처 저장본 {n}장 삭제 완료", $"Saved captures deleted: {n}"), null);
        }, null, filter);
    }

    private void PageTools(Settings s, string filter)
    {
        Page("tools", Tr("도구", "Tools"), G.Stopwatch, Tr("창 전환 · 이모지 · AI 빠른 동작 · 타이머 · 컬러 피커의 단축키와 설정", "Shortcuts and options for switching windows, emoji, AI actions, timers and the colour picker"), filter);
        Header(Tr("창 · 이모지 · AI 빠른 동작", "Windows · emoji · AI actions"));
        Shortcut("windows", Tr("창 전환 단축키", "Switch windows shortcut"), Tr("열린 창 목록이 바로 표시됩니다", "Opens the list of open windows"), G.NewWindow, s.WindowsHotkey, v => s.WindowsHotkey = v, filter);
        Shortcut("emoji", Tr("이모지 · 기호 단축키", "Emoji & symbols shortcut"), Tr("이모지를 선택하고 Enter를 누르면 앞의 창에 붙여넣습니다.", "Choose a symbol and press Enter to paste into the active window."), '\uE76E', s.EmojiHotkey, v => s.EmojiHotkey = v, filter);
        Shortcut("quick", Tr("AI 빠른 동작 단축키", "AI actions shortcut"), Tr("아무 앱에서 글을 선택하고 누르면 요약 · 번역 · 다듬기 (동작마다 사용량 1회 · 엔진은 Bloom Lens > 번역)", "Select text in any app and press it: summarize, translate, polish (1 use per action · engine: Bloom Lens > Translation)"), G.Sparkle, s.QuickHotkey, v => s.QuickHotkey = v, filter);
        Header(Tr("항목 단축키", "Item shortcuts"));
        if (ItemKeys.All.Count == 0 && collected is null)
            Add(Tr("지정된 항목 없음", "None yet"), Tr("런처에서 앱 · 폴더 · 명령 · 창 배치를 고르고 Ctrl+K › 단축키 지정", "Pick an app, folder, command or layout in the launcher, then Ctrl+K › Set a shortcut"), null, G.Keyboard, null, null, filter);
        foreach (var k in collected is null ? ItemKeys.All.ToList() : []) // the user's own shortcuts are not settings lines
        {
            var item = k.Item;
            Shortcut("item:" + item.Id, item.Title, Tr("눌러서 바꾸기 · Backspace로 지우기", "Click to change · Backspace removes it"), G.Keyboard, k.Combo, v => ItemKeys.Set(item, v == "off" ? null : v), filter);
        }
        Header(Tr("도구 창", "Tool windows"));
        foreach (var (key, name, glyph) in ToolDock.Kinds)
        {
            string k = key;
            Add(name, Tr("런처에 붙이면 함께 표시됩니다. 떼면 별도 창으로 유지됩니다. Enter로 여세요.", "Sits beside the launcher and comes and goes with it · detached, it stays on its own · Enter opens it"), null, glyph, () => host.Tools.Open(k), null, filter);
        }
        Header(Tr("타이머 · 알림", "Timers · reminders"));
        var tSound = Toggle(s.TimerSound, v => s.TimerSound = v, out var tSoundT);
        Add(Tr("소리로도 알리기", "Play a sound too"), Tr("끝나면 오른쪽 아래 카드와 함께 Windows 알람 소리 (30초까지)", "With the card at the bottom right, Windows' alarm sound (up to 30 s)"), tSound, G.Stopwatch, tSoundT, null, filter);
        Add(Tr("타이머 · 알림 열기", "Open timers and reminders"), Timers.Current.Items.Count > 0 ? Tr($"진행 중 {Timers.Current.Items.Count}개", $"{Timers.Current.Items.Count} running") : Tr("진행 중인 것 없음", "Nothing running"), null, G.Stopwatch, () => host.OpenView(App.Mode.Timers), null, filter);
        Header(Tr("컬러 피커", "Color picker"));
        Shortcut("color", Tr("컬러 피커 단축키", "Color picker shortcut"), Tr("어디서든 화면 색 찍기", "Pick a colour anywhere"), G.Eyedrop, s.ColorHotkey, v => s.ColorHotkey = v, filter);
        var cCopy = Toggle(s.ColorCopy, v => s.ColorCopy = v, out var cCopyT);
        Add(Tr("찍은 색 바로 복사", "Copy the colour at once"), Tr("HEX(#ff5a8a)로 복사합니다", "Copies it as HEX (#ff5a8a)"), cCopy, G.Copy, cCopyT, null, filter);
        Add(Tr("최근 색 · 팔레트 지우기", "Clear recent colours and palettes"), Tr($"색 {ColorBook.Current.Recent.Count}개 · 팔레트 {ColorBook.Current.Palettes.Count}개", $"{ColorBook.Current.Recent.Count} colours · {ColorBook.Current.Palettes.Count} palettes"), null, G.Delete, () =>
        {
            ColorBook.Clear();
            Build(lastFilter);
        }, null, filter);
    }

    private void PageNotices(Settings s, string filter)
    {
        Page("notices", Tr("알림", "Notifications"), G.Bell, Tr("Claude Code · Codex 작업과 AI 사용 한도를 화면 구석에 표시합니다. 전체 화면 게임이나 발표 중에는 알림이 숨겨집니다.", "Shows Claude Code and Codex tasks and AI usage limits in a screen corner. Hidden during full-screen games and presentations."), filter);
        Header(Tr("알림", "Notifications"));
        var on = Toggle(s.Notices, v =>
        {
            s.Notices = v;
            Notices.Apply();
        }, out var onT);
        Add(Tr("작업 알림", "Work notifications"), s.Notices ? Tr("켜짐 · 메모리 약 +1 MB · 알릴 일은 아래에서 선택하세요", "On · about +1 MB of memory · pick what to tell below") : Tr("꺼짐 — 알림도 소리도 없음 · 켜면 메모리 약 +1 MB", "Off — no cards, no sounds · on: about +1 MB"), on, G.Bell, onT, null, filter);
        Menu(Tr("위치", "Position"), Tr("고르면 미리 보기 카드가 그 자리에 표시됩니다", "A preview card shows there when you pick"), G.Screen, new (string, string, string?)[]
        {
            ("top-left", Tr("왼쪽 위", "Top left"), null), ("top-center", Tr("가운데 위", "Top center"), null), ("top-right", Tr("오른쪽 위", "Top right"), null),
            ("bottom-left", Tr("왼쪽 아래", "Bottom left"), null), ("bottom-center", Tr("가운데 아래", "Bottom center"), null), ("bottom-right", Tr("오른쪽 아래", "Bottom right"), null),
        }, s.NoticeSpot, v =>
        {
            s.NoticeSpot = v;
            Notices.Preview(one: true);
        }, filter);
        Choice(Tr("크기", "Size"), null, G.Sliders, new[] { ("s", Tr("작게", "Small")), ("m", Tr("보통", "Medium")), ("l", Tr("크게", "Large")), ("xl", Tr("아주 크게", "X-large")) }, s.NoticeSize, v =>
        {
            s.NoticeSize = v;
            Notices.Preview(one: true);
        }, filter);
        Choice(Tr("보이는 시간", "Shown for"), Tr("마우스를 올리면 유지 · 승인 대기는 작업 재개까지", "Stays while hovered · approval cards stay until work resumes"), G.Stopwatch,
            new[] { ("4", Tr("4초", "4 s")), ("6", Tr("6초", "6 s")), ("10", Tr("10초", "10 s")), ("20", Tr("20초", "20 s")) }, s.NoticeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), v => s.NoticeSeconds = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Choice(Tr("효과음", "Sound"), Tr("알림마다 다른 짧은 소리", "A short sound of its own for each kind"), G.Volume, new[] { ("off", Tr("끔", "Off")), ("s", Tr("작게", "Low")), ("m", Tr("보통", "Medium")), ("l", Tr("크게", "Loud")) }, s.NoticeSound, v =>
        {
            s.NoticeSound = v;
            Chimes.Play("done");
        }, filter);
        Add(Tr("미리 보기", "Preview"), Tr("사용 중인 알림 미리 보기", "Preview enabled notifications"), null, G.Bell, () => Notices.Preview(), null, filter);
        Header(Tr("알림 종류", "Notification types"));
        void Kind(string title, string sub, char glyph, bool value, Action<bool> set)
        {
            var sw = Toggle(value, set, out var t);
            Add(title, sub, sw, glyph, t, null, filter);
        }
        Kind(Tr("진행 중", "Working"), Tr("작업이 시작되면 무엇을 하는지 (4초 넘게 걸릴 때)", "What a turn works on once it runs past 4 s"), G.Gears, s.NoticeStart, v => s.NoticeStart = v);
        Kind(Tr("작업 완료", "Done"), Tr("끝나면 걸린 시간과 함께 (10초 넘게 걸린 작업)", "When it ends, with how long it took (past 10 s)"), G.Done, s.NoticeDone, v => s.NoticeDone = v);
        Kind(Tr("승인 대기", "Needs approval"), Tr("승인 또는 답변이 필요할 때 표시 · 작업 재개까지 유지", "Shown when approval or an answer is needed · stays until work resumes"), G.Clock, s.NoticeWaiting, v => s.NoticeWaiting = v);
        Kind(Tr("알림 카드에서 승인", "Approve on the card"), Tr("Claude Code의 승인 요청을 카드에서 허용 · 거절 · 켜면 Claude Code 설정(settings.json)에 Bloom 훅을 추가하고, 끄면 뺍니다", "Allow or decline Claude Code's requests on the card · On adds Bloom's hook to Claude Code's settings.json, off takes it out"), G.Shield, s.NoticeApprove, v =>
        {
            s.NoticeApprove = v; // first: Bloom's start-up ClaudeHooks.Follow reads it in its turn on the file
            if (!ClaudeHooks.Set(v))
            {
                s.NoticeApprove = !v;
                Notices.Result(false, Tr("Bloom · 알림", "Bloom · Notifications"), Tr("Claude Code 설정을 바꾸지 못했습니다", "Couldn't change Claude Code's settings"), ClaudeHooks.SettingsFile);
                Dispatcher.BeginInvoke(() => Build(lastFilter)); // the switch shows what holds
                return;
            }
            ClaudeApprovals.Apply();
        });
        Kind(Tr("한도 임박", "Limit near"), Tr("5시간 · 주간 한도가 20% 남았을 때와 다 썼을 때", "5-hour · weekly limit at 20% left or when used up"), G.Warning, s.NoticeLimit, v => s.NoticeLimit = v);
        Kind(Tr("한도 복구", "Limit reset"), Tr("모자랐던 한도가 초기화되어 다시 찼을 때", "When a low limit is full again after its reset"), G.Sync, s.NoticeRecover, v => s.NoticeRecover = v);
    }

    private void PagePerformance(Settings s, string filter)
    {
        Page("performance", Tr("성능", "Performance"), G.Speed, Tr("화면 움직임과 투명 효과 조절", "Motion and transparency"), filter);
        Header(Tr("애니메이션", "Animation"));
        Choice(Tr("애니메이션", "Animation"), Tr("줄이기: 나타나고 사라지기만 · 끄기: 움직임 없음", "Reduced: fades only · Off: nothing moves"), G.Bolt,
            new[] { ("system", Tr("시스템", "System")), ("on", Tr("켜기", "On")), ("reduced", Tr("줄이기", "Reduced")), ("off", Tr("끄기", "Off")) }, s.Animation, v => s.Animation = v, filter);
        Choice(Tr("초당 애니메이션 횟수", "Animation frames per second"), Tr("낮출수록 GPU · CPU를 덜 사용합니다", "Lower uses less GPU and CPU"), G.Speed,
            new[] { ("30", Tr("30회", "30")), ("60", Tr("60회", "60")), ("0", Tr("최대", "Max")) }, s.AnimationFps is 30 or 60 ? s.AnimationFps.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0", v => s.AnimationFps = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Header(Tr("화면", "Display"));
        Choice(Tr("유리 효과", "Glass effect"), Tr("끄면 단색 배경으로 GPU를 덜 사용합니다", "Off: a solid background, lighter on the GPU"), G.Palette,
            new[] { ("system", Tr("시스템", "System")), ("off", Tr("끄기", "Off")) }, s.Transparency, v =>
            {
                s.Transparency = v;
                Theme.Apply(s.Theme);
            }, filter);
        Level(Tr("유리 투명도", "Glass transparency"), Tr("클수록 뒤 화면이 더 비칩니다. 유리 효과가 켜져 있을 때 적용됩니다.", "Higher shows more of the screen behind. Applies while the glass effect is on."), G.Palette,
            s.GlassTransparency, 15, 75, 5, v => $"{v}%", v =>
            {
                s.GlassTransparency = v;
                Theme.P.Glass(v);
            }, filter);
        var gpuSw = Toggle(s.GpuDrawing, v =>
        {
            s.GpuDrawing = v;
            AskRestart(Tr("GPU로 그리기 설정을 적용하려면 Bloom을 다시 시작해야 합니다.", "Restart Bloom to apply GPU drawing."));
        }, out var gpuT);
        // experimental since 10-14: GPU drawing froze the whole screen for seconds on one PC (Settings.GpuDrawing)
        Add(Tr("GPU로 그리기 (실험적)", "GPU drawing (experimental)"), s.GpuDrawing == (System.Windows.Media.RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly)
            ? Tr("GPU로 화면을 그립니다. 일부 PC에서는 화면이 끊기거나 몇 초 동안 멈출 수 있습니다. 끄면 CPU로 그리며 메모리를 약 30 MB 덜 씁니다.", "Draws the screen with the GPU. Some PCs may stutter or freeze for seconds. Off: the CPU draws, with about 30 MB less memory.")
            : Tr("다시 시작하면 바뀝니다", "Changes when Bloom restarts"), gpuSw, G.Screen, gpuT, null, filter);
        Header(Tr("메모리", "Memory"));
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Add(Tr("지금 Bloom이 쓰는 메모리", "Memory Bloom uses now"), Tr($"{me.PrivateMemorySize64 / 1048576.0:0} MB · 모두 끈 기준 약 80 MB, 켤 때 늘어나는 양은 각 설정에 적혀 있습니다", $"{me.PrivateMemorySize64 / 1048576.0:0} MB · about 80 MB with everything off; each setting says what it adds"), null, G.Speed, () => Build(lastFilter), null, filter);
    }

    private void PageAbout(Settings s, string filter)
    {
        Page("about", Tr("정보", "About"), G.Info, Tr("데이터와 버전", "Data and version"), filter);
        Header(Tr("데이터", "Data"));
        Add(Tr("데이터 폴더 열기", "Open the data folder"), Demo.On ? Tr("녹화용 데이터 폴더", "The recording's data folder") : Paths.Data, null, G.Folder, () => Launch.Open(Paths.Data), null, filter);
        Add(Tr("설정 내보내기", "Export settings"), Tr("설정 · 즐겨찾기 · 앱 이름 · 브라우저 바로 가기 · 최근 색을 파일 하나로", "Settings, favorites, app names, browser shortcuts and recent colours in one file"), null, G.Send, () => host.ExportSettings(Window.GetWindow(this)), null, filter);
        Add(Tr("Bloom 초기화", "Reset Bloom"), Tr("모든 설정과 기록을 지우고 처음 설정 화면부터 다시 · 지우기 전에 내보낼지 확인 창이 표시됩니다", "Deletes all settings and history and starts from the first-run screens · asks to export first"), null, G.Delete, () => host.ResetAll(Window.GetWindow(this)), null, filter);
        Add(Tr("설정 불러오기", "Import settings"), Tr("내보낸 설정 불러오기 · 기존 설정은 데이터 폴더에 백업", "Imports exported settings · backs up current settings in the data folder"), null, G.Import, () => host.ImportSettings(Window.GetWindow(this)), null, filter);
        Add(Tr("Bloom 삭제", "Uninstall Bloom"), Tr("이 PC에서 Bloom을 삭제합니다. 대화와 설정의 삭제 여부를 선택할 수 있습니다.", "Removes Bloom from this PC · asks once more about your chats and settings"), null, G.Delete, () => host.UninstallBloom(Window.GetWindow(this)), null, filter);
        Header(Tr("버전", "Version"));
        string ver = typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "";
        Add($"Bloom {ver}", Tr("Windows 런처", "Windows launcher"), null, G.Info, null, null, filter);
        Add(Tr("이번 버전에서 바뀐 점", "What's new in this version"), Tr("업데이트한 뒤 처음 켤 때도 한 번 표시합니다 · Enter로 보기", "Also shown once at the first start after an update · Enter shows it"), null, G.Document, () => host.ShowWhatsNew(), null, filter);
        UpdateRows(s, filter);
        Add(Tr("Bloom 둘러보기", "Bloom tour"), Tr("처음 설정과 사용법 안내를 다시 보기", "See the first-run setup and the guide again"), null, G.Info, () => host.ShowOnboarding(), null, filter);
        Add(Tr("문제 보고", "Problem report"), Tr("버전 · Windows · 최근 로그를 복사하고 GitHub 이슈 작성 페이지를 엽니다 · 사용자 이름과 이메일 주소는 가려지며 Bloom이 직접 보내는 것은 없습니다", "Copies the version, Windows and recent log (user name and e-mail addresses hidden) and opens a GitHub issue · Bloom sends nothing itself"), null, G.Warning, () =>
        {
            Launch.Copy(ProblemReport.Build());
            Web.Open(ProblemReport.IssueUrl());
            Notices.Result(true, Tr("문제 보고", "Problem report"), Tr("진단 정보 복사 완료", "Report copied"), Tr("열린 페이지에 붙여넣으세요 (Ctrl+V)", "Paste it into the page that opened (Ctrl+V)"));
        }, null, filter);
    }

    /// A switch that only takes hold at the next start (언어, GPU로 그리기): asks whether to restart now.
    private void AskRestart(string question) => Dispatcher.BeginInvoke(() =>
    {
        if (ConfirmWindow.Ask(Window.GetWindow(this), Tr("Bloom 다시 시작", "Restart Bloom"), question, (Tr("다시 시작", "Restart"), MessageBoxResult.Yes), (Tr("나중에", "Later"), MessageBoxResult.No)) == MessageBoxResult.Yes) host.Restart("--settings");
        else Build(lastFilter); // the line under it says it changes at the restart
    });

    /// Minutes in words for the lines under a time: "1일 2시간", "45분".
    private static string Words(int minutes)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (minutes / 1440 > 0) parts.Add(Tr($"{minutes / 1440}일", $"{minutes / 1440} d"));
        if (minutes / 60 % 24 > 0) parts.Add(Tr($"{minutes / 60 % 24}시간", $"{minutes / 60 % 24} h"));
        if (minutes % 60 > 0) parts.Add(Tr($"{minutes % 60}분", $"{minutes % 60} min"));
        return string.Join(" ", parts);
    }

    /// 쉬는 대화 닫기's line: when a chat's CLI closes and what that means.
    private static string IdleText(int minutes) => minutes <= 0
        ? Tr("해제 안 함 · 대화마다 150~300 MB 사용", "Never release · 150–300 MB per chat")
        : Tr($"{Words(minutes)} 동안 쓰지 않은 대화의 메모리 해제 · 기록 유지 · 다음 첫 답변이 몇 초 늦음 · 0이면 해제 안 함", $"Frees a chat's memory after {Words(minutes)} unused · history stays · the next first answer starts a few seconds later · 0 = never");

    /// 업데이트 확인: what the last look found (a click looks now, or restarts into a downloaded version); a copy not
    /// installed with the setup file opens the releases page instead. Under it, whether Bloom looks once at each start.
    private void UpdateRows(Settings s, string filter)
    {
        var now = Updates.Now;
        string sub = now switch
        {
            Updates.Stage.NotInstalled => Tr("자동 업데이트는 설치 파일로 설치한 Bloom에서 사용 가능 · 릴리스 페이지 열기", "Automatic updates require a setup installation · open releases"),
            Updates.Stage.Checking => Tr("확인하는 중…", "Checking…"),
            Updates.Stage.Latest => Tr("최신 버전", "Up to date"),
            Updates.Stage.Downloading => Tr($"새 버전({Updates.Found})을 받는 중…", $"Downloading {Updates.Found}…"),
            Updates.Stage.Ready => Tr($"새 버전 {Updates.Found} 준비됨 · 클릭하여 다시 시작", $"Version {Updates.Found} ready · click to restart"),
            Updates.Stage.Failed => Tr("업데이트 확인 실패 · 인터넷 연결을 확인하고 다시 누르세요", "Could not check for updates. Check your connection and retry."),
            _ => Tr("GitHub 릴리즈에서 새 버전을 검색합니다", "Looks for a new version on GitHub Releases"),
        };
        Add(now == Updates.Stage.Ready ? Tr("다시 시작해서 업데이트", "Restart to update") : Tr("업데이트 확인", "Check for updates"), sub, null, G.Refresh, () =>
        {
            switch (Updates.Now)
            {
                case Updates.Stage.NotInstalled:
                    Launch.Open(Updates.Page);
                    break;
                case Updates.Stage.Ready:
                    host.UpdateNow();
                    break;
                default:
                    _ = Updates.Check(true);
                    break;
            }
        }, null, filter);
        var checkSw = Toggle(s.UpdateCheck, v => s.UpdateCheck = v, out var checkT);
        Add(Tr("켤 때 업데이트 확인", "Check when Bloom starts"), Tr("실행 후 한 번 확인 · 새 버전 다운로드 후 다음 실행 시 적용", "Checks once after launch · downloads new versions for the next start"), checkSw, G.Sync, checkT, null, filter);
    }
}
