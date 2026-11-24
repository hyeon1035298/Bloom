using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
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
        Choice("언어 · Language", Tr("언어 변경 시 Bloom 다시 시작 · 자동은 Windows 표시 언어 사용", "Changing language restarts Bloom · Auto uses the Windows display language"), G.Globe,
            Lang.Choices, s.Language, v =>
            {
                if (v == s.Language) return;
                s.Language = v;
                Settings.Save();
                if (!Lang.Pending(v)) return; // "auto" that lands on the language already shown
                AskRestart(Tr("언어를 적용하려면 Bloom을 다시 시작해야 합니다.", "Bloom needs to restart to apply the language change."));
            }, filter);
        Header(Tr("열기", "Open"));
        var winSw = Toggle(s.WinKey, v => { s.WinKey = v; host.ApplyHotkeys(); }, out var winT);
        string winSub = Tr("Win 키만 눌렀다 떼면 열립니다 (Win+E 같은 조합은 그대로)", "Opens when you press and release the Win key alone (shortcuts like Win+E work normally)");
        Add(Tr("Win 키로 열기", "Open with Win Key"), winSub, winSw, G.Keyboard, winT, null, filter);
        var altSw = Toggle(s.AltSpace, v => { s.AltSpace = v; host.ApplyHotkeys(); }, out var altT);
        // the recording mode shows what an installed copy says for these switches, not this run (no hooks, maybe a dev build)
        string altSub = Core.Demo.On ? (s.AltSpace ? Tr("켜짐", "On") : Tr("꺼짐", "Off"))
            : host.AltSpaceStatus switch { "taken" => Tr("다른 앱에서 사용 중", "In use by another app"), "ok" => Tr("켜짐", "On"), _ => Tr("꺼짐", "Off") };
        Add(Tr("Alt + Space로 열기", "Open with Alt + Space"), altSub, altSw, G.Keyboard, altT, null, filter);
        var autoSw = Toggle(s.Autostart, v =>
        {
            s.Autostart = v;
            Autostart.Set(v);
            LogonTask.SyncLater(); // the earlier start at sign-in for a taskbar add-on follows this switch
        }, out var autoT);
        bool autoOn = Core.Demo.On ? s.Autostart : Autostart.IsOn();
        Add(Tr("Windows 시작 시 실행", "Launch at Windows Startup"), Paths.IsDev && !Core.Demo.On ? Tr("개발 실행에서는 자동 시작 설정 불가", "Startup setting is unavailable in development builds") : autoOn ? Tr("로그인 시 트레이에서 실행", "Runs in the system tray at sign-in") : Tr("등록 안 됨", "Not registered"), autoSw, G.Power, autoT, null, filter);
        var open = new List<(bool, Action)>();
        if (ElevatedWinKey.Offered)
            open.Add((s.WinKeyAdmin != Defaults.WinKeyAdmin, () =>
            {
                // the switch follows the outcome: a declined UAC prompt leaves it off
                Switch? adminSw = null;
                adminSw = Toggle(s.WinKeyAdmin, async v => adminSw!.On = await host.SetWinKeyAdmin(v, Window.GetWindow(this)), out var adminT);
                Add(Tr("관리자 앱 위에서도 Win 키 사용", "Use Win Key Over Admin Apps"), Tr("관리자 권한 앱이 앞에 있어도 Win 키로 열립니다 · 켤 때 관리자 승인 필요", "Opens with the Win key even when an administrator app is in front · Requires administrator approval when turning on"), adminSw, G.Shield, adminT, null, filter);
            }
            ));
        More("open", filter, open);
        Header(Tr("모양", "Appearance"));
        Choice(Tr("테마", "Theme"), Tr("시스템은 Windows의 밝기 설정을 따릅니다", "System follows your Windows theme setting"), G.Palette,
            new[] { ("system", Tr("시스템", "System")), ("light", Tr("밝게", "Light")), ("dark", Tr("어둡게", "Dim")) }, s.Theme, v =>
            {
                s.Theme = v;
                Theme.Apply(v);
            }, filter);
        AccentRows(s, filter);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Choice(Tr("크기", "Size"), Tr("런처 · 도구 창 · AI 채팅 · 설정 창을 함께 · 런처에서 Ctrl+= / Ctrl+- / Ctrl+0", "Scales Launcher · Tool windows · AI Chat · Settings together · Ctrl+= / Ctrl+- / Ctrl+0 in Launcher"), G.Screen,
            Theme.Scales.Select(x => (x.ToString(inv), $"{x * 100:0}%")).ToArray(), Theme.Scale.ToString(inv), v => Theme.SetScale(double.Parse(v, inv)), filter);
        Choice(Tr("애니메이션", "Animations"), Tr("줄이기: 나타나고 사라지기만 · 끄기: 움직임 없음", "Reduce: Fade in and out only · Off: No motion"), G.Bolt,
            new[] { ("system", Tr("시스템", "System")), ("on", Tr("켜기", "Turn On")), ("reduced", Tr("줄이기", "Reduce")), ("off", Tr("끄기", "Turn off")) }, s.Animation, v => s.Animation = v, filter);
        bool glass = s.Transparency != "off";
        var glassSw = Toggle(glass, v =>
        {
            s.Transparency = v ? "system" : "off";
            Theme.Apply(s.Theme);
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // 유리 투명도 shows only with it on
        }, out var glassT);
        Add(Tr("유리 효과", "Glass Effect"), Tr("Windows 투명 효과를 따릅니다. 끄면 단색 배경입니다.", "Follows Windows transparency effects. Uses a solid background when turned off."), glassSw, G.Palette, glassT, null, filter);
        void ResetSize()
        {
            host.Window.ResetSize();
            Build(lastFilter); // the size on the line
        }
        var look = new List<(bool, Action)>
        {
            (s.Width != Settings.DefaultWidth || s.Height != Settings.DefaultHeight, () =>
            {
                var reset = ToolUi.TextButton(Tr("기본 크기로 되돌리기", "Reset to Default Size"), ResetSize);
                reset.VerticalAlignment = VerticalAlignment.Center;
                Add(Tr("런처 창 크기", "Launcher Window Size"),
                    Tr($"지금 {s.Width} × {s.Height} · 기본 {Settings.DefaultWidth} × {Settings.DefaultHeight} · 런처의 가장자리나 모서리를 끌어서 바꿉니다", $"Current {s.Width} × {s.Height} · Default {Settings.DefaultWidth} × {Settings.DefaultHeight} · Drag the edges or corners of Launcher to resize"),
                    reset, G.Screen, ResetSize, null, filter);
            }),
            (s.AnimationFps != Defaults.AnimationFps, () =>
                Choice(Tr("초당 애니메이션 횟수", "Animation Frame Rate"), Tr("낮출수록 GPU · CPU를 덜 사용합니다", "Lower frame rates use less GPU · CPU"), G.Speed,
                    new[] { ("30", Tr("30회", "30 FPS")), ("60", Tr("60회", "60 FPS")), ("0", Tr("최대", "Max")) }, s.AnimationFps is 30 or 60 ? s.AnimationFps.ToString(inv) : "0", v => s.AnimationFps = int.Parse(v, inv), filter)),
        };
        if (glass)
            look.Add((s.GlassTransparency != Defaults.GlassTransparency, () =>
                Level(Tr("유리 투명도", "Glass Transparency"), Tr("클수록 뒤 화면이 더 비칩니다", "Higher values let more of the background show through"), G.Palette,
                    s.GlassTransparency, 15, 75, 5, v => $"{v}%", v =>
                    {
                        s.GlassTransparency = v;
                        Theme.P.Glass(v);
                        OwnGlass.Follow();
                    }, filter)));
        look.Add((s.GpuDrawing != Defaults.GpuDrawing, () =>
        {
            var gpuSw = Toggle(s.GpuDrawing, v =>
            {
                s.GpuDrawing = v;
                AskRestart(Tr("GPU로 그리기 설정을 적용하려면 Bloom을 다시 시작해야 합니다.", "Bloom needs to restart to apply the GPU rendering setting."));
            }, out var gpuT);
            // experimental: GPU drawing froze the whole screen for seconds on one PC (Settings.GpuDrawing)
            Add(Tr("GPU로 그리기 (실험적)", "GPU Rendering (Experimental)"), s.GpuDrawing == (System.Windows.Media.RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly)
                ? Tr("GPU로 화면을 그립니다. 일부 PC에서는 화면이 끊기거나 몇 초 동안 멈출 수 있습니다. 끄면 CPU로 그리며 메모리를 약 30 MB 덜 씁니다.", "Renders the UI using the GPU. On some PCs, the screen may stutter or freeze for a few seconds. When off, renders on the CPU and uses ~30 MB less memory.")
                : Tr("다시 시작하면 바뀝니다", "Takes effect after restart"), gpuSw, G.Screen, gpuT, null, filter);
        }
        ));
        look.Add((false, () =>
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            Add(Tr("지금 Bloom이 쓰는 메모리", "Current Bloom Memory Usage"), Tr($"{me.PrivateMemorySize64 / 1048576.0:0} MB · 모두 끈 기준 약 80 MB, 켤 때 늘어나는 양은 각 설정에 적혀 있습니다", $"{me.PrivateMemorySize64 / 1048576.0:0} MB · ~80 MB with optional features off; additional usage is noted on each setting"), null, G.Speed, () => Build(lastFilter), null, filter);
        }
        ));
        More("look", filter, look);
    }

    private void PageLauncher(Settings s, string filter)
    {
        Page("launcher", Tr("런처", "Launcher"), G.Search, Tr("검색줄이 비었을 때의 홈과 검색 결과", "Home view when the search bar is empty, and search results"), filter);
        Header(Tr("홈 블록", "Home Blocks"), Tr("끌기, ▲▼, Ctrl+↑↓로 순서", "Reorder by dragging, ▲▼, or Ctrl+↑↓"));
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
            ctl.Children.Add(Arrow('\uE70E', Tr("위로 (Ctrl+↑)", "Move Up (Ctrl+↑)"), () => MoveHome(key, -1)));
            ctl.Children.Add(Arrow('\uE70D', Tr("아래로 (Ctrl+↓)", "Move Down (Ctrl+↓)"), () => MoveHome(key, 1)));
            hsw.Margin = new Thickness(10, 0, 0, 0);
            ctl.Children.Add(hsw);
            // the token blocks: the one thing to know on the line, the rest of what they show in its tooltip
            bool tokens = key.StartsWith("token", StringComparison.Ordinal);
            string about = tokens ? Tr("처음 켜면 기록을 한 번 읽습니다", "Reads history once when first turned on")
                : key != "usage" || collected is not null ? kind.about
                : AiUsage.Available ? Tr("5시간 · 주간 사용량 · 켜면 메모리 약 +2 MB", "5-hour · Weekly usage · Uses ~+2 MB memory when on")
                : kind.about + Tr(" — Claude Code · Codex가 없습니다", " — Claude Code · Codex not found");
            if (tokens) Tip(kind.about);
            // only a row that shows gets its key and the drag (other categories add nothing)
            if (Add(kind.name, about, ctl, kind.glyph, () =>
            {
                if (justDragged) justDragged = false; // the release that ends a drag is not a click
                else hToggle();
            }, null, filter) is not { } homeRow) continue;
            homeKeys[rows.Count - 1] = key;
            if (filter.Length == 0) WireDrag(homeRow, key);
        }

        Header(Tr("검색 결과", "Search Results"));
        bool ev = collected is null && host.Everything.Running; // collecting asks no program
        var filesSw = Toggle(s.FilesInRoot, v => s.FilesInRoot = v, out var filesT);
        FrameworkElement files = filesSw;
        if (!ev && collected is null && EverythingClient.ExePath() is { } everything)
        {
            var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var start = RowButton(Tr("Everything 실행", "Launch Everything"), () => Launch.Open(everything, "-startup"));
            start.Margin = new Thickness(0, 0, 10, 0);
            end.Children.Add(start);
            end.Children.Add(filesSw);
            files = end;
        }
        Add(Tr("파일도 함께 찾기", "Include Files in Search"), ev ? Tr("Everything 실행 중 · 위에서 5개까지", "Everything is running · Up to top 5 results") : Tr("Everything 꺼짐 · 최근 파일에서 찾음", "Everything is not running · Searches recent files"), files, G.Search, filesT, null, filter);
        if (!FeatureOff(filter, "windows"))
        {
            var winsSw = Toggle(s.WindowsInRoot, v => s.WindowsInRoot = v, out var winsT);
            Add(Tr("열린 창도 함께 찾기", "Include Open Windows in Search"), Tr("제목이 맞는 창을 결과 아래에 3개까지 · Enter로 그 창으로", "Up to 3 matching windows below results · Press Enter to switch to window"), winsSw, G.NewWindow, winsT, null, filter);
        }
        var learnSw = Toggle(s.LearnQueries, v => s.LearnQueries = v, out var learnT);
        Add(Tr("선택한 결과 우선 표시", "Prioritize Previously Selected Results"), Tr("같은 말을 다시 적으면 지난번에 고른 걸 맨 위로 (“c” → Chrome) · 이 PC에만 저장", "Moves your last pick to the top when you type the same query again (“c” → Chrome) · Stored on this PC only"), learnSw, G.Bulb, learnT, null, filter);
        PageHidden(filter);

        Header(Tr("웹 검색", "Web Search"));
        Choice(Tr("검색 엔진", "Search Engine"), null, G.Search, Web.Engines.Select(e => (e.key, e.name)).ToArray(), s.SearchEngine, v => s.SearchEngine = v, filter);
        Menu(Tr("링크를 열 브라우저", "Browser for Opening Links"), Tr("이 PC에 설치된 브라우저 중에서 · 웹 검색과 링크를 이 브라우저로 바로 엽니다", "Choose from browsers installed on this PC · Opens web searches and links directly in this browser"), G.Globe,
            collected is null ? Web.Choices.Select(c => (c.value, c.label, (string?)null)).ToArray() : [], collected is null ? Web.Picked?.Key ?? "default" : "", v => s.WebBrowser = v, filter); // collecting looks up no browsers
        if (!FeatureOff(filter, "quicklinks"))
            Add(Tr("사이트 바로 가기", "Site Shortcuts"), Tr($"단축어 + 스페이스 + 검색어 (예: yt 고양이) · {Quicklinks.Current.Items.Count}개 · Enter로 런처에서 관리", $"Keyword + Space + query (e.g. yt cat) · {Quicklinks.Current.Items.Count} shortcuts · Press Enter to manage in Launcher"), null, G.Link, () => host.OpenView(App.Mode.Quicklinks), null, filter);

        Header(Tr("즐겨찾기 아이콘", "Favorite Icons"));
        Choice(Tr("아이콘 스타일", "Icon Style"), Tr("그림 아이콘 배경", "Glyph icon background"), G.Palette,
            new[] { ("soft", Tr("부드러운 장미색", "Soft Rose")), ("tile", Tr("중립색", "Neutral")), ("card", Tr("카드", "Card")), ("black", Tr("검정", "Black")) }, s.IconStyle, v =>
            {
                s.IconStyle = v;
                host.Window.Refresh(true);
            }, filter);
        int favs = Favorites.All().Count(), monos = Favorites.MonoCount;
        Add(Tr("그림 아이콘 만들기", "Generate Glyph Icons"), Tr($"즐겨찾기 {favs}개에 이름에 맞는 그림과 검정 바탕, 강조색을 적용합니다. 폴더 안의 항목도 포함됩니다.", $"Applies matching glyphs, a black background, and your accent color to {favs} favorites, including items inside folders."), null, G.Star, favs == 0 ? null : () =>
        {
            host.Engine.MakePictureIcons();
            host.Window.Refresh(true);
            Build(lastFilter);
        }, null, filter);
        Add(Tr("AI로 아이콘 그리기", "Generate Icons with AI"), drawing ?? Tr($"즐겨찾기 {favs}개 아이콘을 AI로 생성합니다. 사용량 {favs}회.", $"Generates icons for {favs} favorites using AI. Uses {favs} requests."), null, G.Sparkle, favs == 0 || drawing is not null ? null : async () =>
        {
            drawing = Tr("그리는 중…", "Generating…");
            Build(lastFilter);
            await host.Engine.DrawAllIcons((d, n, bad) =>
            {
                drawing = d < n ? Tr($"그리는 중… {d}/{n}", $"Generating… {d}/{n}") + (bad > 0 ? Tr($" · 실패 {bad}", $" · {bad} failed") : "") : null;
                Build(lastFilter);
                host.Window.Refresh(true);
            });
            drawing = null;
            Build(lastFilter);
        }, null, filter);
        if (monos > 0)
            Add(Tr("원래 아이콘으로 되돌리기", "Restore Original Icons"), Tr($"그림 아이콘 {monos}개를 원래 앱 아이콘으로 복원합니다.", $"Restores {monos} custom icons to their original app icons."), null, G.Refresh, () =>
            {
                Favorites.ClearMono();
                host.Window.Refresh(true);
                Build(lastFilter);
            }, null, filter);
    }

    /// 설정 › 런처 › 숨긴 항목: what Ctrl+K › 검색에서 숨기기 took out of the search and the home, newest first, each with
    /// 되돌리기. The settings search finds the first line (collecting lists no items: their names change without a save).
    private void PageHidden(string filter)
    {
        Header(Tr("숨긴 항목", "Hidden Items"));
        bool live = collected is null;
        var hidden = live ? HiddenResults.List() : [];
        void Changed()
        {
            host.Window.Refresh(true);
            Build(lastFilter);
        }
        string sub = !live ? Tr("검색과 홈에서 뺀 결과와 되돌리기", "Items hidden from search and Home, with option to restore")
            : hidden.Count == 0 ? Tr("없음 · 결과에서 Ctrl+K › 검색에서 숨기기", "None · In results, press Ctrl+K › Hide from Search")
            : Tr($"{hidden.Count}개 · 검색과 홈에 나오지 않음", $"{hidden.Count} items · Hidden from search and Home");
        Add(Tr("숨긴 검색 결과", "Hidden Search Results"), sub, hidden.Count > 1 ? RowButton(Tr("모두 되돌리기", "Restore All"), () => { HiddenResults.Clear(); Changed(); }) : null, G.Hide, null, null, filter);
        foreach (var e in hidden)
        {
            string id = e.Id;
            Add(e.Title.Length > 0 ? e.Title : id, HiddenSub(HiddenKind(e.Kind), e.Sub), RowButton(Tr("되돌리기", "Show again"), () => { HiddenResults.Remove(id); Changed(); }), G.Hide, null, null, filter);
        }
    }

    /// A hidden item's kind in the line under its name.
    private static string HiddenKind(string kind) => kind switch
    {
        nameof(ItemKind.App) => Tr("앱", "App"),
        nameof(ItemKind.Command) => Tr("명령", "Command"),
        nameof(ItemKind.Setting) => Tr("설정", "Settings"),
        nameof(ItemKind.File) => Tr("파일", "File"),
        nameof(ItemKind.Folder) => Tr("폴더", "Folder"),
        nameof(ItemKind.Quicklink) => Tr("사이트 바로 가기", "Site Shortcuts"),
        nameof(ItemKind.Toggle) => Tr("시스템 스위치", "System Toggle"),
        _ => "",
    };

    private static string HiddenSub(string a, string? b) => a.Length == 0 ? b ?? "" : string.IsNullOrEmpty(b) ? a : $"{a} · {b}";

    /// 토큰 집계 지우기 (설정 > 데이터): asked first (what Claude Code already removed does not come back), then said done;
    /// the recording mode's numbers are made up, so there it does nothing.
    private void ClearTokens()
    {
        if (host.Opt.Demo) return;
        if (ConfirmWindow.Ask(Window.GetWindow(this), Tr("토큰 집계 지우기", "Clear Token Counts"),
                Tr("Bloom이 센 토큰 숫자를 지우고 남은 기록에서 다시 셉니다. Claude Code가 이미 지운 30일 넘은 기록은 돌아오지 않습니다.", "Clear token counts calculated by Bloom and recount from remaining history. Logs older than 30 days already deleted by Claude Code cannot be recovered."),
                (Tr("지우기", "Clear"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
        TokenStats.Reset();
        host.Window.Refresh(true);
        Notices.Result(true, Tr("AI 토큰", "AI Tokens"), Tr("토큰 집계 지우기 완료", "Token counts cleared"), null);
    }

    /// 검색 학습 기록 지우기 (설정 > 데이터): what the search learned from the picks.
    private void ClearSearchLearning()
    {
        QueryMemory.Clear();
        Build(lastFilter);
    }

    /// 바꾼 앱 이름 (설정 > 데이터): every app back to its own name.
    private void ClearAppNames()
    {
        AppNames.ClearAll();
        host.AppsChanged();
        Build(lastFilter);
    }

    /// 클립보드 기록 모두 지우기 (설정 > 데이터): the pinned entries stay.
    private void ClearClipboardHistory()
    {
        host.Clipboard.Clear();
        Build(lastFilter);
    }

    /// The saved captures' folder (설정 > 데이터).
    private static void OpenLensFolder()
    {
        Directory.CreateDirectory(Host.LensDir);
        Launch.Open(Host.LensDir);
    }

    /// 캡처 저장본 모두 삭제 (설정 > 데이터): pinned pictures stay; said done with how many went.
    private void ClearLensCaptures()
    {
        int n = host.ClearLensFiles();
        Build(lastFilter);
        Notices.Result(true, "Bloom Lens", Tr($"캡처 저장본 {n}장 삭제 완료", $"Deleted {n} saved captures"), null);
    }

    /// 최근 색 · 팔레트 지우기 (설정 > 데이터).
    private void ClearRecentColours()
    {
        ColorBook.Clear();
        Build(lastFilter);
    }

    private void PageShortcuts(Settings s, string filter)
    {
        Page("shortcuts", Tr("단축키", "Shortcuts"), G.Keyboard, Tr("어느 앱에서든 쓰는 단축키", "Global shortcuts available in any app"), filter);
        Header(Tr("창", "Windows"));
        if (!FeatureOff(filter, "chat"))
            Shortcut("chat", Tr("AI 채팅 창", "AI Chat Window"), Tr("AI 채팅 창 열기 · 앞에 있으면 최소화", "Open AI Chat window · Minimize if already in front"), G.Chat, s.ChatHotkey, v => s.ChatHotkey = v, filter);
        Shortcut("clipboard", Tr("클립보드 기록", "Clipboard History"), Tr("누르면 클립보드 기록이 바로 열립니다", "Opens Clipboard History directly"), G.Paste, s.ClipHotkey, v => s.ClipHotkey = v, filter);
        Shortcut("snippets", Tr("상용구", "Snippets"), Tr("누르면 클립보드의 상용구 탭이 열립니다. Enter로 앞의 창에 붙여넣습니다.", "Opens the Snippets tab in Clipboard. Press Enter to paste into the active window."), G.Bookmark, s.SnippetHotkey, v => s.SnippetHotkey = v, filter);
        if (!FeatureOff(filter, "windows"))
            Shortcut("windows", Tr("창 전환", "Window Switcher"), Tr("열린 창 목록이 바로 표시됩니다", "Shows the list of open windows right away"), G.NewWindow, s.WindowsHotkey, v => s.WindowsHotkey = v, filter);
        if (!FeatureOff(filter, "emoji"))
            Shortcut("emoji", Tr("이모지 · 기호", "Emoji · Symbols"), Tr("이모지를 선택하고 Enter를 누르면 앞의 창에 붙여넣습니다.", "Select an emoji and press Enter to paste it into the active window."), '\uE76E', s.EmojiHotkey, v => s.EmojiHotkey = v, filter);
        if (!FeatureOff(filter, "quick"))
            Shortcut("quick", Tr("AI 빠른 동작", "AI Quick Actions"), Tr("아무 앱에서 글을 선택하고 누르면 요약 · 번역 · 다듬기 (동작마다 사용량 1회 · 엔진은 AI 채팅 › 번역 · AI 빠른 동작)", "Select text in any app and press to summarize · translate · polish (1 usage per action · engine set in AI Chat › Translation · AI Quick Actions)"), G.Sparkle, s.QuickHotkey, v => s.QuickHotkey = v, filter);
        if (!FeatureOff(filter, "color"))
            Shortcut("color", Tr("컬러 피커", "Color Picker"), Tr("어디서든 화면 색 찍기", "Pick a color from anywhere on screen"), G.Eyedrop, s.ColorHotkey, v => s.ColorHotkey = v, filter);
        Header("Bloom Lens");
        if (!FeatureOff(filter, "lens"))
        {
            Shortcut("lens", "Bloom Lens", Tr("영역을 고르면 할 일 메뉴가 표시됩니다", "Select an area to open the action menu"), G.Page, s.LensHotkey, v => s.LensHotkey = v, filter);
            Shortcut("ask", Tr("바로 AI에게 묻기", "Ask AI Immediately"), Tr("영역을 고르면 그 화면을 붙인 채팅이 열립니다 (묻기마다 사용량 1회)", "Select an area to open a chat with that screenshot attached (1 usage per question)"), G.Sparkle, s.AskHotkey, v => s.AskHotkey = v, filter);
            Shortcut("ocr", Tr("바로 글자 복사", "Copy Text Immediately"), Tr("영역을 고르면 글자를 읽어 복사합니다 (PC 안에서만, 사용량 없음)", "Select an area to recognize and copy its text (on-device only, no usage cost)"), G.Copy, s.OcrHotkey, v => s.OcrHotkey = v, filter);
            Shortcut("translate", Tr("바로 번역", "Translate Immediately"), Tr("영역을 고르면 읽어서 번역합니다 (번역마다 사용량 1회)", "Select an area to recognize and translate its text (1 usage per translation)"), G.Globe, s.TranslateHotkey, v => s.TranslateHotkey = v, filter);
            Shortcut("live", Tr("바로 실시간 번역", "Live Translate Immediately"), Tr("영역을 계속 읽어 바뀔 때만 번역합니다 (사용량 계속)", "Continuously watches the area and translates whenever the text changes (ongoing usage)"), G.Refresh, s.LiveHotkey, v => s.LiveHotkey = v, filter);
            Shortcut("capture", Tr("바로 캡처", "Capture Immediately"), Tr("영역을 고르면 펜 · 형광펜 막대가 열립니다 — Enter로 복사, Ctrl+V로 붙여넣기", "Select an area to open the pen · highlighter toolbar — Enter to copy, Ctrl+V to paste"), G.Camera, s.CaptureHotkey, v => s.CaptureHotkey = v, filter);
            Shortcut("record", Tr("화면 녹화", "Screen Recording"), Tr("영역을 고르면 녹화 막대가 열립니다 · 녹화 중에 다시 누르면 멈춤", "Select an area to open the recording bar · Press again while recording to stop"), G.Video, s.RecordHotkey, v => s.RecordHotkey = v, filter);
        }
        Header(Tr("항목 단축키", "Item Shortcuts"));
        if (ItemKeys.All.Count == 0 && collected is null)
            Add(Tr("지정된 항목 없음", "No items assigned"), Tr("런처에서 앱 · 폴더 · 명령 · 창 배치를 고르고 Ctrl+K › 단축키 지정", "Select an app · folder · command · window layout in Launcher and press Ctrl+K › Assign Shortcut"), null, G.Keyboard, null, null, filter);
        foreach (var k in collected is null ? ItemKeys.All.ToList() : []) // the user's own shortcuts are not settings lines
        {
            var item = k.Item;
            Shortcut("item:" + item.Id, item.Title, Tr("눌러서 바꾸기 · Backspace로 지우기", "Click to change · Backspace to clear"), G.Keyboard, k.Combo, v => ItemKeys.Set(item, v == "off" ? null : v), filter);
        }
    }

    private void PageAi(Settings s, string filter)
    {
        Page("ai", Tr("AI 채팅", "AI Chat"), G.Chat, Tr("Tab으로 여는 AI 채팅과 번역, AI 빠른 동작", "AI Chat opened with Tab, translation, and AI Quick Actions"), filter);
        List<string> apiIds = collected is null ? ApiCatalog.Ids() : []; // collecting reads no keys
        string? omp = collected is null ? OmpCli.FindCli(s.OmpPath) : null; // collecting looks nowhere on the PC
        if (omp is not null) OmpCatalog.Ensure(); // the list from `omp models` arrives once (OnCatalog builds the page again)
        var sides = new List<(string, string)> { ("claude", "Claude"), ("chatgpt", "ChatGPT") };
        // omp is one to pick when it is on this PC (or already picked: new chats then say it is missing, AiErrors.OmpMissing)
        if (omp is not null || s.AiProvider == "omp") sides.Add(("omp", "omp"));
        // the API side stays listed while chosen, also with no model left: new chats then say so (AiModels.ApiDefault)
        if (apiIds.Count > 0 || s.AiProvider == "api") sides.Add(("api", "API"));
        string side = s.AiProvider is "chatgpt" or "omp" or "api" ? s.AiProvider : "claude";
        string? cli = collected is null ? ClaudeSession.FindCli(s.ClaudePath) : null;
        string? codex = collected is null ? CodexSession.FindCli(s.CodexPath) : null;

        Header(Tr("기본", "Default"));
        if (!FeatureOff(filter, "chat"))
        {
            Choice(Tr("새 대화의 AI", "AI for New Chats"), Tr("Ctrl+Shift+M으로 AI 변경 · Ctrl+M으로 모델 선택", "Ctrl+Shift+M to switch AI · Ctrl+M to select model"), G.Sparkle,
                sides.ToArray(), side, v =>
                {
                    s.AiProvider = v;
                    if (v == "api" && AiModels.ApiDefault() is { } api && api != AiModels.ApiUnset) s.ApiModel = api; // the model new chats will use, saved
                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // the model and its levels follow the AI
                }, filter);
            foreach (var (_, add) in SideRows(s, side, true, apiIds, filter)) add();
            // the picked AI's CLI missing: where to get it, in its place
            if (side == "claude" && cli is null)
                Add(Tr("Claude Code 설치 안내", "Claude Code Installation Guide"), AiErrors.ClaudeMissing, null, G.Info, () => Launch.Open("https://docs.anthropic.com/en/docs/claude-code"), null, filter);
            else if (side == "chatgpt" && codex is null)
                Add(Tr("Codex 설치 안내", "Codex Installation Guide"), AiErrors.CodexMissing, null, G.Info, () => Launch.Open("https://github.com/openai/codex"), null, filter);
            if (side == "omp") OmpSearchRow(s, filter);
            var tabSw = Toggle(s.TabContinuesLastThread, v => s.TabContinuesLastThread = v, out var tabT);
            Add(Tr("Tab으로 지난 대화 이어가기", "Resume Recent Chat with Tab"), Tr("30분 안에 사용한 대화 이어가기 · Shift+Tab은 새 대화", "Resumes a chat used within 30 minutes · Shift+Tab starts a new chat"), tabSw, G.Chat, tabT, null, filter);
            var titleSw = Toggle(s.AiTitles, v =>
            {
                s.AiTitles = v;
                host.Chats.EndAll(); // Claude reads its instructions when the session starts
            }, out var titleT);
            Add(Tr("AI로 대화 제목 생성", "Generate Chat Titles with AI"), Tr("대화 내용으로 제목을 짓습니다. 새 작업을 시작할 때만 바뀝니다.", "Generates a title from the conversation content. Updates only when starting a new task."), titleSw, G.Tag, titleT, null, filter);
        }
        // the add-ons' 보내기 · 정지 for T3 Code's threads go through Bloom (Ai\T3Agent.cs): off, Bloom asks T3 nothing
        var t3Sw = Toggle(s.T3Link, v =>
        {
            s.T3Link = v;
            App.T3Lines.Changed(); // the cards' 보내기 and 정지 come and go at once
        }, out var t3T);
        Add(Tr("T3 Code 연결", "T3 Code Connection"), Tr("작업 카드·작업표시줄 애드온에서 T3 Code 스레드에 보내고, 질문에 답하고, 정지합니다", "Send to T3 Code threads, answer questions, and stop runs from work cards and the taskbar add-on"), t3Sw, G.Link, t3T, null, filter);

        TranslationGroup(s, filter);

        // what new chats don't use now: the other AIs' models and levels (Ctrl+Shift+M switches to them), where the CLIs are
        Header(Tr("다른 AI와 고급", "Other AIs & Advanced"));
        var more = new List<(bool, Action)>();
        foreach (var (other, _) in sides.Where(x => x.Item1 != side))
            more.AddRange(SideRows(s, other, false, apiIds, filter));
        if (collected is not null && !sides.Exists(x => x.Item1 == "omp")) more.AddRange(SideRows(s, "omp", false, apiIds, filter)); // found by the search too
        more.Add((s.AiIdleMinutes != Defaults.AiIdleMinutes, () => Duration(Tr("대화 메모리 해제 시간", "Chat Memory Unload Timeout"), IdleText, G.Clock, s.AiIdleMinutes, 24 * 60, DurationField.MinutesHours, v => s.AiIdleMinutes = v, filter)));
        // recording mode: no path with the account name
        if (cli is not null)
            more.Add((false, () => Add("Claude Code", Demo.On ? Tr("설치됨", "Installed") : cli, null, G.Info, null, null, filter)));
        if (codex is not null)
            more.Add((false, () => Add("Codex", (Demo.On ? Tr("설치됨", "Installed") : codex) + Tr(" · ChatGPT 로그인으로 사용", " · Uses ChatGPT sign-in"), null, G.Info, null, null, filter)));
        OmpMore(s, omp, more, filter);
        More("advanced", filter, more);
    }

    /// The model and thinking level of one AI: under 기본 for the one new chats use (`primary`, titled 모델 and 생각
    /// 수준), else folded under 다른 AI와 고급 with its name.
    private List<(bool changed, Action add)> SideRows(Settings s, string side, bool primary, List<string> apiIds, string filter)
    {
        var d = Defaults;
        var list = new List<(bool, Action)>();
        string level = Tr("생각 수준", "Thinking Level");
        switch (side)
        {
            case "chatgpt":
                {
                    IReadOnlyList<GptCatalog.Model> gpts = collected is null ? GptCatalog.All : [];
                    string? gptSlug = collected is not null ? null : s.GptModel is { Length: > 0 } g && gpts.Any(m => m.Slug == g) ? g : GptCatalog.DefaultSlug();
                    string title = primary ? Tr("모델", "Model") : Tr("ChatGPT 모델", "ChatGPT Model");
                    list.Add((s.GptModel != d.GptModel, () =>
                    {
                        if (gpts.Count > 0)
                            Menu(title, (GptCatalog.Find(gptSlug) is { } cur ? GptCatalog.About(cur) : Tr("Codex 기본 모델", "Codex default model")) + Tr(" · 대화 중에는 Ctrl+M", " · Ctrl+M during chat"), G.Model,
                                gpts.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), gptSlug ?? gpts[0].Slug, v =>
                                {
                                    s.GptModel = v;
                                    Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels and the line under it follow the model
                                }, filter);
                        else
                            Add(title, Tr("Codex의 모델 목록이 아직 없음 — Codex를 실행하여 목록을 불러오세요", "Codex model list not loaded yet — run Codex to load the list"), null, G.Model, null, null, filter);
                    }
                    ));
                    string gptId = gptSlug is null ? "chatgpt" : AiModels.Gpt(gptSlug);
                    list.Add((s.GptEffort != d.GptEffort, () =>
                        Choice(primary ? level : Tr("ChatGPT 생각 수준", "ChatGPT Thinking Level"), Tr("모델마다 단계가 다릅니다 · 대화 중 Ctrl+E", "Available levels vary by model · Ctrl+E during chat"), G.Bulb,
                            collected is null ? AiModels.Efforts(gptId).Select(e => (e, AiModels.EffortName(e))).ToArray() : [], collected is null ? AiModels.Fit(gptId, s.GptEffort) : "", v => s.GptEffort = v, filter))); // collecting asks no model catalog
                    break;
                }
            case "omp":
                OmpChatRows(s, primary, list, filter);
                break;
            case "api":
                if (apiIds.Count > 0)
                {
                    string apiModel = AiModels.ApiDefault() is { } dm && apiIds.Contains(dm) ? dm : apiIds[0]; // the one new chats use
                    list.Add((s.ApiModel != d.ApiModel, () =>
                        Menu(primary ? Tr("모델", "Model") : Tr("API 모델", "API Model"), Tr("새 대화의 기본값 · 대화 중에는 Ctrl+M", "Default for new chats · Ctrl+M during chat"), G.Model,
                            apiIds.Select(id => (id, AiModels.Who(id), (string?)null)).ToArray(), apiModel, v =>
                            {
                                s.ApiModel = v;
                                Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels follow the model's provider
                            }, filter)));
                    if (AiModels.HasEffort(apiModel))
                        list.Add((s.ApiEffort != d.ApiEffort, () =>
                            Choice(primary ? level : Tr("API 생각 수준", "API Thinking Level"), Tr("기본은 모델 자체 설정 · 추론 모델에서만 사용 · 대화 중 Ctrl+E", "Default uses the model's own setting · Reasoning models only · Ctrl+E during chat"), G.Bulb,
                                AiModels.Efforts(apiModel).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(apiModel, s.ApiEffort), v => s.ApiEffort = v, filter)));
                }
                else if (primary)
                    list.Add((false, () => Add(Tr("API 키 추가", "Add API Key"), Tr("API 키가 없습니다. AI 계정에서 키를 추가하세요.", "No API keys found. Add a key in AI Accounts."), null, G.Model, () => Go("accounts"), null, filter)));
                break;
            default:
                list.Add((s.AiModel != d.AiModel, () =>
                    Choice(primary ? Tr("모델", "Model") : Tr("Claude 모델", "Claude Model"), Tr("새 대화의 기본값 · 대화 중에는 Ctrl+M", "Default for new chats · Ctrl+M during chat"), G.Model,
                        AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), s.AiModel, v => s.AiModel = v, filter)));
                list.Add((s.AiEffort != d.AiEffort, () =>
                    Choice(primary ? level : Tr("Claude 생각 수준", "Claude Thinking Level"), Tr("빠름은 생각 끔 · Haiku에서는 설정 불가 · 대화 중 Ctrl+E", "Fast turns off thinking · Unavailable on Haiku · Ctrl+E during chat"), G.Bulb,
                        AiModels.ClaudeEfforts.Select(e => (e, AiModels.EffortName(e))).ToArray(), s.AiEffort, v => s.AiEffort = v, filter)));
                break;
        }
        return list;
    }

    /// 번역 · AI 빠른 동작 (Bloom Lens's translation and the selected-text actions share the engine): the engine, its
    /// model and level, the language; the live translation's interval folded.
    private void TranslationGroup(Settings s, string filter)
    {
        Header(Tr("번역 · AI 빠른 동작", "Translation · AI Quick Actions"));
        if (FeatureOff(filter, "lens", "quick")) return;
        // collecting reads no keys or model lists: the CLI sides only
        string tp = collected is null ? Translator.Provider() : s.TranslateProvider == "chatgpt" ? "chatgpt" : "claude";
        string[] engines = collected is null ? Translator.Providers() : new[] { "claude", "chatgpt" };
        Choice(Tr("번역 엔진", "Translation Engine"), Tr("AI 빠른 동작도 이 엔진을 사용합니다 · 번역 창에서도 바꿀 수 있습니다", "AI Quick Actions also use this engine · Can also be changed in the translation window"), G.Sparkle,
            engines.Select(e => (e, e == "api" ? "API" : Translator.Label(e))).ToArray(), tp, v => { s.TranslateProvider = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        string model = Tr("모델", "Model");
        if (tp == "claude")
            Choice(model, Tr("Haiku는 생각 수준을 지원하지 않습니다.", "Haiku does not support thinking levels."), G.Model,
                AiModels.Claude.Select(m => (m, AiModels.Name(m))).ToArray(), Translator.Model("claude"), v => { s.TranslateClaudeModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        else if (tp == "omp")
            OmpModelPick("fast", model, Tr("가벼울수록 빠릅니다 · 비우면 omp의 기본 모델", "Lighter models are faster · Leave blank for omp default model"), s.OmpFastModel, v => s.OmpFastModel = v, filter);
        else if (tp == "api")
            Menu(model, Tr("가벼운 모델일수록 빠르고 비용이 적습니다", "Lighter models are faster and cost less"), G.Model,
                ApiCatalog.Ids().Select(id => (id, AiModels.Who(id), (string?)null)).ToArray(), Translator.Model("api"), v => { s.TranslateApiModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        else if (GptCatalog.All.Count > 0)
            Menu(model, Tr("가벼운 모델일수록 빠르고 사용량이 적습니다", "Lighter models are faster and use less quota"), G.Model,
                GptCatalog.All.Select(m => (m.Slug, m.Name, (string?)GptCatalog.About(m))).ToArray(), AiModels.GptSlug(Translator.Model("chatgpt")) ?? GptCatalog.All[0].Slug, v => { s.TranslateGptModel = v; Dispatcher.BeginInvoke(() => Build(lastFilter)); }, filter);
        var tLevels = Translator.Levels(tp);
        if (tLevels.Length > 0)
            Choice(Tr("생각 수준", "Thinking Level"), Tr("낮을수록 빠르고 사용량이 적습니다. 번역 창에서도 변경할 수 있습니다.", "Lower levels are faster and use less quota. You can also change this in the translation window."), G.Bulb,
                tLevels.Select(e => (e, AiModels.EffortName(e))).ToArray(), Translator.Effort(tp)!, v => Translator.SetEffort(tp, v), filter);
        Choice(Tr("번역할 언어", "Target Language"), Tr("자동: 한국어는 영어로, 나머지는 한국어로", "Auto: Korean to English, other languages to Korean"), G.Globe, Translator.Targets, s.TranslateTo, v => s.TranslateTo = v, filter);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        More("translate", filter, new()
        {
            (s.LiveSeconds != Defaults.LiveSeconds, () =>
                Choice(Tr("실시간 번역 간격", "Live Translation Interval"), Tr("화면을 다시 읽는 간격 (읽기는 사용량 없음 · 글이 바뀌고 멈췄을 때만 번역)", "How often the screen is re-read (reading uses no quota · translates only when text changes and settles)"), G.History,
                    new[] { ("1", Tr("1초", "1s")), ("1.5", Tr("1.5초", "1.5s")), ("3", Tr("3초", "3s")), ("5", Tr("5초", "5s")) }, s.LiveSeconds.ToString(inv), v => s.LiveSeconds = double.Parse(v, inv), filter)),
        });
    }

    private void PageMcp(Settings s, string filter)
    {
        Page("mcp", Tr("PC 작업 · MCP", "PC Actions · MCP"), G.Bolt, Tr("AI 채팅에서 이 PC의 앱 실행, 파일 정리, 타이머를 사용합니다", "Launch apps, organize files, and use timers on this PC from AI Chat"), filter);
        Header("");
        // each chat's own switch; a running session took its tools when it began, so Claude's and omp's end
        var chats = new List<(string label, bool on, Action<bool> set)>
        {
            ("Claude", s.AiTools, v =>
            {
                s.AiTools = v;
                host.Chats.EndAll(); // the next message starts its session with (or without) the tools
            }),
            ("ChatGPT", s.AiToolsGpt, v => s.AiToolsGpt = v),
        };
        if (collected is not null || OmpCli.FindCli(s.OmpPath) is not null) // no omp here: nothing to switch
            chats.Add(("omp", s.AiToolsOmp, v =>
            {
                s.AiToolsOmp = v;
                host.Chats.EndAll();
            }
            ));
        chats.Add(("API", s.AiToolsApi, v => s.AiToolsApi = v));
        KeyChips(Tr("사용할 대화", "Enabled Chats"), Tr("요청마다 약 +250~500 토큰", "~+250–500 tokens per request"), chats, filter);

        Header(Tr("권한", "Permissions"), Tr("파일 · 명령 · 다른 MCP 도구는 API 대화에만 적용", "Files · Commands · Other MCP tools apply only to API chats"));
        (string, string)[] rules = { (ToolPermissions.Ask, Tr("매번 묻기", "Ask Every Time")), (ToolPermissions.Allow, Tr("항상 허용", "Always Allow")), (ToolPermissions.Off, Tr("사용 안 함", "Disabled")) };
        (string, string)[] withAuto = [(ToolPermissions.Auto, Tr("자동", "Auto")), .. rules];
        // `restart`: the rule decides what a chat gets at all (the tool, Claude Code's mode), which a running CLI session
        // fixed when it began
        void Rule(string action, string title, string sub, char glyph, bool restart = false) =>
            Choice(title, sub, glyph, ToolPermissions.DefaultOf(action) == ToolPermissions.Auto ? withAuto : rules, ToolPermissions.Of(s.Permissions, action), v =>
            {
                if (v == ToolPermissions.DefaultOf(action)) s.Permissions.Remove(action);
                else s.Permissions[action] = v;
                if (restart) host.Chats.EndAll();
                // 사용 안 함 ends the running MCP servers and a start under way at once, not at the next message
                if (action == ToolPermissions.Mcp && v == ToolPermissions.Off) ApiSession.CloseMcpAll();
            }, filter);
        Rule(ToolPermissions.ReadOutside, Tr("파일 읽기 (대화 폴더 밖)", "Read files (outside chat folder)"), Tr("대화 폴더 밖만 묻습니다. 내용 검색은 모든 대화에 적용됩니다.", "Only asks for files outside the chat folder. Content search applies to all chats."), G.Document);
        Rule(ToolPermissions.Write, Tr("파일 쓰기", "Write files"), Tr("새 파일 만들기, 파일 덮어쓰기", "Create new files, overwrite files"), G.Save);
        Rule(ToolPermissions.Edit, Tr("파일 고치기", "Edit files"), Tr("파일의 일부 바꾸기", "Replace parts of a file"), G.Pen);
        Rule(ToolPermissions.Command, Tr("명령 실행", "Run commands"), Tr("대화 폴더에서 PowerShell 실행", "Run PowerShell in the chat folder"), G.Code);
        Rule(ToolPermissions.Mcp, Tr("다른 MCP 도구", "Other MCP tools"), Tr("아래에서 켠 MCP 서버의 도구 · 끄면 서버를 시작하지 않음", "Tools from MCP servers enabled below · Servers do not start when off"), G.Plug);
        Rule(ToolPermissions.Pc, Tr("PC 조작 (Bloom 기능)", "PC actions (Bloom features)"), Tr("모든 대화 · 찾기는 묻지 않음 · 자동은 새로 받거나 만든 실행 파일과 클립보드 읽기만 물음", "All chats · Searching never asks · Auto only asks for reading the clipboard or running newly downloaded/created executables"), G.Bolt, restart: true);
        Rule(ToolPermissions.Claude, Tr("Claude 대화 도구", "Claude chat tools"), Tr("웹 페이지 · 대화 폴더 밖 파일 · 다른 MCP 도구 · 자동은 Claude Code의 자동 모드", "Web pages · Files outside chat folder · Other MCP tools · Auto uses Claude Code's auto mode"), G.Globe, restart: true);
        Header(Tr("할 수 있는 일", "Capabilities"));
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
        Group("look", Tr("찾아보기", "Search & browse"), Tr("앱 · 파일 · 최근 파일 · 열린 창 · 타이머 · 사용자 명령 — 묻지 않고 바로 검색합니다", "Apps · Files · Recent files · Open windows · Timers · Custom commands — searched without asking"), G.Search);
        Group("open", Tr("열기 · 실행", "Open · Run"), Tr("앱 · 파일 · 사이트 열기, ‘10분 타이머’ ‘볼륨 30’ 같은 Bloom 명령, 사용자 명령", "Open apps, files, and sites, Bloom commands like ‘10m timer’ or ‘volume 30’, and custom commands"), G.App);
        Group("bloom", Tr("메모 · 타이머 · 일정", "Notes · Timers · Events"), Tr("Bloom 메모에 적기 · 타이머 · 캘린더 일정 — 적은 메모는 채팅 옆에 표시됩니다", "Write to Bloom notes · Timers · Calendar events — notes appear next to the chat"), G.Document);
        Group("files", Tr("파일 정리", "Organize files"), Tr("이름 바꾸기 · 옮기기 · 복사 · 휴지통 — 휴지통에서 되살릴 수 있습니다", "Rename · Move · Copy · Trash — can be restored from the Recycle Bin"), G.Folder);
        Group("clip", Tr("클립보드", "Clipboard"), Tr("클립보드 읽기 · 넣기", "Read · Write clipboard"), G.Paste);
        Header(Tr("이 PC의 다른 MCP 서버", "Other MCP servers on this PC"), Tr("켜면 대화를 시작할 때 함께 켜집니다. 첫 답이 조금 늦어지고 사용량이 늘어납니다.", "When enabled, starts along with a new chat. Slightly delays the first reply and increases usage."),
            (Tr("목록 다시 읽기", "Reload list"), () =>
            {
                mcpFound = null;
                Build(lastFilter);
            }
        ));
        // read where each program keeps them (Claude Code, Claude Desktop, Cursor, VS Code, Windsurf, Codex, files
        // brought in), once per opening of this page
        mcpFound ??= collected is null ? McpCatalog.Found(s.McpFiles) : []; // collecting reads no program's settings files
        if (mcpFound.Count == 0 && collected is null)
            Add(Tr("등록된 서버 없음", "No servers registered"), Tr("Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex에 등록한 MCP가 여기 표시됩니다", "MCP servers registered in Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex appear here"), null, G.Plug, null, null, filter);
        foreach (var d in mcpFound)
        {
            string key = d.Key;
            var sw = Toggle(s.McpOn.Contains(key), v =>
            {
                if (v) s.McpOn.Add(key);
                else s.McpOn.Remove(key);
                host.Chats.EndAll(); // the next message starts with (or without) it
            }, out var t);
            string where = McpCatalog.Label(d.Source) + (d.Stdio ? Tr(" · 이 PC에서 실행", " · Runs on this PC") : Tr(" · 웹 주소 · ChatGPT에서 사용 불가", " · Web URL · Unavailable in ChatGPT"));
            Add(d.Name, where, sw, G.Plug, t, null, filter);
        }
        Add(Tr("MCP 설정 파일 가져오기…", "Import MCP config file…"), Tr("mcpServers가 적힌 .json (예: 프로젝트의 .mcp.json) — 위에서 못 찾은 서버가 있을 때", "A .json file containing mcpServers (e.g., a project's .mcp.json) — use when a server isn't found above"), null, G.Import, () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Tr("MCP 설정 (*.json)|*.json", "MCP Settings (*.json)|*.json") };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            if (!s.McpFiles.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) s.McpFiles.Add(dlg.FileName);
            Settings.Save();
            mcpFound = null;
            Build(lastFilter);
        }, null, filter);
    }

    private void PageFeatures(Settings s, string filter)
    {
        Page("features", Tr("기능", "Features"), G.Sliders, Tr("기능을 끄면 검색 결과, 명령 목록, 홈, 즐겨찾기에서 제외되고 단축키가 해제됩니다. 저장된 데이터는 유지됩니다.", "Turned-off features are hidden from search results, commands, Home, and favorites, and their shortcuts are disabled. Saved data is kept."), filter);
        foreach (var (group, title) in new[] { ("ai", "AI"), ("search", Tr("검색줄에서", "In Search Bar")), ("tools", Tr("창 · 도구", "Windows · Tools")) })
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
                    Add(Tr("기본 앱으로 정하기", "Set as default app"), Tr("Windows 설정에서 .md 파일을 열 앱으로 Bloom을 고르세요. 탐색기에서 두 번 클릭하면 Bloom 문서 창으로 열립니다.", "Choose Bloom as the default app for .md files in Windows Settings. Double-clicking in File Explorer opens them in a Bloom document window."), null, G.NewWindow, () => Launch.Open(FileAssoc.DefaultsPage), null, filter);
            }
        }
    }

    private void PageTools(Settings s, string filter)
    {
        Page("tools", Tr("도구", "Tool"), G.Stopwatch, Tr("클립보드 기록, Bloom Lens, 타이머, 컬러 피커, 미니 플레이어", "Clipboard history, Bloom Lens, Timer, Color Picker, Mini Player"), filter);
        Header(Tr("클립보드 기록", "Clipboard History"));
        int clips = collected is null ? host.Clipboard.Items.Count : 0; // collecting starts no history
        var clSw = Toggle(s.ClipHistory, v =>
        {
            s.ClipHistory = v;
            host.ApplyHotkeys();
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // 보관 기간 shows only with it on
        }, out var clT);
        Tip(s.ClipHistory ? Tr("메모리 약 +2 MB부터, 이미지가 많을수록 늘어납니다", "Uses ~2 MB+ of memory; increases with more images") : Tr("켜면 메모리 약 +2 MB부터", "Uses ~2 MB+ of memory when enabled"));
        Add(Tr("클립보드 기록", "Clipboard History"), s.ClipHistory ? Tr($"켜짐 · {clips}개 · 비밀번호 관리자가 숨긴 내용 제외", $"On · {clips} items · Excludes items hidden by password managers") : Tr("꺼짐 · 기록 저장 안 함", "Off · History not saved"), clSw, G.Paste, clT, null, filter);
        if (s.ClipHistory)
            Duration(Tr("보관 기간", "Retention period"), v => (v <= 0 ? Tr("계속 보관", "Keep forever") : Tr($"{Words(v)} 동안 보관", $"Keep for {Words(v)}")) + Tr(" · 0이면 계속 · 고정한 항목은 늘 유지됩니다 · 최대 500개", " · 0 to keep forever · Pinned items are always kept · Up to 500 items"),
                G.History, s.ClipKeepMinutes, 525_600, DurationField.MinutesHoursDays, v =>
                {
                    s.ClipKeepMinutes = v;
                    host.Clipboard.KeepChanged();
                }, filter);

        Header("Bloom Lens", null, (Tr("단축키", "Shortcuts"), () => Go("shortcuts")));
        if (!FeatureOff(filter, "lens"))
        {
            var ocCopy = Toggle(s.OcrCopy, v => s.OcrCopy = v, out var ocT);
            Add(Tr("읽은 글자 바로 복사", "Copy recognized text immediately"), Tr("끄면 런처에서 고른 뒤에 복사합니다", "When off, select in the launcher before copying"), ocCopy, G.Copy, ocT, null, filter);
            var langs = collected is null ? Ocr.WinOcr.Languages() : [];
            if (langs.Count == 0 && collected is null)
                Add(Tr("Windows 언어 설정 열기", "Open Windows language settings"), Tr("설치된 인식 언어가 없습니다. Windows 언어 설정에서 추가하세요.", "No OCR languages installed. Add one in Windows language settings."), null, G.Keyboard, () => Launch.Open("ms-settings:regionlanguage"), null, filter);
            More("ocr", filter, new()
            {
                (s.OcrLanguage != Defaults.OcrLanguage, () =>
                    Choice(Tr("인식 언어", "Recognition language"), langs.Count == 0 ? Tr("설치된 인식 언어 없음 · Windows 언어 설정에서 추가하세요", "No OCR languages installed · Add one in Windows language settings") : Tr($"설치된 인식기: {string.Join(", ", langs)}", $"Installed recognizers: {string.Join(", ", langs)}"), G.Keyboard,
                        new[] { ("auto", Tr("자동", "Auto")) }.Concat(langs.Select(l => (l, LangName(l)))).ToArray(), s.OcrLanguage, v => s.OcrLanguage = v, filter)),
            });
        }

        Header(Tr("타이머 · 알림", "Timers · Reminders"));
        if (!FeatureOff(filter, "timers"))
        {
            var tSound = Toggle(s.TimerSound, v => s.TimerSound = v, out var tSoundT);
            Add(Tr("끝날 때 소리", "Sound when finished"), Tr("끝나면 오른쪽 아래 카드와 함께 Windows 알람 소리 (30초까지)", "Plays the Windows alarm sound (up to 30s) with a bottom-right card when finished"), tSound, G.Stopwatch, tSoundT, null, filter);
        }

        Header(Tr("컬러 피커", "Color Picker"));
        if (!FeatureOff(filter, "color"))
        {
            var cCopy = Toggle(s.ColorCopy, v => s.ColorCopy = v, out var cCopyT);
            Add(Tr("찍은 색 바로 복사", "Copy picked color immediately"), Tr("HEX(#ff5a8a)로 복사합니다", "Copies as HEX (#ff5a8a)"), cCopy, G.Copy, cCopyT, null, filter);
        }

        PlayerGroup(s, filter);
    }

    private void PageNotices(Settings s, string filter)
    {
        Page("notices", Tr("작업 알림", "Task Notifications"), G.Bell, Tr("AI 작업의 시작, 완료, 승인 대기를 화면 구석 카드로", "Show corner cards when AI tasks start, finish, or await approval"), filter);
        Header("");
        var on = Toggle(s.Notices, v =>
        {
            s.Notices = v;
            Notices.Apply();
            WindowsToasts.Apply(Dispatcher);
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the groups below follow the switch
        }, out var onT);
        Add(Tr("작업 알림", "Task Notifications"), s.Notices ? Tr("켜짐 · 메모리 약 +1 MB", "On · Uses ~1 MB of memory") : Tr("꺼짐 — 알림도 소리도 없음 · 켜면 메모리 약 +1 MB", "Off — no cards or sounds · Uses ~1 MB of memory when enabled"), on, G.Bell, onT, null, filter);
        if (!s.Notices) return;
        // 미리 보기: a card of each kind switched on (picking a position or a size shows one)
        Header(Tr("표시", "Display"), null, (Tr("미리 보기", "Preview"), () => Notices.Preview()));
        var spots = new List<(string, string, string?)>
        {
            ("top-left", Tr("왼쪽 위", "Top left"), null), ("top-center", Tr("가운데 위", "Top center"), null), ("top-right", Tr("오른쪽 위", "Top right"), null),
            ("bottom-left", Tr("왼쪽 아래", "Bottom left"), null), ("bottom-center", Tr("가운데 아래", "Bottom center"), null), ("bottom-right", Tr("오른쪽 아래", "Bottom right"), null),
        };
        if (s.NoticeSpot == "free") spots.Add(("free", Tr("직접 정한 위치", "Custom position"), null));
        Menu(Tr("위치", "Position"), Tr("고르면 미리 보기 카드가 그 자리에 표시됩니다", "Selecting a position shows a preview card there"), G.Screen, spots.ToArray(), s.NoticeSpot, v =>
        {
            s.NoticeSpot = v;
            Notices.Preview(one: true);
        }, filter);
        Add(Tr("직접 옮기기", "Move freely"), Tr("화면에서 알림 카드를 끌어 옮깁니다 · 가운데와 가장자리에 붙습니다", "Drag the notification card on screen · Snaps to center and edges"), null, G.Move,
            () => Notices.Arrange(() => Build(lastFilter)), null, filter);
        Choice(Tr("크기", "Size"), null, G.Sliders, new[] { ("s", Tr("작게", "Small")), ("m", Tr("보통", "Normal")), ("l", Tr("크게", "Large")), ("xl", Tr("아주 크게", "Extra large")) }, s.NoticeSize, v =>
        {
            s.NoticeSize = v;
            Notices.Preview(one: true);
        }, filter);
        Choice(Tr("보이는 시간", "Duration"), Tr("마우스를 올리면 유지 · 승인 대기는 작업 재개까지", "Stays while hovered · Approval requests stay until resumed"), G.Stopwatch,
            new[] { ("4", Tr("4초", "4s")), ("6", Tr("6초", "6s")), ("10", Tr("10초", "10s")), ("20", Tr("20초", "20s")) }, s.NoticeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), v => s.NoticeSeconds = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture), filter);
        Choice(Tr("효과음", "Sound effect"), Tr("알림마다 다른 짧은 소리", "Short distinct sound for each notification type"), G.Volume, new[] { ("off", Tr("끔", "Off")), ("s", Tr("작게", "Small")), ("m", Tr("보통", "Normal")), ("l", Tr("크게", "Large")) }, s.NoticeSound, v =>
        {
            s.NoticeSound = v;
            Chimes.Play("done");
        }, filter);
        Choice(Tr("카드를 누르면", "When card is clicked"), NoticeClickNote(s.NoticeClick), G.Chat, new[] { ("app", Tr("앱으로 이동", "Go to app")), ("taskbar", Tr("작업표시줄 AI 패널", "Taskbar AI panel")) }, s.NoticeClick, v =>
        {
            s.NoticeClick = v;
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the line under it says what the add-on still needs
        }, filter);
        Header(Tr("알릴 일", "Events to Notify"));
        void Kind(string title, string sub, char glyph, bool value, Action<bool> set)
        {
            var sw = Toggle(value, set, out var t);
            Add(title, sub, sw, glyph, t, null, filter);
        }
        Kind(Tr("진행 중", "In progress"), Tr("작업이 시작되면 무엇을 하는지 (4초 넘게 걸릴 때)", "What the task is doing when started (if taking over 4s)"), G.Gears, s.NoticeStart, v => s.NoticeStart = v);
        Kind(Tr("작업 완료", "Completed"), Tr("끝나면 걸린 시간과 함께 (10초 넘게 걸린 작업)", "Shows elapsed time when finished (for tasks over 10s)"), G.Done, s.NoticeDone, v => s.NoticeDone = v);
        Kind(Tr("승인 대기", "Awaiting approval"), Tr("승인 또는 답변이 필요할 때 표시 · 작업 재개까지 유지", "Shown when approval or reply is needed · Stays until task resumes"), G.Clock, s.NoticeWaiting, v => s.NoticeWaiting = v);
        Kind(Tr("알림 카드에서 승인", "Approve from notification card"), Tr("Claude Code의 승인 요청을 카드에서 허용 · 거절 · 켜면 Claude Code 설정(settings.json)에 Bloom 훅을 추가하고, 끄면 뺍니다", "Allow or deny Claude Code requests on the card · Adds a Bloom hook to Claude Code's settings.json when enabled, and removes it when disabled"), G.Shield, s.NoticeApprove, v =>
        {
            s.NoticeApprove = v; // first: Bloom's start-up ClaudeHooks.Follow reads it in its turn on the file
            if (!ClaudeHooks.Set(v))
            {
                s.NoticeApprove = !v;
                Notices.Result(false, Tr("Bloom · 알림", "Bloom · Notification"), Tr("Claude Code 설정을 바꾸지 못했습니다", "Could not update Claude Code settings"), ClaudeHooks.SettingsFile);
                Dispatcher.BeginInvoke(() => Build(lastFilter)); // the switch shows what holds
                return;
            }
            ClaudeApprovals.Apply();
        });
        Kind(Tr("한도 임박", "Limit almost reached"), Tr("5시간 · 주간 한도가 20% 남았을 때와 다 썼을 때", "When 5-hour or weekly quota reaches 20% remaining or is exhausted"), G.Warning, s.NoticeLimit, v => s.NoticeLimit = v);
        Kind(Tr("한도 복구", "Quota reset"), Tr("모자랐던 한도가 초기화되어 다시 찼을 때", "When a low or depleted quota resets"), G.Sync, s.NoticeRecover, v => s.NoticeRecover = v);
        Kind(Tr("Windows 알림", "Windows notifications"), Tr("다른 앱의 Windows 알림 배너 대신 이 카드로 · 클릭하면 Windows 알림 센터 · 작업표시줄 애드온 PillTaskbar가 켜져 있어야 합니다", "Show other apps' Windows notification banners as this card · Click to open Notification Center · Requires taskbar add-on PillTaskbar to be enabled"), G.Bell, s.NoticeWindows, v =>
        {
            s.NoticeWindows = v;
            WindowsToasts.Apply(Dispatcher);
        });
    }

    /// The line under 카드를 누르면: where a Claude Code or Codex card goes, and what the taskbar add-on still needs for its panel.
    private static string NoticeClickNote(string value) => value != "taskbar"
        ? Tr("Claude Code와 Codex 카드를 누르면 그 세션 창으로 갑니다", "Clicking a Claude Code or Codex card switches to its session window")
        : TaskbarChat.Missing() switch
        {
            TaskbarChat.Need.Addon => Tr("작업표시줄 애드온 PillTaskbar를 설치하고 켜야 쓸 수 있어요. 그 전에는 앱으로 갑니다", "Requires taskbar add-on PillTaskbar to be installed and enabled. Until then, clicking opens the app"),
            TaskbarChat.Need.Chat => Tr("작업표시줄 애드온 설정에서 사용량 막대의 세션 목록과 AI 채팅을 켜야 해요. 그 전에는 앱으로 갑니다", "Enable session list and AI chat for the usage bar in taskbar add-on settings. Until then, clicking opens the app"),
            _ => Tr("그 세션의 채팅을 작업표시줄 사용량 창에서 엽니다", "Opens that session's chat in the taskbar usage popup"),
        };

    private const string PillTaskbarSource = "https://github.com/hyeon1035298/PillTaskbar_BloomAddon";

    private void PageAbout(Settings s, string filter)
    {
        Page("about", Tr("정보", "About"), G.Info, Tr("버전, 업데이트, 도움말", "Version, updates, and help"), filter);
        Header(Tr("버전", "Version"));
        UpdateRows(s, filter);
        Add(Tr("업데이트 기록", "Release notes"), Tr("버전별 변경 사항을 GitHub에서 불러옵니다 · 업데이트한 뒤 처음 켤 때도 한 번 표시합니다 · Enter로 보기", "Loads release notes from GitHub · Also shown once on first launch after an update · Press Enter to view"), null, G.Document, () => host.ShowWhatsNew(), null, filter);
        // the taskbar add-on ships beside Bloom under its own licence; its source is a separate repository
        Add(Tr("라이선스", "License"), Tr("Bloom은 MIT입니다 · 작업표시줄 애드온 PillTaskbar는 GPL-3.0인 별도 프로그램입니다", "Bloom is MIT licensed · Taskbar add-on PillTaskbar is a separate GPL-3.0 program"),
            RowButton(Tr("소스 보기", "View Source"), () => Launch.Open(PillTaskbarSource)), G.Document, () => Launch.Open(PillTaskbarSource), null, filter);
        Header(Tr("도움말", "Help"));
        Add(Tr("Bloom 둘러보기", "Bloom Tour"), Tr("처음 설정과 사용법 안내를 다시 보기", "View the initial setup and usage guide again"), null, G.Info, () => host.ShowOnboarding(), null, filter);
        Add(Tr("문제 보고", "Report an issue"), Tr("버전 · Windows · 최근 로그를 복사하고 GitHub 이슈 작성 페이지를 엽니다 · 사용자 이름과 이메일 주소는 가려지며 Bloom이 직접 보내는 것은 없습니다", "Copies version, Windows info, and recent logs and opens the GitHub issue page · Usernames and emails are redacted, and Bloom sends nothing automatically"), null, G.Warning, () =>
        {
            Launch.Copy(ProblemReport.Build());
            Web.Open(ProblemReport.IssueUrl());
            Notices.Result(true, Tr("문제 보고", "Report an issue"), Tr("진단 정보 복사 완료", "Diagnostics copied"), Tr("열린 페이지에 붙여넣으세요 (Ctrl+V)", "Paste into the opened page (Ctrl+V)"));
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
        if (minutes / 1440 > 0) parts.Add(Tr($"{minutes / 1440}일", $"{minutes / 1440}d"));
        if (minutes / 60 % 24 > 0) parts.Add(Tr($"{minutes / 60 % 24}시간", $"{minutes / 60 % 24}h"));
        if (minutes % 60 > 0) parts.Add(Tr($"{minutes % 60}분", $"{minutes % 60} min"));
        return string.Join(" ", parts);
    }

    /// 쉬는 대화 닫기's line: when a chat's CLI closes and what that means.
    private static string IdleText(int minutes) => minutes <= 0
        ? Tr("해제 안 함 · 대화마다 150~300 MB 사용", "Never unload · Uses 150–300 MB per active chat")
        : Tr($"{Words(minutes)} 동안 쓰지 않은 대화의 메모리 해제 · 기록 유지 · 다음 첫 답변이 몇 초 늦음 · 0이면 해제 안 함", $"Unloads memory for chats inactive for {Words(minutes)} · Keeps history · Next reply takes a few extra seconds · 0 to never unload");

    /// Bloom {버전}: the version, and under it what the last look for an update found; Enter looks now, restarts into a
    /// downloaded version (the line is then 다시 시작해서 업데이트), or for a copy not installed with the setup file opens
    /// the releases page. Under it, whether Bloom looks once at each start.
    private void UpdateRows(Settings s, string filter)
    {
        var now = Updates.Now;
        string ver = typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "";
        string sub = now switch
        {
            Updates.Stage.NotInstalled => Tr("자동 업데이트는 설치 파일로 설치한 Bloom에서 사용 가능 · 릴리스 페이지 열기", "Auto-update is available when Bloom is installed via the installer · Open releases page"),
            Updates.Stage.Checking => Tr("확인하는 중…", "Checking…"),
            Updates.Stage.Latest => Tr("최신 버전 · Enter로 업데이트 확인", "Up to date · Press Enter to check for updates"),
            Updates.Stage.Downloading => Tr($"새 버전({Updates.Found})을 받는 중…", $"Downloading new version ({Updates.Found})…"),
            Updates.Stage.Ready => Tr($"새 버전 {Updates.Found} 준비됨 · 클릭하여 다시 시작", $"New version {Updates.Found} ready · Click to restart"),
            Updates.Stage.Failed => Tr("업데이트 확인 실패 · 인터넷 연결을 확인하고 다시 누르세요", "Update check failed · Check your internet connection and try again"),
            _ => Tr("Enter로 Bloom과 애드온의 업데이트를 확인합니다. GitHub 릴리스에서 새 버전을 찾습니다", "Press Enter to check for Bloom and add-on updates from GitHub Releases"),
        };
        Add(now == Updates.Stage.Ready ? Tr("다시 시작해서 업데이트", "Restart to update") : $"Bloom {ver}", sub, null, G.Refresh, () =>
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
                    AddonHub.CheckNow();
                    break;
            }
        }, null, filter);
        var checkSw = Toggle(s.UpdateCheck, v => s.UpdateCheck = v, out var checkT);
        Add(Tr("켤 때 업데이트 확인", "Check for updates on startup"), Tr("실행 후 한 번 확인 · 새 버전 다운로드 후 다음 실행 시 적용", "Checks once after launch · Downloads new versions and applies them on next launch"), checkSw, G.Sync, checkT, null, filter);
    }
}
