using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Search;

namespace Bloom.Ui;

/// The home's own blocks (not result rows): the AI usage cards and the week strip.
internal static class HomeViews
{
    /// One card per account (Claude, ChatGPT; up to four in a row, more rows below — the user found two by two untidy):
    /// each limit as a thin bar, its percent and how long until it resets. A card names its account in the user's own
    /// words, or by e-mail when one kind has two (AiUsage.Who). `edit` (홈 편집): ‹ › swap a card with its neighbour in
    /// 설정's account order, then Moved draws the home again; ✎ asks Rename for the account's words.
    public static FrameworkElement Usage(List<UsageAccount> accounts, (Action Moved, Action<string> Rename)? edit = null)
    {
        if (accounts.Count == 0)
        {
            var none = Theme.T(AiUsage.Busy ? Tr("AI 사용량 조회 중…", "Checking AI usage…") : Tr("사용량 조회 실패 · Claude Code 또는 Codex 로그인 상태를 확인하고 ↻로 다시 조회하세요", "Could not get usage. Check the Claude Code or Codex sign-in, then press ↻."), 12.5, Theme.P.TextFaint);
            none.Margin = new Thickness(22, 2, 22, 8);
            return none;
        }
        var grid = new Grid { Margin = new Thickness(16, 0, 16, 6) };
        int columns = Math.Min(accounts.Count, PerRow);
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // 홈 편집's tools move and rename accounts: an API key's balance card is none, and stays where it is
        var movable = accounts.Where(a => a.Balance is null).ToList();
        for (int i = 0; i < accounts.Count; i++)
        {
            if (i % PerRow == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var a = accounts[i];
            var card = Card(a, AiUsage.Who(a, accounts), edit is { } e && a.Balance is null ? CardTools(movable, movable.IndexOf(a), e) : null);
            card.Margin = new Thickness(4, i >= PerRow ? 8 : 0, 4, 0);
            Grid.SetColumn(card, i % PerRow);
            Grid.SetRow(card, i / PerRow);
            grid.Children.Add(card);
        }
        return grid;
    }

    private const int PerRow = 4;

    /// 홈 편집's ‹ › ✎ for a card (as a block has ▲ ▼ ✕).
    private static SmoothBorder CardTools(List<UsageAccount> accounts, int i, (Action Moved, Action<string> Rename) edit)
    {
        string id = accounts[i].Id;
        void Swap(int with)
        {
            Core.AiAccounts.Swap(id, accounts[with].Id);
            edit.Moved();
        }
        return EditBar(
            (G.Left, Tr("앞으로", "Move earlier"), i > 0, () => Swap(i - 1)),
            (G.Right, Tr("뒤로", "Move later"), i < accounts.Count - 1, () => Swap(i + 1)),
            (G.Pen, Tr("표시 이름 바꾸기", "Rename"), true, () => edit.Rename(id)));
    }

    /// 홈 편집's small bar of glyph buttons (a block's ▲ ▼ ✕, a usage card's ‹ › ✎), at the right of where it is put.
    public static SmoothBorder EditBar(params (char glyph, string tip, bool enabled, Action run)[] tools) =>
        Pill(tools.Select(t => (UIElement)EditButton(t.glyph, t.tip, t.enabled, t.run)).ToArray());

    /// One of 홈 편집's glyph buttons; `run` null: no click of its own (a handle wired by the caller).
    public static SmoothBorder EditButton(char glyph, string tip, bool enabled, Action? run)
    {
        var g = Theme.Glyph(glyph, 10.5, enabled ? Theme.P.TextDim : Theme.P.Track);
        g.HorizontalAlignment = HorizontalAlignment.Center;
        g.VerticalAlignment = VerticalAlignment.Center;
        var b = new SmoothBorder { Width = 26, Height = 22, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = g, ToolTip = tip, Cursor = enabled ? Cursors.Hand : Cursors.Arrow };
        if (!enabled) return b;
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        if (run is not null)
            b.MouseLeftButtonUp += (_, e) =>
            {
                run();
                e.Handled = true;
            };
        return b;
    }

    /// 홈 편집's − n + on a list block: how many rows it shows (`min`…`max`). `words` (its tip, − and +) for another count
    /// (컬러 팔레트's colours).
    public static SmoothBorder Stepper(int value, int min, int max, Action<int> set, (string tip, string fewer, string more)? words = null)
    {
        var (tip, fewer, more) = words ?? (Tr($"표시 개수 ({min}–{max})", $"Rows shown ({min}–{max})"), Tr("개수 줄이기", "Fewer rows"), Tr("개수 늘리기", "More rows"));
        var n = Theme.T(value.ToString(System.Globalization.CultureInfo.InvariantCulture), 11.5, Theme.P.Text, Theme.Medium, false);
        n.MinWidth = 16;
        n.TextAlignment = TextAlignment.Center;
        n.VerticalAlignment = VerticalAlignment.Center;
        n.ToolTip = tip;
        return Pill(EditButton(G.Minus, fewer, value > min, () => set(value - 1)), n, EditButton(G.Add, more, value < max, () => set(value + 1)));
    }

    /// A dashed rounded outline over the whole of what it is put in (홈 편집's "+ 블록 추가").
    public static System.Windows.Shapes.Rectangle Dashes(Brush line) => new()
    {
        Stroke = line,
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 4, 3 },
        RadiusX = 10,
        RadiusY = 10,
        IsHitTestVisible = false,
    };

