using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Bloom.Core;

/// What a top-level (or quoted) Markdown block is.
internal enum MdKind { Heading, Paragraph, Item, Quote, Rule, Code, Table }

/// How a piece of inline text is drawn.
[Flags]
internal enum MdStyle { None = 0, Bold = 1, Italic = 2, Strike = 4, Code = 8 }

/// A piece of inline text, a hard line break, or a picture.
internal enum MdSpanKind { Text, Break, Picture }

/// A link's target; every span of one link's text holds the same one (a link is the spans that share it).
internal sealed class MdLink(string url)
{
    public string Url { get; } = url;
}

/// Inline content: text in one style (Kind Text), a hard break, or a picture (Text: its alt text, Source: its path or
/// address). `Copy`: what copying the whole span gives when that isn't Text (an autolink shown shortened).
internal sealed record MdSpan(MdSpanKind Kind, string Text, MdStyle Style = MdStyle.None, MdLink? Link = null, string? Copy = null, string? Source = null)
{
    public static readonly MdSpan Break = new(MdSpanKind.Break, "");
}

/// One block as read from the Markdown source (MdParser). Which fields count goes by Kind.
internal sealed class MdBlock
{
    public MdKind Kind;

    /// The source line it starts on (0-based) and the one after its last (a quote's children: in the whole text).
    /// A list item's Line is the line its box ticks (MarkdownText.ToggleTask).
    public int Line, End;

    /// The first block in its container (the text, or a quote): a heading gets no room above.
    public bool First;

    /// Heading: 1–6.
    public int Level;

    /// Heading, paragraph, list item: the text.
    public List<MdSpan> Spans = [];

    /// List item: its nesting (each 2 spaces of indent, a tab as 4), its marker ("•", "◦", "3.") unless it is a
    /// checklist item (Done: ticked or not), and whether it is the last of its list.
    public int Indent;
    public string Marker = "";
    public bool? Done;
    public bool ListEnd;

    /// Quote: what is quoted.
    public List<MdBlock> Children = [];

    /// Fenced code: the code (lines at '\n') and its language.
    public string Code = "", Lang = "";

    /// Table: rows of cells (row 0 the header), each cell's text; Cols: the most cells in a row.
    public List<List<List<MdSpan>>> Rows = [];
    public int Cols;
}

/// Markdown read into blocks and inline spans — the one reading of it: Ui\Markdown.cs builds a chat answer's
/// FlowDocument from these, and the panes' view (Ui\MdView.cs) lays them out itself. Headings, paragraphs, lists,
/// checklists, quotes, rules, pipe tables, fenced code, **bold**, *italic*, `code`, ~~strike~~, links, autolinks and,
/// in a pane, ![pictures](src). `document`: a .md file's reading (a paragraph's lines joined as a Markdown file does);
/// otherwise (a note, a chat answer) each line breaks.
internal static class MdParser
{
    private static readonly Regex ListItem = MarkdownText.ListItem, TaskBox = MarkdownText.TaskBox; // one rule, drawn and ticked
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    private static readonly Regex RuleLine = new(@"^\s*([-*_])(\s*\1){2,}\s*$");
    private static readonly Regex TableSep = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");

    private const string TokenPattern =
        @"(\*\*(?<b>.+?)\*\*)|(__(?<b2>.+?)__)|(~~(?<s>.+?)~~)|(`(?<c>[^`]+)`)|(\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)\))|(?<u>https?://[^\s<>()\]]+[^\s<>()\].,;:!?'""])|((?<![\w*])\*(?<i>[^*\s][^*]*?)\*(?![\w*]))|((?<![\w_])_(?<i2>[^_\s][^_]*?)_(?![\w_]))";

    // Singleline: a document's paragraph comes whole, its line ends in it (Lines), and **bold** may go over one
    private static readonly Regex Token = new(TokenPattern, RegexOptions.Compiled | RegexOptions.Singleline);

