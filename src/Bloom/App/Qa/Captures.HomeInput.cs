using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Bloom.Core;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set homeinput`: 홈 편집 by real input. The launcher shows on the screen (`--display=<n>`, else the primary one)
/// and SendInput moves the mouse, presses its button and the keys as a hand would, for some seconds (the cursor goes
/// back after): blocks dragged by ≡, by the title and by the header's empty part, a drag with the list refreshed
/// midway, one taken back by Esc, the title clicked (fold), the tools clicked, "+ 블록 추가" opened, a card clicked and
/// one dragged into place, then the tile and a card reached by ↓ and Enter. Each step's outcome in homeinput.txt
/// (ok / FAIL), pictures of the drags midway. Input goes out only while the launcher has the foreground (else the run
/// stops sending: it would land in another app); the button and the cursor go back whatever happens.
internal static partial class Captures
{
    /// The window real input is for: none goes out while another has the foreground.
    private static IntPtr inputFor;

    private static async Task HomeInputScreens(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        Win32.GetCursorPos(out var cursorWas);
        try
        {
            await HomeInputSteps(host, w, dir, log);
        }
        catch (OperationCanceledException e) // the launcher without the foreground: the rest not sent
        {
            log.AppendLine("FAIL  " + e.Message);
        }
        finally
        {
            inputFor = IntPtr.Zero;
            Win32.SetCursorPos(cursorWas.X, cursorWas.Y);
        }
        int failed = log.ToString().Split('\n').Count(l => l.StartsWith("FAIL", StringComparison.Ordinal));
        log.AppendLine(failed == 0 ? "all ok" : $"{failed} failed");
        File.WriteAllText(Path.Combine(dir, "homeinput.txt"), log.ToString());
        Settings.Current.Home = null;
    }

    private static async Task HomeInputSteps(Host host, Launcher w, string dir, StringBuilder log)
    {
        void Check(bool ok, string what) => log.AppendLine((ok ? "ok    " : "FAIL  ") + what);
        // the blocks as they show: a column's, top to bottom (one column: all of them in the left)
        string Column(bool right) => string.Join(" ", w.HomeForQa.Children.OfType<FrameworkElement>()
            .Where(e => e.Tag is string && (VisualTreeHelper.GetOffset(e).X > 0) == right).OrderBy(e => VisualTreeHelper.GetOffset(e).Y).Select(e => (string)e.Tag));
        string Left() => Column(false);
        string Right() => Column(true);
        string Shown() => $"{Left()} | {Right()}";
        static bool Titled(FrameworkElement e, string text) => e is System.Windows.Controls.TextBlock { Text: var t } && t == text;
        Row? RowOf(string id) => Descendants(w.HomeForQa).OfType<Row>().FirstOrDefault(r => r.Item?.Id == id);
        bool AnyCard() => Descendants(w.HomeForQa).OfType<Row>().Any(r => r.Item?.Id.StartsWith("home:card:", StringComparison.Ordinal) == true);
        Settings.Current.Home = null;
        Theme.Apply("dark");
        Timers.Add("timer", Tr("집중", "Focus"), DateTime.Now.AddMinutes(25));
        for (int i = 0; i < 60 && !host.Apps.Ready; i++) await Task.Delay(100);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), step = 0;
        foreach (var app in host.Apps.Apps.Take(9)) Usage.Current.Items[app.Id] = new Usage.Entry { Count = 3, Last = now - 60_000 * step++ };
        w.Go(Mode.Root, "");
        w.Place(host.Opt.Display ?? 1);
        w.Activate();
        Win32.ForceForeground(w.Handle);
        await Task.Delay(800);
        inputFor = w.Handle;
        Foreground(); // else none of what follows goes out
        Check(true, "the launcher has the foreground");
        w.ToggleHomeEdit();
        await Task.Delay(700);
        var grid = w.HomeForQa;
        bool two = grid.ActualWidth >= HomeColumns.TwoColumnWidth;
        log.AppendLine($"window {w.ActualWidth:0}x{w.ActualHeight:0} dip, home {(two ? "two columns" : "one column")}");
        log.AppendLine($"start: {Shown()}");
        // a column's top and end on the screen (one column: the home's); off the window too, the held mouse goes on
        Point Top(bool right) => grid.PointToScreen(new Point(grid.ActualWidth * (two ? right ? 0.75 : 0.25 : 0.5), 6));
        Point End(bool right) => grid.PointToScreen(new Point(grid.ActualWidth * (two ? right ? 0.75 : 0.25 : 0.5), grid.ActualHeight - 4));
        bool StartsWith(string key, bool right) => (two ? right ? Right() : Left() : Left()).StartsWith(key, StringComparison.Ordinal);
        bool EndsWith(string key, bool right) => (two ? right ? Right() : Left() : Left() + " " + Right()).Trim().EndsWith(key, StringComparison.Ordinal);

        // the ≡ handle: "이번 주" to the top (two columns: of the right one)
        var grip = await Spot(grid, "week", e => e.ToolTip as string == Tr("끌어서 옮기기", "Drag to move"));
        Check(grip is not null, "≡ found on 이번 주");
        if (grip is { } g)
        {
            await RealDrag(g, Top(true), () => Snap(w, dir, "real-01-grip-midway"));
            log.AppendLine($"after ≡: {Shown()}");
            Check(StartsWith("week", true), "≡ drag moved 이번 주 to the top");
        }

        // by its title: "최근 사용" to the left column's end (one column: the top)
        if (await Spot(grid, "recent", e => Titled(e, Tr("최근 사용", "Recent Items"))) is { } title)
        {
            await RealDrag(title, two ? End(false) : Top(false), () => Snap(w, dir, "real-02-title-midway"));
            log.AppendLine($"after the title drag: {Shown()}");
            Check(two ? EndsWith("recent", false) : StartsWith("recent", false), "a drag by the title moved 최근 사용");
        }
        else Check(false, "최근 사용's title found");

        // by the header's empty part (between the title and the tools): "명령" to the left column's top
        if (Block(grid, "commands") is { } commands)
        {
            await RealDrag(await Reach(commands, new Point(commands.ActualWidth * 0.45, 16)), Top(false), () => Snap(w, dir, "real-03-header-midway"));
            log.AppendLine($"after the header drag: {Shown()}");
            Check(StartsWith("commands", false), "a drag by the header's empty part moved 명령");
        }

        // the list refreshed while a block is held (new data: here a timer added; the clipboard, AI usage … alike):
        // the drag goes on, the refresh comes after it
        if (await Spot(grid, "timers", e => Titled(e, Tr("타이머 · 알림", "Timers · Reminders"))) is { } held)
        {
            var to = two ? End(true) : Top(false);
            await RealDrag(held, to, async () =>
            {
                Timers.Add("timer", Tr("회의", "Meeting"), DateTime.Now.AddHours(2));
                await Task.Delay(900); // the data refresh comes 300 ms after a change
                MouseAt(to + new Vector(0, -2));
                await Task.Delay(100);
                await Snap(w, dir, "real-04-refresh-midway");
            });
            await Task.Delay(500); // the refresh put off
            log.AppendLine($"after a refresh midway: {Shown()}");
            Check(two ? EndsWith("timers", true) : StartsWith("timers", false), "a drag survives a data refresh midway (타이머 to the right column's end; one column: the top)");
            Check(Descendants(Block(grid, "timers")!).OfType<Row>().Count() == 2, "… the refresh came after it (2 timers)");
        }

        // Esc while a block is held: it goes back, the button let go clicks nothing
        if (await Spot(grid, "usage", e => Titled(e, Tr("AI 사용량", "AI Usage"))) is { } usage)
        {
            string before = Shown();
            await RealDrag(usage, End(true), async () =>
            {
                RealKey(0x1B); // Esc
                await Task.Delay(250);
            });
            Check(Shown() == before && w.HomeEditing && w.Mode == Mode.Root && w.IsVisible, "Esc while held puts it back, still editing");
        }

        // a click on the title, no move: it folds; again: it opens
        if (await Spot(grid, "recent", e => Titled(e, Tr("최근 사용", "Recent Items"))) is { } t1)
        {
            await RealClick(t1);
            Check(Home.Of("recent")!.Collapsed, "a click on the title folds 최근 사용");
            if (await Spot(grid, "recent", e => Titled(e, Tr("최근 사용", "Recent Items"))) is { } t2) await RealClick(t2);
            Check(!Home.Of("recent")!.Collapsed, "… and opens it again");
        }

        // the tools keep their clicks: ▲ on the left column's second block, + on 최근 사용's rows
        string second = Left().Split(' ')[1];
        if (await Spot(grid, second, e => e.ToolTip as string == Tr("위로", "Move Up")) is { } up)
        {
            await RealClick(up);
            Check(StartsWith(second, false), $"▲ moved {second} up");
        }
        int rowsWere = Home.Count(Home.Of("recent")!);
        if (await Spot(grid, "recent", e => e.ToolTip as string == Tr("개수 늘리기", "Show more")) is { } more)
        {
            await RealClick(more);
            Check(Home.Count(Home.Of("recent")!) == rowsWere + 1, "+ shows one more row");
        }

        // "+ 블록 추가": the tile opens its cards
        if (RowOf("home:tile:right") is { } tile)
        {
            await RealClick(await Reach(tile, new Point(tile.ActualWidth / 2, tile.ActualHeight / 2)));
            Check(AnyCard(), "the tile opens the cards");
            await Snap(w, dir, "real-05-picker");
        }
        else Check(false, "the tile found");

        // a card clicked: its block at that column's end (one column: the home's end)
        string first = Home.Hidden()[0].Key;
        if (RowOf("home:card:" + first) is { } card)
        {
            await RealClick(await Reach(card, new Point(card.ActualWidth / 2, card.ActualHeight / 2)));
            var order = Home.Order();
            Check(Home.IsOn(first) && (two ? order.FindLast(x => x.Right is true)! : order[^1]).Key == first, $"a card clicked: {first} on, at the end");
        }

        // a card dragged to the left column's top (one column: the home's top)
        string next = Home.Hidden()[0].Key;
        if (RowOf("home:card:" + next) is { } dragged)
        {
            await RealDrag(await Reach(dragged, new Point(dragged.ActualWidth / 3, dragged.ActualHeight / 2)), Top(false), () => Snap(w, dir, "real-06-card-midway"));
            Check(Home.IsOn(next) && Home.Order().Find(x => x.Right is false)!.Key == next, $"a card dragged: {next} on, at the left column's top");
            log.AppendLine($"after the cards: {Shown()}");
        }

        // keys: Esc closes the cards, ↓ reaches the tile, Enter opens it on the first card, Enter adds that one
        RealKey(0x1B);
        await Task.Delay(400);
        Check(!AnyCard() && w.HomeEditing, "Esc closes the cards (still editing)");
        for (int i = 0; i < 60 && w.Selected?.Id != "home:tile:right"; i++)
        {
            RealKey(0x28); // ↓
            await Task.Delay(60);
        }
        Check(w.Selected?.Id == "home:tile:right", "↓ reaches the tile");
        string last = Home.Hidden()[0].Key;
        RealKey(0x0D); // Enter
        await Task.Delay(500);
        Check(w.Selected?.Id == "home:card:" + last, "Enter on the tile opens it, its first card selected");
        RealKey(0x0D);
        await Task.Delay(500);
        Check(Home.IsOn(last), $"Enter on the card adds {last}");
        bool says = Descendants(grid).OfType<System.Windows.Controls.TextBlock>().Any(x => x.Text == Tr("숨긴 블록 없음 · 블록의 ✕로 숨기면 여기서 다시 추가할 수 있습니다", "No hidden blocks · Blocks hidden via ✕ can be re-added here"));
        Check(Home.Hidden().Count > 0 || (RowOf("home:tile:right") is null && says), "nothing hidden: the tile says so, no row to stop on");
        await Snap(w, dir, "real-07-after-keys");

        RealKey(0x1B);
        await Task.Delay(400);
        Check(!w.HomeEditing && w.IsVisible, "Esc ends 홈 편집");
        log.AppendLine($"end: {Shown()}");
    }

    /// The home block `key` (its box in the columns).
    private static FrameworkElement? Block(FrameworkElement grid, string key) =>
        Descendants(grid).OfType<FrameworkElement>().FirstOrDefault(e => e.Tag as string == key && VisualTreeHelper.GetParent(e) == grid);

    /// The middle of the first element in home block `key` that `match` picks, brought into view, on the screen (physical
    /// pixels): a hand presses only what it sees.
    private static async Task<Point?> Spot(FrameworkElement grid, string key, Func<FrameworkElement, bool> match) =>
        Block(grid, key) is { } block && Descendants(block).OfType<FrameworkElement>().FirstOrDefault(e => e.IsVisible && match(e)) is { } el
            ? await Reach(el, new Point(el.ActualWidth / 2, el.ActualHeight / 2))
            : null;

    /// Point `at` of `el` brought into view, on the screen (physical pixels).
    private static async Task<Point> Reach(FrameworkElement el, Point at)
    {
        el.BringIntoView(new Rect(at.X - 24, at.Y - 24, 48, 48));
        await Task.Delay(300);
        return el.PointToScreen(at);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// Before each input sent: the launcher still in the foreground, or the run stops sending (HomeInputScreens).
    private static void Foreground()
    {
        if (Win32.GetForegroundWindow() != inputFor)
            throw new OperationCanceledException("the launcher hasn't the foreground: no more input sent (it would land in another app)");
    }

    /// One mouse event as the hardware would send it: to `px` on the screen (physical pixels), with `buttons`
    /// (MOUSEEVENTF_LEFTDOWN 2); the launcher in the foreground.
    private static void MouseAt(Point px, uint buttons = 0)
    {
        Foreground();
        int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77), vw = Win32.GetSystemMetrics(78), vh = Win32.GetSystemMetrics(79);
        SendMouse(new Win32.MOUSEINPUT
        {
            dx = (int)Math.Round((px.X - vx) * 65535.0 / (vw - 1)),
            dy = (int)Math.Round((px.Y - vy) * 65535.0 / (vh - 1)),
            dwFlags = 0x0001 | 0x8000 | 0x4000 | buttons, // MOVE | ABSOLUTE | VIRTUALDESK
        });
    }

    /// The button let go where the mouse is (MOUSEEVENTF_LEFTUP), whatever has the foreground: none stays down.
    private static void LetGo() => SendMouse(new Win32.MOUSEINPUT { dwFlags = 0x0004 });

    private static void SendMouse(Win32.MOUSEINPUT mi) =>
        _ = Win32.SendInput(1, new[] { new Win32.INPUT { type = 0, mi = mi } }, Marshal.SizeOf<Win32.INPUT>());

    /// A key pressed and let go, the launcher in the foreground.
    private static void RealKey(ushort vk)
    {
        Foreground();
        Win32.SendChord(vk);
    }

    /// Pressed at `from`, moved to `to` in small steps, `midway` there (a picture), let go there; let go wherever a step
    /// failed.
    private static async Task RealDrag(Point from, Point to, Func<Task>? midway = null)
    {
        MouseAt(from);
        await Task.Delay(150);
        MouseAt(from, 2);
        try
        {
            await Task.Delay(150);
            for (int i = 1; i <= 20; i++)
            {
                MouseAt(from + (to - from) * (i / 20.0));
                await Task.Delay(30);
            }
            await Task.Delay(250);
            if (midway is not null) await midway();
            MouseAt(to);
        }
        finally
        {
            LetGo();
        }
        await Task.Delay(600);
    }

    private static async Task RealClick(Point at)
    {
        MouseAt(at);
        await Task.Delay(150);
        MouseAt(at, 2);
        try
        {
            await Task.Delay(80);
        }
        finally
        {
            LetGo();
        }
        await Task.Delay(600);
    }
}
