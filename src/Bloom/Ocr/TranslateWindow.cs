using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Ocr;

/// The translation of screen text in a small Bloom panel. Once: the picked text, translated once.
/// Live: the same screen area is read again every LiveSeconds (Windows OCR, local) and only text
/// that changed and then held still for one more reading is sent for translation — each send is one
/// Claude / ChatGPT call, counted in the footer. The panel is left out of screen captures, so it can
/// sit over the text it translates.
internal sealed class TranslateWindow : Window
{
    private readonly Host host;
    private readonly ScreenRect area;
    private readonly bool live;
    private readonly Border frame;
    private readonly TextBox output;
    private readonly TextBlock source, status;
    private readonly StackPanel chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel actionsBar = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly WorkLine work = new();
    private readonly DispatcherTimer tick = new();
    private CancellationTokenSource? running;
    private string text = ""; // what is being shown translated
    private string seen = ""; // the last reading
    private string? lastPixels;
    private int calls;
    private bool paused, reading;

    public TranslateWindow(Host host, ScreenRect area, string? text, bool live)
    {
        this.host = host;
        this.area = area;
        this.live = live;
        Title = live ? Tr("Bloom 실시간 번역", "Bloom live translation") : Tr("Bloom 번역", "Bloom translation");
        Width = 440;
        Height = 260;
        MinWidth = 300;
        MinHeight = 150;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = !live;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        // head: what it is, the engine, the target, close
        var head = new Grid { Margin = new Thickness(14, 0, 8, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = Launcher.MarkImage(14);
        mark.Margin = new Thickness(0, 0, 8, 0);
        name.Children.Add(mark);
        name.Children.Add(Theme.T(live ? Tr("실시간 번역", "Live translation") : Tr("번역", "Translation"), 13, Theme.P.Text, Theme.Semi, false));
        head.Children.Add(name);
        Grid.SetColumn(chips, 2);
        head.Children.Add(chips);
        var close = new SmoothBorder { Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0), Child = Theme.Glyph(G.Close, 11, Theme.P.TextDim) };
        close.MouseEnter += (_, _) => close.Background = Theme.P.Hover;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonUp += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        WindowChrome.SetIsHitTestVisibleInChrome(chips, true);
        Grid.SetColumn(close, 3);
        head.Children.Add(close);
        grid.Children.Add(head);

        var line = new Grid();
        line.Children.Add(new SmoothBorder { Background = Theme.P.Line });
        line.Children.Add(work);
        Grid.SetRow(line, 1);
        grid.Children.Add(line);

        // body: the translation large, the original small under it
        output = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Theme.Sans,
            FontSize = 15,
            Foreground = Theme.P.Text,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Template = Launcher.BareTextBox(),
        };
        source = Theme.T("", 12, Theme.P.TextFaint, Theme.Regular, false);
        source.TextWrapping = TextWrapping.Wrap;
        source.Margin = new Thickness(0, 10, 0, 0);
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        body.Children.Add(output);
        body.Children.Add(source);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Template = Launcher.ThinScroll(), Focusable = false };
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);

        var footLine = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(footLine, 3);
        grid.Children.Add(footLine);
        var foot = new Grid { Margin = new Thickness(14, 0, 10, 0) };
        status = Theme.T("", 11.5, Theme.P.TextFaint);
        foot.Children.Add(status);
        foot.Children.Add(actionsBar);
        Grid.SetRow(foot, 4);
        grid.Children.Add(foot);

        frame = new SmoothBorder { Child = grid };
        Content = frame;

        tick.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.Current.LiveSeconds, 0.8, 10));
        tick.Tick += async (_, _) => await Read();
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            Glass.ToolWindow(this);
            // never in a screen capture: neither in our own live readings nor in anyone's screenshots
            SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
            Paint();
            Glass.CloakFirstFrame(this);
            if (Left > -30000) Place(h); // QA pictures stay off-screen
        };
        Theme.Changed += Paint;
        Closed += (_, _) =>
        {
            Theme.Changed -= Paint;
            tick.Stop();
            running?.Cancel();
        };
        Chrome();
        if (text is not null) _ = Translate(text);
        if (live)
        {
            status.Text = Tr("화면을 읽는 중…", "Reading the screen…");
            Loaded += async (_, _) =>
            {
                await Read();
                tick.Start();
            };
        }
    }

    private void Paint()
    {
        frame.Background = Theme.Acrylic ? Theme.P.Tint : Theme.P.Solid;
        Glass.Apply(this, Theme.P.Dark, Theme.Acrylic, Theme.P.Dark ? Color.FromRgb(0x35, 0x2E, 0x31) : Color.FromRgb(0xD9, 0xD2, 0xD5));
    }

    /// Under the area when there is room on its monitor, else above it, else inside its bottom edge.
    private void Place(IntPtr h)
    {
        Win32.GetCursorPos(out var cursor);
        var mon = area.IsEmpty
            ? Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST)
            : Win32.MonitorFromPoint(new Win32.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(mon, ref info)) return;
        if (Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96; // failed: 100 %, never a zero-size window
        double s = dpi / 96.0;
        var wa = info.rcWork;
        if (area.IsEmpty)
        {
            // no area (from the OCR view): in the middle of the screen with the mouse
            int cw = (int)Math.Round(Width * s), ch = (int)Math.Round(Height * s);
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, wa.Left + (wa.Right - wa.Left - cw) / 2, wa.Top + (wa.Bottom - wa.Top - ch) / 3, cw, ch, 0);
            return;
        }
        int w = (int)Math.Round(Math.Clamp(area.Width / s, 340, 620) * s);
        int hgt = (int)Math.Round(Height * s);
        int gap = (int)(8 * s);
        int x = Math.Clamp(area.X, wa.Left, Math.Max(wa.Left, wa.Right - w));
        int y = area.Y + area.Height + gap;
        if (y + hgt > wa.Bottom) y = area.Y - hgt - gap;
        if (y < wa.Top) y = Math.Max(wa.Top, area.Y + area.Height - hgt);
        Win32.SetWindowPos(h, Win32.HWND_TOPMOST, x, y, w, hgt, live ? Win32.SWP_NOACTIVATE : 0);
    }

    // ── head chips and foot buttons ─────────────────────────────────────────────────────────────
    private void Chrome()
    {
        chips.Children.Clear();
        string provider = Translator.Provider();
        chips.Children.Add(Chip(Translator.Label(provider), Tr("번역 엔진 바꾸기 (설정에도 저장됩니다)", "Switch the translation engine (saved in Settings too)"), () =>
        {
            Settings.Current.TranslateProvider = AiModels.Next(Translator.Providers(), provider);
            Settings.Save();
            Chrome();
            Again();
        }));
        string to = Settings.Current.TranslateTo;
        var targetChip = Chip(to == "auto" ? Tr("→ 자동", "→ Auto") : "→ " + Translator.TargetName(to), Tr("번역할 언어 (자동: 한국어 글은 영어로, 나머지는 한국어로)", "Translate into (Auto: English text into Korean, everything else into English)"), () =>
        {
            var list = Translator.Targets.Select(t => t.value).ToList();
            Settings.Current.TranslateTo = list[(list.IndexOf(Settings.Current.TranslateTo) + 1) % list.Count];
            Settings.Save();
            Chrome();
            Again();
        });
        targetChip.Margin = new Thickness(6, 0, 0, 0);
        var levels = Translator.Levels(provider);
        if (levels.Length > 0 && Translator.Effort(provider) is { } effort)
        {
            var levelChip = Chip(Tr($"생각 {AiModels.EffortName(effort)}", $"Thinking {AiModels.EffortName(effort)}"), Tr("생각 수준 (높을수록 느리고 사용량이 많습니다 · 설정에도 저장됩니다)", "Thinking level (higher is slower and uses more · saved in Settings too)"), () =>
            {
                Translator.SetEffort(provider, AiModels.Next(levels, effort));
                Chrome();
                Again();
            });
            levelChip.Margin = new Thickness(6, 0, 0, 0);
            chips.Children.Add(levelChip);
        }
        chips.Children.Add(targetChip);

        actionsBar.Children.Clear();
        actionsBar.Children.Add(Button(Tr("복사", "Copy"), "Ctrl+C", Copy));
        if (live) actionsBar.Children.Add(Button(paused ? Tr("다시 읽기", "Resume") : Tr("멈춤", "Pause"), "Space", TogglePause));
        else actionsBar.Children.Add(Button(Tr("다시 번역", "Translate again"), "Ctrl+R", Again));
        Status();
    }

    private static Border Chip(string label, string tip, Action click)
    {
        var b = new SmoothBorder
        {
            Child = Theme.T(label, 11.5, Theme.P.TextDim, Theme.Medium, false),
            Background = Theme.P.Tile,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    private static FrameworkElement Button(string label, string keys, Action click)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        var t = Theme.T(label, 11.5, Theme.P.TextDim, Theme.Medium, false);
        t.Margin = new Thickness(0, 0, 4, 0);
        sp.Children.Add(t);
        sp.Children.Add(Keys.Caps(keys));
        sp.MouseLeftButtonUp += (_, _) => click();
        return sp;
    }

    private void Status(string? note = null)
    {
        string who = Translator.Who(Translator.Provider());
        status.Text = note ?? (calls == 1 ? Tr($"{who} · 호출 1회", $"{who} · 1 call") : Tr($"{who} · 호출 {calls}회", $"{who} · {calls} calls")) + (live ? paused ? Tr(" · 멈춤", " · paused") : Tr($" · {tick.Interval.TotalSeconds:0.#}초마다 읽기", $" · reading every {tick.Interval.TotalSeconds:0.#} s") : "");
    }

    // ── work ────────────────────────────────────────────────────────────────────────────────────
    private async Task Translate(string what)
    {
        running?.Cancel();
        var cts = running = new CancellationTokenSource();
        text = what;
        source.Text = Hangul.KeepAll(what);
        work.Working = true;
        Status(Tr("번역 중…", "Translating…"));
        string provider = Translator.Provider();
        string target = Translator.TargetFor(what, Settings.Current.TranslateTo);
        calls++;
        var (result, error) = await Translator.Run(what, provider, target, cts.Token);
        if (cts.IsCancellationRequested) return;
        work.Working = false;
        if (result is not null) output.Text = Hangul.KeepAll(result); // Korean words stay whole when it wraps
        else if (error is { Length: > 0 }) output.Text = Hangul.KeepAll(error);
        Status();
    }

    private void Again()
    {
        if (text.Length > 0) _ = Translate(text);
    }

    /// Live: read the area; translate what changed once it reads the same twice in a row.
    private async Task Read()
    {
        if (paused || reading) return;
        reading = true;
        try
        {
            byte[]? px = ScreenGrab.Grab(area);
            if (px is null) return;
            string sig = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(px));
            if (sig != lastPixels)
            {
                // something moved: read it; it is translated once a later reading finds it unchanged
                lastPixels = sig;
                var (big, w, h, _) = ScreenGrab.ForOcr(px, area.Width, area.Height);
                var r = await WinOcr.Recognize(big, w, h, host.OcrLanguage());
                if (r is null) return;
                bool settled = Normalize(r.Text) == Normalize(seen);
                seen = r.Text.Trim();
                if (!settled) return;
            }
            if (Normalize(seen).Length < 2 || Normalize(seen) == Normalize(text)) return;
            if (work.Working) return; // one call at a time; the next reading picks up what is new
            await Translate(seen);
        }
        catch (Exception e)
        {
            Log.Error("live translate", e);
        }
        finally
        {
            reading = false;
        }
    }

    private static string Normalize(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void TogglePause()
    {
        paused = !paused;
        if (paused) tick.Stop();
        else
        {
            lastPixels = null;
            tick.Start();
        }
        Chrome();
    }

    private void Copy()
    {
        Launch.Copy((output.SelectionLength > 0 ? output.SelectedText : output.Text).Replace("\u2060", "")); // without the keep-all joiners
        Status(Tr("복사 완료", "Copied"));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.R when ctrl && !live:
                Again();
                break;
            case Key.Space when live:
                TogglePause();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// QA pictures: the panel with sample text, no call made.
    public void FillForQa(string original, string translated, int count)
    {
        text = original;
        source.Text = Hangul.KeepAll(original);
        output.Text = Hangul.KeepAll(translated);
        calls = count;
        Status();
    }

    public void Capture(string file)
    {
        frame.Background = Theme.P.Solid; // no acrylic in an off-screen picture
        Shots.Save(frame, file);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
