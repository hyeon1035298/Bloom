using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ui;

namespace Bloom.Space;

/// The squarified treemap (Bruls, Huizing & van Wijk) of one folder: nested blocks, each folder with
/// a title strip and its children inside, as deep as blocks stay readable. Drawn in one OnRender;
/// the hovered block is outlined on top without redrawing the map.
internal sealed class TreemapView : FrameworkElement
{
    private const double Strip = 17, Pad = 2, MinSide = 4;
    private const int MaxBlocks = 6000;

    private SpaceDir? root;
    private readonly List<(Rect r, SpaceNode n, int depth)> blocks = new();
    private SpaceNode? hover;
    private Rect hoverRect;
    private readonly DrawingVisual outline = new();
    private readonly Typeface face = new(Theme.Sans, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private double pixels = 1;

    public event Action<SpaceNode?>? Hovered;
    public event Action<SpaceDir>? Opened; // a folder clicked: go into it
    public event Action<SpaceNode, Point>? Menu; // right click

    public TreemapView()
    {
        AddVisualChild(outline);
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.Arrow;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => outline;

    public SpaceDir? Root
    {
        get => root;
        set
        {
            root = value;
            hover = null;
            InvalidateVisual();
        }
    }

    public void Redraw() => InvalidateVisual();

    // ── layout ──────────────────────────────────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        blocks.Clear();
        pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Theme.P.Solid, null, new Rect(RenderSize));
        if (root is null || ActualWidth < 20 || ActualHeight < 20) return;
        // a copy of the palette brush: freezing the pen would freeze the shared brush (themes switch it)
        folderLine = new Pen(Theme.P.CardLine.CloneCurrentValue(), 1);
        folderLine.Freeze();
        // level by level: every folder on screen gets its children before anything goes a level
        // deeper, so the block budget is spread over the whole map
        var queue = new Queue<(SpaceDir dir, Rect area, int depth)>();
        queue.Enqueue((root, new Rect(0, 0, ActualWidth, ActualHeight), 0));
        while (queue.Count > 0)
        {
            var (dir, area, depth) = queue.Dequeue();
            Lay(dc, dir, area, depth, queue);
        }
        DrawHover();
    }

