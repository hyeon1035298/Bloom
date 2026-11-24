using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

/// The first page: dark in both themes, a muted clip behind the headline with its first frame as the still.
internal sealed class TutorialHero : Grid
{
    /// The clip's own ground: the scrims and the window's footer on this page meet its edges without a seam.
    public static readonly Color Ground = Color.FromRgb(0x12, 0x13, 0x17);

    // the clip is made for this box (1280x1032 at 2x), the page above the footer's hairline
    private const double PageWidth = 640, PageHeight = 580, Stage = 516;
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(4);

    private enum State { Idle, Loading, Playing }

    private readonly TutorialEnv env;
    private readonly string variant = TutorialClip.Variant;
    private readonly Image still;
    private MediaElement? clip;
    private State state;
    private bool failed; // the media would not open in this window: the still stays, no second try
    private CancellationTokenSource? wait;
    private DispatcherTimer? limit;

    public TutorialHero(TutorialEnv env)
    {
        this.env = env;
        Width = PageWidth;
        Height = PageHeight;
        ClipToBounds = true;
        Background = new SolidColorBrush(Ground);
        Look.Apply(true, Accents.Current, Settings.Current.GlassTransparency);

        still = Layer(new Image { Source = Poster(variant), Stretch = Stretch.UniformToFill }, Stage);
        RenderOptions.SetBitmapScalingMode(still, BitmapScalingMode.HighQuality);
        Children.Add(still);

        // the headline and the logo stand on the ground: the top of every frame is the ground down to just under the
        // headline, whatever the clip shows there, and clears before the launcher's search line (from y 150 in the clip)
        Children.Add(Scrim(0, 150, (0, 1), (126 / 150.0, 1), (1, 0)));
        // and so do the language control and the import line, on the way down to the footer
        Children.Add(Scrim(380, PageHeight - 380, (0, 0), (0.65, 0.90), (1, 0.97)));

        var logo = Launcher.MarkImage(44);
        logo.HorizontalAlignment = HorizontalAlignment.Left;
        logo.VerticalAlignment = VerticalAlignment.Top;
        logo.Margin = new Thickness((PageWidth - 44) / 2, 30, 0, 0);
        Children.Add(logo);

        var title = Theme.T(Hangul.KeepAll(Tr("Win 키 하나로 모든 것을.", "Everything with one Win key.")), 30, Look.Text, FontWeights.Bold);
        title.TextAlignment = TextAlignment.Center;
        Children.Add(new TutorialParts.EvenLines(title, 38, 2)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 552,
            Margin = new Thickness(0, 84, 0, 0),
        });

