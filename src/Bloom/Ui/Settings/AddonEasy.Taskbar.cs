using System;

namespace Bloom.Ui;

/// 작업표시줄 (PillTaskbar)'s settings in Bloom's words: the add-on ships them in Korean and English only, so these are
/// what the page shows in every language (Bloom's tables carry the Japanese and Chinese). Where the page words a line
/// its own way (SettingsView.Taskbar: 크기, 배경, 재생 중인 곡, …) that wins; a key with no entry here, a newer add-on's
/// setting among them, shows the schema's.
internal static partial class AddonEasy
{
    private static Line? Taskbar(string key) => key switch
    {
        // 모양
        "start.mode" => new()
        {
            Title = Tr("시작 버튼", "Start button"),
            Option = v => v switch { "windows" => Tr("그대로", "Unchanged"), "hidden" => Tr("숨기기", "Hidden"), "bloom" => Tr("Bloom 버튼", "Bloom button"), _ => null },
        },
        "pill.edge" => new() { Option = v => v switch { "top" => Tr("위", "Top"), "bottom" => Tr("아래", "Bottom"), _ => null } },
        "pill.align" => new() { Option = Side },
        "size.taskbarHeight" => new() { Title = Tr("작업표시줄 높이", "Taskbar height"), Sub = Tr("Windows 기본은 48입니다", "Windows' own is 48") },
        "size.iconSize" => new() { Title = Tr("아이콘 크기", "Icon size"), Sub = Tr("Windows 기본은 24입니다", "Windows' own is 24") },
        "size.iconSpacing" => new()
        {
            Title = Tr("아이콘 간격", "Icon spacing"),
            Sub = Tr("앱 단추 폭은 아이콘 크기에 간격을 더한 값입니다. Windows 기본은 20입니다", "An app button is the icon size plus this wide; Windows' own is 20"),
        },
        "pill.cornerRadius" => new() { Title = Tr("모서리 둥글기", "Corner radius"), Sub = "" },
        "pill.blur" => new() { Title = Tr("배경 흐림", "Background blur"), Sub = "" },
        "pill.tintOpacity" => new() { Title = Tr("배경 색 진하기", "Background tint"), Sub = "" },
        "pill.enabled" => new() { Title = Tr("알약 모양", "Pill shape"), Sub = Tr("끄면 Windows 기본 모양으로 돌아갑니다", "Off, the taskbar looks as Windows draws it") },

        // 트레이: the chips' names, then the clock and 자세히
        "tray.hideVolume" => Chip(Tr("볼륨", "Volume")),
        "tray.hideNetwork" => Chip(Tr("네트워크", "Network")),
        "tray.hideBattery" => Chip(Tr("배터리", "Battery")),
        "tray.hideMicrophone" => Chip(Tr("마이크", "Microphone")),
        "tray.hideCopilot" => Chip("Copilot"),
        "tray.bell" => Chip(Tr("알림 벨", "Notification bell")),
        "tray.hideLocation" => Chip(Tr("위치", "Location")),
        "tray.hideStudioEffects" => Chip("Studio Effects"),
        "tray.hideRecall" => Chip("Recall"),
        "tray.hideLanguageBar" => Chip(Tr("언어 표시줄", "Language bar")),
        "tray.hideLanguageExtras" => Chip(Tr("언어 보조 아이콘", "Extra language icons")),
        "tray.clock" => new()
        {
            Title = Tr("시계", "Clock"),
            Option = v => v switch { "timeDate" => Tr("시간과 날짜", "Time and date"), "time" => Tr("시간만", "Time only"), "hidden" => Tr("숨김", "Hidden"), _ => null },
        },
        "tray.clockFormat" => new() { Title = Tr("시간 표기", "Time format") },
        "media.maxWidth" => new() { Title = Tr("곡 제목 최대 폭", "Longest track title"), Sub = Tr("넘치는 제목은 …로 줄입니다", "A longer title ends in …") },
        "usage.gapBefore" => new() { Title = Tr("앱과 사용량 사이", "Gap after the apps"), Sub = "" },
        "usage.gapAfter" => new() { Title = Tr("사용량과 트레이 사이", "Gap before the tray"), Sub = "" },
        "usage.sessions" => new()
        {
            Title = Tr("자세한 창에 세션 보기", "Sessions in the details"),
            Sub = Tr("사용량을 눌러 여는 창에 이 PC의 Claude Code·Codex 세션을 보여 줍니다", "The window a click on the usage opens lists this PC's Claude Code and Codex sessions"),
        },
        "tray.panelsNearTray" => new()
        {
            Title = Tr("Windows 패널을 트레이 옆에", "Windows panels by the tray"),
            Sub = Tr("빠른 설정과 알림 센터를 화면 끝이 아니라 알약 트레이 옆에 엽니다", "Quick Settings and the notification center open by the pill's tray, not at the screen's end"),
        },
        "tray.centerStill" => new()
        {
            Title = Tr("알림 센터 밀려 들어오기 끄기", "Notification center without the slide"),
            Sub = Tr("알림 센터와 달력이 옆에서 밀려 들어오지 않고 바로 나타납니다", "The notification center and calendar appear in place instead of sliding in"),
        },

        // 숨김 아일랜드
        "island.showClock" => Chip(Tr("시계", "Clock")),
        "island.showMedia" => Chip(Tr("재생 중인 곡", "Now playing")),
        "island.showUsage" => Chip(Tr("AI 사용량", "AI usage")),
        "island.showMemory" => Chip(Tr("메모리 사용률", "Memory use")),
        "island.showCpu" => Chip(Tr("CPU 사용률", "CPU use")),
        "island.height" => new() { Title = Tr("높이", "Height"), Sub = Tr("100% 배율 기준입니다", "At 100% scale") },
        "island.width" => new() { Title = Tr("폭", "Width"), Sub = Tr("100% 배율 기준입니다. 넘치면 곡 제목을 줄입니다", "At 100% scale; a track title that doesn't fit is shortened") },
        "island.clockAt" => new()
        {
            Title = Tr("시계 자리", "Where the clock sits"),
            Sub = Tr("가운데 아일랜드, 또는 화면 왼쪽·오른쪽 모서리의 아일랜드에 둡니다", "On the middle island, or on one in the screen's left or right corner"),
            Option = Side,
        },
        "island.mediaAt" => new() { Title = Tr("재생 중인 곡 자리", "Where now playing sits"), Sub = "", Option = Side },
        "island.usageAt" => new() { Title = Tr("AI 사용량 자리", "Where AI usage sits"), Sub = "", Option = Side },
        "island.memoryAt" => new() { Title = Tr("메모리 사용률 자리", "Where memory use sits"), Sub = "", Option = Side },
        "island.cpuAt" => new() { Title = Tr("CPU 사용률 자리", "Where CPU use sits"), Sub = "", Option = Side },
        "autohide.revealZone" => new()
        {
            Title = Tr("나오는 구간", "Reveal zone"),
            Sub = Tr("가장자리의 이만큼에서만 포인터로 작업표시줄이 나옵니다. 100이면 가장자리 전체입니다", "The pointer brings the taskbar out only on this much of the edge; 100 is all of it"),
        },
        "autohide.showDelay" => new() { Title = Tr("나오기까지", "Wait before showing"), Sub = "" },
        "autohide.hideDelay" => new() { Title = Tr("숨기까지", "Wait before hiding"), Sub = Tr("포인터가 떠난 뒤부터 잽니다", "Counted from when the pointer leaves") },
        "autohide.animation" => new() { Title = Tr("움직임 길이", "Animation length"), Sub = "" },
        "autohide.keepHiddenForBloom" => new()
        {
            Title = Tr("Bloom 런처가 열려도 숨긴 채로", "Stay hidden for the Bloom launcher"),
            Sub = Tr("끄면 런처를 열 때 그 모니터의 작업표시줄도 함께 나옵니다", "Off, opening the launcher brings that monitor's taskbar out too"),
        },
        "autohide.keepHiddenOnFlash" => new()
        {
            Title = Tr("앱이 깜박여도 숨긴 채로", "Stay hidden when an app flashes"),
            Sub = Tr("끄면 Windows처럼 나와서 그 앱을 열 때까지 있습니다", "Off, it comes out as in Windows and stays until that app is opened"),
        },

        // 폴더
        "folders.autoRunning" => new()
        {
            Title = Tr("실행 중 폴더", "Running folder"),
            Sub = Tr("고정하지 않고 켠 앱을 시계 모양 폴더 하나에 모읍니다", "Apps started without pinning gather in one clock-shaped folder"),
        },
        "folders.runningName" => new() { Title = Tr("실행 중 폴더 이름", "Running folder's name"), Sub = "" },
        "folders.foldersPath" => new()
        {
            Title = Tr("폴더 위치", "Folder location"),
            Sub = Tr("이 폴더 안의 폴더 하나가 작업표시줄 폴더 하나가 됩니다", "Each folder inside it becomes one taskbar folder"),
        },
        "folders.mergeDelay" => new() { Title = Tr("합쳐지기까지", "Wait before merging"), Sub = Tr("앱을 다른 앱 위에 이만큼 두어야 합쳐집니다", "How long an app stays over another before they merge") },
        "folders.popupScale" => new() { Title = Tr("열린 폴더 크기", "Open folder size"), Sub = "" },
        "folders.showLabels" => new() { Title = Tr("앱 이름 보이기", "App names"), Sub = Tr("끄면 칸이 아이콘 크기로 줄어듭니다", "Off, the cells shrink to the icons") },
        "folders.iconSize" => new() { Title = Tr("폴더 안 아이콘 크기", "Icon size in folders"), Sub = "" },
        "folders.appSpacing" => new() { Title = Tr("앱 사이 간격", "Space between apps"), Sub = "" },
        "folders.popupSide" => new()
        {
            Title = Tr("폴더가 열리는 쪽", "Folder opens"),
            Sub = "",
            Option = v => v switch
            {
                "auto" => Tr("작업표시줄 반대쪽", "Away from the taskbar"),
                "above" => Tr("작업표시줄 위", "Above the taskbar"),
                "below" => Tr("작업표시줄 아래", "Below the taskbar"),
                _ => null,
            },
        },
        "folders.popupGap" => new() { Title = Tr("작업표시줄과의 거리", "Distance from the taskbar"), Sub = "" },
        "folders.cornerRadius" => new() { Title = Tr("모서리 둥글기", "Corner radius"), Sub = "" },
        "folders.cornerSmoothing" => new()
        {
            Title = Tr("모서리 부드러움", "Corner smoothing"),
            Sub = Tr("0이면 보통 둥근 모서리, 60이면 끊김 없이 이어진 곡선입니다", "0 is an ordinary round corner, 60 a continuous curve"),
        },
        _ => null,
    };

    /// A tray or island chip: its name only.
    private static Line Chip(string name) => new() { Title = name };

    /// 왼쪽 · 가운데 · 오른쪽 (the pill's alignment, an island item's place). "Middle", not "Center": Bloom's tables
    /// key on the English, and "Center" is already 가운데로 (WindowLayout).
    private static string? Side(string v) => v switch
    {
        "left" => Tr("왼쪽", "Left"),
        "center" => Tr("가운데", "Middle"),
        "right" => Tr("오른쪽", "Right"),
        _ => null,
    };
}
