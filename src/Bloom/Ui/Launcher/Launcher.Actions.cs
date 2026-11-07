using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// What can be done to a result: its actions panel, confirmations, pickers, home editing, favorites.
internal sealed partial class Launcher
{
    public void RunAction(ActionItem a)
    {
        actions.Close();
        if (!a.StayOpen) Dismiss();
        try
        {
            a.Run();
        }
        catch (Exception e)
        {
            Log.Error("action " + a.Title, e);
        }
        if (a.StayOpen && IsVisible && !actions.IsOpen) FocusBox();
    }

    /// A yes/no question in the action panel: Enter = the action, Esc = cancel.
    public void Confirm(string question, ActionItem yes) =>
        actions.Open(question, new List<ActionItem> { yes, new() { Title = Tr("취소", "Cancel"), Keys = "Esc", Glyph = G.Close, StayOpen = true, Run = () => { } } });

    /// A few choices in the action panel, on the selected row (컬러 팔레트's 전체 복사): Enter runs one, Esc closes it.
    public void Choose(string title, List<ActionItem> list) => actions.Open(title, list, 0, SelectedBox());

    public List<ActionItem> ActionsFor(ResultItem item)
    {
        var list = new List<ActionItem>();
        if (item.Run is not null && item.PrimaryLabel.Length > 0)
            list.Add(new ActionItem { Title = item.PrimaryLabel, Keys = "Enter", Glyph = item.Kind == ItemKind.Calc ? G.Copy : G.NewWindow, Run = item.Run, StayOpen = item.StayOpen });
        if (item.MoreActions?.Invoke() is { } more) list.AddRange(more);
        if (item.Pinnable)
        {
            bool fav = Favorites.Has(item.Id);
            list.Add(new ActionItem
            {
                Title = fav ? Tr("즐겨찾기에서 빼기", "Remove from favorites") : Tr("즐겨찾기에 추가", "Add to favorites"),
                Keys = "Ctrl+D",
                Glyph = fav ? G.Unstar : G.Star,
                StayOpen = true,
                Run = () => ToggleFavorite(item),
            });
            // the menu keeps what is about the item itself (a longer one was cut off at its end): a favorite
            // moves with Ctrl+Shift+←→ or a drag in the row, and AI drawing and the original icon sit in 아이콘 바꾸기
            if (fav)
                list.Add(new ActionItem { Title = Tr("아이콘 바꾸기…", "Change icon…"), Glyph = G.Palette, StayOpen = true, Run = () => PickPicture(item.Id, item.Title) });
            list.Add(new ActionItem { Title = Tr("즐겨찾기 폴더에 넣기…", "Add to a favorites folder…"), Glyph = G.Folder, StayOpen = true, Run = () => PickGroup(item) });
            list.AddRange(Engine.ItemKeyActions(item));
            if (Home.ForCommand(item.Id) is { } widget)
            {
                bool on = Home.IsOn(widget);
                list.Add(new ActionItem { Title = on ? Tr($"홈에서 빼기 ({Home.Name(widget)})", $"Remove from home ({Home.Name(widget)})") : Tr($"홈에 고정 ({Home.Name(widget)})", $"Pin to home ({Home.Name(widget)})"), Glyph = on ? G.Unstar : G.Star, StayOpen = true, Run = () => { Home.Set(widget, !on); ShowToast(on ? Tr($"홈에서 ‘{Home.Name(widget)}’ 제거 완료", $"Removed ‘{Home.Name(widget)}’ from home") : Tr($"홈에 ‘{Home.Name(widget)}’ 고정 완료", $"Pinned ‘{Home.Name(widget)}’ to home")); Refresh(true); } });
            }
            if (Usage.Current.Items.ContainsKey(item.Id) && Mode == Mode.Root && box.Text.Length == 0 && !fav)
                list.Add(new ActionItem { Title = Tr("최근 사용에서 지우기", "Remove from recent"), Glyph = G.Delete, StayOpen = true, Run = () => { Usage.Forget(item.Id); Refresh(false); } });
        }
        if (Mode == Mode.Root && HiddenResults.CanHide(item) && !(box.Text.Length == 0 && Favorites.Has(item.Id))) // a favorite stays on the home
            list.Add(new ActionItem { Title = Tr("검색에서 숨기기", "Hide from search"), Glyph = G.Hide, StayOpen = true, Run = () => HideResult(item) });
        return list;
    }

    /// Ctrl+K › 검색에서 숨기기: the row leaves the search and the home's suggestions until 설정 › 런처 › 숨긴 항목 brings it back.
    private void HideResult(ResultItem item)
    {
        HiddenResults.Add(item);
        ShowToast(Tr($"‘{ClipText(item.Title, 16)}’ 숨김 · 설정 › 런처 › 숨긴 항목에서 되돌리기", $"‘{ClipText(item.Title, 16)}’ hidden · bring it back in Settings › Launcher › Hidden items"));
        Refresh(false);
    }