        var lang = new Segmented(Lang.Choices.Where(c => c.value != "auto").ToArray(), Lang.Code, Look)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 428, 0, 0),
        };
        lang.Picked += Pick;
        TutorialParts.TabStop(lang, new CornerRadius(11));
        // the page's one control reads its own arrows and Space, so a language is picked the same way on every page
        // that has such a choice; the window sees the key handled
        lang.KeyDown += (_, k) =>
        {
            if (!lang.IsKeyboardFocused || k.IsRepeat) return;
            switch (Hotkeys.Pressed(k))
            {
                case Key.Left: Pick(lang.Step(-1)); break;
                case Key.Right: Pick(lang.Step(+1)); break;
                case Key.Space: Pick(lang.Cycle()); break;
                default: return;
            }
            k.Handled = true;
        };
        Children.Add(lang);

        var import = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 474, 0, 0),
        };
        import.Children.Add(Theme.T(Tr("다른 PC에서 쓰던 설정이 있다면", "Settings from another PC?") + " ", 12, Look.TextFaint, Theme.Regular, false));
        import.Children.Add(TutorialParts.Link(Tr("불러오기", "Import"), () => ImportAsked?.Invoke(), 12, Look.AccentText));
        Children.Add(import);
    }

    /// The fixed dark palette the footer and the language control draw with on this page.
    public Palette Look { get; } = new();

    public event Action<string>? LanguagePicked;    // a Lang.Choices value
    public event Action? ImportAsked;

    /// The page is on screen: the still at once, the clip when it may play. Does nothing while the clip plays or is
    /// on its way.
    public async void Enter()
    {
        if (state != State.Idle || failed || !TutorialClip.Allowed(env)) return;
        state = State.Loading;
        var mine = wait = new CancellationTokenSource();
        string? file = await TutorialClip.Get(variant, mine.Token);
        if (wait != mine || state != State.Loading) return; // left while it came
        if (file is null)
        {
            state = State.Idle; // offline or refused: the still is the page
            return;
        }
        var p = Player();
        p.Opacity = 0;
        limit = new DispatcherTimer { Interval = OpenLimit };
        limit.Tick += (_, _) => Fail("did not open in " + OpenLimit.TotalSeconds + " s");
        limit.Start();
        p.Source = new Uri(file);
        p.Play();
    }

    /// Another page, or the window is hidden or closing: playback stops and the decoder is released. A download under
    /// way goes on.
    public void Leave()
    {
        wait?.Cancel();
        wait?.Dispose();
        wait = null;
        Release();
    }

    /// The accent changed.
    public void Retheme()
    {
        Look.Apply(true, Accents.Current, Settings.Current.GlassTransparency);
        // motion or Windows' effects went off while it played: the still from now on, as on a fresh page
        if (state != State.Idle && !TutorialClip.Allowed(env)) Leave();
    }

    internal bool Playing => state == State.Playing;

    private void Pick(string v)
    {
        if (v != Lang.Code) LanguagePicked?.Invoke(v);
    }

    // the one player of this page, made when it first plays and opened again on every return
    private MediaElement Player()
    {
        if (clip is not null) return clip;
        var p = Layer(new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            IsMuted = true,
            ScrubbingEnabled = false,
            Stretch = Stretch.UniformToFill,
            Opacity = 0,
        }, Stage);
        p.MediaOpened += (_, _) =>
        {
            if (state != State.Loading) return;
            StopLimit();
            state = State.Playing;
            // a frame or two after it opened, so the first one is drawn before it shows; the still is underneath
            if (!Motion.Fades)
            {
                p.Opacity = 1;
                return;
            }
            p.BeginAnimation(OpacityProperty, Motion.Paced(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            {
                BeginTime = TimeSpan.FromMilliseconds(80),
                FillBehavior = FillBehavior.HoldEnd,
            }));
        };
        // the clip ends where it begins: back to the start without a gap
        p.MediaEnded += (_, _) =>
        {
            if (state != State.Playing) return;
            p.Position = TimeSpan.Zero;
            p.Play();
        };
        p.MediaFailed += (_, e) =>
        {
            if (state != State.Idle) Fail(e.ErrorException?.Message ?? "failed");
        };
        clip = p;
        Children.Insert(Children.IndexOf(still) + 1, p);
        return p;
    }

    private void Fail(string why)
    {
        Log.Write("tutorial clip: " + why);
        failed = true;
        Leave();
    }

    // stops and closes the player (the decoder and its frames go with Close) and hides it over the still
    private void Release()
    {
        StopLimit();
        state = State.Idle;
        if (clip is null) return;
        clip.BeginAnimation(OpacityProperty, null);
        clip.Opacity = 0;
        try
        {
            clip.Stop();
            clip.Close();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            /* already gone */
        }
        clip.Source = null;
    }

    private void StopLimit()
    {
        limit?.Stop();
        limit = null;
    }

    private static T Layer<T>(T e, double height) where T : FrameworkElement
    {
        e.Width = PageWidth;
        e.Height = height;
        e.HorizontalAlignment = HorizontalAlignment.Left;
        e.VerticalAlignment = VerticalAlignment.Top;
        e.IsHitTestVisible = false;
        return e;
    }

    // a band of the ground from `top`, its opacity at each stop (0 = the band's top, 1 = its bottom)
    private static Rectangle Scrim(double top, double height, params (double at, double alpha)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (at, alpha) in stops)
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(alpha * 255), Ground.R, Ground.G, Ground.B), at));
        brush.Freeze();
        var r = Layer(new Rectangle { Fill = brush }, height);
        r.Margin = new Thickness(0, top, 0, 0);
        return r;
    }

    // the clip's first frame in this language, decoded once for the window
    private static BitmapImage? Poster(string variant)
    {
        using var s = typeof(TutorialHero).Assembly.GetManifestResourceStream("Bloom.Assets.Tutorial.poster-" + variant + ".jpg");
        if (s is null) return null;
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.StreamSource = s;
        b.EndInit();
        b.Freeze();
        return b;
    }
}
