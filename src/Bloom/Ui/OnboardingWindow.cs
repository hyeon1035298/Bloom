using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// The tutorial: on a first run every page (how Bloom opens, how it looks, the browser, the home, the AIs, the
/// add-ons, the task notices, the keys), after an update only the pages this user has not seen. Shown once
/// (Settings.Onboarded, TutorialSeen); "Bloom 둘러보기" opens it again. Enter / → next, ← back, Esc skips; Tab goes
/// over a page's parts and Space works the one with the keyboard. A choice is saved the moment it is made, except the
/// home's features (written when the window closes) and the add-ons and notices (held in Choices until the last
/// button; skipping drops them). The pages are in Tutorial/OnboardingWindow.*.cs.
internal sealed partial class OnboardingWindow : Window
{
    // the pages are drawn for this client area: the first page's clip and the add-on pictures are made to its measure
    private const double ClientWidth = 640, ClientHeight = 580, PageHeight = 515, FootHeight = 64, PageWidth = 552;

    private readonly Host host;
    private readonly TutorialEnv env;
    private readonly List<string> ids; // the run's pages (TutorialSteps ids)
    private readonly Grid root = new() { Width = ClientWidth, Height = ClientHeight, Background = Theme.P.Solid };
    private readonly TutorialHero? hero; // the first page, when the run has it
    private readonly ScrollViewer scroll;
    private readonly SmoothBorder line = new();
    private readonly StackPanel petals = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly FrameworkElement skip;
    private SmoothBorder? back, next;
    private int step = -1;
    private (double left, double top)? openAt; // where a dev or QA start put the window
    /// The choices of the page shown that the window's own keys work: what ← → and Space do on each.
    private readonly Dictionary<FrameworkElement, (Action<int> step, Action cycle)> choices = new();

