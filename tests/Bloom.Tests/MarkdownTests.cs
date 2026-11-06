using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// Checklists and pictures: as the model reads them, as chat draws them (Ui\Markdown.cs) and as the panes' view draws
/// them (Ui\MdView.cs), and the pane that ticks a note's boxes (Ui\MdPane.cs).
internal static class MarkdownTests
{
    private const string Tasks = "# 장보기\n- [ ] 우유\n- [x] 계란\n\n> 1. [ ] 인용 안\n> - [X] 둘째\n\n```\n- [ ] 코드 안\n```\n* [ ] 별표\n+ [ ] 더하기";

    public static void ChecklistLines()
    {
        var items = DocMarkdownTests.Items(MdParser.Parse(Tasks, false, true)).ToList();
        Check.Eq(items.Count, 6, "a box per checklist item, none in code");
        Check.Eq(string.Join(",", items.Select(i => i.Line)), "1,2,4,5,10,11", "each box has its own source line, in a quote too");

        var chat = Markdown.Render(Tasks, _ => { }, 14);
        var chatBoxes = Boxes(chat.Document.Blocks).ToList();
        Check.Eq(chatBoxes.Count, 6, "chat draws the boxes too");
        Check.True(chatBoxes.All(b => b.Cursor is null), "but they take no clicks there");

        // code fenced under a list item is code, as MarkdownText.ToggleTask reads it: every box drawn takes its click
        string inList = "- a\n  ```\n  - [ ] in code\n  ```\n- [ ] after\n- [x]done";
        var listItems = DocMarkdownTests.Items(MdParser.Parse(inList, false, true)).ToList();
        Check.Eq(listItems.Count, 1, "no box in the code, none for \"[x]done\"");
        string text = inList;
        Check.True(MarkdownText.Line(text, listItems[0].Line) is { } l && MarkdownText.ToggleTask(text, listItems[0].Line, l) is { } next && (text = next) is not null, "the one drawn ticks");
        Check.Eq(text, "- a\n  ```\n  - [ ] in code\n  ```\n- [x] after\n- [x]done", "its own line");
    }

    public static void ChatLooksTheSame()
    {
        var chat = Markdown.Render("```\ncode\n```\n\n`inline` ![alt](https://example.com/a.png)", _ => { });
        var code = (TextBlock)((Border)((Grid)((SmoothBorder)((BlockUIContainer)chat.Document.Blocks.FirstBlock).Child).Child).Children[0]).Child;
        Check.Eq(code.FontSize, 12.5, "code at 12.5 in a chat answer");
        var para = (Paragraph)chat.Document.Blocks.LastBlock;
        Check.Eq(para.Inlines.OfType<Span>().First(sp => new TextRange(sp.ContentStart, sp.ContentEnd).Text.Contains("inline", System.StringComparison.Ordinal)).FontSize, 12.5, "inline code too");
        Check.True(para.Inlines.OfType<Hyperlink>().Any() && !para.Inlines.OfType<InlineUIContainer>().Any(), "a picture in chat stays \"!\" and a link");
        var big = Markdown.Render("```\ncode\n```", _ => { }, 28);
        var bigCode = (TextBlock)((Border)((Grid)((SmoothBorder)((BlockUIContainer)big.Document.Blocks.FirstBlock).Child).Child).Children[0]).Child;
        Check.Eq(bigCode.FontSize, 25.0, "code follows the size");
        var styled = (Paragraph)Markdown.Render("**굵게 _기울임_** ~~줄~~ [링크](https://a.b)", _ => { }).Document.Blocks.FirstBlock;
        var runs = styled.Inlines.OfType<Run>().ToList();
        Check.True(runs[0].FontWeight == FontWeights.Bold && runs[1].FontStyle == FontStyles.Italic && runs[1].FontWeight == FontWeights.Bold, "bold, and italic in it");
        Check.True(runs.Any(r => r.TextDecorations == TextDecorations.Strikethrough), "strike");
        Check.True(styled.Inlines.OfType<Hyperlink>().Single().Inlines.OfType<Run>().Single().Text == Hangul.KeepAll("링크"), "a link's text in its Hyperlink");
    }