    /// "+ 블록 추가" with nothing hidden: says so, takes no clicks (no row: the keys pass it by).
    public static FrameworkElement NoneHidden()
    {
        var g = new Grid { Height = 44, Margin = new Thickness(16, 8, 16, 4) };
        g.Children.Add(Dashes(Theme.P.Line));
        var t = Theme.T(Tr("숨긴 블록 없음 · 블록의 ✕로 숨기면 여기서 다시 추가할 수 있습니다", "No hidden blocks · hide one with its ✕ to add it back here"), 12, Theme.P.TextFaint);
        t.Margin = new Thickness(16, 0, 16, 1);
        g.Children.Add(t);
        return g;
    }

    /// 홈 편집's bar: its buttons in a row on a small popup-coloured plate.
    public static SmoothBorder Pill(params UIElement[] children)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) bar.Children.Add(c);
        return new SmoothBorder
        {
            Child = bar,
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    /// `who`: the line under the name that says whose account it is (AiUsage.Who). `tools` (홈 편집): a row of their own
    /// at the card's foot, over none of its words (on top they hid the plan; the cards were too narrow).
    private static Border Card(UsageAccount a, string? who, FrameworkElement? tools = null)
    {
        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new System.Windows.Documents.Run(a.Name) { FontFamily = Theme.Sans, FontSize = 12.5, FontWeight = Theme.Semi, Foreground = Theme.P.Text });
        if (a.Plan is { Length: > 0 } plan)
            name.Inlines.Add(new System.Windows.Documents.Run("  " + plan) { FontFamily = Theme.Sans, FontSize = 11.5, Foreground = Theme.P.TextFaint });
        head.Children.Add(name);
        if (DateTime.Now - a.Fetched > TimeSpan.FromMinutes(15)) // this side failed since: its numbers are old
        {
            var age = Theme.T(Tr($"{Ago(DateTime.Now - a.Fetched)} 전", $"{Ago(DateTime.Now - a.Fetched)} ago"), 11, Theme.P.TextFaint, Theme.Regular, false);
            age.ToolTip = Tr("마지막으로 받은 때", "Last fetched");
            Grid.SetColumn(age, 1);
            head.Children.Add(age);
        }
        stack.Children.Add(head);
        if (who is { Length: > 0 })
        {
            var mail = Theme.T(who, 11, Theme.P.TextFaint, Theme.Regular, false);
            mail.TextTrimming = TextTrimming.CharacterEllipsis;
            mail.Margin = new Thickness(0, -3, 0, 4);
            stack.Children.Add(mail);
        }
        if (AiUsage.Failed(a.Id) && Core.AiAccounts.Find(a.Id) is { } account)
        {
            var need = Theme.T(Core.AiAccounts.NeedsSignIn(account, false), 11, Theme.P.Danger, Theme.Regular, false);
            need.TextTrimming = TextTrimming.CharacterEllipsis;
            need.Margin = new Thickness(0, -2, 0, 3);
            stack.Children.Add(need);
        }
        bool left = Core.Settings.Current.UsageLeft;
        foreach (var l in a.Limits.Take(3))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Theme.T(l.Title, 11, Theme.P.TextDim);
            row.Children.Add(label);
            var right = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
            right.Inlines.Add(new System.Windows.Documents.Run($"{Shown(l.Percent, left)}%") { FontFamily = Theme.Sans, FontSize = 11.5, FontWeight = Theme.Medium, Foreground = l.Percent >= 90 ? Theme.P.Danger : Theme.P.Text });
            if (left)
                right.Inlines.Add(new System.Windows.Documents.Run(Tr(" 남음", " left")) { FontFamily = Theme.Sans, FontSize = 11, Foreground = Theme.P.TextDim });
            if (l.Resets is { } r)
                right.Inlines.Add(new System.Windows.Documents.Run("  " + Ago(r - DateTime.Now)) { FontFamily = Theme.Sans, FontSize = 11, Foreground = Theme.P.TextFaint });
            Typography.SetNumeralAlignment(right, FontNumeralAlignment.Tabular);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            stack.Children.Add(row);
            stack.Children.Add(Bar(left ? Shown(l.Percent, true) : l.Percent, l.Percent >= 90));
        }
        if (a.Balance is { } balance) // an API key's card: "잔액  $12.34"
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Theme.T(balance.Label, 11, Theme.P.TextDim));
            var amount = Theme.T(balance.Amount, 12.5, Theme.P.Text, Theme.Semi, false);
            Typography.SetNumeralAlignment(amount, FontNumeralAlignment.Tabular);
            Grid.SetColumn(amount, 1);
            row.Children.Add(amount);
            stack.Children.Add(row);
        }
        FrameworkElement body = stack;
        if (tools is not null)
        {
            var g = new Grid(); // the foot row lines up across a row of cards (they stretch to the tallest)
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(stack);
            tools.Margin = new Thickness(0, 6, -6, -4);
            Grid.SetRow(tools, 1);
            g.Children.Add(tools);
            body = g;
        }
        return new SmoothBorder
        {
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 10),
            Child = body,
            ToolTip = a.Balance?.Tip ?? string.Join("\n", a.Limits.Select(l => Tip(l, left))),
        };
    }

    /// A limit's tooltip line: the percent and the reset.
    private static string Tip(UsageLimit l, bool left)
    {
        string line = $"{l.Title}: {Shown(l.Percent, left)}%" + (left ? Tr(" 남음", " left") : "");
        return l.Resets is { } r ? line + Tr($" · {r:M/d HH:mm} 초기화", $" · resets {r:M/d HH:mm}") : line;
    }

    /// The whole percent a card shows: used, or what's left (the two always add up to 100).
    private static double Shown(double used, bool left) => left ? Math.Max(0, 100 - Math.Round(used)) : Math.Round(used);

    /// A thin bar filled to percent; danger = the limit is nearly used up (≥ 90% used), either way it counts.
    internal static FrameworkElement Bar(double percent, bool danger)
    {
        double p = Math.Clamp(percent / 100, 0, 1);
        var g = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(p, 0.0001), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - p, 0.0001), GridUnitType.Star) });
        g.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(2), Background = Theme.P.Track });
        var fill = new SmoothBorder { CornerRadius = new CornerRadius(2), Background = danger ? Theme.P.Danger : Theme.P.Accent, Visibility = p <= 0 ? Visibility.Collapsed : Visibility.Visible };
        Grid.SetColumnSpan(g.Children[0], 2);
        g.Children.Add(fill);
        return g;
    }

    private static string Ago(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return Tr("곧", "now");
        if (t.TotalMinutes < 60) return Tr($"{(int)t.TotalMinutes}분", $"{(int)t.TotalMinutes} min");
        if (t.TotalHours < 24) return Tr($"{(int)t.TotalHours}시간", $"{(int)t.TotalHours} h");
        return Tr($"{(int)t.TotalDays}일", $"{(int)t.TotalDays} d");
    }

    private static readonly string[] Weekdays = { Tr("일", "Su"), Tr("월", "Mo"), Tr("화", "Tu"), Tr("수", "We"), Tr("목", "Th"), Tr("금", "Fr"), Tr("토", "Sa") };

    /// Today and the next six days, each with up to two marks; a click opens the calendar on that day.
    public static FrameworkElement Week(IReadOnlyDictionary<DateTime, List<DayMark>> marks, Action<DateTime> open)
    {
        var grid = new Grid { Margin = new Thickness(16, 0, 16, 6) };
        var today = DateTime.Today;
        for (int i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = today.AddDays(i);
            var stack = new StackPanel { Margin = new Thickness(7, 5, 6, 6) };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            top.Children.Add(Theme.T(Weekdays[(int)day.DayOfWeek], 11, day.DayOfWeek == DayOfWeek.Sunday ? Theme.P.AccentText : Theme.P.TextFaint, Theme.Medium, false));
            var num = Theme.T(day.Day.ToString(), 12, i == 0 ? Theme.P.AccentInk : Theme.P.Text, Theme.Semi, false);
            num.VerticalAlignment = VerticalAlignment.Center;
            if (i == 0)
            {
                num.HorizontalAlignment = HorizontalAlignment.Center;
                top.Children.Add(new SmoothBorder { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Theme.P.Accent, Child = num, Margin = new Thickness(5, -1, 0, 0) });
            }
            else
            {
                num.Margin = new Thickness(5, 0, 0, 0);
                top.Children.Add(num);
            }
            stack.Children.Add(top);
            var list = marks.TryGetValue(day, out var m) ? m : null;
            int shown = Math.Min(2, list?.Count ?? 0);
            for (int k = 0; k < shown; k++)
            {
                var mk = list![k];
                stack.Children.Add(new SmoothBorder
                {
                    Background = mk.Deadline ? Theme.P.AccentSoft : Theme.P.Tile,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 0, 3, 1),
                    Margin = new Thickness(0, 2, 0, 0),
                    Child = Theme.T(mk.Label, 10.5, mk.Deadline ? Theme.P.AccentText : Theme.P.TextDim, Theme.Medium),
                });
            }
            if (list is not null && list.Count > shown)
            {
                var more = Theme.T($"+{list.Count - shown}", 10.5, Theme.P.TextFaint, Theme.Medium, false);
                more.Margin = new Thickness(2, 2, 0, 0);
                stack.Children.Add(more);
            }
            if (shown == 0)
            {
                var free = Theme.T("—", 10.5, Theme.P.Track, Theme.Regular, false);
                free.Margin = new Thickness(2, 2, 0, 0);
                stack.Children.Add(free);
            }
            var cell = new SmoothBorder
            {
                Child = stack,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(2, 0, 2, 0),
                Background = i == 0 ? Theme.P.Card : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = list is { Count: > 0 } ? string.Join("\n", list.Select(x => (x.Deadline ? Tr($"마감 · {x.Label}", $"Due · {x.Label}") : Tr($"할 일 · {x.Label}", $"To do · {x.Label}")))) : null,
            };
            var bg = cell.Background;
            cell.MouseEnter += (_, _) => cell.Background = Theme.P.Hover;
            cell.MouseLeave += (_, _) => cell.Background = bg;
            var d = day;
            cell.MouseLeftButtonUp += (_, e) =>
            {
                open(d);
                e.Handled = true;
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }
}
