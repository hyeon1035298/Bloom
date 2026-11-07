using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ui;

/// Chat answers (and 새 소식) drawn from Markdown: the blocks Core\MdModel.cs reads — headings, paragraphs, lists,
/// checklists (drawn only), quotes, rules, pipe tables, fenced code (with a copy button), **bold**, *italic*, `code`,
/// ~~strike~~, links — as one document per answer, so text can be dragged over and copied anywhere in it
/// (Controls\ReadText.cs). Korean words are kept together when lines wrap (display only; copying leaves the joiners out).
/// The panes (메모, 문서) draw the same blocks themselves (MdView).
internal static class Markdown
{
    /// One rendering's settings, handed down the blocks.
    private sealed class Ctx(Action<string> openLink, MdMetrics m, Func<MdSpan, FrameworkElement?>? picture)
    {
        public readonly Action<string> OpenLink = openLink;
        public readonly MdMetrics M = m;
        public readonly Func<MdSpan, FrameworkElement?>? Picture = picture;
        public double Size => M.Size;
    }

    /// The whole answer as one selectable document: drag across paragraphs, lists and tables to copy. With `picture`
    /// (바뀐 점), ![alt](src) is a picture: a paragraph that is one picture alone is the element `picture` makes of it (its
    /// link, [![alt](src)](url), in the span), and any other picture, or one it makes nothing of, its alt text.
    public static ReadText Render(string md, Action<string> openLink, double size = 14, Func<MdSpan, FrameworkElement?>? picture = null)
    {
        var ctx = new Ctx(openLink, new MdMetrics(size, false), picture);
        var doc = ReadText.Doc(size);
        Blocks(MdParser.Parse(md, false, picture is not null), ctx, doc.Blocks);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return new ReadText(doc);
    }

