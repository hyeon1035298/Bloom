using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

/// The home's blocks (Core\Home.cs): kept lists old and new, their columns, rows and moves; the two columns themselves
/// (Ui\Controls\HomeColumns.cs) and the kept home's keys (Ui\Launcher\Launcher.Kept.cs).
internal static class HomeLayoutTests
{
    /// The blocks in order (one column's order), or those of one column.
    private static string Blocks(bool? right = null) =>
        string.Join(" ", Home.Order().Where(w => right is null || (w.Right is true) == right).Select(w => w.Key));

    private static void From(string json, Action check)
    {
        Settings.Current.Home = JsonSerializer.Deserialize<Settings>(json, Json.Options)!.Home;
        try
        {
            check();
        }
        finally
        {
            Settings.Current.Home = null;
        }
    }

    private static readonly HashSet<string> Shown = ["usage", "timers", "week", "favorites", "recent", "commands"];

    public static void DefaultsAndOldLists()
    {
        From("{}", () =>
        {
            Check.Eq(Blocks(false), "usage tokens tokenchart timers week", "the left column by default");
            Check.Eq(Blocks(true), "tokenmodels tokensessions favorites recent recentfiles clipboard threads notes player commands", "the right column by default");
            Check.True(Home.Order().TrueForAll(w => !w.Collapsed && w.Count is null), "none folded, every count its kind's");
        });
        // kept before columns (key and on only) in the default order: split as the defaults are
        From("""{"home":[{"key":"usage","on":true},{"key":"timers","on":true},{"key":"week","on":true},{"key":"favorites","on":true},{"key":"recent","on":true},{"key":"recentfiles","on":false},{"key":"clipboard","on":true},{"key":"threads","on":false},{"key":"commands","on":false}]}""", () =>
        {
            Check.Eq(Blocks(false), "usage timers week tokens tokenchart", "an old default list: left, the token blocks after");
            Check.Eq(Blocks(true), "favorites recent recentfiles clipboard threads commands tokenmodels tokensessions notes player", "an old default list: right");
            Check.True(!Home.IsOn("commands") && Home.IsOn("clipboard"), "its switches kept");
            Check.Eq(Home.Count(Home.Of("clipboard")!), 3, "a new field gets its default");
        });
        // reordered before columns: its order stays (one column shows it as it was), split where the fewest move
        From("""{"home":[{"key":"favorites","on":true},{"key":"usage","on":true},{"key":"timers","on":true},{"key":"week","on":true},{"key":"recent","on":true},{"key":"recentfiles","on":false},{"key":"clipboard","on":false},{"key":"threads","on":false},{"key":"commands","on":true}]}""", () =>
        {
            Check.Eq(Blocks(), "favorites usage timers week tokens tokenchart recent recentfiles clipboard threads commands tokenmodels tokensessions notes player", "the old order");
            Check.Eq(Blocks(false), "favorites usage timers week tokens tokenchart", "the favorites stay above the usage");
        });
        // a null in a hand-edited list goes (no throw); alone, the defaults
        From("""{"home":[null]}""", () =>
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage tokens tokenchart timers week | tokenmodels tokensessions favorites recent recentfiles clipboard threads notes player commands", "only a null: the defaults"));
        From("""{"home":[null,{"key":"commands","on":false,"right":false},null]}""", () =>
        {
            Check.Eq(Blocks(false), "commands usage tokens tokenchart timers week", "nulls among kept blocks: those kept, new kinds after");
            Check.True(!Home.IsOn("commands") && Home.Order().TrueForAll(w => w is not null), "… as they were, and no null left");
        });
        // a block kind the list didn't know joins its column with its defaults; an unknown or twice-kept one goes
        From("""{"home":[{"key":"commands","on":true,"right":false},{"key":"gone","on":true},{"key":"recent","on":false,"right":true,"count":7,"collapsed":true},{"key":"recent","on":true}]}""", () =>
        {
            Check.Eq(Blocks(false), "commands usage tokens tokenchart timers week", "kept columns, new kinds in theirs");
            Check.Eq(Blocks(true), "recent tokenmodels tokensessions favorites recentfiles clipboard threads notes player", "the right column");
            var recent = Home.Of("recent")!;
            Check.True(!recent.On && recent.Collapsed && Home.Count(recent) == 7, "the first of a twice-kept block");
        });
        // saved and read again: the same blocks the same way (no split again)
        From("""{"home":[{"key":"favorites","on":true},{"key":"usage","on":true}]}""", () =>
        {
            Home.SetCollapsed("week", true);
            string before = Blocks() + "|" + Home.Arrangement();
            var read = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings.Current, Json.Options), Json.Options)!;
            Settings.Current.Home = read.Home;
            Check.Eq(Blocks() + "|" + Home.Arrangement(), before, "after a restart");
            Check.True(Home.Of("week")!.Collapsed, "a fold kept");
        });
    }

    public static void CountBounds()
    {
        From("""{"home":[{"key":"clipboard","on":true,"right":true,"count":50},{"key":"threads","on":true,"right":true,"count":-3}]}""", () =>
        {
            Check.Eq(Home.Count(Home.Of("recent")!), 5, "recent: 5 by default");
            Check.Eq(Home.Count(Home.Of("timers")!), 3, "timers: 3 by default");
            Check.Eq(Home.Count(Home.Of("recentfiles")!), 3, "recent files: 3 by default");
            Check.Eq(Home.Count(Home.Of("clipboard")!), Home.CountMax, "a count kept too high");
            Check.Eq(Home.Count(Home.Of("threads")!), Home.CountMin, "a count kept too low");
            Check.Eq(Home.Count(Home.Of("tokenmodels")!), 5, "models by use: 5 by default");
            Check.Eq(Home.Count(Home.Of("tokensessions")!), 5, "sessions by use: 5 by default");
            foreach (string key in new[] { "usage", "tokens", "tokenchart", "week", "favorites", "commands" })
                Check.Eq(Home.Count(Home.Of(key)!), 0, key + " has no rows to count");
            Home.SetCount("recent", 8);
            Check.Eq(Home.Count(Home.Of("recent")!), 8, "set");
            Home.SetCount("recent", 0);
            Check.Eq(Home.Count(Home.Of("recent")!), Home.CountMin, "no fewer than the least");
            Home.SetCount("recent", 99);
            Check.Eq(Home.Count(Home.Of("recent")!), Home.CountMax, "no more than the most");
            Home.SetCount("week", 4);
            Check.True(Home.Of("week")!.Count is null, "a block of no rows keeps no count");
        });
    }

    public static void DropAndMoves()
    {
        // two columns: into the column it was put in, at the gap
        From("{}", () =>
        {
            Home.Drop("week", null, "tokenmodels", true);
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage tokens tokenchart timers | week tokenmodels tokensessions favorites recent recentfiles clipboard threads notes player commands", "on top of the right column");
            Home.Drop("commands", "usage", "tokens", false);
            Check.Eq(Blocks(false), "usage commands tokens tokenchart timers", "between two of the left column");
            foreach (string key in new[] { "usage", "commands", "tokens", "tokenchart", "timers" }) Home.Drop(key, null, null, true);
            Home.Drop("recent", null, null, false);
            Check.Eq(Blocks(false), "recent", "into an empty left column");
        });
        // one column: a gap inside a column joins it; the gap between the columns keeps the block's own
        From("{}", () =>
        {
            Home.Drop("commands", "usage", "tokens", null);
            Check.Eq(Blocks(false), "usage commands tokens tokenchart timers week", "dropped between two left blocks");
            Home.Drop("timers", "recent", "recentfiles", null);
            Check.Eq(Blocks(true), "tokenmodels tokensessions favorites recent timers recentfiles clipboard threads notes player", "dropped between two right blocks");
            Home.Drop("recent", "week", "tokenmodels", null);
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage commands tokens tokenchart week | recent tokenmodels tokensessions favorites timers recentfiles clipboard threads notes player", "between the columns: a right block stays right");
            Home.Drop("usage", "week", "recent", null);
            Check.Eq(Blocks(false), "commands tokens tokenchart week usage", "between the columns: a left block stays left");
            Check.Eq(Blocks(), "commands tokens tokenchart week usage recent tokenmodels tokensessions favorites timers recentfiles clipboard threads notes player", "one column: the left column's first");
        });
        // ▲ ▼ as laid out: in one column through both columns, in two within one; past the blocks shown only
        From("{}", () =>
        {
            Check.True(!Home.CanStep("usage", -1, false, Shown) && !Home.CanStep("commands", 1, false, Shown), "the ends");
            Check.True(Home.CanStep("week", 1, false, Shown) && !Home.CanStep("week", 1, true, Shown), "the left column's end: on in one column, off in two");
            Check.True(!Home.CanStep("favorites", -1, true, Shown), "the right column's top");
            Home.Step("favorites", -1, false, Shown);
            Check.Eq(Blocks(), "usage tokens tokenchart timers favorites week tokenmodels tokensessions recent recentfiles clipboard threads notes player commands", "up past the left column's last");
            Check.True(Home.Of("favorites")!.Right is false, "… into it");
            Home.Step("week", 1, false, Shown);
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage tokens tokenchart timers favorites | tokenmodels tokensessions recent recentfiles clipboard threads notes player week commands", "down past the left column's end (before the next block shown)");
            Home.Step("timers", 1, true, Shown);
            Check.Eq(Blocks(false), "usage tokens tokenchart favorites timers", "two columns: within its own");
            Home.Step("timers", -1, false, Shown.Where(k => k != "favorites").ToHashSet());
            Check.Eq(Blocks(false), "timers usage tokens tokenchart favorites", "a block not shown is passed over");
        });
        // ‹ ›: across, at the same place among the blocks shown
        From("{}", () =>
        {
            Home.MoveAcross("timers", Shown);
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage tokens tokenchart week | tokenmodels tokensessions favorites timers recent recentfiles clipboard threads notes player commands", "to the right column, second");
            Home.MoveAcross("commands", Shown);
            Check.Eq(Blocks(false), "usage tokens tokenchart week commands", "to the left column's end");
        });
        // 설정's list: ▲ ▼ and a drag through the whole list, hidden blocks too
        From("{}", () =>
        {
            Home.Move("week", 1);
            Check.Eq(Blocks(), "usage tokens tokenchart timers tokenmodels week tokensessions favorites recent recentfiles clipboard threads notes player commands", "down a place");
            Check.True(Home.Of("week")!.Right is true, "into the right column");
            Home.MoveTo("commands", 0);
            Check.Eq(Blocks(false), "commands usage tokens tokenchart timers", "dragged to the top");
            Home.MoveTo("usage", Home.Kinds.Length);
            Check.Eq(Blocks(), "commands tokens tokenchart timers tokenmodels week tokensessions favorites recent recentfiles clipboard threads notes player usage", "dragged to the end");
            Check.True(Home.Of("usage")!.Right is true, "… the right column's end");
        });
    }

    public static void TwoColumnsWhenWide()
    {
        Check.True(!HomeColumns.Wide(Settings.DefaultWidth), "the default launcher: one column");
        Check.True(HomeColumns.Wide(1000) && HomeColumns.Wide(HomeColumns.TwoColumnWidth) && !HomeColumns.Wide(HomeColumns.TwoColumnWidth - 1), "about 1000 wide: two");
        var grid = new HomeColumns();
        int flips = 0;
        grid.Flipped += () => flips++;
        Border Block(double height, bool right)
        {
            var b = new Border { Height = height };
            HomeColumns.SetRight(b, right);
            grid.Children.Add(b);
            return b;
        }
        var a = Block(100, false);
        var b = Block(50, false);
        var c = Block(80, true);
        var d = Block(40, true);
        void Lay(double width)
        {
            grid.Measure(new Size(width, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
        }
        static string Box(UIElement e)
        {
            var at = System.Windows.Media.VisualTreeHelper.GetOffset(e);
            return FormattableString.Invariant($"{at.X},{at.Y} {e.RenderSize.Width}");
        }

        Lay(760);
        Check.True(!grid.Two && flips == 0, "narrow: one column");
        Check.Eq(grid.DesiredSize.Height, 270.0, "every block stacked");
        Check.Eq(string.Join(" / ", new[] { a, b, c, d }.Select(Box)), "0,0 760 / 0,100 760 / 0,150 760 / 0,230 760", "left column's first, then the right's");
        var (right, above, below) = grid.DropAt(new Point(700, 200));
        Check.True(right is null && above == c && below == d, "one column: the gap nearest the mouse, any column");

        // a block held: out of its column (the others close up, it stays drawn where it was), the drop taken without it
        grid.Lift(a);
        Lay(760);
        Check.Eq(string.Join(" / ", new[] { a, b, c, d }.Select(Box)), "0,0 760 / 0,0 760 / 0,50 760 / 0,130 760", "held: the rest close up");
        (right, above, below) = grid.DropAt(new Point(700, 200));
        Check.True(above == d && below is null, "past the last block, the held one left out: the end");
        // the gap where it would land: the blocks below move down by it, the drop does not move with them
        grid.OpenGap(false, c, 60);
        Lay(760);
        Check.Eq(string.Join(" / ", new[] { b, c, d }.Select(Box)), "0,0 760 / 0,110 760 / 0,190 760", "a gap before 'c'");
        Check.Eq(grid.GapBox, new Rect(0, 50, 760, 60), "… where 'c' was");
        Check.Eq(grid.DesiredSize.Height, 230.0, "the held block's room given to the gap");
        (_, above, below) = grid.DropAt(new Point(700, 60));
        Check.True(above == b && below == c, "over the gap: still before 'c'");
        grid.Lift(null);
        Lay(760);
        Check.True(grid.GapBox.IsEmpty && Box(a) == "0,0 760" && Box(b) == "0,100 760", "put back: as before, no gap");

        Lay(1100);
        Check.True(grid.Two && flips == 1, "wide: two columns (once flipped)");
        Check.Eq(grid.DesiredSize.Height, 150.0, "as tall as the taller column");
        Check.Eq(string.Join(" / ", new[] { a, b, c, d }.Select(Box)), "0,0 550 / 0,100 550 / 550,0 550 / 550,80 550", "side by side");
        (right, above, below) = grid.DropAt(new Point(800, 10));
        Check.True(right == true && above is null && below == c, "the right column's top");
        (right, above, below) = grid.DropAt(new Point(100, 500));
        Check.True(right == false && above == b && below is null, "the left column's end");
        grid.OpenGap(true, c, 30);
        Lay(1100);
        Check.True(Box(c) == "550,30 550" && Box(d) == "550,110 550" && grid.GapBox == new Rect(550, 0, 550, 30), "a gap on top of the right column");
        grid.OpenGap(false, null, 30);
        Lay(1100);
        Check.True(Box(c) == "550,0 550" && grid.GapBox == new Rect(0, 150, 550, 30) && grid.DesiredSize.Height == 180, "… then at the left column's end");
        grid.OpenGap(false, null, 0);

        // 홈 편집's feet ("+ 블록 추가"): each ends its column, whatever their place among the children; no drop lands by them
        var leftFoot = new Border { Height = 20 };
        HomeColumns.SetFoot(leftFoot, true);
        grid.Children.Insert(2, leftFoot);
        var rightFoot = new Border { Height = 20 };
        HomeColumns.SetFoot(rightFoot, true);
        HomeColumns.SetRight(rightFoot, true);
        grid.Children.Add(rightFoot);
        Lay(1100);
        Check.Eq(string.Join(" / ", new[] { leftFoot, rightFoot }.Select(Box)), "0,150 550 / 550,120 550", "two columns: a foot under each");
        (right, above, below) = grid.DropAt(new Point(100, 160));
        Check.True(right == false && above == b && below is null, "over the foot: the column's end");
        grid.OpenGap(false, null, 30);
        Lay(1100);
        Check.Eq(Box(leftFoot), "0,180 550", "a gap at a column's end comes before its foot");
        grid.OpenGap(false, null, 0);
        Lay(760);
        Check.Eq(string.Join(" / ", new[] { d, leftFoot, rightFoot }.Select(Box)), "0,230 760 / 0,270 760 / 0,290 760", "one column: after every block");
    }

    /// "+ 블록 추가" (Home.Hidden, Home.Add, Home.AddAt): the blocks it offers, and one turned on at a column's end or
    /// where its card was dropped.
    public static void AddingBlocks()
    {
        static string Hidden() => string.Join(" ", Home.Hidden().Select(w => w.Key));
        // two columns: at the end of the column whose tile was pressed
        From("{}", () =>
        {
            Check.Eq(Hidden(), "tokens tokenchart tokenmodels tokensessions recentfiles clipboard threads notes player", "the blocks off by default, in their order");
            Home.Add("clipboard", false);
            Check.True(Home.IsOn("clipboard"), "on");
            Check.Eq(Blocks(false), "usage tokens tokenchart timers week clipboard", "the left column's end");
            Home.Add("threads", true);
            Check.Eq(Blocks(true), "tokenmodels tokensessions favorites recent recentfiles notes player commands threads", "the right column's end, past a block still off");
            Check.Eq(Hidden(), "tokens tokenchart tokenmodels tokensessions recentfiles notes player", "seven left to offer");
            foreach (string key in new[] { "recentfiles", "tokens", "tokenchart", "tokenmodels", "tokensessions", "notes", "player" }) Home.Add(key, false);
            Check.True(Home.Hidden().Count == 0, "none left");
        });
        // one column: after the home's last block, in that block's column
        From("{}", () =>
        {
            Home.Add("recentfiles", null);
            Check.Eq(Blocks(), "usage tokens tokenchart timers week tokenmodels tokensessions favorites recent clipboard threads notes player commands recentfiles", "the home's end");
            Check.True(Home.Of("recentfiles")!.Right is true, "… in the right column, the last block's");
        });
        From("""{"home":[{"key":"usage","on":true,"right":false},{"key":"tokens","on":false,"right":false},{"key":"tokenchart","on":false,"right":false},{"key":"tokenmodels","on":false,"right":false},{"key":"timers","on":true,"right":false},{"key":"week","on":true,"right":false},{"key":"favorites","on":true,"right":false},{"key":"recent","on":true,"right":false},{"key":"recentfiles","on":false,"right":true},{"key":"clipboard","on":false,"right":false},{"key":"threads","on":false,"right":false},{"key":"commands","on":true,"right":false}]}""", () =>
        {
            Home.Add("recentfiles", null);
            Check.Eq(Blocks(false) + " | " + Blocks(true), "usage tokens tokenchart tokenmodels timers week favorites recent clipboard threads commands | tokensessions notes player recentfiles", "every other block left: its own column, still the home's end");
            Home.Add("threads", null);
            Check.Eq(Blocks(), "usage tokens tokenchart tokenmodels timers week favorites recent clipboard commands tokensessions notes player recentfiles threads", "the next one: after it, in its column");
        });
        // a card dropped: between the blocks around the gap, in its column
        From("{}", () =>
        {
            Home.AddAt("threads", null, "usage", false);
            Check.True(Home.IsOn("threads"), "on");
            Check.Eq(Blocks(false), "threads usage tokens tokenchart timers week", "the left column's top");
            Home.AddAt("clipboard", "favorites", "recent", null);
            Check.Eq(Blocks(true), "tokenmodels tokensessions favorites clipboard recent recentfiles notes player commands", "one column, between two right blocks: in the right column");
            Home.AddAt("recentfiles", null, null, false);
            Check.Eq(Blocks(false), "threads usage tokens tokenchart timers week recentfiles", "no blocks around the gap (an empty column): the column's end");
        });
        // a block of a feature switched off (설정 > 기능) is not offered: it would not show
        From("{}", () =>
        {
            Settings.Current.FeaturesOff.Add("chat");
            try
            {
                Check.Eq(Hidden(), "tokens tokenchart tokenmodels tokensessions recentfiles clipboard notes player", "AI chat off: no 최근 AI 채팅");
            }
            finally
            {
                Settings.Current.FeaturesOff.Remove("chat");
            }
        });
    }

    /// The token blocks (Ai\TokenStats.Blocks): known kinds, off by default, a span each that is kept as set; the
    /// numbers as the blocks write them (Ui\TokenViews.cs).
    public static void TokenBlocks()
    {
        From("{}", () =>
        {
            foreach (string key in Bloom.Ai.TokenStats.Blocks)
                Check.True(Home.Kinds.Any(k => k.key == key) && !Home.IsOn(key), key + ": a kind of block, off by default");
            Check.True(Home.Of("tokens")!.Period is null, "no span kept: the block's default (24 hours)");
            Home.SetPeriod("tokens", nameof(Bloom.Ai.TokenPeriod.Month));
            Home.SetPeriod("tokenchart", nameof(Bloom.Ai.TokenPeriod.All));
            var read = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Settings.Current, Json.Options), Json.Options)!;
            Settings.Current.Home = read.Home;
            Check.Eq($"{Home.Of("tokens")!.Period} {Home.Of("tokenchart")!.Period} {Home.Of("tokenmodels")!.Period ?? "-"}", "Month All -", "each block's span after a restart");
        });
        Check.Eq(string.Join(" ", new long[] { 980, 9_870, 12_345, 999_600, 19_700_000, 2_710_000_000 }.Select(TokenViews.Count)), "980 9,870 12.3K 1M 19.7M 2.71B", "counts in a few figures");
        Check.Eq(TokenViews.Money(1109.92, false) + " " + TokenViews.Money(1109.92, true) + " " + TokenViews.Money(49.26, true) + " " + TokenViews.Money(123_456, true), "$1,109.92 $1,110 $49.26 $123K", "costs");
        Check.Eq(TokenViews.Percent(0.9612) + " " + TokenViews.Percent(0.004) + " " + TokenViews.Percent(1), "96.1% 0.4% 100%", "shares");
        Check.Eq(string.Join(" | ", new[] { "claude-opus-5-5", "claude-haiku-4-5", "claude-opus-4", "gpt-6.1-sol", "claude-x-y" }.Select(TokenViews.Pretty)), "Opus 5.5 | Haiku 4.5 | Opus 4 | gpt-6.1-sol | claude-x-y", "model names");
        // the models block: the chips, never more rows than its count, the rest (one model too) as a line
        var all = TokenDemo.View(Bloom.Ai.TokenPeriod.All);
        int Lines(int rows) => ((Panel)TokenViews.Models(all, false, false, rows, _ => { })).Children.Count;
        Check.Eq(all.Models.Count, 4, "the made-up models");
        Check.Eq($"{Lines(1)} {Lines(3)} {Lines(4)} {Lines(5)}", "3 5 5 5", "rows and the rest's line");
        // nothing summed yet: the first count's line, not the empty period's
        static string Words(bool counting) => ((TextBlock)((Panel)TokenViews.Summary(Bloom.Ai.TokenView.Empty(Bloom.Ai.TokenPeriod.Day), false, counting, _ => { })).Children[^1]).Text;
        Check.True(Words(true) != Words(false), "counting reads apart from nothing in the period");
        // the made-up sessions and efforts add up as the real ones do
        foreach (var p in new[] { Bloom.Ai.TokenPeriod.Day, Bloom.Ai.TokenPeriod.Month, Bloom.Ai.TokenPeriod.All })
        {
            var v = TokenDemo.View(p);
            var held = v.NoSession;
            foreach (var s in v.Sessions) held += s.Counts;
            Check.Eq(held, v.Sum, p + ": the sessions and what no session holds make the sum");
            Check.True(v.Models.All(m => m.Efforts.Sum(e => e.Tokens) == m.Counts.Total), p + ": each model's efforts make its tokens");
            Check.True(v.Sessions.All(s => s.Efforts.Sum(e => e.Tokens) == s.Counts.Total && s.Models.Sum(m => m.Tokens) == s.Counts.Total), p + ": each session's efforts and models make its tokens");
        }
        // the sessions block: the chips, never more rows than its count, the rest as a line, then what no session holds
        Check.True(all.Sessions.Count > 6 && all.NoSession.Calls > 0, "made-up sessions, some calls with none");
        int SessionLines(int rows) => ((Panel)TokenViews.Sessions(all, [], false, false, rows, _ => { })).Children.Count;
        Check.Eq($"{SessionLines(1)} {SessionLines(5)} {SessionLines(all.Sessions.Count)}", $"4 8 {all.Sessions.Count + 2}", "rows, the rest's line, the no-session line");
        // its names: a Bloom chat's (or a deleted one), the title, the folder, the id's head
        static Bloom.Ai.TokenSession Session(string? title, string? folder, bool bloom) =>
            new("claude", "0123456789abcdef", title, folder, bloom, default, null, 0, 0, DateTime.Now, DateTime.Now, [], []);
        Check.Eq(string.Join(" | ", TokenViews.SessionName(Session("A title", "src", false), null), TokenViews.SessionName(Session(null, "src", false), null),
            TokenViews.SessionName(Session(null, null, false), null), TokenViews.SessionName(Session(null, "ai", true), "My chat"), TokenViews.SessionName(Session(null, "ai", true), null)),
            "A title | src | 세션 01234567 | My chat | Bloom AI 채팅 (찾을 수 없음)", "session names");
        // a made-up Bloom session reads as a made-up chat, never as one not found
        Check.True(all.Sessions.Any(s => s.Bloom) && all.Sessions.Where(s => s.Bloom).All(s => TokenDemo.ChatName(s.Id) is not null), "made-up Bloom sessions have chat names");
        Check.True(all.Sessions.Where(s => !s.Bloom).All(s => TokenDemo.ChatName(s.Id) is null), "no chat name for other sessions");
        // the effort mix: the order given, a share that rounds to 0% left out of the line but not the tooltip, none known: no line
        var mix = new (Bloom.Ai.TokenEffort, long)[] { (Bloom.Ai.TokenEffort.Max, 100), (Bloom.Ai.TokenEffort.XHigh, 350), (Bloom.Ai.TokenEffort.High, 379_549), (Bloom.Ai.TokenEffort.Unknown, 1) };
        Check.Eq(TokenViews.EffortMix(mix, false), "xhigh 0.1% · high 99.9%", "the effort line");
        Check.Eq(TokenViews.EffortMix(mix, true), "max 0% 100 · xhigh 0.1% 350 · high 99.9% 380K · 모름 0% 1", "the effort tooltip");
        Check.True(TokenViews.EffortMix([(Bloom.Ai.TokenEffort.Unknown, 5)], false) is null && TokenViews.EffortMix([], true) is null, "no effort known: no line");
        Check.Eq($"{TokenViews.TopEffort(mix)} {TokenViews.TopEffort([(Bloom.Ai.TokenEffort.High, 5), (Bloom.Ai.TokenEffort.XHigh, 5)])} {TokenViews.TopEffort([(Bloom.Ai.TokenEffort.Unknown, 9)])?.ToString() ?? "-"}",
            "High XHigh -", "the most-used effort: the higher of two the same, never unknown");
    }

    /// A row's name and tail on a short line (NameTail): the tail gives way first, the name keeps its share.
    public static void NameAndTailShare()
    {
        Check.Eq(NameTail.Widths(300, 120, 100), (120.0, 100.0), "room for both: whole");
        Check.Eq(NameTail.Widths(200, 120, 100), (120.0, 80.0), "a little short: only the tail cut");
        Check.Eq(NameTail.Widths(200, 180, 100), (120.0, 80.0), "short: the name keeps its share, both cut");
        Check.Eq(NameTail.Widths(200, 300, 20), (180.0, 20.0), "a short tail stays whole, the name cut");
        Check.Eq(NameTail.Widths(double.PositiveInfinity, 120, 100), (120.0, 100.0), "no limit: whole");
    }

    /// A session row's first line after its name (SessionTail), its tooltip (SessionTip, the row's own), and which
    /// chat's session a token session is (Engine.ChatSide).
    public static void SessionRows()
    {
        var day = new DateTime(2026, 11, 3, 9, 0, 0);
        static (Bloom.Ai.TokenEffort, long)[] Efforts(params (Bloom.Ai.TokenEffort, long)[] e) => e;
        Bloom.Ai.TokenSession Session(string provider, string? title, string? folder, bool bloom, (Bloom.Ai.TokenEffort, long)[] efforts, double? cost = 1.5, long unpriced = 0) =>
            new(provider, "0123456789abcdef", title, folder, bloom, new(3, 100, 200, 1_000, 50, 0), cost, unpriced, 0.25, day, day.AddHours(5),
                [("claude-opus-5-5", 1_000), ("claude-sonnet-5-5", 350)], efforts);
        var high = Efforts((Bloom.Ai.TokenEffort.High, 900), (Bloom.Ai.TokenEffort.Max, 450));
        Check.Eq(string.Join(" | ", TokenViews.SessionTail(Session("claude", "A title", "src", false, high)), TokenViews.SessionTail(Session("codex", null, "src", false, [])),
            TokenViews.SessionTail(Session("claude", null, "ai", true, Efforts((Bloom.Ai.TokenEffort.Unknown, 5))))),
            "Claude Code · src · high | Codex | Claude Code · Bloom", "the row's tail: the folder only beside a title, Bloom for a Bloom chat, the most-used effort");
        var tip = TokenViews.SessionTip(Session("claude", "A title", "src", false, high), "A title").Split('\n');
        Check.Eq(string.Join(" | ", tip.Take(5)), "A title · Claude Code | src · 01234567 | 11-03 09:00 – 15:00 | Opus 5.5 74.1% · Sonnet 5.5 25.9% | effort high 66.7% · max 33.3%",
            "the tooltip's name, folder and id, span, models and efforts");
        Check.Eq(tip[^1], "환산액 $1.50", "then the numbers and the cost");
        var bloomTip = TokenViews.SessionTip(Session("codex", null, "ai", true, [], null), "My chat").Split('\n');
        Check.Eq($"{bloomTip[0]} | {bloomTip[1]} | {bloomTip[^1]}", "My chat · Codex | Bloom · 01234567 | 가격 없음 · 환산액에 들어가지 않음", "a Bloom chat's tooltip, no price");
        Check.Eq(TokenViews.SessionTip(Session("claude", null, "ai", true, []), "X").Split('\n')[2], "11-03 09:00 – 15:00", "no effort known: no effort line");
        Check.True(TokenViews.SessionTip(Session("claude", "T", null, false, [], 2, 40), "T").EndsWith("가격 없는 토큰 40 · 환산액에 없음", StringComparison.Ordinal), "a priced session's unpriced tokens");
        var across = Session("claude", "T", null, false, []) with { Last = day.AddHours(20) };
        Check.Eq(TokenViews.SessionTip(across, "T").Split('\n')[2], "11-03 09:00 – 11-04 06:00", "a span across days");
        // the row's tooltip is SessionTip
        var view = TokenDemo.View(Bloom.Ai.TokenPeriod.All);
        var row = (FrameworkElement)((Panel)TokenViews.Sessions(view, [], false, false, 1, _ => { })).Children[1];
        Check.Eq(row.ToolTip as string, TokenViews.SessionTip(view.Sessions[0], TokenViews.SessionName(view.Sessions[0], null)), "the first row's tooltip");
        // a chat's session provider on the token side: none is Claude, ChatGPT is Codex
        Check.Eq(string.Join(" ", new[] { null, "claude", "chatgpt", "omp" }.Select(Engine.ChatSide)), "claude claude codex omp", "chat sides");
    }

    /// The kept home draws a block anew when its column, fold or count changes (its key): its tools are drawn from them.
    public static void KeptBlockKeys()
    {
        static Section Block(bool right, bool collapsed, int count = 5) => new("최근 사용") { HomeKey = "recent", HomeRight = right, Collapsed = collapsed, HomeCount = count };
        string key = Launcher.SectionKey(Block(false, false), out _);
        Check.Eq(Launcher.SectionKey(Block(false, false), out _), key, "the same");
        Check.True(Launcher.SectionKey(Block(true, false), out _) != key, "another column");
        Check.True(Launcher.SectionKey(Block(false, true), out _) != key, "folded");
        Check.True(Launcher.SectionKey(Block(false, false, 6), out _) != key, "another count");
        // the same items under another count: a folded block (no items) or a list shorter than either count
        Check.True(Launcher.SectionKey(Block(false, true, 6), out _) != Launcher.SectionKey(Block(false, true, 5), out _), "another count, folded");
        static Section Short(int count)
        {
            var s = Block(false, false, count);
            s.Items.Add(new ResultItem { Id = "app:a", Title = "A" });
            return s;
        }
        Check.True(Launcher.SectionKey(Short(4), out _) != Launcher.SectionKey(Short(3), out _), "another count, one item either way");
        // a token block's span: the same title, note and count, another picked chip and numbers
        static Section Tokens(Bloom.Ai.TokenPeriod p) => new("토큰 사용량", "방금")
        {
            HomeKey = "tokens",
            Widget = () => TokenViews.Summary(TokenDemo.View(p), false, false, _ => { }),
        };
        string day = Launcher.SectionKey(Tokens(Bloom.Ai.TokenPeriod.Day), out _);
        Check.Eq(Launcher.SectionKey(Tokens(Bloom.Ai.TokenPeriod.Day), out _), day, "the same span");
        Check.True(Launcher.SectionKey(Tokens(Bloom.Ai.TokenPeriod.Week), out _) != day, "another span");
    }

    /// A token block's kept span by its name; none, a number or a name that is not a span is the last 24 hours.
    public static void TokenPeriodOf()
    {
        Check.Eq(string.Join(" ", new[] { null, "7", "4", "Year", "month", "Month", "All" }.Select(s => Engine.PeriodOf(s).ToString())), "Day Day Day Day Day Month All", "kept spans");
    }

    /// ↑ ↓ through the home (Launcher.NextStop): the favorites strip is a stop in its block's place among the rows —
    /// the left column's blocks, then the right one's, in one column as in two.
    public static void KeysThroughBlocks()
    {
        // `steps` presses of `delta` from row `sel` (or the strip): the stops, ★ for the strip
        static string Walk(int rows, int stripAt, int sel, bool inStrip, int delta, int steps)
        {
            var seen = new List<string>();
            for (int n = 0; n < steps; n++)
            {
                if (Launcher.NextStop(sel, inStrip, delta, rows, stripAt) is not { } to) return "none";
                inStrip = to < 0;
                if (!inStrip) sel = to;
                seen.Add(inStrip ? "★" : sel.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(" ", seen);
        }

        // the defaults' columns with timers on: left usage, timers (rows 0-2), week; right favorites, recent (rows 3-7)
        From("{}", () => Check.True(Home.Of("favorites")!.Right is true && Home.Of("timers")!.Right is false, "favorites in the right column"));
        Check.Eq(Walk(8, 3, 0, false, 1, 9), "1 2 ★ 3 4 5 6 7 0", "↓ from the first timer: past the last one onto the favorites, then recent, then around");
        Check.Eq(Walk(8, 3, 4, false, -1, 6), "3 ★ 2 1 0 7", "↑ from recent: the favorites, then the timers, then around");
        Check.Eq(Walk(8, 3, 0, true, 1, 1) + " / " + Walk(8, 3, 0, true, -1, 1), "3 / 2", "from the favorites: ↓ the right column's next, ↑ the left one's last");
        Check.Eq(Walk(8, 3, 0, false, 8, 2), "7 7", "PageDown stops at the end");
        Check.Eq(Walk(8, 3, 7, false, -8, 2), "0 0", "PageUp stops at the top");
        Check.Eq(Walk(12, 3, 10, false, -8, 2), "★ 0", "PageUp onto the favorites, then the top");
        // the favorites last (the right column's end): ↓ from the last row onto them, then around to the top
        Check.Eq(Walk(4, 4, 2, false, 1, 4), "3 ★ 0 1", "the favorites last");
        Check.Eq(Walk(4, 4, 0, false, -1, 2), "★ 3", "↑ from the top: around to the favorites");
        // the favorites first (as before columns), none, or nothing but them
        Check.Eq(Walk(3, 0, 1, false, -1, 3), "0 ★ 2", "the favorites first");
        Check.Eq(Walk(3, -1, 1, false, 1, 3), "2 0 1", "no favorites: the rows around");
        Check.Eq(Walk(0, 0, -1, true, 1, 2), "★ ★", "the favorites alone stay");
        Check.Eq(Walk(0, -1, -1, false, 1, 1), "none", "nothing to go to");
    }

    /// A held block that loses the mouse with the button still down (Esc twice: 홈 편집 ends, the home drawn anew under
    /// the mouse): the release that comes clicks nothing (Launcher.HomeDrag.cs); and a row clicks only on a press and a
    /// release both on it (Ui\Controls\Row.cs).
    public static void ReleaseAfterALostHold() => LauncherPreviewTests.WithLauncher(window =>
    {
        static void Press(UIElement on) =>
            on.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        // the button let go as WPF sends it: the preview, then the same event bubbling; true: the preview was handled
        static bool Release(UIElement on)
        {
            var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent };
            on.RaiseEvent(e);
            bool swallowed = e.Handled;
            e.RoutedEvent = UIElement.MouseLeftButtonUpEvent;
            on.RaiseEvent(e);
            return swallowed;
        }

        int clicks = 0;
        var a = new Row(null) { Clicked = () => clicks++ };
        var b = new Row(null) { Clicked = () => clicks++ };
        Release(a);
        Check.Eq(clicks, 0, "a row let go on with no press on it: no click");
        Press(a);
        Release(a);
        Check.Eq(clicks, 1, "pressed and let go on a row: a click");
        Press(a);
        Release(b);
        Check.Eq(clicks, 1, "pressed on one row, let go on another: no click");
        Press(a);
        Press(window);
        Release(a);
        Check.Eq(clicks, 1, "a press elsewhere in a window since: no click");

        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var held = typeof(Launcher).GetField("hold", instance)!;
        var source = new Border();
        typeof(Launcher).GetMethod("WireHold", instance)!.Invoke(window, new object[] { source });
        void Hold()
        {
            var type = typeof(Launcher).GetNestedType("Hold", BindingFlags.NonPublic)!;
            var h = Activator.CreateInstance(type)!;
            type.GetField("Source")!.SetValue(h, source);
            type.GetField("Key")!.SetValue(h, "week");
            held.SetValue(window, h);
        }
        void Lose() => source.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });

        Hold();
        Lose();
        Check.True(held.GetValue(window) is null, "the hold ends as the mouse goes");
        Check.True(Release(window), "the button let go after it: clicks nothing");
        Check.True(!Release(window), "… that once");
        Hold();
        Lose();
        Press(window);
        Check.True(!Release(window), "a new press since: its release is the window's as ever");
        Hold();
        Release(source);
        Lose(); // the mouse let go after the hold ended
        Check.True(held.GetValue(window) is null && !Release(window), "a hold let go as usual leaves the next release alone");
    });
}
