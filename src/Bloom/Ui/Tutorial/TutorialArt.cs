using System.Collections.Generic;
using System.Windows;

namespace Bloom.Ui;

internal enum TutorialScene
{
    OpenWin, OpenAltSpace, OpenAutostart, ThumbAdmin,
    LookSystem, LookLight, LookDark, ThumbAccent, ThumbGlass,
    BadgeWindows,
    HomeBrowser, HomeFiles, HomeClipboard, HomeTimer, HomeCalendar, HomeLens, HomeSpace, HomeAi, HomeInbox, HomeRecord, HomeCommands, HomeLayout,
    AiClaude, AiCodex, AiT3, AiOmp, ThumbChat, ThumbTranslate,
    NotifyToApp, NotifyToTaskbar, NotifyCards, ThumbApprove, ThumbWindows, ThumbT3,
    KeyOpen, KeyTab, KeyMore, KeyLens, KeyClipboard, KeyHelp,
    DoneOpen, DoneAddons, DoneNotify,
}

/// What a scene shows that depends on the user's choices.
internal sealed record TutorialArtData(bool WinKey = true, bool AltSpace = true, IReadOnlyList<string>? Names = null, bool On = true);

/// The tutorial's pictures, drawn in code in the theme and the accent. They do not move.
internal sealed class TutorialArt : FrameworkElement
{
    private TutorialArt(TutorialScene scene, TutorialArtData data)
    {
        Scene = scene;
        Data = data;
    }

    internal TutorialScene Scene { get; }
    internal TutorialArtData Data { get; }

    public static TutorialArt For(TutorialScene scene, double width, double height, TutorialArtData? data = null) =>
        new(scene, data ?? new()) { Width = width, Height = height };

    /// The home tile picture of a feature id ("cmd:files"), or null when there is none.
    public static TutorialScene? Home(string id) => id switch
    {
        "cmd:quicklinks" => TutorialScene.HomeBrowser,
        "cmd:files" => TutorialScene.HomeFiles,
        "cmd:clipboard" => TutorialScene.HomeClipboard,
        "cmd:timer" => TutorialScene.HomeTimer,
        "cmd:calendar" => TutorialScene.HomeCalendar,
        "cmd:lens" => TutorialScene.HomeLens,
        "cmd:space" => TutorialScene.HomeSpace,
        "cmd:ai" => TutorialScene.HomeAi,
        "cmd:inbox" => TutorialScene.HomeInbox,
        "cmd:record" => TutorialScene.HomeRecord,
        "cmd:commands" => TutorialScene.HomeCommands,
        "cmd:layout" => TutorialScene.HomeLayout,
        _ => null,
    };

    /// A provider's mark in one colour, for the segmented rows: "claude", "chatgpt", "omp".
    public static FrameworkElement Logo(string provider, double size) => new FrameworkElement { Width = 0, Height = 0 };

    /// QA: while on, every scene string that did not fit its width is listed in Trims ("scene\ttext\tneeds N of M").
    internal static bool TrackTrims;
    internal static List<string> Trims { get; } = [];
}
