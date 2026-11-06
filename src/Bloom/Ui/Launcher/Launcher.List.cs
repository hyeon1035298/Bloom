using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The result list: refresh, render, selection (with the favorites row), running items.
internal sealed partial class Launcher
{
    // ── list ────────────────────────────────────────────────────────────────────────────────────
    public ResultItem? Selected => inStrip && strip is { } st && stripSel < st.Items.Count ? st.Items[stripSel] : sel >= 0 && sel < rows.Count ? rows[sel].Item : null;

    private void PathReady(string full)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && Mode == Mode.Root && PathInput.Expand(box.Text) == full) Refresh(true);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnText()
    {
        placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (suppressText) return;
        if (Mode is Mode.Chat)
        {
            if (thread is not null && box.Text.Length > 0) host.Chats.PreStart(thread); // typing: the process starts before Enter
            UpdateChrome();
            return;
        }
        Refresh(false);
    }

    /// The view built again: `keep`, the selection stays (`select`: on that item instead, for the caller to bring into
    /// view). 홈 편집's changes keep the scroll where it was.
    public void Refresh(bool keep, string? select = null)
    {
        int v = ++version;
        string text = box.Text;
        string? keepId = select ?? (keep ? Selected?.Id : null);
        switch (Mode)
        {
            case Mode.Root:
                sections = Engine.Root(text);
                Render(sections, keepId, keepScroll: keep && select is null && HomeEditing && text.Length == 0);
                debounce.Stop();
                if (Settings.Current.FilesInRoot && text.Trim().Length >= 2) debounce.Start();
                break;
            case Mode.Form:
                if (form is not { } f) break;
                sections = Engine.FormView(f, text);
                Render(sections, "form:" + f.Step);
                break;
            case Mode.Files:
                debounce.Stop();
                debounce.Start();
                work.Working = true;
                break;
            default:
                var spec = View(Mode);
                if (spec.Build is null) break;
                sections = spec.Build(text);
                Render(sections, keepId, keepScroll: keep && spec.KeepScroll);
                break;
        }
        _ = v;
    }

    private bool slowBusy, slowAgain; // an Everything question is out; the text changed meanwhile (debounce)

    /// The slow half of a refresh: Everything, one question at a time; the text typed meanwhile goes next.
    private async Task Slow()
    {
        slowBusy = true;
        try
        {
            await SlowOnce();
        }
        finally
        {
            slowBusy = false;
        }
        if (!slowAgain) return;
        slowAgain = false;
        await Slow();
    }

    private async Task SlowOnce()
    {
        int v = version;
        string text = box.Text;
        if (Mode == Mode.Root)
        {
            void Insert(Section s)
            {
                int at = sections.FindIndex(x => x.Fallback);
                if (at < 0) at = sections.Count;
                sections.Insert(at, s);
                Render(sections, Selected?.Id, keepScroll: true);
            }
            var files = await Engine.RootFiles(text);
            if (v != version || Mode != Mode.Root) return;
            if (files is not null) Insert(files);
        }
        else if (Mode == Mode.Files)
        {
            work.Working = true;
            var result = await Engine.Files(text, fileKind);
            if (v != version || Mode != Mode.Files) return;
            work.Working = false;
            sections = result;
            Render(sections, null);
        }
    }

    /// Rows built at once; a longer list gets the rest in bounded Background slices, so input can run between them
    /// (a row costs 1–2 ms: the emoji view's 88 took ~200 ms in one go). The kept row is always among the first ones.
    /// While the launcher opens, only what a screen holds comes first (ScreenRows) and the rest waits for it to be on
    /// screen (afterReveal): built first, they held the reveal back by their own time and a second layout pass.
    private const int FirstRows = 24;

    /// The rows the list shows at once (its height as last laid out, over a row's 42 px), one more for a part shown:
    /// the home's 명령 alone, ~20 rows below the fold, took 30 ms of the opening's first frame (measured).
    private int ScreenRows() => scroll.ActualHeight > 0 ? Math.Min(FirstRows, (int)(scroll.ActualHeight / 42) + 1) : FirstRows;

    private int renderVersion;

    private void Render(List<Section> secs, string? keepId, bool keepScroll = false)
    {
        double offset = scroll.VerticalOffset;
        int v = ++renderVersion;
        int keep = -1, keepStrip = -1;
        inStrip = false;
        var shown = secs.Where(s => s.Widget is not null || s.Items.Count > 0 || s.Collapsed).ToList();
        homeShown = shown.Select(s => s.HomeKey).OfType<string>().ToHashSet();
        // 홈 편집: "+ 블록 추가" at the columns' ends (Launcher.HomeAdd.cs); a home edited is drawn anew each time
        if (HomeEditing && Mode == Mode.Root && box.Text.Length == 0) AddFeet(shown);
        // the home: its blocks stay as the list holds them where they would show the same (Launcher.Kept.cs)
        string? look = null;
        List<string>? keys = null;
        var made = new Dictionary<Section, FrameworkElement>(); // widgets made for the keys, used if drawn anew
        if (Mode == Mode.Root && box.Text.Length == 0 && shown.Count > 0 && shown.All(s => s.HomeKey is not null))
        {
            look = HomeLook();
            if (!homeGrid.Splitting) homeGrid.Split = Settings.Current.HomeSplit; // as kept, or as a settings file brought in has it
            keys = [];
            foreach (var s in shown)
            {
                keys.Add(SectionKey(s, out var widget));
                if (widget is not null) made[s] = widget;
            }
        }
        if (keys is not null && KeepHome(shown, look!, keys, made))
        {
            if (keepId is not null)
            {
                keep = rows.FindIndex(r => r.Item?.Id == keepId);
                keepStrip = strip?.Items.FindIndex(x => x.Id == keepId) ?? -1;
            }
        }
        else
        {
            home = null;
            list.Children.Clear();
            homeGrid.Children.Clear();
            rows.Clear();
            strip = null;
            // the list as steps in order (a section's start, then its rows), so the late rows land where they belong
            var steps = new List<(Action run, bool row)>();
            Panel into = list;
            int first = opening ? ScreenRows() : FirstRows, need = first, counted = 0;
            bool wide = HomeWide(), right = false;
            var holders = new UIElement?[shown.Count];
            var starts = new int[shown.Count];
            var strips = new FavStrip?[shown.Count];
            for (int n = 0; n < shown.Count; n++)
            {
                var sec = shown[n];
                int at = n;
                // the favorites row comes in the first go wherever it is: the home opens on it (Enter runs the first one)
                if (sec.Strip) need = Math.Max(need, counted);
                // two columns: the right one's first screen too (its rows come after all of the left one's)
                if (wide && sec.HomeRight && !right)
                {
                    right = true;
                    need = Math.Max(need, counted + first);
                }
                steps.Add((() =>
                {
                    starts[at] = rows.Count;
                    into = StartSection(sec, keepId, made.GetValueOrDefault(sec), out int ks, out holders[at]);
                    if (ks >= 0) keepStrip = ks;
                    if (sec.Strip) strips[at] = strip;
                }, false));
                if (sec.Widget is not null || sec.Strip) continue;
                foreach (var item in sec.Items)
                {
                    if (keepId is not null && item.Id == keepId) need = Math.Max(need, counted + 1); // past the right column's first screen too
                    counted++;
                    var it = item;
                    steps.Add((() =>
                    {
                        if (keepId is not null && it.Id == keepId && keep < 0) keep = rows.Count;
                        into.Children.Add(AddRow(it));
                    }, true));
                }
            }
            if (keys is not null)
                steps.Add((() =>
                {
                    // the home whole (its late rows too), one box per block: kept for the next time
                    if (homeGrid.Children.Count != shown.Count) return;
                    var blocks = new List<Block>(shown.Count);
                    for (int n = 0; n < shown.Count; n++)
                    {
                        int end = n + 1 < shown.Count ? starts[n + 1] : rows.Count;
                        blocks.Add(new Block(keys[n], shown[n].HomeKey, holders[n]!, rows.GetRange(starts[n], end - starts[n]), strips[n]));
                    }
                    home = blocks;
                    homeLook = look;
                }, false));
            int built = 0, next = 0;
            for (; next < steps.Count; next++)
            {
                if (steps[next].row && built >= need) break;
                steps[next].run();
                if (steps[next].row) built++;
            }
            if (next < steps.Count)
            {
                int from = next;
                void Rest()
                {
                    RenderSlices.Run(Dispatcher, from, steps.Count, i => steps[i].run(), () => v == renderVersion,
                        () =>
                        {
                            if (keepScroll) scroll.ScrollToVerticalOffset(offset);
                        });
                }
                if (opening) afterReveal = Rest;
                else Dispatcher.BeginInvoke(Rest, System.Windows.Threading.DispatcherPriority.Background);
            }
            if (rows.Count == 0 && strip is null)
            {
                ShowPreview(null);
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 64, 0, 0) };
                empty.Children.Add(Theme.Glyph(G.Search, 26, Theme.P.TextFaint));
                var t = Theme.T(Mode == Mode.Root && box.Text.Length == 0 ? Tr("검색어를 입력하세요", "Type to search") : Tr("검색 결과 없음", "Nothing found"), 13.5, Theme.P.TextFaint);
                t.Margin = new Thickness(0, 12, 0, 0);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(t);
                list.Children.Add(empty);
            }
        }
        sel = -1;
        // the empty view starts on the first favorite (Enter opens it, as before the row of icons)
        if (strip is not null && (keepStrip >= 0 || keep < 0)) SelectStrip(Math.Max(0, keepStrip), false);
        else Select(keep >= 0 ? keep : 0, false);
        if (keepScroll) scroll.ScrollToVerticalOffset(offset);
        else if (keep < 0 && keepStrip < 0) scroll.ScrollToTop(); // a kept favorite, as a kept row, keeps the view on it
        UpdateChrome();
    }

    /// A section's start in the list: a home block's own box in its column (while 홈 편집 is on its tools, and its header
    /// row the handle that drags it), a column's foot (Launcher.HomeAdd.cs), a widget, the favorites strip or a header.
    /// Returns the panel its rows go into; `keepStrip`: the kept item's place in the strip (or -1); `holder`: a home
    /// block's box. `widget`: the section's widget if already made (SectionKey).
    private Panel StartSection(Section s, string? keepId, FrameworkElement? widget, out int keepStrip, out UIElement? holder)
    {
        keepStrip = -1;
        holder = null;
        if (s.HomeFoot) return StartFoot(s, out holder);
        Panel into = list;
        Action? fold = null;
        Grid? blockBox = null;
        FrameworkElement? tools = null;
        if (s.HomeKey is { } homeKey)
        {
            var inner = new StackPanel();
            blockBox = new Grid { Background = Brushes.Transparent, Tag = homeKey };
            blockBox.Children.Add(inner);
            // only while 홈 편집 is on (the footer button / Ctrl+E), never on a mere hover
            if (HomeEditing || ShowHomeToolsForQa)
            {
                tools = HomeTools(homeKey, s);
                blockBox.Children.Add(tools);
            }
            HomeColumns.SetRight(blockBox, s.HomeRight);
            if (homeGrid.Parent is null) list.Children.Add(homeGrid);
            homeGrid.Children.Add(blockBox);
            holder = blockBox;
            into = inner;
            // folded and opened by its header while 홈 편집 is on; a folded one opens by it any time
            if (tools is not null || s.Collapsed) fold = () => FoldBlock(homeKey, !s.Collapsed);
        }
        // the favorites strip has no header on the home, but one to fold it by (and for the tools to sit on) while 홈 편집 is on
        var header = s.Collapsed ? Section2.Header(s.Title ?? "", s.Note, s.Reload, fold, true)
            : s.Title is null || (s.Strip && fold is null) ? null
            : Section2.Header(s.Title, s.Note, s.Widget is not null ? s.Reload : null, fold);
        if (header is not null) into.Children.Add(blockBox is not null && tools is not null ? HeaderHandle(header, blockBox, tools, fold) : header);
        if (s.Collapsed) return into;
        if (s.Widget is { } make)
        {
            into.Children.Add(widget ?? make());
            return into;
        }
        if (s.Strip)
        {
            var st = new FavStrip(s.Items);
            st.Clicked = i =>
            {
                SelectStrip(i);
                RunSelected(null);
            };
            st.RightClicked = i =>
            {
                SelectStrip(i);
                OpenActions();
            };
            var row = s.Items;
            st.Reordered = (from, before) =>
            {
                SelectStrip(from); // the moved one stays selected after the refresh
                Favorites.MoveBefore(row[from].Id, before < row.Count ? row[before].Id : null);
                Refresh(true);
            };
            if (keepId is not null) keepStrip = s.Items.FindIndex(x => x.Id == keepId);
            strip = st;
            stripAt = rows.Count; // the rows of the blocks before it
            into.Children.Add(st);
        }
        return into;
    }

    /// One result row, wired to select and run; added to `rows` (the keyboard's order).
    private Row AddRow(ResultItem item)
    {
        Row r = item.Data is HomeAdd add ? FootRow(item, add) : item.Data is HomeCommands.Tile tile ? Row.AddTile(item, tile.Open)
            : item.Data is HomeCommands.Card ? Row.Card(item) : item.Kind == ItemKind.Calc ? Row.Calc(item) : Split || Mode == Mode.Calendar ? Row.Compact(item, Mode is Mode.Clipboard or Mode.Calendar) : Row.Result(item);
        r.Clicked = () =>
        {
            Select(rows.IndexOf(r), false);
            RunSelected(null);
        };
        r.RightClicked = () =>
        {
            Select(rows.IndexOf(r), false);
            OpenActions();
        };
        r.SideClicked = act =>
        {
            Select(rows.IndexOf(r), false);
            RunAction(act);
        };
        rows.Add(r);
        return r;
    }

    private void Select(int index, bool bring = true)
    {
        if (rows.Count == 0)
        {
            sel = -1;
            return;
        }
        index = Math.Clamp(index, 0, rows.Count - 1);
        if (inStrip)
        {
            inStrip = false;
            strip?.Select(-1);
        }
        if (sel >= 0 && sel < rows.Count) rows[sel].Selected = false;
        sel = index;
        rows[sel].Selected = true;
        if (bring) rows[sel].BringIntoView(new Rect(0, -30, rows[sel].ActualWidth, rows[sel].ActualHeight + 60));
        if (Split) ShowPreview(rows[sel].Item);
        UpdateChrome();
    }

    private void SelectStrip(int i, bool bring = true)
    {
        if (strip is null || strip.Items.Count == 0) return;
        if (sel >= 0 && sel < rows.Count) rows[sel].Selected = false;
        sel = -1;
        inStrip = true;
        stripSel = Math.Clamp(i, 0, strip.Items.Count - 1);
        strip.Select(stripSel);
        // into view where its block is (the home's top, or lower down, or the right column)
        if (bring) strip.BringIntoView(new Rect(0, -30, strip.ActualWidth, strip.ActualHeight + 60));
        UpdateChrome();
    }

    /// Ctrl+1…9: the favorite in that place of the row as shown (ones that can't show — a removed app, a feature switched
    /// off — take no number), from any view of the root.
    private void RunFavorite(int i)
    {
        if (Favorites.Current.Items.Select(Engine.FromFavorite).OfType<ResultItem>().ElementAtOrDefault(i) is not { Run: { } run } item) return;
        if (!item.StayOpen) Dismiss();
        try
        {
            run();
        }
        catch (Exception e)
        {
            Log.Error("favorite " + item.Id, e);
        }
    }

    private void Move(int delta)
    {
        if (NextStop(sel, inStrip, delta, rows.Count, strip is { Items.Count: > 0 } ? stripAt : -1) is not { } to) return;
        if (to < 0) SelectStrip(stripSel); // the icon it was left on
        else Select(to);
    }

    /// ↑ ↓ (PageUp/PageDown: more) from row `sel` or the favorites strip (`inStrip`), among `rows` rows: the row it
    /// lands on, -1 for the strip, null when there is nowhere to go. The strip is a stop of its own in its block's place,
    /// `stripAt` rows before it (-1: no strip), so the keys go through the blocks in their order — the left column's,
    /// then the right one's — in one column as in two. Single steps wrap around, longer ones stop at the ends.
    internal static int? NextStop(int sel, bool inStrip, int delta, int rows, int stripAt)
    {
        int count = rows + (stripAt >= 0 ? 1 : 0);
        if (count == 0) return null;
        int at = inStrip ? stripAt : stripAt >= 0 && sel >= stripAt ? sel + 1 : sel;
        int to = at + delta;
        if (to < 0) to = delta < -1 ? 0 : count - 1;
        else if (to >= count) to = delta > 1 ? count - 1 : 0;
        return to == stripAt ? -1 : stripAt >= 0 && to > stripAt ? to - 1 : to;
    }

    public void RunSelected(string? combo)
    {
        var item = Selected;
        if (item is null) return;
        if (combo is not null)
        {
            var act = ActionsFor(item).FirstOrDefault(a => a.Keys == combo);
            if (act is not null) RunAction(act);
            return;
        }
        if (item.Run is null) return;
        if (Mode == Mode.Root && box.Text.Trim().Length > 0) QueryMemory.Learn(box.Text, item.Id);
        if (!item.StayOpen) Dismiss();
        try
        {
            item.Run();
        }
        catch (Exception e)
        {
            Log.Error("run " + item.Id, e);
        }
    }
}
