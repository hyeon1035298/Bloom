using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Bloom.Core;

namespace Bloom.Ui;

/// A text laid out in a box (a paragraph's, a cell's, code's): where it sits in the box, its lines and their tops.
internal sealed class MdPart(MdText text, double lineHeight)
{
    public readonly MdText Text = text;
    public readonly double LineHeight = lineHeight;
    public double X, Y, Width;
    public int[] Starts = [0];
    public double[] Tops = [0, lineHeight]; // each line's top, then the bottom
    public int Row = -1, Col = -1; // a table cell's

    public double Height => Tops[^1];

    public int Lines => Starts.Length;

    public int End(int k) => Text.LineEnd(Starts, k);

    /// Its lines at `width`: a picture fitted to the width (shaped again when its size changed), a line with one as tall
    /// as it.
    public void Arrange(MdEnv env, double width)
    {
        Width = width;
        bool again = false;
        foreach (var (_, p) in Text.Pictures)
        {
            double w = p.Ready ? Math.Min(p.DipWidth, Math.Max(1, width)) : 0, h = p.Ready && p.DipWidth > 0 ? w * p.DipHeight / p.DipWidth : 0;
            if (Math.Abs(w - p.Width) > 0.25 || Math.Abs(h - p.Height) > 0.25)
            {
                p.Width = w;
                p.Height = h;
                again = true;
            }
        }
        if (again || !Text.Shaped) Text.Shape(env);
        Starts = Text.Break(width);
        Tops = new double[Starts.Length + 1];
        for (int k = 0; k < Starts.Length; k++) Tops[k + 1] = Tops[k] + LineHeight;
        if (Text.Pictures.Length == 0) return;
        double grow = 0;
        for (int k = 0; k < Starts.Length; k++)
        {
            int s = Starts[k], e = End(k);
            double h = LineHeight;
            foreach (var (at, p) in Text.Pictures)
                if (at >= s && at < e) h = Math.Max(h, p.Height);
            Tops[k] += grow;
            grow += h - LineHeight;
        }
        Tops[^1] += grow;
    }

    public double LineHeightOf(int k) => Tops[k + 1] - Tops[k];

    /// The line `y` (from the part's top) is on, kept to its lines.
    public int LineAt(double y)
    {
        int k = Array.BinarySearch(Tops, y);
        if (k < 0) k = ~k - 1;
        return Math.Clamp(k, 0, Starts.Length - 1);
    }

    /// The line the place `i` is on (a place where a line wraps is the next line's start).
    public int LineOf(int i)
    {
        int k = Array.BinarySearch(Starts, i);
        if (k < 0) k = ~k - 1;
        return Math.Clamp(k, 0, Starts.Length - 1);
    }

    /// The place `i` on screen (from the box's top left): its x, its line's top and height.
    public Rect Bounds(int i)
    {
        int k = LineOf(i);
        return new Rect(X + Text.X[Math.Clamp(i, 0, Text.Text.Length)] - Text.X[Starts[k]], Y + Tops[k], 0, LineHeightOf(k));
    }

    /// The rectangles from `a` to `b` covers, line by line (the selection's).
    public IEnumerable<Rect> Rects(int a, int b)
    {
        if (b <= a) yield break;
        for (int k = LineOf(a); k < Starts.Length && Starts[k] < b; k++)
        {
            int s = Starts[k], e = End(k);
            int from = Math.Max(a, s), to = Math.Min(b, e);
            double x1 = Text.X[from] - Text.X[s], x2 = Text.X[to] - Text.X[s];
            if (b > e && e < Text.Text.Length && Text.Text[e] == '\n') x2 += LineHeight * 0.25; // the line break, selected
            if (x2 > x1) yield return new Rect(X + x1, Y + Tops[k], x2 - x1, LineHeightOf(k));
        }
    }

    /// The place nearest to (x, y) from the box's top left.
    public int Hit(double x, double y)
    {
        int k = LineAt(y - Y);
        return Text.Hit(Starts[k], End(k), x - X);
    }

