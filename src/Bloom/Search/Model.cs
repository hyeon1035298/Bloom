using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Bloom.Search;

internal enum ItemKind
{
    App,
    Command,
    Setting,
    File,
    Folder,
    Url,
    WebSearch,
    Calc,
    Ai,
    Thread,
    Info,
    Clip, // clipboard history
    Quicklink,
    Process,
    Ocr,
    Lens,
    Group, // a favorites folder
    Window, // an open window (창 전환)
    Symbol, // an emoji or symbol to paste
    Toggle, // a system switch (dark mode, mute, Bluetooth …)
    Quick, // an AI action on text
}

/// How a row gets its icon: a shell item (app, file, AppsFolder entry), a glyph tile, or a ready image.
internal sealed class IconSpec
{
    public string? ShellPath; // parsing name for IShellItemImageFactory
    public string? CacheKey;
    public char Glyph;
    public bool Accent; // glyph drawn in the accent colour (AI, pairing)
    public string? Mono; // name icon: letters on a black tile

    public static IconSpec MonoOf(string letters) => new() { Mono = letters };

    public static IconSpec Shell(string path, string? key = null) => new() { ShellPath = path, CacheKey = key ?? path.ToLowerInvariant() };

    /// A picture's own thumbnail (clipboard images, folder icons), not its file-type icon.
    public static IconSpec Thumb(string path) => new() { ShellPath = path, CacheKey = Ui.Icons.ThumbPrefix + path.ToLowerInvariant() };

    public static IconSpec Of(char glyph, bool accent = false) => new() { Glyph = glyph, Accent = accent };

    /// A colour or gradient swatch (the colour picker).
    public Brush? Fill;

    public static IconSpec Swatch(Brush fill) => new() { Fill = fill };

    /// An emoji or symbol drawn as the icon itself (emoji in colour through Ui.ColorEmoji, symbols in the UI font).
    public string? Text;
    public bool Emoji;

    public static IconSpec Symbol(string text, bool emoji) => new() { Text = text, Emoji = emoji };

    /// A small screen with a part of it filled (창 배치: where the window goes), as fractions of the screen.
    public System.Windows.Rect? Area;

    public static IconSpec Share(double x, double y, double w, double h) => new() { Area = new System.Windows.Rect(x, y, w, h) };
}

internal sealed class ActionItem
{
    public string Title = "";
    public string? Keys; // shortcut label, e.g. "Ctrl+Enter"
    public char Glyph;
    public bool Danger;
    public Action Run = () => { };
    /// Keep the launcher open after the action (toggles, copy with feedback, …).
    public bool StayOpen;
}

internal sealed class ResultItem
{
    public string Id = ""; // stable: favorites and usage key on it
    public ItemKind Kind;
    public string Title = "";
    public string? Subtitle;
    public string? Accessory;
    public Func<string>? Live; // an accessory that changes every second (a timer's time left)
    public ActionItem? Side; // a button on the row while it is selected (관리자로 실행); also in the 더보기 menu
    public Action? Remove; // 홈 편집: a ✕ at the row's end that takes it off its block (the 명령 block's commands)
    public Action<string, bool>? MoveTo; // dragged in its list (RowReorder): put before, or after (true), the item with that id
    public IconSpec Icon = new();
    public int Score;
    public object? Data;

    /// The primary action (Enter). Its label is shown in the footer.
    public string PrimaryLabel = Tr("열기", "Open");
    public Action? Run;
    public bool StayOpen;

    /// Extra actions for the action panel; the primary one is put first automatically.
    public Func<List<ActionItem>>? MoreActions;

    /// The right-hand preview built on the UI thread; may use UI-owned brushes or other WPF objects.
    public Func<Detail>? Detail;

    /// File-backed preview read on a worker. Capture value snapshots only; do not create/access WPF objects here.
    public Func<Detail>? ReadDetail;

    /// For favorites: how to rebuild this item from a stored entry.
    public Core.Favorites.Entry? AsFavorite;

    public bool Pinnable => AsFavorite is not null;

    // calculator card
    public string? CalcExpr, CalcValue, CalcDetail;
    public string? CalcCaption; // under the left side (null: 수식)
}

internal sealed class Section
{
    public string? Title;
    public string? Note; // right-aligned small text on the header (counts, status)
    public bool Strip; // favorites: one row of icons under the search line
    public System.Func<System.Windows.FrameworkElement>? Widget; // a home block drawn as is (usage, week), no rows
    public string? HomeKey; // a home block: 홈 편집 shows its tools on it (Core\Home.cs)
    public bool HomeRight; // … in the right column when the home has two (Ui\Controls\HomeColumns.cs)
    public bool Collapsed; // … folded: only its header shows (no rows, widget or strip)
    public int HomeCount; // … a list block's rows as set (Home.Count; 0: not a list), its tools' − n +
    public bool HomeFoot; // 홈 편집: a column's "+ 블록 추가" and its cards, at the end of its column (HomeRight)
    public System.Action? Reload; // a ↻ on the header (the AI usage block)
    public bool Fallback; // the root's "다른 곳에서 찾기": late results (files) go in before it
    public readonly List<ResultItem> Items = new();

    public Section(string? title, string? note = null)
    {
        Title = title;
        Note = note;
    }
}
