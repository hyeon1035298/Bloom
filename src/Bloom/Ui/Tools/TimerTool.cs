using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 타이머 · 알림: the length as "00일 00시간 10분 00초" (▲ ▼, the wheel, the arrow keys or digits), what it is for
/// underneath, and 시작; with no length set the line is read in words as before ("10분", "오후 3시 회의"). Below, the
/// ones waiting with the time left.
internal sealed class TimerTool : ITool
{
    private readonly Grid view = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox input, days, hours, minutes, seconds;
    private readonly TextBlock note;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public string Key => "timers";
    public string Name => Tr("타이머 · 알림", "Timers · Reminders");
    public char Glyph => G.Stopwatch;
    public FrameworkElement View => view;
    public FrameworkElement? Head => null;
    public string? State => null;

    public TimerTool()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var units = new Grid { Margin = new Thickness(12, 10, 12, 0) };
        days = Unit(units, Tr("일", "days"), 99);
        hours = Unit(units, Tr("시간", "hr"), 23);
        minutes = Unit(units, Tr("분", "min"), 59);
        seconds = Unit(units, Tr("초", "sec"), 59);
        view.Children.Add(units);

        var line = new Grid { Margin = new Thickness(12, 8, 12, 2) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (field, box) = ToolUi.Field(Tr("내용 (또는 ‘오후 3시 회의’)", "Label (or 'Meeting at 3 PM')"));
        input = box;
        line.Children.Add(field);
        var start = StartButton();
        Grid.SetColumn(start, 1);
        line.Children.Add(start);
        Grid.SetRow(line, 1);
        view.Children.Add(line);

        note = Theme.T("", 11.5, Theme.P.TextFaint);
        note.Margin = new Thickness(16, 4, 16, 0);
        note.Visibility = Visibility.Collapsed;
        Grid.SetRow(note, 2);
        view.Children.Add(note);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 3);
        view.Children.Add(scroll);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            Start();
        };
        // typing or turning again: the last word goes
        input.TextChanged += (_, _) => Heard();
        tick.Tick += (_, _) => Tick();
        view.IsVisibleChanged += (_, _) =>
        {
            if (view.IsVisible) tick.Start();
            else tick.Stop();
        };
        Timers.Changed += OnChanged;
        Fill();
    }

    private void OnChanged() => view.Dispatcher.BeginInvoke(Fill);

    /// A line under the field (what started, or what it couldn't read) until the next change.
    private void Say(string text)
    {
        note.Text = text;
        note.Visibility = Visibility.Visible;
    }

    private void Heard() => note.Visibility = Visibility.Collapsed;

    /// 시작: the length set above, for what the line says (the units stay for the next one); with none set, the line
    /// in words.
    private void Start()
    {
        string text = input.Text.Trim();
        var length = new TimeSpan(Value(days), Value(hours), Value(minutes), Value(seconds));
        if (length > TimeSpan.Zero)
        {
            var end = DateTime.Now + length;
            Timers.Add("timer", text.Length > 0 ? text : Tr($"{Timers.DurationText(length)} 타이머", $"{Timers.DurationText(length)} timer"), end);
            input.Clear();
            Say(Tr($"타이머 시작 · {Timers.WhenText(end)} 종료", $"Timer started · Ends at {Timers.WhenText(end)}"));
            return;
        }
        if (text.Length == 0)
        {
            Say(Tr("시간을 맞추거나 ‘10분’, ‘오후 3시 회의’처럼 입력하세요", "Set a duration or type like '10m' or 'Meeting at 3 PM'"));
            return;
        }
        // "10" alone reads as minutes here: this line only ever starts timers
        if (int.TryParse(text, out int n) && n is > 0 and <= 1440) text += Tr("분", " min");
        // this line only starts timers and reminders, so a time needs no 타이머 / 알림 word ("10분 회의", "오후 3시 회의")
        if (TimeParse.Read(text, timerField: true) is not { } ask)
        {
            Say(Tr("시간 인식 실패 · 위에서 시간을 맞추거나 ‘10분’, ‘오후 3시 회의’처럼 입력하세요", "Could not recognize time · Set a time above or type like '10m' or 'Meeting at 3 PM'"));
            return;
        }
        var due = ask.Length is { } l ? DateTime.Now + l : ask.Due;
        Timers.Add(ask.Kind, ask.Label, due);
        input.Clear();
        Say(ask.Kind == "timer" ? Tr($"타이머 시작 · {Timers.WhenText(due)} 종료", $"Timer started · Ends at {Timers.WhenText(due)}") : Tr($"{Timers.WhenText(due)} 알림 설정됨", $"Reminder set for {Timers.WhenText(due)}"));
    }

    /// One unit of the length in its own column: a number with its name after it ("10분") that ▲ ▼, the wheel and the
    /// arrow keys change (wrapping past 0 and `max`), digits replace, and Enter starts.
    private TextBox Unit(Grid units, string name, int max)
    {
        int column = units.ColumnDefinitions.Count;
        units.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var box = DigitBox.Make("00", 2, 17, Theme.Semi, 21);
        box.Tag = max;
        void Step(int by)
        {
            box.Text = ((Value(box) + by + max + 1) % (max + 1)).ToString("00");
            box.SelectAll();
            Heard();
        }
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Up: Step(1); break;
                case System.Windows.Input.Key.Down: Step(-1); break;
                case System.Windows.Input.Key.Enter: Start(); break;
                default: return;
            }
            e.Handled = true;
        };
        box.TextChanged += (_, _) => Heard();
        // a click selects the number, so typing replaces it
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (box.IsKeyboardFocusWithin) return;
            box.Focus();
            e.Handled = true;
        };
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.LostKeyboardFocus += (_, _) => box.Text = Value(box).ToString("00");

        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        arrows.Children.Add(Arrow(G.Up, $"{name} +1", () => Step(1)));
        arrows.Children.Add(Arrow(G.Down, $"{name} −1", () => Step(-1)));
        var label = Theme.T(name, 11.5, Theme.P.TextDim, Theme.Medium, false);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(2, 1, 2, 0);
        // the number and its name together in the middle, ▲ ▼ at the end
        var reading = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        reading.Children.Add(box);
        reading.Children.Add(label);
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.Children.Add(reading);
        Grid.SetColumn(arrows, 1);
        inner.Children.Add(arrows);
        var well = new SmoothBorder { Background = Theme.P.Well, CornerRadius = new CornerRadius(8), Padding = new Thickness(2, 3, 2, 3), Child = inner, Cursor = Cursors.IBeam };
        well.MouseLeftButtonDown += (_, _) => box.Focus();
        well.MouseWheel += (_, e) =>
        {
            Step(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
        well.Margin = new Thickness(column == 0 ? 0 : 4, 0, 0, 0);
        Grid.SetColumn(well, column);
        units.Children.Add(well);
        return box;
    }

    /// A ▲ or ▼ beside a unit's number.
    private static SmoothBorder Arrow(char glyph, string tip, Action click)
    {
        var b = new SmoothBorder { Width = 15, Height = 13, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = Theme.Glyph(glyph, 8, Theme.P.TextDim) };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // the well would take the focus to the number
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// 시작, beside the line.
    private SmoothBorder StartButton()
    {
        var b = new SmoothBorder
        {
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(8),
            Background = Theme.P.AccentSoft,
            Cursor = Cursors.Hand,
            ToolTip = "Enter",
            Child = Theme.T(Tr("시작", "Start"), 13, Theme.P.AccentText, Theme.Semi),
        };
        ((TextBlock)b.Child).VerticalAlignment = VerticalAlignment.Center;
        b.MouseEnter += (_, _) => b.Background = Theme.P.Selected;
        b.MouseLeave += (_, _) => b.Background = Theme.P.AccentSoft;
        b.MouseLeftButtonUp += (_, e) =>
        {
            Start();
            e.Handled = true;
        };
        return b;
    }

    /// A unit's number: 0 when empty, its most (Tag) at most.
    private static int Value(TextBox box) => int.TryParse(box.Text, out int v) ? Math.Clamp(v, 0, (int)box.Tag) : 0;

    /// Every second: only the time left changes (the rows stay, so a button under the mouse stays lit).
    private void Tick()
    {
        foreach (var (t, left) in shown) left.Text = Timers.LeftText(t.Left);
    }

    private readonly System.Collections.Generic.List<(TimerEntry t, TextBlock left)> shown = new();

    private void Fill()
    {
        list.Children.Clear();
        shown.Clear();
        var waiting = Timers.Waiting.ToList();
        if (waiting.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("진행 중인 타이머·알림 없음", "No active timers or reminders")));
            return;
        }
        foreach (var t in waiting)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            var left = Theme.T(Timers.LeftText(t.Left), 15, Theme.P.Text, Theme.Semi);
            shown.Add((t, left));
            text.Children.Add(left);
            var sub = Theme.T($"{t.Label} · {Timers.WhenText(t.DueAt)}", 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            g.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string id = t.Id;
            buttons.Children.Add(ToolUi.Button(G.Add, Tr("5분 더", "+5 min"), () => Timers.Extend(id, TimeSpan.FromMinutes(5))));
            buttons.Children.Add(ToolUi.Button(G.Close, Tr("취소", "Cancel"), () => Timers.Remove(id)));
            Grid.SetColumn(buttons, 1);
            g.Children.Add(buttons);
            list.Children.Add(ToolUi.Row(g, null));
        }
    }

    /// The keyboard goes to 분 (shown for the AI beside a chat, it stays in the chat).
    public void FocusFirst() => minutes.Dispatcher.BeginInvoke(() =>
    {
        if (Window.GetWindow(minutes) is { IsActive: false }) return;
        minutes.Focus();
        Keyboard.Focus(minutes);
    }, DispatcherPriority.Input);

    public void Closed()
    {
        tick.Stop();
        Timers.Changed -= OnChanged;
    }
}
