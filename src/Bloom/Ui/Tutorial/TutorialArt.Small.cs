using System;
using System.Windows;
using System.Windows.Media;
using Bloom.Search;
using static Bloom.Ui.ArtKit;

namespace Bloom.Ui;

/// The small pictures: a home feature's (132x46, clear of the tile's badge at its top right), a row's thumb (64x38)
/// and the badge on the default browser's icon.
internal sealed partial class TutorialArt
{
    // ---- home features. Each lies in the 96 px from the left margin; the badge has the corner beyond

    private void HomeBrowser()
    {
        var r = new Rect(9, 5, 96, 36);
        Inside(r, 8, () =>
        {
            Query(new Rect(r.X, r.Y, r.Width, 18), "yt " + Tr("고양이", "cats"), size: 9.5);
            var row = new Rect(r.X, r.Y + 18, r.Width, 18);
            Row(row, true);
            Put(Say(Tr("유튜브 검색", "YouTube Search"), 9.5, FontWeights.Normal, tone.Soft, r.Width - 10 - 6), row.X + 10, row.Y + 9);
        });
    }

    private void HomeFiles()
    {
        var field = new Rect(9, 5, 96, 16);
        Box(field, 6, tone.Deep, tone.Line);
        Glass(field.X + 6, field.Y + 8, 7);
        Put(Say(Tr("보고서", "report"), 9.5, FontWeights.Normal, tone.Text, field.Width - 18 - 6), field.X + 18, field.Y + 8);
        Icon(G.Document, 12, tone.Blue, 18, 32.5);
        Bar(29, 27, 48, 4, tone.Bar);
        Bar(29, 34, 30, 3, Fade(tone.Faint, .4));
    }

    /// Three things copied; the chosen one a colour, shown with its code (the accent's own).
    private void HomeClipboard()
    {
        for (int i = 0; i < 3; i++)
        {
            var row = new Rect(9, 4 + i * 14, 96, 12);
            if (i != 1)
            {
                Tile(row, 4, tone.Block);
                if (i == 0) Bar(row.X + 7, row.Y + 4, 50, 4, tone.Stroke);
                else
                {
                    Tile(new Rect(row.X + 6, row.Y + 2, 13, 8), 2, tone.Blue);
                    Bar(row.X + 24, row.Y + 4, 34, 4, tone.Stroke);
                }
                continue;
            }
            Tile(row, 4, tone.Bar);
            dc.PushClip(new RectangleGeometry(row, 4, 4));
            dc.DrawRectangle(B(tone.Accent), null, new Rect(row.X, row.Y, 2.5, row.Height));
            dc.Pop();
            Tile(new Rect(row.X + 7, row.Y + 2, 8, 8), 2, tone.Accent);
            Put(Say($"#{tone.Accent & 0xFFFFFF:X6}", 8.5, FontWeights.Normal, tone.Text, row.Width - 19 - 5), row.X + 19, row.Y + 6);
        }
    }

