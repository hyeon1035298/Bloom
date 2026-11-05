using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 일반 > 모양's colour: the dots (색), and under them while 직접 is chosen its picker (직접 고른 색).
internal sealed partial class SettingsView
{
    // what the picker keeps over the redraw a colour change makes
    private bool accentOpen; // 직접 picked with no colour yet: the picker shows before there is one
    private (string key, string? custom)? accentStart; // the colour before the picker opened (처음 색으로)
    private (double h, double s, double v, string kept)? accentAt; // the picker's place, while it made the colour kept
    private string? accentFocus; // the picker's part to give the keyboard back to

    /// QA: the picker's row on the page last built, or -1.
    internal static int AccentRowForQa = -1;

    private void AccentRows(Settings s, string filter)
    {
        var accent = Accents.Current ?? Accents.All[0];
        bool customOk = Accents.TryParse(s.AccentCustom, out var customColour, out _);
        bool dark = Theme.P.Dark;
        string colourTitle = Tr("색", "Colour"), customTitle = Tr("직접 고른 색", "Custom colour");
        string colourSub = Tr($"{accent.Name} · 켠 스위치, 고른 줄, 글자 커서에 쓰입니다", $"{accent.Name} · for switches that are on, the chosen line and the text cursor");
        string customSub = Tr("#RRGGBB 로 적습니다 (예: #3A7BD5) · 라이트는 그 색, 다크는 같은 색을 밝게", "Type #RRGGBB (like #3A7BD5) · the light theme uses it, the dark one a lighter take");
        if (Record(colourTitle, colourSub, filter))
        {
            Record(customTitle, customSub, filter);
            return;
        }
        AccentRowForQa = -1;
        bool custom = s.Accent == Accents.CustomKey && customOk;
        var dots = Accents.All.Select(a => (a.Key, a.Name, Accents.Swatch(a, dark)))
            .Append((Accents.CustomKey, Tr("직접", "Custom"), customOk ? (dark ? Accents.DarkOf(customColour) : customColour) : Color.FromRgb(0x8C, 0x8C, 0x90)));
        var sw = new Swatches(dots, accentOpen && !custom ? Accents.CustomKey : s.Accent == Accents.CustomKey && !customOk ? Accents.All[0].Key : accent.Key, Accents.CustomKey);
        void PickAccent(string v)
        {
            if (v == Accents.CustomKey)
            {
                if (s.Accent != Accents.CustomKey && !accentOpen) accentStart = (s.Accent, s.AccentCustom);
                if (!customOk) // nothing picked yet: the picker opens with the code to type in
                {
                    accentOpen = true;
                    accentFocus = "hex";
                    Build(lastFilter);
                    return;
                }
            }
            else
            {
                accentOpen = false;
                accentStart = null;
                accentAt = null;
            }
            s.Accent = v;
            Settings.Save();
            Theme.Apply(s.Theme); // every window repaints; this list is rebuilt by its window
        }
        sw.Picked += PickAccent;
        Add(colourTitle, colourSub, sw, G.Palette, () =>
        {
            string next = sw.Cycle();
            if (next == Accents.CustomKey && !customOk) // nothing typed in: round to the first
            {
                next = Accents.All[0].Key;
                sw.Set(next);
            }
            PickAccent(next);
        }, d =>
        {
            PickAccent(sw.Step(d));
            return true;
        }, filter);

        if (!custom && !accentOpen && filter.Length == 0) return;
        // the colour the theme shown wears: the custom one, or the accent in use to start from
        var pair = custom ? (Accents.DarkOf(customColour), customColour) : (Accents.Swatch(accent, true), Accents.Swatch(accent, false));
        accentStart ??= (s.Accent, s.AccentCustom);
        (double h, double s, double v)? at = accentAt is { } k && custom && k.kept == s.AccentCustom ? (k.h, k.s, k.v) : null;
        var recent = ColorBook.Current.Recent.Select(ColorBook.Parse).OfType<Color>().ToArray();
        var picker = new AccentPicker(pair, at, dark, recent) { Done = () => FocusFilter?.Invoke() };
        bool Differs((string key, string? custom) now) => accentStart is { } st && (st.key != now.key || (now.key == Accents.CustomKey && st.custom != now.custom));
        var back = ToolUi.TextButton(Tr("처음 색으로", "Back to the first colour"), () =>
        {
            if (accentStart is not { } st) return;
            accentOpen = st.key != Accents.CustomKey; // the picker stays open on the colour it started from
            accentAt = null;
            s.Accent = st.key;
            s.AccentCustom = st.custom;
            Settings.Save();
            Theme.Apply(s.Theme);
        });
        back.VerticalAlignment = VerticalAlignment.Center;
        back.Visibility = Differs((s.Accent, s.AccentCustom)) ? Visibility.Visible : Visibility.Collapsed;
        picker.Moved += light =>
        {
            Theme.P.Recolor(Accents.Custom(light)); // the whole UI follows as it moves; kept when it is let go
            sw.SetColour(Accents.CustomKey, picker.Shown);
            back.Visibility = Differs((Accents.CustomKey, ColorBook.Hex(light))) ? Visibility.Visible : Visibility.Collapsed;
        };
        picker.Picked += light =>
        {
            string kept = ColorBook.Hex(light);
            var (ph, ps, pv) = picker.Hsv;
            accentAt = (ph, ps, pv, kept);
            if (kept == s.AccentCustom && s.Accent == Accents.CustomKey) return;
            accentFocus = picker.FocusedPart;
            accentOpen = false;
            s.AccentCustom = kept;
            s.Accent = Accents.CustomKey;
            Settings.Save();
            Dispatcher.BeginInvoke(() => Theme.Apply(s.Theme)); // after the pointer's or key's own handlers are done
        };
        picker.Eyedrop = async () =>
        {
            var w = Window.GetWindow(this);
            w?.Hide();
            try
            {
                await Task.Delay(150); // the window off the screen before it is frozen
                var c = await Ocr.ColorPick.Pick();
                if (c is { } got) ColorBook.Add(got);
                return c;
            }
            finally
            {
                w?.Show();
                w?.Activate();
            }
        };
        if (accentFocus is { } part)
        {
            accentFocus = null;
            picker.Loaded += (_, _) => Dispatcher.BeginInvoke(() => picker.FocusPart(part), System.Windows.Threading.DispatcherPriority.Input);
        }
        int index = rows.Count;
        if (Place(customTitle, customSub, () => PickerLine(customTitle, back, picker), () => picker.FocusPart("square"), null, filter) is not null)
            AccentRowForQa = index;
    }

    /// 직접 고른 색's line: its title and what it does, 처음 색으로 at the end, the picker under them across the line.
    private static Row PickerLine(string title, FrameworkElement back, AccentPicker picker)
    {
        var r = new Row(null) { Height = double.NaN, MinHeight = 56, Margin = new Thickness(0), Padding = new Thickness(0, 9, 16, 16), Selectable = false };
        var head = new DockPanel();
        DockPanel.SetDock(back, Dock.Right);
        back.Margin = new Thickness(16, 0, 0, 0);
        head.Children.Add(back);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(title, 13, Theme.P.Text, Theme.Medium));
        var sub = Theme.T(Hangul.KeepAll(Tr("지금 테마에서 보일 색을 고릅니다. 다른 테마의 색은 같은 색을 밝게 또는 어둡게 맞춥니다", "Pick the colour this theme shows; the other theme gets the same colour, lighter or darker")), 12, Theme.P.TextFaint);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(sub);
        head.Children.Add(text);
        var body = new StackPanel();
        body.Children.Add(head);
        picker.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(picker);
        r.Add(body, new GridLength(1, GridUnitType.Star));
        return r;
    }
}