    // a pane's: ![alt](src) pictures too, and a linked one [![alt](src)](url) (a chat answer keeps reading them as "!" and a link)
    private static readonly Regex PaneToken = new(@"(\[!\[(?<la>[^\]]*)\]\((?<ls>[^)\s]+)\)\]\((?<lu2>[^)\s]+)\))|(!\[(?<pa>[^\]]*)\]\((?<ps>[^)\s]+)\))|" + TokenPattern, RegexOptions.Compiled | RegexOptions.Singleline);

    /// An autolink longer than this is shown shortened with "…" (copied whole).
    public const int AutolinkShown = 60;

    private sealed record Ctx(bool Document, bool Pictures);

    /// `md` as blocks. `pictures`: a pane's (![alt](src) is a picture); a chat answer's are "!" and a link.
    public static List<MdBlock> Parse(string md, bool document, bool pictures)
    {
        var into = new List<MdBlock>();
        Blocks(md.Replace("\r\n", "\n"), 0, new Ctx(document, pictures), into);
        return into;
    }

    /// `md`'s blocks into `into`; `lineBase`: the source line md's first line is (a quote's text keeps its lines' numbers).
    private static void Blocks(string md, int lineBase, Ctx ctx, List<MdBlock> into)
    {
        string[] lines = md.Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trim = line.TrimStart();
            int start = i;
            if (MarkdownText.Fence(line) is { } fence)
            {
                string lang = trim[3..].Trim();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && MarkdownText.Fence(lines[i]) != fence)
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                i++; // closing fence (or the end while streaming)
                Add(into, new MdBlock { Kind = MdKind.Code, Code = code.ToString(), Lang = lang }, lineBase, start, Math.Min(i, lines.Length));
                continue;
            }
            if (trim.Length == 0)
            {
                i++;
                continue;
            }
            var h = Heading.Match(line);
            if (h.Success)
            {
                var b = new MdBlock { Kind = MdKind.Heading, Level = h.Groups[1].Value.Length };
                Inline(h.Groups[2].Value, MdStyle.None, null, ctx, b.Spans);
                i++;
                Add(into, b, lineBase, start, i);
                continue;
            }
            if (RuleLine.IsMatch(line))
            {
                i++;
                Add(into, new MdBlock { Kind = MdKind.Rule }, lineBase, start, i);
                continue;
            }
            if (trim.StartsWith('|') && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1]))
            {
                var t = new MdBlock { Kind = MdKind.Table };
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    if (!TableSep.IsMatch(lines[i])) Row(lines[i], ctx, t);
                    i++;
                }
                Add(into, t, lineBase, start, i);
                continue;
            }
            if (trim.StartsWith('>'))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    string q = lines[i].TrimStart()[1..];
                    if (q.StartsWith(' ')) q = q[1..];
                    quote.Append(q).Append('\n');
                    i++;
                }
                var b = new MdBlock { Kind = MdKind.Quote };
                Blocks(quote.ToString().TrimEnd(), lineBase + start, ctx, b.Children); // one quote line per source line
                Add(into, b, lineBase, start, i);
                continue;
            }
            if (ListItem.IsMatch(line))
            {
                int number = 0;
                MdBlock? prev = null;
                var itemLines = new List<string>(); // the item's text, its continuation lines after it
                while (i < lines.Length)
                {
                    var m = ListItem.Match(lines[i]);
                    if (!m.Success)
                    {
                        // a wrapped continuation line belongs to the previous item; a fence (indented too) opens code, as
                        // everywhere (MarkdownText.Fence: what ticking a box goes by)
                        if (lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim().Length > 0 && prev is not null && MarkdownText.Fence(lines[i]) is null)
                        {
                            itemLines.Add(ctx.Document ? lines[i].TrimStart() : lines[i].Trim());
                            i++;
                            prev.End = lineBase + i;
                            continue;
                        }
                        break;
                    }
                    if (prev is not null) Lines(itemLines, ctx, prev.Spans);
                    itemLines.Clear();
                    int indent = m.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    string marker = m.Groups[2].Value;
                    string body = m.Groups[3].Value;
                    bool ordered = char.IsDigit(marker[0]);
                    number = ordered ? int.TryParse(marker.TrimEnd('.', ')'), out int n) ? n : number + 1 : number;
                    prev = new MdBlock { Kind = MdKind.Item, Indent = indent };
                    // a checklist item: its box where the marker would be
                    if (TaskBox.Match(body) is { Success: true } task)
                    {
                        prev.Done = task.Groups[1].Value != " ";
                        body = body[task.Length..];
                    }
                    else prev.Marker = ordered ? number + "." : indent > 0 ? "◦" : "•";
                    itemLines.Add(body);
                    Add(into, prev, lineBase, i, i + 1);
                    i++;
                }
                if (prev is not null)
                {
                    Lines(itemLines, ctx, prev.Spans);
                    prev.ListEnd = true;
                }
                continue;
            }
            // paragraph: until a blank line or another block
            var para = new MdBlock { Kind = MdKind.Paragraph };
            var paraLines = new List<string>();
            while (i < lines.Length)
            {
                string l = lines[i];
                string lt = l.TrimStart();
                if (lt.Length == 0 || MarkdownText.Fence(l) is not null || Heading.IsMatch(l) || ListItem.IsMatch(l) || lt.StartsWith('>') || (lt.StartsWith('|') && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1])) || RuleLine.IsMatch(l)) break;
                paraLines.Add(ctx.Document ? lt : l.TrimEnd());
                i++;
            }
            Lines(paraLines, ctx, para.Spans);
            Add(into, para, lineBase, start, i);
        }
    }

    private static void Add(List<MdBlock> into, MdBlock b, int lineBase, int start, int end)
    {
        b.First = into.Count == 0;
        b.Line = lineBase + start;
        b.End = lineBase + end;
        into.Add(b);
    }

    /// One table row's cells, trimmed, as inline text.
    private static void Row(string row, Ctx ctx, MdBlock table)
    {
        string t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        var parts = t.Split('|');
        var cells = new List<List<MdSpan>>(parts.Length);
        foreach (string part in parts)
        {
            var cell = new List<MdSpan>();
            Inline(part.Trim(), MdStyle.None, null, ctx, cell);
            cells.Add(cell);
        }
        table.Cols = Math.Max(table.Cols, cells.Count);
        table.Rows.Add(cells);
    }

    /// A paragraph's (or a list item's) source lines as its text. A document joins them as a Markdown file does
    /// (CommonMark): a line ending in two spaces or a backslash breaks there, any other joins the next with a space
    /// (Text, over the whole: bold or code may go on over a line end). A note or a chat answer breaks at each.
    private static void Lines(List<string> lines, Ctx ctx, List<MdSpan> into)
    {
        if (ctx.Document)
        {
            Inline(string.Join('\n', lines).TrimEnd(), MdStyle.None, null, ctx, into);
            return;
        }
        for (int k = 0; k < lines.Count; k++)
        {
            if (k > 0) into.Add(MdSpan.Break);
            Inline(lines[k], MdStyle.None, null, ctx, into);
        }
    }

    private static void Inline(string s, MdStyle style, MdLink? link, Ctx ctx, List<MdSpan> into)
    {
        int at = 0;
        foreach (Match m in (ctx.Pictures ? PaneToken : Token).Matches(s))
        {
            if (m.Index > at) Text(s[at..m.Index], style, link, into);
            if (m.Groups["la"].Success) into.Add(new MdSpan(MdSpanKind.Picture, m.Groups["la"].Value, style, new MdLink(m.Groups["lu2"].Value), Source: m.Groups["ls"].Value));
            else if (m.Groups["pa"].Success) into.Add(new MdSpan(MdSpanKind.Picture, m.Groups["pa"].Value, style, link, Source: m.Groups["ps"].Value));
            else if (m.Groups["b"].Success || m.Groups["b2"].Success) Inline(m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value, style | MdStyle.Bold, link, ctx, into);
            else if (m.Groups["s"].Success) Inline(m.Groups["s"].Value, style | MdStyle.Strike, link, ctx, into);
            else if (m.Groups["c"].Success) into.Add(new MdSpan(MdSpanKind.Text, m.Groups["c"].Value.Replace('\n', ' '), style | MdStyle.Code, link)); // a line end in code: a space
            else if (m.Groups["lt"].Success) Text(m.Groups["lt"].Value, style, new MdLink(m.Groups["lu"].Value), into); // over a line end it joins as any text
            else if (m.Groups["u"].Success)
            {
                string u = m.Groups["u"].Value;
                into.Add(new MdSpan(MdSpanKind.Text, Autolink(u), style, new MdLink(u), u.Length > AutolinkShown ? u : null));
            }
            else if (m.Groups["i"].Success || m.Groups["i2"].Success) Inline(m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value, style | MdStyle.Italic, link, ctx, into);
            at = m.Index + m.Length;
        }
        if (at < s.Length) Text(s[at..], style, link, into);
    }

    /// An address as an autolink shows it: at most AutolinkShown characters, the rest "…".
    public static string Autolink(string url) => url.Length > AutolinkShown ? url[..(AutolinkShown - 3)] + "…" : url;

    /// Plain text. Only a document's has line ends in it (Lines): one after two spaces or a backslash breaks the line,
    /// any other joins the lines with a space.
    private static void Text(string s, MdStyle style, MdLink? link, List<MdSpan> into)
    {
        string[] parts = s.Split('\n');
        for (int k = 0; k < parts.Length; k++)
        {
            string part = parts[k];
            bool hard = false;
            if (k < parts.Length - 1)
            {
                if (part.EndsWith("  ", StringComparison.Ordinal)) hard = true;
                else if (part.EndsWith('\\'))
                {
                    hard = true;
                    part = part[..^1];
                }
                part = part.TrimEnd(' ', '\t');
                if (!hard) part += " ";
            }
            if (part.Length > 0) into.Add(new MdSpan(MdSpanKind.Text, part, style, link));
            if (hard) into.Add(MdSpan.Break);
        }
    }

    /// Inline code may break after . / \ _ - when a letter or digit follows (a long name or path wraps there): the one
    /// rule for both drawings of code (chat's break chances, the view's line breaker).
    public static bool CodeBreakAfter(ReadOnlySpan<char> code, int k) => code[k] is '.' or '/' or '\\' or '_' or '-' && k + 1 < code.Length && char.IsLetterOrDigit(code[k + 1]);

    /// Each column's share of a table's width by how wide its cells' text is (all rows, on one line), kept between 14
    /// and 45 % (fewer columns: what they must): a column of long text or code gets the room, a short one still holds a
    /// word or a path ("Installer/", "WeatherService." in a 760 px document window). The shares are one scale times the
    /// widths, clamped, the scale found so they make the whole width.
    public static double[] ColumnShares(double[] widths)
    {
        int cols = widths.Length;
        // two columns go by their text too (45 % each couldn't fill the width); eight or more keep half an even share
        double lo = cols * 0.14 < 1 ? 0.14 : 0.5 / cols, hi = cols == 2 ? 1 - lo : Math.Max(0.45, 1.0 / cols);
        double low = 0, high = 1;
        foreach (double w in widths) high = Math.Max(high, hi / Math.Max(w, 1)); // every column at hi: at least the whole
        for (int round = 0; round < 60; round++)
        {
            double scale = (low + high) / 2, sum = 0;
            foreach (double w in widths) sum += Math.Clamp(scale * w, lo, hi);
            if (sum > 1) high = scale;
            else low = scale;
        }
        var share = new double[cols];
        double total = 0;
        for (int c = 0; c < cols; c++) total += share[c] = Math.Clamp(low * widths[c], lo, hi);
        for (int c = 0; c < cols; c++) share[c] /= total;
        return share;
    }
}

