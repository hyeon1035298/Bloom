using System;
using System.Collections.Generic;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// What one view of the launcher is: the chip in the search line, its hint, the footer's context, how its list
/// is built and how it behaves. A new view is one entry in Launcher.Views() plus its Engine.<View>.cs.
internal sealed class ViewSpec
{
    public Func<string?> Chip = () => null;
    public Func<string> Placeholder = () => "";
    public Func<string> Context = () => "Bloom";
    /// The list for the typed text; null for the views with their own path (Root, Files, Form, Chat).
    public Func<string, List<Section>>? Build;
    public bool Split; // the preview pane on the right
    public bool KeepScroll; // a refresh while it is open keeps the scroll position (lists that tick)
    public bool Live; // data changes (Host.Later) refresh it
}

internal sealed partial class Launcher
{
    private Dictionary<Mode, ViewSpec>? views;
    private static readonly ViewSpec NoView = new();

    private ViewSpec View(Mode m) => (views ??= Views()).TryGetValue(m, out var v) ? v : NoView;

    private Dictionary<Mode, ViewSpec> Views() => new()
    {
        [Mode.Root] = new()
        {
            // one example of what can be typed after it, the next at each opening (Core\Expressions)
            Placeholder = () => Tr("검색하거나 AI에게 묻기", "Search, or ask AI") + (Expressions.Hint(openings) is { } example ? " · " + example : ""),
            Context = () => box.Text.Trim() == Expressions.Key ? Tr("쓸 수 있는 표현", "What you can type") : "Bloom",
            Live = true,
        },
        [Mode.Files] = new()
        {
            Chip = () => Tr("파일", "Files"),
            Placeholder = () => Tr("파일 이름 · Everything 문법 (ext:pdf, dm:today)", "File name · Everything syntax (ext:pdf, dm:today)"),
            Context = () => host.Everything.Running ? Tr("파일 검색 · Everything", "File search · Everything") : Tr("파일 검색 · Everything 꺼짐", "File search · Everything is off"),
            Split = true,
        },
        [Mode.Threads] = new()
        {
            Chip = () => Tr("AI 채팅", "AI chats"),
            Placeholder = () => Tr("대화 찾기, 또는 새로 묻기", "Find a chat, or ask something new"),
            Context = () => Tr("AI 채팅", "AI chats"),
            Build = Engine.Threads,
            Live = true,
        },
        [Mode.Clipboard] = new()
        {
            Chip = () => Tr("클립보드", "Clipboard"),
            Placeholder = () => Tr("복사한 내용 찾기", "Find something you copied"),
            Context = () => Settings.Current.ClipHistory
                ? Tr($"클립보드 기록 · {host.Clipboard.Items.Count}개", $"Clipboard history · {host.Clipboard.Items.Count}")
                : Tr("클립보드 기록 · 꺼짐", "Clipboard history · off"),
            Build = Engine.ClipboardView,
            Split = true,
            Live = true,
        },
        [Mode.Calendar] = new()
        {
            Chip = () => Tr("캘린더", "Calendar"),
            Placeholder = () => Tr("알림 찾기", "Find a reminder"),
            Context = () => Timers.Current.Items.Count > 0
                ? Tr($"캘린더 · 알림 {Timers.Current.Items.Count}개", $"Calendar · {Timers.Current.Items.Count} reminders")
                : Tr("캘린더 · 알림", "Calendar · reminders"),
            Build = text =>
            {
                var list = Engine.CalendarView(text, calDay);
                month.Show(calDay, calDay, Engine.CalendarMarks(text));
                return list;
            },
            Live = true,
        },
        [Mode.Quicklinks] = new()
        {
            Chip = () => Tr("브라우저", "Browser"),
            Placeholder = () => Tr("바로 가기 찾기 · 주소를 넣고 Enter로 새로 만들기", "Find a shortcut · type an address and Enter to add one"),
            Context = () => Tr($"브라우저 · {Browser.Web.BrowserName} · 단축어 + 스페이스 + 검색어", $"Browser · {Browser.Web.BrowserName} · keyword + space + search"),
            Build = Engine.QuicklinksView,
            Live = true,
        },
        [Mode.Commands] = new()
        {
            Chip = () => Tr("사용자 명령", "Custom commands"),
            Placeholder = () => Tr("명령 찾기 · 스크립트 · 스니펫 · 묶음 · AI 명령", "Find a command · scripts, snippets, chains, AI commands"),
            Context = () => Tr($"사용자 명령 · {UserCommands.Current.Items.Count}개 · 단축어 + 스페이스 + 입력", $"Custom commands · {UserCommands.Current.Items.Count} · keyword + space + input"),
            Build = Engine.CommandsView,
            Live = true,
        },
        [Mode.Inbox] = new()
        {
            Chip = () => Tr("알림 센터", "Notification center"),
            Placeholder = () => Tr("알림 찾기 · Enter로 그 채팅 열기", "Find a notification · Enter opens its chat"),
            Context = () => Tr($"알림 센터 · {NoticeLog.Current.Items.Count}개", $"Notification center · {NoticeLog.Current.Items.Count}"),
            Build = Engine.InboxView,
            Split = false, // each line shows its preview and time; Enter opens the whole answer in its chat
            Live = true,
        },
        [Mode.Processes] = new()
        {
            Chip = () => Tr("앱", "Apps"),
            Placeholder = () => Tr("프로그램 찾기 · Enter 강제 종료 · Ctrl+Enter 창 닫기", "Find a program · Enter quits it · Ctrl+Enter closes its windows"),
            Context = () => Tr($"앱 종료 · {host.Processes.Groups.Count}개", $"End apps · {host.Processes.Groups.Count}"),
            Build = Engine.ProcessesView,
            KeepScroll = true,
            Live = true,
        },
        [Mode.Ocr] = new()
        {
            Chip = () => Tr("화면 텍스트", "Screen text"),
            Placeholder = () => Tr("읽은 글자로 할 일 고르기", "Pick what to do with the text"),
            Context = () => host.LastOcr is { } lo ? Tr($"화면 텍스트 · {lo.Lines.Count}줄 · {lo.Language}", $"Screen text · {lo.Lines.Count} lines · {lo.Language}") : Tr("화면 텍스트", "Screen text"),
            Build = _ => Engine.OcrView(),
            Split = true,
        },
        [Mode.Lens] = new()
        {
            Chip = () => "Bloom Lens",
            Placeholder = () => Tr("이 영역으로 할 일 · 1 묻기 2 번역 3 글자 4 실시간 5 고정 6 캡처 7 팔레트", "What to do with this area · 1 ask 2 translate 3 text 4 live 5 pin 6 capture 7 palette"),
            Context = () => host.LastLens is { } ll ? $"Bloom Lens · {ll.Rect.Width}×{ll.Rect.Height}" : "Bloom Lens",
            Build = _ => Engine.LensView(),
            Split = true,
        },
        [Mode.Timers] = new()
        {
            Chip = () => Tr("타이머", "Timers"),
            Placeholder = () => Tr("10분 · 1시간 30분 · 15:30 회의 · 내일 9시 업로드", "10 min · 1h 30m · 15:30 meeting · tomorrow 9am upload"),
            Context = () => Timers.Current.Items.Count > 0
                ? Tr($"타이머 · 알림 · 진행 중 {Timers.Current.Items.Count}개", $"Timers · reminders · {Timers.Current.Items.Count} running")
                : Tr("타이머 · 알림", "Timers · reminders"),
            Build = Engine.TimersView,
            KeepScroll = true,
            Live = true,
        },
        [Mode.Color] = new()
        {
            Chip = () => Tr("컬러 피커", "Color picker"),
            Placeholder = () => Tr("색 코드 적기 (#ff5a8a, rgb(…)) · 또는 화면에서 찍기", "Type a colour (#ff5a8a, rgb(…)) · or pick one from the screen"),
            Context = () => Tr($"컬러 피커 · 최근 색 {ColorBook.Current.Recent.Count}개", $"Color picker · {ColorBook.Current.Recent.Count} recent"),
            Build = Engine.ColorView,
            Split = true,
        },
        [Mode.Palette] = new()
        {
            Chip = () => Tr("컬러 팔레트", "Color palette"),
            Placeholder = () => Tr("← → 색 고르기 · − + 색 개수 · Enter 복사", "← → pick a colour · − + colour count · Enter copies"),
            Context = () => host.Palette is { Colors.Count: > 0 } p ? Tr($"컬러 팔레트 · {p.Colors.Count}색 · PC 안에서 추출", $"Color palette · {p.Colors.Count} colours · made on this PC") : Tr("컬러 팔레트", "Color palette"),
            Build = _ => Engine.PaletteView(),
            KeepScroll = true, // a swatch picked draws the list again where it was
        },
        [Mode.Group] = new()
        {
            Chip = () => groupId is not null && Favorites.Group(groupId) is { } fg ? ClipText(fg.Title, 18) : Tr("폴더", "Folder"),
            Placeholder = () => Tr("폴더 안에서 찾기 · Enter 열기", "Find in this folder · Enter opens"),
            Context = () => Tr("즐겨찾기 폴더 · Ctrl+K 빼기 · 순서", "Favorites folder · Ctrl+K remove · order"),
            Build = text => groupId is null ? new List<Section>() : Engine.GroupView(groupId, text),
            Live = true,
        },
        [Mode.Windows] = new()
        {
            Chip = () => Tr("창 전환", "Windows"),
            Placeholder = () => Tr("창 제목이나 앱 이름 · Enter 전환 · Ctrl+W 닫기", "Window title or app · Enter switches · Ctrl+W closes"),
            Context = () => Tr($"창 전환 · 열린 창 {OpenWindows.Count}개", $"Switch windows · {OpenWindows.Count} open"),
            Build = Engine.WindowsView,
            KeepScroll = true,
            Live = true,
        },
        [Mode.Emoji] = new()
        {
            Chip = () => Tr("이모지 · 기호", "Emoji · symbols"),
            Placeholder = () => Tr("하트, 웃음, 화살표, 단위 … · Enter 붙여넣기", "heart, smile, arrow, degree … · Enter pastes"),
            Context = () => Tr($"이모지 · 기호 {Symbols.All.Count}개", $"Emoji · {Symbols.All.Count} symbols"),
            Build = Engine.EmojiView,
            Split = true,
        },
        [Mode.Recent] = new()
        {
            Chip = () => Tr("최근 파일", "Recent files"),
            Placeholder = () => Tr("최근에 연 파일 · 폴더 찾기", "Find a file or folder you opened lately"),
            Context = () => Tr("최근 파일 · Windows 최근 항목", "Recent files · Windows' recent items"),
            Build = Engine.RecentView,
            Split = true,
        },
        [Mode.Layout] = new()
        {
            Chip = () => Tr("창 배치", "Layout"),
            Placeholder = () => Tr("왼쪽 절반, 1/3, 최대화, 다음 화면 … · Enter로 앞의 창 옮기기", "left half, third, maximize, next screen … · Enter moves the window in front"),
            Context = () => Tr("창 배치 · Ctrl+K › 단축키 지정", "Window layout · Ctrl+K › Set a shortcut"),
            Build = Engine.LayoutView,
        },
        [Mode.Winget] = new()
        {
            Chip = () => Tr("앱 설치", "Install"),
            Placeholder = () => Tr("설치할 앱 이름 · Enter로 설치 · Ctrl+K 터미널에서 설치", "App to install · Enter installs · Ctrl+K installs in a terminal"),
            Context = () => Tr("앱 설치 · winget", "Install apps · winget"),
            Build = Engine.WingetView,
        },
        [Mode.System] = new()
        {
            Chip = () => Tr("시스템", "System"),
            Placeholder = () => Tr("다크 모드, 음소거, 볼륨 30, 블루투스 …", "dark mode, mute, volume 30, bluetooth …"),
            Context = () => Tr("시스템 · 켜고 끄기", "System · toggles"),
            Build = Engine.SystemView,
            KeepScroll = true,
            Live = true,
        },
        [Mode.Quick] = new()
        {
            Chip = () => Tr("AI 빠른 동작", "AI actions"),
            Placeholder = () => host.Quick.Result is not null || host.Quick.Busy
                ? Tr("결과로 할 일 고르기", "Pick what to do with the result")
                : Tr("동작 고르기 · 또는 원하는 걸 적고 Enter", "Pick an action · or type your own and Enter"),
            Context = () => host.Quick.Source.Length > 0
                ? (host.Quick.FromSelection ? Tr($"AI 빠른 동작 · 선택한 글 {host.Quick.Source.Length}자", $"AI actions · selected text, {host.Quick.Source.Length} chars") : Tr($"AI 빠른 동작 · 복사한 글 {host.Quick.Source.Length}자", $"AI actions · copied text, {host.Quick.Source.Length} chars"))
                : Tr("AI 빠른 동작", "AI actions"),
            Build = Engine.QuickView,
            Split = true,
            Live = true,
        },
    };
}
