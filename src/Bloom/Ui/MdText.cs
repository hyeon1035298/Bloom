using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Bloom.Core;

namespace Bloom.Ui;

/// How a run of the view's text is drawn (TextFormatter's run properties): one per look, kept by MdEnv.
internal sealed class MdRunProps : TextRunProperties
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US"); // a FlowDocument's own (xml:lang en-US)

    public MdRunProps(Typeface face, double size, Brush brush, TextDecorationCollection? decorations, double pixelsPerDip)
    {
        Typeface = face;
        FontRenderingEmSize = size;
        ForegroundBrush = brush;
        TextDecorations = decorations;
        PixelsPerDip = pixelsPerDip;
    }

    public override Typeface Typeface { get; }
    public override double FontRenderingEmSize { get; }
    public override double FontHintingEmSize => FontRenderingEmSize;
    public override TextDecorationCollection? TextDecorations { get; }
    public override Brush ForegroundBrush { get; }
    public override Brush? BackgroundBrush => null;
    public override CultureInfo CultureInfo => Culture;
    public override TextEffectCollection? TextEffects => null;
}

/// A line of the view's text: never wrapped by TextFormatter (MdView breaks lines itself), left to right, `lineHeight` high.
internal sealed class MdParaProps(TextRunProperties props, double lineHeight) : TextParagraphProperties
{
    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
    public override TextAlignment TextAlignment => TextAlignment.Left;
    public override double LineHeight => lineHeight;
    public override bool FirstLineInParagraph => true;
    public override TextRunProperties DefaultTextRunProperties => props;
    public override TextWrapping TextWrapping => TextWrapping.NoWrap;
    public override TextMarkerProperties? TextMarkerProperties => null;
    public override double Indent => 0;
}

/// Something drawn in a line rather than shaped (a picture, inline code's padding, a tab): only its width counts here;
/// MdText draws pictures itself.
internal sealed class MdObject(double width, TextRunProperties props) : TextEmbeddedObject
{
    public override LineBreakCondition BreakBefore => LineBreakCondition.BreakPossible;
    public override LineBreakCondition BreakAfter => LineBreakCondition.BreakPossible;
    public override bool HasFixedSize => true;
    public override CharacterBufferReference CharacterBufferReference => new(LineBreaks.ObjectChar.ToString(), 0);
    public override int Length => 1;
    public override TextRunProperties Properties => props;
    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(width, 0, 0);
    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, 0, width, 0);
    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
    {
    }
}

/// A picture in a text: where it comes from and, once its file's header is read (MdPictures), its size.
internal sealed class MdPicture(string path, string alt)
{
    public readonly string Path = path, Alt = alt;
    public DateTime Written;
    public int PixelWidth, PixelHeight, Decoding;
    public double DipWidth, DipHeight;
    public bool Ready, Missing, Asked;
    public BitmapRef? Bitmap; // decoded for the width it's drawn at
    public double Width, Height; // as laid out (fitted to the line)
}

/// A decoded picture at a pixel width (shared through MdPictures' cache).
internal sealed class BitmapRef(System.Windows.Media.Imaging.BitmapSource source, int width)
{
    public readonly System.Windows.Media.Imaging.BitmapSource Source = source;
    public readonly int Width = width;
}

/// The view's shared settings and tools at one text size and DPI: the TextFormatter (Display mode, as every window
/// draws), the metrics, and the run looks.
internal sealed class MdEnv
{
    public readonly TextFormatter Formatter = TextFormatter.Create(TextFormattingMode.Display);
    public readonly MdMetrics M;
    public readonly double Ppd;
    private readonly Dictionary<(FontFamily, FontWeight, FontStyle, double, Brush, bool), MdRunProps> props = [];
    private readonly Dictionary<MdRunProps, double> spaces = [];
    private double pad = -1;

    public MdEnv(double size, bool document, double pixelsPerDip)
    {
        M = new MdMetrics(size, document);
        Ppd = pixelsPerDip;
    }

