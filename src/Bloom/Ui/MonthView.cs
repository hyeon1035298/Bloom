using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// One mark in a day cell: a deadline (rose) or a dated to-do (neutral).
internal readonly record struct DayMark(string Label, bool Deadline, bool Hit);

/// The calendar view's month: a header with the month and ‹ 오늘 ›, the weekdays, the day cells with up to
/// two marks each. The selected day has the rose frame, today the rose number. Click picks a day.
internal sealed class MonthView : SmoothBorder
{
    private static readonly string[] Weekdays = { Tr("일", "Sun"), Tr("월", "Mon"), Tr("화", "Tue"), Tr("수", "Wed"), Tr("목", "Thu"), Tr("금", "Fri"), Tr("토", "Sat") };

    public Action<DateTime>? DayClicked;
    public Action<int>? MonthStepped;
    public Action? TodayClicked;

    public MonthView()
    {
        Padding = new Thickness(14, 10, 10, 12);
    }

    public void Show(DateTime month, DateTime selected, IReadOnlyDictionary<DateTime, List<DayMark>> marks)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        int offset = (int)first.DayOfWeek;
        int days = DateTime.DaysInMonth(month.Year, month.Month);
        int weeks = (offset + days + 6) / 7;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // header: 2026년 10월 · n개      ‹  오늘  ›
        var head = new Grid { Margin = new Thickness(4, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Theme.T(month.ToString(Tr("yyyy년 M월", "MMM yyyy"), Culture), 15, Theme.P.Text, Theme.Semi, false));
        int count = 0;
        foreach (var (d, list) in marks)
            if (d.Year == month.Year && d.Month == month.Month) count += list.Count;
        if (count > 0)
        {
            var c = Theme.T(count == 1 ? Tr("일정 1개", "1 event") : Tr($"일정 {count}개", $"{count} events"), 12, Theme.P.TextFaint, Theme.Regular, false);
            c.Margin = new Thickness(10, 2, 0, 0);
            c.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(c);
        }
        head.Children.Add(title);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        nav.Children.Add(NavButton(Theme.Glyph('', 10, Theme.P.TextDim), Tr("지난달 (PgUp)", "Previous Month (PgUp)"), () => MonthStepped?.Invoke(-1)));
        nav.Children.Add(NavButton(Theme.T(Tr("오늘", "Today"), 12, Theme.P.TextDim, Theme.Medium, false), Tr("오늘로 (Home)", "Go to Today (Home)"), () => TodayClicked?.Invoke()));
        nav.Children.Add(NavButton(Theme.Glyph('', 10, Theme.P.TextDim), Tr("다음 달 (PgDn)", "Next Month (PgDn)"), () => MonthStepped?.Invoke(1)));
        Grid.SetColumn(nav, 1);
        head.Children.Add(nav);
        root.Children.Add(head);

        var names = new UniformGrid7();
        for (int i = 0; i < 7; i++)
        {
            var w = Theme.T(Weekdays[i], 11, i == 0 ? Theme.P.AccentText : Theme.P.TextFaint, Theme.Medium, false);
            w.Margin = new Thickness(6, 0, 0, 4);
            names.Add(w, i, 0);
        }
        Grid.SetRow(names, 1);
        root.Children.Add(names);

        var grid = new Grid();
        for (int i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int w = 0; w < weeks; w++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var today = DateTime.Today;
        for (int i = 0; i < weeks * 7; i++)
        {
            var day = first.AddDays(i - offset);
            bool inMonth = day.Month == month.Month;
            var cell = Cell(day, inMonth, day == today, day == selected.Date, marks.TryGetValue(day, out var m) ? m : null, weeks >= 6 ? 1 : 2);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, i / 7);
            grid.Children.Add(cell);
        }
        Grid.SetRow(grid, 2);
        root.Children.Add(grid);
        Child = root;
    }

    /// A day: its number (today in a rose dot) with "+n" on the right, then up to `room` marks.
    private FrameworkElement Cell(DateTime day, bool inMonth, bool today, bool selected, List<DayMark>? marks, int room)
    {
        var stack = new StackPanel { Margin = new Thickness(5, 2, 4, 1) };
        var top = new Grid { Height = 17 };
        var n = Theme.T(day.Day.ToString(), 11.5, today ? Theme.P.AccentInk : inMonth ? (day.DayOfWeek == DayOfWeek.Sunday ? Theme.P.AccentText : Theme.P.Text) : Theme.P.TextFaint, today || selected ? Theme.Semi : Theme.Medium, false);
        n.VerticalAlignment = VerticalAlignment.Center;
        if (today)
        {
            n.HorizontalAlignment = HorizontalAlignment.Center;
            top.Children.Add(new SmoothBorder { Width = 17, Height = 17, CornerRadius = new CornerRadius(8.5), Background = Theme.P.Accent, Child = n, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-3, 0, 0, 0) });
        }
        else
        {
            n.Margin = new Thickness(1, 0, 0, 0);
            top.Children.Add(n);
        }
        int shown = Math.Min(room, marks?.Count ?? 0);
        if (marks is not null && marks.Count > shown)
        {
            var more = Theme.T($"+{marks.Count - shown}", 10, Theme.P.TextFaint, Theme.Medium, false);
            more.HorizontalAlignment = HorizontalAlignment.Right;
            more.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(more);
        }
        stack.Children.Add(top);
        for (int i = 0; i < shown; i++)
        {
            var m = marks![i];
            var t = Theme.T(m.Label, 10, m.Deadline ? Theme.P.AccentText : Theme.P.TextDim, Theme.Medium);
            stack.Children.Add(new SmoothBorder
            {
                Background = m.Deadline ? Theme.P.AccentSoft : Theme.P.Tile,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 0, 3, 1),
                Margin = new Thickness(0, 1.5, 0, 0),
                Opacity = m.Hit ? 1 : 0.35,
                Child = t,
            });
        }
        var b = new SmoothBorder
        {
            Child = stack,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(1.5),
            ClipToBounds = true,
            Background = selected ? Theme.P.Selected : Brushes.Transparent,
            BorderBrush = selected ? Theme.P.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Opacity = inMonth ? 1 : 0.55,
            Cursor = Cursors.Hand,
        };
        if (!selected)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            DayClicked?.Invoke(day);
            e.Handled = true;
        };
        return b;
    }

    private static FrameworkElement NavButton(FrameworkElement content, string tip, Action click)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var b = new SmoothBorder
        {
            Child = content,
            MinWidth = 26,
            Height = 24,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Margin = new Thickness(2, 0, 0, 0),
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// A 7-column grid row (the weekday names).
    private sealed class UniformGrid7 : Grid
    {
        public UniformGrid7()
        {
            for (int i = 0; i < 7; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public void Add(UIElement el, int col, int row)
        {
            SetColumn(el, col);
            Children.Add(el);
        }
    }
}
