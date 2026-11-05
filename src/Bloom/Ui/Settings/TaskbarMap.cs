using System.Collections.Generic;
using System.Linq;

namespace Bloom.Ui;

/// Where on 설정 > 애드온 each of the add-on's settings is changed. Every key the add-on has belongs to exactly one
/// line (the tests check it against the add-on's key list); the "More" sections are folded under 자세히 at first.
internal static class TaskbarMap
{
    internal enum Section { Folders, FoldersMore, Look, LookMore, Tray, TrayMore, Island, IslandMore }

    internal enum Control
    {
        Switch, // its own switch row (from the schema's name)
        Choice, // segmented choice, or a list with many options
        Number, // a slider, or a number field for a wide range
        Text,
        Shown, // a chip that shows a tray icon: on = the hide* key false
        Chip, // a chip that is the key itself
        Bell, // the bell's chip (shown / hidden) and its "only when new" switch under 자세히
        Curated, // a row written for the page (size, background, start button, media, usage, island, hover)
        SetWith, // set together with a curated row, never shown alone (size.enabled, autohide.enabled)
        Running, // the running folder's switch and name in the folder editor
    }

    internal readonly record struct Entry(string Key, Section Section, Control Control);

    public static readonly Entry[] Entries =
    {
        // 모양
        new("size.enabled", Section.Look, Control.SetWith),
        new("pill.style", Section.Look, Control.Curated),
        new("start.mode", Section.Look, Control.Curated),
        new("size.taskbarHeight", Section.LookMore, Control.Number),
        new("size.iconSize", Section.LookMore, Control.Number),
        new("size.iconSpacing", Section.LookMore, Control.Number),
        new("pill.cornerRadius", Section.LookMore, Control.Number),
        new("pill.blur", Section.LookMore, Control.Number),
        new("pill.tintOpacity", Section.LookMore, Control.Number),
        new("pill.enabled", Section.LookMore, Control.Switch),
        // 트레이
        new("tray.hideVolume", Section.Tray, Control.Shown),
        new("tray.hideNetwork", Section.Tray, Control.Shown),
        new("tray.hideBattery", Section.Tray, Control.Shown),
        new("tray.hideMicrophone", Section.Tray, Control.Shown),
        new("tray.hideCopilot", Section.Tray, Control.Shown),
        new("tray.bell", Section.Tray, Control.Bell),
        new("tray.clock", Section.Tray, Control.Choice),
        new("media.enabled", Section.Tray, Control.Curated),
        new("usage.enabled", Section.Tray, Control.Curated),
        new("tray.hideLocation", Section.TrayMore, Control.Shown),
        new("tray.hideStudioEffects", Section.TrayMore, Control.Shown),
        new("tray.hideRecall", Section.TrayMore, Control.Shown),
        new("tray.hideLanguageBar", Section.TrayMore, Control.Shown),
        new("tray.hideLanguageExtras", Section.TrayMore, Control.Shown),
        new("media.maxWidth", Section.TrayMore, Control.Number),
        new("usage.logoColor", Section.Tray, Control.Curated), // 아이콘 색, one setting with the home cards' (Settings.UsageLogo)
        new("usage.gapBefore", Section.TrayMore, Control.Number),
        new("usage.gapAfter", Section.TrayMore, Control.Number),
        // 숨김 아일랜드
        new("island.enabled", Section.Island, Control.Curated),
        new("autohide.enabled", Section.Island, Control.SetWith),
        new("island.reveal", Section.Island, Control.Choice),
        new("island.showClock", Section.Island, Control.Chip),
        new("island.showMedia", Section.Island, Control.Chip),
        new("island.showUsage", Section.Island, Control.Chip),
        new("island.hoverTransparency", Section.Island, Control.Curated),
        new("island.height", Section.IslandMore, Control.Number),
        new("island.width", Section.IslandMore, Control.Number),
        new("autohide.revealZone", Section.IslandMore, Control.Number),
        new("autohide.showDelay", Section.IslandMore, Control.Number),
        new("autohide.hideDelay", Section.IslandMore, Control.Number),
        new("autohide.animation", Section.IslandMore, Control.Number),
        new("autohide.keepHiddenForBloom", Section.IslandMore, Control.Switch),
        new("autohide.keepHiddenOnFlash", Section.IslandMore, Control.Switch),
        // 폴더
        new("folders.enabled", Section.Folders, Control.Switch), // off: the editor gives way to this switch alone
        new("folders.autoRunning", Section.Folders, Control.Running),
        new("folders.runningName", Section.Folders, Control.Running),
        new("folders.foldersPath", Section.FoldersMore, Control.Text),
        new("folders.mergeDelay", Section.FoldersMore, Control.Number),
        new("folders.popupScale", Section.FoldersMore, Control.Number),
        new("folders.showLabels", Section.FoldersMore, Control.Switch),
        new("folders.iconSize", Section.FoldersMore, Control.Number),
        new("folders.appSpacing", Section.FoldersMore, Control.Number),
        new("folders.popupSide", Section.FoldersMore, Control.Choice),
        new("folders.popupGap", Section.FoldersMore, Control.Number),
        new("folders.cornerRadius", Section.FoldersMore, Control.Number),
        new("folders.cornerSmoothing", Section.FoldersMore, Control.Number),
    };

    public static IEnumerable<Entry> In(Section section) => Entries.Where(e => e.Section == section);

    /// Numbers the add-on reads as "not set" (its about texts say so): shown as words, never as the number.
    private static readonly Dictionary<string, int> Unset = new()
    {
        ["autohide.showDelay"] = -1,
        ["autohide.hideDelay"] = -1,
        ["autohide.animation"] = 0,
        ["island.width"] = 0,
    };

    /// The words for `value` when it is the key's "not set" number, else null.
    public static string? Special(string key, int value) => Unset.TryGetValue(key, out int u) && u == value
        ? key == "island.width" ? Tr("내용에 맞춤", "Fits the content") : Tr("Windows 기본", "Windows default")
        : null;

    /// The page's own name for an option whose schema name is a description (else null: the schema's name).
    public static string? OptionLabel(string key, string value) => (key, value) switch
    {
        ("pill.style", "glass") => Tr("빛 받는 유리", "Lit glass"),
        ("pill.style", "windowGlass") => Tr("테두리 없는 유리", "Borderless glass"),
        _ => null,
    };

    /// 크기's presets: taskbar height, icon size, icon spacing. 크게 is the add-on's default look, 보통 Windows' own sizes.
    public static readonly (string key, int height, int icon, int spacing)[] Sizes =
    {
        ("s", 40, 20, 12),
        ("m", 48, 24, 20),
        ("l", 56, 32, 14),
    };
}
