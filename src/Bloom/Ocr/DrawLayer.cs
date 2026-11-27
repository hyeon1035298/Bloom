using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.App;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Ocr;

using Change = (System.Windows.Ink.StrokeCollection added, System.Windows.Ink.StrokeCollection removed); // one undo step

/// 화면 캡처's pen and highlighter: ink over the picked area on the frozen screen, with a bar of tools beside it
/// (펜 P · 형광펜 H · 직선 L · 사각형 R · 지우개 E · colours 1–4 · three sizes · Ctrl+Z / Ctrl+Y) and where the picture goes next
/// (고정 · AI · 팔레트 · 저장 · 녹화 · 복사 Enter). The tool, colour and size last used are kept in the settings.
internal sealed class DrawLayer
{
    private static readonly Color[] Inks = { Color.FromRgb(0xFF, 0x4D, 0x4F), Color.FromRgb(0xFF, 0xD4, 0x3B), Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x22, 0xC5, 0x5E) };
    private static readonly double[] PenSizes = { 2, 4, 7 }, MarkSizes = { 12, 20, 30 };
    private static string[] ColorNames => new[] { Tr("빨강", "Red"), Tr("노랑", "Yellow"), Tr("파랑", "Blue"), Tr("초록", "Green") }; // the colour picker's names
    private static readonly Brush BarFill = Frozen(Color.FromArgb(0xF5, 0x27, 0x27, 0x2A)), BarLine = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
        Bright = Frozen(Color.FromRgb(0xEC, 0xED, 0xEE)), Dim = Frozen(Color.FromRgb(0xA1, 0xA1, 0xAA)), Hover = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
        On = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    public static string Hint => Tr("펜 P · 형광펜 H · 지우개 E · Enter 복사 · Esc 취소", "Pen P · Highlighter H · Eraser E · Enter Copy · Esc Cancel");

    private readonly InkCanvas ink;
    private readonly Size area; // the picked area in DIPs
    private readonly Action<string> done;
    private DrawPreferences preferences;
    private readonly Action<DrawPreferences> persist;
    private readonly Brush accent, accentInk, accentSoft;
    private readonly List<Change> undo = new(), redo = new();
    private readonly Dictionary<string, SmoothBorder> tools = new();
    private readonly List<SmoothBorder> swatches = new(), sizes = new();
    private bool replaying;
    private Point? shapeFrom; // 직선 · 사각형: where the drag started (DIPs in the area)
    private Stroke? shaping; // the shape being dragged, once it has moved

    /// Ink over `sel` (DIPs on `canvas`), the bar below it (above, or inside, when the screen ends), off the boxes in
    /// `keepOff` (the hint, the island) where one of those places is clear of them. `done` gets what to do with the
    /// picture: capture (copy) · pin · ask · palette · save.
    public DrawLayer(Canvas canvas, Rect sel, Size room, Action<string> done,
        DrawPreferences? preferences = null, DrawTheme? theme = null, Action<DrawPreferences>? persist = null,
        IReadOnlyList<Rect>? keepOff = null)
    {
        area = sel.Size;
        this.done = done;
        this.preferences = preferences ?? DrawPreferences.Capture();
        this.persist = persist ?? (p => p.Save());
        var colors = theme ?? DrawTheme.Capture();
        accent = DrawTheme.Brush(colors.Accent);
        accentInk = DrawTheme.Brush(colors.AccentInk);
        accentSoft = DrawTheme.Brush(colors.AccentSoft);
        ink = new InkCanvas { Width = sel.Width, Height = sel.Height, Background = Brushes.Transparent, ClipToBounds = true };
        ink.Strokes.StrokesChanged += (_, e) =>
        {
            if (replaying) return;
            undo.Add((e.Added, e.Removed));
            redo.Clear();
        };
        ink.MouseLeftButtonDown += (_, e) => ShapeStart(e);
        ink.MouseMove += (_, e) => ShapeMove(e);
        ink.MouseLeftButtonUp += (_, e) => ShapeEnd(e);
        Canvas.SetLeft(ink, sel.X);
        Canvas.SetTop(ink, sel.Y);
        canvas.Children.Add(ink);

        var bar = Bar();
        canvas.Children.Add(bar);
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (x, y) = BarAt(sel, room, bar.DesiredSize, keepOff ?? []);
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        Use(InHand);
    }

    /// The bar's place: below the area, above it, or inside its bottom, the first that fits the room and is clear of
    /// `keepOff`; when none is clear, the first that fits, as before there was anything to keep off.
    internal static (double x, double y) BarAt(Rect sel, Size room, Size bar, IReadOnlyList<Rect> keepOff)
    {
        double x = Math.Clamp(sel.X, 8, Math.Max(8, room.Width - bar.Width - 8));
        double inside = Math.Max(8, sel.Bottom - 10 - bar.Height);
        var places = new[] { sel.Bottom + 10, sel.Top - 10 - bar.Height, inside };
        bool Fits(double y) => y == inside || (y >= 8 && y + bar.Height <= room.Height - 8);
        bool Clear(double y)
        {
            var box = new Rect(x, y, bar.Width, bar.Height);
            foreach (var k in keepOff)
                if (k.IntersectsWith(box)) return false;
            return true;
        }
        foreach (double y in places)
            if (Fits(y) && Clear(y)) return (x, y);
        foreach (double y in places)
            if (Fits(y)) return (x, y);
        return (x, inside);
    }

    /// The overlay's keys while drawing (Esc stays with the overlay: it cancels).
    public bool HandleKey(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var key = Hotkeys.Pressed(e); // with the Korean IME on, P · H · E arrive as ImeProcessed
        switch (key)
        {
            case Key.Enter:
            case Key.C when ctrl:
                done("capture");
                return true;
            case Key.S when ctrl:
                done("save");
                return true;
            case Key.Z when ctrl:
                Step(undo, redo, true);
                return true;
            case Key.Y when ctrl:
                Step(redo, undo, false);
                return true;
            case Key.P when !ctrl:
                Use("pen");
                return true;
            case Key.H when !ctrl:
                Use("mark");
                return true;
            case Key.L when !ctrl:
                Use("line");
                return true;
            case Key.R when !ctrl:
                Use("rect");
                return true;
            case Key.E when !ctrl:
                Use("erase");
                return true;
            case >= Key.D1 and <= Key.D4 when !ctrl:
                Pick(key - Key.D1, null);
                return true;
            case >= Key.NumPad1 and <= Key.NumPad4 when !ctrl:
                Pick(key - Key.NumPad1, null);
                return true;
        }
        return false;
    }

    /// The picked pixels (BGRA, `width` × `height` physical px) with the ink drawn in; the same array when nothing was drawn.
    public byte[] Merge(byte[] bgra, int width, int height)
    {
        if (ink.Strokes.Count == 0) return bgra; // a plain capture costs nothing extra
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(picture, new Rect(0, 0, width, height));
            dc.PushTransform(new ScaleTransform(width / area.Width, height / area.Height));
            ink.Strokes.Draw(dc);
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[width * height * 4];
        rtb.CopyPixels(px, width * 4, 0); // the picture is opaque, so premultiplied = straight
        return px;
    }

    /// QA: a stroke through `points` (DIPs inside the area) as if drawn with the pen or the highlighter.
    public void AddForQa(string tool, int color, params Point[] points)
    {
        Use(tool);
        Pick(color, null);
        ink.Strokes.Add(new Stroke(new StylusPointCollection(points), ink.DefaultDrawingAttributes.Clone()));
    }

    /// QA: a 직선 or 사각형 from `a` to `b` (DIPs inside the area), as if dragged.
    public void AddShapeForQa(string tool, int color, Point a, Point b)
    {
        Use(tool);
        Pick(color, null);
        ink.Strokes.Add(new Stroke(new StylusPointCollection(ShapePoints(tool, a, b, false)), ink.DefaultDrawingAttributes.Clone()));
    }

    /// The tool in hand: the pen, the highlighter, 직선 or 사각형, or the last of those before the eraser.
    private string InHand => preferences.Tool is "mark" or "line" or "rect" ? preferences.Tool : "pen";

    /// The highlighter is in hand (its own colour and size); every other tool takes the pen's.
    private bool Marking => preferences.Tool == "mark";

    private static bool IsShape(string tool) => tool is "line" or "rect";

    private void Use(string tool)
    {
        var s = preferences;
        if (tool == "erase") ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
        else
        {
            bool mark = tool == "mark", shape = IsShape(tool);
            if (s.Tool != tool)
            {
                preferences = s = s with { Tool = tool };
                persist(s);
            }
            int c = Math.Clamp(mark ? s.MarkColor : s.PenColor, 0, Inks.Length - 1);
            int z = Math.Clamp(mark ? s.MarkSize : s.PenSize, 0, 2);
            double w = (mark ? MarkSizes : PenSizes)[z];
            ink.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Inks[c],
                IsHighlighter = mark,
                StylusTip = mark ? StylusTip.Rectangle : StylusTip.Ellipse,
                Width = mark ? w * 0.4 : w,
                Height = w,
                FitToCurve = !mark && !shape, // a shape's corners stay corners
                IgnorePressure = mark,
            };
            // the shapes are dragged out by ShapeStart · ShapeMove · ShapeEnd, so the canvas leaves the mouse alone
            ink.EditingMode = shape ? InkCanvasEditingMode.None : InkCanvasEditingMode.Ink;
            ink.UseCustomCursor = shape;
            ink.Cursor = shape ? Cursors.Cross : null;
            for (int i = 0; i < swatches.Count; i++) swatches[i].BorderBrush = i == c ? Bright : Brushes.Transparent;
            for (int i = 0; i < sizes.Count; i++) Mark(sizes[i], i == z);
        }
        foreach (var (key, button) in tools) Mark(button, key == tool, true);
    }

    /// A colour (0–3) and/or a size (0–2) for the tool in hand (the highlighter's own, else the pen's); from the eraser
    /// it goes back to that tool.
    private void Pick(int? color, int? size)
    {
        var s = preferences;
        if (Marking)
        {
            preferences = s with { MarkColor = color ?? s.MarkColor, MarkSize = size ?? s.MarkSize };
        }
        else
        {
            preferences = s with { PenColor = color ?? s.PenColor, PenSize = size ?? s.PenSize };
        }
        persist(preferences);
        Use(InHand);
    }

    // ── 직선 · 사각형 ──────────────────────────────────────────────────────────────────────────────
    // Pressed, the shape starts at the pointer; once dragged past a few DIPs it is one stroke (one undo step) whose
    // points follow the pointer; let go, it stays. A click without a drag draws nothing.

    private void ShapeStart(MouseButtonEventArgs e)
    {
        if (ink.EditingMode != InkCanvasEditingMode.None) return;
        shapeFrom = e.GetPosition(ink);
        ink.CaptureMouse();
        e.Handled = true;
    }

    private void ShapeMove(MouseEventArgs e)
    {
        if (shapeFrom is not { } a) return;
        var at = e.GetPosition(ink);
        var b = new Point(Math.Clamp(at.X, 0, area.Width), Math.Clamp(at.Y, 0, area.Height));
        var points = new StylusPointCollection(ShapePoints(preferences.Tool, a, b, (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
        if (shaping is null)
        {
            if ((b - a).Length < 3) return;
            shaping = new Stroke(points, ink.DefaultDrawingAttributes.Clone());
            ink.Strokes.Add(shaping);
        }
        else shaping.StylusPoints = points;
        e.Handled = true;
    }

    private void ShapeEnd(MouseButtonEventArgs e)
    {
        if (shapeFrom is null) return;
        ShapeMove(e);
        shapeFrom = null;
        shaping = null;
        ink.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// A shape's points (DIPs) from `a` to `b`: a straight line's two, or a rectangle's corners, closed. `even` (Shift):
    /// the line turned to the nearest multiple of 45°, the rectangle a square.
    internal static Point[] ShapePoints(string tool, Point a, Point b, bool even)
    {
        var d = b - a;
        if (tool == "line")
        {
            if (even)
            {
                double step = Math.PI / 4, angle = Math.Round(Math.Atan2(d.Y, d.X) / step) * step;
                var along = new Vector(Math.Round(Math.Cos(angle), 12), Math.Round(Math.Sin(angle), 12));
                b = a + along * (d * along); // the pointer projected onto that direction
            }
            return new[] { a, b };
        }
        if (even)
        {
            double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            b = new Point(a.X + Math.CopySign(side, d.X), a.Y + Math.CopySign(side, d.Y));
        }
        return new[] { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a };
    }

    private void Step(List<Change> from, List<Change> to, bool back)
    {
        if (from.Count == 0) return;
        var (added, removed) = from[^1];
        from.RemoveAt(from.Count - 1);
        replaying = true;
        try
        {
            ink.Strokes.Remove(back ? added : removed);
            ink.Strokes.Add(back ? removed : added);
        }
        finally
        {
            replaying = false;
        }
        to.Add((added, removed));
    }

    // ── the bar ─────────────────────────────────────────────────────────────────────────────────
    private SmoothBorder Bar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        tools["pen"] = Button(row, G.Pen, Tr("펜", "Pen"), Tr("펜 (P)", "Pen (P)"), () => Use("pen"));
        tools["mark"] = Button(row, G.Highlight, Tr("형광펜", "Highlighter"), Tr("형광펜 (H)", "Highlighter (H)"), () => Use("mark"));
        // no Segoe Fluent glyph draws these two, so they are drawn here, in the glyphs' size and weight
        var lineIcon = new System.Windows.Shapes.Line { X1 = 2, Y1 = 13, X2 = 13, Y2 = 2, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 15, Height = 15 };
        tools["line"] = Hit(row, lineIcon, null, Tr("직선 (L) · Shift로 45° 단위", "Line (L) · Shift for 45° steps"), () => Use("line"));
        var rectIcon = new System.Windows.Shapes.Rectangle { Width = 14, Height = 11, StrokeThickness = 1.4, RadiusX = 2, RadiusY = 2 };
        tools["rect"] = Hit(row, rectIcon, null, Tr("사각형 (R) · Shift로 정사각형", "Rectangle (R) · Shift for a square"), () => Use("rect"));
        tools["erase"] = Button(row, G.Erase, null, Tr("지우개 (E) · 그은 선을 통째로 지웁니다", "Eraser (E) · Erases entire strokes"), () => Use("erase"));
        Separator(row);
        for (int i = 0; i < Inks.Length; i++)
        {
            int c = i;
            var dot = new SmoothBorder
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(2),
                Child = new SmoothBorder { Background = Frozen(Inks[c]), CornerRadius = new CornerRadius(7) },
            };
            swatches.Add(dot);
            Hit(row, dot, null, $"{ColorNames[c]} ({c + 1})", () => Pick(c, null));
        }
        Separator(row);
        for (int i = 0; i < 3; i++)
        {
            int z = i;
            var line = new SmoothBorder { Width = 16, Height = PenSizes[z], CornerRadius = new CornerRadius(3.5), Background = Dim };
            sizes.Add(Hit(row, line, null, new[] { Tr("가는 선", "Thin"), Tr("보통 선", "Medium line"), Tr("굵은 선", "Thick") }[z], () => Pick(null, z)));
        }
        Separator(row);
        Button(row, G.Undo, null, Tr("되돌리기 (Ctrl+Z)", "Undo (Ctrl+Z)"), () => Step(undo, redo, true));
        Button(row, G.Redo, null, Tr("다시 하기 (Ctrl+Y)", "Redo (Ctrl+Y)"), () => Step(redo, undo, false));
        Separator(row);
        Button(row, G.Pin, null, Tr("화면에 고정", "Pin to Screen"), () => done("pin"));
        Hit(row, Theme.Sparkle(15, Dim), null, Tr("AI에게 묻기", "Ask AI"), () => done("ask")); // Bloom Lens' own words
        Button(row, G.Palette, null, Tr("컬러 팔레트 만들기 · 그린 내용 포함", "Create Color Palette · Including drawings"), () => done("palette"));
        Button(row, G.Save, null, Tr("그림 저장…", "Save Image…") + " (Ctrl+S)", () => done("save"));
        Button(row, G.Video, null, Tr("화면 녹화 · 이 영역을 영상으로 녹화합니다", "Record Screen · Record this area as a video"), () => done("record"));
        var copy = new SmoothBorder
        {
            Background = accent,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 0, 12, 0),
            Height = 32,
            Margin = new Thickness(4, 0, 2, 0),
            Cursor = Cursors.Hand,
        };
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(Theme.T(Tr("복사", "Copy"), 13, accentInk, Theme.Medium, false));
        var key = Theme.T("Enter", 11.5, accentInk, null, false);
        key.Opacity = 0.62;
        key.Margin = new Thickness(8, 1, 0, 0);
        label.Children.Add(key);
        copy.Child = label;
        copy.MouseLeftButtonUp += (_, e) =>
        {
            done("capture");
            e.Handled = true;
        };
        row.Children.Add(copy);
        Button(row, G.Close, null, Tr("취소 (Esc)", "Cancel (Esc)"), () => done(""));
        return new SmoothBorder
        {
            Background = BarFill,
            BorderBrush = BarLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            Child = row,
            Cursor = Cursors.Arrow,
        };
    }

    private static SmoothBorder Button(Panel row, char glyph, string? label, string tip, Action click)
    {
        FrameworkElement content = Theme.Glyph(glyph, 15, Dim);
        if (label is not null)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(content);
            var t = Theme.T(label, 13, Dim, null, false);
            t.Margin = new Thickness(6, 0, 0, 0);
            p.Children.Add(t);
            content = p;
        }
        return Hit(row, content, label, tip, click);
    }

    private static SmoothBorder Hit(Panel row, FrameworkElement content, string? label, string tip, Action click)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var b = new SmoothBorder
        {
            MinWidth = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(label is null ? 0 : 9, 0, label is null ? 0 : 10, 0),
            Background = Brushes.Transparent,
            Child = content,
            ToolTip = tip,
            Cursor = Cursors.Hand,
        };
        b.MouseEnter += (_, _) =>
        {
            if (b.Tag is not true) b.Background = Hover;
        };
        b.MouseLeave += (_, _) =>
        {
            if (b.Tag is not true) b.Background = Brushes.Transparent;
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        row.Children.Add(b);
        return b;
    }

    /// A button shown as chosen: the accent for the tools, a lighter tile for the sizes.
    private void Mark(SmoothBorder b, bool on, bool accented = false)
    {
        b.Tag = on;
        b.Background = on ? (accented ? accentSoft : On) : Brushes.Transparent;
        var fg = on ? (accented ? accent : Bright) : Dim;
        Tint(b.Child, fg);
    }

    private static void Tint(object? child, Brush fg)
    {
        switch (child)
        {
            case TextBlock t:
                t.Foreground = fg;
                break;
            case Panel p:
                foreach (var c in p.Children) Tint(c, fg);
                break;
            case Border { Child: null } line: // a size button's line
                line.Background = fg;
                break;
            case System.Windows.Shapes.Shape shape: // 직선 · 사각형's icons
                shape.Stroke = fg;
                break;
        }
    }

    private static void Separator(Panel row) => row.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), Background = BarLine });

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
