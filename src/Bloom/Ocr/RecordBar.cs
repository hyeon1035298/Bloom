using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Bloom.Native;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Ocr;

/// 화면 녹화's bar. Before it starts: 녹화 시작, the two sounds (시스템 소리 · 마이크, kept for next time) and cancel;
/// while it records: a red dot, the time and 멈춤. It grows out of what is there (RecordSurface): the island turns
/// into it at the top and shrinks back to an island that shows the time; the taskbar's pill turns into it at the
/// bottom and, once recording, gives the taskbar back with a small recording pill above it; with Windows' own taskbar
/// it is a bar under the area. One transparent window holds the shape, which changes size inside it; the window is
/// kept out of screen captures, so it is never in the video.
internal sealed class RecordBar : Window
{
    public event Action? StartClicked, CancelClicked, StopClicked;

    /// The sounds picked: (system, microphone).
    public event Action<bool, bool>? SoundsChanged;

    public bool SystemAudio { get; private set; }
    public bool Mic { get; private set; }

    private static readonly Duration Open = TimeSpan.FromMilliseconds(200), Fast = TimeSpan.FromMilliseconds(160);
    private static readonly IEasingFunction Ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }; // close to the design's cubic-bezier(0.22, 1, 0.36, 1)

    private readonly RecordSurface surface;
    private readonly ScreenRect area;
    private readonly Palette ink;
    private readonly bool capsule; // the island and the pill are round at the ends; the plain bar is a 12-DIP card
    private readonly double scale;
    private readonly Size from; // the island's or pill's own size (DIPs), where the shape starts and ends
    private readonly SmoothBorder shape, chip;
    private readonly Grid menu, rec, chipRec;
    private readonly SmoothBorder systemKey, micKey;
    private readonly TextBlock time, chipTime;
    private readonly Size menuSize, recSize;
    private readonly Stopwatch clock = new();
    private readonly List<FrameworkElement> dots = new(); // the recording rows' dots, which beat
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer raise = new() { Interval = TimeSpan.FromSeconds(1) };

    /// The window property Pill taskbar reads (PointerOnHolder in its mod).
    private const string HoldRevealProp = "PillTaskbar.HoldReveal";

    public RecordBar(RecordSurface surface, ScreenRect area, Palette ink, bool systemAudio, bool mic)
    {
        this.surface = surface;
        this.area = area;
        this.ink = ink;
        SystemAudio = systemAudio;
        Mic = mic;
        capsule = surface.Kind != RecordSurfaceKind.Plain;
        scale = ScaleAt(Anchor(surface, area));
        from = capsule ? new Size(surface.Box.Width / scale, surface.Box.Height / scale) : new Size(0, 42);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        UseLayoutRounding = true;

        systemKey = Key(Tr("시스템 소리", "System sound"), () => SystemAudio, v => SystemAudio = v);
        micKey = Key(Tr("마이크", "Microphone"), () => Mic, v => Mic = v);
        menu = Row(StartButton(), Faint(Tr("3초 뒤", "In 3 s"), new Thickness(6, 0, 6, 0)), Separator(), systemKey, Gap(4), micKey, Separator(),
            IconButton(G.Close, Tr("취소 (Esc)", "Cancel (Esc)"), () => CancelClicked?.Invoke()));
        time = Time();
        rec = RecordingRow(time, !capsule);
        chipTime = Time();
        chipRec = RecordingRow(chipTime, false);

        var layers = new Grid { Margin = new Thickness(capsule ? 8 : 5, 0, capsule ? 8 : 5, 0) };
        layers.Children.Add(menu);
        layers.Children.Add(rec);
        shape = new SmoothBorder
        {
            Background = surface.Kind == RecordSurfaceKind.Island ? IslandFill(ink) : ink.Popup,
            BorderBrush = surface.Kind == RecordSurfaceKind.Island ? Brushes.Transparent : ink.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = layers,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = Shadow(ink.Dark),
        };
        if (capsule) shape.SizeChanged += (_, _) => shape.CornerRadius = new CornerRadius(shape.ActualHeight / 2);

        // the pill's recording chip, above the taskbar it gave back
        chip = new SmoothBorder
        {
            Background = ink.Popup,
            BorderBrush = ink.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Height = 36,
            Padding = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = chipRec,
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = Shadow(ink.Dark),
            RenderTransform = new TranslateTransform(0, 6),
        };

        menuSize = Measure(menu, capsule ? (surface.Kind == RecordSurfaceKind.Island ? 48 : 52) : 42);
        recSize = Measure(rec, capsule ? Math.Max(from.Height, 35) : 42);
        var chipSize = Measure(chipRec, 36);

        var root = new Grid();
        root.Children.Add(shape);
        if (surface.Kind == RecordSurfaceKind.Pill) root.Children.Add(chip);
        Content = root;

        // the window: wide and tall enough for every shape it takes, with room for the shadow
        double width = Math.Max(Math.Max(menuSize.Width, recSize.Width), Math.Max(from.Width, chipSize.Width)) + 48;
        double height = surface.Kind switch
        {
            RecordSurfaceKind.Pill => Math.Max(menuSize.Height, from.Height) + 8 + chipSize.Height + 24,
            RecordSurfaceKind.Island => Math.Max(menuSize.Height, from.Height) + 28,
            _ => menuSize.Height + 36,
        };
        Width = width;
        Height = height;
        switch (surface.Kind)
        {
            case RecordSurfaceKind.Pill:
                shape.VerticalAlignment = VerticalAlignment.Bottom;
                chip.Margin = new Thickness(0, 0, 0, from.Height + 8);
                break;
            case RecordSurfaceKind.Island:
                shape.VerticalAlignment = VerticalAlignment.Top;
                break;
            default:
                shape.VerticalAlignment = VerticalAlignment.Top;
                shape.Margin = new Thickness(0, 4, 0, 0);
                break;
        }

        // where it starts: the island's or pill's own shape and colour, nothing in it yet; the plain bar fades in
        shape.Width = capsule ? from.Width : menuSize.Width;
        shape.Height = capsule ? from.Height : menuSize.Height;
        menu.Opacity = 0;
        rec.Opacity = 0;
        rec.IsHitTestVisible = false;
        if (!capsule) shape.Opacity = 0;

        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            Glass.ToolWindow(this);
            SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
            // Pill taskbar: the pointer on the bar doesn't bring the hidden taskbar out over it (the island and the
            // edge stay as they are); gone with the window
            SetPropW(h, HoldRevealProp, new IntPtr(1));
            Place();
            raise.Start();
        };
        // the taskbar and its island come to the front as they change: the bar keeps above them
        raise.Tick += (_, _) => Win32.SetWindowPos(new WindowInteropHelper(this).Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Closing += (_, _) => RemovePropW(new WindowInteropHelper(this).Handle, HoldRevealProp);
        Closed += (_, _) => raise.Stop();
        // moved onto a monitor of another scale, WPF resizes it by the ratio: put it back where it belongs
        DpiChanged += (_, _) => Place();
        Loaded += (_, _) => ShowMenu();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (rec.IsHitTestVisible || chip.IsHitTestVisible) StopClicked?.Invoke();
                else CancelClicked?.Invoke();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter && menu.IsHitTestVisible)
            {
                StartClicked?.Invoke();
                e.Handled = true;
            }
        };
        tick.Tick += (_, _) => ShowTime();
        Closed += (_, _) => tick.Stop();
    }

    /// The countdown: the shape takes its recording look (the time at 00:00, faint), the pill gives the taskbar back.
    public void Counting()
    {
        menu.IsHitTestVisible = false;
        Fade(menu, 0);
        time.Opacity = chipTime.Opacity = 0.5;
        if (surface.Kind == RecordSurfaceKind.Pill)
        {
            Grow(from);
            Fade(shape, 0);
            shape.IsHitTestVisible = false;
            chip.IsHitTestVisible = true;
            Fade(chip, 1);
            ((TranslateTransform)chip.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Open) { EasingFunction = Ease });
        }
        else
        {
            Grow(recSize);
            rec.IsHitTestVisible = true;
            Fade(rec, 1);
        }
    }

    /// Recording from now: the time runs, the dot beats.
    public void Recording()
    {
        clock.Restart();
        time.Opacity = chipTime.Opacity = 1;
        ShowTime();
        tick.Start();
        foreach (var dot in dots)
            dot.BeginAnimation(OpacityProperty, Motion.Moves
                ? new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(1200)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Ease }
                : null);
    }

    /// The recording is being finished: the time stops, 멈춤 can't be pressed twice.
    public void Saving()
    {
        tick.Stop();
        rec.IsHitTestVisible = chip.IsHitTestVisible = false;
        foreach (var dot in dots) dot.BeginAnimation(OpacityProperty, null);
    }

    public TimeSpan Elapsed => clock.Elapsed;

    /// Back into what it came from (the island shrinks to its own size) and gone.
    public async Task Leave()
    {
        tick.Stop();
        menu.IsHitTestVisible = rec.IsHitTestVisible = chip.IsHitTestVisible = false;
        Fade(menu, 0);
        Fade(rec, 0);
        Fade(chip, 0);
        if (capsule && surface.Kind == RecordSurfaceKind.Island) Grow(from);
        else Fade(shape, 0);
        await Task.Delay(Open.TimeSpan + TimeSpan.FromMilliseconds(40));
        Close();
    }

    private void ShowMenu()
    {
        if (!capsule) Fade(shape, 1);
        else shape.Opacity = 1;
        Grow(menuSize);
        menu.IsHitTestVisible = true;
        Fade(menu, 1);
    }

    private void ShowTime()
    {
        var t = clock.Elapsed;
        string text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) : t.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        time.Text = chipTime.Text = text;
    }

    private void Grow(Size to)
    {
        shape.BeginAnimation(WidthProperty, Motion.Moves ? new DoubleAnimation(to.Width, Open) { EasingFunction = Ease } : null);
        shape.BeginAnimation(HeightProperty, Motion.Moves ? new DoubleAnimation(to.Height, Open) { EasingFunction = Ease } : null);
        if (!Motion.Moves)
        {
            shape.Width = to.Width;
            shape.Height = to.Height;
        }
    }

    private static void Fade(UIElement e, double to)
    {
        if (Motion.Moves) e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Fast) { EasingFunction = Ease });
        else
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = to;
        }
    }

    // ── where it goes ──────────────────────────────────────────────────────────────────────────

    /// The point the window is placed by (physical): the island's top middle, the pill's bottom middle, the area's bottom
    /// middle for the plain bar.
    private static Point Anchor(RecordSurface s, ScreenRect area) => s.Kind switch
    {
        RecordSurfaceKind.Island => new Point(s.Box.X + s.Box.Width / 2, s.Box.Y),
        RecordSurfaceKind.Pill => new Point(s.Box.X + s.Box.Width / 2, s.Box.Bottom),
        _ => new Point(area.X + area.Width / 2.0, area.Y + area.Height),
    };

    private void Place()
    {
        IntPtr h = new WindowInteropHelper(this).Handle;
        int w = (int)Math.Round(Width * scale), hgt = (int)Math.Round(Height * scale);
        var at = Anchor(surface, area);
        int x = (int)Math.Round(at.X - w / 2.0), y;
        switch (surface.Kind)
        {
            case RecordSurfaceKind.Island:
                y = (int)Math.Round(at.Y);
                break;
            case RecordSurfaceKind.Pill:
                y = (int)Math.Round(at.Y) - hgt;
                break;
            default:
                var work = WorkArea(at);
                int bar = (int)Math.Round((menuSize.Height + 8) * scale), gap = (int)Math.Round(6 * scale);
                y = PlainTop(area, work, bar, gap) - (int)Math.Round(4 * scale);
                x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - w));
                break;
        }
        Win32.SetWindowPos(h, Win32.HWND_TOPMOST, x, y, w, hgt, Win32.SWP_NOACTIVATE);
    }

    /// The plain bar's top (physical): under the area, else over it, else inside its bottom, inside the work area.
    internal static int PlainTop(ScreenRect area, ScreenRect work, int bar, int gap)
    {
        int below = area.Y + area.Height + gap, above = area.Y - gap - bar;
        if (below + bar <= work.Y + work.Height) return below;
        if (above >= work.Y) return above;
        return Math.Max(work.Y, area.Y + area.Height - gap - bar);
    }

    private static ScreenRect WorkArea(Point at)
    {
        IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT { X = (int)at.X, Y = (int)Math.Min(at.Y, at.Y - 1) }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref info);
        var r = info.rcWork;
        return new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static double ScaleAt(Point at)
    {
        IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT { X = (int)at.X, Y = (int)at.Y - 1 }, Win32.MONITOR_DEFAULTTONEAREST);
        return Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
    }

    // ── the parts ──────────────────────────────────────────────────────────────────────────────

    private static Size Measure(FrameworkElement content, double height)
    {
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(Math.Ceiling(content.DesiredSize.Width) + 2 * 8 + 2, height);
    }

    private static Grid Row(params UIElement[] parts)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var p in parts) sp.Children.Add(p);
        var g = new Grid();
        g.Children.Add(sp);
        return g;
    }

    /// The dot, the time, the sounds being taken and 멈춤 (a button with its name on the plain bar, an icon on the island
    /// and the pill's chip).
    private Grid RecordingRow(TextBlock clockText, bool named)
    {
        var dot = Dot(10);
        dot.Margin = new Thickness(6, 0, 8, 0);
        dots.Add(dot);
        var parts = new List<UIElement> { dot, clockText };
        if (SystemAudio) parts.Add(SoundGlyph(G.Volume, Tr("시스템 소리 녹음 중", "Recording system sound")));
        if (Mic) parts.Add(SoundGlyph(Microphone, Tr("마이크 녹음 중", "Recording the microphone")));
        if (named)
        {
            parts.Add(Separator());
            parts.Add(TextButton(G.Stop, Tr("멈춤", "Stop"), () => StopClicked?.Invoke(), 28));
        }
        else parts.Add(IconButton(G.Stop, Tr("멈춤", "Stop"), () => StopClicked?.Invoke()));
        return Row(parts.ToArray());
    }

    private SmoothBorder Dot(double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = ink.DangerFill,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private TextBlock Time()
    {
        var t = Theme.T("00:00", 13, ink.Text, Theme.Semi, false);
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        t.MinWidth = 40;
        t.Margin = new Thickness(0, 0, 4, 0);
        return t;
    }

    private FrameworkElement SoundGlyph(char glyph, string tip)
    {
        var g = Theme.Glyph(glyph, 12, ink.TextFaint);
        g.Margin = new Thickness(2, 0, 4, 0);
        g.ToolTip = tip;
        return g;
    }

    private SmoothBorder StartButton()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = Dot(9);
        dot.Margin = new Thickness(0, 0, 7, 0);
        sp.Children.Add(dot);
        sp.Children.Add(Theme.Label(Tr("녹화 시작", "Start recording"), 13, ink.Text, Theme.Medium, inBorder: true));
        var b = ButtonLook.Apply(new SmoothBorder
        {
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(capsule ? 16 : 8),
            Cursor = Cursors.Hand,
            Child = sp,
            ToolTip = Tr("시작 (Enter) · 3초 뒤 녹화합니다", "Start (Enter) · records in 3 s"),
        }, ink);
        b.MouseLeftButtonUp += (_, e) =>
        {
            StartClicked?.Invoke();
            e.Handled = true;
        };
        return b;
    }

    /// A sound to take: on = a filled key with a check, off = an outline (Bloom's 글자 키).
    private SmoothBorder Key(string name, Func<bool> on, Action<bool> set)
    {
        var check = Theme.Glyph(G.Check, 11, ink.Text);
        check.Margin = new Thickness(0, 0, 6, 0);
        var label = Theme.Label(name, 12, ink.Text, Theme.Medium, inBorder: true);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(check);
        sp.Children.Add(label);
        var k = new SmoothBorder { Height = 28, CornerRadius = new CornerRadius(capsule ? 14 : 8), Cursor = Cursors.Hand, Child = sp, BorderThickness = new Thickness(1) };
        void Paint()
        {
            bool v = on();
            check.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            k.Padding = new Thickness(v ? 9 : 12, 0, 12, 0);
            k.Background = v ? ink.Btn : Brushes.Transparent;
            k.BorderBrush = v ? ink.BtnEdge : ink.Edge;
            k.TopLine = v ? ink.BtnTop : null;
            k.BottomLine = v ? ink.BtnBottom : null;
            label.Foreground = v ? ink.Text : ink.TextFaint;
        }
        Paint();
        k.MouseLeftButtonUp += (_, e) =>
        {
            set(!on());
            Paint();
            SoundsChanged?.Invoke(SystemAudio, Mic);
            e.Handled = true;
        };
        return k;
    }

    private SmoothBorder IconButton(char glyph, string tip, Action click)
    {
        var b = new SmoothBorder
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(capsule ? 14 : 8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = Theme.Glyph(glyph, 13, ink.TextDim),
        };
        b.MouseEnter += (_, _) => b.Background = ink.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    private SmoothBorder TextButton(char glyph, string text, Action click, double height)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(glyph, 12, ink.Text);
        g.Margin = new Thickness(0, 0, 6, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.Label(text, 12, ink.Text, Theme.Medium, inBorder: true));
        var b = ButtonLook.Apply(new SmoothBorder { Height = height, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Child = sp }, ink);
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    private TextBlock Faint(string text, Thickness margin)
    {
        var t = Theme.T(text, 12, ink.TextFaint, null, false);
        t.Margin = margin;
        return t;
    }

    private Border Separator() => new() { Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0), Background = ink.Line };

    private static Border Gap(double w) => new() { Width = w };

    private static Brush IslandFill(Palette p) => p.Dark ? Brushes.Black : Brushes.White;

    private static DropShadowEffect Shadow(bool dark) => new()
    {
        BlurRadius = 24,
        ShadowDepth = 8,
        Direction = 270,
        Color = Colors.Black,
        Opacity = dark ? 0.4 : 0.14,
    };

    private const char Microphone = '';

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetPropW(IntPtr hwnd, string name, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemovePropW(IntPtr hwnd, string name);
}