    public MdRunProps Props(FontFamily family, FontWeight weight, FontStyle style, double size, Brush brush, bool strike = false)
    {
        var key = (family, weight, style, size, brush, strike);
        if (!props.TryGetValue(key, out var p))
            props[key] = p = new MdRunProps(new Typeface(family, style, weight, FontStretches.Normal), size, brush, strike ? TextDecorations.Strikethrough : null, Ppd);
        return p;
    }

    /// Inline code's padding each side: a figure space of the code's font (chat's is narrower, Markdown.CodePadScale; here
    /// the room keeps a line's ink off its neighbour's).
    public double Pad
    {
        get
        {
            if (pad < 0) pad = Measure("0", Props(Theme.Mono, FontWeights.Normal, FontStyles.Normal, M.CodeSize, Theme.P.Text));
            return pad;
        }
    }

    /// A tab: four spaces of its run's font.
    public double Tab(MdRunProps p)
    {
        if (!spaces.TryGetValue(p, out double w)) spaces[p] = w = Measure(" ", p);
        return 4 * w;
    }

    private readonly Dictionary<(MdRunProps, double), (double Baseline, double Height)> lines = [];

    /// Where the baseline of a line in look `p`, `lineHeight` high (0: the font's own), is below its top, and how high
    /// the line is.
    public (double Baseline, double Height) LineOf(MdRunProps p, double lineHeight)
    {
        if (!lines.TryGetValue((p, lineHeight), out var m))
        {
            var t = MdText.Plain("0", p);
            t.Shape(this);
            using var line = Formatter.FormatLine(MdText.Probe(t), 0, 1e6, new MdParaProps(p, lineHeight), null);
            lines[(p, lineHeight)] = m = (line.Baseline, line.Height);
        }
        return m;
    }

    /// A short text's width on one line.
    public double Measure(string text, MdRunProps p)
    {
        var t = new MdText(text, [0], [p], [], [], [], []);
        t.Shape(this);
        return t.Widest;
    }

    /// Drawn snapped to device pixels.
    public double Snap(double v) => Math.Round(v * Ppd) / Ppd;
}

/// One text of the view (a paragraph's, a heading's, a list item's, a table cell's, a code block's, a label's) in its
/// looks, shaped once on one unbroken line per segment (between hard breaks: '\n') — each character's x — and broken
/// into lines at any width by arithmetic (LineBreaks). Pictures, inline code's padding and tabs are in the text as one
/// character each (LineBreaks.ObjectChar, Hangul.CodePad, '\t') and drawn, not shaped. Very long segments are shaped in pieces
/// of about 4,000 characters cut where a line may break.
internal sealed class MdText
{
    private const int Chunk = 4000;

    public readonly string Text;
    private readonly int[] runStarts; // each run's start, the first 0
    private readonly MdRunProps[] runProps;
    public readonly (int Start, int End, MdLink Link)[] Links;
    public readonly (int Start, int End)[] Code; // inline code with its padding, or a code block's whole text
    public readonly (int Start, int End, string Copy)[] CopyAs; // what a span gives when copied whole (an autolink)
    public readonly (int At, MdPicture Picture)[] Pictures;
    private readonly bool codeTint;
    private readonly MdRunProps look; // the text's own look (its lines' baseline goes by it)
    private int[]? objectsAt; // where the text has an object character, in order
    private MdObject?[]? objects;

    /// A picture in it shows as its alt text because its file couldn't be read.
    public bool Unread { get; init; }

    public float[] X = [];
    public int[] Opportunities = [];
    private float[] cuts = []; // per opportunity: the ink's right end before it, the ink's left end after it (InkAtCuts)
    private bool[]? graphemes;
    public bool Shaped;

    /// The pieces it was shaped in (a segment, or ~4,000 characters of a long one), each drawn once on one line when
    /// first needed: a line on screen is its piece's drawing moved and clipped to it, so a new width formats nothing.
    private readonly List<(int From, int To)> pieces = [];
    private Drawing?[]? drawings;
    private double[]? baselines;
    private double drawnAt = -1; // the line height the drawings are for
    private Dictionary<(int, int, double), Drawing>? aloneDrawn; // lines formatted on their own (DrawLine)

    /// The widest segment (a table column's natural width adds these up).
    public double Widest;