    private static void Blocks(List<MdBlock> blocks, Ctx ctx, BlockCollection into)
    {
        var m = ctx.M;
        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case MdKind.Code:
                    var (codeTop, codeBottom) = m.Around;
                    into.Add(new BlockUIContainer(CodeBlock(b.Code, b.Lang, ctx)) { Margin = new Thickness(0, codeTop, 0, codeBottom) });
                    break;
                case MdKind.Heading:
                    var h = ReadText.Paragraph(m.HeadingSize(b.Level), Theme.Semi);
                    var (top, bottom) = m.HeadingAround(b.Level, b.First);
                    h.Margin = new Thickness(0, top, 0, bottom);
                    Inline(b.Spans, h.Inlines, ctx);
                    into.Add(h);
                    break;
                case MdKind.Rule:
                    var (ruleTop, ruleBottom) = m.RuleAround;
                    into.Add(new BlockUIContainer(new SmoothBorder { Height = 1, Background = Theme.P.Line }) { Margin = new Thickness(0, ruleTop, 0, ruleBottom) });
                    break;
                case MdKind.Table:
                    into.Add(Table(b, ctx));
                    break;
                case MdKind.Quote:
                    var section = new Section
                    {
                        BorderBrush = Theme.P.Line,
                        BorderThickness = new Thickness(MdMetrics.QuoteBar, 0, 0, 0),
                        Padding = new Thickness(MdMetrics.QuoteLeft, MdMetrics.QuoteTop, 0, MdMetrics.QuoteBottom),
                        Margin = new Thickness(0, 0, 0, m.After),
                    };
                    Blocks(b.Children, ctx, section.Blocks);
                    if (section.Blocks.LastBlock is { } lastQ) lastQ.Margin = new Thickness(lastQ.Margin.Left, lastQ.Margin.Top, lastQ.Margin.Right, 0);
                    into.Add(section);
                    break;
                case MdKind.Item:
                    double left = m.IndentOf(b.Indent);
                    var item = b.Done is { } done
                        ? TaskItem(done, left, ctx)
                        : Item(Theme.T(b.Marker, ctx.Size, Theme.P.TextFaint, Theme.Regular, false), m.MarkerWidth(char.IsDigit(b.Marker[0])), left, ctx);
                    item.Margin = new Thickness(item.Margin.Left, 0, 0, m.ItemAfter(b.ListEnd));
                    Inline(b.Spans, item.Inlines, ctx);
                    into.Add(item);
                    break;
                case MdKind.Paragraph when b.Spans is [{ Kind: MdSpanKind.Picture } only] && ctx.Picture?.Invoke(only) is { } shown:
                    into.Add(new BlockUIContainer(shown) { Margin = new Thickness(0, m.After / 2, 0, m.After * 1.5) });
                    break;
                default:
                    var para = ReadText.Paragraph(ctx.Size, Theme.Regular);
                    para.Margin = new Thickness(0, 0, 0, m.After);
                    Inline(b.Spans, para.Inlines, ctx);
                    into.Add(para);
                    break;
            }
        }
    }

    /// One list item: the marker (a line of text's height) in a column of its own (`width`), the text hanging beside it
    /// when it wraps.
    private static Paragraph Item(FrameworkElement mark, double width, double indent, Ctx ctx)
    {
        double size = ctx.Size;
        var p = ReadText.Paragraph(size, Theme.Regular);
        p.Margin = new Thickness(indent + width, 0, 0, 0);
        p.TextIndent = -width;
        mark.Width = width;
        // an inline element sits on the baseline by its bottom edge: the part under the marker's own baseline hangs below
        mark.Margin = new Thickness(0, 0, 0, -(Theme.Sans.LineSpacing - Theme.Sans.Baseline) * size);
        p.Inlines.Add(new InlineUIContainer(mark) { BaselineAlignment = BaselineAlignment.Baseline });
        return p;
    }

    /// A checklist item: a box in the marker's column, ticked for [x] (its text dimmed); in a chat answer it is only
    /// drawn (the panes' boxes take clicks: MdView).
    private static Paragraph TaskItem(bool done, double indent, Ctx ctx)
    {
        double size = ctx.Size, s = ctx.M.BoxSide;
        var box = new SmoothBorder
        {
            Width = s,
            Height = s,
            CornerRadius = new CornerRadius(ctx.M.BoxRadius),
            BorderThickness = new Thickness(MdMetrics.BoxStroke),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = done ? Theme.P.Accent : Brushes.Transparent,
            BorderBrush = done ? Theme.P.Accent : Theme.P.TextFaint,
        };
        // over the box rather than in it: the glyph's line is taller than the box and would be cut off there
        var tick = Theme.Glyph(Search.G.Check, ctx.M.TickSize, Theme.P.AccentInk);
        tick.Width = s;
        tick.LineHeight = double.NaN; // not the paragraph's, which it would take through the document
        tick.TextAlignment = TextAlignment.Center;
        tick.HorizontalAlignment = HorizontalAlignment.Left;
        tick.Visibility = done ? Visibility.Visible : Visibility.Hidden;
        // the whole spot in the column, a line of text high
        var spot = new Grid { Height = Theme.Sans.LineSpacing * size, Background = Brushes.Transparent, Children = { box, tick } };
        var p = Item(spot, ctx.M.BoxColumn, indent, ctx);
        if (done) p.Foreground = Theme.P.TextDim;
        return p;
    }

    public static TextBlock Para(double size, FontWeight weight) => new()
    {
        FontFamily = Theme.Sans,
        FontSize = size,
        FontWeight = weight,
        Foreground = Theme.P.Text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = Math.Round(size * 1.6),
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// Fenced code: monospace on its own tint, with 복사 for all of it. It is drawn as a plain TextBlock and becomes
    /// selectable (a ReadText, laid out the same) when the mouse first comes over it: a text box of its own inside the
    /// answer's costs about 20 KB of memory, which most code blocks are never dragged over to need.
    private static SmoothBorder CodeBlock(string code, string lang, Ctx ctx)
    {
        double size = ctx.M.CodeSize, lineHeight = ctx.M.CodeLine;
        var grid = new Grid();
        var holder = new Border
        {
            Margin = new Thickness(0, lang.Length > 0 ? MdMetrics.LabelGap : 0, 0, 0),
            Background = Brushes.Transparent, // the mouse finds it between the lines too
            Child = new TextBlock { Text = code, FontFamily = Theme.Mono, FontSize = size, Foreground = Theme.P.Text, TextWrapping = TextWrapping.Wrap, LineHeight = lineHeight },
        };
        holder.MouseEnter += Selectable;
        void Selectable(object sender, MouseEventArgs e)
        {
            holder.MouseEnter -= Selectable;
            var p = new Paragraph(new Run(code)) { FontFamily = Theme.Mono, FontSize = size, LineHeight = lineHeight, Margin = new Thickness(0) };
            holder.Child = new ReadText(ReadText.Doc(size, p));
        }
        grid.Children.Add(holder);
        if (lang.Length > 0)
        {
            var l = Theme.T(lang, MdMetrics.LabelSize, Theme.P.TextFaint, Theme.Medium);
            l.VerticalAlignment = VerticalAlignment.Top;
            l.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(l);
        }
        var copyText = Theme.T(Tr("복사", "Copy"), MdMetrics.LabelSize, Theme.P.TextDim, Theme.Medium, false);
        var copy = new SmoothBorder
        {
            Child = copyText,
            Padding = new Thickness(MdMetrics.CopyPadX, MdMetrics.CopyPadTop, MdMetrics.CopyPadX, MdMetrics.CopyPadBottom),
            CornerRadius = new CornerRadius(MdMetrics.CopyRadius),
            Background = Theme.P.Key,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, -MdMetrics.CopyOutTop, -MdMetrics.CopyOutRight, 0),
        };
        copy.MouseLeftButtonUp += (_, e) =>
        {
            Search.Launch.Copy(code);
            copyText.Text = Tr("복사 완료", "Copied");
            var t = new DispatcherTimer { Interval = MdMetrics.CopiedFor };
            t.Tick += (_, _) =>
            {
                t.Stop();
                copyText.Text = Tr("복사", "Copy");
            };
            t.Start();
            e.Handled = true;
        };
        grid.Children.Add(copy);
        return new SmoothBorder
        {
            Background = Theme.P.Code,
            CornerRadius = new CornerRadius(MdMetrics.CodeRadius),
            Padding = new Thickness(MdMetrics.CodeLeft, MdMetrics.CodeTop, MdMetrics.CodeRight, MdMetrics.CodeBottom),
            Child = grid,
        };
    }

    private static Table Table(MdBlock t, Ctx ctx)
    {
        var m = ctx.M;
        var (top, bottom) = m.Around;
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, top, 0, bottom) };
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var widths = new double[t.Cols];
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < t.Cols; c++)
            {
                var p = ReadText.Paragraph(m.CellSize, r == 0 ? Theme.Semi : Theme.Regular);
                p.Margin = new Thickness(0);
                p.LineHeight = m.CellLine;
                if (c < t.Rows[r].Count) Inline(t.Rows[r][c], p.Inlines, ctx);
                widths[c] += Width(p.Inlines, p.FontSize, p.FontWeight);
                var cell = new TableCell(p) { Padding = new Thickness(MdMetrics.CellX, MdMetrics.CellY, MdMetrics.CellX, MdMetrics.CellY), BorderBrush = Theme.P.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
                if (r == 0) cell.Background = Theme.P.Tile; // the header row on a faint tint
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        foreach (double share in MdParser.ColumnShares(widths)) table.Columns.Add(new TableColumn { Width = new GridLength(share, GridUnitType.Star) });
        return table;
    }

    /// Inlines' text as wide as it is drawn on one line, each run in its own font (code's mono, the rest the text's).
    private static double Width(InlineCollection inlines, double size, FontWeight weight)
    {
        double w = 0;
        foreach (var inline in inlines)
        {
            if (inline is Span span) w += Width(span.Inlines, size, weight);
            else if (inline is Run run && run.Text.Length > 0)
            {
                var family = run.ReadLocalValue(TextElement.FontFamilyProperty) as FontFamily ?? Theme.Sans;
                double at = run.ReadLocalValue(TextElement.FontSizeProperty) is double s ? s : size;
                w += new FormattedText(run.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), at, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
            }
        }
        return w;
    }

    // ── inline ──────────────────────────────────────────────────────────────────────────────────

    /// A block's spans as inlines: styled runs (Korean kept together on wrap), line breaks, and each link's spans in one
    /// Hyperlink.
    private static void Inline(List<MdSpan> spans, InlineCollection into, Ctx ctx)
    {
        Hyperlink? link = null;
        MdLink? linkOf = null;
        foreach (var s in spans)
        {
            System.Windows.Documents.Inline inline;
            if (s.Kind == MdSpanKind.Break) inline = new LineBreak();
            else if (s.Kind == MdSpanKind.Picture) inline = new Run(Hangul.KeepAll(s.Text)); // one not drawn: its alt text
            else if ((s.Style & MdStyle.Code) != 0) inline = Code(s.Text, ctx.M.CodeSize);
            else inline = new Run(Hangul.KeepAll(s.Text));
            if ((s.Style & MdStyle.Bold) != 0) inline.FontWeight = FontWeights.Bold;
            if ((s.Style & MdStyle.Italic) != 0) inline.FontStyle = FontStyles.Italic;
            if ((s.Style & MdStyle.Strike) != 0) inline.TextDecorations = TextDecorations.Strikethrough;
            if (s.Link is null)
            {
                link = null;
                into.Add(inline);
                continue;
            }
            if (link is null || linkOf != s.Link)
            {
                link = Link(s.Link.Url, ctx.OpenLink);
                linkOf = s.Link;
                into.Add(link);
            }
            link.Inlines.Add(inline);
        }
    }

    /// How wide inline code's padding is each side, against a figure space of the code's font: a full one read as a word
    /// space before the text that follows ("견적서_1007.pdf 를").
    private const double CodePadScale = 0.35;

    /// Inline code as drawn: a narrow padding each side that no line break parts from it (Hangul.CodePad at a small
    /// size), and in a long one a chance to break where MdParser.CodeBreakAfter allows (BreakHint; copied as written:
    /// Hangul.Unjoin).
    private static Span Code(string code, double size)
    {
        var sb = new StringBuilder(code.Length + 8);
        for (int k = 0; k < code.Length; k++)
        {
            sb.Append(code[k]);
            if (MdParser.CodeBreakAfter(code, k)) sb.Append(Hangul.BreakHint);
        }
        Run Pad() => new(Hangul.CodePad.ToString()) { FontSize = size * CodePadScale };
        var span = new Span { FontFamily = Theme.Mono, FontSize = size, Background = Theme.P.Code };
        span.Inlines.Add(Pad());
        span.Inlines.Add(new Run(sb.ToString()));
        span.Inlines.Add(Pad());
        return span;
    }

    private static Hyperlink Link(string url, Action<string> openLink)
    {
        var h = new Hyperlink { Foreground = Theme.P.AccentText, TextDecorations = null, Cursor = Cursors.Hand };
        h.Click += (_, _) => openLink(url);
        h.MouseEnter += (_, _) => h.TextDecorations = TextDecorations.Underline;
        h.MouseLeave += (_, _) => h.TextDecorations = null;
        return h;
    }
}
