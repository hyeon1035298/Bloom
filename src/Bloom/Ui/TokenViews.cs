using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Ui;

/// The token blocks of the home (Ai\TokenStats): the sums as tiles and a ratio bar, a bar chart over the period, the
/// models that used the most and at what effort, the sessions that used the most. Each lays out a TokenView it is given
/// and reads nothing else, so the kept home's key changes only with the numbers; every size is a star column or row, so
/// a block fits two columns (~360) and one (~700).
internal static class TokenViews
{
    /// 토큰 사용량: four tiles (cost, tokens, calls, cache hits), the prompt and output split as one bar with its legend,
    /// and a line for the models with no price. `noLogs`: no log folder at all; `counting`: the first count is not done,
    /// so an empty period reads "세는 중…", not "기록 없음" (the other blocks take the two the same way).
    public static FrameworkElement Summary(TokenView v, bool noLogs, bool counting, Action<TokenPeriod> pick)
    {
        var stack = Body();
        if (noLogs) return Empty(stack, NoLogsWords);
        stack.Children.Add(Periods(v.Period, pick, v.Sum.Calls > 0 ? Range(v) : null));
        if (v.Sum.Calls == 0) return Empty(stack, Nothing(counting));
        var tiles = new Grid { Margin = new Thickness(-3, 0, -3, 0) };
        var s = v.Sum;
        var cost = Tile(Tr("환산액", "Cost"), Money(v.Cost, true),
            Tr($"API 가격으로 환산 {Money(v.Cost, false)}", $"At API prices {Money(v.Cost, false)}") + (v.Unpriced > 0 ? "\n" + UnpricedWords(v) : ""));
        var tokens = Tile(Tr("토큰", "Tokens"), Count(s.Total), Tr($"토큰 {Whole(s.Total)}", $"{Whole(s.Total)} tokens"));
        var calls = Tile(Tr("호출", "Calls"), Count(s.Calls), Tr($"호출 {Whole(s.Calls)}", $"{Whole(s.Calls)} calls"));
        var hits = Tile(Tr("캐시 적중", "Cache hits"), Percent(s.HitRate),
            Tr($"보낸 입력 {Whole(s.Prompt)} 중 캐시에서 읽음 {Whole(s.CacheRead)}", $"{Whole(s.CacheRead)} of {Whole(s.Prompt)} prompt tokens read from the cache"));
        var all = new[] { cost, tokens, calls, hits };
        for (int i = 0; i < all.Length; i++)
        {
            tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(all[i], i);
            tiles.Children.Add(all[i]);
        }
        stack.Children.Add(tiles);
        stack.Children.Add(Split(s));
        if (v.Unpriced > 0)
        {
            var line = Theme.T(UnpricedWords(v), 11, Theme.P.TextFaint);
            line.Margin = new Thickness(0, 6, 0, 0);
            line.ToolTip = Tr("설정 > 홈 화면 > 가격표 고치기에서 가격을 넣을 수 있습니다", "Add a price in Settings > Home > Edit prices");
            stack.Children.Add(line);
        }
        return stack;
    }

