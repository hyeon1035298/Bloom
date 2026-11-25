using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;
using static Bloom.Ui.ArtKit;

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

/// What a scene shows that depends on the user's choices. `Glyphs`, beside `Names`: each named thing's icon.
internal sealed record TutorialArtData(bool WinKey = true, bool AltSpace = true, IReadOnlyList<string>? Names = null, bool On = true,
    IReadOnlyList<char>? Glyphs = null);

/// The tutorial's pictures, drawn in code in the theme and the accent. They do not move. A picture has no ground of
/// its own (it lies on its tile's card), except the theme samples and the screen with its notice cards. Its words are
/// made smaller before they are cut: a word that still does not fit ends in an ellipsis and is listed in Trims.
internal sealed partial class TutorialArt : FrameworkElement
{
    private readonly ArtKit.Words words;

    private TutorialArt(TutorialScene scene, TutorialArtData data)
    {
        Scene = scene;
        Data = data;
        words = new(this);
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            Theme.Changed -= InvalidateVisual;
            Theme.Changed += InvalidateVisual;
            InvalidateVisual();
        };
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
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
    public static FrameworkElement Logo(string provider, double size) => new Mark(provider, size) { Width = size, Height = size };

    /// QA: while on, every scene string that did not fit its width is listed in Trims ("scene\ttext\tneeds N of M").
    internal static bool TrackTrims;
    internal static List<string> Trims { get; } = [];

    /// QA: how many add-on pills the last done-page picture drew whole, of how many names it was given.
    internal static (int Shown, int Of) LastPills { get; private set; }

    // ---- one drawing: the context, the palette and the size, kept for the scene's own methods

