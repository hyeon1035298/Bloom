using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home's 메모 block: the latest notes (Core\Notes.Ordered's order) as rows, a click opening one in 메모, and a row
/// for a new note.
internal sealed partial class Engine
{
    /// As many notes as the block's rows (pinned first, then the newest), "고정 · 3분 전" under each, and 새 메모 last;
    /// with no notes only 새 메모, so a block switched on never goes. The header counts them all when some are left out.
    /// A blank note (새 메모's, still open in 메모) is neither a row nor counted.
    private Section NotesBlock(HomeWidget w)
    {
        var shown = Latest(Notes.Current.Items, Home.Count(w), out int all);
        var s = new Section(Home.Name(w.Key), all > shown.Count ? Tr($"{all}개", $"{all} notes") : null);
        foreach (var n in shown)
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

    /// The first `rows` of the notes with words in Notes.Ordered's order (pinned first, then the newest; a tie keeps the
    /// list's order), and how many have words: one pass with no copy of the list, as the home is drawn again every 300 ms
    /// while 메모 beside it is typed in.
    internal static List<Note> Latest(List<Note> notes, int rows, out int count)
    {
        var top = new List<Note>(rows + 1);
        count = 0;
        foreach (var n in notes)
        {
            if (string.IsNullOrWhiteSpace(n.Text)) continue;
            count++;
            int at = top.Count;
            while (at > 0 && Before(n, top[at - 1])) at--;
            if (at >= rows) continue;
            top.Insert(at, n);
            if (top.Count > rows) top.RemoveAt(rows);
        }
        return top;
    }

    private static bool Before(Note a, Note b) => a.Pinned != b.Pinned ? a.Pinned : a.Updated > b.Updated;
}
