using System;
using Bloom.Addons;

namespace Bloom.Ui;

/// How Bloom words and lays out the settings of the add-ons it ships (설정 > 애드온, the pages drawn from a schema): a
/// plainer name and line than the schema's, plainer option names, a line shown only while the switch or choice it
/// belongs to calls for it, rarely changed lines under 자세히 (an option's picture, where it has one, is AddonPictures').
/// Data only; the page reads it (SettingsView.AddonSettings). A key with no entry, a newer add-on's setting among them, is drawn from its schema.
internal static partial class AddonEasy
{
    internal sealed record Line
    {
        public string? Title { get; init; }
        public string? Sub { get; init; }

        /// An option's own name by its value (null: the schema's).
        public Func<string, string?>? Option { get; init; }

        /// Shown only while this holds, indented under the line it belongs to; searching finds it either way.
        public Func<AddonValues, bool>? When { get; init; }

        /// Under the group's 자세히.
        public bool More { get; init; }

        /// A number of minutes past an hour: typed in minutes or hours (DurationField).
        public bool Minutes { get; init; }
    }

    public static Line? Of(string addon, string key) => (addon, key) switch
    {
        // ── 모니터별 Alt+Tab ──
        ("alt-tab-per-monitor", "switcher.style") => new()
        {
            Title = Tr("모양", "Look"),
            Sub = Tr("관리자 권한 창이 앞에 있으면 Windows 모양으로 뜹니다", "With an administrator window in front, Windows' own shows"),
            Option = v => v switch { "windows" => Tr("창 미리 보기", "Window previews"), "icons" => Tr("앱 아이콘", "App icons"), _ => null },
        },
        ("alt-tab-per-monitor", "switcher.iconSize") => new()
        {
            Title = Tr("아이콘 크기", "Icon size"),
            Sub = "",
            Option = v => v switch { "small" => Tr("작게", "Small"), "medium" => Tr("보통", "Medium"), "large" => Tr("크게", "Large"), "huge" => Tr("아주 크게", "Huge"), _ => null },
            When = AppIcons,
        },
        ("alt-tab-per-monitor", "switcher.titles") => new()
        {
            Title = Tr("창 이름", "Window names"),
            Sub = "",
            Option = v => v switch { "none" => Tr("안 보임", "None"), "selected" => Tr("고른 창만", "The chosen one"), "all" => Tr("모두", "All"), _ => null },
            When = AppIcons,
        },
        ("alt-tab-per-monitor", "monitor.altTabLocation") => Location(Tr("Alt+Tab이 뜨는 곳", "Where Alt+Tab shows")),
        ("alt-tab-per-monitor", "monitor.altTabWindows") => Listed(Tr("보일 창", "Windows listed")),
        ("alt-tab-per-monitor", "monitor.winAltTabLocation") => Location(Tr("Win+Alt+Tab이 뜨는 곳", "Where Win+Alt+Tab shows")),
        ("alt-tab-per-monitor", "monitor.winAltTabWindows") => Listed(Tr("Win+Alt+Tab에 보일 창", "Windows Win+Alt+Tab lists")),

        // ── 메모리 정리 ──
        ("memory-clean", "widget.icon") => new()
        {
            Title = Tr("아이콘 모양", "Icon look"),
            Sub = Tr("숫자는 지금 쓰는 양입니다", "The number is what is in use now"),
            Option = v => v switch { "0" => Tr("선", "Outline"), "1" => Tr("채움", "Filled"), "2" => Tr("꽉 채움", "Solid"), _ => null },
        },
        ("memory-clean", "widget.show") => new()
        {
            Title = Tr("보일 값", "Value shown"),
            Sub = Tr("아이콘을 오른쪽 클릭해도 바뀝니다", "Right-clicking the icon changes it too"),
            Option = v => v switch { "0" => Tr("메모리", "Memory"), "1" => "CPU", "2" => Tr("둘 다", "Both"), _ => null },
        },
        ("memory-clean", "widget.refreshSeconds") => new() { Title = Tr("새로 고침 간격", "Refresh every"), Sub = "", More = true },
        ("memory-clean", "auto.percentOn") => new()
        {
            Title = Tr("메모리가 많이 차면 정리", "Clean when memory fills up"),
            Sub = Tr("30초에 한 번까지", "At most once every 30 seconds"),
        },
        ("memory-clean", "auto.percent") => new() { Title = Tr("기준", "At"), When = v => v.Bool("auto.percentOn") },
        ("memory-clean", "auto.intervalOn") => new() { Title = Tr("정해진 시간마다 정리", "Clean on a schedule"), Sub = "" },
        ("memory-clean", "auto.minutes") => new() { Title = Tr("간격", "Every"), When = v => v.Bool("auto.intervalOn"), Minutes = true },
        ("memory-clean", "exclude.programs") => new() { Title = Tr("프로그램", "Program") },

        // ── 모니터 밝기 조정 ──
        ("brightness", "taskbar.number") => new()
        {
            Title = Tr("작업표시줄에 보일 밝기", "Brightness on the taskbar"),
            Sub = "",
            Option = v => v switch { "0" => Tr("첫 모니터", "First monitor"), "1" => Tr("평균", "Average"), _ => null },
        },
        ("brightness", "taskbar.wheelStep") => new() { Title = Tr("휠 한 칸에", "Per wheel notch"), Sub = Tr("작업표시줄 아이콘 위에서 휠을 돌릴 때", "Turning the wheel over the taskbar icon") },
        ("brightness", "taskbar.link") => new()
        {
            Title = Tr("모든 모니터 함께", "All monitors together"),
            Sub = Tr("한 모니터를 바꾸면 다른 모니터도 같은 밝기가 됩니다", "Changing one sets the others to the same brightness"),
        },
        ("brightness", "keys.up") => new() { Title = Tr("밝게", "Brighter"), Sub = Tr("누른 뒤 새 조합을 누르세요", "Click, then press a new combination") },
        ("brightness", "keys.down") => new() { Title = Tr("어둡게", "Dimmer"), Sub = "" },
        ("brightness", "keys.step") => new() { Title = Tr("한 번 누를 때", "Per press") },
        ("brightness", "idle.on") => new()
        {
            Title = Tr("자리를 비우면 어둡게", "Dim when away"),
            Sub = Tr("마우스나 키보드를 쓰면 원래대로 돌아옵니다", "It comes back as soon as the mouse or keyboard is used"),
        },
        ("brightness", "idle.minutes") => new() { Title = Tr("얼마 뒤에", "After"), When = v => v.Bool("idle.on") },
        ("brightness", "idle.level") => new() { Title = Tr("어둡게 할 밝기", "Dim to"), When = v => v.Bool("idle.on") },

        // ── 커피 ──
        ("keep-awake", "awake.display") => new()
        {
            Title = Tr("켜 둘 것", "Keep on"),
            Sub = Tr("PC만 켜 두면 화면은 평소처럼 꺼집니다", "With the PC only, the screen turns off as usual"),
            Option = v => v switch { "display" => Tr("화면과 PC", "Screen and PC"), "system" => Tr("PC만", "PC only"), _ => null },
        },
        ("keep-awake", "awake.duration") => new()
        {
            Title = Tr("오른쪽 클릭으로 켜면", "On with a right-click"),
            Sub = "",
            Option = v => v switch
            {
                "always" => Tr("끌 때까지", "Until turned off"),
                "m30" => Tr("30분", "30 min"),
                "h1" => Tr("1시간", "1 hour"),
                "h2" => Tr("2시간", "2 hours"),
                "custom" => Tr("직접 정한 시간", "A time of my own"),
                _ => null,
            },
        },
        ("keep-awake", "awake.minutes") => new() { Title = Tr("직접 정한 시간", "A time of my own"), Sub = "", When = v => v.Text("awake.duration") == "custom", Minutes = true },
        ("pill-taskbar", _) => Taskbar(key), // AddonEasy.Taskbar.cs
        _ => null,
    };