    /// Lines drawn between `from` and `to` (the box's y).
    public void Draw(DrawingContext dc, MdEnv env, double from, double to)
    {
        if (Y + Height <= from || Y >= to) return;
        for (int k = LineAt(from - Y); k < Starts.Length && Y + Tops[k] < to; k++)
        {
            double top = Y + Tops[k];
            if (top + LineHeightOf(k) <= from) continue;
            Text.DrawLine(dc, env, Starts[k], End(k), X, env.Snap(top), LineHeight, LineHeightOf(k));
        }
    }
}

/// Something a box draws besides its text: behind it (tints, lines, bars) or over it (markers, boxes, buttons).
internal sealed class MdDeco(Rect r, Action<DrawingContext, Rect> paint)
{
    public Rect R = r;
    public readonly Action<DrawingContext, Rect> Paint = paint;
}

/// A place in a box that takes a click: a checklist box (Item: its item; the marker column a line high, Inner the box
/// drawn) or a code block's 복사 (Code: its box).
internal sealed class MdSpot(Rect r, MdBox owner, Rect inner)
{
    public Rect R = r, Inner = inner;
    public readonly MdBox Owner = owner;
}

/// One Markdown block (MdBlock) laid out: shaped once at the view's size and DPI (Shape), broken into lines at a width
/// (Arrange: arithmetic only), drawn between two heights (Draw). Its texts (Parts) are in reading order — a table's
/// cells row by row, a quote's blocks one after another — which is the order selections and copies go by.
internal sealed class MdBox
{
    public MdBlock Block;
    public readonly string Key;
    public bool Shaped;
    private MdEnv? made; // the look its texts were made at (Make); null: to be made again
    public double Width = -1, Height, MarginTop, MarginBottom;
    public readonly List<MdPart> Parts = [];
    private readonly List<MdDeco> back = [], front = [];
    public readonly List<MdSpot> Spots = [];
    private readonly List<MdBox> children = [];
    private MdText? marker, label, copy, copied;
    private Brush brush = Theme.P.Text;
    public bool Copied; // 복사 완료 shows on its button for a moment
    public double Estimated = -1;

    public MdBox(MdBlock block, string key)
    {
        Block = block;
        Key = key;
        foreach (var c in block.Children) children.Add(new MdBox(c, ""));
    }

    /// The block (a new reading of the same source) in place of the one it was made from: lines and first-ness may
    /// have moved, the looks didn't.
    public void Adopt(MdBlock b)
    {
        Block = b;
        for (int k = 0; k < children.Count && k < b.Children.Count; k++) children[k].Adopt(b.Children[k]);
    }

    public bool IsTable => Block.Kind == MdKind.Table;

    /// The room above and under it (MdMetrics).
    public void Margins(MdMetrics m)
    {
        var b = Block;
        (MarginTop, MarginBottom) = b.Kind switch
        {
            MdKind.Heading => m.HeadingAround(b.Level, b.First),
            MdKind.Rule => m.RuleAround,
            MdKind.Code or MdKind.Table => m.Around,
            MdKind.Item => (0, m.ItemAfter(b.ListEnd)),
            _ => (0, m.After),
        };
        foreach (var c in children) c.Margins(m);
    }

    // ── shaping ─────────────────────────────────────────────────────────────────────────────────

    /// Made and shaped again when next needed (its box ticked, a picture found or lost), its quoted blocks too.
    public void Invalidate()
    {
        Shaped = false;
        made = null;
        foreach (var c in children) c.Invalidate();
    }

    /// Whether it is drawn as `b` reads (a box ticked since it was made shows its own state): it may stand for it.
    public bool Shows(MdBlock b)
    {
        if (Block.Done != b.Done || children.Count != b.Children.Count) return false;
        for (int k = 0; k < children.Count; k++)
            if (!children[k].Shows(b.Children[k])) return false;
        return true;
    }

