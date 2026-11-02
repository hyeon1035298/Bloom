using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 홈 편집's "+ 블록 추가": a tile at the end of each column (one column: one, at the home's end). Pressed, or Enter on
/// it, its cards open under it: the hidden blocks (Home.Hidden), each with its glyph, name and what it shows. A card
/// pressed (or Enter) turns its block on at that column's end; dragged, at the gap it is let go in, in either column
/// (Launcher.HomeDrag.cs). With nothing hidden the tile says so and takes no clicks. The tile and the cards are rows:
/// ↑ ↓ reach them in the columns' order, the left column's after its blocks.
internal sealed partial class Launcher
{
    /// What a tile or a card adds: the block (`Key`; null: the tile) at the end of column `Right` (null: the one column).
    private sealed record HomeAdd(string? Key, bool? Right, bool Open);

    private bool? addOpen; // the column whose cards show (one column: true, its only tile); null: none

    /// The feet among the home's blocks: two columns, the left one's after its last block and the right one's at the end;
    /// one column, one at the end.
    private void AddFeet(List<Section> shown)
    {
        var hidden = Home.Hidden();
        if (!HomeWide())
        {
            shown.Add(Foot(null, hidden));
            return;
        }
        shown.Insert(shown.FindLastIndex(s => !s.HomeRight) + 1, Foot(false, hidden));
        shown.Add(Foot(true, hidden));
    }

    /// A column's foot (`right`; null: the one column): its tile, its cards when open, or nothing (the tile that says
    /// nothing is hidden, StartFoot).
    private Section Foot(bool? right, List<HomeWidget> hidden)
    {
        bool col = right ?? true, open = addOpen == col;
        var foot = new Section(null) { HomeFoot = true, HomeRight = col };
        if (hidden.Count == 0) return foot;
        foot.Items.Add(new ResultItem
        {
            Id = "home:tile:" + (col ? "right" : "left"),
            Kind = ItemKind.Command,
            Title = Tr("블록 추가", "Add a block"),
            Subtitle = open ? Tr("카드를 누르면 이 열 끝에, 끌면 놓은 자리에 추가", "Click a card to add it here, or drag it into place")
                : hidden.Count == 1 ? Tr("숨긴 블록 1개", "1 hidden block") : Tr($"숨긴 블록 {hidden.Count}개", $"{hidden.Count} hidden blocks"),
            Icon = IconSpec.Of(G.Add),
            PrimaryLabel = open ? Tr("목록 닫기", "Close list") : Tr("목록 열기", "Open list"),
            StayOpen = true,
            Data = new HomeAdd(null, right, open),
            Run = () => ToggleAdd(col),
        });
        if (!open) return foot;
        foreach (var w in hidden)
        {
            var kind = Home.Kinds.First(k => k.key == w.Key);
            string key = w.Key;
            foot.Items.Add(new ResultItem
            {
                Id = "home:card:" + key,
                Kind = ItemKind.Command,
                Title = kind.name,
                Subtitle = kind.about,
                Icon = IconSpec.Of(kind.glyph),
                PrimaryLabel = Tr("추가", "Add"),
                StayOpen = true,
                Data = new HomeAdd(key, right, true),
                Run = () => AddBlock(key, right, null),
            });
        }
        return foot;
    }

    /// A foot's box at its column's end; its rows go in (the tile, the cards). Nothing hidden: the tile that says so.
    private StackPanel StartFoot(Section s, out UIElement? holder)
    {
        var box = new StackPanel();
        HomeColumns.SetRight(box, s.HomeRight);
        HomeColumns.SetFoot(box, true);
        if (homeGrid.Parent is null) list.Children.Add(homeGrid);
        homeGrid.Children.Add(box);
        holder = box;
        if (s.Items.Count == 0) box.Children.Add(HomeViews.NoneHidden());
        return box;
    }

    /// A tile or a card as a row (AddRow): a card can be dragged in too.
    private Row FootRow(ResultItem item, HomeAdd add)
    {
        if (add.Key is null) return Row.AddTile(item, add.Open);
        var card = Row.Card(item);
        card.ToolTip = Tr("누르면 이 열 끝에 추가 · 끌어서 원하는 자리에 놓기", "Click to add at this column's end · drag it into place");
        WireCard(card, add.Key);
        return card;
    }

    /// The tile pressed: its cards open (the first one selected, in view) or close.
    private void ToggleAdd(bool col)
    {
        addOpen = addOpen == col ? null : col;
        if (addOpen is null) Refresh(true);
        else ShowCard("home:card:" + Home.Hidden()[0].Key);
    }

    /// The home drawn again with card `id` selected, its foot (the tile and the cards) brought into view once its last
    /// rows are in (RenderSlices' Background work) and laid out.
    private void ShowCard(string id)
    {
        Refresh(true, id);
        Dispatcher.BeginInvoke(() =>
        {
            if (sel >= 0 && sel < rows.Count && VisualTreeHelper.GetParent(rows[sel]) is FrameworkElement foot) foot.BringIntoView();
        }, DispatcherPriority.ContextIdle);
    }

    /// The home drawn again with row `id` selected, brought into view once laid out (the 명령 block's “+ 명령 추가” and
    /// its cards, App\Engine\Engine.CommandsBlock.cs).
    public void RefreshTo(string id)
    {
        Refresh(true, id);
        Dispatcher.BeginInvoke(() =>
        {
            if (sel >= 0 && sel < rows.Count && rows[sel].Item?.Id == id) rows[sel].BringIntoView();
        }, DispatcherPriority.ContextIdle);
    }

    /// QA: the right column's tile pressed (one column: the only one).
    public void ToggleAddForQa() => ToggleAdd(true);

    /// A hidden block on again: at its column's end (`right`; a card pressed, or Enter on it), with the next card selected
    /// in view, or where its card was let go (`at`), the view kept there. The cards stay open while more are hidden.
    private void AddBlock(string key, bool? right, (string? above, string? below, bool? right)? at)
    {
        int i = Home.Hidden().FindIndex(w => w.Key == key);
        if (at is { } t) Home.AddAt(key, t.above, t.below, t.right);
        else Home.Add(key, right);
        ShowToast(Tr($"‘{Home.Name(key)}’ 블록 추가 완료", $"Added ‘{Home.Name(key)}’"));
        var hidden = Home.Hidden();
        if (hidden.Count == 0) addOpen = null;
        if (hidden.Count == 0 || at is not null) Refresh(true);
        else ShowCard("home:card:" + hidden[Math.Clamp(i, 0, hidden.Count - 1)].Key);
    }
}
