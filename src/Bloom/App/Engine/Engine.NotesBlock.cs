using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home's 메모 block: the latest notes (Core\Notes.Ordered) as rows, a click opening one in 메모, and a row for a
/// new note.
internal sealed partial class Engine
{
    private Section NotesBlock(HomeWidget w) => new(Home.Name(w.Key));
}
