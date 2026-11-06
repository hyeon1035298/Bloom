using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// Where the Markdown view ends lines (Core\LineBreaks.cs) and the lines a shaped text makes (Ui\MdText.cs): Korean kept
/// whole, Chinese and Japanese between characters with kinsoku, code and addresses after . / \ _ -, emoji sequences and
/// letters with marks never split, styles changing inside a word, tabs, no-break spaces, hard breaks.
internal static class LineBreakTests
{
    public static void ShortNamesKeepTheirWords()
    {
        const char J = '⁠'; // WORD JOINER
        Check.Eq(Hangul.KeepWords("クリップボード履歴"), $"ク{J}リ{J}ッ{J}プ{J}ボ{J}ー{J}ド履{J}歴", "katakana and kanji each held; a break where the script changes");
        Check.Eq(Hangul.KeepWords("클립보드 기록"), Hangul.KeepAll("클립보드 기록"), "Korean as KeepAll");
        Check.Eq(Hangul.KeepWords("Bloom Space"), "Bloom Space", "Latin as it is");
        Check.Eq(Hangul.Unjoin(Hangul.KeepWords("剪贴板历史")), "剪贴板历史", "copied without the joiners");
    }

    private static MdEnv Env() => new(14, true, 1.0);

    private static MdText Shaped(MdEnv env, params MdSpan[] spans)
    {
        var t = MdText.Of([.. spans], env, 14, FontWeights.Normal, Theme.P.Text, null);
        t.Shape(env);
        return t;
    }

    private static MdSpan T(string s, MdStyle style = MdStyle.None) => new(MdSpanKind.Text, s, style);

    public static void HangulBreaksOnlyAtSpaces()
    {
        const string s = "한국어 문장이 여러 줄로 이어집니다";
        var opps = LineBreaks.Opportunities(s);
        var spaces = Enumerable.Range(1, s.Length - 1).Where(k => s[k - 1] == ' ').ToArray();
        Check.Eq(string.Join(",", opps), string.Join(",", spaces), "only after spaces");
        var env = Env();
        var t = Shaped(env, T(s));
        foreach (double width in new[] { 80.0, 100, 120, 200 }) // the longest word (5 syllables) fits each
            foreach (int start in t.Break(width).Skip(1))
                Check.True(s[start - 1] == ' ' || start == 0, $"at {width} px a line starts at a word: {start}");
    }

    public static void CjkBreaksBetweenCharactersWithKinsoku()
    {
        const string s = "日本語のテキストです。（括弧）とカッコ";
        var opps = LineBreaks.Opportunities(s).ToHashSet();
        Check.True(opps.Contains(1) && opps.Contains(2), "between ideographs");
        Check.True(!opps.Contains(s.IndexOf('。')), "not before 。");
        Check.True(opps.Contains(s.IndexOf('。') + 1), "after 。, before an opening mark");
        Check.True(!opps.Contains(s.IndexOf('（') + 1), "not after （");
        Check.True(!opps.Contains(s.IndexOf('）')), "not before ）");
        Check.True(!opps.Contains(s.IndexOf('ッ')), "not before a small kana");
        Check.True(LineBreaks.Opportunities("ラーメン").SequenceEqual([2, 3]), "not before the long vowel mark");
    }

    public static void CodeAndAddressesBreakAfterMarks()
    {
        var env = Env();
        var code = Shaped(env, T("see "), T("Contoso.WeatherService.Runtime", MdStyle.Code));
        string text = code.Text;
        Check.True(text[4] == Hangul.CodePad && text[^1] == Hangul.CodePad, "code with its padding as one character each side");
        var inCode = code.Opportunities.Where(k => k > 5).ToArray();
        Check.Eq(string.Join(",", inCode.Select(k => text[k - 1])), ".,.", "in code: after the dots only");
        Check.True(!code.Opportunities.Contains(5) && code.Opportunities.Contains(4), "the padding holds on to the code; the space before it breaks");
        var lines = code.Break(120);
        Check.True(lines.Length > 1 && lines.Skip(1).All(k => text[k - 1] is '.' or ' '), "a narrow line breaks code after its dots");

        string url = "https://example.com/" + new string('a', 20) + "/" + new string('b', 30) + "/c";
        var link = Shaped(env, new MdSpan(MdSpanKind.Text, MdParser.Autolink(url), MdStyle.None, new MdLink(url), url));
        Check.True(link.Opportunities.Any(k => link.Text[k - 1] == '/'), "an address breaks after a slash");
        Check.Eq(link.Copy(0, link.Text.Length), url, "and copies whole");
    }