    /// The symbols a favorite (or folder) can wear on its black tile; ticked = the one it has.
    public void PickPicture(string id, string title)
    {
        string? now = Favorites.Find(id)?.Mono;
        var list = Icons.Pictures.Select(p => new ActionItem
        {
            Title = p.name,
            Glyph = p.value == now ? G.Check : p.value == "pic:sparkle" ? G.Sparkle : (char)int.Parse(p.value.AsSpan(4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture),
            StayOpen = true,
            Run = () =>
            {
                Favorites.SetMono(id, p.value);
                Refresh(true);
            },
        }).ToList();
        list.Insert(0, new ActionItem { Title = Tr("AI로 그리기… (원하는 그림을 말로)", "Draw with AI… (describe the picture)"), Glyph = G.Sparkle, StayOpen = true, Run = () => StartForm(Engine.AiIconForm(id, title)) });
        if (now is not null) list.Add(new ActionItem { Title = Tr("원래 아이콘으로", "Back to the original icon"), Glyph = G.Refresh, StayOpen = true, Run = () => { Favorites.SetMono(id, null); Refresh(true); } });
        actions.Open(Tr($"‘{ClipText(title, 16)}’ 그림 아이콘", $"Picture icon for ‘{ClipText(title, 16)}’"), list, Math.Max(0, Array.FindIndex(Icons.Pictures, p => p.value == now) + 1));
    }

    public static bool ShowHomeToolsForQa;

    /// 홈 편집: the blocks show their tools (Launcher.HomeTools), drag by their header rows and fold by their titles
    /// (Launcher.HomeDrag.cs), the AI usage cards ‹ › ✎, and each column ends with "+ 블록 추가" (Launcher.HomeAdd.cs).
    public bool HomeEditing { get; private set; }

    public void ToggleHomeEdit()
    {
        HomeEditing = !HomeEditing;
        addOpen = null;
        if (HomeEditing && (Mode != Mode.Root || box.Text.Length > 0)) Go(Mode.Root);
        else Refresh(true);
        if (HomeEditing) ShowToast(Tr("홈 편집 · 블록 제목 줄을 끌어 옮기고 제목을 눌러 접기 · ▲ ▼ ‹ › 이동, − + 개수, ✕ 숨기기 · ‘+ 블록 추가’로 다시 넣기 · 끝내려면 Esc", "Editing home · drag a block by its title row, click the title to fold it · ▲ ▼ ‹ › move, − + rows, ✕ hides · ‘+ Add a block’ puts one back · Esc to finish"));
    }

    /// The home blocks shown as last drawn (their ▲ ▼ ‹ › go past these only).
    private HashSet<string> homeShown = [];

    /// Whether the home is two columns at the list's width now (before the first layout: at the window's).
    private bool HomeWide() => HomeColumns.Wide(body.ActualWidth > 0 ? body.ActualWidth : Width / Theme.Scale);

    /// A block's header: folded to its header, or open again.
    private void FoldBlock(string key, bool folded)
    {
        Home.SetCollapsed(key, folded);
        Refresh(true);
    }

    /// AI draws a favorite's icon (one Claude call); the toast says when it starts and how it ended.
    public async void DrawIcon(string id, string name, string? idea)
    {
        ShowToast(Tr($"‘{ClipText(name, 14)}’ 아이콘 생성 중… (사용량 1회)", $"Creating icon for ‘{ClipText(name, 14)}’… (1 use)"));
        var (icon, error) = await Ai.IconMaker.Draw(name, idea, System.Threading.CancellationToken.None);
        if (icon is null)
        {
            ShowToast(error ?? Tr("아이콘 생성 실패 · 다시 시도하세요", "Icon creation failed. Try again."));
            return;
        }
        Favorites.SetMono(id, icon);
        if (IsVisible && Mode == Mode.Root) Refresh(true);
        ShowToast(Tr($"‘{ClipText(name, 14)}’ 아이콘 변경 완료 · Ctrl+K로 다시 그리기", $"Icon changed for ‘{ClipText(name, 14)}’ · Ctrl+K to draw again"));
    }

    /// A home block's tools (top right, over its header): − n + for a list's rows, ≡ to drag it (as its whole header row
    /// does, Launcher.HomeDrag.cs), ▲ ▼ a place up or down as laid out (one column: through both), ‹ › to the other
    /// column, ✕ to hide it. Drawn from the block's section `s` (its column and count), which its kept key holds
    /// (Launcher.Kept.cs).
    private FrameworkElement HomeTools(string key, Section s)
    {
        bool wide = HomeWide(), right = s.HomeRight;
        var shownNow = homeShown;
        void Then(Action change)
        {
            change();
            Refresh(true);
        }
        void Hide()
        {
            Home.Set(key, false);
            ShowToast(Tr($"‘{Home.Name(key)}’ 숨김 · ‘+ 블록 추가’로 다시 넣기", $"‘{Home.Name(key)}’ hidden · ‘+ Add a block’ puts it back"));
            Refresh(true);
        }
        var grip = HomeViews.EditButton(G.Grip, Tr("끌어서 옮기기", "Drag to move"), true, null);
        grip.Cursor = Cursors.SizeAll;
        var bar = HomeViews.Pill(grip,
            HomeViews.EditButton(G.Up, Tr("위로", "Up"), Home.CanStep(key, -1, wide, shownNow), () => Then(() => Home.Step(key, -1, wide, shownNow))),
            HomeViews.EditButton(G.Down, Tr("아래로", "Down"), Home.CanStep(key, 1, wide, shownNow), () => Then(() => Home.Step(key, 1, wide, shownNow))),
            HomeViews.EditButton(G.Left, Tr("왼쪽 열로", "To the left column"), right, () => Then(() => Home.MoveAcross(key, shownNow))),
            HomeViews.EditButton(G.Right, Tr("오른쪽 열로", "To the right column"), !right, () => Then(() => Home.MoveAcross(key, shownNow))),
            HomeViews.EditButton(G.Close, Tr("홈에서 숨기기", "Hide from home"), true, Hide));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 16, 0) };
        if (s.HomeCount > 0)
        {
            var stepper = HomeViews.Stepper(s.HomeCount, Home.CountMin, Home.CountMax, n => Then(() => Home.SetCount(key, n)));
            stepper.Margin = new Thickness(0, 0, 6, 0);
            tools.Children.Add(stepper);
        }
        tools.Children.Add(bar);
        return tools;
    }

    /// Which folder an item goes into (or a new one).
    private void PickGroup(ResultItem item)
    {
        if (item.AsFavorite is not { } entry) return;
        var groups = Favorites.Current.Items.Where(g => g.Kind == Favorites.GroupKind).ToList();
        string? now = Favorites.GroupOf(item.Id)?.Id;
        var list = groups.Select(g => new ActionItem
        {
            Title = g.Title + (g.Id == now ? Tr("  (지금 여기)", "  (here now)") : Tr($"  · {g.Items?.Count ?? 0}개", $"  · {g.Items?.Count ?? 0}")),
            Glyph = g.Id == now ? G.Check : G.Folder,
            StayOpen = true,
            Run = () =>
            {
                Favorites.AddToGroup(g.Id, entry);
                ShowToast(Tr($"‘{g.Title}’에 추가 완료", $"Added to ‘{g.Title}’"));
                Refresh(true);
            },
        }).ToList();
        list.Add(new ActionItem { Title = Tr("새 폴더 만들기…", "New folder…"), Glyph = G.Add, StayOpen = true, Run = () => StartForm(Engine.GroupForm(null, entry)) });
        actions.Open(Tr($"즐겨찾기 폴더에 넣기 · {ClipText(item.Title, 20)}", $"Add to a favorites folder · {ClipText(item.Title, 20)}"), list);
    }

    private void ToggleFavorite(ResultItem item)
    {
        if (item.AsFavorite is not { } e) return;
        if (Favorites.Has(item.Id))
        {
            Favorites.Remove(item.Id);
            ShowToast(Tr("즐겨찾기에서 제거 완료", "Removed from favorites"));
        }
        else
        {
            Favorites.Add(e);
            ShowToast(Tr("즐겨찾기 추가 완료", "Added to favorites"));
        }
        Refresh(true);
    }

    private void MoveFavorite(ResultItem item, int delta)
    {
        if (Mode == Mode.Group && groupId is not null) Favorites.MoveInGroup(groupId, item.Id, delta);
        else if (Favorites.Current.Items.Exists(e => e.Id == item.Id)) Favorites.Move(item.Id, delta); // the row: items and folders
        else return;
        Refresh(true);
    }

    public void OpenActions()
    {
        if (Mode == Mode.Chat)
        {
            var items = chat.Actions(thread);
            items.Insert(Math.Min(4, items.Count), new ActionItem { Title = Tr("AI 채팅 창에서 열기", "Open in the chat window"), Keys = "Ctrl+Shift+O", Glyph = G.NewWindow, Run = OpenInWindow });
            actions.Open(Tr("AI 채팅", "AI chat"), items);
            return;
        }
        if (Selected is not { } item) return;
        var list = ActionsFor(item);
        if (list.Count == 0) return;
        actions.Open(item.Title, list, 0, SelectedBox());
    }

    public void OpenActions(ResultItem item)
    {
        var list = ActionsFor(item);
        if (list.Count > 0) actions.Open(item.Title, list);
    }

    /// The selected row's box in the body (the 더보기 menu opens on it); null when it isn't a list row.
    private Rect? SelectedBox()
    {
        if (rows.Find(r => r.Selected) is not { IsVisible: true } row || row.ActualHeight <= 0) return null;
        try
        {
            return row.TransformToAncestor(body).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void ActionsClosed() => FocusBox();

    public void CloseActionsForQa() => actions.Close();

    /// QA (popup-perf): a key as the open panel takes it.
    public bool ActionKeyForQa(System.Windows.Input.Key key) => actions.HandleKey(key);
}