    /// `only` = the pages of this run (TutorialSteps ids); null = the full tour for this PC.
    public OnboardingWindow(Host host, IReadOnlyList<string>? only = null, TutorialEnv? env = null)
    {
        this.host = host;
        this.env = env ?? TutorialEnv.Now;
        Title = Tr("Bloom 시작", "Welcome to Bloom");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        var full = TutorialSteps.Full(this.env.Has);
        ids = only is null ? full : [.. full.Where(only.Contains)];
        if (ids.Count == 0) ids = [TutorialSteps.Keys]; // a window always has a page, and this one every PC has
        tools = ToolTable();
        ResetTools();
        Choices = MakeChoices();

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PageHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FootHeight) });
        if (ids.Contains(TutorialSteps.Start))
        {
            hero = new TutorialHero(this.env) { Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(hero, 3);
            hero.LanguagePicked += PickLanguage;
            hero.ImportAsked += Import;
            root.Children.Add(hero);
        }
        // a page that is taller than its area scrolls instead of being cut; no page is meant to (ScrolledForQa)
        scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Template = Launcher.ThinScroll(),
        };
        root.Children.Add(scroll);
        Grid.SetRow(line, 1);
        root.Children.Add(line);
        var foot = new Grid { Margin = new Thickness(44, 0, 28, 0) };
        foot.Children.Add(petals);
        skip = TutorialParts.Link(Tr("건너뛰기", "Skip"), Close, 12.5, Theme.P.TextFaint);
        skip.HorizontalAlignment = HorizontalAlignment.Center;
        skip.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(skip);
        foot.Children.Add(buttons);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;

        PreviewKeyDown += OnKey;
        AccentFollows();
        SourceInitialized += (_, _) =>
        {
            Caption();
            Place();
        };
        Loaded += (_, _) => FitScreen();
        Theme.Changed += OnTheme;
        IsVisibleChanged += (_, _) => HeroFollows();
        StateChanged += (_, _) => HeroFollows();
        Closed += (_, _) =>
        {
            hero?.Leave();
            CloseAccent();
            Theme.Changed -= OnTheme;
            // closing the window counts as seen, unless a restart closes it (it opens again in the new Bloom)
            if (host.Restarting) return;
            Settings.Current.MarkTutorialSeen();
            if (ids.Contains(TutorialSteps.Home)) AddTools(); // visited or not: a first run closed on its first page still gets its home
            Settings.Save();
        };
        if (host.Opt.Display is { } display) OpenOn(display);
        Go(0);
    }

    public IReadOnlyList<string> StepIds => ids;
    public int StepCount => ids.Count;
    public string StepId => ids[step];
    public bool UpdateOnly => !ids.Contains(TutorialSteps.Start);

    /// What the addons and notify pages hold; made again after an import.
    internal TutorialChoices Choices { get; private set; }

    /// An import (here or in Settings) put new settings and a new home in place: the pages' state and the home tiles
    /// are read from them again, so closing writes what was brought in, not what was there before.
    internal void SettingsReplaced()
    {
        Choices = MakeChoices();
        ResetTools();
    }

    /// The addons and notify pages' starting state, from the settings as they are now.
    private TutorialChoices MakeChoices()
    {
        var catalog = AddonHub.All.Where(a => !a.User).Select(a => a.Id).ToList();
        return TutorialChoices.From(Settings.Current, catalog, id => AddonHub.All.Any(a => a.Id == id && a.On), ids, env);
    }

    /// The window is up and not put away: the first page's clip may play.
    private bool Showing => IsVisible && WindowState != WindowState.Minimized;

    private void HeroFollows()
    {
        if (hero is null) return;
        if (!Showing) hero.Leave();
        else if (StepId == TutorialSteps.Start) hero.Enter();
    }

    private void OnTheme()
    {
        hero?.Retheme();
        if (accentPopup is not null && accentDark != Theme.P.Dark) CloseAccent(); // the picker was made for the other theme
        Go(step); // the page's pictures, dots and switches take the new colours
    }

    public bool Go(string id)
    {
        int i = ids.IndexOf(id);
        if (i < 0) return false;
        Go(i);
        return true;
    }

    /// Shows page `i`, built anew. The page that is showing built again (a theme or accent picked on it, a QA
    /// rebuild) keeps its scroll place and the keyboard on the part that had it.
    public void Go(int i)
    {
        int to = Math.Clamp(i, 0, ids.Count - 1);
        bool again = to == step;
        string? was = step < 0 ? null : ids[step];
        int stop = again ? FocusedStop() : -1;
        string? role = back is { IsKeyboardFocused: true } ? "back" : next is { IsKeyboardFocused: true } ? "next" : null;
        // (not while the keyboard is in the colour picker: it outlives the page)
        bool held = accentPopup?.Child.IsKeyboardFocusWithin != true && (scroll.IsKeyboardFocusWithin || role is not null);
        double at = scroll.VerticalOffset;
        step = to;
        string id = ids[step];
        if (!again) CloseAccent();

        choices.Clear();
        var page = new StackPanel { Margin = new Thickness(44, 34, 44, 0) };
        Fill(page, id);
        scroll.Content = page;
        if (again) scroll.ScrollToVerticalOffset(at);
        else scroll.ScrollToTop();
        if (hero is not null) hero.Visibility = id == TutorialSteps.Start ? Visibility.Visible : Visibility.Collapsed;
        PaintFooter();
        Caption();

        if (held)
        {
            // the part that had the keyboard is gone with the old page: its place in the new one takes it, else the
            // window, so Enter and the arrows go on working
            root.UpdateLayout();
            UIElement? target = role == "back" ? back : role == "next" ? next : stop >= 0 ? StopAt(stop) : null;
            if (target is null || !target.Focus()) Focus();
        }
        if (again || hero is null) return;
        if (id == TutorialSteps.Start)
        {
            if (Showing) hero.Enter();
        }
        else if (was == TutorialSteps.Start) hero.Leave();
    }

    // the first page has nothing here: the hero is the page
    private void Fill(StackPanel page, string id)
    {
        switch (id)
        {
            case TutorialSteps.Open: Opening(page); break;
            case TutorialSteps.Looks: Looks(page); break;
            case TutorialSteps.Browser: BrowserPick(page); break;
            case TutorialSteps.Home: HomeTools(page); break;
            case TutorialSteps.Ai: Ais(page); break;
            case TutorialSteps.Addons: AddonsPick(page); break;
            case TutorialSteps.Notify: Notify(page); break;
            case TutorialSteps.Keys: KeysToKnow(page); break;
            case TutorialSteps.Done: Done(page); break;
        }
    }

    private void Next()
    {
        if (step < ids.Count - 1) Go(step + 1);
        // the last button applies what the pages hold; only the full tour's opens the launcher after it (an update's
        // pages come at a start, with 바뀐 점 right behind them)
        else TutorialFinish.Run(host, this, Choices, env, Close, summon: StepId == TutorialSteps.Done);
    }

    // ── the footer ──────────────────────────────────────────────────────────────────────────────
    private static readonly Brush HeroLine = HeroInk(0x12), HeroTrack = HeroInk(0x1F);

    // the first page's light gray, thin: its hairline and step marks lie on the dark ground in both themes
    private static SolidColorBrush HeroInk(byte alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, 0xEC, 0xED, 0xEE));
        b.Freeze();
        return b;
    }

    /// The step marks and the buttons of the page shown. The first page is dark in both themes, so there they draw
    /// from its palette, not the theme's.
    private void PaintFooter()
    {
        string id = StepId;
        bool start = id == TutorialSteps.Start && hero is not null, last = step == ids.Count - 1, done = id == TutorialSteps.Done;
        var look = start ? hero!.Look : Theme.P;
        line.Background = start ? HeroLine : Theme.P.Line;
        petals.Children.Clear();
        for (int k = 0; k < ids.Count; k++) // the step marks: Row's selection bar, lying down
            petals.Children.Add(new Rectangle
            {
                Width = 16,
                Height = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = k == step ? look.Accent : start ? HeroTrack : Theme.P.EdgeStrong,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
            });
        buttons.Children.Clear();
        back = step > 0 ? Button(ActionButton.Make(Tr("이전", "Previous"), false, Back), Back) : null;
        string label = start ? Tr("시작하기", "Get Started") : !last ? Tr("다음", "Next") : done ? Tr("Bloom 열기", "Open Bloom") : Tr("완료", "Done");
        next = Button(start ? ActionButton.Make(label, Next, look) : ActionButton.Make(label, true, Next), Next);
        // the first page and the last of the full tour have nothing to skip to
        skip.Visibility = start || done ? Visibility.Hidden : Visibility.Visible;
    }

    private void Back() => Go(step - 1);

    // a footer button is a Tab stop too: Enter and Space on it run it
    private SmoothBorder Button(SmoothBorder b, Action click)
    {
        TutorialParts.Focusable(b, click, new CornerRadius(b.CornerRadius.TopLeft + 2), enter: true);
        buttons.Children.Add(b);
        return b;
    }

    /// The title bar in the ground of the page under it: the first page's dark one, else the theme's.
    private void Caption()
    {
        if (step < 0 || StepId != TutorialSteps.Start || hero is null)
        {
            Shots.SolidCaption(this);
            return;
        }
        IntPtr h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = 1;
        _ = Win32.DwmSetWindowAttribute(h, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        var c = TutorialHero.Ground;
        int caption = c.R | c.G << 8 | c.B << 16;
        _ = Win32.DwmSetWindowAttribute(h, Win32.DWMWA_CAPTION_COLOR, ref caption, 4);
    }

    // ── the keyboard ────────────────────────────────────────────────────────────────────────────
    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = Hotkeys.Pressed(e); // with the Korean IME on, Space arrives as ImeProcessed
        if (accentPopup is not null)
        {
            // The colour picker is up. A key pressed in it never comes here (the popup is not in the window's tree,
            // its card reads its own), so these are the window's: Esc closes the picker before it closes the window,
            // and a key that moves on or picks closes it and then does what it does.
            if (key == Key.Escape)
            {
                CloseAccent();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Enter or Key.Left or Key.Right or Key.Space) CloseAccent();
        }
        if (key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        // held with Alt, Ctrl or Shift it is another key: the window menu, the IME's language switch
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var focused = Keyboard.FocusedElement;
        // a choice's own ← → and Space: a page's go through the window, the first page's language reads them itself
        var choice = focused is FrameworkElement f && choices.TryGetValue(f, out var keys) ? keys : ((Action<int> step, Action cycle)?)null;
        bool own = choice is null && focused is Segmented;
        switch (key)
        {
            case Key.Enter:
                if (TutorialParts.TakesEnter(focused)) return; // a link or a button with the keyboard runs itself
                if (!e.IsRepeat) Next();
                break;
            case Key.Right when own:
            case Key.Left when own:
                return;
            case Key.Right when choice is { } c:
                c.step(+1);
                break;
            case Key.Left when choice is { } c:
                c.step(-1);
                break;
            case Key.Right:
                if (step < ids.Count - 1) Go(step + 1); // on the last page only its button finishes
                break;
            case Key.Left:
                if (step > 0) Go(step - 1);
                break;
            case Key.Space when choice is { } c:
                if (!e.IsRepeat) c.cycle();
                break;
            default:
                return; // Space on a tile, a row, a link or a button is that part's
        }
        e.Handled = true;
    }

    /// A segmented choice of a page: a click picks, and the window's ← → and Space on it reach `picked` the same way
    /// (Step and Cycle only move the control).
    private Segmented Choice(Segmented seg, Action<string> picked)
    {
        seg.Picked += picked;
        choices[seg] = (d =>
        {
            string was = seg.Value;
            if (seg.Step(d) != was) picked(seg.Value);
        }, () => picked(seg.Cycle()));
        return seg;
    }

    /// The page's Tab stops in the order Tab visits them.
    private List<UIElement> Stops()
    {
        var found = new List<UIElement>();
        if (scroll.Content is DependencyObject page) Collect(page);
        return found;

        void Collect(DependencyObject d)
        {
            if (d is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } e) found.Add(e);
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int k = 0; k < n; k++) Collect(VisualTreeHelper.GetChild(d, k));
        }
    }

    /// Which of the page's Tab stops has the keyboard, or -1.
    private int FocusedStop() => scroll.IsKeyboardFocusWithin && Keyboard.FocusedElement is UIElement e ? Stops().IndexOf(e) : -1;

    private UIElement? StopAt(int index) => Stops() is { Count: > 0 } stops ? stops[Math.Min(index, stops.Count - 1)] : null;

    // ── the first page's two actions ────────────────────────────────────────────────────────────
    // the new Bloom opens the whole tour again in the language picked; a restart that is called off (a document's
    // unwritten edits, a new Bloom that would not start) puts the language back, saved and shown
    private void PickLanguage(string value)
    {
        string was = Settings.Current.Language;
        Settings.Current.Language = value;
        Settings.Flush();
        host.Restart("--tour");
        if (host.Restarting) return;
        Settings.Current.Language = was;
        Settings.Flush();
        hero?.ShowLanguage();
    }

    private void Import()
    {
        if (!host.ImportSettings(this, restartThen: "--tour")) return; // the pages' state came with it (SettingsReplaced)
        if (host.Restarting) return; // the imported language needs a new Bloom, which opens the tour again
        // what an import does not bring is what is left to choose: the add-ons and notices are this PC's
        string to = new[] { TutorialSteps.Addons, TutorialSteps.Notify, TutorialSteps.Keys }.FirstOrDefault(ids.Contains) ?? ids[^1];
        Go(to);
    }

    // ── where the window opens ──────────────────────────────────────────────────────────────────
    /// A dev or QA start names its screen (--display=<n>): the window opens in the middle of that one, not of the
    /// primary screen.
    private void OpenOn(int display)
    {
        if (ScreenWork(Launcher.Monitors() is { Count: > 0 } mons ? mons[Math.Clamp(display - 1, 0, mons.Count - 1)] : IntPtr.Zero) is not { } on) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // the frame's own size is not known before the window exists: near the middle now, exactly once it does (Place)
        Left = on.work.Left / on.scale + (on.work.Width / on.scale - ClientWidth) / 2;
        Top = on.work.Top / on.scale + (on.work.Height / on.scale - ClientHeight) / 2;
        openAt = (Left, Top);
    }

    private void Place()
    {
        // only where OpenOn's place still stands: a QA picture run moves the window off the screen after it
        if (openAt is not { } at || host.Opt.Display is not { } display || Left != at.left || Top != at.top) return;
        IntPtr h = new WindowInteropHelper(this).Handle;
        var mons = Launcher.Monitors();
        if (mons.Count == 0 || ScreenWork(mons[Math.Clamp(display - 1, 0, mons.Count - 1)]) is not { } on || !Win32.GetWindowRect(h, out var r)) return;
        int x = (int)(on.work.Left + (on.work.Width - (r.Right - r.Left)) / 2), y = (int)(on.work.Top + (on.work.Height - (r.Bottom - r.Top)) / 2);
        Win32.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// A screen's work area in pixels and its scale.
    private static (Rect work, double scale)? ScreenWork(IntPtr monitor)
    {
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref info)) return null;
        if (Win32.GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        var w = info.rcWork;
        return (new Rect(w.Left, w.Top, Math.Max(0, w.Right - w.Left), Math.Max(0, w.Bottom - w.Top)), dpi / 96.0);
    }

    /// A screen whose work area is lower than the window: the pages are made smaller as a whole, so the buttons at
    /// the bottom stay on the screen.
    private void FitScreen()
    {
        IntPtr h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || ScreenWork(Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONEAREST)) is not { } on) return;
        double room = on.work.Height / on.scale - 16, outer = ActualHeight;
        if (outer <= 0 || outer <= room) return;
        double k = Math.Max(0.7, room / outer);
        root.LayoutTransform = new ScaleTransform(k, k);
        // the window shrinks towards its top left corner: back to the middle of the work area
        Left += ClientWidth * (1 - k) / 2;
        Top = on.work.Top / on.scale + Math.Max(0, (on.work.Height / on.scale - (outer - ClientHeight * (1 - k))) / 2);
    }

    // ── QA ──────────────────────────────────────────────────────────────────────────────────────
    public void Capture(string file) => Shots.Save(root, file);

    /// QA: builds the page again from Choices and the settings.
    public void Rebuild() => Go(step);

    /// QA: true when the page shown did not fit and had to scroll.
    public bool ScrolledForQa => scroll.ScrollableHeight > 0.5;

    /// QA: the finish button's action without the consent window or the add-on downloads.
    internal TutorialFinish.Plan PlanForQa() => TutorialFinish.Decide(Choices, env,
        AddonHub.All.Where(a => a.On).Select(a => a.Id).ToList(), Settings.Current.AddonsConfirmed, TaskbarChatSetup.Ready(AddonHub.Taskbar));

    /// QA: flips a home feature tile ("cmd:timer").
    public void ToggleToolForQa(string id)
    {
        if (!picked.Remove(id)) picked.Add(id);
        Go(step);
    }
}
