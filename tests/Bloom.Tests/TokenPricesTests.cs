using System;
using Bloom.Ai;

namespace Bloom.Tests;

/// The token blocks' prices (Ai\TokenPrices, Ai\TokenPrice): names as the logs write them, built-in rows by exact name
/// only, the user's rows and prefixes over them, the long-prompt, fast and US prices (the effort bits beside them change
/// none), and a check against a dashboard's sums.
internal static class TokenPricesTests
{
    private const long M = 1_000_000;

    public static void NamesLoseTheirTails()
    {
        Check.Eq(TokenPrices.Normalize("Claude-Haiku-4-5-20251001"), "claude-haiku-4-5", "the date tail, in lower case");
        Check.Eq(TokenPrices.Normalize("claude-opus-4-6[1m]"), "claude-opus-4-6", "a [1m] tail");
        Check.Eq(TokenPrices.Normalize("gpt-6.1-sol"), "gpt-6.1-sol", "nothing to take off");
        Check.Eq(TokenPrices.WithoutDate("claude-Opus-5-5-20261101"), "claude-Opus-5-5", "the date only, case kept");
        Check.Eq(TokenPrices.WithoutDate("model-2026110"), "model-2026110", "seven digits are no date");
    }

    public static void BuiltInRowsFitExactNamesOnly()
    {
        Check.True(TokenPrices.Of("claude-opus-5-5-20261101") is { Input: 4, Output: 20 }, "a dated name");
        Check.True(TokenPrices.Of("claude-haiku-4-5-20251001") is { Input: 1 }, "Haiku 4.5");
        Check.True(TokenPrices.Of("claude-opus-5-7") is null, "a newer model gets no older one's price");
        Check.True(TokenPrices.Of("claude-opus-5-5-fast") is null, "a longer name neither");
        Check.True(TokenPrices.Of("codex-auto-review") is null && TokenPrices.Of("chatgpt-web/gpt-6") is null, "names with no API price");
        Check.Eq(TokenPrices.Of("claude-sonnet-5-5")!.CacheRead, 0.10, "Sonnet 5.5's cache read as the pricing page says");
    }

    public static void TheUsersRowsComeFirst()
    {
        try
        {
            TokenPrices.Use("""
                {"version":1,"models":{
                  "chatgpt-web/*":{},
                  "codex-auto-*":{"input":1,"output":2},
                  "codex-auto-review-*":{"input":3},
                  "claude-opus-5-5":{"output":30},
                  "Claude-Opus-5-7-20270101":{"input":5,"cacheRead":0.5,"output":25},
                  "gpt-6.1-sol":{"input":0,"cacheWrite":0,"cacheWriteLong":0,"cacheRead":0,"output":0},
                }}
                """);
            var free = TokenPrices.Of("chatgpt-web/gpt-6") ?? throw new InvalidOperationException("no price");
            Check.Eq(free.Cost(new TokenCounts(1, M, M, M, M, M), TokenVariant.None), 0.0, "a prefix row of nothing: free");
            Check.True(TokenPrices.Of("codex-auto-review") is { Input: 1, Output: 2 }, "a prefix row");
            Check.True(TokenPrices.Of("codex-auto-review-2") is { Input: 3, Output: 0 }, "the longest prefix first");
            var opus = TokenPrices.Of("claude-opus-5-5")!;
            Check.True(opus is { Input: 4, Output: 30, UsMarkup: true } && opus.Fast is not null, "a field given replaces, the rest of the built-in row stays");
            Check.True(TokenPrices.Of("claude-opus-5-7") is { Input: 5, CacheRead: 0.5, Output: 25 }, "a model the table lacks, by its name without the date");
            Check.True(TokenPrices.Of("gpt-6.1-sol") is { Input: 0, Output: 0 }, "a row at 0");
            TokenPrices.Use("{ broken");
            Check.True(TokenPrices.Of("claude-opus-5-7") is null, "a broken file: the built-in table");
        }
        finally
        {
            TokenPrices.Use(null);
        }
    }