    /// Its texts made from its block in `ink` (a document's quote: dimmed), in reading order — a quote's are its
    /// blocks' — not shaped: copying and moving the selection need only these. Once per look (MdEnv).
    public void Make(MdEnv env, Brush ink, string? baseDir)
    {
        if (made == env) return;
        var m = env.M;
        var b = Block;
        brush = ink;
        Parts.Clear();
        Margins(m);
        switch (b.Kind)
        {
            case MdKind.Heading:
                Parts.Add(new MdPart(MdText.Of(b.Spans, env, m.HeadingSize(b.Level), FontWeights.SemiBold, ink, baseDir), m.HeadingLine(b.Level)));
                break;
            case MdKind.Paragraph:
                Parts.Add(new MdPart(MdText.Of(b.Spans, env, m.Size, FontWeights.Normal, ink, baseDir), m.Line));
                break;
            case MdKind.Item:
                Parts.Add(new MdPart(MdText.Of(b.Spans, env, m.Size, FontWeights.Normal, b.Done == true ? Theme.P.TextDim : ink, baseDir), m.Line));
                marker = b.Done is null ? MdText.Plain(b.Marker, env.Props(Theme.Sans, FontWeights.Normal, FontStyles.Normal, m.Size, Theme.P.TextFaint)) : null;
                break;
            case MdKind.Code:
                var small = env.Props(Theme.Sans, FontWeights.Medium, FontStyles.Normal, MdMetrics.LabelSize, Theme.P.TextFaint);
                label = b.Lang.Length > 0 ? MdText.Plain(b.Lang, small) : null;
                var button = env.Props(Theme.Sans, FontWeights.Medium, FontStyles.Normal, MdMetrics.LabelSize, Theme.P.TextDim);
                copy = MdText.Plain(Tr("복사", "Copy"), button);
                copied = MdText.Plain(Tr("복사 완료", "Copied"), button);
                Parts.Add(new MdPart(MdText.OfCode(b.Code, env.Props(Theme.Mono, FontWeights.Normal, FontStyles.Normal, m.CodeSize, Theme.P.Text)), m.CodeLine));
                break;
            case MdKind.Table:
                for (int r = 0; r < b.Rows.Count; r++)
                    for (int c = 0; c < b.Cols; c++)
                    {
                        var cell = c < b.Rows[r].Count ? b.Rows[r][c] : [];
                        Parts.Add(new MdPart(MdText.Of(cell, env, m.CellSize, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, ink, baseDir), m.CellLine) { Row = r, Col = c });
                    }
                break;
            case MdKind.Quote:
                foreach (var c in children)
                {
                    c.Make(env, m.Document ? Theme.P.TextDim : ink, baseDir); // a document's quote reads as aside
                    Parts.AddRange(c.Parts);
                }
                break;
        }
        made = env;
        Shaped = false;
    }

    /// Its texts made (Make) and shaped — at the view's size and DPI, once.
    public void Shape(MdEnv env, Brush ink, string? baseDir)
    {
        Make(env, ink, baseDir);
        if (Block.Kind == MdKind.Quote)
            foreach (var c in children) c.Shape(env, c.brush, baseDir);
        else
        {
            foreach (var p in Parts) p.Text.Shape(env);
            marker?.Shape(env);
            label?.Shape(env);
            copy?.Shape(env);
            copied?.Shape(env);
        }
        Shaped = true;
        Width = -1;
    }

    /// Every picture in it (to read their headers).
    public IEnumerable<MdPicture> Pictures() => Parts.SelectMany(p => p.Text.Pictures.Select(x => x.Picture));

    /// Whether a picture of it shows as its alt text because its file couldn't be read (Show looks again).
    public bool MissesPictures() => Parts.Any(p => p.Text.Unread);

    // ── layout ──────────────────────────────────────────────────────────────────────────────────

