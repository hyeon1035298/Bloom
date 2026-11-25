using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

/// The looks page's colour: the dots, and under their row the picker a click on 직접 opens (the same one as 설정 >
/// 일반 > 직접 고른 색). The picker is the window's, not the page's: every colour it keeps builds the page again.
internal sealed partial class OnboardingWindow
{
    private Swatches? accentDots; // the dots of the page shown
    private FrameworkElement? accentRow; // their row: the picker opens under it
    private Popup? accentPopup;
    private AccentPicker? accentPicker; // the picker in it
    private bool accentDark; // the theme the open picker was made for
    private bool accentReopen; // a colour is being picked from the screen: the picker comes back with it

    /// The colour dots, with 직접 at the end in the colour kept for it (gray while there is none).
    private Swatches AccentDots(Settings s)
    {
        bool dark = Theme.P.Dark;
        bool customOk = Accents.TryParse(s.AccentCustom, out var custom, out _);
        string first = Accents.All[0].Key;
        string current = s.Accent == Accents.CustomKey && !customOk ? first : (Accents.Current ?? Accents.All[0]).Key;
        var dots = new Swatches(Accents.All.Select(a => (a.Key, a.Name, Accents.Swatch(a, dark)))
            .Append((Accents.CustomKey, Tr("직접", "Custom"), customOk ? (dark ? Accents.DarkOf(custom) : custom) : Color.FromRgb(0x8C, 0x8C, 0x90))), current, Accents.CustomKey);
        void Pick(string v)
        {
            s.Accent = v;
            Settings.Save();
            Theme.Apply(s.Theme); // every window repaints; this page is built again by its window
        }
        // a click on 직접 opens its picker, on the colour kept for it when there is one
        dots.Picked += v =>
        {
            if (v != Accents.CustomKey)
            {
                CloseAccent();
                Pick(v);
                return;
            }
            if (customOk) Pick(v);
            else dots.Set(current); // no colour of its own yet: the ring stays where it was until one is picked
            OpenAccent();
        };
        // the keys go over the colours there are: 직접 without a colour is passed by
        void Step(int d)
        {
            string was = dots.Value, v = dots.Step(d);
            if (v == Accents.CustomKey && !customOk) dots.Set(v = was);
            if (v != was) Pick(v);
        }
        void Cycle()
        {
            string v = dots.Cycle();
            if (v == Accents.CustomKey && !customOk) dots.Set(v = first);
            Pick(v);
        }
        choices[dots] = (Step, Cycle);
        return dots;
    }

    private void OpenAccent()
    {
        CloseAccent();
        if (accentRow is not { } row) return;
        root.UpdateLayout(); // a colour just applied built the page again: its row has no place yet
        if (PresentationSource.FromVisual(row) is null) return;
        var s = Settings.Current;
        bool dark = accentDark = Theme.P.Dark;
        var accent = Accents.Current ?? Accents.All[0];
        // the colour the theme shown wears: the custom one, or the accent in use to start from
        var pair = s.Accent == Accents.CustomKey && Accents.TryParse(s.AccentCustom, out var kept, out _)
            ? (Accents.DarkOf(kept), kept)
            : (Accents.Swatch(accent, true), Accents.Swatch(accent, false));
        // as many recent picks as the picker's side has room for under this window
        var recent = ColorBook.Current.Recent.Select(ColorBook.Parse).OfType<Color>().Take(6).ToArray();
        var picker = new AccentPicker(pair, null, dark, recent) { Done = CloseAccent };
        picker.Moved += light =>
        {
            Theme.P.Recolor(Accents.Custom(light)); // the whole window follows as it moves; kept when it is let go
            accentDots?.SetColour(Accents.CustomKey, picker.Shown);
        };
        picker.Picked += light =>
        {
            string code = ColorBook.Hex(light);
            var now = Settings.Current;
            bool reopen = accentReopen;
            accentReopen = false;
            if (code == now.AccentCustom && now.Accent == Accents.CustomKey && !reopen) return;
            now.AccentCustom = code;
            now.Accent = Accents.CustomKey;
            Settings.Save();
            // after the pointer's or key's own handlers are done
            Dispatcher.BeginInvoke(() =>
            {
                Theme.Apply(Settings.Current.Theme);
                if (reopen && accentPopup is null && StepId == TutorialSteps.Looks) OpenAccent();
            });
        };
        picker.Eyedrop = async () =>
        {
            // the screen is picked from with this window out of the way, and the picker with it
            CloseAccent();
            var c = await AccentPicker.FromScreen(this);
            accentReopen = c is not null;
            return c;
        };

        // The menus' colour without their see-through: this card is large and lies over the page's switch and the
        // footer's buttons, which would show through it. The colour is the theme's as it opens; a switch to the other
        // theme closes the picker (OnTheme), and the next one opens in that theme's.
        var fill = Theme.P.Popup.Color;
        var ground = new SolidColorBrush(Color.FromRgb(fill.R, fill.G, fill.B));
        ground.Freeze();
        var card = new SmoothBorder
        {
            Background = ground,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 16, 16, 14),
            Width = AccentWidth,
            Margin = new Thickness(ShadowSide, 4, ShadowSide, 20), // room for the shadow inside the popup
            Child = picker,
            UseLayoutRounding = true,
        };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        card.LiftMenu();
        // its field and pads read their own keys (Tag), Esc among them; Esc elsewhere in it closes it
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { Tag: Func<Key, bool> own })
            {
                if (own(e.Key)) e.Handled = true;
                return;
            }
            if (e.Key != Key.Escape) return;
            CloseAccent();
            e.Handled = true;
        };
        // under the row, as wide as the window lets it be: the row is the page's width, the picker needs a little more
        var under = row.TranslatePoint(new Point(0, row.ActualHeight), scroll);
        // It stays open until the window closes it (AccentFollows, Esc): a popup that closes itself does so by holding
        // the mouse, which it loses whenever the page under it is built again, and that is at every colour kept.
        var popup = new Popup
        {
            Child = card,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None,
            PlacementTarget = scroll, // the row is replaced with its page at every colour kept
            Placement = PlacementMode.Relative,
            HorizontalOffset = (ClientWidth - AccentWidth) / 2 - ShadowSide,
            VerticalOffset = under.Y + 2,
        };
        accentPopup = popup;
        accentPicker = picker;
        popup.IsOpen = true;
    }

    private const double AccentWidth = 600, ShadowSide = 14;

    /// What closes the picker besides Esc: a press anywhere in the window outside it, the window moved, and another
    /// program coming forward (a popup floats over every window).
    private void AccentFollows()
    {
        PreviewMouseDown += (_, _) =>
        {
            if (accentPopup is { Child: UIElement { IsMouseOver: false } }) CloseAccent();
        };
        LocationChanged += (_, _) => CloseAccent();
        Deactivated += (_, _) => CloseAccent();
    }

    private void CloseAccent()
    {
        if (accentPopup is not { } popup) return;
        var picker = accentPicker;
        accentPopup = null;
        accentPicker = null;
        // a colour nudged with the keys is kept before the picker goes, so a dot picked right after it is not
        // overwritten by that colour when its pause would have passed
        picker?.Settle();
        popup.IsOpen = false;
        if (IsVisible && IsActive && !IsKeyboardFocusWithin) Focus(); // Enter and the arrows are the window's again
    }
}
