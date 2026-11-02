using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home's 플레이어 block: the song playing now (Bloom's own, or another app's while Bloom plays none) with previous,
/// play and next (HomePlayer).
internal sealed partial class Engine
{
    private Section PlayerBlock(HomeWidget w) => new(Home.Name(w.Key));
}
