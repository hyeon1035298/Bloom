namespace Bloom.Ai;

/// One model's API price in USD per 1M tokens, for the five kinds TokenCounts keeps (CacheWrite: Claude's 5-minute and
/// Codex's writes; CacheWriteLong: Claude's 1-hour writes). Fast: the price of a call made in fast mode; Long: the price
/// of a call whose prompt is over LongAbove tokens; UsMarkup: a call kept in the US (inference_geo "us") costs 1.1 times.
internal sealed record TokenPrice(double Input, double CacheWrite, double CacheWriteLong, double CacheRead, double Output)
{
    public TokenPrice? Fast { get; init; }
    public long? LongAbove { get; init; }
    public TokenPrice? Long { get; init; }
    public bool UsMarkup { get; init; }

    /// What `counts` cost, made as `variant` says: fast mode's price over the long prompt's, the US markup on top.
    public double Cost(TokenCounts counts, TokenVariant variant)
    {
        var p = variant.HasFlag(TokenVariant.Fast) && Fast is { } fast ? fast
            : variant.HasFlag(TokenVariant.Long) && Long is { } longer ? longer : this;
        double usd = (counts.Input * p.Input + counts.CacheWrite * p.CacheWrite + counts.CacheWriteLong * p.CacheWriteLong
            + counts.CacheRead * p.CacheRead + counts.Output * p.Output) / 1_000_000;
        return variant.HasFlag(TokenVariant.Us) && UsMarkup ? usd * 1.1 : usd;
    }
}