    public MdText(string text, int[] runStarts, MdRunProps[] runProps, (int, int, MdLink)[] links, (int, int)[] code, (int, int, string)[] copyAs, (int, MdPicture)[] pictures, bool codeTint = false, MdRunProps? look = null)
    {
        Text = text;
        this.runStarts = runStarts;
        this.runProps = runProps;
        Links = links;
        Code = code;
        CopyAs = copyAs;
        Pictures = pictures;
        this.codeTint = codeTint;
        this.look = look ?? runProps[0];
    }

    /// The text of `spans` in a block's look: `size`, `weight` and `brush` for plain text; code in Mono at the metrics'
    /// code size with padding each side; links in the accent; pictures (MdPictures) or, for one on the web, a link with
    /// its alt text, and for one that can't be shown its alt text, faint.
    public static MdText Of(List<MdSpan> spans, MdEnv env, double size, FontWeight weight, Brush brush, string? baseDir)
    {
        var sb = new StringBuilder();
        var starts = new List<int>();
        var looks = new List<MdRunProps>();
        var links = new List<(int, int, MdLink)>();
        var code = new List<(int, int)>();
        var copy = new List<(int, int, string)>();
        var pictures = new List<(int, MdPicture)>();
        bool unread = false;
        void Look(MdRunProps p)
        {
            if (looks.Count > 0 && looks[^1] == p) return;
            if (starts.Count > 0 && starts[^1] == sb.Length)
            {
                looks[^1] = p;
                return;
            }
            starts.Add(sb.Length);
            looks.Add(p);
        }
        foreach (var s in spans)
        {
            var w = (s.Style & MdStyle.Bold) != 0 ? FontWeights.Bold : weight;
            var st = (s.Style & MdStyle.Italic) != 0 ? FontStyles.Italic : FontStyles.Normal;
            bool strike = (s.Style & MdStyle.Strike) != 0;
            int at = sb.Length;
            if (s.Kind == MdSpanKind.Break)
            {
                Look(env.Props(Theme.Sans, w, st, size, brush));
                sb.Append('\n');
                continue;
            }
            if (s.Kind == MdSpanKind.Picture)
            {
                string shown = s.Text.Length > 0 ? s.Text : s.Source ?? "";
                string src = s.Source ?? "";
                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // a picture on the web: a link with its alt text (nothing is fetched just by showing the text)
                    Look(env.Props(Theme.Sans, w, st, size, Theme.P.AccentText, strike));
                    sb.Append(shown);
                    links.Add((at, sb.Length, new MdLink(src)));
                }
                else
                {
                    string? path = DocLink.Picture(src, baseDir);
                    if (path is not null && MdPictures.Known(path) is not { Missing: true })
                    {
                        Look(env.Props(Theme.Sans, w, st, size, brush));
                        sb.Append(LineBreaks.ObjectChar);
                        var picture = new MdPicture(path, s.Text);
                        MdPictures.Fill(picture);
                        pictures.Add((at, picture));
                    }
                    else
                    {
                        unread |= path is not null; // a file that couldn't be read (MdPictures looks again on the next Show)
                        Look(env.Props(Theme.Sans, w, st, size, Theme.P.TextFaint, strike));
                        sb.Append(shown.Replace('\n', ' '));
                    }
                }
                continue;
            }
            bool isCode = (s.Style & MdStyle.Code) != 0;
            var look = isCode
                ? env.Props(Theme.Mono, w, st, env.M.CodeSize, s.Link is null ? brush : Theme.P.AccentText, strike)
                : env.Props(Theme.Sans, w, st, size, s.Link is null ? brush : Theme.P.AccentText, strike);
            Look(look);
            if (isCode) sb.Append(Hangul.CodePad);
            sb.Append(s.Text);
            if (isCode) sb.Append(Hangul.CodePad);
            if (isCode) code.Add((at, sb.Length));
            else if (s.Link is not null && (s.Copy is not null || s.Text == s.Link.Url)) code.Add((at, sb.Length)); // an address breaks as code
            if (s.Link is not null)
            {
                if (links.Count > 0 && links[^1].Item3 == s.Link && links[^1].Item2 == at) links[^1] = (links[^1].Item1, sb.Length, s.Link);
                else links.Add((at, sb.Length, s.Link));
            }
            if (s.Copy is not null) copy.Add((at, sb.Length, s.Copy));
        }
        var own = env.Props(Theme.Sans, weight, FontStyles.Normal, size, brush);
        if (starts.Count == 0)
        {
            starts.Add(0);
            looks.Add(own);
        }
        return new MdText(sb.ToString(), [.. starts], [.. looks], [.. links], [.. code], [.. copy], [.. pictures], codeTint: true, look: own) { Unread = unread };
    }

    /// A code block's text: one look, lines at '\n', all of it breaking as code.
    public static MdText OfCode(string code, MdRunProps look) => new(code, [0], [look], [], [(0, code.Length)], [], []);

    /// A one-look text (a list marker, a label).
    public static MdText Plain(string text, MdRunProps look) => new(text, [0], [look], [], [], [], []);

    public MdRunProps LookAt(int i)
    {
        int k = Array.BinarySearch(runStarts, i);
        if (k < 0) k = ~k - 1;
        return runProps[Math.Max(k, 0)];
    }

    private int RunEnd(int i)
    {
        int k = Array.BinarySearch(runStarts, i);
        if (k < 0) k = ~k - 1;
        return k + 1 < runStarts.Length ? runStarts[k + 1] : Text.Length;
    }

    public MdPicture? PictureAt(int i)
    {
        foreach (var (at, p) in Pictures)
            if (at == i) return p;
        return null;
    }

    /// The link the character at `i` is in.
    public MdLink? LinkAt(int i)
    {
        foreach (var (s, e, l) in Links)
            if (i >= s && i < e) return l;
        return null;
    }

    private void Objects(MdEnv env)
    {
        var at = new List<int>();
        for (int i = 0; i < Text.Length; i++)
            if (Text[i] is LineBreaks.ObjectChar or Hangul.CodePad or '\t') at.Add(i);
        objectsAt = [.. at];
        objects = new MdObject?[at.Count];
        for (int k = 0; k < at.Count; k++)
        {
            int i = at[k];
            var look = LookAt(i);
            double w = Text[i] switch
            {
                Hangul.CodePad => env.Pad,
                '\t' => env.Tab(look),
                _ => PictureAt(i) is { } p ? p.Width : 0,
            };
            objects[k] = new MdObject(w, look);
        }
    }

    private MdObject? ObjectAt(int i)
    {
        if (objectsAt is null || objects is null) return null;
        int k = Array.BinarySearch(objectsAt, i);
        return k >= 0 ? objects[k] : null;
    }

    private int NextObject(int i)
    {
        if (objectsAt is null) return int.MaxValue;
        int k = Array.BinarySearch(objectsAt, i);
        if (k < 0) k = ~k;
        return k < objectsAt.Length ? objectsAt[k] : int.MaxValue;
    }

    /// The whole text as TextFormatter reads it (MdEnv's line measures).
    public static TextSource Probe(MdText t) => new Source(t) { End = t.Text.Length };

    /// TextFormatter's view of the text from a start up to End.
    private sealed class Source(MdText t) : TextSource
    {
        public int End;

        public override TextRun GetTextRun(int i)
        {
            if (i >= End) return new TextEndOfLine(1);
            if (t.ObjectAt(i) is { } o) return o;
            int end = Math.Min(Math.Min(End, t.RunEnd(i)), t.NextObject(i));
            return new TextCharacters(t.Text, i, end - i, t.LookAt(i));
        }

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(0, new CultureSpecificCharacterBufferRange(CultureInfo.InvariantCulture, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int i) => i;
    }

    /// Shaped (again: the size, the DPI or a picture's size changed): every character's x on its segment's one line,
    /// and where lines may break.
    public void Shape(MdEnv env)
    {
        Objects(env);
        Opportunities = LineBreaks.Opportunities(Text, Code);
        cuts = new float[Opportunities.Length * 2];
        Array.Fill(cuts, float.NaN);
        graphemes = null;
        pieces.Clear();
        Forget();
        int n = Text.Length;
        X = new float[n + 1];
        Widest = 0;
        var source = new Source(this);
        int seg = 0;
        while (seg <= n)
        {
            int segEnd = Text.IndexOf('\n', seg);
            if (segEnd < 0) segEnd = n;
            double x0 = 0;
            for (int from = seg; from < segEnd;)
            {
                int to = segEnd;
                if (to - from > Chunk)
                {
                    // cut at the last place a line may break before the chunk's end
                    int k = Array.BinarySearch(Opportunities, from + Chunk);
                    if (k < 0) k = ~k - 1;
                    to = k >= 0 && Opportunities[k] > from ? Opportunities[k] : from + Chunk;
                    while (to < segEnd && char.IsLowSurrogate(Text[to])) to++;
                }
                x0 = ShapePiece(env, source, from, to, x0);
                from = to;
            }
            X[segEnd] = (float)x0;
            Widest = Math.Max(Widest, x0);
            seg = segEnd + 1;
        }
        Split(env.Ppd);
        Shaped = true;
    }

    /// The pieces cut again: at each place inside a word a line may break where the two sides' ink comes within 3 device
    /// pixels (a "/" before a letter in code: no clip between them would hold, CutAt), so a line ending there ends a
    /// piece; and to about DrawPiece characters each, where a line may break (else between clusters), so a line on screen
    /// draws only the few it lies in, not all of a long paragraph clipped.
    private void Split(double ppd)
    {
        var shaped = pieces.ToArray();
        pieces.Clear();
        foreach (var (from, to) in shaped)
        {
            int j = Array.BinarySearch(Opportunities, from + 1);
            if (j < 0) j = ~j;
            for (int start = from; start < to;)
            {
                while (j < Opportunities.Length && Opportunities[j] < to && !Tight(j, ppd)) j++;
                int end = j < Opportunities.Length && Opportunities[j] < to ? Opportunities[j++] : to;
                BySize(start, end);
                start = end;
            }
        }
    }

    private bool Tight(int j, double ppd)
    {
        int k = Opportunities[j];
        float r = cuts[2 * j], l = cuts[2 * j + 1];
        return !char.IsWhiteSpace(Text[k - 1]) && !float.IsNaN(r) && !float.IsNaN(l) && (l - r) * ppd < 3;
    }

    private void BySize(int start, int to)
    {
        const int DrawPiece = 120;
        while (to - start > DrawPiece * 3 / 2)
        {
            int k = Array.BinarySearch(Opportunities, start + DrawPiece);
            if (k < 0) k = ~k - 1;
            int cut = k >= 0 && Opportunities[k] > start + DrawPiece / 2 ? Opportunities[k] : start + DrawPiece;
            graphemes ??= LineBreaks.Graphemes(Text);
            while (cut < to && !graphemes[cut]) cut++;
            if (cut >= to) break;
            pieces.Add((start, cut));
            start = cut;
        }
        pieces.Add((start, to));
    }

    /// Where the ink ends before and starts after each place inside a word a line may break at (Chinese, Japanese,
    /// code, dashes), from the glyphs' bearings in this run (`start` to `end`): DrawLine clips a piece between them.
    /// Left unknown (NaN, the line is formatted alone) where one glyph spans the place (a ligature).
    private void InkAtCuts(GlyphRun g, double[] gx, IList<ushort>? map, int start, int end)
    {
        var face = g.GlyphTypeface;
        var ids = g.GlyphIndices;
        double em = g.FontRenderingEmSize;
        int count = ids.Count;
        int i = Array.BinarySearch(Opportunities, start);
        if (i < 0) i = ~i;
        for (; i < Opportunities.Length && Opportunities[i] <= end; i++)
        {
            int k = Opportunities[i];
            if (k > start && !char.IsWhiteSpace(Text[k - 1]))
            {
                // the cluster before k: its glyphs' right ink
                int g0 = GlyphOf(map, k - 1 - start, count), g1 = k < end ? GlyphOf(map, k - start, count) : count;
                if (g1 <= g0) continue;
                double right = double.NegativeInfinity;
                for (int j = g0; j < g1; j++) right = Math.Max(right, gx[j] + (face.AdvanceWidths[ids[j]] - face.RightSideBearings[ids[j]]) * em);
                cuts[2 * i] = (float)right;
            }
            if (k < end)
            {
                // the cluster from k: its glyph's left ink, and its marks' (no advance of their own)
                int j = GlyphOf(map, k - start, count);
                double left = double.PositiveInfinity;
                for (int m = j; m < count && (m == j || gx[m + 1] == gx[m]); m++) left = Math.Min(left, gx[m] + face.LeftSideBearings[ids[m]] * em);
                if (j < count) cuts[2 * i + 1] = (float)left;
            }
        }
    }

    private static int GlyphOf(IList<ushort>? map, int c, int count) => map is null ? Math.Min(c, count) : map[c];

    /// A clip edge for a line that starts (`keepAfter`) or ends at the break `k` inside a word: on a device pixel at
    /// least a pixel clear of the ink on both sides (InkAtCuts; a glyph drawn in Display mode sits on its pixel), as near the other line's ink as that allows so the
    /// line's own overhang shows; NaN where the two come closer (kerning, overhang). `shift` takes X to the drawing's x.
    private double CutAt(int k, double shift, double ppd, bool keepAfter)
    {
        int i = Array.BinarySearch(Opportunities, k);
        if (i < 0) return double.NaN; // a word longer than the line, broken between clusters
        float r = cuts[2 * i], l = cuts[2 * i + 1];
        if (float.IsNaN(r) || float.IsNaN(l)) return double.NaN;
        double a = Math.Ceiling((r + shift) * ppd + 1), b = Math.Floor((l + shift) * ppd - 1);
        return a > b ? double.NaN : (keepAfter ? a : b) / ppd;
    }

    /// One unbroken line from `from` to `to`: each character's x from its glyphs' advances (a cluster's characters share
    /// its x), objects by their widths. Returns the x at `to`.
    private double ShapePiece(MdEnv env, Source source, int from, int to, double x0)
    {
        source.End = to;
        var props = new MdParaProps(look, 0);
        int at = from;
        while (at < to)
        {
            using var line = env.Formatter.FormatLine(source, at, 1e6, props, null);
            double x = x0;
            int covered = at;
            foreach (var run in line.GetIndexedGlyphRuns())
            {
                int start = run.TextSourceCharacterIndex, len = run.TextSourceLength;
                for (; covered < start && covered < to; covered++)
                {
                    X[covered] = (float)x; // an object before this run
                    x += ObjectAt(covered) is { } o ? o.Format(0).Width : 0;
                }
                var g = run.GlyphRun;
                var advances = g.AdvanceWidths;
                var map = g.ClusterMap;
                int count = advances.Count;
                var gx = new double[count + 1]; // each glyph's left edge
                gx[0] = x;
                for (int k = 0; k < count; k++) gx[k + 1] = gx[k] + advances[k];
                for (int c = 0; c < len && start + c < to; c++) X[start + c] = (float)gx[map is null ? Math.Min(c, count) : map[c]];
                InkAtCuts(g, gx, map, start, Math.Min(start + len, to));
                x = gx[count];
                covered = Math.Max(covered, start + len);
            }
            int done = at + line.Length - line.NewlineLength;
            for (; covered < to && covered < done; covered++)
            {
                X[covered] = (float)x;
                x += ObjectAt(covered) is { } o ? o.Format(0).Width : 0;
            }
            if (done > at) pieces.Add((at, Math.Min(done, to)));
            x0 = x;
            if (done <= at) break;
            at = done;
        }
        return x0;
    }

    /// The lines the text makes at `width`: the index each starts at.
    public int[] Break(double width)
    {
        var starts = new List<int>();
        int n = Text.Length;
        for (int seg = 0; seg <= n;)
        {
            int segEnd = Text.IndexOf('\n', seg);
            if (segEnd < 0) segEnd = n;
            LineBreaks.Break(Text, X, Opportunities, seg, segEnd, width, starts, ref graphemes);
            seg = segEnd + 1;
        }
        return [.. starts];
    }

    /// Where line `k` (of `starts`) ends: before the hard break that ends it, or at the next line's start.
    public int LineEnd(int[] starts, int k) => k + 1 < starts.Length ? (Text[starts[k + 1] - 1] == '\n' ? starts[k + 1] - 1 : starts[k + 1]) : Text.Length;

    /// The nearest place between grapheme clusters to `x` on the line from `s` to `e` (x from the line's start).
    public int Hit(int s, int e, double x)
    {
        graphemes ??= LineBreaks.Graphemes(Text);
        double baseX = X[s];
        int best = s;
        double bestD = double.MaxValue;
        int lo = s, hi = e;
        // the first place right of x, then the nearer of it and the one before
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (X[mid] - baseX < x) lo = mid + 1;
            else hi = mid;
        }
        for (int k = Math.Max(s, lo - 3); k <= Math.Min(e, lo + 3); k++)
        {
            if (!graphemes[k]) continue;
            double d = Math.Abs(X[k] - baseX - x);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        if (best == e && e > s && LineBreaks.IsSpace(Text[e - 1]) && e < Text.Length && Text[e] != '\n') best = LineBreaks.VisibleEnd(Text, s, e); // not past a line's hanging spaces
        return best;
    }

    /// The text from `from` to `to` as copied: hard breaks as line breaks, code's padding as the space it was, a picture
    /// as nothing, an autolink taken whole as its whole address.
    public string Copy(int from, int to)
    {
        var sb = new StringBuilder(to - from + 8);
        for (int i = from; i < to; i++)
        {
            bool whole = false;
            foreach (var (s, e, c) in CopyAs)
                if (s == i && e <= to)
                {
                    sb.Append(c);
                    i = e - 1;
                    whole = true;
                    break;
                }
            if (whole) continue;
            char ch = Text[i];
            if (ch == '\n') sb.Append("\r\n");
            else if (ch == Hangul.CodePad) sb.Append(' ');
            else if (ch != LineBreaks.ObjectChar) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// The word around `i` (a double-click): letters and digits (Korean to the spaces), or a run of spaces, or one mark.
    public (int, int) WordAt(int i)
    {
        int n = Text.Length;
        if (n == 0) return (0, 0);
        i = Math.Clamp(i, 0, n - 1);
        static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_' || char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Surrogate;
        char at = Text[i];
        Func<char, bool> same = Word(at) ? Word : LineBreaks.IsSpace(at) ? LineBreaks.IsSpace : _ => false;
        int s = i, e = i + 1;
        while (s > 0 && same(Text[s - 1])) s--;
        while (e < n && same(Text[e])) e++;
        return (s, e);
    }

    /// The line from `s` to `e` as TextFormatter lays it out on its own (tests: against the shaped x).
    internal double FormattedWidth(MdEnv env, int s, int e)
    {
        using var line = env.Formatter.FormatLine(new Source(this) { End = e }, s, 1e6, new MdParaProps(look, 0), null);
        return line.WidthIncludingTrailingWhitespace;
    }

    /// Its pieces as drawn let go (the text went off screen).
    public void Forget()
    {
        drawings = null;
        baselines = null;
        aloneDrawn = null;
    }

    /// Piece `k` drawn once, on one line `lineHeight` high with its top at 0 and its first character at 0.
    private (Drawing Drawing, double Baseline) Piece(MdEnv env, int k, double lineHeight)
    {
        if (drawings is null || baselines is null || drawnAt != lineHeight)
        {
            drawings = new Drawing?[pieces.Count];
            baselines = new double[pieces.Count];
            drawnAt = lineHeight;
        }
        if (drawings[k] is { } done) return (done, baselines[k]);
        var (from, to) = pieces[k];
        using var line = env.Formatter.FormatLine(new Source(this) { End = to }, from, 1e6, new MdParaProps(look, lineHeight), null);
        var g = new DrawingGroup();
        using (var dc = g.Open()) line.Draw(dc, new Point(0, 0), InvertAxes.None);
        drawings[k] = g;
        baselines[k] = line.Baseline;
        return (g, line.Baseline);
    }

    /// The line from `s` to `e` drawn with its top at (x, y), `height` high (`lineHeight` 0: the font's own): inline
    /// code's tint under it, its pictures, then its text — the pieces it lies in, moved and clipped to it. `alone`
    /// (tests): its text formatted on its own instead, what the line should show.
    public void DrawLine(DrawingContext dc, MdEnv env, int s, int e, double x, double y, double lineHeight, double height, bool alone = false)
    {
        if (e <= s || pieces.Count == 0) return;
        double dy = lineHeight > 0 ? height - lineHeight : 0; // a picture made the line taller: the text sits at its bottom
        double baseX = X[s];
        int first = PieceAt(s);
        double baseline = Piece(env, first, lineHeight).Baseline;
        if (codeTint)
            foreach (var (cs, ce) in Code)
            {
                if (ce <= s || cs >= e || Text[cs] != Hangul.CodePad) continue; // an address: no tint
                var at = LookAt(Math.Min(ce - 1, Math.Max(cs, s)));
                var fam = at.Typeface.FontFamily;
                double size = at.FontRenderingEmSize;
                double top = y + dy + baseline - fam.Baseline * size;
                double x1 = X[Math.Max(cs, s)] - baseX, x2 = X[Math.Min(ce, e)] - baseX;
                dc.DrawRectangle(Theme.P.Code, null, new Rect(x + x1, top, Math.Max(0, x2 - x1), fam.LineSpacing * size));
            }
        foreach (var (at, p) in Pictures)
        {
            if (at < s || at >= e || p.Bitmap is not { } bmp) continue;
            dc.DrawImage(bmp.Source, new Rect(x + X[at] - baseX, y + height - p.Height, p.Width, p.Height));
        }
        // A piece holds only its own characters, so it is clipped only where it runs on past the line: halfway into the
        // spaces where the lines part at spaces (the line's own last glyph may overhang into its half); where they part
        // inside a word (Chinese, Japanese, code), between the two sides' ink (CutAt; where it comes closer a piece ends
        // there, Split). Where no edge holds (a ligature, a word longer than the line) the line is formatted on its own,
        // as `alone`.
        double left = double.NegativeInfinity, right = double.PositiveInfinity;
        int last = PieceAt(e - 1);
        if (!alone && s > pieces[first].From)
        {
            int p = LineBreaks.VisibleEnd(Text, pieces[first].From, s);
            left = p < s ? x + (X[p] + X[s]) / 2 - baseX : CutAt(s, x - baseX, env.Ppd, keepAfter: true);
            alone = double.IsNaN(left);
        }
        if (!alone && e < pieces[last].To)
        {
            int t = LineBreaks.VisibleEnd(Text, s, e);
            right = t < e ? x + (X[t] + X[e]) / 2 - baseX : CutAt(e, x - baseX, env.Ppd, keepAfter: false);
            alone = double.IsNaN(right);
        }
        if (alone)
        {
            // kept while the text is on screen: a resize or a scroll back finds most such lines as they were
            aloneDrawn ??= [];
            var key = (s, e, lineHeight);
            if (!aloneDrawn.TryGetValue(key, out var drawn))
            {
                if (aloneDrawn.Count >= 256) aloneDrawn.Clear(); // lines of widths long gone
                using var line = env.Formatter.FormatLine(new Source(this) { End = e }, s, 1e6, new MdParaProps(look, lineHeight), null);
                var g = new DrawingGroup();
                using (var c = g.Open()) line.Draw(c, new Point(0, 0), InvertAxes.None);
                aloneDrawn[key] = drawn = g;
            }
            dc.PushTransform(new TranslateTransform(x, y + dy));
            dc.DrawDrawing(drawn);
            dc.Pop();
            return;
        }
        bool clip = !double.IsInfinity(left) || !double.IsInfinity(right);
        if (clip)
        {
            double l = double.IsInfinity(left) ? x - height * 4 : left, r = double.IsInfinity(right) ? x + X[e] - baseX + height * 4 : right;
            dc.PushClip(new RectangleGeometry(new Rect(l, y - height, Math.Max(0, r - l), height * 3)));
        }
        for (int k = first; k <= last; k++)
        {
            var (drawing, _) = Piece(env, k, lineHeight);
            dc.PushTransform(new TranslateTransform(x - baseX + X[pieces[k].From], y + dy));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
        if (clip) dc.Pop();
    }

    /// The piece the character at `i` was shaped in.
    private int PieceAt(int i)
    {
        int lo = 0, hi = pieces.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (pieces[mid].From <= i) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}