    /// 토큰 그래프: the period's bars, each as tall as its tokens against the tallest (a grid of star rows, so new numbers
    /// draw it anew), the first and last bars' times under it.
    public static FrameworkElement Chart(TokenView v, bool noLogs, bool counting, Action<TokenPeriod> pick)
    {
        var stack = Body();
        if (noLogs) return Empty(stack, NoLogsWords);
        long peak = v.Bars.Count == 0 ? 0 : v.Bars.Max(b => b.Counts.Total);
        string? caption = v.Sum.Calls > 0 ? Tr($"토큰 {Count(v.Sum.Total)} · {Money(v.Cost, true)}", $"{Count(v.Sum.Total)} tokens · {Money(v.Cost, true)}") : null;
        stack.Children.Add(Periods(v.Period, pick, caption));
        if (v.Sum.Calls == 0 || v.Bars.Count == 0) return Empty(stack, Nothing(counting));
        var chart = new Grid { Height = 72 };
        // the floor under the bars
        var floor = new Border { Height = 1, Background = Theme.P.Line, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(floor, v.Bars.Count);
        chart.Children.Add(floor);
        double gap = v.Bars.Count > 40 ? 0.5 : v.Bars.Count > 20 ? 1 : 2;
        for (int i = 0; i < v.Bars.Count; i++)
        {
            var bar = v.Bars[i];
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            double h = peak == 0 ? 0 : (double)bar.Counts.Total / peak;
            var column = new Grid { Background = Brushes.Transparent, ToolTip = BarTip(bar, v.Unit) };
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(1 - h, 0.0001), GridUnitType.Star) });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(h, 0.0001), GridUnitType.Star) });
            var fill = new SmoothBorder
            {
                Background = Theme.P.Accent,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                Margin = new Thickness(gap / 2, 0, gap / 2, 0),
                MinHeight = bar.Counts.Total > 0 ? 2 : 0,
                Visibility = bar.Counts.Total > 0 ? Visibility.Visible : Visibility.Collapsed,
            };
            Grid.SetRow(fill, 1);
            column.Children.Add(fill);
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
        stack.Children.Add(chart);
        var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        axis.Children.Add(Theme.T(When(v.Bars[0].Start, v.Unit), 10.5, Theme.P.TextFaint));
        var last = Theme.T(v.Unit == TokenUnit.Hour ? Tr("지금", "Now") : When(v.Bars[^1].Start, v.Unit), 10.5, Theme.P.TextFaint, null, false);
        Grid.SetColumn(last, 1);
        axis.Children.Add(last);
        stack.Children.Add(axis);
        return stack;
    }

    /// 모델별 사용량: the models with the most tokens, `rows` of them (name, where it was used, tokens, share and cost,
    /// a bar of the share), never more; the rest as one line, a single leftover model too.
    public static FrameworkElement Models(TokenView v, bool noLogs, bool counting, int rows, Action<TokenPeriod> pick)
    {
        var stack = Body();
        if (noLogs) return Empty(stack, NoLogsWords);
        int n = v.Models.Count;
        string? caption = n == 0 ? null : n == 1 ? Tr("모델 1개", "1 model") : Tr($"모델 {n}개", $"{n} models");
        stack.Children.Add(Periods(v.Period, pick, caption));
        if (v.Sum.Calls == 0 || v.Models.Count == 0) return Empty(stack, Nothing(counting));
        int shown = Math.Min(Math.Max(rows, 1), v.Models.Count); // the count 홈 편집's − n + shows, as the list blocks
        Grid.SetIsSharedSizeScope(stack, true); // the rows' numbers line up as columns
        for (int i = 0; i < shown; i++) stack.Children.Add(ModelRow(v.Models[i], i == 0));
        if (shown < v.Models.Count)
        {
            var rest = v.Models.Skip(shown).ToList();
            stack.Children.Add(RestLine(RestWords(rest.Count, rest.Sum(m => m.Counts.Total), rest.Sum(m => m.Cost ?? 0), rest.Count(m => m.Cost is null)),
                string.Join("\n", rest.Select(m => $"{Pretty(m.Model)}  {Count(m.Counts.Total)}  {(m.Cost is { } c ? Money(c, true) : "—")}"))));
        }
        return stack;
    }

    /// 세션별 사용량: the sessions with the most tokens, `rows` of them (name, where and at what effort, tokens, share and
    /// cost, a bar of the share), never more; the rest as one line, a single leftover session too; then what no session
    /// holds. `named`: each session's name and, for a Bloom chat still kept, how to open it, in v.Sessions' order, for
    /// the rows and the rest's tooltip at least (Engine names them; a session past it gets no name of a chat).
    public static FrameworkElement Sessions(TokenView v, IReadOnlyList<(string Name, Action? Open)> named, bool noLogs, bool counting, int rows, Action<TokenPeriod> pick)
    {
        var stack = Body();
        if (noLogs) return Empty(stack, NoLogsWords);
        int n = v.Sessions.Count;
        string? caption = n == 0 ? null : n == 1 ? Tr("세션 1개", "1 session") : Tr($"세션 {n}개", $"{n} sessions");
        stack.Children.Add(Periods(v.Period, pick, caption));
        if (v.Sum.Calls == 0 || n == 0) return Empty(stack, Nothing(counting));
        (string Name, Action? Open) Named(int i) => i < named.Count ? named[i] : (SessionName(v.Sessions[i], null), null);
        int shown = Math.Min(Math.Max(rows, 1), n);
        Grid.SetIsSharedSizeScope(stack, true);
        for (int i = 0; i < shown; i++)
        {
            var (name, open) = Named(i);
            stack.Children.Add(SessionRow(v.Sessions[i], name, open, i == 0));
        }
        if (shown < n)
        {
            var rest = v.Sessions.Skip(shown).ToList();
            var tip = new List<string>();
            for (int i = shown; i < Math.Min(n, shown + RestTipMost); i++)
            {
                var s = v.Sessions[i];
                tip.Add($"{Named(i).Name}  {Count(s.Counts.Total)}  {(s.Cost is { } c ? Money(c, true) : "—")}");
            }
            if (n - shown > RestTipMost) tip.Add(Tr($"… 외 {n - shown - RestTipMost}개", $"… {n - shown - RestTipMost} more"));
            stack.Children.Add(RestLine(RestWords(rest.Count, rest.Sum(s => s.Counts.Total), rest.Sum(s => s.Cost ?? 0), rest.Count(s => s.Cost is null)), string.Join("\n", tip)));
        }
        if (v.NoSession.Calls > 0)
        {
            var line = Theme.T(Tr($"세션 구분 없음 · 토큰 {Count(v.NoSession.Total)}", $"No session · {Count(v.NoSession.Total)} tokens"), 11, Theme.P.TextFaint);
            line.Margin = new Thickness(0, shown < n ? 2 : 7, 0, 0);
            line.ToolTip = Tr("100일 지난 기록과 세션을 적지 않은 호출", "Logs older than 100 days, and calls that named no session");
            stack.Children.Add(line);
        }
        return stack;
    }

    /// How many of the sessions past the rows the rest's tooltip names (a month can hold more than a thousand).
    public const int RestTipMost = 10;

    /// A session's name as its row shows it: a Bloom chat's title (`chat`; none: no kept chat holds that session now,
    /// as one deleted or one that went on in another session), else the title its CLI gave it, else its folder, else
    /// "세션" and the head of its id.
    public static string SessionName(TokenSession s, string? chat) =>
        s.Bloom ? chat ?? Tr("Bloom AI 채팅 (찾을 수 없음)", "Bloom AI chat (not found)")
        : s.Title ?? s.Folder ?? Tr($"세션 {IdHead(s.Id)}", $"Session {IdHead(s.Id)}");

    private static string IdHead(string id) => id.Length > 8 ? id[..8] : id;

    /// The rows past the count as one line: how many, their tokens, their cost when any is priced (an unpriced one is
    /// not $0), and how many of them have no price.
    private static string RestWords(int n, long total, double priced, int unpriced)
    {
        string tokens = Count(total);
        if (unpriced == n) return Tr($"그 밖 {n}개 · 토큰 {tokens} · 가격 없음", $"{n} more · {tokens} tokens · no price");
        string cost = Money(priced, true);
        return unpriced == 0 ? Tr($"그 밖 {n}개 · 토큰 {tokens} · {cost}", $"{n} more · {tokens} tokens · {cost}")
            : Tr($"그 밖 {n}개 · 토큰 {tokens} · {cost} · 가격 없음 {unpriced}개", $"{n} more · {tokens} tokens · {cost} · {unpriced} with no price");
    }

    private static TextBlock RestLine(string words, string tip)
    {
        var line = Theme.T(words, 11, Theme.P.TextFaint);
        line.Margin = new Thickness(0, 7, 0, 0);
        line.ToolTip = tip;
        return line;
    }

    /// A session's row, as a model's: its name with SessionTail, tokens, share, cost and the bar; the rest in the
    /// tooltip (SessionTip). `open`: a Bloom chat still kept, opened on a click.
    private static Border SessionRow(TokenSession s, string name, Action? open, bool first)
    {
        var stack = new StackPanel();
        stack.Children.Add(Head(name, SessionTail(s), s.Counts.Total, s.Share, s.Cost));
        stack.Children.Add(HomeViews.Bar(s.Share * 100, false));
        var row = new Border { Child = stack, Background = Brushes.Transparent, Margin = new Thickness(0, first ? 0 : 5, 0, 0), ToolTip = SessionTip(s, name) };
        if (open is not null)
        {
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonDown += (_, e) => e.Handled = true; // no search box focus, no block held
            row.MouseLeftButtonUp += (_, e) =>
            {
                open();
                e.Handled = true;
            };
        }
        return row;
    }

    /// What a session's row says after its name: where it ran (Codex or Claude Code), then "Bloom" for a Bloom chat or
    /// its folder when the name is its title, then its most-used effort.
    public static string SessionTail(TokenSession s)
    {
        string tail = Source(s.Provider);
        if (s.Bloom) tail += " · Bloom";
        else if (s.Title is not null && s.Folder is not null) tail += " · " + s.Folder;
        if (TopEffort(s.Efforts) is { } e) tail += " · " + EffortName(e);
        return tail;
    }

    /// A session row's tooltip: its name and side; "Bloom" or its folder with the head of its id; its span; its top
    /// three models; its effort mix; its numbers and cost.
    public static string SessionTip(TokenSession s, string name)
    {
        var tip = new List<string>
        {
            $"{name} · {Source(s.Provider)}",
            string.Join(" · ", new[] { s.Bloom ? "Bloom" : s.Folder, IdHead(s.Id) }.Where(x => x is not null)),
            Span(s.First, s.Last.AddHours(1)),
        };
        long sum = Math.Max(1, s.Models.Sum(m => m.Tokens));
        tip.Add(string.Join(" · ", s.Models.Take(3).Select(m => $"{Pretty(m.Model)} {Percent((double)m.Tokens / sum)}")));
        if (EffortMix(s.Efforts, false) is { } mix) tip.Add("effort " + mix);
        tip.AddRange(Numbers(s.Counts));
        tip.Add(s.Cost is { } cost ? Tr($"환산액 {Money(cost, false)}", $"Cost {Money(cost, false)}") : Tr("가격 없음 · 환산액에 들어가지 않음", "No price · not in the cost"));
        if (s.Cost is not null && s.UnpricedTokens > 0)
            tip.Add(Tr($"가격 없는 토큰 {Whole(s.UnpricedTokens)} · 환산액에 없음", $"{Whole(s.UnpricedTokens)} tokens with no price · not in the cost"));
        return string.Join("\n", tip.Where(t => t.Length > 0));
    }

    /// "11-03 09:00 – 14:00"; across days, each end with its day.
    private static string Span(DateTime from, DateTime to)
    {
        var c = CultureInfo.InvariantCulture;
        string start = from.ToString("MM-dd HH:mm", c);
        return from.Date == to.Date || to == from.Date.AddDays(1) ? $"{start} – {to.ToString("HH:mm", c)}" : $"{start} – {to.ToString("MM-dd HH:mm", c)}";
    }

    /// The effort used for the most tokens (the higher of two the same); null when none is known.
    public static TokenEffort? TopEffort(IReadOnlyList<(TokenEffort Effort, long Tokens)> efforts)
    {
        TokenEffort? top = null;
        long most = 0;
        foreach (var (e, tokens) in efforts)
            if (e != TokenEffort.Unknown && tokens > 0 && (tokens > most || tokens == most && e > top))
                (top, most) = (e, tokens);
        return top;
    }

    /// An effort as the logs write it ("xhigh", "max"); "모름" for Unknown.
    public static string EffortName(TokenEffort e) => e == TokenEffort.Unknown ? Tr("모름", "unknown") : e.ToString().ToLowerInvariant();

    /// The effort mix in the order given (the highest first, unknown last): "max 27% · xhigh 35% · high 38%". `full`:
    /// every effort with its tokens ("max 27% 1.2M"), else those that round to 0% left out. Null when no effort is known.
    public static string? EffortMix(IReadOnlyList<(TokenEffort Effort, long Tokens)> efforts, bool full)
    {
        if (!efforts.Any(x => x.Effort != TokenEffort.Unknown && x.Tokens > 0)) return null;
        double total = efforts.Sum(x => x.Tokens);
        var parts = new List<string>();
        foreach (var (e, tokens) in efforts)
        {
            if (tokens <= 0) continue;
            string share = Percent(tokens / total);
            if (full) parts.Add($"{EffortName(e)} {share} {Count(tokens)}");
            else if (share != "0%") parts.Add($"{EffortName(e)} {share}");
        }
        return string.Join(" · ", parts);
    }

    /// The first line of a model's or a session's row: the name (and a faint tail), then tokens, share and cost in
    /// columns shared with the block's other rows.
    private static Grid Head(string title, string tail, long tokens, double share, double? cost)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (string column in new[] { "Tokens", "Share", "Cost" })
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = column });
        // the tail right after the name; short of room it gives way before the name does (NameTail)
        var after = Theme.T("  " + tail, 11, Theme.P.TextFaint);
        after.VerticalAlignment = VerticalAlignment.Bottom;
        after.Margin = new Thickness(0, 0, 0, 1);
        head.Children.Add(new NameTail(Theme.T(title, 12.5, Theme.P.Text, Theme.Medium), after)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var cells = new[]
        {
            Theme.T(Count(tokens), 12, Theme.P.Text, Theme.Medium, false),
            Theme.T(Percent(share), 11, Theme.P.TextFaint, null, false),
            Theme.T(cost is { } c ? Money(c, true) : "—", 12, cost is null ? Theme.P.TextFaint : Theme.P.TextDim, null, false),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].HorizontalAlignment = HorizontalAlignment.Right;
            cells[i].Margin = new Thickness(i == 0 ? 10 : 8, 0, 0, 0);
            Grid.SetColumn(cells[i], i + 1);
            head.Children.Add(cells[i]);
        }
        return head;
    }

    /// A sum's lines in a tooltip: calls, input, output, cache read and write, cache hits.
    private static string[] Numbers(TokenCounts k) =>
    [
        Tr($"호출 {Whole(k.Calls)}", $"Calls {Whole(k.Calls)}"),
        Tr($"입력 {Whole(k.Input)}", $"Input {Whole(k.Input)}"),
        Tr($"출력 {Whole(k.Output)}", $"Output {Whole(k.Output)}"),
        Tr($"캐시 읽기 {Whole(k.CacheRead)}", $"Cache read {Whole(k.CacheRead)}"),
        Tr($"캐시 쓰기 {Whole(k.CacheWrite + k.CacheWriteLong)}", $"Cache write {Whole(k.CacheWrite + k.CacheWriteLong)}"),
        Tr($"캐시 적중 {Percent(k.HitRate)}", $"Cache hits {Percent(k.HitRate)}"),
    ];

    /// A model's row: its name and where it was used, tokens, share, cost, the bar, and under it the effort mix in one
    /// faint line (none when no effort is known); every number in the tooltip.
    private static Border ModelRow(TokenModel m, bool first)
    {
        var stack = new StackPanel();
        stack.Children.Add(Head(Pretty(m.Model), Source(m.Provider), m.Counts.Total, m.Share, m.Cost));
        stack.Children.Add(HomeViews.Bar(m.Share * 100, false));
        if (EffortMix(m.Efforts, false) is { } mix)
        {
            var line = Theme.T(mix, 11, Theme.P.TextFaint);
            line.Margin = new Thickness(0, 0, 0, 1);
            stack.Children.Add(line);
        }
        var tip = new List<string> { $"{Pretty(m.Model)} · {Source(m.Provider)}" };
        if (EffortMix(m.Efforts, true) is { } all) tip.Add("effort " + all);
        tip.AddRange(Numbers(m.Counts));
        tip.Add(m.Cost is { } cost ? Tr($"환산액 {Money(cost, false)}", $"Cost {Money(cost, false)}") : Tr("가격 없음 · 환산액에 들어가지 않음", "No price · not in the cost"));
        return new Border { Child = stack, Background = Brushes.Transparent, Margin = new Thickness(0, first ? 0 : 5, 0, 0), ToolTip = string.Join("\n", tip) };
    }

    /// The input, cache writes, cache reads and output of a sum as one bar, each part its colour, the legend under it.
    private static FrameworkElement Split(TokenCounts s)
    {
        var parts = new (string name, long value, Brush colour)[]
        {
            (Tr("입력", "Input"), s.Input, Theme.P.Accent),
            (Tr("캐시 쓰기", "Cache write"), s.CacheWrite + s.CacheWriteLong, Theme.P.Selection),
            (Tr("캐시 읽기", "Cache read"), s.CacheRead, Theme.P.TextFaint),
            (Tr("출력", "Output"), s.Output, Theme.P.Text),
        };
        long total = Math.Max(1, s.Total);
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var bar = new Grid { Height = 6 };
        for (int i = 0; i < parts.Length; i++)
        {
            var (_, value, colour) = parts[i];
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            var piece = new SmoothBorder { Background = colour, CornerRadius = new CornerRadius(2), Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed };
            Grid.SetColumn(piece, i);
            bar.Children.Add(piece);
        }
        stack.Children.Add(bar);
        var legend = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var (name, value, colour) in parts)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 0), ToolTip = $"{name} {Whole(value)}" };
            item.Children.Add(new SmoothBorder { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = colour, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0) });
            var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            words.Inlines.Add(new Run(name) { FontFamily = Theme.Sans, FontSize = 11, Foreground = Theme.P.TextDim });
            words.Inlines.Add(new Run(" " + Count(value)) { FontFamily = Theme.Sans, FontSize = 11, FontWeight = Theme.Medium, Foreground = Theme.P.Text });
            words.Inlines.Add(new Run(" " + Percent((double)value / total)) { FontFamily = Theme.Sans, FontSize = 11, Foreground = Theme.P.TextFaint });
            item.Children.Add(words);
            legend.Children.Add(item);
        }
        stack.Children.Add(legend);
        return stack;
    }

    private static SmoothBorder Tile(string label, string value, string tip)
    {
        var stack = new StackPanel();
        stack.Children.Add(Theme.T(label, 11, Theme.P.TextFaint));
        var number = Theme.T(value, 15, Theme.P.Text, Theme.Semi, false);
        number.Margin = new Thickness(0, 1, 0, 0);
        // a narrow tile shrinks a long number instead of cutting it
        stack.Children.Add(new Viewbox { Child = number, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        return new SmoothBorder
        {
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 8, 7),
            Margin = new Thickness(3, 0, 3, 0),
            Child = stack,
            ToolTip = tip,
        };
    }

    /// The body of a block: its numbers in tabular figures, under the header's words.
    private static StackPanel Body()
    {
        var stack = new StackPanel { Margin = new Thickness(20, 0, 20, 8) };
        Typography.SetNumeralAlignment(stack, FontNumeralAlignment.Tabular);
        return stack;
    }

    /// No log folder at all: the line alone; nothing in the period, or nothing counted yet: one line under the chips.
    private static StackPanel Empty(StackPanel stack, string words)
    {
        var line = Theme.T(words, 12, Theme.P.TextFaint);
        line.Margin = new Thickness(0, 2, 0, 2);
        stack.Children.Add(line);
        return stack;
    }

    private static string NoLogsWords => Tr("Claude Code · Codex 기록 없음", "No Claude Code or Codex logs");

    private static string Nothing(bool counting) => counting ? Tr("세는 중…", "Counting…") : Tr("이 기간의 기록 없음", "Nothing in this period");

    private static readonly TokenPeriod[] AllPeriods = [TokenPeriod.Day, TokenPeriod.Week, TokenPeriod.Month, TokenPeriod.Quarter, TokenPeriod.All];

    /// The body's first row: `caption` at the left (faint), the period chips at the right; the picked one in the accent.
    /// A chip takes the press (a held block does not start under it) and picks on the release.
    private static Grid Periods(TokenPeriod current, Action<TokenPeriod> pick, string? caption)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (caption is not null)
        {
            var words = Theme.T(caption, 11, Theme.P.TextFaint);
            words.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(words);
        }
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, -6, 0) };
        foreach (var p in AllPeriods)
        {
            bool on = p == current;
            var text = Theme.T(Short(p), 11, on ? Theme.P.AccentText : Theme.P.TextFaint, on ? Theme.Semi : Theme.Medium, false);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            var chip = new SmoothBorder
            {
                Child = text,
                MinWidth = 30,
                Padding = new Thickness(7, 2, 7, 3),
                CornerRadius = new CornerRadius(6),
                Background = on ? Theme.P.AccentSoft : Brushes.Transparent,
                Cursor = on ? Cursors.Arrow : Cursors.Hand,
                ToolTip = Long(p),
            };
            if (!on)
            {
                chip.MouseEnter += (_, _) => chip.Background = Theme.P.Hover;
                chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
                var to = p;
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    pick(to);
                    e.Handled = true;
                };
            }
            chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
            chips.Children.Add(chip);
        }
        Grid.SetColumn(chips, 1);
        row.Children.Add(chips);
        return row;
    }

    private static string Short(TokenPeriod p) => p switch
    {
        TokenPeriod.Day => Tr("24시간", "24h"),
        TokenPeriod.Week => Tr("7일", "7d"),
        TokenPeriod.Month => Tr("30일", "30d"),
        TokenPeriod.Quarter => Tr("90일", "90d"),
        _ => Tr("전체", "All"),
    };

    private static string Long(TokenPeriod p) => p switch
    {
        TokenPeriod.Day => Tr("지난 24시간", "The last 24 hours"),
        TokenPeriod.Week => Tr("오늘까지 7일", "The last 7 days"),
        TokenPeriod.Month => Tr("오늘까지 30일", "The last 30 days"),
        TokenPeriod.Quarter => Tr("오늘까지 90일", "The last 90 days"),
        _ => Tr("Bloom이 센 기록 전체", "Everything Bloom has counted"),
    };

    /// The span a view covers, from its start: "11월 2일 14시 – 지금", "10월 27일 – 오늘", "10월 2일부터".
    private static string Range(TokenView v)
    {
        string from = v.Period == TokenPeriod.Day ? Hour(v.From) : Day(v.From);
        return v.Period switch
        {
            TokenPeriod.Day => Tr($"{from} – 지금", $"{from} – now"),
            TokenPeriod.All => Tr($"{from}부터", $"Since {from}"),
            _ => Tr($"{from} – 오늘", $"{from} – today"),
        };
    }

    private static string Hour(DateTime t) => t.ToString(Tr("M월 d일 H시", "MMM d, h tt"), Lang.Culture);

    private static string Day(DateTime t) => t.ToString(Tr("M월 d일", "MMM d"), Lang.Culture);

    /// A bar's start as the axis and its tooltip name it: the hour, the day, the week's Monday, the month.
    private static string When(DateTime t, TokenUnit unit) => unit switch
    {
        TokenUnit.Hour => t.ToString(Tr("H시", "h tt"), Lang.Culture),
        TokenUnit.Month => t.ToString(Tr("yyyy년 M월", "MMM yyyy"), Lang.Culture),
        _ => Day(t),
    };

    private static string BarTip(TokenBar b, TokenUnit unit)
    {
        string when = unit switch
        {
            TokenUnit.Hour => Hour(b.Start),
            TokenUnit.Week => Tr($"{Day(b.Start)}부터 한 주", $"Week of {Day(b.Start)}"),
            _ => When(b.Start, unit),
        };
        if (b.Counts.Calls == 0) return when + Tr(" · 기록 없음", " · nothing");
        return string.Join("\n", when,
            Tr($"토큰 {Whole(b.Counts.Total)}", $"{Whole(b.Counts.Total)} tokens"),
            Tr($"호출 {Whole(b.Counts.Calls)}", $"{Whole(b.Counts.Calls)} calls"),
            Tr($"환산액 {Money(b.Cost, false)}", $"Cost {Money(b.Cost, false)}"));
    }

    private static string UnpricedWords(TokenView v)
    {
        string tokens = Count(v.UnpricedTokens);
        // no particle after the number (12.3K은, 2.71B은 read wrong)
        return v.Unpriced == 1 ? Tr($"가격 없는 모델 1개 · 환산액에 안 들어간 토큰 {tokens}", $"1 model with no price · {tokens} tokens not in the cost")
            : Tr($"가격 없는 모델 {v.Unpriced}개 · 환산액에 안 들어간 토큰 {tokens}", $"{v.Unpriced} models with no price · {tokens} tokens not in the cost");
    }

    /// "Claude Code" or "Codex" for a model's provider.
    private static string Source(string provider) => provider == "codex" ? "Codex" : provider == "claude" ? "Claude Code" : provider;

    /// A model's name as people say it: "claude-opus-5-5" → "Opus 5.5", "claude-haiku-4-5" → "Haiku 4.5"; any other
    /// name as the log has it.
    public static string Pretty(string model)
    {
        if (!model.StartsWith("claude-", StringComparison.Ordinal)) return model;
        var parts = model["claude-".Length..].Split('-');
        if (parts.Length < 2 || parts[0].Length == 0 || !parts[0].All(char.IsAsciiLetterLower) || !parts.Skip(1).All(p => p.Length > 0 && p.All(char.IsAsciiDigit))) return model;
        return char.ToUpperInvariant(parts[0][0]) + parts[0][1..] + " " + string.Join(".", parts.Skip(1));
    }

    /// A count in a few figures: 980, 9,870, 12.3K, 19.7M, 2.71B.
    public static string Count(long n)
    {
        double a = Math.Abs((double)n);
        if (a < 10_000) return Whole(n);
        if (a < 999_500) return Scaled(n / 1e3, "K"); // 999,600 is 1M, not 1000K
        if (a < 999_500_000) return Scaled(n / 1e6, "M");
        return (n / 1e9).ToString(a < 99.995e9 ? "0.##" : "0", CultureInfo.InvariantCulture) + "B";
        static string Scaled(double x, string unit) => x.ToString(Math.Abs(x) < 99.95 ? "0.#" : "0", CultureInfo.InvariantCulture) + unit;
    }

    /// Every figure, with thousands marks: 1,234,567.
    public static string Whole(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// USD: $12.34, $0.03; `compact` past $1,000: $1,235, past $100,000: $123K.
    public static string Money(double usd, bool compact)
    {
        var c = CultureInfo.InvariantCulture;
        if (!compact || usd < 1000) return "$" + usd.ToString("N2", c);
        return usd < 100_000 ? "$" + usd.ToString("N0", c) : "$" + Count((long)Math.Round(usd));
    }

    /// A share as a percent: 94.2%, 0.4%, 100%.
    public static string Percent(double share) => (share * 100).ToString(share >= 0.9995 ? "0" : "0.#", CultureInfo.InvariantCulture) + "%";
}