/// The spacing and sizes of Markdown as drawn — chat answers and notes (document false) or a .md file's look — at a
/// text size: the one table both drawings read (Ui\Markdown.cs for chat, Ui\MdView.cs for the panes).
internal readonly record struct MdMetrics(double Size, bool Document)
{
    /// A chat answer's measure (at 14) at this size: code 12.5, a list marker's column 16 …
    public double Scaled(double at14) => at14 * Size / 14;

    /// A paragraph's line: 1.6 × the size.
    public static double LineOf(double size) => Math.Round(size * 1.6);

    public double Line => LineOf(Size);

    /// The room under a paragraph or a quote: 8, a document's 12.
    public double After => Document ? Scaled(12) : 8;

    /// Around a code block or a table: 2 above and 10 under, a document's 4 and 14.
    public (double Top, double Bottom) Around => Document ? (Scaled(4), Scaled(14)) : (2, 10);

    /// A rule's room: 6 above and 10 under, a document's 16 each.
    public (double Top, double Bottom) RuleAround => Document ? (Scaled(16), Scaled(16)) : (6, 10);

    /// A heading's size: a document's at 14 # 24, ## 19, ### 16, the rest 14.5; a chat answer's the text's + 3, 1.5, 0.5.
    public double HeadingSize(int level) => Document ? Scaled(level switch { 1 => 24, 2 => 19, 3 => 16, _ => 14.5 }) : Size + (level <= 1 ? 3 : level == 2 ? 1.5 : 0.5);

    /// A heading's line: a document's 1.3 × its size, a chat answer's as a paragraph's.
    public double HeadingLine(int level) => Document ? Math.Round(HeadingSize(level) * 1.3) : LineOf(HeadingSize(level));

    /// The room above a heading (none first: a document's 28, 20, 16, a chat answer's 6) and under it (8, # 12; chat 6).
    public (double Top, double Bottom) HeadingAround(int level, bool first) => Document
        ? (first ? 0 : Scaled(level <= 2 ? 28 : level == 3 ? 20 : 16), Scaled(level == 1 ? 12 : 8))
        : (first ? 0 : 6, 6);

    /// A document's # has a faint line under it, this far below the text.
    public double HeadingRuleGap => Scaled(8);

    /// Under a list item (3, a document's 5) and under a list's last (the item's 3 + the list's own 8; a document's 5 + 9).
    public double ItemAfter(bool listEnd) => listEnd ? (Document ? Scaled(14) : 11) : Document ? Scaled(5) : 3;

    /// A list item's marker column: a number's 22, a bullet's 16; a checklist box's its side and a gap.
    public double MarkerWidth(bool ordered) => Scaled(ordered ? 22 : 16);

    public double BoxSide => Math.Round(Size * 0.95);

    public double BoxColumn => BoxSide + Math.Round(Size * 0.55);

    /// A checklist box's corners and its line, and its tick's size.
    public double BoxRadius => Math.Round(BoxSide * 0.3);

    public const double BoxStroke = 1.5;

    public double TickSize => Math.Round(Size * 0.75);

    /// A list item's indent per level (up to 4).
    public double IndentOf(int level) => Math.Min(level, 4) * Scaled(18);

    /// A quote: its bar, and the room inside it.
    public const double QuoteBar = 2, QuoteLeft = 12, QuoteTop = 2, QuoteBottom = 2;

    /// Code: monospace at 12.5 (scaled), lines 19 apart (scaled), inside 12/9/12/10, round 8, a language label at 11 with
    /// the code 18 below it.
    public double CodeSize => Scaled(12.5);

    public double CodeLine => Math.Round(Scaled(19));

    public const double CodeLeft = 12, CodeTop = 9, CodeRight = 12, CodeBottom = 10, CodeRadius = 8, LabelSize = 11, LabelGap = 18;

    /// Code's 복사 button: its label at LabelSize with 7 / 1 / 7 / 2 round it, round 5, out past the code's inside 4
    /// right and 2 up; 복사 완료 shows on it for CopiedFor.
    public const double CopyPadX = 7, CopyPadTop = 1, CopyPadBottom = 2, CopyRadius = 5, CopyOutRight = 4, CopyOutTop = 2;

    public static readonly TimeSpan CopiedFor = TimeSpan.FromMilliseconds(1300);

    /// A table's text: a size smaller, lines 1.5 × that, cells 8 / 6 inside, a 1 px line under each.
    public double CellSize => Size - 1;

    public double CellLine => Math.Round((Size - 1) * 1.5);

    public const double CellX = 8, CellY = 6;
}