    /// A group's own name (null: the schema's).
    public static string? Group(string addon, string group) => (addon, group) switch
    {
        ("alt-tab-per-monitor", "switcher") => Tr("전환기 모양", "Switcher look"),
        ("alt-tab-per-monitor", "monitor") => Tr("모니터가 여럿일 때", "With several monitors"),
        ("memory-clean", "widget") => Tr("작업표시줄 아이콘", "Taskbar icon"),
        ("memory-clean", "clean") => Tr("정리", "Cleaning"),
        ("memory-clean", "exclude") => Tr("정리하지 않을 프로그램", "Programs left alone"),
        ("brightness", "ranges") => Tr("모니터마다 밝기 범위", "Brightness range per monitor"),
        ("brightness", "idle") => Tr("자리를 비우면 어둡게", "Dim when away"),
        _ => null,
    };

    /// A group's own line under its name ("" for none; null: the schema's).
    public static string? GroupNote(string addon, string group) => (addon, group) switch
    {
        ("memory-clean", "exclude") => Tr("정리할 때 이 프로그램들의 메모리는 비우지 않습니다", "Cleaning leaves these programs' memory as it is"),
        ("brightness", "ranges") => Tr("밝기 0과 100이 모니터의 실제 밝기 어디를 가리킬지 정합니다", "Sets where 0 and 100 fall on each monitor's own brightness"),
        _ => null,
    };