    /// Its lines at `width` (the text's column), its height, where its parts and decorations sit.
    public void Arrange(MdEnv env, double width)
    {
        Width = width;
        back.Clear();
        front.Clear();
        Spots.Clear();
        var m = env.M;
        var b = Block;
        switch (b.Kind)
        {
            case MdKind.Heading:
                {
                    var p = Parts[0];
                    p.X = p.Y = 0;
                    p.Arrange(env, width);
                    Height = p.Height;
                    if (m.Document && b.Level == 1)
                    {
                        Height += m.HeadingRuleGap + 1;
                        back.Add(new MdDeco(new Rect(0, Height - 1, width, 1), (dc, r) => dc.DrawRectangle(Theme.P.Line, null, r)));
                    }
                    break;
                }
            case MdKind.Paragraph:
                {
                    var p = Parts[0];
                    p.X = p.Y = 0;
                    p.Arrange(env, width);
                    Height = p.Height;
                    break;
                }
            case MdKind.Item:
                ArrangeItem(env, width);
                break;
            case MdKind.Rule:
                Height = 1;
                back.Add(new MdDeco(new Rect(0, 0, width, 1), (dc, r) => dc.DrawRectangle(Theme.P.Line, null, r)));
                break;
            case MdKind.Code:
                ArrangeCode(env, width);
                break;
            case MdKind.Table:
                ArrangeTable(env, width);
                break;
            case MdKind.Quote:
                ArrangeQuote(env, width);
                break;
        }
    }

    /// A list item: the marker (or the checklist box) in a column of its own, the text hanging beside it.
    private void ArrangeItem(MdEnv env, double width)
    {
        var m = env.M;
        var b = Block;
        double indent = m.IndentOf(b.Indent), column = b.Done is null ? m.MarkerWidth(char.IsDigit(b.Marker[0])) : m.BoxColumn;
        var p = Parts[0];
        p.X = indent + column;
        p.Y = 0;
        p.Arrange(env, Math.Max(1, width - indent - column));
        Height = p.Height;
        double first = p.LineHeightOf(0), dy = first - p.LineHeight;
        if (marker is { } mark)
        {
            front.Add(new MdDeco(new Rect(indent, 0, column, first), (dc, r) => mark.DrawLine(dc, env, 0, mark.Text.Length, r.X, env.Snap(r.Y + dy), p.LineHeight, p.LineHeight)));
            return;
        }
        // the box: a line of text high in the column, centred on the text's middle (as chat's)
        var look = env.Props(Theme.Sans, FontWeights.Normal, FontStyles.Normal, m.Size, Theme.P.Text);
        double baseline = dy + env.LineOf(look, p.LineHeight).Baseline;
        double s = m.BoxSide, mid = baseline - (2 * Theme.Sans.Baseline - Theme.Sans.LineSpacing) * m.Size / 2;
        var box = new Rect(indent, mid - s / 2, s, s);
        bool done = b.Done == true;
        var tickLook = env.Props(Theme.IconFont, FontWeights.Normal, FontStyles.Normal, m.TickSize, Theme.P.AccentInk);
        var tick = done ? MdText.Plain(Search.G.Check.ToString(), tickLook) : null;
        tick?.Shape(env);
        front.Add(new MdDeco(box, (dc, r) =>
        {
            BoxShape(dc, env, r, done ? Theme.P.Accent : null, done ? Theme.P.Accent : Theme.P.TextFaint);
            if (tick is null) return;
            var (tb, th) = env.LineOf(tickLook, 0);
            tick.DrawLine(dc, env, 0, tick.Text.Length, r.X + (r.Width - tick.Widest) / 2, env.Snap(r.Y + (r.Height - th) / 2), 0, th);
        }));
        Spots.Add(new MdSpot(new Rect(indent, 0, column, first), this, box));
    }

    /// A checklist box's square in `box` (MdMetrics: its corners and line, inside its edge): filled, ringed.
    public static void BoxShape(DrawingContext dc, MdEnv env, Rect box, Brush? fill, Brush ring)
    {
        double half = MdMetrics.BoxStroke / 2, radius = env.M.BoxRadius;
        var square = new Rect(env.Snap(box.X) + half, env.Snap(box.Y) + half, box.Width - MdMetrics.BoxStroke, box.Height - MdMetrics.BoxStroke);
        dc.DrawGeometry(fill, new Pen(ring, MdMetrics.BoxStroke), SmoothBorder.Shape(square, new CornerRadius(radius)));
    }

