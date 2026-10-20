using System.Collections.Generic;
using Bloom.Core;

namespace Bloom.Search;

internal sealed class SysEntry
{
    public string Id = "", Title = "", Target = "", Kind = "";
    public string? Subtitle, Args;
    public char Glyph;
    public MatchTarget Match = null!;
}

/// Windows settings pages, admin tools, known folders and the launcher's own commands.
internal static class SystemItems
{
    public const string SettingsApp = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    public static readonly List<SysEntry> Commands = new();
    public static readonly List<SysEntry> All = new();

    static SystemItems()
    {
        Cmd("cmd:ai", Tr("AI 채팅", "AI chat"), Tr("Claude · ChatGPT 대화 창 · 지난 대화 이어가기", "A window for Claude and ChatGPT chats, past ones too"), G.Sparkle, "ai chat claude 클로드 질문 채팅 ask");
        Cmd("cmd:threads", Tr("AI 채팅", "AI chats"), Tr("지난 대화 이어가기", "Pick up a past chat"), G.History, "ai history threads conversations 대화 기록");
        Cmd("cmd:files", Tr("파일 검색", "Search files"), "Everything", G.Search, "search files everything 에브리띵 파일 찾기 find");
        Cmd("cmd:clipboard", Tr("클립보드 기록", "Clipboard history"), Tr("복사한 텍스트 · 이미지 · 파일 다시 붙여넣기", "Paste text, images and files you copied"), G.Paste, "clipboard history clip paste 클립보드 기록 복사 붙여넣기 클립");
        Cmd("cmd:calendar", Tr("캘린더", "Calendar"), Tr("알림 · 리마인더를 달력으로", "Reminders on a calendar"), G.Calendar, "calendar schedule agenda reminders 캘린더 일정 달력 스케줄 알림 리마인더");
        Cmd("cmd:processes", Tr("앱 종료", "End apps"), Tr("CPU · 메모리를 보고 프로그램 끝내기", "See CPU and memory, end programs"), G.Close, "kill process task 프로세스 종료 강제 종료 작업 끝내기 응답 없음 멈춤 메모리 cpu");
        Cmd("cmd:space", "Bloom Space Cleaner", Tr("디스크 공간 지도 · 큰 파일 찾아 정리", "A map of disk space · find and clear big files"), G.Folder, "space cleaner spacesniffer disk usage treemap 디스크 공간 용량 폴더 크기 정리 큰 파일 스페이스 클리너");
        Cmd("cmd:commands", Tr("사용자 명령", "Custom commands"), Tr("스크립트 · 스니펫 · 여러 동작 묶음을 만들고 실행", "Make and run scripts, snippets and chains of actions"), G.Bolt, "my commands scripts snippets chains macros automation powershell batch 내 명령 스크립트 스니펫 상용구 묶음 매크로 자동화 파워셸 배치");
        Cmd("cmd:quicklinks", Tr("브라우저", "Browser"), Tr("사이트 바로 가기 · 단축어 + 검색어로 바로 검색", "Site shortcuts · keyword + words searches right away"), G.Link, "browser quicklinks quick links 브라우저 퀵링크 바로가기 바로 가기 단축어 검색 링크 사이트");
        Cmd("cmd:lens", "Bloom Lens", Tr("화면 영역을 골라 AI에게 묻기 · 번역 · 글자 복사 · 실시간 번역 · 캡처", "Pick an area of the screen: ask AI, translate, copy text, live translation, capture"), G.Page, "lens bloom lens 렌즈 블룸 렌즈 screen capture screenshot 캡처 스크린샷 화면 ai 질문 묻기 ocr 텍스트 추출 글자 읽기 번역 translate 실시간 번역 live");
        Cmd("cmd:capture", Tr("화면 캡처", "Screen capture"), Tr("고른 영역에 펜 · 형광펜으로 표시한 뒤 복사 · Ctrl+V로 붙여넣기 (Bloom Lens)", "Mark up an area with a pen or highlighter, then copy it · paste with Ctrl+V (Bloom Lens)"), G.Camera, "capture screenshot snip screen copy picture print screen annotate draw pen highlighter markup 캡처 캡쳐 스크린샷 화면 캡처 복사 그림 붙여넣기 스샷 펜 형광펜 그리기 표시");
        Cmd("cmd:pin", Tr("화면 고정", "Pin to the screen"), Tr("화면 영역을 그림으로 맨 위에 띄워 두기 (Bloom Lens)", "Keep an area of the screen on top as a picture (Bloom Lens)"), G.Pin, "pin screen capture screenshot float on top sticky picture snip 화면 고정 캡처 스크린샷 띄우기 맨 위 그림 고정 핀 붙이기");
        Cmd("cmd:timer", Tr("타이머 · 알림", "Timers · reminders"), Tr("예: \"10분 타이머\", \"3시 반에 회의 알림\"", "Or type \"10 min timer\", \"remind me at 3:30pm meeting\""), G.Stopwatch, "timer reminder alarm countdown pomodoro 타이머 알림 리마인더 알람 스톱워치 뽀모도로 카운트다운 시간 재기");
        Cmd("cmd:color", Tr("컬러 피커", "Color picker"), Tr("화면에서 색 찍기 · HEX · RGB · 그라데이션 추천", "Pick colours from the screen · HEX · RGB · gradient ideas"), G.Eyedrop, "color colour picker eyedropper hex rgb hsl gradient palette 컬러 피커 색 스포이드 색상 색깔 그라데이션 팔레트 색 추출");
        Cmd("cmd:notes", Tr("메모", "Notes"), Tr("여러 메모 · 적는 대로 저장 · 런처 옆에 붙여 쓰기", "Notes kept as you type · use them beside the launcher"), G.Document, "notes memo notepad sticky jot 메모 메모장 노트 적기 포스트잇");
        Cmd("cmd:player", Tr("미니 플레이어", "Mini player"), Tr("지금 곡 · 재생 · 다음 · 대기열을 런처 옆 작은 창으로 (JRiver · foobar2000 · MusicBee도 가능)", "The song, play, next and the queue in a small window beside the launcher (JRiver, foobar2000 and MusicBee too)"), '\uE8D6', "mini player music song audio play pause next queue jriver foobar foobar2000 musicbee 미니 플레이어 음악 노래 재생 일시정지 다음 곡 대기열 뮤직 플레이어");
        Cmd("cmd:tools", Tr("도구 붙이기", "Attach tools"), Tr("메모 · 타이머 · 계산기 · 클립보드를 런처 옆 창으로 (Ctrl+J)", "Notes, timers, calculator, clipboard as windows beside the launcher (Ctrl+J)"), G.NewWindow, "tools panel side dock widget attach 도구 패널 붙이기 위젯 옆");
        Cmd("cmd:windows", Tr("창 전환", "Switch windows"), Tr("열린 창을 찾아서 앞으로", "Find an open window and bring it to the front"), G.NewWindow, "switch windows window alt tab 창 전환 윈도우 열린 창 작업 전환");
        Cmd("cmd:layout", Tr("창 배치", "Window layout"), Tr("앞의 창을 절반 · 1/3 · 1/4 · 가운데 · 다음 화면으로", "The window in front to a half, a third, a quarter, the middle or the next screen"), '', "window layout snap arrange tile resize move half third quarter maximize center next screen monitor 창 배치 정렬 분할 절반 반 화면 나누기 최대화 가운데 다음 화면 모니터 스냅");
        Cmd("cmd:winget", Tr("앱 설치 (winget)", "Install apps (winget)"), Tr("winget · Microsoft Store 목록에서 찾아 설치", "Find and install from winget and the Microsoft Store"), G.Download, "install apps winget package manager store download setup 앱 설치 프로그램 설치 다운로드 패키지 윈겟 스토어 깔기");
        Cmd("cmd:emoji", Tr("이모지 · 기호", "Emoji · symbols"), Tr("한국어 · 영어로 찾아서 바로 붙여넣기", "Search in Korean or English and paste"), '\uE76E', "emoji emoticon symbol special character 이모지 이모티콘 기호 특수문자 특수 문자 화살표 하트");
        Cmd("cmd:recent", Tr("최근 파일", "Recent files"), Tr("Windows가 기억하는 최근에 연 파일 · 폴더", "Files and folders Windows remembers you opened"), G.History, "recent files documents opened 최근 파일 최근 문서 열어본 최근 항목");
        Cmd("cmd:system", Tr("시스템", "System"), Tr("다크 모드 · 소리 · 블루투스 · Wi-Fi · 절전 …", "Dark mode · sound · Bluetooth · Wi-Fi · sleep …"), '\uE770', "system toggles quick settings 시스템 빠른 설정 토글 켜기 끄기");
        Cmd("cmd:quick", Tr("AI 빠른 동작", "AI actions"), Tr("선택하거나 복사한 글을 요약 · 번역 · 다듬기", "Summarize, translate or polish the selected or copied text"), G.Sparkle, "ai quick actions summarize translate rewrite fix grammar 빠른 동작 요약 번역 다듬기 맞춤법 교정");
        Cmd("cmd:welcome", Tr("Bloom 둘러보기", "Bloom tour"), Tr("처음 설정과 사용법 안내를 다시 보기", "See the first-run setup and the guide again"), G.Info, "welcome tutorial onboarding guide help 둘러보기 튜토리얼 안내 처음 사용법 도움말 시작하기");
        Cmd("cmd:export", Tr("설정 내보내기", "Export settings"), Tr("설정 · 즐겨찾기 · 앱 이름 · 브라우저 바로 가기를 파일 하나로", "Settings, favorites, app names and browser shortcuts in one file"), G.Send, "export settings backup 설정 내보내기 백업 저장 옮기기");
        Cmd("cmd:import", Tr("설정 불러오기", "Import settings"), Tr("내보낸 설정 파일에서 가져오기", "From an exported settings file"), G.Import, "import settings restore 설정 불러오기 가져오기 복원 옮기기");
        Cmd("cmd:settings", Tr("Bloom 설정", "Bloom settings"), null, G.Settings, "settings preferences 설정 환경설정 options config bloom 블룸");
        Cmd("cmd:reindex", Tr("앱 목록 새로고침", "Refresh the app list"), null, G.Refresh, "reload refresh apps reindex 새로고침");
        Cmd("cmd:lock", Tr("화면 잠금", "Lock screen"), Tr("Windows 잠금", "Lock Windows"), G.Lock, "lock screen 잠금 잠그기");
        Cmd("cmd:restart", Tr("다시 시작", "Restart"), Tr("PC 다시 시작", "Restart the PC"), G.Restart, "restart reboot 재시작 다시 시작 재부팅 리부트");
        Cmd("cmd:shutdown", Tr("시스템 종료", "Shut down"), Tr("PC 끄기", "Turn off the PC"), G.Power, "shut down shutdown power off 시스템 종료 컴퓨터 끄기 전원 끄기");
        Cmd("cmd:quit", Tr("Bloom 종료", "Quit Bloom"), null, G.Power, "quit exit close 종료 bloom 블룸"); // "끄기" means the PC (시스템 종료)

        Page(Tr("디스플레이", "Display"), "ms-settings:display", "display screen resolution scale 화면 해상도 배율 모니터");
        Page(Tr("야간 모드", "Night light"), "ms-settings:nightlight", "night light 블루라이트");
        Page(Tr("소리", "Sound"), "ms-settings:sound", "sound audio volume 볼륨 스피커 마이크 오디오");
        Page(Tr("소리 장치", "Sound devices"), "ms-settings:sound-devices", "sound devices output input 출력 입력");
        Page(Tr("앱 볼륨 및 장치", "App volume and devices"), "ms-settings:apps-volume", "app volume mixer 볼륨 믹서");
        Page(Tr("알림", "Notifications"), "ms-settings:notifications", "notifications 알림 끄기");
        Page(Tr("집중 모드", "Focus"), "ms-settings:quiethours", "focus assist do not disturb 방해 금지");
        Page(Tr("전원 및 절전", "Power & sleep"), "ms-settings:powersleep", "power sleep battery 전원 절전 배터리");
        Page(Tr("저장소", "Storage"), "ms-settings:storagesense", "storage disk 저장 공간 디스크");
        Page(Tr("멀티태스킹", "Multitasking"), "ms-settings:multitasking", "multitasking snap 창 끌기 스냅");
        Page(Tr("클립보드", "Clipboard"), "ms-settings:clipboard", "clipboard history 클립보드 기록");
        Page(Tr("정보", "About"), "ms-settings:about", "about pc system info 시스템 정보 사양 이름");
        Page(Tr("Bluetooth 및 장치", "Bluetooth & devices"), "ms-settings:bluetooth", "bluetooth devices 블루투스 장치");
        Page(Tr("프린터 및 스캐너", "Printers & scanners"), "ms-settings:printers", "printers scanners 프린터 스캐너");
        Page(Tr("마우스", "Mouse"), "ms-settings:mousetouchpad", "mouse pointer 마우스 포인터");
        Page(Tr("입력", "Typing"), "ms-settings:typing", "typing keyboard 키보드 입력");
        Page("Wi-Fi", "ms-settings:network-wifi", "wifi wireless 와이파이 무선");
        Page(Tr("네트워크 및 인터넷", "Network & internet"), "ms-settings:network", "network internet ethernet 네트워크 인터넷 이더넷");
        Page("VPN", "ms-settings:network-vpn", "vpn");
        Page(Tr("프록시", "Proxy"), "ms-settings:network-proxy", "proxy 프록시");
        Page(Tr("모바일 핫스팟", "Mobile hotspot"), "ms-settings:network-mobilehotspot", "hotspot 핫스팟 테더링");
        Page(Tr("배경", "Background"), "ms-settings:personalization-background", "wallpaper background 배경화면 바탕화면");
        Page(Tr("색", "Colors"), "ms-settings:colors", "colors accent dark mode 색 다크 모드 테마 색상");
        Page(Tr("테마", "Themes"), "ms-settings:themes", "themes 테마");
        Page(Tr("잠금 화면", "Lock screen"), "ms-settings:lockscreen", "lock screen 잠금 화면");
        Page(Tr("작업 표시줄", "Taskbar"), "ms-settings:taskbar", "taskbar 작업 표시줄");
        Page(Tr("시작", "Start"), "ms-settings:personalization-start", "start menu 시작 메뉴");
        Page(Tr("글꼴", "Fonts"), "ms-settings:fonts", "fonts 폰트 글꼴");
        Page(Tr("설치된 앱", "Installed apps"), "ms-settings:appsfeatures", "apps features uninstall installed 앱 제거 프로그램 설치");
        Page(Tr("기본 앱", "Default apps"), "ms-settings:defaultapps", "default apps browser 기본 앱 기본 브라우저");
        Page(Tr("시작 프로그램", "Startup apps"), "ms-settings:startupapps", "startup apps 시작 프로그램 자동 실행");
        Page(Tr("계정 정보", "Your info"), "ms-settings:yourinfo", "account 계정 사용자");
        Page(Tr("로그인 옵션", "Sign-in options"), "ms-settings:signinoptions", "sign in pin password hello 로그인 비밀번호 핀");
        Page(Tr("날짜 및 시간", "Date & time"), "ms-settings:dateandtime", "date time clock 날짜 시간 시계");
        Page(Tr("언어 및 지역", "Language & region"), "ms-settings:regionlanguage", "language region 언어 지역 한국어");
        Page(Tr("게임 모드", "Game Mode"), "ms-settings:gaming-gamemode", "game mode 게임");
        Page(Tr("게임 바", "Game Bar"), "ms-settings:gaming-gamebar", "xbox game bar 게임 바 녹화");
        Page(Tr("접근성", "Accessibility"), "ms-settings:easeofaccess", "accessibility 접근성 돋보기");
        Page(Tr("개인 정보 및 보안", "Privacy & security"), "ms-settings:privacy", "privacy security 개인정보 보안 권한");
        Page(Tr("마이크 권한", "Microphone permissions"), "ms-settings:privacy-microphone", "microphone permission 마이크 권한");
        Page(Tr("카메라 권한", "Camera permissions"), "ms-settings:privacy-webcam", "camera permission 카메라 권한 웹캠");
        Page(Tr("Windows 보안", "Windows Security"), "windowsdefender:", "defender antivirus security 바이러스 보안");
        Page(Tr("Windows 업데이트", "Windows Update"), "ms-settings:windowsupdate", "windows update 업데이트");
        Page(Tr("그래픽", "Graphics"), "ms-settings:display-advancedgraphics", "graphics gpu 그래픽 gpu 설정");

        Tool(Tr("제어판", "Control Panel"), "control.exe", null, "control panel 제어판");
        Tool(Tr("장치 관리자", "Device Manager"), "devmgmt.msc", null, "device manager 장치 드라이버");
        Tool(Tr("작업 관리자", "Task Manager"), "taskmgr.exe", null, "task manager taskmgr 작업 관리자 프로세스");
        Tool(Tr("서비스", "Services"), "services.msc", null, "services 서비스");
        Tool(Tr("디스크 관리", "Disk Management"), "diskmgmt.msc", null, "disk management 파티션 디스크");
        Tool(Tr("이벤트 뷰어", "Event Viewer"), "eventvwr.msc", null, "event viewer logs 이벤트 로그");
        Tool(Tr("컴퓨터 관리", "Computer Management"), "compmgmt.msc", null, "computer management 컴퓨터 관리");
        Tool(Tr("작업 스케줄러", "Task Scheduler"), "taskschd.msc", null, "task scheduler 스케줄러 예약");
        Tool(Tr("레지스트리 편집기", "Registry Editor"), "regedit.exe", null, "registry regedit 레지스트리");
        Tool(Tr("명령 프롬프트", "Command Prompt"), "cmd.exe", null, "cmd command prompt 명령 프롬프트 콘솔");
        Tool(Tr("시스템 정보", "System Information"), "msinfo32.exe", null, "system information msinfo 시스템 정보");
        Tool(Tr("리소스 모니터", "Resource Monitor"), "resmon.exe", null, "resource monitor resmon 리소스");
        Tool(Tr("프로그램 및 기능", "Programs and Features"), "appwiz.cpl", null, "programs features uninstall 프로그램 제거");
        Tool(Tr("네트워크 연결", "Network Connections"), "ncpa.cpl", null, "network connections adapter 어댑터 네트워크");
        Tool(Tr("시스템 속성", "System Properties"), "sysdm.cpl", null, "system properties 시스템 속성");
        Tool(Tr("환경 변수", "Environment Variables"), "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables", "environment variables path 환경 변수");
        Tool(Tr("전원 옵션", "Power Options"), "powercfg.cpl", null, "power options 전원 옵션 고성능");

        Folder(Tr("다운로드", "Downloads"), "shell:Downloads", "downloads 다운로드");
        Folder(Tr("문서", "Documents"), "shell:Personal", "documents 문서");
        Folder(Tr("바탕 화면", "Desktop"), "shell:Desktop", "desktop 바탕화면");
        Folder(Tr("사진", "Pictures"), "shell:My Pictures", "pictures photos 사진");
        Folder(Tr("동영상", "Videos"), "shell:My Video", "videos 동영상 비디오");
        Folder(Tr("음악", "Music"), "shell:My Music", "music 음악");
        Folder(Tr("이 PC", "This PC"), "shell:MyComputerFolder", "this pc my computer 내 PC 내 컴퓨터 드라이브");
        Folder(Tr("휴지통", "Recycle Bin"), "shell:RecycleBinFolder", "recycle bin trash 휴지통");
        Folder(Tr("시작프로그램 폴더", "Startup folder"), "shell:Startup", "startup folder 시작프로그램");
        Folder("AppData", Paths.AppData, "appdata roaming");
        Folder(Tr("임시 폴더", "Temp folder"), System.IO.Path.GetTempPath(), "temp 임시");
    }

