using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home's 메모 block: the latest notes (Core\Notes.Ordered) as rows, a click opening one in 메모, and a row for a
/// new note.
internal sealed partial class Engine
{
    /// As many notes as the block's rows (pinned first, then the newest), "고정 · 3분 전" under each, and 새 메모 last;
    /// with no notes only 새 메모, so a block switched on never goes. The header counts them all when some are left out.
    private Section NotesBlock(HomeWidget w)
    {
        var all = Notes.Ordered.ToList();
        int rows = Home.Count(w);
        var s = new Section(Home.Name(w.Key), all.Count > rows ? Tr($"{all.Count}개", $"{all.Count} notes") : null);
        foreach (var n in all.Take(rows))
            s.Items.Add(NoteItem(n, n.Pinned ? Tr("고정", "Pinned") + " · " + Ui.ToolUi.Ago(n.UpdatedAt) : Ui.ToolUi.Ago(n.UpdatedAt)));
        s.Items.Add(new ResultItem
        {
            Id = "note:add",
            Kind = ItemKind.Command,
            Title = Tr("새 메모", "New note"),
            Icon = IconSpec.Of(G.Add, true),
            PrimaryLabel = Tr("만들기", "Create"),
            StayOpen = true,
            Run = () => host.Tools.Open("notes", Notes.New().Id), // left empty, 메모 deletes it again
        });
        return s;
    }
}