    /// Fenced code: its tint (round 8), the language top left, 복사 top right, the code below.
    private void ArrangeCode(MdEnv env, double width)
    {
        var m = env.M;
        var p = Parts[0];
        p.X = MdMetrics.CodeLeft;
        p.Y = MdMetrics.CodeTop + (label is null ? 0 : MdMetrics.LabelGap);
        p.Arrange(env, Math.Max(1, width - MdMetrics.CodeLeft - MdMetrics.CodeRight));
        Height = p.Y + p.Height + MdMetrics.CodeBottom;
        back.Add(new MdDeco(new Rect(0, 0, width, Height), (dc, r) => dc.DrawGeometry(Theme.P.Code, null, SmoothBorder.Shape(r, new CornerRadius(MdMetrics.CodeRadius)))));
        if (label is { } l)
        {
            var (_, lh) = env.LineOf(l.LookAt(0), 0);
            back.Add(new MdDeco(new Rect(MdMetrics.CodeLeft, MdMetrics.CodeTop, l.Widest, lh), (dc, r) => l.DrawLine(dc, env, 0, l.Text.Length, r.X, env.Snap(r.Y), 0, lh)));
        }
        if (copy is null || copied is null) return;
        var (_, bh) = env.LineOf(copy.LookAt(0), 0);
        var shown = Copied ? copied : copy;
        double w = shown.Widest + 2 * MdMetrics.CopyPadX, h = bh + MdMetrics.CopyPadTop + MdMetrics.CopyPadBottom;
        var button = new Rect(width - MdMetrics.CodeRight + MdMetrics.CopyOutRight - w, MdMetrics.CodeTop - MdMetrics.CopyOutTop, w, h);
        front.Add(new MdDeco(button, (dc, r) =>
        {
            var at = new Rect(env.Snap(r.X), env.Snap(r.Y), r.Width, r.Height);
            dc.DrawGeometry(Theme.P.Key, null, SmoothBorder.Shape(at, new CornerRadius(MdMetrics.CopyRadius)));
            shown.DrawLine(dc, env, 0, shown.Text.Length, at.X + MdMetrics.CopyPadX, at.Y + MdMetrics.CopyPadTop, 0, bh);
        }));
        Spots.Add(new MdSpot(button, this, button));
    }

    /// A table: columns by their text (MdParser.ColumnShares), each row as tall as its tallest cell, a tint under the
    /// header, a line under each row.
    private void ArrangeTable(MdEnv env, double width)
    {
        var b = Block;
        int cols = Math.Max(1, b.Cols);
        var natural = new double[cols];
        foreach (var p in Parts) natural[p.Col] += p.Text.Widest;
        var share = MdParser.ColumnShares(natural);
        var x = new double[cols + 1];
        for (int c = 0; c < cols; c++) x[c + 1] = x[c] + share[c] * width;
        double y = 0;
        int i = 0;
        for (int r = 0; r < b.Rows.Count; r++)
        {
            double tallest = 0;
            int first = i;
            for (int c = 0; c < cols; c++, i++)
            {
                var p = Parts[i];
                p.X = x[c] + MdMetrics.CellX;
                p.Y = y + MdMetrics.CellY;
                p.Arrange(env, Math.Max(1, x[c + 1] - x[c] - 2 * MdMetrics.CellX));
                tallest = Math.Max(tallest, p.Height);
            }
            double row = tallest + 2 * MdMetrics.CellY + 1;
            if (r == 0) back.Add(new MdDeco(new Rect(0, y, width, row), (dc, rect) => dc.DrawRectangle(Theme.P.Tile, null, Snapped(env, rect))));
            back.Add(new MdDeco(new Rect(0, y + row - 1, width, 1), (dc, rect) => dc.DrawRectangle(Theme.P.Line, null, Snapped(env, rect))));
            y += row;
        }
        Height = y;
    }

    private static Rect Snapped(MdEnv env, Rect r)
    {
        double top = env.Snap(r.Y), bottom = env.Snap(r.Bottom);
        return new Rect(r.X, top, r.Width, Math.Max(1 / env.Ppd, bottom - top));
    }

