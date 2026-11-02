using System;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The token blocks of the home (토큰 사용량, 토큰 그래프, 모델별 사용량): one snapshot each from TokenStats, for the span
/// the block keeps (HomeWidget.Period).
internal sealed partial class Engine
{
    private Section TokenBlock(HomeWidget w)
    {
        if (!w.Collapsed) TokenStats.Look();
        var view = TokenStats.Query(PeriodOf(w.Period));
        string? note = TokenStats.Indexing is { } done ? Tr($"세는 중 {Percent(done)}%", $"Counting {Percent(done)}%")
            : TokenStats.Counted is { } counted ? Ago(counted) : null;
        string key = w.Key;
        void Pick(TokenPeriod to)
        {
            Home.SetPeriod(key, to.ToString());
            host.Window.Refresh(true);
        }
        bool none = TokenStats.NoLogs;
        bool counting = TokenStats.Indexing is not null || TokenStats.Counted is null; // the first count is not done
        int rows = Home.Count(w);
        Func<System.Windows.FrameworkElement> widget = key switch
        {
            "tokenchart" => () => Ui.TokenViews.Chart(view, none, counting, Pick),
            "tokenmodels" => () => Ui.TokenViews.Models(view, none, counting, rows, Pick),
            _ => () => Ui.TokenViews.Summary(view, none, counting, Pick),
        };
        return new Section(Home.Name(key), note) { Widget = widget, Reload = TokenStats.Refresh };
    }

    /// A block's kept span (HomeWidget.Period) by its name; anything else (none yet, a number, an older name) is the
    /// last 24 hours.
    internal static TokenPeriod PeriodOf(string? stored) =>
        Enum.TryParse<TokenPeriod>(stored, out var p) && Enum.IsDefined(p) && p.ToString() == stored ? p : TokenPeriod.Day;

    /// 0…1 as a whole percent, 99 at most until it is done (100% while still counting reads as finished).
    private static int Percent(double done) => Math.Min(99, (int)Math.Floor(done * 100));
}