    /// The groups' order on the page where it isn't the schema's (the rest keep theirs, after these).
    public static string[] Order(string addon) => addon switch
    {
        "brightness" => ["taskbar", "keys", "idle", "ranges"],
        _ => [],
    };

    /// A group drawn folded under one 자세히 line (rarely changed).
    public static bool Folded(string addon, string group) => (addon, group) is ("brightness", "ranges");

    private static bool AppIcons(AddonValues v) => v.Text("switcher.style") == "icons";

    private static Line Location(string title) => new()
    {
        Title = title,
        Sub = "",
        Option = v => v switch { "cursor" => Tr("포인터가 있는 모니터", "The pointer's monitor"), "primary" => Tr("주 모니터", "Main monitor"), _ => null },
        When = title.StartsWith("Win", StringComparison.Ordinal) ? v => !SameWin(v) : null,
    };

    private static Line Listed(string title) => new()
    {
        Title = title,
        Sub = Tr("강조색 창이 목록에 나옵니다", "The highlighted windows are listed"),
        Option = v => v switch
        {
            "cursorMonitor" => Tr("그 모니터 창만", "That monitor's"),
            "allMonitors" => Tr("모든 창", "All"),
            "cursorMonitorExceptPrimary" => Tr("주 모니터에선 모두", "All on the main one"),
            _ => null,
        },
        When = title.StartsWith("Win", StringComparison.Ordinal) ? v => !SameWin(v) : null,
    };

    /// Win+Alt+Tab set as Alt+Tab ('Win+Alt+Tab도 같게'), unless the switch was turned off this visit.
    public static bool SameWin(AddonValues v) => !WinApart && v.Text("monitor.winAltTabLocation") == v.Text("monitor.altTabLocation")
        && v.Text("monitor.winAltTabWindows") == v.Text("monitor.altTabWindows");

    /// 'Win+Alt+Tab도 같게' turned off while the two still match: their own lines show until Bloom closes.
    public static bool WinApart { get; set; }

    /// 정리 세기's steps as the add-on's bits (clean.areas): the working sets only, the add-on's default, all seven.
    public static readonly (string key, int bits)[] Strengths = [("light", 1), ("normal", 103), ("strong", 127)];

    /// 정리 세기 '직접' picked: the seven chips show even while they match a step.
    public static bool CustomAreas { get; set; }
}