    /// A quote: a bar down its left, its blocks beside it (their margins collapsing as anywhere, the last one's none).
    private void ArrangeQuote(MdEnv env, double width)
    {
        double inner = MdMetrics.QuoteBar + MdMetrics.QuoteLeft;
        double y = MdMetrics.QuoteTop, under = 0;
        for (int k = 0; k < children.Count; k++)
        {
            var c = children[k];
            c.Arrange(env, Math.Max(1, width - inner));
            y += k == 0 ? c.MarginTop : Math.Max(under, c.MarginTop);
            c.Shift(inner, y); // its parts are this quote's (Make)
            back.AddRange(c.back);
            front.AddRange(c.front);
            Spots.AddRange(c.Spots);
            y += c.Height;
            under = k == children.Count - 1 ? 0 : c.MarginBottom;
        }
        Height = y + MdMetrics.QuoteBottom;
        back.Insert(0, new MdDeco(new Rect(0, 0, MdMetrics.QuoteBar, Height), (dc, r) => dc.DrawRectangle(Theme.P.Line, null, r)));
    }

    /// Everything in it moved by (dx, dy) (a quote placing its blocks).
    private void Shift(double dx, double dy)
    {
        foreach (var p in Parts)
        {
            p.X += dx;
            p.Y += dy;
        }
        foreach (var d in back.Concat(front)) d.R.Offset(dx, dy);
        foreach (var s in Spots)
        {
            s.R.Offset(dx, dy);
            s.Inner.Offset(dx, dy);
        }
    }

    /// Its lines as drawn let go (it went off screen).
    public void Release()
    {
        foreach (var p in Parts) p.Text.Forget();
        marker?.Forget();
        foreach (var c in children) c.Release();
    }

    /// Whether `inner` is one of the blocks in it (a quote's).
    public bool Contains(MdBox inner) => children.Any(c => c == inner || c.Contains(inner));

    /// Its checklist items (and its quoted ones), in order.
    public void Items(List<MdBox> into)
    {
        if (Block.Done is not null) into.Add(this);
        foreach (var c in children) c.Items(into);
    }

    /// Its height guessed before it is shaped, from how much text it has (the scroll bar's while the rest is shaped).
    public double Estimate(MdMetrics m, double width)
    {
        static double Chars(List<MdSpan> spans, double size)
        {
            double w = 0;
            foreach (var s in spans)
                foreach (char c in s.Text) w += Hangul.IsSyllable(c) || LineBreaks.IsCjk(c) ? size : size * 0.52;
            return w;
        }
        double Lines(double textWidth, double column) => Math.Max(1, Math.Ceiling(textWidth / Math.Max(column, 1)));
        var b = Block;
        width = Math.Max(width, 1);
        return b.Kind switch
        {
            MdKind.Heading => Lines(Chars(b.Spans, m.HeadingSize(b.Level)), width) * m.HeadingLine(b.Level) + (m.Document && b.Level == 1 ? m.HeadingRuleGap + 1 : 0),
            MdKind.Paragraph => Lines(Chars(b.Spans, m.Size), width) * m.Line,
            MdKind.Item => Lines(Chars(b.Spans, m.Size), width - m.IndentOf(b.Indent) - m.MarkerWidth(false)) * m.Line,
            MdKind.Rule => 1,
            MdKind.Code => (b.Code.Count(c => c == '\n') + 1) * m.CodeLine + MdMetrics.CodeTop + MdMetrics.CodeBottom + (b.Lang.Length > 0 ? MdMetrics.LabelGap : 0),
            MdKind.Table => b.Rows.Sum(r => r.Count == 0 ? m.CellLine : r.Max(c => Lines(Chars(c, m.CellSize), width / Math.Max(1, b.Cols) - 16)) * m.CellLine) + b.Rows.Count * (2 * MdMetrics.CellY + 1),
            MdKind.Quote => children.Sum(c => c.Estimate(m, width - 14) + c.MarginBottom) + MdMetrics.QuoteTop + MdMetrics.QuoteBottom,
            _ => m.Line,
        };
    }

    // ── drawing ─────────────────────────────────────────────────────────────────────────────────

