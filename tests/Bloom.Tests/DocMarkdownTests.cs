using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Documents;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// Markdown as read once for every drawing (Core\MdModel.cs): a .md file's lines joined as a Markdown file, hard breaks,
/// table columns by their text, inline code that copies as written in chat (Ui\Markdown.cs), checklist lines.
internal static class DocMarkdownTests
{
    public static void SoftWrapJoinsInDocuments()
    {
        const string md = "Read this first before\nchanging anything.\n\n- an item that\n  goes on here\n- next";
        var doc = MdParser.Parse(md, true, true);
        Check.Eq(Plain(doc[0].Spans), "Read this first before changing anything.", "a document joins a paragraph's lines with a space");
        Check.Eq(Plain(doc[1].Spans), "an item that goes on here", "and a list item's continuation lines");
        foreach (var (pictures, what) in new[] { (true, "a note"), (false, "a chat answer") })
        {
            var other = MdParser.Parse(md, false, pictures);
            Check.Eq(Plain(other[0].Spans), "Read this first before\nchanging anything.", what + " keeps each line");
            Check.Eq(Plain(other[1].Spans), "an item that\ngoes on here", what + "'s list item too");
        }
        var bold = MdParser.Parse("**bold over\ntwo lines** after", true, true)[0].Spans;
        Check.Eq(string.Concat(bold.Where(s => (s.Style & MdStyle.Bold) != 0).Select(s => s.Text)), "bold over two lines", "bold goes on over a line end");
        Check.True(bold.All(s => s.Kind != MdSpanKind.Break), "joined, no break in it");
    }

    public static void HardBreaks()
    {
        var p = MdParser.Parse("two spaces  \nback\\\nslash\nsoft  \t\nlast\\", true, true).Single();
        Check.Eq(Plain(p.Spans), "two spaces\nback\nslash soft last\\", "two spaces or a backslash break the line (not a tab); the last line's backslash stays");
        var code = MdParser.Parse("see `Logs\\\nmore` here", true, true).Single().Spans;
        Check.Eq(string.Join("|", code.Select(s => s.Text)), "see |Logs\\ more| here", "a code span over a line end: a space, no break");
        Check.True(code[1].Style == MdStyle.Code, "the middle span is code");
    }

    public static void ModelBlocks()
    {
        const string md = "# Title\n\n## Sub\n\npara with [a link](https://example.com) and https://example.com/" + "a-very-long-address/that/goes/on/and/on/and/on/for/more/than/sixty/characters\n\n1. one\n   - [x] done\n\n> quoted\n> > deeper\n\n---\n\n```cs\nvar x = 1;\n\tindented\n```\n\n| a | b |\n|---|---|\n| 1 |\n";
        var blocks = MdParser.Parse(md, true, true);
        Check.Eq(string.Join(",", blocks.Select(b => b.Kind)), "Heading,Heading,Paragraph,Item,Item,Quote,Rule,Code,Table", "the blocks in order");
        Check.True(blocks[0].First && blocks[0].Level == 1 && !blocks[1].First && blocks[1].Level == 2, "heading levels, the first one first");
        var para = blocks[2].Spans;
        var links = para.Where(s => s.Link is not null).ToList();
        Check.Eq(links[0].Link!.Url, "https://example.com", "a link's address");
        Check.True(links[1].Text.EndsWith('…') && links[1].Text.Length < MdParser.AutolinkShown, "a long autolink shown shortened");
        Check.Eq(links[1].Copy, links[1].Link!.Url, "and copied whole");
        Check.Eq(blocks[3].Marker, "1.", "an ordered item's number");
        Check.True(blocks[4].Done == true && blocks[4].Indent == 1 && blocks[4].ListEnd, "a ticked checklist item, nested, the list's last");
        Check.True(blocks[5].Children.Count == 2 && blocks[5].Children[1].Kind == MdKind.Quote, "a quote in a quote");
        Check.True(blocks[7].Lang == "cs" && blocks[7].Code == "var x = 1;\n\tindented", "code and its language");
        Check.True(blocks[8].Cols == 2 && blocks[8].Rows.Count == 2 && blocks[8].Rows[1].Count == 1, "a short row keeps its cells");
        Check.True(blocks[3].Line == 6 && blocks[4].Line == 7, "list items know their source lines");
    }

