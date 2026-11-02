using Bloom.Core;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.App;

/// The home's 플레이어 block: the song playing now (Bloom's own, or another app's while Bloom plays none) with previous,
/// play and next (HomePlayer).
internal sealed partial class Engine
{
    private Section PlayerBlock(HomeWidget w)
    {
        HomePlayer.Sync(); // just turned on (홈 편집), or unfolded: watched from now
        var now = HomePlayer.Now();
        System.Action<PlayerOp>? press = HomePlayer.Live ? op => NowPlaying.Press(now.Other, op) : null;
        void Open() => host.Tools.Open("player");
        return new Section(Home.Name(w.Key)) { Widget = () => Ui.PlayerViews.Block(now, Open, press) };
    }
}
