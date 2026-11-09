using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// The notices (App\Notices) as cards at the place picked in 설정 > 알림 (a corner, an edge or anywhere: NoticePlacement),
/// under the island window and a taskbar showing at the top when they would cover them (NoticeIsland; they glide
/// along as the taskbar slides in and out), newest nearest the edge, at the size picked
/// there. Drawn like the timer card (AlarmWindow). Never takes the focus; a click closes a card (after the card's own
/// Click, if it has one: a turn's card goes to its session window), the mouse over one holds it, and a card with a key
/// (승인 대기) stays until Notices takes it away.
internal sealed class NoticeWindow : Window
{
    private const int MaxCards = 4;
    private readonly StackPanel stack = new() { Margin = new Thickness(0, 4, 0, 4) };
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Dictionary<string, FrameworkElement> keyed = new();

    /// QA: laid out off-screen, never shown on the user's screens.
    public bool Offscreen;

    public NoticeWindow()
    {
        Title = "Bloom";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = new Border { Child = stack, LayoutTransform = scale };
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_NOACTIVATE)); // a click never takes the focus
        };
        SizeChanged += (_, _) => Place();
        islandWatch.Tick += (_, _) =>
        {
            if (!Offscreen && NoticeIsland.Look(true) != aboveSeen) Follow(); // the island grew, came back or went
        };
        glide.Tick += (_, _) => Glide();
        IsVisibleChanged += (_, _) => Watch(IsVisible);
    }

    private bool held;
    private bool topSeen = AtTop; // which edge the stack's order is for
    private Above aboveSeen; // the island and the taskbar as the cards were last placed
    private readonly Dictionary<FrameworkElement, DispatcherTimer> lives = new(); // the timers of the cards that expire
    private readonly DispatcherTimer islandWatch = new() { Interval = TimeSpan.FromSeconds(1) };

    // Following what is above the cards: while the taskbar moves, and for a moment after (the island turns into it or
    // back), the window steps toward its place every frame.
    private static readonly TimeSpan FollowFor = TimeSpan.FromMilliseconds(800);
    private readonly DispatcherTimer glide = new(DispatcherPriority.Render);
    private DateTime followUntil, glided;
    private bool watching;
    private IntPtr hook, taskbar;
    private Win32.WinEventProc? onMoved; // held here: the hook calls it

    /// The place picked in 설정 > 알림 as fractions (NoticePlacement).
    public static (double X, double Y) Spot => NoticePlacement.Of(Settings.Current.NoticeSpot, Settings.Current.NoticeX, Settings.Current.NoticeY);

    private static bool AtTop => NoticePlacement.AtTop(Spot.Y);

    /// The card scale of the size picked in 설정 > 알림.
    public static double SizeScale => Settings.Current.NoticeSize switch { "s" => 0.85, "l" => 1.2, "xl" => 1.4, _ => 1 };

    /// While the place is being picked (NoticePlacer) the cards are out of sight but kept, with their timers and keys;
    /// a card that comes meanwhile waits with them. Let go, they show at the new place.
    public void Hold(bool on)
    {
        held = on;
        if (on)
        {
            foreach (var life in lives.Values) life.Stop();
            Hide();
            return;
        }
        foreach (var life in lives.Values) life.Start();
        if (stack.Children.Count == 0) return;
        Show();
        Place();
    }

    /// A new card (one with the same key replaces the old one); whether it stayed (a made-up one goes at once when
    /// four real cards are up).
    public bool Add(Notice n)
    {
        Orient();
        scale.ScaleX = scale.ScaleY = SizeScale;
        if (n.Key is { } k && keyed.Remove(k, out var old))
        {
            stack.Children.Remove(old);
            TellGone(old);
        }
        var card = Card(n);
        if (AtTop) stack.Children.Insert(0, card);
        else stack.Children.Add(card);
        if (n.Key is { } key) keyed[key] = card;
        while (stack.Children.Count > MaxCards) Dismiss(Farthest(), false);
        if (!stack.Children.Contains(card)) return false;
        if (!IsVisible && !held) Show();
        Place();
        Enter(card);
        return true;
    }

    /// The cards in the order of the edge they are at: newest nearest it. Turned over when the place moved to the
    /// other side.
    private void Orient()
    {
        if (topSeen == AtTop) return;
        topSeen = AtTop;
        var cards = stack.Children.Cast<UIElement>().Reverse().ToList();
        stack.Children.Clear();
        foreach (var c in cards) stack.Children.Add(c);
    }

    /// The card that goes when there are too many: a made-up one first (a preview never pushes a real card out), else
    /// the one farthest from the edge.
    private FrameworkElement Farthest()
    {
        var cards = stack.Children.OfType<FrameworkElement>().ToList();
        if (AtTop) cards.Reverse();
        return cards.FirstOrDefault(c => c.Tag is Notice { Sample: true }) ?? cards[0];
    }

    /// The made-up cards of 미리 보기 go; the real ones (an approval waiting among them) stay.
    public void ClearSamples()
    {
        foreach (var card in stack.Children.OfType<FrameworkElement>().Where(c => c.Tag is Notice { Sample: true }).ToList()) Dismiss(card, false);
    }

    /// A card's text line is typed in (AskUserQuestion's 기타): the window takes the focus for it, and goes back to
    /// never taking it after.
    public void Typing(bool on)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(on ? ex & ~Win32.WS_EX_NOACTIVATE : ex | Win32.WS_EX_NOACTIVATE));
        if (on && !IsActive) Activate();
    }

    /// The card with this key goes (its 승인 대기 ended).
    public void Remove(string key)
    {
        if (keyed.TryGetValue(key, out var card)) Dismiss(card, true);
    }

    public void Clear()
    {
        var all = stack.Children.OfType<FrameworkElement>().ToList();
        stack.Children.Clear();
        keyed.Clear();
        Hide();
        foreach (var card in all)
        {
            Forget(card);
            TellGone(card);
        }
    }

    /// A card off the window stops its timer and leaves `lives`: one that was never shown (it came while held) gets
    /// no Unloaded.
    private void Forget(FrameworkElement card)
    {
        if (lives.Remove(card, out var life)) life.Stop();
    }

    /// A card is off the window: its notice's Gone (an approval card left unanswered: the terminal asks).
    private static void TellGone(FrameworkElement card) => (card.Tag as Notice)?.Gone?.Invoke();

    public void Capture(string file) => Shots.Save((FrameworkElement)Content, file);

    private void Place()
    {
        if (Offscreen)
        {
            Left = Top = -32000;
            return;
        }
        Orient();
        var at = Target(NoticeIsland.Look(true));
        Left = at.X;
        if (!glide.IsEnabled) Top = at.Y; // while following, the glide takes it there
    }

    /// Where the cards go with this above them: the picked place, kept under the island and a taskbar showing at the top.
    private Point Target(Above above)
    {
        var wa = SystemParameters.WorkArea;
        var size = new Size(ActualWidth, ActualHeight);
        var (x, y) = Spot;
        aboveSeen = above;
        return NoticePlacement.AvoidAll(wa, size, NoticePlacement.At(wa, size, x, y), above.Boxes);
    }

    /// While the cards show: the island looked at once a second, the taskbar's moves as they happen (a hook on explorer's
    /// window moves, read for its taskbar window only) and the work area's changes (a taskbar docked or let go).
    private void Watch(bool on)
    {
        on &= !Offscreen;
        islandWatch.IsEnabled = on;
        if (on == watching) return;
        watching = on;
        if (on)
        {
            SystemParameters.StaticPropertyChanged += OnSystem;
            taskbar = NoticeIsland.TaskbarWindow;
            if (taskbar != IntPtr.Zero && Win32.GetWindowThreadProcessId(taskbar, out uint pid) != 0)
            {
                onMoved ??= (_, _, hwnd, idObject, _, _, _) =>
                {
                    if (idObject == 0 && hwnd == taskbar) Follow(); // the window itself, not a part of it
                };
                hook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, onMoved, pid, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
            }
            return;
        }
        SystemParameters.StaticPropertyChanged -= OnSystem;
        if (hook != IntPtr.Zero) Win32.UnhookWinEvent(hook);
        hook = IntPtr.Zero;
        glide.Stop();
    }

    private void OnSystem(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea)) Follow();
    }

    /// Something above the cards moved: they follow it for a while, a step every frame.
    private void Follow()
    {
        if (Offscreen || !IsVisible) return;
        followUntil = DateTime.UtcNow + FollowFor;
        if (glide.IsEnabled) return;
        int fps = Settings.Current.AnimationFps;
        glide.Interval = TimeSpan.FromMilliseconds(fps > 0 ? 1000.0 / fps : 15);
        glided = DateTime.UtcNow;
        glide.Start();
    }

    /// One frame of following: the place under what is above now, and the window a step nearer to it (there at once when
    /// nothing may slide); done once it is there and nothing has moved for a while.
    private void Glide()
    {
        var now = DateTime.UtcNow;
        double ms = (now - glided).TotalMilliseconds;
        glided = now;
        var to = Target(NoticeIsland.Look(false));
        Left = to.X;
        double gap = to.Y - Top;
        Top = !Motion.Moves || Math.Abs(gap) < 0.5 ? to.Y : Top + (gap * (1 - Math.Exp(-ms / 60)));
        if (Top == to.Y && now > followUntil) glide.Stop();
    }

    /// One card as this window holds it, at the picked size, without its timer or click (the place picker's card).
    public static FrameworkElement Sheet(Notice n) => new Border
    {
        Child = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Children = { Face(n) } },
        LayoutTransform = new ScaleTransform(SizeScale, SizeScale),
    };

    private FrameworkElement Card(Notice n)
    {
        var card = Face(n);
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            n.Click?.Invoke();
            Dismiss(card, true);
        };
        if (n.Key is null) // a keyed card stays until its wait ends
        {
            var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.Current.NoticeSeconds, 2, 60) + (n.Kind is "limit" or "recover" ? 4 : 0)) };
            life.Tick += (_, _) =>
            {
                life.Stop();
                Dismiss(card, true);
            };
            card.MouseEnter += (_, _) => life.Stop();
            card.MouseLeave += (_, _) =>
            {
                life.Interval = TimeSpan.FromSeconds(2);
                life.Start();
            };
            card.Unloaded += (_, _) => Forget(card);
            lives[card] = life;
            if (!held) life.Start(); // a card that comes while held starts its time when it shows
        }
        return card;
    }

    /// What a card looks like: the kind's glyph, the head, title, body, detail and buttons.
    private static SmoothBorder Face(Notice n)
    {
        var tint = Tint(n.Kind);
        var icon = new SmoothBorder
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, tint.R, tint.G, tint.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 12, 0),
            Child = Theme.Glyph(Glyph(n.Kind), 17, new SolidColorBrush(tint)),
        };
        var text = new StackPanel();
        text.Children.Add(Theme.T(n.Head, 12, new SolidColorBrush(tint), Theme.Semi));
        var title = Theme.T(n.Title, 14, Theme.P.Text, Theme.Medium); // one line each, then … (the tooltip has it all)
        title.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(title);
        if (n.Body is { Length: > 0 } body && n.Questions is null)
            text.Children.Add(new SmoothBorder
            {
                Background = Theme.P.Well,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 5, 8, 6),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontFamily = Theme.Mono, FontSize = 12, Foreground = Theme.P.Text },
            });
        if (n.Detail is { Length: > 0 } d)
        {
            var detail = Theme.T(d, 12, Theme.P.TextFaint);
            detail.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(detail);
        }
        if (n.Action is { } action && n.ActionText is { Length: > 0 } actionText)
        {
            var button = ToolUi.TextButton(actionText, action);
            button.Margin = new Thickness(0, 8, 0, 0);
            text.Children.Add(button);
        }
        if (n.Questions is { } questions) text.Children.Add(NoticeQuestionsView.Build(questions, n.Buttons)); // its buttons after 답하기
        else if (n.Buttons is { Count: > 0 } choices)
        {
            var row2 = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (label, run) in choices)
            {
                var button = ToolUi.TextButton(label, run);
                button.Margin = new Thickness(0, 0, 6, 0);
                row2.Children.Add(button);
            }
            text.Children.Add(row2);
        }
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new SmoothBorder
        {
            Background = Theme.P.Solid,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 16, 12),
            Width = 330,
            Margin = new Thickness(10, 4, 10, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.3 },
            Cursor = Cursors.Hand,
            Child = row,
            Tag = n,
            // questions show whole on the card, and a tooltip would cover their choices
            ToolTip = n.Questions is not null ? null : (n.Detail is { Length: > 0 } ? $"{n.Title}\n{n.Detail}" : n.Title) + (n.Body is { Length: > 0 } ? "\n" + n.Body : "") + (n.Click is null ? "" : "\n" + Tr("클릭하면 세션 창으로 이동합니다", "Click to go to the session window")),
        };
    }

    /// 진행 중 · 작업 완료 in the accent, 승인 대기 in tangerine, 한도 in red, 복구 in mint.
    public static Color Tint(string kind) => kind switch
    {
        "waiting" => Accents.Swatch(Accents.Find("tangerine")!, Theme.P.Dark),
        "limit" => Theme.P.Danger.Color,
        "recover" => Accents.Swatch(Accents.Find("mint")!, Theme.P.Dark),
        _ => Theme.P.Accent.Color,
    };

    private static char Glyph(string kind) => kind switch
    {
        "start" => G.Gears,
        "waiting" => G.Clock,
        "limit" => G.Warning,
        "recover" => G.Sync,
        _ => G.Done,
    };

    private void Enter(FrameworkElement card)
    {
        if (Motion.Moves)
        {
            var move = new TranslateTransform(0, AtTop ? -10 : 10);
            card.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, Motion.Paced(new DoubleAnimation(AtTop ? -10 : 10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } }));
        }
        if (Motion.Fades) card.BeginAnimation(OpacityProperty, Motion.Paced(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))));
    }

    /// Out (faded when fades are on); the window hides with the last card.
    private void Dismiss(FrameworkElement card, bool fade)
    {
        foreach (var (k, c) in keyed)
            if (c == card)
            {
                keyed.Remove(k);
                break;
            }
        void Gone()
        {
            bool shown = stack.Children.Contains(card); // else cleared or replaced meanwhile, which told it already
            stack.Children.Remove(card);
            Forget(card);
            if (stack.Children.Count == 0) Hide();
            if (shown) TellGone(card);
        }
        if (!fade || !Motion.Fades || !stack.Children.Contains(card))
        {
            Gone();
            return;
        }
        card.IsHitTestVisible = false;
        var a = Motion.Paced(new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        a.Completed += (_, _) => Gone();
        card.BeginAnimation(OpacityProperty, a);
    }
}