    public static void Pictures()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-md-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var view = new MdView();
        var w = MdViewTests.Hosted(view, 300);
        try
        {
            string file = Path.Combine(dir, "wide.png");
            var pixels = new byte[3000 * 4 * 4];
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3000, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 3000 * 4)));
            using (var s = File.Create(file)) encoder.Save(s);

            view.Show("![넓은 그림](wide.png)\n\n![웹](https://example.com/a.png)\n\n![없는 그림](missing.png)\n\n- ![절대 경로](" + file + ")\n\n![x](" + @"\\bloom-test.invalid\share\a.png" + ") ![y](file://bloom-test.invalid/s/b.png)", 14, false, dir);
            var surface = view.Surface;
            MdViewTests.Pump(() =>
            {
                view.UpdateLayout();
                return surface.Boxes.All(b => b.Shaped) && surface.Boxes[0].Parts[0].Text.Pictures is [(_, { Bitmap: not null })] && surface.Boxes[2].Parts[0].Text.Pictures.Length == 0
                    && surface.Boxes[3].Parts[0].Text.Pictures is [(_, { Bitmap: not null })];
            }, "the pictures read");
            var wide = surface.Boxes[0].Parts[0].Text.Pictures[0].Picture;
            double column = surface.Boxes[0].Width;
            Check.True(wide.Width is > 0 && wide.Width <= column + 0.01, $"a wide picture fits the width (got {wide.Width:0.#} in {column:0.#})");
            double ppd = VisualTreeHelper.GetDpi(view).PixelsPerDip;
            Check.True(wide.Bitmap!.Width <= System.Math.Ceiling(column * ppd / MdPictures.WidthStep) * MdPictures.WidthStep && wide.Bitmap.Source.PixelWidth <= MdPictures.PictureWidth, $"decoded for its drawn width, not its 3000 pixels (got {wide.Bitmap.Width})");
            var inList = surface.Boxes[3].Parts[0];
            Check.True(inList.Text.Pictures[0].Picture.Width <= column - inList.X + 0.01 && inList.X >= 16, "and the room beside a list marker");
            Check.True(surface.Boxes[0].Height >= wide.Height, "the line as tall as the picture");
            File.Delete(file);
            Check.True(!File.Exists(file), "and the file isn't held open");
            Check.True(surface.Boxes[1].Parts[0].Text is { Text: "웹", Links.Length: 1 }, "a picture on the web is a link with its alt text");
            Check.Eq(surface.Boxes[2].Parts[0].Text.Text, "없는 그림", "a missing one is its alt text");
            Check.Eq(surface.Boxes[4].Parts[0].Text.Text, "x y", "network pictures: alt text, never looked for");

            view.Show("![x](wide.png)", 14, false, null);
            view.UpdateLayout();
            Check.Eq(view.Surface.Boxes[0].Parts[0].Text.Text, "x", "a relative path with no folder: its alt text");
        }
        finally
        {
            w.Close();
            Directory.Delete(dir, true);
        }
    }

    public static void PaneTicksTheText()
    {
        int was = Settings.Current.MdTextSize;
        try
        {
            var pane = new MdPane("# 할 일\r\n- [ ] 우유\r\n- [x] 계란\r\n", true, null, false, false);
            string? edited = null;
            pane.Edited += t => edited = t;
            var view = (MdView)pane.KeyTarget;
            var shown = view.Surface.Boxes;
            Check.True(view.Surface.TickForQa(0), "the first box takes the click");
            Check.Eq(edited, "# 할 일\r\n- [x] 우유\r\n- [x] 계란\r\n", "a click ticks the box in the text, CRLF kept, and hands it over");
            Check.Eq(pane.Text, edited, "the pane holds the new text");
            Check.True(ReferenceEquals(view.Surface.Boxes, shown), "without drawing the view again");
            Check.True(shown[1].Block.Done == true, "the box shows ticked");
            view.Surface.TickForQa(0);
            Check.Eq(edited, "# 할 일\r\n- [ ] 우유\r\n- [x] 계란\r\n", "a second click clears it again");

            pane.Text = "# 할 일\r\n- [x] 두유\r\n";
            Check.True(!ReferenceEquals(view.Surface.Boxes, shown), "a text from outside is drawn again");
            Check.Eq(edited, "# 할 일\r\n- [ ] 우유\r\n- [x] 계란\r\n", "and isn't handed back as an edit");

            var readOnly = new MdPane("- [ ] a", true, null, true, false);
            Check.True(!((MdView)readOnly.KeyTarget).Surface.TickForQa(0) && readOnly.Text == "- [ ] a", "a read-only pane's boxes take no clicks");

            pane.SetTextSize(40);
            Check.Eq(Settings.Current.MdTextSize, Settings.MdTextMax, "the size stops at its largest");
            pane.SetTextSize(3);
            Check.Eq(MdPane.TextSize, Settings.MdTextMin, "and its smallest");
            pane.InView = false;
            Check.True(pane.KeyTarget is TextBox, "편집: the editor");
        }
        finally
        {
            Settings.Current.MdTextSize = was;
        }
    }

    /// The checklist boxes' spots in a chat answer, in document order.
    private static IEnumerable<FrameworkElement> Boxes(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Section s)
                foreach (var b in Boxes(s.Blocks)) yield return b;
            else if (block is Paragraph p && p.Inlines.FirstInline is InlineUIContainer { Child: Grid { Children: [SmoothBorder, ..] } spot })
                yield return spot;
        }
    }
}
