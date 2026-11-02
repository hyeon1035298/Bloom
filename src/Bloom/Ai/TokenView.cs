using System;
using System.Collections.Generic;

namespace Bloom.Ai;

/// What a block shows for one period. From and To in local time. Cost: priced models only; Unpriced and UnpricedTokens:
/// the models with no price and their tokens (in Sum, not in Cost). Models: every model, most tokens first. Bars: oldest
/// first, every bar of the period, zeros too. Sum.Calls 0 = nothing counted in the period (the block shows its empty line).
/// Sessions + NoSession = Sum.
internal sealed record TokenView(TokenPeriod Period, DateTime From, DateTime To, TokenCounts Sum, double Cost, int Unpriced, long UnpricedTokens,
    IReadOnlyList<TokenModel> Models, TokenUnit Unit, IReadOnlyList<TokenBar> Bars)
{
    /// Every session of the period, most tokens first (the same tokens: by Id); [] when none.
    public IReadOnlyList<TokenSession> Sessions { get; init; } = [];

    /// What no session holds: sums folded into days (past TokenIndex.HourDays) and calls whose line named no session.
    public TokenCounts NoSession { get; init; }

    /// Nothing counted: no models, no bars.
    public static TokenView Empty(TokenPeriod period)
    {
        var now = DateTime.Now;
        return new(period, now, now, default, 0, 0, 0, [], TokenUnit.Hour, []);
    }
}
