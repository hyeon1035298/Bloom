using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Bloom.Search;

namespace Bloom.Ui;

/// The accent's colour picker (설정 > 일반 > 직접 고른 색), for the colour the theme shown wears: a saturation × value
/// square and a hue strip (drag, or ←↑↓→ with Shift for big steps), the HEX code, 화면에서 찍기, the recent picks, and the
/// dark / light pair each on its own ground with its contrast; under 3:1 a warning and 보이게 맞추기.
/// Moved follows every change as it happens; Picked says the colour is to be kept (let go, a key's pause, a code
/// applied, a pick). Both give the light theme's colour, the one the settings keep.
internal sealed class AccentPicker : Grid
{
    private const double SquareW = 260, SquareH = 156, StripH = 12;

    private readonly bool dark;
    private double h, s, v;
    private readonly Color first, firstDark, firstLight; // the colour it opened on and the pair kept for it
    private readonly SmoothBorder square, strip;
    private readonly Grid squarePad, stripPad;
    private readonly GradientStop squareHue;
    private readonly FrameworkElement squareThumb, stripThumb;
    private readonly SolidColorBrush squareThumbFill = new(), stripThumbFill = new();
    private readonly ColourField hex;
    private readonly Pair darkCell, lightCell;
    private readonly FrameworkElement warn;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(450) };

    /// The light colour, as it changes.
    public event Action<Color>? Moved;

    /// The light colour, to be kept.
    public event Action<Color>? Picked;

    /// 화면에서 찍기: a colour from the screen, or null when the pick was called off.
    public Func<Task<Color?>>? Eyedrop;

    /// Esc in the picker: the keyboard goes back where it came from.
    public Action? Done
    {
        set => hex.Done = done = value;
    }

    private Action? done;

    public (double h, double s, double v) Hsv => (h, s, v);

    public Color Shown => Ui.Hsv.To(h, s, v);

    /// The pair for the colour shown; while it is still the one it opened on, the pair it opened with (a preset's own,
    /// or a code kept that the rounding there and back would move by a step).
    public Color Light => Shown == first ? firstLight : Accents.Stored(Shown, dark);

    private Color Dark => Shown == first ? firstDark : Accents.DarkOf(Light);

    /// `pair`: the accent in use, for each theme; `at`: the place it was picked at (a gray keeps its hue).
    public AccentPicker((Color dark, Color light) pair, (double h, double s, double v)? at, bool dark, Color[] recent)
    {
        this.dark = dark;
        var shown = dark ? pair.dark : pair.light;
        (h, s, v) = at ?? Ui.Hsv.From(shown);
        first = Shown;
        (firstDark, firstLight) = pair;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SquareW) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // the square: white to the hue across, black over it downward
        squareHue = new GradientStop(Colors.Red, 1);
        var across = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        across.GradientStops.Add(new GradientStop(Colors.White, 0));
        across.GradientStops.Add(squareHue);
        var down = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
        down.Freeze();
        squareThumb = Thumb(squareThumbFill);
        squarePad = Pad(SquareH, 8, across, down, squareThumb);
        square = Focus(squarePad, 8, Tr("채도와 밝기", "Saturation and brightness"));
        Wire(squarePad, square, p =>
        {
            s = Math.Clamp(p.X / SquareW, 0, 1);
            v = Math.Clamp(1 - p.Y / SquareH, 0, 1);
        });
        square.Tag = (Func<Key, bool>)(key => Nudge(key, (dx, dy) =>
        {
            s = Math.Clamp(s + dx, 0, 1);
            v = Math.Clamp(v + dy, 0, 1);
        }, 0.01, 0.1));

        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (c, o) in new[] { (0xFF0000u, 0.0), (0xFFFF00u, 1 / 6.0), (0x00FF00u, 2 / 6.0), (0x00FFFFu, 0.5), (0x0000FFu, 4 / 6.0), (0xFF00FFu, 5 / 6.0), (0xFF0000u, 1.0) })
            hues.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), o));
        hues.Freeze();
        stripThumb = Thumb(stripThumbFill);
        stripPad = Pad(StripH, 6, hues, null, stripThumb);
        strip = Focus(stripPad, 6, Tr("색상", "Hue"));
        strip.Margin = new Thickness(-3, 9, -3, -3);
        Wire(stripPad, strip, p => h = Math.Clamp(p.X / SquareW * 360, 0, 359.9));
        strip.Tag = (Func<Key, bool>)(key => Nudge(key, (dx, dy) => h = Math.Clamp(h + (dx + dy) * 100, 0, 359.9), 0.01, 0.1));

        var left = new StackPanel();
        left.Children.Add(square);
        left.Children.Add(strip);
        Children.Add(left);

        // the side: the code and 화면에서 찍기, the reason a code was not taken, the recent picks, the pair, the warning
        var side = new StackPanel { MinWidth = 0 };
        SetColumn(side, 2);
        Children.Add(side);
        hex = new ColourField(Code(Shown), placeError: true);
        hex.Applied += code =>
        {
            if (!Accents.TryParse(code, out var c, out _)) return;
            Set(c);
            Picked?.Invoke(Light);
        };
        var drop = ToolUi.TextButton(Tr("화면에서 찍기", "Pick from screen"), async () =>
        {
            if (Eyedrop is null || await Eyedrop() is not { } c) return;
            Set(c);
            Picked?.Invoke(Light);
        });
        drop.Margin = new Thickness(0, 10, 0, 0);
        side.Children.Add(hex);
        hex.HorizontalAlignment = HorizontalAlignment.Left;
        hex.Error.TextWrapping = TextWrapping.Wrap;
        hex.Error.Margin = new Thickness(0, 6, 0, 0);
        side.Children.Add(hex.Error);
        side.Children.Add(drop);
        if (recent.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var label = Theme.T(Tr("최근 찍은 색", "Recent picks"), 12, Theme.P.TextFaint, Theme.Regular, false);
            label.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(label);
            foreach (var c in recent.Take(8))
            {
                var chip = new SmoothBorder
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(c),
                    BorderBrush = Theme.P.Edge,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = Code(c),
                };
                var cc = c;
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    Set(cc);
                    Picked?.Invoke(Light);
                    e.Handled = true;
                };
                row.Children.Add(chip);
            }
            side.Children.Add(row);
        }
        var cells = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        darkCell = new Pair(true, Tr("다크", "Dark"));
        lightCell = new Pair(false, Tr("라이트", "Light"));
        SetColumn(lightCell.Root, 2);
        cells.Children.Add(darkCell.Root);
        cells.Children.Add(lightCell.Root);
        side.Children.Add(cells);

        var warnLine = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        warnLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        warnLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        warnLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sign = Theme.Glyph(G.Warning, 12, Theme.P.Warn);
        sign.VerticalAlignment = VerticalAlignment.Center;
        sign.Margin = new Thickness(0, 1, 6, 0);
        warnLine.Children.Add(sign);
        var why = Theme.T(Core.Hangul.KeepAll(Tr("켠 스위치가 배경에 묻힙니다", "A switch that is on gets lost on its background")), 12, Theme.P.Warn, Theme.Regular, false);
        why.TextWrapping = TextWrapping.Wrap;
        SetColumn(why, 1);
        warnLine.Children.Add(why);
        var fix = ToolUi.TextButton(Tr("보이게 맞추기", "Make it stand out"), () =>
        {
            var light = Accents.MakeVisible(Light);
            Set(dark ? Accents.DarkOf(light) : light);
            Picked?.Invoke(Light);
        });
        fix.Margin = new Thickness(10, 0, 0, 0);
        SetColumn(fix, 2);
        warnLine.Children.Add(fix);
        warn = warnLine;
        side.Children.Add(warn);

        settle.Tick += (_, _) =>
        {
            settle.Stop();
            Picked?.Invoke(Light);
        };
        Paint();
    }

    /// Which part has the keyboard ("square", "strip", "hex"), to give it back after the settings are drawn again.
    public string? FocusedPart => square.IsKeyboardFocused ? "square" : strip.IsKeyboardFocused ? "strip" : hex.Typing ? "hex" : null;

    public void FocusPart(string? part)
    {
        switch (part)
        {
            case "square":
                Keyboard.Focus(square);
                break;
            case "strip":
                Keyboard.Focus(strip);
                break;
            case "hex":
                hex.FocusBox();
                break;
        }
    }

    /// A colour from outside the square (a code, a pick, the fix): its place, then everything drawn for it.
    private void Set(Color c)
    {
        var (nh, ns, nv) = Ui.Hsv.From(c);
        if (ns > 0.001 && nv > 0.001) h = nh; // a gray keeps the hue it had
        if (nv > 0.001) s = ns;
        v = nv;
        Paint();
        Moved?.Invoke(Light);
    }

    private void Paint()
    {
        var shown = Shown;
        squareHue.Color = Ui.Hsv.To(h, 1, 1);
        stripThumbFill.Color = squareHue.Color;
        squareThumbFill.Color = shown;
        Canvas.SetLeft(squareThumb, s * SquareW - 9);
        Canvas.SetTop(squareThumb, (1 - v) * SquareH - 9);
        Canvas.SetLeft(stripThumb, h / 360 * SquareW - 9);
        Canvas.SetTop(stripThumb, StripH / 2 - 9);
        hex.Show(Code(shown));
        var (light, darkC) = (Light, Dark);
        double cd = Accents.Contrast(darkC, Accents.DarkGround), cl = Accents.Contrast(light, Accents.LightGround);
        darkCell.Show(darkC, cd);
        lightCell.Show(light, cl);
        warn.Visibility = cd < Accents.MinContrast || cl < Accents.MinContrast ? Visibility.Visible : Visibility.Collapsed;
    }

    /// A change from the pointer or the keys: drawn, and the accent follows.
    private void Changed()
    {
        Paint();
        Moved?.Invoke(Light);
    }

    /// ←→ and ↑↓ move by `step` (Shift: `big`); a key's colour is kept once the keys pause.
    private bool Nudge(Key key, Action<double, double> by, double step, double big)
    {
        double d = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? big : step;
        switch (key)
        {
            case Key.Left: by(-d, 0); break;
            case Key.Right: by(d, 0); break;
            case Key.Up: by(0, d); break;
            case Key.Down: by(0, -d); break;
            case Key.Escape:
                done?.Invoke();
                return true;
            default: return false;
        }
        Changed();
        settle.Stop();
        settle.Start();
        return true;
    }

    /// Press anywhere on `pad` and drag: the colour follows; letting go keeps it.
    private void Wire(Grid pad, SmoothBorder focus, Action<Point> at)
    {
        pad.MouseLeftButtonDown += (_, e) =>
        {
            Keyboard.Focus(focus);
            pad.CaptureMouse();
            at(e.GetPosition(pad));
            Changed();
            e.Handled = true;
        };
        pad.MouseMove += (_, e) =>
        {
            if (!pad.IsMouseCaptured) return;
            at(e.GetPosition(pad));
            Changed();
        };
        pad.MouseLeftButtonUp += (_, e) =>
        {
            if (!pad.IsMouseCaptured) return;
            pad.ReleaseMouseCapture();
            e.Handled = true;
        };
        pad.LostMouseCapture += (_, _) =>
        {
            settle.Stop();
            Picked?.Invoke(Light);
        };
    }

    /// A gradient pad with its thumb on top (which may hang over its edges).
    private static Grid Pad(double height, double radius, Brush fill, Brush? over, FrameworkElement thumb)
    {
        var g = new Grid { Width = SquareW, Height = height, Background = Brushes.Transparent };
        g.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(radius), Background = fill });
        if (over is not null) g.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(radius), Background = over });
        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(thumb);
        g.Children.Add(canvas);
        return g;
    }

    /// The ring a pad wears while it has the keyboard; the pad answers ←↑↓→ through its Tag (SettingsView.HandleKey).
    private static SmoothBorder Focus(Grid pad, double radius, string name)
    {
        var b = new SmoothBorder
        {
            CornerRadius = new CornerRadius(radius + 3),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(1.5),
            Margin = new Thickness(-3),
            Focusable = true,
            FocusVisualStyle = null,
            Cursor = Cursors.Cross,
            Child = pad,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.GotKeyboardFocus += (_, _) => b.BorderBrush = Theme.P.Text;
        b.LostKeyboardFocus += (_, _) => b.BorderBrush = Brushes.Transparent;
        return b;
    }

    /// A ring in white with a faint dark edge, filled with `fill`.
    private static FrameworkElement Thumb(Brush fill)
    {
        var g = new Grid { Width = 18, Height = 18 };
        g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), StrokeThickness = 1 });
        g.Children.Add(new Ellipse { Margin = new Thickness(1), Stroke = Brushes.White, StrokeThickness = 2, Fill = fill });
        return g;
    }

    private static string Code(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// One theme's colour on that theme's own ground: its name and code, a switch and a key that are on, the contrast.
    private sealed class Pair
    {
        public readonly SmoothBorder Root;
        private readonly Palette p = new();
        private readonly SolidColorBrush accent = new();
        private readonly TextBlock code, contrast;

        public Pair(bool dark, string name)
        {
            p.Apply(dark, null, 0);
            var top = new DockPanel();
            var title = Theme.T(name, 12, p.Text, Theme.Medium);
            code = Theme.T("", 12, p.TextFaint, Theme.Regular);
            code.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(title, Dock.Left);
            top.Children.Add(title);
            top.Children.Add(code);

            // a switch that is on: its outline, the lit line it came from, the filled knob at the right
            var track = new SmoothBorder { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = p.Edge };
            var inside = new Canvas();
            var lit = new Rectangle { Width = 10, Height = 2, RadiusX = 1, RadiusY = 1, Fill = accent };
            Canvas.SetLeft(lit, 4);
            Canvas.SetTop(lit, 7);
            var knob = new Ellipse { Width = 12, Height = 12, Fill = accent };
            Canvas.SetLeft(knob, 16);
            Canvas.SetTop(knob, 2);
            inside.Children.Add(lit);
            inside.Children.Add(knob);
            track.Child = inside;
            // a key that is on: plain text over a lit line
            var key = new Grid { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var word = Theme.T(Tr("켬", "On"), 12, p.Text, Theme.Medium);
            word.Margin = new Thickness(0, 0, 0, 4);
            key.Children.Add(word);
            key.Children.Add(new Rectangle { Height = 2, RadiusX = 1, RadiusY = 1, Fill = accent, VerticalAlignment = VerticalAlignment.Bottom });
            var demo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            demo.Children.Add(track);
            demo.Children.Add(key);
            // the contrast on a line of its own, as the cells are narrow beside the square
            contrast = Theme.T("", 12, p.TextFaint, Theme.Regular, false);
            contrast.TextWrapping = TextWrapping.Wrap;
            contrast.Margin = new Thickness(0, 8, 0, 0);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(demo);
            stack.Children.Add(contrast);
            Root = new SmoothBorder
            {
                CornerRadius = new CornerRadius(10),
                Background = p.Solid,
                BorderBrush = p.Edge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Child = stack,
            };
        }

        public void Show(Color c, double ratio)
        {
            accent.Color = c;
            code.Text = Code(c);
            bool low = ratio < Accents.MinContrast;
            string r = ratio.ToString("0.0", CultureInfo.InvariantCulture);
            // the warning on a line of its own: a narrow cell would break it inside a word
            contrast.Inlines.Clear();
            contrast.Inlines.Add(new Run(Tr($"대비 {r}", $"Contrast {r}")));
            if (low)
            {
                contrast.Inlines.Add(new LineBreak());
                contrast.Inlines.Add(new Run(Tr("잘 안 보임", "Hard to see")));
            }
            contrast.Foreground = low ? p.Warn : p.TextFaint;
        }
    }
}