    private DrawingContext dc = null!;
    private Tone tone;
    private double w, h, scale = 1;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        words.Forget();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        (w, h) = (RenderSize.Width, RenderSize.Height);
        if (w <= 0 || h <= 0) return;
        dc = drawing;
        tone = new Tone(Theme.P.Dark);
        scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        switch (Scene)
        {
            case TutorialScene.OpenWin: OpenWin(); break;
            case TutorialScene.OpenAltSpace: OpenAltSpace(); break;
            case TutorialScene.OpenAutostart: OpenAutostart(); break;
            case TutorialScene.ThumbAdmin: Fixed(64, 38, ThumbAdmin); break;
            case TutorialScene.LookSystem: LookSystem(); break;
            case TutorialScene.LookLight: Look(false, 0); break;
            case TutorialScene.LookDark: Look(true, 0); break;
            case TutorialScene.ThumbAccent: Fixed(64, 38, ThumbAccent); break;
            case TutorialScene.ThumbGlass: Fixed(64, 38, ThumbGlass); break;
            case TutorialScene.BadgeWindows: Fixed(18, 18, BadgeWindows); break;
            case TutorialScene.HomeBrowser: Fixed(132, 46, HomeBrowser); break;
            case TutorialScene.HomeFiles: Fixed(132, 46, HomeFiles); break;
            case TutorialScene.HomeClipboard: Fixed(132, 46, HomeClipboard); break;
            case TutorialScene.HomeTimer: Fixed(132, 46, HomeTimer); break;
            case TutorialScene.HomeCalendar: Fixed(132, 46, HomeCalendar); break;
            case TutorialScene.HomeLens: Fixed(132, 46, HomeLens); break;
            case TutorialScene.HomeSpace: Fixed(132, 46, HomeSpace); break;
            case TutorialScene.HomeAi: Fixed(132, 46, HomeAi); break;
            case TutorialScene.HomeInbox: Fixed(132, 46, HomeInbox); break;
            case TutorialScene.HomeRecord: Fixed(132, 46, HomeRecord); break;
            case TutorialScene.HomeCommands: Fixed(132, 46, HomeCommands); break;
            case TutorialScene.HomeLayout: Fixed(132, 46, HomeLayout); break;
            case TutorialScene.AiClaude: AiMark("anthropic"); break;
            case TutorialScene.AiCodex: AiMark("openai"); break;
            case TutorialScene.AiT3: AiT3(); break;
            case TutorialScene.AiOmp: AiOmp(); break;
            case TutorialScene.ThumbChat: Fixed(64, 38, ThumbChat); break;
            case TutorialScene.ThumbTranslate: Fixed(64, 38, ThumbTranslate); break;
            case TutorialScene.NotifyToApp: NotifyTo(false); break;
            case TutorialScene.NotifyToTaskbar: NotifyTo(true); break;
            case TutorialScene.NotifyCards: NotifyCards(); break;
            case TutorialScene.ThumbApprove: Fixed(64, 38, ThumbApprove); break;
            case TutorialScene.ThumbWindows: Fixed(64, 38, ThumbWindows); break;
            case TutorialScene.ThumbT3: Fixed(64, 38, ThumbT3); break;
            case TutorialScene.KeyOpen: KeyOpen(); break;
            case TutorialScene.KeyTab: KeyTab(); break;
            case TutorialScene.KeyMore: KeyMore(); break;
            case TutorialScene.KeyLens: KeyLens(); break;
            case TutorialScene.KeyClipboard: KeyClipboard(); break;
            case TutorialScene.KeyHelp: KeyHelp(); break;
            case TutorialScene.DoneOpen: DoneOpen(); break;
            case TutorialScene.DoneAddons: DoneAddons(); break;
            case TutorialScene.DoneNotify: DoneNotify(); break;
        }
    }

    /// A picture laid out for one size, set in the middle of the size it was given.
    private void Fixed(double width, double height, Action body)
    {
        dc.PushTransform(new TranslateTransform(Math.Round((w - width) / 2), Math.Round((h - height) / 2)));
        (w, h) = (width, height);
        body();
        dc.Pop();
    }

    // ---- colours per theme; none is pure white or black

    private readonly record struct Tone(bool Dark)
    {
        /// Raised surfaces: the small launcher, cards, pills.
        public uint Block => Dark ? 0xFF2E2E33u : 0xFFFAFAFAu;
        /// Placeholder bars and icon tiles on the tile's own ground.
        public uint Bar => Dark ? 0xFF3A3A40u : 0xFFD4D4D8u;
        /// Insets: a field, a screen, the taskbar's strip.
        public uint Deep => Dark ? 0xFF1C1C1Fu : 0xFFF1F1F3u;
        public uint Line => Dark ? 0x14ECEDEEu : 0x1A111111u;
        public uint RowSel => Dark ? 0x0FECEDEEu : 0x0D111111u;
        public uint Text => Dark ? 0xFFECEDEEu : 0xFF11181Cu;
        public uint Soft => Dark ? 0xFFD4D4D8u : 0xFF3F3F46u;
        public uint Faint => Dark ? 0xFF8B8B90u : 0xFF71717Au;
        public uint Shade => Dark ? 0x66101013u : 0x1F18181Bu;
        public uint Warn => Dark ? 0xFFE8A25Cu : 0xFFB86A1Eu;
        public uint Blue => Dark ? 0xFF5EA7F2u : 0xFF2F72D6u;
        public uint Red => Argb(Theme.P.Danger.Color);
        public uint Accent => ArtKit.Accent;
        public uint AccentInk => ArtKit.AccentInk;
        public uint AccentSoft => Fade(ArtKit.Accent, .18);
        /// A notice's head line.
        public uint Head => Argb(Theme.P.AccentText.Color);
        /// A placeholder bar and an icon tile on a raised surface or an inset, whichever it is: the text's colour, thin.
        public uint Stroke => Fade(Text, .17);
        public uint Wash => Fade(Text, .10);
        /// The other parts of a disk's map, beside the accent's.
        public uint[] Parts => Dark ? [0xFF4F6E9E, 0xFF3F8A82, 0xFFB37A3E, 0xFF7B5AA6] : [0xFF7E9DCB, 0xFF6FB5AC, 0xFFDBA76C, 0xFFA78BCF];
    }

    // ---- words: made smaller down to the least size a script reads at, then cut

    /// Hangul, kana and Han characters need 9.5 px; Latin letters and digits read at 8.5.
    private static double Least(string s)
    {
        foreach (char c in s)
            if (c is >= (char)0x1100 and <= (char)0x11FF or >= (char)0x2E80 and < (char)0xE000 or >= (char)0xF900) return 9.5;
        return 8.5;
    }

    /// The words in at most `max` px on one line. Without `shrink` they keep their size (words set beside others of
    /// that size) and are only cut.
    private FormattedText Say(string s, double size, FontWeight weight, uint argb, double max, FontFamily? family = null, bool shrink = true)
    {
        family ??= Theme.Sans;
        double least = shrink ? Math.Min(size, Least(s)) : size;
        var t = words.Of(s, size, weight, argb, family);
        while (t.Width > max && size - .5 >= least)
        {
            size -= .5;
            t = words.Of(s, size, weight, argb, family);
        }
        if (t.Width <= max) return t;
        if (TrackTrims)
        {
            string line = $"{Scene}\t{s}\tneeds {t.Width:0} of {max:0}";
            if (!Trims.Contains(line)) Trims.Add(line);
        }
        return words.Trimmed(s, size, weight, argb, family, Math.Max(1, max));
    }

    private double Snap(double v) => Math.Round(v * scale) / scale;

    /// Words from x, the middle of their line at `cy`.
    private void Put(FormattedText t, double x, double cy) => dc.DrawText(t, new Point(Snap(x), Snap(cy - t.Height / 2)));

    private void PutEnd(FormattedText t, double right, double cy) => Put(t, right - t.Width, cy);

    private void PutMid(FormattedText t, double cx, double cy) => Put(t, cx - t.Width / 2, cy);

    private void Icon(char glyph, double size, uint argb, double cx, double cy)
    {
        var t = words.Glyph(glyph, size, argb);
        dc.DrawText(t, new Point(Snap(cx - t.Width / 2), Snap(cy - t.Height / 2)));
    }

    // ---- shapes

    private static Geometry Shape(Rect r, double radius) => SmoothBorder.Shape(r, new CornerRadius(radius));

    /// A rounded box, its hairline inside its edge.
    private void Box(Rect r, double radius, uint fill, uint line = 0)
    {
        if (fill >> 24 != 0) dc.DrawGeometry(B(fill), null, Shape(r, radius));
        if (line >> 24 == 0) return;
        r.Inflate(-.5, -.5);
        dc.DrawGeometry(null, P(line, 1), Shape(r, Math.Max(0, radius - .5)));
    }

    /// A soft shadow under a raised shape: four thin layers of `shade`, each wider and lower than the one before, which
    /// together are as dark as it under the shape's lower edge.
    private void Lift(Rect r, double radius, uint shade)
    {
        var layer = B(Fade(shade, .3));
        for (int i = 1; i <= 4; i++)
        {
            var s = new Rect(r.Left - i * .75, r.Top + 1 + i * .9, r.Width + i * 1.5, r.Height);
            dc.DrawRoundedRectangle(layer, null, s, radius + i * .75, radius + i * .75);
        }
    }

    /// A raised surface: its shadow, its fill and its hairline.
    private void Raised(Rect r, double radius)
    {
        Lift(r, radius, tone.Shade);
        Box(r, radius, tone.Block, tone.Line);
    }

    /// A raised surface and what lies in it, cut to its corners.
    private void Inside(Rect r, double radius, Action body)
    {
        Raised(r, radius);
        dc.PushClip(Shape(r, radius));
        body();
        dc.Pop();
    }

    /// A bar with round ends.
    private void Bar(double x, double y, double width, double height, uint argb)
    {
        if (width > 0) dc.DrawRoundedRectangle(B(argb), null, new Rect(x, y, width, height), height / 2, height / 2);
    }

    private void Tile(Rect r, double radius, uint argb) => dc.DrawRoundedRectangle(B(argb), null, r, radius, radius);

    /// The Windows logo: four panes from (x, y).
    private void WinLogo(double x, double y, double pane, double gap, uint argb)
    {
        for (int i = 0; i < 4; i++)
            dc.DrawRectangle(B(argb), null, new Rect(x + i % 2 * (pane + gap), y + i / 2 * (pane + gap), pane, pane));
    }

    private static readonly Geometry sparkle = Theme.SparkleGeometry();

    /// The AI's four-point spark, `size` across, about (cx, cy).
    private void Sparkle(double cx, double cy, double size, uint argb)
    {
        double k = size / 21;
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, cx - 12 * k, cy - 12 * k));
        dc.DrawGeometry(B(argb), null, sparkle);
        dc.Pop();
    }

    /// The search field's glass, `size` across from x.
    private void Glass(double x, double cy, double size)
    {
        var pen = Round(tone.Faint, 1.4);
        double r = size / 2 - .7, reach = r * .7071 + .5;
        var c = new Point(x + size / 2, cy - .5);
        dc.DrawEllipse(null, pen, c, r, r);
        dc.DrawLine(pen, new Point(c.X + reach, c.Y + reach), new Point(c.X + reach + size * .28, c.Y + reach + size * .28));
    }

    /// An arrow to the right from x, `length` long.
    private void Arrow(double x, double cy, double length)
    {
        var pen = Round(tone.Faint, 1.3);
        double tip = x + length;
        dc.DrawLine(pen, new Point(x, cy), new Point(tip, cy));
        dc.DrawLine(pen, new Point(tip - 3, cy - 3), new Point(tip, cy));
        dc.DrawLine(pen, new Point(tip - 3, cy + 3), new Point(tip, cy));
    }

    /// The outline of what is picked on the screen: dashes in the accent over a breath of it.
    private void Dashed(Rect r)
    {
        r.Inflate(-.75, -.75);
        var pen = new Pen(B(tone.Accent), 1.5) { DashStyle = new DashStyle([2, 5.0 / 3], 0) };
        pen.Freeze();
        dc.DrawRoundedRectangle(B(Fade(tone.Accent, .07)), pen, r, 4, 4);
    }

    // ---- key caps

    private const string WinKey = "Win";

    /// A key: its word (WinKey is drawn as the Windows logo) and the least width of its cap.
    private readonly record struct Key(string Label, double Least = 24);

    private FormattedText CapWord(string label) => words.Text(label, 11, FontWeights.Bold, tone.AccentInk);

    private double CapWidth(Key k, double pad) => Math.Max(k.Least, Math.Ceiling((k.Label == WinKey ? 11 : CapWord(k.Label).Width) + 2 * pad));

    /// Key caps in a row from (x, y), a plus sign between them with `plus`. The caps get narrower, then the row smaller,
    /// before it passes `max`. Returns where the row ends.
    private double Caps(double x, double y, double max, bool plus, params Key[] keys)
    {
        var sign = words.Text("+", 11, FontWeights.Normal, tone.Faint);
        double between = plus ? 4 + Math.Ceiling(sign.Width) + 4 : 4, pad = 7;
        double Total() => between * (keys.Length - 1) + Sum();
        double Sum()
        {
            double sum = 0;
            foreach (var k in keys) sum += CapWidth(k, pad);
            return sum;
        }
        while (Total() > max && pad > 4) pad -= 1;
        double fit = Math.Min(1, max / Total());
        if (fit < 1) dc.PushTransform(new ScaleTransform(fit, fit, x, y + 12));
        double at = x;
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                if (plus) PutMid(sign, at + between / 2, y + 11.5);
                at += between;
            }
            double width = CapWidth(keys[i], pad);
            Cap(new Rect(at, y, width, 24), keys[i].Label);
            at += width;
        }
        if (fit < 1) dc.Pop();
        return x + (at - x) * fit;
    }

    /// One cap: the accent with a darker lower edge and a soft ring round it.
    private void Cap(Rect r, string label)
    {
        var ring = r;
        ring.Inflate(4, 4);
        dc.DrawGeometry(B(Fade(tone.Accent, .15)), null, Shape(ring, 11));
        var shape = Shape(r, 7);
        dc.DrawGeometry(B(Mix(tone.Accent, 0xFF101013, .25)), null, shape);
        dc.PushClip(shape);
        dc.DrawGeometry(B(tone.Accent), null, Shape(new Rect(r.X, r.Y - 2, r.Width, r.Height), 7));
        dc.Pop();
        if (label == WinKey) WinLogo(Math.Round(r.X + (r.Width - 11) / 2), r.Y + 6, 5, 1, tone.AccentInk);
        else PutMid(CapWord(label), r.X + r.Width / 2, r.Y + 11.5);
    }

    /// The caps of a shortcut given as its keys' names.
    private static Key[] Keys(IReadOnlyList<string>? names, params string[] otherwise)
    {
        var from = names is { Count: > 0 } ? names : otherwise;
        var keys = new Key[from.Count];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Key(from[i]);
        return keys;
    }

    // ---- the launcher made small

    /// The field: the glass, what is typed (or the hint while nothing is), the caret, and the rule under it.
    private void Query(Rect row, string text, bool hint = false, bool caret = false, bool rule = true, double size = 10.5)
    {
        bool small = size < 10;
        double cy = row.Top + row.Height / 2, glass = small ? 7 : 8, x = row.Left + (small ? 6 : 9);
        Glass(x, cy, glass);
        x += glass + (small ? 5 : 6);
        var t = Say(text, size, FontWeights.Normal, hint ? tone.Faint : tone.Text, row.Right - (small ? 6 : 9) - (caret ? 4 : 0) - x);
        Put(t, x, cy);
        if (caret) dc.DrawRectangle(B(tone.Accent), null, new Rect(Snap(x + t.Width + 2), cy - 5.5, 1, 11));
        if (rule) dc.DrawRectangle(B(tone.Line), null, new Rect(row.Left, row.Bottom - 1, row.Width, 1));
    }

    /// The ground of a result row: lit, with the accent's bar at its start, when it is the chosen one.
    private void Row(Rect row, bool chosen)
    {
        if (!chosen) return;
        dc.DrawRectangle(B(tone.RowSel), null, row);
        double bar = Math.Min(10, row.Height - 8);
        dc.DrawRoundedRectangle(B(tone.Accent), null, new Rect(row.Left + 3, row.Top + (row.Height - bar) / 2, 2.5, bar), 1.25, 1.25);
    }

    /// A result's icon tile, 14 across, with a glyph where one is given.
    private void IconTile(double x, double cy, char glyph = '\0', uint argb = 0)
    {
        Tile(new Rect(x, cy - 7, 14, 14), 4, tone.Wash);
        if (glyph != '\0') Icon(glyph, 9, argb, x + 7, cy);
    }

    // ---- notices, bubbles, the taskbar

    /// A notice card's words. `Warn`: it waits for an answer (the clock, in the warning's colour).
    private readonly record struct Notice(string Head, string Title, string? Sub = null, string? Yes = null, string? No = null, bool Warn = false);

    private static double CardHeight(double icon) => (icon >= 22 ? 16 : 12) + 13 + 1 + (icon >= 22 ? 15 : 14);

    /// A notice card from (x, top). `icon` sets its scale: 22 as on the screen, less where the card is one thing of
    /// several. With `least` it is as wide as its words need, from that up to `width`. Returns its box.
    private Rect Card(Notice n, double x, double top, double width, double icon, double least = 0)
    {
        double padX = icon >= 22 ? 10 : icon >= 20 ? 9 : 8, padY = icon >= 22 ? 8 : 6, gap = icon >= 20 ? 8 : 6;
        double titleSize = icon >= 22 ? 11 : icon >= 20 ? 10.5 : 10, titleLine = icon >= 22 ? 15 : 14;
        uint ink = n.Warn ? tone.Warn : tone.Accent;
        double textX = x + padX + icon + gap, room = x + width - padX - textX;
        var head = Say(n.Head, 9.5, FontWeights.SemiBold, n.Warn ? tone.Warn : tone.Head, room);
        var title = Say(n.Title, titleSize, FontWeights.Medium, tone.Text, room);
        var sub = n.Sub is null ? null : Say(n.Sub, 9.5, FontWeights.Normal, tone.Faint, room);
        if (least > 0) width = Math.Clamp(Math.Ceiling(textX - x + Math.Max(head.Width, Math.Max(title.Width, sub?.Width ?? 0)) + padX + 1), Math.Min(least, width), width);
        double height = CardHeight(icon) + (sub is null ? 0 : 14) + (n.Yes is null ? 0 : 24);
        var r = new Rect(x, top, width, height);
        Raised(r, 10);

        var tile = new Rect(x + padX, top + padY, icon, icon);
        dc.DrawGeometry(B(Fade(ink, .18)), null, Shape(tile, 6));
        Icon(n.Warn ? G.Clock : G.Done, Math.Round(icon * .55), ink, tile.X + icon / 2, tile.Y + icon / 2);

        double y = top + padY;
        Put(head, textX, y + 6.5);
        y += 14;
        Put(title, textX, y + titleLine / 2);
        y += titleLine;
        if (sub is not null)
        {
            Put(sub, textX, y + 7.5);
            y += 14;
        }
        if (n.Yes is not null)
        {
            y += 6;
            double bx = textX;
            foreach (var (label, first) in new[] { (n.Yes, true), (n.No, false) })
            {
                if (label is null) continue;
                var word = Say(label, 9.5, first ? FontWeights.SemiBold : FontWeights.Normal, first ? tone.AccentInk : tone.Soft, 64);
                var box = new Rect(bx, y, Math.Ceiling(word.Width) + 18, 18);
                Tile(box, 5, first ? tone.Accent : tone.Stroke);
                PutMid(word, box.X + box.Width / 2, box.Y + 9);
                bx = box.Right + 5;
            }
        }
        return r;
    }

    /// A chat bubble from (x, y); with `end`, x is its right edge. An answer begins with the AI's spark. Returns its box.
    private Rect Bubble(string text, bool mine, double x, double y, double max, double size = 10, bool end = false)
    {
        bool small = size < 10;
        double pad = small ? 8 : 9, height = small ? 17 : 22, spark = mine ? 0 : (small ? 8 : 9) + 4;
        var t = Say(text, size, FontWeights.Normal, mine ? tone.Text : tone.Soft, max - 2 * pad - spark);
        double width = Math.Ceiling(t.Width + 2 * pad + spark);
        var r = new Rect(end ? x - width : x, y, width, height);
        dc.DrawGeometry(B(mine ? tone.AccentSoft : tone.Bar), null, Shape(r, 9));
        double cy = r.Top + height / 2;
        if (!mine) Sparkle(r.Left + pad + (spark - 4) / 2, cy, spark - 4, tone.Accent);
        Put(t, r.Left + pad + spark, cy);
        return r;
    }

    /// A tab or a tag: its word on a small tile. Returns where it ends.
    private double Chip(string text, double x, double y, bool lit, double max = 64)
    {
        var t = Say(text, 9.5, FontWeights.Normal, lit ? tone.Text : tone.Soft, max - 14);
        var r = new Rect(x, y, Math.Ceiling(t.Width) + 14, 16);
        Tile(r, 5, lit ? tone.Stroke : tone.RowSel);
        PutMid(t, r.X + r.Width / 2, r.Y + 8);
        return r.Right;
    }

    /// The taskbar along the bottom of the picture. Returns its box.
    private Rect Strip(double height = 26)
    {
        var r = new Rect(0, h - height, w, height);
        dc.DrawRectangle(B(tone.Deep), null, r);
        dc.DrawRectangle(B(tone.Line), null, new Rect(0, r.Top, w, 1));
        return r;
    }

    // ---- the companies' marks

    private static readonly Dictionary<string, (Geometry[] Shapes, Rect Box)?> marks = [];

    /// The shapes of a mark Bloom carries ("anthropic", "openai") in its own box; null when it cannot be read.
    private static (Geometry[] Shapes, Rect Box)? MarkOf(string name)
    {
        lock (marks)
        {
            if (marks.TryGetValue(name, out var hit)) return hit;
            (Geometry[], Rect)? made = null;
            try
            {
                if (UsageLogo.Svg(Encoding.UTF8.GetString(OfficialLogos.Mark(name))) is { Drawing: DrawingGroup { ClipGeometry: { } frame } group })
                {
                    var shapes = new List<Geometry>();
                    foreach (var d in group.Children)
                        if (d is GeometryDrawing { Geometry: { } g, Brush: SolidColorBrush { Color.A: > 0 } })
                        {
                            var kept = g.Clone();
                            kept.Freeze();
                            shapes.Add(kept);
                        }
                    if (shapes.Count > 0) made = (shapes.ToArray(), frame.Bounds);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or System.Xml.XmlException)
            {
                Log.Error("tutorial mark " + name, e);
            }
            return marks[name] = made;
        }
    }

    /// A mark in one colour, as large as fits `into`, in its middle.
    private static void DrawMark(DrawingContext dc, string name, Rect into, Brush ink)
    {
        if (MarkOf(name) is not var (shapes, box)) return;
        double k = Math.Min(into.Width / box.Width, into.Height / box.Height);
        dc.PushTransform(new MatrixTransform(k, 0, 0, k,
            into.X + (into.Width - box.Width * k) / 2 - box.X * k, into.Y + (into.Height - box.Height * k) / 2 - box.Y * k));
        foreach (var s in shapes) dc.DrawGeometry(ink, null, s);
        dc.Pop();
    }

    /// A provider's mark for a segmented row, in the text colour of where it lies (the theme's where none is set).
    private sealed class Mark(string provider, double size) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            Brush ink = DependencyPropertyHelper.GetValueSource(this, TextElement.ForegroundProperty).BaseValueSource == BaseValueSource.Default
                ? Theme.P.Text
                : TextElement.GetForeground(this);
            var box = new Rect(0, 0, size, size);
            switch (provider)
            {
                case "claude": DrawMark(dc, "anthropic", box, ink); break;
                case "chatgpt": DrawMark(dc, "openai", box, ink); break;
                case "omp":
                    // omp has no mark of its own: a terminal's prompt, as it runs in one
                    var t = new FormattedText(">_", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(Theme.Mono, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), Math.Max(8.5, size * .72), ink,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(t, new Point((size - t.Width) / 2, (size - t.Height) / 2));
                    break;
            }
        }
    }
}