    public static void CodeBreaksBeforeOpeningBrackets()
    {
        var env = Env();
        var t = Shaped(env, T("Shader.Find(\"Hidden/Blit\") a[0] f{x} ((y)) .(z)", MdStyle.Code));
        string text = t.Text;
        var before = t.Opportunities.Where(k => text[k] is '(' or '[' or '{' && text[k - 1] != ' ').Select(k => text[(k - 1)..(k + 1)]).ToArray();
        Check.Eq(string.Join(" ", before), "d( a[ f{", "before a bracket that follows a letter or digit only");
        Check.True(!LineBreaks.Opportunities("Find(x)").Contains(4), "not in prose");
        // a table cell's width: "Shader.Find" / "(\"Hidden/Blit\")", as FlowDocument wrapped it
        var cell = Shaped(env, T("Shader.Find(\"Hidden/Blit\")", MdStyle.Code));
        int open = cell.Text.IndexOf('(');
        var starts = cell.Break(cell.X[cell.Text.Length] - cell.X[open] + 1);
        Check.Eq(string.Join(",", starts), $"0,{open}", "two lines, the second from the bracket");
    }

    public static void ClustersStayWhole()
    {
        var env = Env();
        const string family = "👩‍👩‍👧‍👦";
        var emoji = Shaped(env, T(family + family + family));
        var starts = emoji.Break(5);
        Check.Eq(string.Join(",", starts), $"0,{family.Length},{2 * family.Length}", "an emoji sequence is never split");
        var marks = Shaped(env, T("e\u0301e\u0301e\u0301e\u0301"));
        Check.True(marks.Break(3).All(k => k % 2 == 0), "a letter keeps its combining mark");
        Check.True(LineBreaks.Opportunities("a\u0301 b").SequenceEqual([3]), "and a break after a space only");
    }

    public static void StyleChangeInsideAWord()
    {
        var env = Env();
        var t = Shaped(env, T("bo", MdStyle.Bold), T("ld words"));
        Check.True(!t.Opportunities.Contains(2), "no break where the style changes");
        Check.True(t.X[2] > t.X[0] && t.X[3] > t.X[2], "x goes on over the change");
        for (int i = 0; i < t.Text.Length; i++) Check.True(t.X[i + 1] >= t.X[i], "x never goes back");
    }

    public static void TabsAndNoBreakSpaces()
    {
        var env = Env();
        var tab = Shaped(env, T("a\tb"));
        double space = env.Measure(" ", tab.LookAt(0));
        Check.Near(tab.X[2] - tab.X[1], 4 * space, 0.01, "a tab: four spaces");
        Check.True(tab.Opportunities.Contains(2), "a line may end after a tab");
        Check.True(LineBreaks.Opportunities("a\u00A0b c").SequenceEqual([4]), "not at a no-break space, after a space");
    }

    public static void HardBreaksEndLines()
    {
        var env = Env();
        var t = Shaped(env, T("one"), MdSpan.Break, T("two"));
        Check.Eq(string.Join(",", t.Break(1000)), "0,4", "a hard break starts a line");
        Check.Eq(t.Copy(0, t.Text.Length), "one\r\ntwo", "copied as a line break");
        Check.Eq(t.X[4], 0f, "the next segment starts at 0");
    }