    /// What lies between `from` and `to` (from its top) drawn: tints and lines, the text, then markers, boxes, buttons.
    public void Draw(DrawingContext dc, MdEnv env, double from, double to)
    {
        foreach (var d in back)
            if (d.R.Bottom > from && d.R.Top < to) d.Paint(dc, d.R);
        foreach (var p in Parts) p.Draw(dc, env, from, to);
        foreach (var d in front)
            if (d.R.Bottom > from && d.R.Top < to) d.Paint(dc, d.R);
    }

    // ── places ──────────────────────────────────────────────────────────────────────────────────

    /// The text place nearest to (x, y) from its top left: the part under it (or nearest: up and down first), the place
    /// on its line. Null when it has no text (a rule).
    public (int Part, int At)? Hit(double x, double y)
    {
        if (Parts.Count == 0) return null;
        int best = 0;
        double bestDy = double.MaxValue, bestDx = double.MaxValue;
        for (int k = 0; k < Parts.Count; k++)
        {
            var p = Parts[k];
            double dy = y < p.Y ? p.Y - y : y > p.Y + p.Height ? y - p.Y - p.Height : 0;
            double dx = x < p.X ? p.X - x : x > p.X + p.Width ? x - p.X - p.Width : 0;
            if (dy < bestDy - 0.01 || (Math.Abs(dy - bestDy) <= 0.01 && dx < bestDx))
            {
                best = k;
                bestDy = dy;
                bestDx = dx;
            }
        }
        return (best, Parts[best].Hit(x, y));
    }

    /// The text of part `k` from `a` to `b`, and how it joins the part after it when copied: a table's cells in a row
    /// by tabs, anything else by a line break.
    public string Copy(int k, int a, int b) => Parts[k].Text.Copy(a, b);

    public string Joiner(int k) => IsTable && k + 1 < Parts.Count && Parts[k + 1].Row == Parts[k].Row ? "\t" : "\r\n";

    /// The checklist box or 복사 at (x, y), if any.
    public MdSpot? SpotAt(double x, double y)
    {
        foreach (var s in Spots)
            if (s.R.Contains(x, y)) return s;
        return null;
    }

    /// The link under (x, y), if any, and the part it is in.
    public (MdLink Link, MdPart Part)? LinkAt(double x, double y)
    {
        foreach (var p in Parts)
        {
            if (p.Text.Links.Length == 0 || y < p.Y || y >= p.Y + p.Height || x < p.X || x > p.X + p.Width) continue;
            int k = p.LineAt(y - p.Y);
            int s = p.Starts[k], e = p.End(k);
            double rx = x - p.X + p.Text.X[s];
            for (int i = s; i < e; i++)
                if (p.Text.X[i] <= rx && rx < p.Text.X[i + 1] && p.Text.LinkAt(i) is { } link) return (link, p);
        }
        return null;
    }

    /// The picture under (x, y), if any.
    public MdPicture? PictureAt(double x, double y)
    {
        foreach (var p in Parts)
        {
            if (p.Text.Pictures.Length == 0 || y < p.Y || y >= p.Y + p.Height) continue;
            int k = p.LineAt(y - p.Y);
            double rx = x - p.X + p.Text.X[p.Starts[k]];
            foreach (var (at, picture) in p.Text.Pictures)
                if (at >= p.Starts[k] && at < p.End(k) && p.Text.X[at] <= rx && rx < p.Text.X[at] + picture.Width) return picture;
        }
        return null;
    }

    /// Under a link's text on every line it covers: where its underline goes (hovered).
    public IEnumerable<Rect> Underlines(MdEnv env, MdLink link)
    {
        foreach (var p in Parts)
            foreach (var (s, e, l) in p.Text.Links)
            {
                if (l != link) continue;
                var look = p.Text.LookAt(s);
                double baseline = env.LineOf(look, p.LineHeight).Baseline;
                double size = look.FontRenderingEmSize, at = -look.Typeface.UnderlinePosition * size, thick = Math.Max(1 / env.Ppd, look.Typeface.UnderlineThickness * size);
                foreach (var r in p.Rects(s, e))
                    yield return new Rect(r.X, env.Snap(r.Bottom - p.LineHeight + baseline + at), r.Width, thick);
            }
    }
}
