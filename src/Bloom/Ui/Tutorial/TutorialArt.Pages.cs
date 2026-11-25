using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Bloom.Search;
using static Bloom.Ui.ArtKit;

namespace Bloom.Ui;

/// The pictures of the pages' large tiles: how Bloom opens, the themes, the AI programs, where a notice leads, the keys
/// and what was set.
internal sealed partial class TutorialArt
{
    // ---- how Bloom opens. The caps are at the top left of every tile that has them, the launcher under them

    /// Where the opening launcher lies: across the middle, a little above the middle of what the caps leave.
    private Rect OpenBox => new(Math.Round((w - 132) / 2), 48 + Math.Round((h - 132) * .45), 132, 72);

    /// The launcher as it opens: the hint in its field, and rows not yet read.
    private void OpenLauncher()
    {
        var r = OpenBox;
        Inside(r, 9, () =>
        {
            Query(new Rect(r.X, r.Y, r.Width, 24), Tr("검색하거나 묻기", "Search or ask"), hint: true);
            for (int i = 0; i < 2; i++)
            {
                double cy = r.Y + 36 + i * 24;
                IconTile(r.X + 11, cy);
                Bar(r.X + 32, cy - 2, i == 0 ? 56 : 40, 4, tone.Stroke);
            }
        });
    }

    private void OpenWin()
    {
        Caps(10, 9, w - 20, false, new Key(WinKey, 41));
        OpenLauncher();
    }

    private void OpenAltSpace()
    {
        // the window of whatever is in front, which the launcher opens over: its title bar shows beside the caps
        var r = OpenBox;
        var app = new Rect(r.X + 18, r.Y - 10, w, h);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        Box(app, 8, tone.RowSel, tone.Line);
        for (int i = 0; i < 3; i++) dc.DrawEllipse(B(tone.Bar), null, new Point(w - 11 - i * 8, app.Y + 5), 2, 2);
        dc.Pop();
        Caps(10, 9, w - 20, true, new Key("Alt"), new Key("Space", 64));
        OpenLauncher();
    }

    /// The desk just after signing in: nothing open yet, and Bloom already beside the clock.
    private void OpenAutostart()
    {
        var strip = Strip();
        uint light = Fade(tone.Block, .75);
        var glow = new RadialGradientBrush(Tint(light), Tint(light & 0xFFFFFF))
        {
            Center = new Point(.3, .15),
            GradientOrigin = new Point(.3, .15),
            RadiusX = .78,
            RadiusY = .7,
        };
        glow.Freeze();
        dc.DrawRectangle(glow, null, new Rect(0, 0, w, strip.Top));

        double top = strip.Top + 8;
        WinLogo(9, top, 5, 1, tone.Blue);
        for (int i = 1; i <= 2; i++) Tile(new Rect(9 + i * 18, top, 11, 11), 3, tone.Stroke);

        var time = Say(Tr("오전 9:00", "9:00 AM"), 9.5, FontWeights.Normal, tone.Text, 56);
        var date = Say("11-25", 9, FontWeights.Normal, tone.Faint, 56);
        double right = w - 9;
        PutEnd(time, right, strip.Top + 8.5);
        PutEnd(date, right, strip.Top + 19);

        // Bloom's own mark, lit: it is the one thing here that was started
        var mark = new Rect(Math.Round(right - Math.Max(time.Width, date.Width) - 9 - 15), strip.Top + 6, 15, 15);
        var ring = mark;
        ring.Inflate(6, 6);
        Tile(ring, 9, Fade(tone.Accent, .14));
        ring = mark;
        ring.Inflate(3, 3);
        Tile(ring, 6.5, Fade(tone.Accent, .42));
        dc.PushTransform(new TranslateTransform(mark.X, mark.Y));
        dc.DrawDrawing(Theme.MarkVisual(mark.Width).Drawing);
        dc.Pop();
    }