    private static void Cmd(string id, string title, string? sub, char glyph, string aliases)
    {
        var e = new SysEntry { Id = id, Title = title, Subtitle = sub, Kind = "cmd", Glyph = glyph, Target = id, Match = new MatchTarget(title, Split(aliases)) };
        Commands.Add(e);
        All.Add(e);
    }

    private static void Page(string title, string uri, string aliases) =>
        All.Add(new SysEntry { Id = "set:" + uri, Title = title, Subtitle = Tr("Windows 설정", "Windows Settings"), Kind = "setting", Target = uri, Match = new MatchTarget(title, Split(aliases)) });

    private static void Tool(string title, string exe, string? args, string aliases) =>
        All.Add(new SysEntry { Id = "tool:" + exe + (args ?? ""), Title = title, Subtitle = Tr("Windows 도구", "Windows tool"), Kind = "tool", Target = exe, Args = args, Match = new MatchTarget(title, Split(aliases)) });

    private static void Folder(string title, string path, string aliases) =>
        All.Add(new SysEntry { Id = "folder:" + path.ToLowerInvariant(), Title = title, Subtitle = Tr("폴더", "Folder"), Kind = "folder", Target = path, Match = new MatchTarget(title, Split(aliases)) });

    private static IEnumerable<string> Split(string s) => new[] { s };