    private Pen folderLine = new(Brushes.Gray, 1);
    private static readonly Brush SizeInk = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// One folder's children in `area` (the folder's own frame is drawn by the caller); folders
    /// among them go on the queue for the next level.
    private void Lay(DrawingContext dc, SpaceDir dir, Rect area, int depth, Queue<(SpaceDir, Rect, int)> next)
    {
        long total = Volatile.Read(ref dir.Size);
        if (total <= 0 || area.Width < MinSide || area.Height < MinSide || blocks.Count > MaxBlocks) return;
        var items = new List<(SpaceNode? n, double size)>();
        foreach (var c in dir.Children())
        {
            long s = Volatile.Read(ref c.Size);
            if (s > 0) items.Add((c, s));
        }
        if (dir.Hidden > 0 && dir.RestSize > 0) items.Add((null, dir.RestSize)); // the folded small files
        items.Sort((a, b) => b.size.CompareTo(a.size));
        double sum = items.Sum(i => i.size);
        if (sum <= 0) return;
        double scale = area.Width * area.Height / sum;
        var rects = Squarify(items.Select(i => i.size * scale).ToList(), area);
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (r.Width < 1 || r.Height < 1) continue;
            var (n, _) = items[i];
            if (n is null)
            {
                Block(dc, r, Theme.P.Card, null);
                if (r.Width > 70 && r.Height > 18) Label(dc, Tr($"작은 파일 {dir.Hidden:#,0}개", $"{dir.Hidden:#,0} small files"), r, Theme.P.TextFaint, 10.5);
                continue;
            }
            blocks.Add((r, n, depth));
            if (n is SpaceDir d)
            {
                var fill = depth % 2 == 0 ? Theme.P.Tile : Theme.P.Card;
                Block(dc, r, fill, folderLine);
                bool strip = r.Height > Strip + 10 && r.Width > 44;
                if (strip) Label(dc, $"{n.Name}  {Size(Volatile.Read(ref n.Size))}", new Rect(r.X + 5, r.Y + 1, r.Width - 8, Strip - 2), Theme.P.TextDim, 11, Theme.Semi);
                var inner = strip ? new Rect(r.X + Pad, r.Y + Strip, Math.Max(0, r.Width - Pad * 2), Math.Max(0, r.Height - Strip - Pad)) : Rect.Inflate(r, -Pad, -Pad);
                if (!d.Done && d.ChildCount == 0 && inner.Width > 30 && inner.Height > 14) Label(dc, Tr("읽는 중…", "Scanning…"), inner, Theme.P.TextFaint, 10.5);
                if (inner.Width > MinSide && inner.Height > MinSide) next.Enqueue((d, inner, depth + 1));
            }
            else
            {
                Block(dc, r, Kind(n.Name), null);
                if (r.Width > 54 && r.Height > 16) Label(dc, n.Name, new Rect(r.X + 4, r.Y + 2, r.Width - 6, Math.Min(r.Height - 2, 16)), Palette.OffWhiteBrush, 10.5);
                if (r.Width > 54 && r.Height > 32) Label(dc, Size(Volatile.Read(ref n.Size)), new Rect(r.X + 4, r.Y + 17, r.Width - 6, 14), SizeInk, 10);
            }
        }
    }

    private static void Block(DrawingContext dc, Rect r, Brush fill, Pen? line)
    {
        var inset = Rect.Inflate(r, -0.5, -0.5);
        if (inset.Width <= 0 || inset.Height <= 0) return;
        dc.DrawRectangle(fill, line, inset);
    }

    private void Label(DrawingContext dc, string text, Rect r, Brush color, double size, FontWeight? weight = null)
    {
        if (r.Width < 8 || r.Height < 8) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, weight is { } w ? new Typeface(Theme.Sans, FontStyles.Normal, w, FontStretches.Normal) : face, size, color, pixels)
        {
            MaxTextWidth = r.Width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, new Point(r.X, r.Y + Math.Max(0, (Math.Min(r.Height, 16) - ft.Height) / 2)));
    }

    /// Bruls' squarify: rows of blocks along the shorter side, a block joins the row while the
    /// row's worst aspect ratio does not get worse. Sizes are areas, largest first.
    private static List<Rect> Squarify(List<double> sizes, Rect area)
    {
        var result = new List<Rect>(sizes.Count);
        int i = 0;
        var free = area;
        while (i < sizes.Count && free.Width > 0 && free.Height > 0)
        {
            double side = Math.Min(free.Width, free.Height);
            int start = i;
            double rowSum = sizes[i];
            double worst = Worst(sizes, start, i + 1, rowSum, side);
            i++;
            while (i < sizes.Count)
            {
                double nextSum = rowSum + sizes[i];
                double w = Worst(sizes, start, i + 1, nextSum, side);
                if (w > worst) break;
                worst = w;
                rowSum = nextSum;
                i++;
            }
            // lay the row along the shorter side
            double thick = rowSum / side;
            bool horizontal = free.Width >= free.Height; // row stands as a column on the left
            double pos = 0;
            for (int k = start; k < i; k++)
            {
                double len = sizes[k] / thick;
                result.Add(horizontal ? new Rect(free.X, free.Y + pos, thick, len) : new Rect(free.X + pos, free.Y, len, thick));
                pos += len;
            }
            free = horizontal
                ? new Rect(free.X + thick, free.Y, Math.Max(0, free.Width - thick), free.Height)
                : new Rect(free.X, free.Y + thick, free.Width, Math.Max(0, free.Height - thick));
        }
        while (result.Count < sizes.Count) result.Add(Rect.Empty);
        return result;
    }

    private static double Worst(List<double> sizes, int from, int to, double sum, double side)
    {
        double max = 0, min = double.MaxValue;
        for (int k = from; k < to; k++)
        {
            max = Math.Max(max, sizes[k]);
            min = Math.Min(min, sizes[k]);
        }
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    // ── colours: muted hues that sit with the rose-tinted neutrals ──────────────────────────────
    private static Brush Tint(uint rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    private static readonly Brush Video = Tint(0xB4476A), Picture = Tint(0xB37A3E), Audio = Tint(0x7B5AA6), Archive = Tint(0x4F6E9E),
        Code = Tint(0x3F8A82), Program = Tint(0x5E6676), Document = Tint(0x8C6A5A), Model = Tint(0x9A5C8E), Other = Tint(0x6F6468);

    public static Brush Kind(string name)
    {
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".wmv" or ".m4v" or ".ts" or ".prproj" => Video,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".psd" or ".tif" or ".tiff" or ".bmp" or ".exr" or ".hdr" or ".tga" or ".ai" or ".heic" => Picture,
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" => Audio,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".unitypackage" or ".iso" or ".cab" or ".msi" => Archive,
            ".cs" or ".js" or ".ts" or ".json" or ".py" or ".rs" or ".cpp" or ".h" or ".txt" or ".md" or ".xml" or ".log" or ".meta" or ".asset" or ".prefab" or ".unity" or ".mat" => Code,
            ".exe" or ".dll" or ".sys" or ".pak" or ".bin" or ".dat" or ".db" => Program,
            ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".hwp" => Document,
            ".fbx" or ".blend" or ".obj" or ".vrm" or ".glb" or ".gltf" or ".spp" or ".sbsar" => Model,
            _ => Other,
        };
    }

    public static string Size(long b) => App.Engine.Size(b);

    // ── hover and clicks ────────────────────────────────────────────────────────────────────────
    /// The deepest block under the point.
    public (SpaceNode? node, Rect rect) At(Point p)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
            if (blocks[i].r.Contains(p)) return (blocks[i].n, blocks[i].r);
        return (null, Rect.Empty);
    }

    private void DrawHover()
    {
        using var dc = outline.RenderOpen();
        if (hover is null || hoverRect.IsEmpty) return;
        dc.DrawRectangle(null, new Pen(Theme.P.Accent, 2), Rect.Inflate(hoverRect, -1, -1));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var (n, r) = At(e.GetPosition(this));
        if (n == hover && r == hoverRect) return;
        hover = n;
        hoverRect = r;
        DrawHover();
        Hovered?.Invoke(n);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hover = null;
        DrawHover();
        Hovered?.Invoke(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var (n, _) = At(e.GetPosition(this));
        if (n is null) return;
        // a click on a folder (or a file inside one) goes one level in, towards what was clicked
        var target = n as SpaceDir ?? n.Parent;
        while (target is not null && target.Parent != root && target != root) target = target.Parent;
        if (target is not null && target != root) Opened?.Invoke(target);
        else if (!n.IsDir && e.ClickCount == 2) Search.Launch.Open(n.Path);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var (n, _) = At(p);
        if (n is not null) Menu?.Invoke(n, p);
    }
}
