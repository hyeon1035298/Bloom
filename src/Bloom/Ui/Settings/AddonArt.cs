using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Addons;
using Bloom.Core;
using static Bloom.Ui.ArtKit;

namespace Bloom.Ui;

/// The moving picture of what an add-on does, at the start of its line on 설정 > 애드온: shapes and words drawn in code
/// in the theme's colours, an 8 s loop. The loop runs only while the line is on screen and Motion.Moves allows it, or,
/// with animations off, while the pointer is on the line (Pointed: the viewer asked to see it, from the start);
/// otherwise the picture holds the moment that shows the add-on at work. For gives null for an add-on Bloom has no
/// picture of (its line shows its glyph).
internal sealed class AddonArt : FrameworkElement
{
    public const double W = 232, H = 84;
    private const double Loop = 8;

    private enum Kind { Taskbar, AltTab, Brightness, Memory, Coffee, RunCard }

    private readonly Kind kind;
    private readonly double still;
    private readonly Stopwatch clock = new();
    private readonly ArtKit.Words words;
    private DispatcherTimer? timer;

    /// In a tutorial tile (Tile): it plays only while pointed at, and draws in the tile's palette.
    private readonly bool inTile;

    private AddonArt(Kind kind, double still, bool inTile = false)
    {
        this.kind = kind;
        this.still = still;
        this.inTile = inTile;
        words = new(this);
        Width = W;
        Height = H;
        IsHitTestVisible = false;
        // a line's picture has its own round corners; a tile's is cut by the tile, which shows a part of it
        double corner = inTile ? 0 : 8;
        var clip = new RectangleGeometry(new Rect(0, 0, W, H), corner, corner);
        clip.Freeze();
        Clip = clip;
        Loaded += (_, _) => Run();
        Unloaded += (_, _) => Run();
        IsVisibleChanged += (_, _) => Run();
    }

    public static AddonArt? For(string id) => Make(id, false);

    private static AddonArt? Make(string id, bool inTile) => id switch
    {
        AddonProtocol.Id => new(Kind.Taskbar, 0.45, inTile),
        "alt-tab-per-monitor" => new(Kind.AltTab, 0.25, inTile),
        "brightness" => new(Kind.Brightness, 0.6, inTile),
        "memory-clean" => new(Kind.Memory, 0.6, inTile),
        "keep-awake" => new(Kind.Coffee, 0.62, inTile),
        "run-card" => new(Kind.RunCard, 0.40, inTile),
        _ => null,
    };

    /// The add-on's picture for a 178x84 tile: the same drawing at 1:1, shown through a window on its middle. It plays
    /// only while its tile is pointed at; `frozen` (QA pictures) = never, it keeps its still moment.
    public static FrameworkElement? Tile(string id, double width = 178, double height = 84, bool frozen = false)
    {
        if (Make(id, true) is not { } art) return null;
        // transparent, not empty: on its own (no tile around it) the view is what the pointer is over
        var view = new Canvas { Width = width, Height = height, ClipToBounds = true, Background = Brushes.Transparent };
        switch (art.kind)
        {
            case Kind.AltTab:
                // both monitors are the picture: all of it, made as narrow as the tile and set in its middle
                double scale = width / W;
                art.RenderTransform = new ScaleTransform(scale, scale);
                Canvas.SetTop(art, Math.Round((height - H * scale) / 2));
                break;
            case Kind.Taskbar:
                Canvas.SetLeft(art, (width - W) / 2); // the pill is in the middle
                break;
            default:
                Canvas.SetLeft(art, width - W); // the widget and its window are at the right end
                break;
        }
        view.Children.Add(art);
        if (frozen) return view;

        UIElement? over = null;
        void Enter(object s, System.Windows.Input.MouseEventArgs e) => art.Pointed = true;
        void Leave(object s, System.Windows.Input.MouseEventArgs e) => art.Pointed = false;
        view.Loaded += (_, _) =>
        {
            if (over is not null) return;
            over = view;
            for (DependencyObject? d = view; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is PictureTile tile)
                {
                    over = tile;
                    break;
                }
            over.MouseEnter += Enter;
            over.MouseLeave += Leave;
            art.Pointed = over.IsMouseOver;
        };
        view.Unloaded += (_, _) =>
        {
            if (over is null) return;
            over.MouseEnter -= Enter;
            over.MouseLeave -= Leave;
            over = null;
            art.Pointed = false;
        };
        return view;
    }

    /// The loop is running (the tests read it).
    internal bool Playing => timer is not null;

    /// A moment of the loop to show (0–1) in place of the clock: the tests' pictures.
    internal double? Hold { get; set; }

