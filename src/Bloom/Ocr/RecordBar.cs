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
/// while it records: a red dot, the time and 멈춤. With the island or Pill taskbar (RecordSurface) it is a pill that
/// floats clear of the taskbar as it is once all the way out: under it at the top, a little over it at the bottom, so
/// the taskbar coming out never covers it; with Windows' own taskbar it is a bar under the area. One transparent window
/// holds the shape, which changes size inside it; the window is kept out of screen captures, so it is never in the
/// video.
internal sealed class RecordBar : Window
{
    public event Action? StartClicked, CancelClicked, StopClicked;

    /// The sounds picked: (system, microphone).
    public event Action<bool, bool>? SoundsChanged;

    public bool SystemAudio { get; private set; }
    public bool Mic { get; private set; }

    private static readonly Duration Open = TimeSpan.FromMilliseconds(200), Fast = TimeSpan.FromMilliseconds(160);
    private static readonly IEasingFunction Ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }; // close to the design's cubic-bezier(0.22, 1, 0.36, 1)

    /// Between the taskbar's edge and the pill (DIP).
    private const double Clear = 8;

    /// The window's room around the shape for its shadow (DIP): above, and below where the shadow falls.
    private const double Above = 4, Below = 32;

    private readonly RecordSurface surface;
    private readonly ScreenRect area;
    private readonly Palette ink;
    private readonly bool capsule; // the island's and the pill's bar is round at the ends; the plain bar is a 12-DIP card
    private readonly bool bottom; // the pill over a taskbar at the bottom: it keeps its bottom edge as it changes size
    private readonly double scale;
    private readonly SmoothBorder shape;
    private readonly Grid menu, rec;
    private readonly SmoothBorder systemKey, micKey;
    private readonly TextBlock time;
    private readonly Size menuSize, recSize;
    private readonly Stopwatch clock = new();
    private readonly List<FrameworkElement> dots = new(); // the recording row's dot, which beats
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
        bottom = surface.Kind == RecordSurfaceKind.Pill;
        scale = ScaleAt(Anchor(surface, area));

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

        var layers = new Grid { Margin = new Thickness(capsule ? 8 : 5, 0, capsule ? 8 : 5, 0) };
        layers.Children.Add(menu);
        layers.Children.Add(rec);
        shape = new SmoothBorder
        {
            Background = ink.Popup,
            BorderBrush = ink.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = layers,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            Margin = bottom ? new Thickness(0, 0, 0, Below) : new Thickness(0, Above, 0, 0),
            Effect = Shadow(ink.Dark),
        };
        if (capsule) shape.SizeChanged += (_, _) => shape.CornerRadius = new CornerRadius(shape.ActualHeight / 2);

        menuSize = Measure(menu, capsule ? 48 : 42);
        recSize = Measure(rec, capsule ? 40 : 42);
        Content = shape;

        // the window: wide and tall enough for every shape it takes, with room for the shadow
        Width = Math.Max(menuSize.Width, recSize.Width) + 48;
        Height = menuSize.Height + Above + Below;

        // where it starts: nothing shown yet, the menu's size; it fades in
        shape.Width = menuSize.Width;
        shape.Height = menuSize.Height;
        shape.Opacity = 0;
        menu.Opacity = 0;
        rec.Opacity = 0;
        rec.IsHitTestVisible = false;

        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            Glass.ToolWindow(this);
            SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
            // Pill taskbar: the pointer on the bar doesn't bring the hidden taskbar out (the island and the edge stay as
            // they are); gone with the window
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
                if (rec.IsHitTestVisible) StopClicked?.Invoke();
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

    /// The countdown: the shape takes its recording look, the time at 00:00, faint.
    public void Counting()
    {
        menu.IsHitTestVisible = false;
        Fade(menu, 0);
        time.Opacity = 0.5;
        Grow(recSize);
        rec.IsHitTestVisible = true;
        Fade(rec, 1);
    }

    /// Recording from now: the time runs, the dot beats.
    public void Recording()
    {
        clock.Restart();
        time.Opacity = 1;
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
        rec.IsHitTestVisible = false;
        foreach (var dot in dots) dot.BeginAnimation(OpacityProperty, null);
    }

    public TimeSpan Elapsed => clock.Elapsed;

    /// Fades out and is gone.
    public async Task Leave()
    {
        tick.Stop();
        menu.IsHitTestVisible = rec.IsHitTestVisible = false;
        Fade(menu, 0);
        Fade(rec, 0);
        Fade(shape, 0);
        await Task.Delay(Open.TimeSpan + TimeSpan.FromMilliseconds(40));
        Close();
    }

    private void ShowMenu()
    {
        Fade(shape, 1);
        Grow(menuSize);
        menu.IsHitTestVisible = true;
        Fade(menu, 1);
    }

    private void ShowTime()
    {
        var t = clock.Elapsed;
        time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) : t.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
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

    /// The point the window is placed by (physical): the middle of the island or pill at the taskbar's edge once it is
    /// out, the area's bottom middle for the plain bar.
    private static Point Anchor(RecordSurface s, ScreenRect area) => s.Kind switch
    {
        RecordSurfaceKind.Island or RecordSurfaceKind.Pill => new Point(s.Box.X + s.Box.Width / 2, s.Edge),
        _ => new Point(area.X + area.Width / 2.0, area.Y + area.Height),
    };

    private void Place()
    {
        IntPtr h = new WindowInteropHelper(this).Handle;
        int w = (int)Math.Round(Width * scale), hgt = (int)Math.Round(Height * scale);
        var at = Anchor(surface, area);
        int x = (int)Math.Round(at.X - w / 2.0), y;
        if (capsule) y = FloatTop(bottom, at.Y, scale, hgt);
        else
        {
            var work = WorkArea(at);
            int bar = (int)Math.Round((menuSize.Height + 8) * scale), gap = (int)Math.Round(6 * scale);
            y = PlainTop(area, work, bar, gap) - (int)Math.Round(Above * scale);
            x = Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - w));
        }
        Win32.SetWindowPos(h, Win32.HWND_TOPMOST, x, y, w, hgt, Win32.SWP_NOACTIVATE);
    }

    /// The floating pill's window top (physical) for a taskbar edge at `edge`: the shape `Clear` DIPs under a taskbar at
    /// the top, or that much over one at the bottom; `height` is the window's, shadow room and all.
    internal static int FloatTop(bool bottom, double edge, double scale, int height) => bottom
        ? (int)Math.Round(edge + (Below - Clear) * scale) - height
        : (int)Math.Round(edge + (Clear - Above) * scale);

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
