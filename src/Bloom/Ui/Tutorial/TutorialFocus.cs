using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;

namespace Bloom.Ui;

/// The tutorial's keyboard side: which parts are Tab stops, the ring the one with the keyboard wears, and what Space
/// and Enter do there.
internal static partial class TutorialParts
{
    private static readonly DependencyProperty StopProperty = DependencyProperty.RegisterAttached("Stop", typeof(TabStopState), typeof(TutorialParts));
    private static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached("Enter", typeof(bool), typeof(TutorialParts), new PropertyMetadata(false));

    /// Makes an element a Tab stop with the tutorial's focus ring; Space runs `act`, and Enter too when `enter`
    /// (links and buttons; on a tile or a row Enter stays the window's "next page"). A click runs `act` as well,
    /// unless the element has a click handler of its own that takes it. `ring` is the ring's corner radius: the
    /// element's own and 2 more.
    public static void Focusable(FrameworkElement e, Action act, CornerRadius ring, bool enter = false)
    {
        TabStop(e, ring);
        Keys(e, act, enter);
        e.MouseLeftButtonUp += (_, args) =>
        {
            act();
            args.Handled = true;
        };
    }

    /// The element with keyboard focus runs on Enter itself (a link, or something made Focusable with `enter`).
    public static bool TakesEnter(object? focused) => focused is DependencyObject d && (bool)d.GetValue(EnterProperty);

    /// A Tab stop with the ring and no key of its own: a part that handles its click itself, or a control whose keys
    /// the window reads (a segmented choice, the colour dots). `grow` is how far the ring's box reaches past the
    /// element's on each side (less than 0: stays inside), for an element whose box is not what the eye takes it for;
    /// `shown` hears the ring come and go.
    internal static void TabStop(FrameworkElement e, CornerRadius ring, Thickness grow = default, Action<bool>? shown = null)
    {
        if (e.GetValue(StopProperty) is not null) return;
        e.Focusable = true;
        e.FocusVisualStyle = null;
        e.SetValue(StopProperty, new TabStopState(e, ring, grow, shown));
    }

    /// Space runs `act` on the element with the keyboard, and Enter too when `enter`. A held key runs it once.
    internal static void Keys(FrameworkElement e, Action act, bool enter = false)
    {
        if (enter) e.SetValue(EnterProperty, true);
        e.KeyDown += (_, k) =>
        {
            if (!e.IsKeyboardFocused) return; // a key that came up from a part inside is that part's
            var key = Hotkeys.Pressed(k); // with the Korean IME on, Space arrives as ImeProcessed
            if (key != Key.Space && !(enter && key is Key.Enter)) return;
            // held with Alt, Ctrl or Shift it is another key: the window menu, or the Korean IME's language switch
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (!k.IsRepeat) act();
            k.Handled = true;
        };
    }

    /// The last input came from the keyboard: focus that the pointer or the program moved shows no ring. Tests put
    /// their own answer here, as keys they make are not the keyboard's.
    internal static Func<bool> KeyboardLast = () => InputManager.Current.MostRecentInputDevice is KeyboardDevice;

    /// The ring is on the element now.
    internal static bool RingShown(FrameworkElement e) => e.GetValue(StopProperty) is TabStopState { Shown: true };

    /// Pictures and tests: the ring put on or taken off with no key pressed. The element sits under an
    /// AdornerDecorator, as in a window.
    internal static void ShowRing(FrameworkElement e, bool on) => (e.GetValue(StopProperty) as TabStopState)?.Show(on);

    /// One Tab stop: it wears the ring from the moment the keyboard brings the focus to it, or a key is pressed on
    /// it, until the focus leaves. A press of the pointer anywhere in its window hands the focus back to the window,
    /// so no part keeps the keyboard without showing it, and Enter and the arrows are the window's again.
    private sealed class TabStopState
    {
        private readonly FrameworkElement e;
        private readonly CornerRadius radius;
        private readonly Thickness grow;
        private readonly Action<bool>? shown;
        private Ring? ring;
        private UIElement? root;

        public TabStopState(FrameworkElement e, CornerRadius radius, Thickness grow, Action<bool>? shown)
        {
            (this.e, this.radius, this.grow, this.shown) = (e, radius, grow, shown);
            e.IsKeyboardFocusedChanged += (_, a) =>
            {
                if ((bool)a.NewValue) Focused();
                else Left();
            };
            e.PreviewKeyDown += (_, _) =>
            {
                if (e.IsKeyboardFocused) Show(true);
            };
            e.Unloaded += (_, _) => Left();
        }

        public bool Shown => ring is not null;

        private void Focused()
        {
            Show(KeyboardLast());
            if (root is not null) return;
            root = PresentationSource.FromVisual(e)?.RootVisual as UIElement;
            if (root is not null) root.PreviewMouseDown += Pressed;
        }

        private void Left()
        {
            Show(false);
            if (root is null) return;
            root.PreviewMouseDown -= Pressed;
            root = null;
        }

        private void Pressed(object sender, MouseButtonEventArgs args)
        {
            if (!e.IsKeyboardFocused) return;
            if (root is { Focusable: true }) root.Focus();
            else Keyboard.ClearFocus();
        }

        public void Show(bool on)
        {
            if (on == Shown) return;
            if (on)
            {
                if (AdornerLayer.GetAdornerLayer(e) is not { } layer) return;
                ring = new Ring(e, radius, grow);
                layer.Add(ring);
                // a page that scrolls brings the whole ring in, not only the element: it reaches past the element's box.
                // Once the focus has settled, as the element's own request, made as it takes the focus, would take this
                // one's place in the scroll viewer's queue.
                e.Dispatcher.BeginInvoke(() =>
                {
                    if (ring is null) return;
                    double o = Ring.Outside;
                    e.BringIntoView(new Rect(-grow.Left - o, -grow.Top - o, e.RenderSize.Width + grow.Left + grow.Right + 2 * o, e.RenderSize.Height + grow.Top + grow.Bottom + 2 * o));
                });
            }
            else
            {
                (VisualTreeHelper.GetParent(ring!) as AdornerLayer)?.Remove(ring!);
                ring = null;
            }
            shown?.Invoke(on);
        }
    }

    /// The ring: 2 px of the accent, its inner edge 2 px outside the element, drawn over the page so nothing moves.
    private sealed class Ring : Adorner
    {
        private const double Gap = 2, Thick = 2;

        /// How far the ring's outer edge sits past the element's box (and its grow).
        public const double Outside = Gap + Thick;

        private readonly CornerRadius radius;
        private readonly Thickness grow;

        public Ring(FrameworkElement e, CornerRadius radius, Thickness grow) : base(e)
        {
            (this.radius, this.grow) = (radius, grow);
            IsHitTestVisible = false;
            Opacity = 0.7; // the accent's own brush, thinned here: it follows a colour picked while the ring shows
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            double w = size.Width + grow.Left + grow.Right, h = size.Height + grow.Top + grow.Bottom;
            if (w <= 0 || h <= 0) return;
            // the pen runs along the ring's middle, half its width outside the inner edge
            double reach = Gap + Thick / 2;
            var r = new CornerRadius(radius.TopLeft + Thick / 2, radius.TopRight + Thick / 2, radius.BottomRight + Thick / 2, radius.BottomLeft + Thick / 2);
            dc.PushTransform(new TranslateTransform(-grow.Left - reach, -grow.Top - reach));
            dc.DrawGeometry(null, new Pen(Theme.P.Accent, Thick), SmoothBorder.Shape(new Rect(0, 0, w + 2 * reach, h + 2 * reach), r));
            dc.Pop();
        }
    }
}