    /// A line is its pieces' drawing clipped to it: no ink of a character on the line before or after may show (the
    /// foot of an "A" starting the next line), and none of its own may go (an italic's overhang), at any DPI or size, in
    /// prose, bold, italic, code and Japanese — against the line formatted on its own.
    public static void LinesShowOnlyTheirOwnInk()
    {
        int beforeA = 0;
        foreach (double ppd in new[] { 1.0, 1.25, 1.5, 2.0 })
            foreach (double size in new[] { 13.0, 14, 18 }) // 13: a table cell's at the default text size
            {
                var env = new MdEnv(size, true, ppd);
                var t = MdText.Of([T("수동 여부 Northwind이 API는없음 Wave Away Java "), T("Tokyo Value ", MdStyle.Italic), T("Year AWAY ", MdStyle.Bold), T("fly "), T("Shader.Find(\"Hidden/Blit\")", MdStyle.Code), T(" 日本語のテキストです。Vault")], env, size, FontWeights.Normal, System.Windows.Media.Brushes.White, null);
                t.Shape(env);
                for (double width = 40; width <= 400; width += 23)
                {
                    var starts = t.Break(width);
                    for (int k = 0; k < starts.Length; k++)
                    {
                        int s = starts[k], e = t.LineEnd(starts, k);
                        if (t.Text.AsSpan(e).StartsWith("API")) beforeA++;
                        // a table cell's text starts between pixels (drawn on the pixels it was laid out for)
                        for (double frac = 0; frac < 1; frac += 0.125)
                        {
                            var drawn = Ink(t, env, s, e, false, ppd, frac);
                            var own = Ink(t, env, s, e, true, ppd, frac);
                            string? leak = Leak(drawn, own);
                            Check.True(leak is null, $"{ppd} ppd, {size} px, width {width}, +{frac}: line \"{t.Text[s..e]}\" shows ink not its own at {leak}");
                            string? cut = Leak(own, drawn); // and all of its own, the italic's overhang too
                            Check.True(cut is null, $"{ppd} ppd, {size} px, width {width}, +{frac}: line \"{t.Text[s..e]}\" loses its own ink at {cut}");
                        }
                    }
                }
            }
        Check.True(beforeA > 0, "some line ends just before \"API\"");
    }

    private const double InkMargin = 24; // room each side for overhang (and any leak) to land in

    /// The line's alpha at `ppd`, its first character at InkMargin.
    private static (byte[] A, int W, int H) Ink(MdText t, MdEnv env, int s, int e, bool alone, double ppd, double frac)
    {
        double wide = t.X[e] - t.X[s] + InkMargin * 2 + 40, high = env.M.Size * 3;
        int w = (int)Math.Ceiling(wide * ppd), h = (int)Math.Ceiling(high * ppd);
        var v = new System.Windows.Media.DrawingVisual();
        using (var dc = v.RenderOpen()) t.DrawLine(dc, env, s, e, InkMargin + frac / ppd, env.M.Size * 0.5, env.M.CellLine, env.M.CellLine, alone);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96 * ppd, 96 * ppd, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(v);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        var a = new byte[w * h];
        for (int i = 0; i < a.Length; i++) a[i] = px[i * 4 + 3];
        return (a, w, h);
    }

    /// Where `drawn` has clear ink that `own` lacks within a pixel each way (shaping alone may move a glyph a fraction
    /// of a pixel), or null.
    private static string? Leak((byte[] A, int W, int H) drawn, (byte[] A, int W, int H) own)
    {
        for (int y = 0; y < drawn.H; y++)
            for (int x = 0; x < drawn.W; x++)
            {
                if (drawn.A[y * drawn.W + x] < 24) continue;
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        near = yy >= 0 && yy < own.H && xx >= 0 && xx < own.W && own.A[yy * own.W + xx] > 0;
                    }
                if (!near) return $"({x}, {y}) px";
            }
        return null;
    }

    public static void LinesFitAndMatchTheirShaping()
    {
        var env = Env();
        var t = Shaped(env, T("Bloom의 보기 화면은 표가 많은 문서도 멈추지 않고 그립니다. Each line is broken by arithmetic over the shaped positions, "), T("Contoso.Weather", MdStyle.Code), T(" 그리고 日本語の文も"), T(" 섞여 있습니다.", MdStyle.Bold));
        foreach (double width in new[] { 60.0, 90, 140, 220, 330, 500 })
        {
            var starts = t.Break(width);
            for (int k = 0; k < starts.Length; k++)
            {
                int s = starts[k], e = t.LineEnd(starts, k);
                double visible = t.X[LineBreaks.VisibleEnd(t.Text, s, e)] - t.X[s];
                bool forced = !t.Opportunities.Any(o => o > s && o < e) && visible > width;
                Check.True(visible <= width + 0.5 || forced, $"at {width}: line {k} is {visible:0.0} wide");
                double alone = t.FormattedWidth(env, s, e), shaped = t.X[e] - t.X[s];
                Check.True(Math.Abs(alone - shaped) <= 0.5, $"at {width}: line {k} formatted alone {alone:0.00} vs shaped {shaped:0.00}");
            }
        }
    }
}
