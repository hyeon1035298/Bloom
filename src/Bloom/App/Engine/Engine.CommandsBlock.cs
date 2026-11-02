using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home's 명령 block: the commands Core\HomeCommands picks, those already on the home (a favorite, a recent one)
/// left out. While 홈 편집 is on each row has a ✕ (and Ctrl+K “명령 블록에서 빼기”) that takes it off, and
/// “+ 명령 추가” at the block's end opens cards of the commands left off; one pressed goes back in Bloom's order.
internal sealed partial class Engine
{
    private const string CommandTileId = "home:cmd:add", CommandCardId = "home:cmd:card:";

    private bool commandCards; // 홈 편집: “+ 명령 추가”'s cards show

    private Section CommandsBlock(HashSet<string> shown)
    {
        bool editing = host.Window.HomeEditing;
        if (!editing) commandCards = false;
        var cmds = new Section(Tr("명령", "Command"));
        foreach (var e in HomeCommands.Shown())
        {
            if (shown.Contains(e.Id)) continue;
            var item = FromSys(e);
            if (editing) Removable(item);
            cmds.Items.Add(item);
        }
        if (!editing) return cmds;
        var missing = HomeCommands.Missing();
        bool open = commandCards && missing.Count > 0;
        cmds.Items.Add(new ResultItem
        {
            Id = CommandTileId,
            Kind = ItemKind.Command,
            Title = Tr("명령 추가", "Add a command"),
            Subtitle = missing.Count == 0 ? Tr("모든 명령이 이 블록에 있음", "Every command is in this block")
                : open ? Tr("카드를 누르면 이 블록에 추가", "Click a card to add it to this block")
                : missing.Count == 1 ? Tr("뺀 명령 1개", "1 command left out") : Tr($"뺀 명령 {missing.Count}개", $"{missing.Count} commands left out"),
            Icon = IconSpec.Of(G.Add),
            PrimaryLabel = open ? Tr("목록 닫기", "Close list") : Tr("목록 열기", "Open list"),
            StayOpen = true,
            Data = new HomeCommands.Tile(open),
            Run = () =>
            {
                commandCards = !open && missing.Count > 0;
                host.Window.RefreshTo(commandCards ? CommandCardId + missing[0].Id : CommandTileId);
            },
        });
        if (!open) return cmds;
        for (int i = 0; i < missing.Count; i++)
        {
            var e = missing[i];
            string? next = i + 1 < missing.Count ? CommandCardId + missing[i + 1].Id : i > 0 ? CommandCardId + missing[i - 1].Id : null;
            cmds.Items.Add(new ResultItem
            {
                Id = CommandCardId + e.Id,
                Kind = ItemKind.Command,
                Title = e.Title,
                Subtitle = e.Subtitle,
                Icon = IconSpec.Of(e.Glyph, e.Id == "cmd:ai"),
                PrimaryLabel = Tr("추가", "Add"),
                StayOpen = true,
                Data = new HomeCommands.Card(e.Id),
                Run = () =>
                {
                    HomeCommands.Set(e.Id, true);
                    if (next is null) commandCards = false;
                    host.Window.ShowToast(Tr($"‘{e.Title}’ 명령 추가 완료", $"Added ‘{e.Title}’"));
                    host.Window.RefreshTo(next ?? CommandTileId);
                },
            });
        }
        return cmds;
    }

    /// 홈 편집: a command row's ✕, and the same in its Ctrl+K menu.
    private void Removable(ResultItem item)
    {
        string id = item.Id, title = item.Title;
        void Remove()
        {
            HomeCommands.Set(id, false);
            host.Window.ShowToast(Tr($"‘{title}’ 명령 뺌 · ‘+ 명령 추가’로 다시 넣기", $"‘{title}’ left out · ‘+ Add a command’ puts it back"));
            host.Window.Refresh(true);
        }
        item.Remove = Remove;
        var more = item.MoreActions;
        item.MoreActions = () =>
        {
            var list = more?.Invoke() ?? [];
            list.Add(new ActionItem { Title = Tr("명령 블록에서 빼기", "Remove from the Commands block"), Glyph = G.Close, StayOpen = true, Run = Remove });
            return list;
        };
    }
}
