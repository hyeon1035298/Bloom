namespace Bloom.Ai;

/// Token sums, both CLIs' meanings folded into one. Input: the prompt neither read from nor written to the cache
/// (Claude's input_tokens; Codex's input − cached − cache_write). CacheWrite: Claude's 5-minute writes and Codex's
/// writes; CacheWriteLong: Claude's 1-hour writes. Output includes thinking / reasoning.
internal readonly record struct TokenCounts(long Calls, long Input, long Output, long CacheRead, long CacheWrite, long CacheWriteLong)
{
    public long Prompt => Input + CacheRead + CacheWrite + CacheWriteLong;
    public long Total => Prompt + Output;

    /// CacheRead / Prompt; 0 when nothing was sent.
    public double HitRate => Prompt == 0 ? 0 : (double)CacheRead / Prompt;

    public static TokenCounts operator +(TokenCounts a, TokenCounts b) =>
        new(a.Calls + b.Calls, a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite, a.CacheWriteLong + b.CacheWriteLong);
}
