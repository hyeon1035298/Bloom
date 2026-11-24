using Bloom.Search;

namespace Bloom.Core;

/// Bloom's features the user can switch off (설정 > 기능) when they don't use them. One that is off leaves the search
/// results, the command list, the home and the favorites row, and its shortcut is let go; nothing it saved is deleted
/// (timers already set still ring), and switching it on brings everything back. The clipboard history and the work
/// notices keep their own switches (Settings.ClipHistory / Notices), as they run in the background.
internal static class Features
{
    /// key, name, what it is and what leaves with it, glyph — grouped as the settings page shows them.
    public static readonly (string group, string key, string name, string about, char glyph)[] All =
    {
        ("ai", "chat", Tr("AI 채팅", "AI Chat"), Tr("Tab · 대화 창 · 검색 끝의 ‘AI에게 묻기’", "Tab · Chat window · ‘Ask AI’ fallback in search"), G.Chat),
        ("ai", "lens", "Bloom Lens", Tr("화면을 골라 AI에게 묻기 · 번역 · 글자 복사 · 단축키 5개", "Select screen region to ask AI · Translate · Copy text · 5 shortcuts"), G.Page),
        ("ai", "quick", Tr("AI 빠른 동작", "AI Quick Actions"), Tr("선택한 글 요약 · 번역 · 다듬기와 그 단축키", "Summarize, translate, or polish selected text and its shortcut"), G.Sparkle),
        ("search", "calc", Tr("계산기 · 단위 · 환율", "Calculator · Units · Currency"), Tr("검색줄에 식을 적으면 바로 계산 (12*3, 5km to mi, 100 usd)", "Instant calculations in the search bar (12*3, 5km to mi, 100 usd)"), G.Calculator),
        ("search", "dates", Tr("날짜 · 세계 시간", "Dates · World Clock"), Tr("‘100일 뒤’ ‘크리스마스까지’ ‘뉴욕 시간’ ‘도쿄 오후 3시’를 바로 계산", "Instant answers for ‘in 100 days’, ‘until Christmas’, ‘New York time’, ‘Tokyo 3pm’"), G.Calendar),
        ("search", "timers", Tr("타이머 · 알림", "Timers · Reminders"), Tr("‘10분 타이머’ ‘오후 3시 회의’ · 홈 타이머 블록 · 설정된 타이머 유지", "‘10m timer’, ‘3pm meeting’ · Home timer block · Active timers keep running"), G.Stopwatch),
        ("search", "calendar", Tr("캘린더", "Calendar"), Tr("월 달력과 홈의 ‘이번 주’ 블록", "Monthly calendar and Home ‘This Week’ block"), G.Calendar),
        ("search", "color", Tr("컬러 피커", "Color Picker"), Tr("화면 색 찍기 · 색 코드 검색 · 그라데이션 추천과 그 단축키", "Screen color picker · Color code lookup · Gradient suggestions and shortcut"), G.Eyedrop),
        ("search", "emoji", Tr("이모지 · 기호", "Emoji · Symbols"), Tr("‘하트’ ‘화살표’로 찾아 붙여넣기와 그 단축키", "Search and paste by name like ‘heart’ or ‘arrow’ and its shortcut"), ''),
        ("search", "system", Tr("볼륨 · 시스템 조작", "Volume · System Controls"), Tr("‘볼륨 30’ · 다크 모드 · 블루투스 같은 켜고 끄기", "‘volume 30’ · Toggle Dark Mode, Bluetooth, and more"), G.Volume),
        ("search", "quicklinks", Tr("사이트 바로 가기", "Site Shortcuts"), Tr("‘yt 고양이’처럼 단축어로 사이트 검색", "Search websites with keywords like ‘yt cats’"), G.Link),
        ("search", "mine", Tr("사용자 명령", "Custom Commands"), Tr("직접 만든 스크립트 · 글 조각 · 연속 실행", "Custom scripts · Text snippets · Multi-step actions"), G.Code),
        ("tools", "notes", Tr("메모장 · 도구 창", "Notes · Tool Windows"), Tr("메모 · 타이머 · 계산기 · 클립보드를 런처 옆 창으로 · 검색줄에서 메모 찾기", "Dock Notes, Timer, Calculator, and Clipboard beside the launcher · Search notes from the search bar"), G.Document),
        ("tools", "player", Tr("미니 플레이어", "Mini Player"), Tr("오디오 파일을 직접 재생하거나 JRiver · foobar2000 · MusicBee로 · 런처 옆 작은 창 · 대기열 · 파일 검색 결과의 ‘미니 플레이어로 재생’", "Play audio directly or via JRiver, foobar2000, or MusicBee · Side mini window · Queue · ‘Play in Mini Player’ in file search"), '\uE8D6'),
        ("tools", "docs", Tr("마크다운 문서 (.md)", "Markdown Documents (.md)"), Tr("탐색기의 ‘연결 프로그램’에 Bloom · 검색한 .md 파일의 ‘Bloom으로 보기’", "Adds Bloom to File Explorer ‘Open with’ · ‘View in Bloom’ for .md files in search"), G.Page),
        ("tools", "windows", Tr("창 전환", "Window Switcher"), Tr("열린 창 목록 · 검색 결과 아래의 창과 그 단축키", "Open windows list · Matching windows in search results and shortcut"), G.NewWindow),
        ("tools", "layout", Tr("창 배치", "Window Layout"), Tr("‘왼쪽 절반’ ‘최대화’ ‘다음 화면으로’처럼 앞의 창 옮기기", "Snap or move the active window like ‘Left Half’, ‘Maximize’, ‘Next Display’"), ''),
        ("tools", "winget", Tr("앱 설치 (winget)", "Install Apps (winget)"), Tr("‘설치 vlc’처럼 찾아서 Enter로 설치 — Windows의 winget으로, 할 때마다 확인 창이 표시됩니다", "Search like ‘install vlc’ and press Enter — installs via Windows winget with a confirmation prompt each time"), G.Download),
        ("tools", "processes", Tr("프로세스 종료", "End Processes"), Tr("멈춘 프로그램 찾아서 끄기", "Find and force quit unresponsive apps"), G.Gears),
        ("tools", "space", "Bloom Space Cleaner", Tr("디스크를 지도로 보고 큰 파일 정리", "Visualize disk space and clean up large files"), G.Folder),
    };