    private void HomeTimer()
    {
        var c = new Point(29, 23);
        const double r = 15, share = .72;
        dc.DrawEllipse(null, P(tone.Bar, 4), c, r, r);
        double a = (share * 360 - 90) * Math.PI / 180;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false, false);
            g.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, Round(tone.Accent, 4), arc);
        Put(Say("9:59", 15, FontWeights.SemiBold, tone.Text, 46), 55, 23);
    }

    private void HomeCalendar()
    {
        for (int i = 0; i < 28; i++)
            Tile(new Rect(12 + i % 7 * 12, 5 + i / 7 * 10, 9, 7), 2, i == 9 ? tone.Accent : i == 18 ? Fade(tone.Faint, .7) : tone.Bar);
    }

    /// Lines of a page, a part of them picked, and what Lens offers for it.
    private void HomeLens()
    {
        Bar(12, 8, 62, 4, tone.Stroke);
        Bar(12, 17, 88, 4, tone.Stroke);
        Bar(12, 26, 76, 4, tone.Stroke);
        Dashed(new Rect(8, 13, 80, 18));
        var pill = new Rect(50, 25, 55, 17);
        Raised(pill, 8.5);
        Sparkle(pill.X + 13, pill.Y + 8.5, 9, tone.Accent);
        Icon(G.Globe, 9.5, tone.Soft, pill.X + 27.5, pill.Y + 8.5);
        Icon(G.Copy, 9.5, tone.Soft, pill.X + 42, pill.Y + 8.5);
    }

    /// A disk's map: its largest part in the accent.
    private void HomeSpace()
    {
        var parts = tone.Parts;
        Tile(new Rect(10, 5, 42, 36), 3, Mix(tone.Accent, tone.Dark ? 0xFF242427 : 0xFFFAFAFA, .28));
        Tile(new Rect(55, 5, 28, 19), 3, parts[0]);
        Tile(new Rect(86, 5, 20, 19), 3, parts[1]);
        Tile(new Rect(55, 27, 28, 14), 3, parts[2]);
        Tile(new Rect(86, 27, 20, 14), 3, parts[3]);
    }

    private void HomeAi()
    {
        Bubble(Tr("이 오류 뭐야", "What's this error?"), true, 101, 5, 92, 9.5, end: true);
        Bubble(Tr("경로가 없대요", "Path not found."), false, 9, 25, 114, 9.5);
    }

    /// Notices kept: one waiting on top, the edge of another under it.
    private void HomeInbox()
    {
        Box(new Rect(14, 22, 78, 18), 6, Mix(tone.Dark ? 0xFF242427 : 0xFFE9E9EA, tone.Block, .55), tone.Line);
        var card = new Rect(9, 5, 88, 26);
        Raised(card, 7);
        var tile = new Rect(card.X + 6, card.Y + 5, 16, 16);
        Tile(tile, 4, Fade(tone.Warn, .18));
        Icon(G.Clock, 9, tone.Warn, tile.X + 8, tile.Y + 8);
        Bar(card.X + 28, card.Y + 8, 40, 4, Fade(tone.Warn, .85));
        Bar(card.X + 28, card.Y + 15, 52, 3, tone.Stroke);
    }

    /// The part of the screen being recorded, and the bar that counts the time.
    private void HomeRecord()
    {
        var frame = new Rect(10, 5, 86, 36);
        frame.Inflate(-.75, -.75);
        dc.DrawRoundedRectangle(B(Fade(tone.Red, .07)), P(tone.Red, 1.5), frame, 5, 5);
        var time = Say("0:12", 9.5, FontWeights.SemiBold, tone.Text, 34);
        double width = 9 + 7 + 5 + Math.Ceiling(time.Width) + 9;
        var pill = new Rect(Math.Round(53 - width / 2), 15, width, 17);
        Raised(pill, 8.5);
        dc.DrawEllipse(B(tone.Red), null, new Point(pill.X + 12.5, pill.Y + 8.5), 3.5, 3.5);
        Put(time, pill.X + 21, pill.Y + 8.5);
    }

    private void HomeCommands()
    {
        for (int i = 0; i < 4; i++)
        {
            var cell = new Rect(9 + i % 2 * 48, 4 + i / 2 * 21, 44, 17);
            Box(cell, 5, tone.Block, tone.Line);
            double cx = cell.X + 22, cy = cell.Y + 8.5;
            switch (i)
            {
                case 0: PutMid(Say(">_", 9, FontWeights.Bold, tone.Accent, 36, Theme.Mono), cx, cy); break;
                case 1: PutMid(Say("Aa", 9.5, FontWeights.SemiBold, tone.Text, 36), cx, cy); break;
                case 2: Sparkle(cx, cy, 9, tone.Accent); break;
                default: Icon(G.Link, 10, tone.Soft, cx, cy); break;
            }
        }
    }

    /// A screen with two windows side by side, the one being placed in the accent.
    private void HomeLayout()
    {
        var screen = new Rect(11, 5, 84, 36);
        screen.Inflate(-.75, -.75);
        dc.DrawRoundedRectangle(null, P(Fade(tone.Faint, .6), 1.5), screen, 5, 5);
        Box(new Rect(14, 8, 38, 30), 3, Fade(tone.Accent, .28), tone.Accent);
        Box(new Rect(55, 8, 37, 30), 3, tone.Block, tone.Line);
    }

    // ---- row thumbs

    private static readonly Geometry shield = Geometry.Parse("M8,1 L15,4 V9 C15,13 12,15.8 8,17 C4,15.8 1,13 1,9 V4 Z");

    /// A window and Windows' own shield for what needs an administrator.
    private void ThumbAdmin()
    {
        const double k = 17 / 16.0, grown = k * 1.24, sx = 38, sy = 14;
        var win = new Rect(9, 6, 38, 26);
        var badge = shield.Clone();
        badge.Transform = new MatrixTransform(k, 0, 0, k, sx, sy);
        var room = shield.Clone();
        room.Transform = new MatrixTransform(grown, 0, 0, grown, sx - 8 * (grown - k), sy - 9 * (grown - k));
        var body = new CombinedGeometry(GeometryCombineMode.Exclude, Shape(win, 5), room);
        dc.DrawGeometry(B(tone.Bar), null, body);
        dc.PushClip(body);
        dc.DrawRectangle(B(Fade(tone.Faint, .45)), null, new Rect(win.X, win.Y, win.Width, 6));
        dc.Pop();

        double midX = sx + 8 * k, midY = sy + 9 * k;
        dc.PushClip(badge);
        dc.DrawRectangle(B(tone.Blue), null, new Rect(sx, sy, 17, 19));
        dc.DrawRectangle(B(tone.Warn), null, new Rect(midX, sy, 17, midY - sy));
        dc.DrawRectangle(B(tone.Warn), null, new Rect(sx, midY, midX - sx, 19));
        dc.Pop();
    }

    /// Where the accent shows: the bar of the chosen row, and what is on.
    private void ThumbAccent()
    {
        var row = new Rect(8, 11, 48, 16);
        Box(row, 5, tone.Block, tone.Line);
        Tile(new Rect(row.X + 3, row.Y + 4, 3, 8), 1.5, tone.Accent);
        Bar(row.X + 10, row.Y + 6, 18, 4, tone.Stroke);
        Bar(row.Right - 15, row.Y + 5, 10, 6, tone.Accent);
    }

    /// Two colours behind a pane that lets them through.
    private void ThumbGlass()
    {
        Blob(new Point(17, 17), 15, Fade(tone.Accent, .62));
        Blob(new Point(45, 23), 16, Fade(tone.Blue, .52));
        var pane = new Rect(12, 8, 40, 22);
        Box(pane, 6, Fade(tone.Block, .55), Fade(tone.Text, .16));
        Bar(pane.X + 6, pane.Y + 6, 22, 3, Fade(tone.Text, .75));
        Bar(pane.X + 6, pane.Y + 13, 14, 3, Fade(tone.Text, .4));
    }

    private void Blob(Point c, double radius, uint argb)
    {
        var soft = new RadialGradientBrush(Tint(argb), Tint(argb & 0xFFFFFF));
        soft.GradientStops.Insert(1, new GradientStop(Tint(argb), .45));
        soft.Freeze();
        dc.DrawEllipse(soft, null, c, radius, radius);
    }

    private void BadgeWindows()
    {
        Box(new Rect(0, 0, 18, 18), 5, tone.Block, tone.Line);
        WinLogo(4, 4, 4.5, 1, tone.Blue);
    }

    private void ThumbChat()
    {
        Tile(new Rect(26, 6, 32, 11), 5.5, Fade(tone.Accent, .26));
        Bar(32, 10, 20, 3, Fade(tone.Accent, .7));
        Tile(new Rect(6, 21, 44, 12), 6, tone.Bar);
        Sparkle(13, 27, 7, tone.Accent);
        Bar(20, 25, 23, 4, tone.Stroke);
    }

    private void ThumbTranslate()
    {
        var from = Say("가", 12, FontWeights.SemiBold, tone.Text, 20);
        var to = Say("A", 12, FontWeights.SemiBold, tone.Text, 20);
        double x = Math.Round((w - (from.Width + 6 + 10 + 6 + to.Width)) / 2), cy = h / 2;
        Put(from, x, cy);
        Arrow(x + from.Width + 6, Math.Floor(cy) + .5, 10);
        Put(to, x + from.Width + 22, cy);
    }

    /// A small notice with its two answers.
    private void ThumbApprove()
    {
        var card = new Rect(6, 5, 52, 28);
        Box(card, 6, tone.Block, tone.Line);
        Bar(card.X + 6, card.Y + 6, 26, 3, tone.Warn);
        Tile(new Rect(card.X + 6, card.Y + 14, 19, 9), 3, tone.Accent);
        Tile(new Rect(card.X + 27, card.Y + 14, 19, 9), 3, tone.Stroke);
    }

    private void ThumbWindows()
    {
        WinLogo(6, 13, 5, 1, tone.Blue);
        Arrow(21, 18.5, 8);
        var card = new Rect(33, 8, 26, 22);
        Box(card, 5, tone.Block, tone.Line);
        Tile(new Rect(card.X + 4, card.Y + 5, 6, 6), 2, Fade(tone.Accent, .6));
        Bar(card.X + 12, card.Y + 6, 9, 3, tone.Stroke);
        Bar(card.X + 4, card.Y + 14, 17, 3, tone.Stroke);
    }

    /// A notice that takes an answer and can stop the work, and T3 Code's tile at its corner.
    private void ThumbT3()
    {
        var card = new Rect(5, 5, 46, 25);
        Box(card, 6, tone.Block, tone.Line);
        Bar(card.X + 6, card.Y + 6, 20, 3, tone.Stroke);
        var field = new Rect(card.X + 6, card.Y + 13, 24, 7);
        Tile(field, 3, tone.Wash);
        dc.DrawRectangle(B(tone.Accent), null, new Rect(field.X + 3, field.Y + 1, 1, 5));
        Tile(new Rect(card.X + 33, card.Y + 13, 7, 7), 2, tone.Red);

        var t3 = new Rect(43, 18, 16, 16);
        Lift(t3, 4, tone.Shade);
        Box(t3, 4, tone.Bar, tone.Line);
        PutMid(Say("T3", 8.5, FontWeights.Bold, tone.Text, 14), t3.X + 8, t3.Y + 8);
    }
}