    public static void LongFastAndUsPricesApply()
    {
        Check.True(!TokenPrices.IsLong("claude-haiku-5-5", new TokenCounts(1, 100_000, 9 * M, 0, 0, 0)), "Haiku 5.5 at 100K: not over");
        Check.True(TokenPrices.IsLong("claude-haiku-5-5", new TokenCounts(1, 1, 0, 90_000, 9_000, 1_000)), "over 100K with the cache's tokens");
        var haiku = TokenPrices.Of("claude-haiku-5-5")!;
        Check.Near(haiku.Cost(new TokenCounts(1, M, M, 0, 0, 0), TokenVariant.None), 0.60, 1e-9, "Haiku 5.5 up to 100K");
        Check.Near(haiku.Cost(new TokenCounts(1, M, M, 0, 0, 0), TokenVariant.Long), 3.00, 1e-9, "over it");
        Check.True(!TokenPrices.IsLong("claude-opus-5-5", new TokenCounts(1, 900_000, 0, 0, 0, 0)), "Opus has one price for any length");

        Check.True(TokenPrices.IsLong("gpt-6.1-sol", new TokenCounts(1, 2_000, 0, 270_001, 0, 0)), "OpenAI over 272K, cached tokens included");
        var sol = TokenPrices.Of("gpt-6.1-sol")!;
        Check.Near(sol.Cost(new TokenCounts(1, M, M, M, M, 0), TokenVariant.Long), 4 + 15 + 0.20 + 5, 1e-9, "input and cache 2×, output 1.5×");
        Check.Near(sol.Cost(new TokenCounts(1, M, M, M, M, 0), TokenVariant.None), 2 + 10 + 0.10 + 2.50, 1e-9, "under it");

        var opus = TokenPrices.Of("claude-opus-5-5")!;
        Check.Near(opus.Cost(new TokenCounts(1, M, M, M, M, M), TokenVariant.Fast), 8 + 40 + 0.40 + 10 + 16, 1e-9, "fast mode, cache multipliers on top");
        Check.Near(opus.Cost(new TokenCounts(1, M, M, M, M, M), TokenVariant.None), 4 + 20 + 0.20 + 5 + 8, 1e-9, "standard");
        Check.Near(opus.Cost(new TokenCounts(1, M, 0, 0, 0, 0), TokenVariant.Fast | TokenVariant.Us), 8.8, 1e-9, "fast in the US");
        Check.Near(TokenPrices.Of("claude-sonnet-5-5")!.Cost(new TokenCounts(1, M, 0, 0, 0, 0), TokenVariant.Us), 2.2, 1e-9, "1.1× in the US");
        Check.Near(TokenPrices.Of("claude-haiku-4-5")!.Cost(new TokenCounts(1, M, 0, 0, 0, 0), TokenVariant.Us), 1.0, 1e-9, "not before 4.6");
        Check.Near(TokenPrices.Of("claude-sonnet-5-5")!.Cost(new TokenCounts(1, M, 0, 0, 0, 0), TokenVariant.Fast), 2.0, 1e-9, "no fast price: the standard one");
    }

    public static void EffortLeavesThePriceAsItIs()
    {
        var counts = new TokenCounts(1, M, M, M, M, M);
        var opus = TokenPrices.Of("claude-opus-5-5")!;
        var haiku = TokenPrices.Of("claude-haiku-5-5")!;
        foreach (var effort in Enum.GetValues<TokenEffort>())
            foreach (var v in new[] { TokenVariant.None, TokenVariant.Fast, TokenVariant.Us, TokenVariant.Long, TokenVariant.Fast | TokenVariant.Us | TokenVariant.Long })
            {
                var with = v.WithEffort(effort);
                Check.Eq(with.Effort(), effort, $"{effort} kept beside {v}");
                Check.Eq(with.WithEffort(TokenEffort.Unknown), v, $"and taken off again ({v})");
                Check.Near(opus.Cost(counts, with), opus.Cost(counts, v), 1e-9, $"Opus at {effort}, {v}");
                Check.Near(haiku.Cost(counts, with), haiku.Cost(counts, v), 1e-9, $"Haiku at {effort}, {v}");
            }
        Check.Eq(TokenVariant.Fast.WithEffort(TokenEffort.Max).WithEffort(TokenEffort.Low).Effort(), TokenEffort.Low, "a new effort takes the old one's place");
    }

    public static void ADashboardsSumsAgree()
    {
        // a dashboard's input, output and cache reads (2026-11-02): Opus 5.5 $1,109.92, Sonnet 5.5 $49.26
        var opus = new TokenCounts(1, 52_569_000, 19_182_000, 2_580_000_000, 0, 0);
        Check.Near(TokenPrices.Of("claude-opus-5-5")!.Cost(opus, TokenVariant.None), 1109.92, 0.005, "Opus 5.5");
        var sonnet = new TokenCounts(1, 4_339_000, 1_858_000, 220_000_000, 0, 0);
        Check.Near(TokenPrices.Of("claude-sonnet-5-5")!.Cost(sonnet, TokenVariant.None), 49.26, 0.005, "Sonnet 5.5");
    }
}