    public static bool On(string key) => !Settings.Current.FeaturesOff.Contains(key);

    public static void Set(string key, bool on)
    {
        var off = Settings.Current.FeaturesOff;
        if (on) off.Remove(key);
        else if (!off.Contains(key)) off.Add(key);
        Settings.Save();
    }

    /// The feature a command, a favorite or a home block belongs to (null: always there).
    public static string? Of(string id) => id switch
    {
        "cmd:ai" or "cmd:threads" or "threads" => "chat",
        "cmd:lens" or "cmd:pin" or "cmd:capture" or "cmd:record" => "lens",
        "cmd:quick" => "quick",
        "cmd:timer" or "timers" => "timers",
        "cmd:calendar" or "week" => "calendar",
        "cmd:color" => "color",
        "cmd:emoji" => "emoji",
        "cmd:system" => "system",
        _ when id.StartsWith("sys:", System.StringComparison.Ordinal) => "system", // a switch pinned as a favorite
        "cmd:quicklinks" => "quicklinks",
        "cmd:commands" => "mine",
        "cmd:notes" or "cmd:tools" or "notes" => "notes",
        "cmd:player" or "player" => "player",
        "cmd:windows" => "windows",
        "cmd:layout" => "layout",
        "cmd:winget" => "winget",
        _ when id.StartsWith("layout:", System.StringComparison.Ordinal) => "layout", // pinned or given a shortcut
        "cmd:processes" => "processes",
        "cmd:space" => "space",
        _ => null,
    };

    /// The command, favorite or home block shows: it belongs to no feature, or to one that is on.
    public static bool Shows(string id) => Of(id) is not { } f || On(f);
}
