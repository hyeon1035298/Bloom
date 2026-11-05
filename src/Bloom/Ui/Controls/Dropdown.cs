using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Search;

namespace Bloom.Ui;

/// A value with a ▾ that opens its options in a list below it (for
/// more options than a segmented choice holds). Click or Enter opens; in the list ↑↓ move, Enter
/// picks, Esc closes; ←→ on the closed control step through the values.
internal sealed class Dropdown : SmoothBorder
{
    private readonly (string value, string label, string? sub)[] options;
    private readonly TextBlock text;
    private readonly System.Windows.Controls.Primitives.Popup popup;
    private readonly Border card;
    private readonly StackPanel list = new();
    private readonly List<Row> rows = new();
    private int sel;

    public string Value { get; private set; }
    public event Action<string>? Picked;
    public bool IsOpen => popup.IsOpen;

    public Dropdown((string value, string label, string? sub)[] options, string value)
    {
        this.options = options;
        Value = value;
        CornerRadius = new CornerRadius(10);
        Background = Theme.P.Well;
        BorderBrush = Theme.P.Line;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(11, 0, 9, 0);
        MinHeight = 24;
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        text = Theme.Label(LabelOf(value), 12, Theme.P.Text, Theme.Medium);
        sp.Children.Add(text);
        var chevron = Theme.Glyph('\uE70D', 9, Theme.P.TextFaint);
        chevron.Margin = new Thickness(9, 0, 0, 0);
        sp.Children.Add(chevron);
        Child = sp;
        var sv = new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Template = Launcher.ThinScroll() };
        card = new SmoothBorder
        {
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0, 6, 0, 6),
            MinWidth = 280,
            Margin = new Thickness(14, 4, 14, 20), // room for the shadow inside the popup
            Child = sv,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.3, Color = Colors.Black },
        };
        card.LayoutTransform = Theme.Zoom; // a popup is a window of its own: Bloom's scale reaches it this way
        popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = card,
            AllowsTransparency = true,
            StaysOpen = false,
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None,
        };
        // a click on the control while the list is open closes the list on its way in; don't reopen it
        long closedAt = 0;
        popup.Closed += (_, _) => closedAt = Environment.TickCount64;
        MouseLeftButtonUp += (_, e) =>
        {
            if (IsOpen) Close();
            else if (Environment.TickCount64 - closedAt > 250) Open();
            e.Handled = true;
        };
    }

    private string LabelOf(string v) => Array.Find(options, o => o.value == v).label ?? v;

    public void Open()
    {
        list.Children.Clear();
        rows.Clear();
        foreach (var (v, label, sub) in options)
        {
            var r = new Row(null, sub is null ? 34 : 46) { Margin = new Thickness(6, 0, 6, 0) };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Theme.T(label, 13, Theme.P.Text, Theme.Medium));
            if (sub is not null)
            {
                var s = Theme.T(sub, 11.5, Theme.P.TextFaint);
                s.Margin = new Thickness(0, 2, 0, 0);
                words.Children.Add(s);
            }
            r.Add(words, new GridLength(1, GridUnitType.Star));
            var check = Theme.Glyph(G.Check, 12, Theme.P.Accent);
            check.Margin = new Thickness(14, 0, 0, 0);
            check.Visibility = v == Value ? Visibility.Visible : Visibility.Hidden;
            r.Add(check, GridLength.Auto);
            string vv = v;
            r.Clicked = () => Pick(vv);
            rows.Add(r);
            list.Children.Add(r);
        }
        sel = Math.Max(0, Array.FindIndex(options, o => o.value == Value));
        // the list's right edge under the control's right edge
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popup.HorizontalOffset = ActualWidth - card.DesiredSize.Width + card.Margin.Right;
        popup.IsOpen = true;
        Paint();
        Dispatcher.BeginInvoke(Paint, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void Close() => popup.IsOpen = false;

    private void Paint()
    {
        for (int i = 0; i < rows.Count; i++) rows[i].Selected = i == sel;
        if (sel >= 0 && sel < rows.Count) rows[sel].BringIntoView();
    }

    private void Pick(string v)
    {
        Close();
        Set(v);
        Picked?.Invoke(v);
    }

    public void Set(string v)
    {
        Value = v;
        text.Text = LabelOf(v);
    }

    public string Step(int delta)
    {
        int i = Array.FindIndex(options, o => o.value == Value);
        Set(options[Math.Clamp(i + delta, 0, options.Length - 1)].value);
        return Value;
    }

    /// Keys while the list is open.
    public bool HandleKey(Key key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case Key.Down:
                sel = (sel + 1) % rows.Count;
                Paint();
                return true;
            case Key.Up:
                sel = (sel - 1 + rows.Count) % rows.Count;
                Paint();
                return true;
            case Key.Enter:
                Pick(options[sel].value);
                return true;
            case Key.Escape:
                Close();
                return true;
        }
        return true; // everything else waits until the list is closed
    }
}