    public static void TableColumnsByText()
    {
        var even = MdParser.ColumnShares([40, 40]);
        Check.Near(even[0], 0.5, 1e-6, "equal text, equal columns");
        var wide = MdParser.ColumnShares([10, 10, 5000]);
        Check.Near(wide[2], 0.45, 1e-6, "a long column stops at 45 %");
        Check.Near(wide[0], 0.275, 1e-6, "the others share the rest");
        Check.Near(MdParser.ColumnShares([10, 5000])[1], 0.86, 1e-6, "two columns by their text too: the short one keeps 14 %");
        var eight = MdParser.ColumnShares([10, 1000, 1000, 1000, 1000, 1000, 1000, 1000]);
        Check.Near(eight[0], 0.0625, 1e-6, "eight columns: a short one keeps half an even share");
        Check.True(eight.Skip(1).All(s => System.Math.Abs(s - 0.9375 / 7) < 1e-6), "the long ones the rest");
        var many = MdParser.ColumnShares([10, 1000, 1000, 1000, 1000]);
        Check.Near(many[0], 0.14, 1e-6, "a short column keeps 14 %");
        Check.Near(many.Sum(), 1, 1e-6, "the shares make the whole width");
        Check.True(many.Skip(1).All(s => System.Math.Abs(s - 0.215) < 1e-6), "the long ones share the rest evenly");
        var some = MdParser.ColumnShares([100, 200, 300]);
        Check.Near(some[1] / some[0], 2, 1e-6, "within the bounds, by width");

        var table = Markdown.Render("| Assembly | Holds | # |\n|---|---|---|\n| `Contoso.WeatherService.Runtime` | scene parts, shaders | 1 |\n| `Contoso.WeatherService.Core` | logic without drawing | 2 |", _ => { }).Document.Blocks.OfType<Table>().Single();
        var stars = table.Columns.Select(c => c.Width).ToList();
        Check.True(stars.All(s => s.IsStar), "star widths");
        double sum = stars.Sum(s => s.Value);
        Check.True(stars[0].Value > stars[1].Value && stars[1].Value > stars[2].Value, $"the code column widest, the short one least (got {string.Join(", ", stars.Select(s => (s.Value / sum).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))})");
        Check.Near(stars[2].Value / sum, 0.14, 1e-6, "the short one at its 14 %");
        var header = table.RowGroups[0].Rows[0].Cells[0];
        Check.True(header.Background is not null && table.RowGroups[0].Rows[1].Cells[0].Background is null, "the header row on a tint");
    }

    public static void CodeCopiesAsWritten()
    {
        var answer = Markdown.Render("값은 `Contoso.WeatherService.Runtime`에 있어요 `a-b/c_d\\e`", _ => { });
        var span = ((Paragraph)answer.Document.Blocks.FirstBlock).Inlines.OfType<Span>().First(sp => new TextRange(sp.ContentStart, sp.ContentEnd).Text.Contains("Contoso", System.StringComparison.Ordinal));
        var runs = span.Inlines.OfType<Run>().ToList();
        Check.Eq(string.Concat(runs.Select(r => r.Text)), $"{Hangul.CodePad}Contoso.{Hangul.BreakHint}WeatherService.{Hangul.BreakHint}Runtime{Hangul.CodePad}", "drawn: padding that holds on, break chances after the dots");
        Check.True(runs[0].FontSize < runs[1].FontSize * 0.5 && runs[^1].FontSize == runs[0].FontSize, "the padding narrower than a space");
        answer.SelectAll();
        Check.Eq(answer.SelectedText, "값은 Contoso.WeatherService.Runtime에 있어요 a-b/c_d\\e", "copied as written: no space put round the code");
        Check.Eq(Hangul.Unjoin("가\u2060나 \u2007x.\u200By\u2007"), "가나 x.y", "Unjoin: joiners, break chances and padding out");
        Check.Eq(Hangul.Unjoin("plain"), "plain", "plain text as it is");
    }

    public static void ChecklistLinesWithJoinedText()
    {
        const string md = "# T\nan intro that\nwraps here\n\n- [ ] one\n  continues\n  and more\n- [x] two\n\n> quoted\n> text\n> - [ ] three\n";
        string text = md;
        var items = Items(MdParser.Parse(md, true, true)).ToList();
        Check.Eq(items.Count, 3, "a box per checklist item");
        foreach (var item in items)
        {
            var next = MarkdownText.Line(text, item.Line) is { } l ? MarkdownText.ToggleTask(text, item.Line, l) : null;
            Check.True(next is not null, "each item's line is a checklist line: " + item.Line);
            text = next!;
        }
        Check.Eq(text, "# T\nan intro that\nwraps here\n\n- [x] one\n  continues\n  and more\n- [ ] two\n\n> quoted\n> text\n> - [x] three\n", "each box ticks its own line");
    }

    /// The checklist items, quoted ones too, in order.
    internal static IEnumerable<MdBlock> Items(IEnumerable<MdBlock> blocks)
    {
        foreach (var b in blocks)
        {
            if (b.Done is not null) yield return b;
            foreach (var c in Items(b.Children)) yield return c;
        }
    }

    /// Spans as text: hard breaks as \n.
    internal static string Plain(IEnumerable<MdSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var s in spans) sb.Append(s.Kind == MdSpanKind.Break ? "\n" : s.Text);
        return sb.ToString();
    }
}
