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
using Bloom.Native;

namespace Bloom.Ui;

/// The first page: dark in both themes, a muted clip behind the headline with its first frame as the still.
internal sealed class TutorialHero : Grid
{
    /// The clip's own ground: the scrims and the window's footer on this page meet its edges without a seam.
    public static readonly Color Ground = Color.FromRgb(0x12, 0x13, 0x17);

    // the clip is made for this box (1280x1032 at 2x), the page above the footer's hairline
    private const double PageWidth = 640, PageHeight = 580, Stage = 516, ClipScale = 2;
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(4);
    // a frame this late (the window's thread was held) is shown and the clip goes on from it, rather than racing on
    private static readonly long Late = TimeSpan.FromMilliseconds(100).Ticks;

    private enum State { Idle, Loading, Playing }

    private readonly TutorialEnv env;
    private readonly string variant = TutorialClip.Variant;
    private readonly Image still;
    private readonly Segmented lang;
    private Image? picture; // the clip decoded here, over the still
    private WriteableBitmap? frames; // its pixels while it plays, at the pixel size it is shown at
    private ClipReader? reader;
    private long? anchor; // the drawing clock (100-ns ticks) less the clip time of the frame shown first
    private TimeSpan drawn; // the last drawing time a frame was looked for, so one drawing looks once
    private long at; // where the frame on screen is in the file (100-ns)
    private State state;
    private bool failed; // the clip would not play in this window: the still stays, no second try
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

        lang = new Segmented(Lang.Choices.Where(c => c.value != "auto").ToArray(), Lang.Code, Look)
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
            // held with Alt, Ctrl or Shift it is another key (the IME's language switch is Shift+Space), as on the
            // window's own choices
            if (Keyboard.Modifiers != ModifierKeys.None) return;
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
        limit = new DispatcherTimer { Interval = OpenLimit };
        limit.Tick += (_, _) => Fail("did not open in " + OpenLimit.TotalSeconds + " s");
        limit.Start();
        Decode(file);
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

    /// The language control back on the language of this run, without picking it (a restart that was called off).
    public void ShowLanguage() => lang.Set(Lang.Code);

    internal bool Playing => state == State.Playing;

    /// Where the frame on screen is in the clip (tests).
    internal TimeSpan Position => TimeSpan.FromTicks(at);

    private void Pick(string v)
    {
        if (v != Lang.Code) LanguagePicked?.Invoke(v);
    }

    // Decoded here, however Bloom draws, on a thread of its own at the size it is shown at (the clip is made at twice
    // the page), so nothing scales it while it plays; the picture is made when it first plays and filled again on every
    // return. All it holds goes when it stops. Windows' player kept 60 to 250 MB in the process after the window
    // closed (the most with GPU drawing, whose decoder keeps its driver's memory) and drew the clip lighter than it was
    // made; this costs a few percent of one core more, only while the first page plays.
    private void Decode(string file)
    {
        var (w, h) = Pixels(VisualTreeHelper.GetDpi(this));
        frames = new WriteableBitmap(w, h, 96 * w / PageWidth, 96 * h / Stage, PixelFormats.Bgr32, null);
        picture ??= Over(new Image { Stretch = Stretch.UniformToFill });
        picture.Opacity = 0;
        picture.Source = frames;
        ClipReader? own = null;
        reader = own = ClipReader.Start(file, w, h, why => Dispatcher.BeginInvoke(() =>
        {
            if (reader is not null && reader == own) Fail(why);
        }));
        anchor = null;
        drawn = TimeSpan.Zero;
        CompositionTarget.Rendering += Present;
    }

    // the clip's pixel size on a screen of this scale: the page's, up to the clip's own (made at twice the page)
    private static (int w, int h) Pixels(DpiScale dpi) =>
        ((int)Math.Round(PageWidth * Math.Min(dpi.DpiScaleX, ClipScale)), (int)Math.Round(Stage * Math.Min(dpi.DpiScaleY, ClipScale)));

    // the window went to a screen of another scale, or Windows' scaling changed, while it plays: the clip starts again
    // at the new pixel size, as sharp as the words over it
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (reader is null || Pixels(newDpi) == (reader.Width, reader.Height)) return;
        Leave();
        Enter();
    }

    // every frame WPF draws: the clip's frame due by then, if a new one is (the reader decodes ahead on its own thread,
    // and the drawing clock keeps the frames to their times, across the loop too)
    private void Present(object? sender, EventArgs e)
    {
        if (reader is null || frames is null || e is not RenderingEventArgs r || r.RenderingTime == drawn) return;
        drawn = r.RenderingTime;
        long now = r.RenderingTime.Ticks;
        if (anchor is null)
        {
            if (reader.Next is not { } next) return;
            anchor = now - next;
        }
        var f = reader.Due(now - anchor.Value);
        if (f is null) return;
        long late = now - anchor.Value - f.Time;
        if (late > Late) anchor += late;
        frames.WritePixels(new Int32Rect(0, 0, reader.Width, reader.Height), f.Pixels, reader.Stride, 0);
        at = f.At;
        reader.Return(f);
        if (state == State.Loading) Shown(picture!);
    }

    // the clip's layer, just over the still
    private T Over<T>(T e) where T : FrameworkElement
    {
        Children.Insert(Children.IndexOf(still) + 1, Layer(e, Stage));
        e.Opacity = 0;
        return e;
    }

    // the first frame is in the picture
    private void Shown(UIElement e)
    {
        StopLimit();
        state = State.Playing;
        // a frame or two after it, so the first one is drawn before it shows; the still is underneath
        if (!Motion.Fades)
        {
            e.Opacity = 1;
            return;
        }
        e.BeginAnimation(OpacityProperty, Motion.Paced(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            FillBehavior = FillBehavior.HoldEnd,
        }));
    }

    private void Fail(string why)
    {
        Log.Write("tutorial clip: " + why);
        failed = true;
        Leave();
    }

    // stops the reader (its thread lets go of the decoder) and drops its picture, hidden over the still
    private void Release()
    {
        StopLimit();
        state = State.Idle;
        CompositionTarget.Rendering -= Present;
        reader?.Dispose();
        reader = null;
        frames = null;
        at = 0;
        if (picture is null) return;
        picture.BeginAnimation(OpacityProperty, null);
        picture.Opacity = 0;
        picture.Source = null;
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