    private bool pointed;

    /// The pointer is on the picture's line (set by the line): with animations off, the loop plays while it is.
    public bool Pointed
    {
        get => pointed;
        set
        {
            if (pointed == value) return;
            pointed = value;
            Run();
        }
    }

    /// Starts the loop while the picture is on screen and things may move (or it is pointed at), and stops it otherwise.
    /// In a tile only the pointer starts it: a page of tiles would run a loop each for as long as it shows.
    private void Run()
    {
        bool moves = Motion.Moves && !inTile, go = IsLoaded && IsVisible && (moves || pointed);
        if (go && timer is null)
        {
            int fps = Settings.Current.AnimationFps > 0 ? Math.Min(Settings.Current.AnimationFps, 60) : 30;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / fps) };
            timer.Tick += (_, _) => InvalidateVisual();
            if (!moves) clock.Reset(); // pointed at with animations off: from the start each time
            clock.Start();
            timer.Start();
        }
        else if (!go && timer is not null)
        {
            timer.Stop();
            timer = null;
            clock.Stop();
        }
        InvalidateVisual();
    }

    // The words are laid out for the DPI they were first drawn at; on another monitor they are laid out again.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        words.Forget();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double p = Hold ?? (timer is null ? still : clock.Elapsed.TotalSeconds % Loop / Loop);
        ink = new Ink(Theme.P.Dark, inTile);
        switch (kind)
        {
            case Kind.Taskbar: Taskbar(dc, p); break;
            case Kind.AltTab: AltTab(dc, p); break;
            case Kind.Brightness: Brightness(dc, p); break;
            case Kind.Memory: Memory(dc, p); break;
            case Kind.Coffee: Coffee(dc, p); break;
            case Kind.RunCard: RunCard(dc, p); break;
        }
    }

    // ---- the pictures. p: the share of the loop (0–1)

    /// The auto-hide island at the top of the screen; pointed at, it opens into the pill where it is, and closes again.
    private void Taskbar(DrawingContext dc, double p)
    {
        Window(dc, new Rect(20, 34, 96, 40));
        Window(dc, new Rect(126, 40, 86, 34));

        double m = K(p, (.22, 0), (.32, 1), (.64, 1), (.74, 0));
        var r = Lerp(new Rect(60, -10, 112, 29), new Rect(36, 8, 160, 26), m);
        double radius = 10 + 3 * m;
        Shadow(dc, r, radius);
        dc.DrawRoundedRectangle(B(Mix(0xFF18181B, ink.Pill, m)), m > 0 ? P(Fade(ink.PillEdge, m), 1) : null, r, radius, radius);
        double cy = (Math.Max(r.Top, 0) + r.Bottom) / 2;

        // the island: the time, a little bar and the battery
        double io = K(p, (.24, 1), (.28, 0), (.68, 0), (.72, 1));
        if (io > 0)
        {
            dc.PushOpacity(io);
            var a = Text("10:39", 8, FontWeights.SemiBold, 0xFFFAFAFA);
            var b = Text("68%", 8, FontWeights.SemiBold, 0xFFFAFAFA);
            double x = r.Left + r.Width / 2 - (a.Width + 6 + 20 + 6 + b.Width) / 2;
            dc.DrawText(a, new Point(x, cy - a.Height / 2));
            x += a.Width + 6;
            dc.DrawRoundedRectangle(B(0x40FFFFFF), null, new Rect(x, cy - 1.5, 20, 3), 1.5, 1.5);
            dc.DrawRoundedRectangle(B(0xFFE4E4E7), null, new Rect(x, cy - 1.5, 20 * .68, 3), 1.5, 1.5);
            dc.DrawText(b, new Point(x + 26, cy - b.Height / 2));
            dc.Pop();
        }

        // the pill: Bloom, four pinned apps and the clock
        double po = K(p, (.27, 0), (.33, 1), (.62, 1), (.67, 0));
        if (po > 0)
        {
            dc.PushOpacity(po);
            double x = r.Left + 5 + (r.Width - 33 - (5 * 13 + 4 * 6)) / 2;
            dc.DrawRoundedRectangle(B(Accent), null, new Rect(x, cy - 6.5, 13, 13), 4, 4);
            foreach (char g in new[] { '\uE8B7', '\uE767', '\uE8BD', '\uE70F' })
            {
                x += 19;
                var tile = new Rect(x, cy - 6.5, 13, 13);
                dc.DrawRoundedRectangle(B(ink.Icon), P(ink.PillEdge, 1), tile, 4, 4);
                var gl = Glyph(g, 8, ink.Glyph);
                dc.DrawText(gl, new Point(tile.Left + (13 - gl.Width) / 2, tile.Top + (13 - gl.Height) / 2));
            }
            var clk = Text("10:39", 8, FontWeights.SemiBold, ink.Text);
            dc.DrawText(clk, new Point(r.Right - 8 - clk.Width, cy - clk.Height / 2));
            dc.Pop();
        }

        Pointer(dc, K(p, (.08, 160), (.20, 124), (.64, 124), (.76, 160)), K(p, (.08, 58), (.20, 12), (.64, 12), (.76, 58)));
    }

    /// Two monitors: Alt+Tab with the cursor on the left one shows the left one's windows alone, then on the right one
    /// the right's alone; each pick is its thumbnail's outline.
    private void AltTab(DrawingContext dc, double p)
    {
        Monitor(dc, 18, new[] { new Rect(6, 6, 44, 28), new Rect(38, 20, 42, 26) },
            K(p, (.06, 0), (.10, 1), (.38, 1), (.42, 0)), p < .24 ? 0 : 1);
        Monitor(dc, 122, new[] { new Rect(5, 5, 36, 22), new Rect(44, 8, 38, 20), new Rect(20, 28, 46, 20) },
            K(p, (.52, 0), (.56, 1), (.86, 1), (.90, 0)), p < .66 ? 0 : p < .76 ? 1 : 2);
        Pointer(dc, K(p, (.42, 98), (.50, 200), (.90, 200), (.98, 98)), 56);
    }

    private void Monitor(DrawingContext dc, double left, Rect[] windows, double shown, int pick)
    {
        dc.DrawRoundedRectangle(B(ink.Screen), P(ink.Frame, 2), new Rect(left + 1, 13, 90, 54), 4, 4);
        dc.DrawRoundedRectangle(B(ink.Frame), null, new Rect(left + 38, 68, 16, 6), 2, 2);
        foreach (var w in windows) dc.DrawRoundedRectangle(B(ink.Thumb), null, new Rect(left + 2 + w.X, 14 + w.Y, w.Width, w.Height), 3, 3);
        if (shown <= 0) return;

        var sw = new Rect(left + 9, 28, 74, 24);
        dc.PushOpacity(shown);
        dc.PushTransform(new ScaleTransform(.94 + .06 * shown, .94 + .06 * shown, sw.Left + sw.Width / 2, sw.Top + sw.Height / 2));
        Shadow(dc, sw, 5);
        dc.DrawRoundedRectangle(B(ink.Pill), P(ink.PillEdge, 1), sw, 5, 5);
        int n = windows.Length;
        double tw = (sw.Width - 6 - 4 * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            var t = new Rect(sw.Left + 3 + i * (tw + 4), sw.Top + 3, tw, sw.Height - 6);
            dc.DrawRoundedRectangle(B(ink.Thumb), null, t, 3, 3);
            if (i == pick)
            {
                t.Inflate(.75, .75);
                dc.DrawRoundedRectangle(null, P(ink.Text, 1.5), t, 3.5, 3.5);
            }
        }
        dc.Pop();
        dc.Pop();
    }

    /// The brightness widget at the pill's right end opens its window; both monitors' bars rise and the widget's
    /// number follows them.
    private void Brightness(DrawingContext dc, double p)
    {
        double v = K(p, (.24, 60), (.48, 85), (.80, 85), (.96, 60));
        Window(dc, new Rect(14, 12, 84, 58));
        Tray(dc, p, 0, v, 46);

        Fly(dc, p, 46, top =>
        {
            for (int i = 0; i < 2; i++)
            {
                double y = top + i * 20;
                var gl = Glyph('\uE7F4', 7, ink.Text);
                dc.DrawText(gl, new Point(103, y + (9 - gl.Height) / 2));
                var name = Text(i == 0 ? Lang.Tr("모니터 1", "Monitor 1") : Lang.Tr("모니터 2", "Monitor 2"), 7, FontWeights.Normal, ink.Text);
                dc.DrawText(name, new Point(114, y + (9 - name.Height) / 2));
                var val = Text($"{Math.Round(v)} %", 7, FontWeights.Bold, ink.Text);
                dc.DrawText(val, new Point(217 - val.Width, y + (9 - val.Height) / 2));
                Bar(dc, y + 13, v);
            }
        });

        double bar = 103 + 114 * v / 100;
        Pointer(dc, K(p, (.04, 120), (.10, 150), (.18, 150), (.26, 171), (.48, bar), (.70, bar), (.80, 120)),
            K(p, (.04, 60), (.10, 18), (.18, 18), (.26, 53), (.70, 53), (.80, 60)));
    }

    /// The RAM widget opens the cleaning window; 지금 정리 drops the use and what was freed shows.
    private void Memory(DrawingContext dc, double p)
    {
        double u = K(p, (.34, 46), (.48, 31), (.80, 31), (.96, 46));
        Window(dc, new Rect(14, 12, 84, 58));
        Tray(dc, p, 1, 60, u);

        Fly(dc, p, 49, top =>
        {
            var big = Text($"{Math.Round(u)}%", 11, FontWeights.Bold, ink.Text);
            dc.DrawText(big, new Point(103, top));
            var gb = Text($"{u * 16 / 100:0.0} GB / 16.0 GB", 7, FontWeights.Normal, ink.Faint);
            dc.DrawText(gb, new Point(106 + big.Width, top + big.Baseline - gb.Baseline));
            Bar(dc, top + 19, u);

            var btn = new Rect(103, top + 26, 50, 13);
            dc.DrawRoundedRectangle(B(p >= .30 && p < .36 ? Fade(Accent, .22) : ink.Hover), null, btn, 4, 4);
            var label = Text("✦ " + Lang.Tr("지금 정리", "Clean Now"), 7, FontWeights.SemiBold, ink.Text);
            dc.DrawText(label, new Point(btn.Left + (btn.Width - label.Width) / 2, btn.Top + (btn.Height - label.Height) / 2));

            double fo = K(p, (.46, 0), (.52, 1), (.72, 1), (.78, 0));
            if (fo > 0)
            {
                var freed = Text(Lang.Tr("1.2 GB 비움", "1.2 GB freed"), 7, FontWeights.Normal, ink.Faint);
                dc.PushOpacity(fo);
                dc.DrawText(freed, new Point(217 - freed.Width, btn.Top + (btn.Height - freed.Height) / 2));
                dc.Pop();
            }
        });

        Pointer(dc, K(p, (.04, 120), (.10, 178), (.18, 178), (.26, 124), (.66, 124), (.80, 120)),
            K(p, (.04, 60), (.10, 18), (.18, 18), (.26, 68), (.66, 68), (.80, 60)));
    }

    /// The cup next to the clock opens its window; the switch fills the cup with the accent and keeps the computer
    /// awake until turned off, then 1시간 is picked and the time left counts down. The add-on's own words.
    private void Coffee(DrawingContext dc, double p)
    {
        double fill = K(p, (.32, 0), (.42, 1), (.84, 1), (.94, 0));
        Window(dc, new Rect(14, 12, 84, 58));

        var clk = Text("2:37", 8, FontWeights.SemiBold, ink.Text);
        double w = 4 + 27 + 4 + 3 + clk.Width + 9, x = 224 - w;
        var seg = new Rect(x, 6, w, 22);
        Shadow(dc, seg, 11);
        dc.DrawRoundedRectangle(B(ink.Pill), P(ink.PillEdge, 1), seg, 11, 11);
        if (p >= .12 && p < .76) dc.DrawRoundedRectangle(B(ink.Hover), null, new Rect(x + 4, 8, 27, 18), 5, 5);
        Cup(dc, x + 4 + 13.5, 17, fill);
        dc.DrawText(clk, new Point(x + 4 + 31 + 3, 17 - clk.Height / 2));

        Fly(dc, p, 46, top =>
        {
            dc.DrawText(Text(Lang.Tr("커피", "Coffee"), 8, FontWeights.SemiBold, ink.Text), new Point(103, top));

            double on = K(p, (.30, 0), (.35, 1));
            var track = new Rect(201, top + 1, 16, 9);
            dc.DrawRoundedRectangle(B(Mix(ink.Track, Accent, on)), null, track, 4.5, 4.5);
            dc.DrawEllipse(B(ink.Paper), null, new Point(track.Left + 4.5 + 7 * on, track.Top + 4.5), 3.5, 3.5);

            double left = Math.Max(0, 3600 - Math.Floor((p - .58) * Loop));
            string big = p < .32 ? Lang.Tr("잠들 수 있음", "Can sleep")
                : p < .58 ? Lang.Tr("끌 때까지 깨어 있음", "Awake until turned off")
                : string.Format(Lang.Tr("{0} 남음", "{0} left"), left >= 3600 ? "1:00:00" : $"{left / 60 % 60:0}:{left % 60:00}");
            dc.DrawText(Text(big, 9, FontWeights.SemiBold, ink.Text), new Point(103, top + 12));

            var row = new Rect(103, top + 26, 114, 11);
            dc.DrawRoundedRectangle(B(ink.Hover), null, row, 4, 4);
            double cw = row.Width / 4;
            double at = K(p, (.54, 0), (.60, 2));
            var pick = new Rect(row.Left + 1 + at * cw, row.Top + 1, cw - 2, row.Height - 2);
            dc.DrawRoundedRectangle(B(ink.Icon), P(ink.PillEdge, 1), pick, 3.5, 3.5);
            string[] times = [Lang.Tr("계속", "Indefinitely"), Lang.Tr("30분", "30 min"), Lang.Tr("1시간", "1 hr"), Lang.Tr("2시간", "2 hr")];
            for (int i = 0; i < times.Length; i++)
            {
                var t = Text(times[i], 6.5, FontWeights.Normal, ink.Text);
                dc.DrawText(t, new Point(row.Left + i * cw + (cw - t.Width) / 2, row.Top + (row.Height - t.Height) / 2));
            }
        });

        Pointer(dc, K(p, (.04, 120), (.10, x + 16), (.18, x + 16), (.27, 208), (.40, 208), (.50, 175), (.66, 175), (.80, 120)),
            K(p, (.04, 60), (.10, 18), (.18, 18), (.27, 45), (.40, 45), (.50, 71), (.66, 71), (.80, 60)));
    }

    /// The coffee cup (open at the top, a handle on the right), filled from the bottom with the accent by `fill` (0–1).
    private void Cup(DrawingContext dc, double cx, double cy, double fill)
    {
        double l = cx - 7, t = cy - 5, r = l + 11, b = t + 10;
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(l, t), true, true);
            g.LineTo(new Point(l, b - 3), true, false);
            g.QuadraticBezierTo(new Point(l, b), new Point(l + 3, b), true, false);
            g.LineTo(new Point(r - 3, b), true, false);
            g.QuadraticBezierTo(new Point(r, b), new Point(r, b - 3), true, false);
            g.LineTo(new Point(r, t), true, false);
        }
        body.Freeze();
        if (fill > 0)
        {
            dc.PushClip(body);
            dc.DrawRectangle(B(Accent), null, new Rect(l, b - 10 * fill, 11, 10 * fill));
            dc.Pop();
        }
        dc.DrawGeometry(null, P(ink.Text, 1.1), body);
        dc.DrawRoundedRectangle(null, P(ink.Text, 1.1), new Rect(r - .5, t + 2, 4, 4.5), 2, 2);
    }

    /// Beside the terminal a Claude Code session runs in, its pill at the top glows while it works; an approval opens it
    /// into a card with the command, 허용 is clicked, it folds back into the pill, works on and finishes. The add-on's
    /// own words.
    private void RunCard(DrawingContext dc, double p)
    {
        double open = K(p, (.20, 0), (.28, 1), (.50, 1), (.58, 0));
        double done = K(p, (.70, 0), (.74, 1), (.93, 1), (.97, 0));

        // the terminal: its lines, the last one marked while it waits, one more once the work goes on
        var term = new Rect(14, 12, 72, 58);
        Window(dc, term);
        double lx = term.Left + 7, ly = term.Top + 9;
        foreach (double w in new[] { 40.0, 28.0 })
        {
            dc.DrawRoundedRectangle(B(ink.Track), null, new Rect(lx, ly, w, 3), 1.5, 1.5);
            ly += 8;
        }
        double wait = K(p, (.20, 0), (.24, 1), (.48, 1), (.52, 0));
        if (wait > 0) dc.DrawEllipse(B(Fade(Accent, wait)), null, new Point(lx + 1.5, ly + 1.5), 1.5, 1.5);
        dc.DrawRoundedRectangle(B(ink.Track), null, new Rect(lx + 6 * wait, ly, 34, 3), 1.5, 1.5);
        double more = K(p, (.58, 0), (.64, 1), (.93, 1), (.97, 0));
        if (more > 0) dc.DrawRoundedRectangle(B(Fade(ink.Track, more)), null, new Rect(lx, ly + 8, 46, 3), 1.5, 1.5);

        // the pill as wide as its words (working, then done), opening into the card
        var work = Lang.Tr("명령 실행 중", "Running command");
        var fin = Text(Lang.Tr("31초 만에 완료", "Done in 31s"), 7, FontWeights.SemiBold, ink.Text);
        var workText = Text(work, 7, FontWeights.SemiBold, ink.Text);
        var time = Text(done > 0 ? "0:31" : $"0:{8 + (int)(p * 30):00}", 6.5, FontWeights.Normal, ink.Faint);
        double pw = 3 + 12 + 4 + (workText.Width + (fin.Width - workText.Width) * done) + 6 + time.Width + 7;
        var card = new Rect(92, 6, 132, 56);
        var pill = new Rect(224 - pw, 8, pw, 18);
        var r = Lerp(pill, card, open);
        double radius = 9 - open;
        Shadow(dc, r, radius);
        dc.DrawRoundedRectangle(B(ink.Pill), P(ink.PillEdge, 1), r, radius, radius);

        double po = K(p, (.20, 1), (.23, 0), (.52, 0), (.57, 1));
        if (po > 0)
        {
            dc.PushOpacity(po);
            // in the pill's place, which the card covers as it folds back
            double cy = pill.Top + pill.Height / 2;
            var at = new Point(pill.Left + 3 + 12 + 4, cy - workText.Height / 2);
            if (done < 1)
            {
                dc.PushOpacity(1 - done);
                Mark(dc, pill.Left + 9, cy);
                Shine(dc, work, at, p);
                dc.Pop();
            }
            if (done > 0)
            {
                dc.PushOpacity(done);
                Done(dc, pill.Left + 9, cy);
                dc.DrawText(fin, at);
                dc.Pop();
            }
            dc.DrawText(time, new Point(pill.Right - 7 - time.Width, cy - time.Height / 2));
            dc.Pop();
        }

        // the card's words, inside its shape while it opens and closes
        double co = K(p, (.25, 0), (.30, 1), (.48, 1), (.52, 0));
        var allow = Text(Lang.Tr("허용", "Allow"), 6.5, FontWeights.SemiBold, AccentInk);
        var allowBox = new Rect(card.Left + 6, card.Top + 42, 4 + 7 + 2 + allow.Width + 5, 11);
        if (co > 0)
        {
            dc.PushClip(new RectangleGeometry(r, radius, radius));
            dc.PushOpacity(co);
            // the head: the mark, what it asks and its time when there is room for it
            var title = Text(Lang.Tr("승인 요청 · Bash", "Approval request · Bash"), 7, FontWeights.SemiBold, ink.Text);
            Mark(dc, card.Left + 9, card.Top + 10);
            dc.DrawText(title, new Point(card.Left + 19, card.Top + 10 - title.Height / 2));
            var clock = Text("0:14", 6.5, FontWeights.Normal, ink.Faint);
            if (card.Left + 19 + title.Width + 6 + clock.Width <= card.Right - 6)
                dc.DrawText(clock, new Point(card.Right - 6 - clock.Width, card.Top + 10 - clock.Height / 2));

            // the steps' bar, three of four done
            double seg = (card.Width - 12 - 3 * 2) / 4;
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(B(i < 3 ? ink.Steps : ink.Track), null, new Rect(card.Left + 6 + i * (seg + 2), card.Top + 20, seg, 1.5), .75, .75);

            var code = new Rect(card.Left + 6, card.Top + 26, card.Width - 12, 12);
            dc.DrawRoundedRectangle(B(ink.Code), null, code, 3, 3);
            var cmd = Words("npm test", 6, FontWeights.Normal, ink.Text, Theme.Mono);
            dc.DrawText(cmd, new Point(code.Left + 4, code.Top + (code.Height - cmd.Height) / 2));

            // 허용 (pressed once), 거절, and the way to the terminal at the end
            double press = K(p, (.44, 0), (.45, 1), (.47, 1), (.49, 0));
            dc.DrawRoundedRectangle(B(Mix(Accent, 0xFFFFFFFF, .22 * press)), null, allowBox, 5.5, 5.5);
            ButtonFace(dc, allowBox, '\uE73E', allow, AccentInk);
            var deny = Text(Lang.Tr("거절", "Deny"), 6.5, FontWeights.SemiBold, ink.Text);
            var denyBox = new Rect(allowBox.Right + 3, allowBox.Top, 4 + 7 + 2 + deny.Width + 5, 11);
            dc.DrawRoundedRectangle(B(ink.Hover), null, denyBox, 5.5, 5.5);
            ButtonFace(dc, denyBox, '\uE711', deny, ink.Text);
            var away = Text(Lang.Tr("터미널에서 ↗", "In terminal ↗"), 6, FontWeights.Normal, ink.Faint);
            dc.DrawText(away, new Point(card.Right - 6 - away.Width, allowBox.Top + (11 - away.Height) / 2));
            dc.Pop();
            dc.Pop();
        }

        // the pointer comes to 허용's icon, so its word stays in sight
        double ax = allowBox.Left + 6, ay = allowBox.Top + 5;
        Pointer(dc, K(p, (.28, 150), (.40, ax), (.50, ax), (.62, 150)), K(p, (.28, 64), (.40, ay), (.50, ay), (.62, 64)));
    }

    /// A button's icon and word, set in from its left.
    private void ButtonFace(DrawingContext dc, Rect box, char icon, FormattedText word, uint argb)
    {
        var g = Glyph(icon, 5.5, argb);
        dc.DrawText(g, new Point(box.Left + 4 + (7 - g.Width) / 2, box.Top + (box.Height - g.Height) / 2));
        dc.DrawText(word, new Point(box.Left + 4 + 7 + 2, box.Top + (box.Height - word.Height) / 2));
    }

    /// The session's mark: a spark in a soft round.
    private void Mark(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(B(ink.Hover), null, new Point(cx, cy), 6, 6);
        var pen = Round(ink.Text, 1.1);
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 4, dx = 3.2 * Math.Cos(a), dy = 3.2 * Math.Sin(a);
            dc.DrawLine(pen, new Point(cx - dx, cy - dy), new Point(cx + dx, cy + dy));
        }
    }

    /// Done: a check in a ring, in the theme's OK colour.
    private void Done(DrawingContext dc, double cx, double cy)
    {
        uint ok = Argb(Theme.P.Ok.Color);
        var pen = Round(ok, 1.1);
        dc.DrawEllipse(null, pen, new Point(cx, cy), 5, 5);
        dc.DrawLine(pen, new Point(cx - 2.3, cy + .1), new Point(cx - .6, cy + 1.8));
        dc.DrawLine(pen, new Point(cx - .6, cy + 1.8), new Point(cx + 2.4, cy - 1.6));
    }

    /// A working headline: its resting tone with light swept across it every 1.6 s.
    private void Shine(DrawingContext dc, string s, Point at, double p)
    {
        var rest = Text(s, 7, FontWeights.SemiBold, ink.Shine);
        var lit = Text(s, 7, FontWeights.SemiBold, ink.Text);
        dc.DrawText(rest, at);
        double sweep = p * Loop % 1.6 / 1.6, x = at.X - 16 + (rest.Width + 32) * sweep;
        shine ??= new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.White, .5), new GradientStop(Colors.Transparent, 1) },
        };
        shine.StartPoint = new Point(x - 8, 0);
        shine.EndPoint = new Point(x + 8, 0);
        dc.PushOpacityMask(shine);
        dc.DrawText(lit, at);
        dc.Pop();
    }

    /// The sweep's mask, moved for each frame (one per picture, never frozen).
    private LinearGradientBrush? shine;

    /// The right end of the pill at the top: the brightness widget (a monitor with its number), the RAM widget (a memory
    /// module with the use) and the clock. `on`: the widget whose window is open.
    private void Tray(DrawingContext dc, double p, int on, double bright, double use)
    {
        var clk = Text("2:37", 8, FontWeights.SemiBold, ink.Text);
        double w = 4 + 27 + 4 + 27 + 4 + 3 + clk.Width + 9, x = 224 - w;
        var seg = new Rect(x, 6, w, 22);
        Shadow(dc, seg, 11);
        dc.DrawRoundedRectangle(B(ink.Pill), P(ink.PillEdge, 1), seg, 11, 11);
        bool lit = p >= .12 && p < .76;
        for (int i = 0; i < 2; i++)
        {
            var wd = new Rect(x + 4 + i * 31, 8, 27, 18);
            if (lit && i == on) dc.DrawRoundedRectangle(B(ink.Hover), null, wd, 5, 5);
            Widget(dc, wd.Left + 3, 10, i == 0, i == 0 ? bright : use);
        }
        dc.DrawText(clk, new Point(x + 4 + 62 + 3, 17 - clk.Height / 2));
    }

    private void Widget(DrawingContext dc, double x, double y, bool monitor, double pct)
    {
        var box = new Rect(x, y, 21, 11);
        var round = new RectangleGeometry(box, 3, 3);
        dc.PushClip(round);
        dc.DrawRectangle(B(Fade(ink.Text, .25)), null, new Rect(x, y, 21 * pct / 100, 11));
        dc.Pop();
        box.Inflate(-.55, -.55);
        dc.DrawRoundedRectangle(null, P(ink.Text, 1.1), box, 2.5, 2.5);
        if (monitor)
        {
            dc.DrawRectangle(B(ink.Text), null, new Rect(x + 9.5, y + 10, 2, 3));
            dc.DrawRoundedRectangle(B(ink.Text), null, new Rect(x + 6.5, y + 12.6, 8, 1.2), .6, .6);
        }
        else
            for (double t = x + 4; t < x + 17; t += 2.6)
                dc.DrawRectangle(B(ink.Text), null, new Rect(t, y + 10.5, 1.4, 2.4));
        var n = Text($"{Math.Round(pct)}", 7.5, FontWeights.Bold, ink.Text);
        dc.DrawText(n, new Point(x + (21 - n.Width) / 2, y + (11 - n.Height) / 2));
    }

    /// The window a widget opens under the pill (right-aligned to it), fading and dropping in; `body` draws what is in
    /// it from the given top.
    private void Fly(DrawingContext dc, double p, double height, Action<double> body)
    {
        double o = K(p, (.12, 0), (.18, 1), (.72, 1), (.78, 0));
        if (o <= 0) return;
        var r = new Rect(96, 33, 128, height);
        dc.PushOpacity(o);
        dc.PushTransform(new TranslateTransform(0, -4 * (1 - o)));
        dc.PushTransform(new ScaleTransform(.96 + .04 * o, .96 + .04 * o, r.Left + r.Width * .8, r.Top));
        Shadow(dc, r, 8);
        dc.DrawRoundedRectangle(B(ink.Pill), P(ink.PillEdge, 1), r, 8, 8);
        body(r.Top + 5);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private void Bar(DrawingContext dc, double y, double pct)
    {
        dc.DrawRoundedRectangle(B(ink.Track), null, new Rect(103, y, 114, 3), 1.5, 1.5);
        dc.DrawRoundedRectangle(B(Accent), null, new Rect(103, y, 114 * pct / 100, 3), 1.5, 1.5);
    }

    private void Window(DrawingContext dc, Rect r)
    {
        r.Inflate(-.5, -.5);
        dc.DrawRoundedRectangle(B(ink.Win), P(ink.WinLine, 1), r, 5, 5);
    }

    private void Shadow(DrawingContext dc, Rect r, double radius) => ArtKit.Shadow(dc, r, radius, ink.Shade);

    private void Pointer(DrawingContext dc, double x, double y) => ArtKit.Pointer(dc, x, y, ink.Paper, 0xFF111111, .8);

    // ---- colours per theme; the bars and Bloom's tile in the theme's accent

    private Ink ink = new(false, false);

    /// `Tile`: the tutorial's tiles, where nothing is pure white or black (the white things off-white, the shades a
    /// dark neutral); the lines of 설정 > 애드온 keep the values they have.
    private readonly record struct Ink(bool Dark, bool Tile)
    {
        public uint Pill => Dark ? 0xFF2E2E33u : 0xFFFAFAFAu;
        public uint PillEdge => Dark ? 0x14FFFFFFu : 0x1A111111u;
        public uint Shade => Tile ? (Dark ? 0x26101013u : 0x0F101013u) : Dark ? 0x26000000u : 0x0F000000u;
        public uint Icon => Dark ? 0xFF3F3F46u : Tile ? 0xFFF4F4F5u : 0xFFFFFFFFu;
        /// The pointer and a switch's knob.
        public uint Paper => !Tile ? 0xFFFFFFFFu : Dark ? 0xFFECEDEEu : 0xFFF4F4F5u;
        public uint Glyph => Dark ? 0xFFE4E4E7u : 0xFF3F3F46u;
        public uint Text => Dark ? 0xFFECEDEEu : 0xFF11181Cu;
        public uint Faint => Dark ? 0xFF97989Au : 0xFF71717Au;
        public uint Win => Dark ? 0x0AFFFFFFu : 0x0D111111u;
        public uint WinLine => Dark ? 0x12FFFFFFu : 0x12111111u;
        public uint Frame => Dark ? 0xFF3F3F46u : 0xFFD4D4D8u;
        public uint Screen => Dark ? 0xFF1C1C1Fu : 0xFFF7F7F8u;
        public uint Thumb => Dark ? 0xFF3A3A40u : 0xFFE4E4E7u;
        public uint Track => Dark ? 0x24FFFFFFu : 0x1F111111u;
        public uint Hover => Dark ? 0x1AFFFFFFu : 0x14111111u;
        public uint Shine => Dark ? 0xFFA1A1AAu : 0xFF5E5E67u;
        public uint Steps => Dark ? 0xBFFFFFFFu : 0xB3111111u;
        public uint Code => Dark ? Tile ? 0x47101013u : 0x47000000u : 0x0F111111u;
    }

    // ---- words, kept while the same words, size, colour and font come again

    private FormattedText Text(string s, double size, FontWeight weight, uint argb) => words.Text(s, size, weight, argb);

    private FormattedText Glyph(char g, double size, uint argb) => words.Glyph(g, size, argb);

    private FormattedText Words(string s, double size, FontWeight weight, uint argb, FontFamily family) => words.Of(s, size, weight, argb, family);
}