    public static SysEntry? Find(string id) => All.Find(e => e.Id == id);
}

/// Segoe Fluent Icons code points used across the UI (Sparkle is drawn as a vector instead).
internal static class G
{
    public const char Sparkle = '✦';
    public const char Grip = '\uE700', Minus = '\uE738'; // 홈 편집: a block's drag handle (≡), a count's −
    public const char View = '\uE890'; // 메모 · 문서: 보기 (the Markdown drawn); 편집 is Pen

    // (written as escapes: the notices and 성능)
    public const char Bell = '', Speed = '', Warning = '', Done = '', Code = '', Volume = '',
        Sync = '', Sliders = '', Gears = '', Clock = '', Screen = '', Bolt = '';
    public const char Stopwatch = '', Eyedrop = '', Pin = '', Camera = '', Attach = '';
    public const char Download = '';
    public const char Account = ''; // 설정 > AI 계정 (Contact)
    public const char Pen = '', Highlight = '', Erase = '', Undo = '', Redo = '', Save = ''; // 화면 캡처's drawing bar
    public const char Restart = '', Calendar = '', Bulb = '', Palette = '', Import = '', Tag = '';
    public const char Search = '', Settings = '', Globe = '', History = '', Refresh = '',
        Lock = '', Power = '', Calculator = '', Folder = '', Document = '', Link = '',
        Copy = '', Star = '', StarFill = '', Unstar = '', Delete = '', NewWindow = '',
        Back = '', Send = '', Close = '', Tab = '', Bookmark = '', Shield = '',
        Up = '', Down = '', Left = '', Right = '', Add = '', Check = '', Info = '', Chat = '', Plug = '',
        Stop = '', Page = '', Keyboard = '', App = '', Paste = '', Model = '';
}