    private static Color Tint(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // ---- the themes: samples in their own fixed colours, the same whichever theme shows them

    private readonly record struct Sample(uint Ground, uint Win, uint Line, uint Bars, uint Accent, uint Shade);

    private static Sample SampleOf(bool dark)
    {
        var accent = Accents.Current ?? Accents.All[0];
        return dark
            ? new(0xFF242427, 0xFF1C1C1F, 0xFF3A3A40, 0xFF52525B, accent.Dark, 0x66101013)
            : new(0xFFE4E4E7, 0xFFFAFAFA, 0xFFD4D4D8, 0xFFA1A1AA, accent.Light, 0x1F18181B);
    }

    /// A theme's sample: a window with a title, a chosen row and a line. `part`: 0 the whole of it, 1 and 2 what the
    /// light and the dark half of the two-theme sample show (a short row each, clear of the line between them).
    private void Look(bool dark, int part)
    {
        var s = SampleOf(dark);
        dc.DrawRectangle(B(s.Ground), null, new Rect(0, 0, w, h));
        var win = new Rect(Math.Round((w - 136) / 2), Math.Round((h - 74) / 2) - 1, 136, 74);
        Lift(win, 10, s.Shade);
        Box(win, 10, s.Win, s.Line);
        if (part != 2) Bar(win.X + 9, win.Y + 10, 64, 5, s.Bars);
        var row = part switch
        {
            1 => new Rect(win.X + 9, win.Y + 26, 52, 16),
            2 => new Rect(win.X + 76, win.Y + 46, 50, 16),
            _ => new Rect(win.X + 9, win.Y + 26, 116, 16),
        };
        Tile(row, 5, Mix(s.Win, s.Line, .5));
        Tile(new Rect(row.X + 2, row.Y + 4, 3, 8), 1.5, s.Accent);
        if (part == 0) Bar(win.X + 9, win.Y + 50, 90, 5, s.Line);
    }

    /// Windows' own choice: the light sample, and the dark one below a line across the middle.
    private void LookSystem()
    {
        Look(false, 1);
        var below = new StreamGeometry();
        using (var g = below.Open())
        {
            g.BeginFigure(new Point((w + h) / 2, 0), true, true);
            g.LineTo(new Point(w, 0), false, false);
            g.LineTo(new Point(w, h), false, false);
            g.LineTo(new Point((w - h) / 2, h), false, false);
        }
        below.Freeze();
        dc.PushClip(below);
        Look(true, 2);
        dc.Pop();
    }

    // ---- the AI programs: each one's mark in the middle, larger when the tiles are tall

    private double MarkSize => h >= 140 ? 44 : 34;

    private Rect MarkBox => new(Math.Round((w - MarkSize) / 2), Math.Round((h - MarkSize) / 2), MarkSize, MarkSize);

    private void AiMark(string name) => DrawMark(dc, name, MarkBox, B(tone.Text));

    private void AiT3()
    {
        var r = MarkBox;
        Box(r, r.Width >= 44 ? 11 : 9, tone.Bar, tone.Line);
        PutMid(Say("T3", r.Width >= 44 ? 16 : 13, FontWeights.Bold, tone.Text, r.Width - 8), r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    /// omp runs in a terminal and has no mark: its name as typed there, the caret after it.
    private void AiOmp()
    {
        bool tall = h >= 140;
        var name = Say("omp", tall ? 24 : 19, FontWeights.Bold, tone.Text, w - 24, Theme.Mono);
        double caret = tall ? 20 : 16, x = Math.Round((w - name.Width - 5) / 2), cy = Math.Round(h / 2);
        Put(name, x, cy);
        dc.DrawRectangle(B(tone.Accent), null, new Rect(Snap(x + name.Width + 3), cy - caret / 2 + 1, 2, caret));
    }

    // ---- notices

    private static Notice DoneNotice => new("Claude Code · " + Tr("작업 완료", "Completed"), Tr("결제 모듈 테스트", "Test payment module"));

    /// A notice card, an arrow, and where a click on the card leads: the window the work runs in, or the taskbar's
    /// pill with its chat panel open under it. The card is as wide as its words; what it leads to takes the rest, up
    /// to where the tile's pick mark is.
    private void NotifyTo(bool taskbar)
    {
        const double left = 10, least = 54, between = 22;
        double right = w - 29;
        var card = Card(DoneNotice, left, Math.Round((h - CardHeight(18)) / 2), right - least - between - left, 18, least: 126);
        Arrow(card.Right + 6, Math.Floor(card.Top + card.Height / 2) + .5, 10);
        double x = card.Right + between, width = right - x, edge = h >= 80 ? 8 : 5;

        if (!taskbar)
        {
            var win = new Rect(x, edge, width, h - 2 * edge);
            Box(win, 7, tone.Deep, tone.Line);
            for (int i = 0; i < 2; i++) dc.DrawEllipse(B(tone.Stroke), null, new Point(win.X + 9 + i * 9, win.Y + 7.5), 2.5, 2.5);
            dc.DrawRectangle(B(tone.Line), null, new Rect(win.X + 1, win.Y + 14, win.Width - 2, 1));
            var prompt = words.Of(">_", 10, FontWeights.Bold, tone.Accent, Theme.Mono);
            Put(prompt, win.X + 7, win.Y + 25);
            double after = win.X + 7 + Math.Ceiling(prompt.Width) + 5;
            Bar(after, win.Y + 23, Math.Min(48, win.Right - 8 - after), 4, tone.Stroke);
            double[] lines = [1, .62];
            for (int i = 0; i < lines.Length && win.Y + 35 + i * 10 + 4 <= win.Bottom - 7; i++)
                Bar(win.X + 7, win.Y + 35 + i * 10, Math.Round((win.Width - 20) * lines[i]), 4, tone.Stroke);
            return;
        }

        var pill = new Rect(x, edge - 2, width, 18);
        Box(pill, 9, tone.Block, tone.Line);
        for (int i = 0; i < 3; i++) Tile(new Rect(pill.X + 7 + i * 13, pill.Y + 4, 10, 10), 3, i == 0 ? tone.Accent : tone.Stroke);
        double lit = Math.Min(20, width - 58);
        if (lit >= 10) Bar(pill.Right - 8 - lit, pill.Y + 7, lit, 4, tone.Accent);
        var panel = new Rect(x, pill.Bottom + 4, width, h - (edge - 2) - pill.Bottom - 4);
        Box(panel, 7, tone.Deep, tone.Line);
        double mine = Math.Min(36, width - 22), theirs = Math.Min(48, width - 14);
        Bar(panel.Right - 7 - mine, panel.Y + 7, mine, 9, Fade(tone.Accent, .38));
        Bar(panel.X + 7, panel.Y + 20, theirs, 9, tone.Stroke);
        if (panel.Height >= 44) Bar(panel.X + 7, panel.Y + 34, width - 20, 4, tone.Wash);
    }

    /// The screen with two notices come over the windows on it: one done, one waiting for an answer.
    private void NotifyCards()
    {
        dc.DrawRectangle(B(tone.Deep), null, new Rect(0, 0, w, h));
        DeskWindow(new Rect(18, 16, 170, 96), [84, 120, 96, 60]);
        DeskWindow(new Rect(200, 30, 120, 84), [70, 52]);

        const double width = 232;
        double x = w - 18 - width;
        var done = Card(new Notice(Tr("Codex · 작업 완료", "Codex · Task Completed"), Tr("결제 모듈 테스트 추가", "Add payment module tests"),
            Tr("소요 시간 12분", "Elapsed: 12 min")), x, 10, width, 22);
        Card(new Notice(Tr("Claude Code · 승인 대기", "Claude Code · Waiting for Approval"), Tr("빌드 스크립트 정리", "Tidy the build script"),
            Yes: Tr("허용", "Allow"), No: Tr("거절", "Deny"), Warn: true), x, done.Bottom + 8, width, 22);
    }

    /// A window on the desk, quiet: it is what the notices lie over.
    private void DeskWindow(Rect r, double[] lines)
    {
        Box(r, 8, Mix(tone.Deep, tone.Block, .6), tone.Line);
        for (int i = 0; i < 3; i++) dc.DrawEllipse(B(tone.Wash), null, new Point(r.X + 11 + i * 8, r.Y + 9), 2, 2);
        for (int i = 0; i < lines.Length; i++) Bar(r.X + 11, r.Y + 26 + i * 13, lines[i], 4, tone.Wash);
    }

    // ---- the keys

    /// Where a key tile's launcher lies.
    private static Rect KeyBox(double height, double top = 42) => new(16, top, 146, height);

    private void KeyOpen()
    {
        if (Data.WinKey || !Data.AltSpace) Caps(10, 9, w - 20, false, new Key(WinKey, 41));
        else Caps(10, 9, w - 20, false, new Key("Alt"), new Key("Space"));
        var r = KeyBox(50);
        string app = Tr("메모장", "Notepad");
        Inside(r, 9, () =>
        {
            Query(new Rect(r.X, r.Y, r.Width, 24), app, caret: true);
            var row = new Rect(r.X, r.Y + 24, r.Width, 26);
            Row(row, true);
            IconTile(row.X + 11, row.Y + 13, G.App, tone.Blue);
            var enter = words.Text("↵", 9, FontWeights.Normal, tone.Faint);
            PutEnd(enter, row.Right - 10, row.Y + 13);
            Put(Say(app, 10, FontWeights.Normal, tone.Text, row.Right - 10 - enter.Width - 6 - (row.X + 32)), row.X + 32, row.Y + 13);
        });
    }

    private void KeyTab()
    {
        Caps(10, 9, w - 20, false, new Key("Tab"));
        var r = KeyBox(26);
        Inside(r, 9, () => Query(r, Tr("이 오류 뜻이 뭐야", "What does this error mean?"), rule: false));
        Bubble(Tr("경로가 없다는 뜻이에요", "It means the path is missing."), false, r.X, 76, r.Width);
    }

    private void KeyMore()
    {
        Caps(10, 9, w - 20, false, new Key("Ctrl"), new Key("K"));
        // the launcher the menu opens over: the chosen result is what the menu is about
        var back = new Rect(10, 52, 90, 49);
        Inside(back, 9, () =>
        {
            for (int i = 0; i < 2; i++)
            {
                var row = new Rect(back.X, back.Y + 1 + i * 24, back.Width, 24);
                Row(row, i == 0);
                IconTile(row.X + 11, row.Y + 12, i == 0 ? G.App : '\0', tone.Blue);
                Bar(row.X + 32, row.Y + 10, i == 0 ? 26 : 20, 4, tone.Stroke);
            }
        });
        string[] lines = [Tr("즐겨찾기에 추가", "Add to Favorites"), Tr("단축키 지정…", "Assign shortcut…"), Tr("검색에서 숨기기", "Hide from search")];
        // as wide as its longest line needs (a language's words may be longer than the Korean), over more of the
        // launcher behind it when they are, never past the tile's left margin
        const double indent = 10, end = 7;
        double need = lines.Max(s => Math.Ceiling(words.Text(s, 9.5, FontWeights.Normal, tone.Text).Width)) + indent + end;
        double width = Math.Clamp(need, 100, w - 20);
        var menu = new Rect(w - 10 - width, 40, width, 61);
        Inside(menu, 8, () => Lines(menu, 2, 19, indent, lines));
    }

    /// A list's lines, the first one chosen.
    private void Lines(Rect list, double top, double pitch, double indent, string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var row = new Rect(list.X, list.Y + top + i * pitch, list.Width, pitch);
            Row(row, i == 0);
            Put(Say(lines[i], 9.5, FontWeights.Normal, i == 0 ? tone.Text : tone.Soft, list.Width - indent - 7), row.X + indent, row.Y + pitch / 2);
        }
    }

    private void KeyLens()
    {
        Caps(10, 9, w - 20, false, Keys(Data.Names, "Ctrl", "Alt", "L"));
        double[] text = [56, 70, 62, 44];
        for (int i = 0; i < text.Length; i++) Bar(12, 50 + i * 10, text[i], 4, tone.Stroke);
        Dashed(new Rect(8, 55, 76, 22));
        string[] lines = [Tr("1  AI에게 묻기", "1  Ask AI"), Tr("2  번역", "2  Translate"), Tr("3  글자 복사", "3  Copy text")];
        var list = new Rect(w - 10 - 82, 44, 82, 58);
        Inside(list, 9, () => Lines(list, 2, 18, 10, lines));
    }

    private void KeyClipboard()
    {
        Caps(10, 9, w - 20, false, Keys(Data.Names, "Ctrl", "Alt", "V"));
        var r = KeyBox(58);
        Inside(r, 9, () =>
        {
            double x = Chip(Tr("기록", "History"), r.X + 7, r.Y + 6, true);
            Chip(Tr("상용구", "Snippets"), x + 4, r.Y + 6, false);
            for (int i = 0; i < 2; i++)
            {
                var row = new Rect(r.X, r.Y + 28 + i * 14, r.Width, 14);
                Row(row, i == 0);
                Bar(row.X + 11, row.Y + 5, i == 0 ? 78 : 56, 4, tone.Stroke);
            }
        });
    }

    private void KeyHelp()
    {
        Caps(10, 9, w - 20, false, new Key("?"));
        var r = KeyBox(64, 40);
        (char Glyph, uint Ink, string Words)[] samples =
        [
            (G.Stopwatch, tone.Accent, Tr("10분 타이머", "10 min timer")),
            (G.Calendar, tone.Faint, Tr("100일 뒤", "in 100 days")),
            (G.App, tone.Faint, Tr("왼쪽 절반", "left half")),
        ];
        Inside(r, 9, () =>
        {
            Query(new Rect(r.X, r.Y, r.Width, 20), "?", caret: true);
            for (int i = 0; i < samples.Length; i++)
            {
                double cy = r.Y + 29.25 + i * 12.5;
                Icon(samples[i].Glyph, 9, samples[i].Ink, r.X + 15.5, cy);
                Put(Say(samples[i].Words, 9.5, FontWeights.Normal, tone.Soft, r.Width - 26 - 9), r.X + 26, cy);
            }
        });
    }

    // ---- what was set

    /// The keys that open Bloom: either one alone when only it is on.
    private void DoneOpen()
    {
        bool win = Data.WinKey, alt = Data.AltSpace, none = !win && !alt;
        double x = 12, y = Math.Round((h - 24) / 2);
        if (none) dc.PushOpacity(.4);
        if (win || none) x = Caps(x, y, w - 24, false, new Key(WinKey, 35));
        if (win == alt)
        {
            var or = Say(Tr("또는", "or"), 11, FontWeights.Normal, tone.Faint, 44);
            Put(or, x + 8, y + 11.5);
            x += 8 + Math.Ceiling(or.Width) + 8;
        }
        if (alt || none) Caps(x, y, w - 12 - x, true, new Key("Alt"), new Key("Space", 52));
        if (none) dc.Pop();
    }

    /// The add-ons picked, each by its icon and name, and that they are yet to be fetched: as many as fit whole, three
    /// at most, their names at the largest size they fit at, and the rest counted. Only a name too long to stand alone
    /// is cut.
    private void DoneAddons()
    {
        // a pill: its add-on's icon on a small tile, then the name
        const double gap = 6, disc = 20, high = 30, padLeft = 5, padRight = 10, icon = 20, iconGap = 7;
        const double chrome = padLeft + icon + iconGap + padRight;
        double cy = Math.Round(h / 2), right = w - 12;
        dc.DrawEllipse(B(tone.Accent), null, new Point(right - disc / 2, cy), disc / 2, disc / 2);
        Icon(G.Download, 10.5, tone.AccentInk, right - disc / 2, cy);

        IReadOnlyList<string> names = Data.Names ?? [];
        double room = right - disc - 10 - 12, size = 11, most = 0;
        int shown = 0;
        FormattedText? more = null;
        for (int n = Math.Min(3, names.Count); n >= 1 && shown == 0; n--)
        {
            var rest = names.Count > n ? words.Text($"+{names.Count - n}", 10.5, FontWeights.Medium, tone.Faint) : null;
            double space = room - (rest is null ? 0 : Math.Ceiling(rest.Width) + 8) - n * chrome - (n - 1) * gap;
            for (double at = 11; at >= 9.5 && shown == 0; at -= .5)
            {
                double sum = 0;
                for (int i = 0; i < n; i++) sum += Math.Ceiling(words.Text(names[i], at, FontWeights.Medium, tone.Text).Width);
                if (sum <= space || (n == 1 && at == 9.5)) (shown, size, more, most) = (n, at, rest, space);
            }
        }

        double x = 12;
        for (int i = 0; i < shown; i++)
        {
            var name = Say(names[i], size, FontWeights.Medium, tone.Text, most, shrink: false);
            var pill = new Rect(x, cy - high / 2, Math.Ceiling(name.Width) + chrome, high);
            Box(pill, 9, tone.Block, tone.Line);
            var tile = new Rect(pill.X + padLeft, cy - icon / 2, icon, icon);
            Tile(tile, 6, tone.Wash);
            Icon(Data.Glyphs is { } glyphs && i < glyphs.Count ? glyphs[i] : G.Plug, 11, tone.Soft, tile.X + icon / 2, cy);
            Put(name, tile.Right + iconGap, cy);
            x = pill.Right + gap;
        }
        if (more is not null) Put(more, x + 2, cy);
    }

    /// The notice as it will come; with notices off, pale, its icon struck through.
    private void DoneNotify()
    {
        if (!Data.On) dc.PushOpacity(.4);
        var card = Card(DoneNotice, 12, Math.Round((h - CardHeight(20)) / 2), w - 24, 20, least: 200);
        if (Data.On) return;
        var tile = new Rect(card.X + 9, card.Y + 6, 20, 20);
        dc.DrawLine(Round(tone.Faint, 1.5), new Point(tile.Left - 1.5, tile.Bottom + 1.5), new Point(tile.Right + 1.5, tile.Top - 1.5));
        dc.Pop();
    }
}
