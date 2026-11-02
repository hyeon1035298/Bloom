using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bloom.Core;

namespace Bloom.Ai;

/// API prices per model (USD per 1M tokens): a built-in table checked on Checked, under the user's own prices.json.
/// A model is looked up by its name in lower case, with a "[1m]"-like tail and a -YYYYMMDD date tail taken off
/// (Normalize). A built-in row is used only for that exact name (a new model never silently gets an older one's price);
/// a row of the user's ending in '*' covers every name starting with what is before it. A model with no row has no price:
/// its tokens and calls are counted, its cost is not guessed. A cost is worked out when shown, so a new price needs no
/// log read again.
///
/// prices.json: {"version":1,"models":{"<name or prefix*>":{"input":..,"cacheWrite":..,"cacheWriteLong":..,"cacheRead":..,
/// "output":..,"fast":{..},"longAbove":..,"long":{..},"usMarkup":true}}}. A field left out keeps the built-in row's of the
/// same name, else is 0 (free: "chatgpt-web/*" at 0 makes what a subscription paid for cost $0).
internal static class TokenPrices
{
    /// When the built-in table was checked against the providers' pages.
    public const string Checked = "2026-11-02";

    /// Prompts longer than this (input_tokens) cost more on OpenAI's newer models: input and cache 2×, output 1.5×.
    private const long OpenAiLongAbove = 272_000;

    /// Anthropic: https://platform.claude.com/docs/en/about-claude/pricing (input, 5-minute write, 1-hour write, cache
    /// read, output; fast mode and the 1.1× US markup from the same page, cache multipliers on top of fast mode's price).
    /// OpenAI: https://developers.openai.com/api/docs/pricing (input, 30-minute write, 12-hour write, cache read, output;
    /// models with no cache-write price pay their input price). Both checked on Checked.
    private static readonly Dictionary<string, TokenPrice> BuiltIn = new(StringComparer.Ordinal)
    {
        ["claude-fable-5-1"] = Us(10, 12.50, 20, 0.25, 50),
        ["claude-mythos-5-1"] = Us(10, 12.50, 20, 0.25, 50),
        ["claude-fable-5"] = Us(10, 12.50, 20, 1, 50),
        ["claude-mythos-5"] = Us(10, 12.50, 20, 1, 50),
        ["claude-opus-5-5"] = Us(4, 5, 8, 0.20, 20) with { Fast = new(8, 10, 16, 0.40, 40) },
        ["claude-opus-5"] = Us(5, 6.25, 10, 0.50, 25) with { Fast = new(10, 12.50, 20, 1, 50) },
        ["claude-opus-4-8"] = Us(5, 6.25, 10, 0.50, 25) with { Fast = new(10, 12.50, 20, 1, 50) },
        ["claude-opus-4-7"] = Us(5, 6.25, 10, 0.50, 25),
        ["claude-opus-4-6"] = Us(5, 6.25, 10, 0.50, 25),
        ["claude-opus-4-5"] = new(5, 6.25, 10, 0.50, 25),
        ["claude-opus-4-1"] = new(15, 18.75, 30, 1.50, 75),
        ["claude-opus-4"] = new(15, 18.75, 30, 1.50, 75),
        ["claude-opus-4-0"] = new(15, 18.75, 30, 1.50, 75),
        ["claude-sonnet-5-5"] = Us(2, 2.50, 4, 0.10, 10),
        ["claude-sonnet-5"] = Us(2, 2.50, 4, 0.20, 10),
        ["claude-sonnet-4-6"] = Us(3, 3.75, 6, 0.30, 15),
        ["claude-sonnet-4-5"] = new(3, 3.75, 6, 0.30, 15),
        ["claude-sonnet-4"] = new(3, 3.75, 6, 0.30, 15),
        ["claude-sonnet-4-0"] = new(3, 3.75, 6, 0.30, 15),
        ["claude-haiku-5-5"] = Us(0.10, 0.125, 0.20, 0.01, 0.50) with { LongAbove = 100_000, Long = new(0.50, 0.625, 1, 0.05, 2.50) },
        ["claude-haiku-4-5"] = new(1, 1.25, 2, 0.10, 5),
        ["claude-3-5-haiku"] = new(0.80, 1, 1.60, 0.08, 4),

        ["gpt-6-astra"] = OpenAi(10, 12.50, 25, 1, 50, longer: true),
        ["gpt-6.1-sol"] = OpenAi(2, 2.50, 5, 0.10, 10, longer: true),
        ["gpt-6-sol"] = OpenAi(2, 2.50, 5, 0.20, 10, longer: true),
        ["gpt-6.1-terra"] = OpenAi(1, 1.25, 2.50, 0.05, 5, longer: true),
        ["gpt-6-luna"] = OpenAi(0.10, 0.125, 0.25, 0.01, 0.50, longer: true),
        ["gpt-5.6-sol"] = OpenAi(4, 5, 10, 0.40, 20, longer: true),
        ["gpt-5.6-terra"] = OpenAi(2, 2.50, 5, 0.20, 12, longer: true),
        ["gpt-5.6-luna"] = OpenAi(0.20, 0.25, 0.50, 0.02, 1.20, longer: true),
        ["gpt-5.5"] = OpenAi(5, 5, 5, 0.50, 30, longer: true),
        ["gpt-5.4"] = OpenAi(2.50, 2.50, 2.50, 0.25, 15, longer: true),
        ["gpt-5.3-codex"] = OpenAi(1.75, 1.75, 1.75, 0.175, 14, longer: false),
        ["gpt-5.2"] = OpenAi(1.75, 1.75, 1.75, 0.175, 14, longer: false),
        ["gpt-5.1"] = OpenAi(1.25, 1.25, 1.25, 0.125, 10, longer: false),
        ["gpt-5"] = OpenAi(1.25, 1.25, 1.25, 0.125, 10, longer: false),
    };

    private static readonly TokenPrice Zero = new(0, 0, 0, 0, 0);

    private static volatile Rows user = Rows.None;
    private static long userStamp = -1;

    /// The user's rows: by exact name, and by prefix (longest first).
    private sealed record Rows(Dictionary<string, TokenPrice> Exact, List<(string Prefix, TokenPrice Price)> Prefixes)
    {
        public static readonly Rows None = new(new Dictionary<string, TokenPrice>(), []);
    }

    /// The user's own prices over the built-in ones: prices.json in the data folder.
    public static string OverridePath => Paths.In("prices.json");

    /// Makes OverridePath as {"version":1,"models":{}} when it is missing (설정 opens it next).
    public static void EnsureOverride()
    {
        if (!File.Exists(OverridePath)) File.WriteAllText(OverridePath, "{\"version\":1,\"models\":{}}\n");
    }

    /// The price of `model` (as a log names it); null when none is known.
    public static TokenPrice? Of(string model)
    {
        string name = Normalize(model);
        var rows = user;
        if (rows.Exact.TryGetValue(name, out var price)) return price;
        foreach (var (prefix, p) in rows.Prefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return p;
        return BuiltIn.GetValueOrDefault(name);
    }

    /// The call is over its model's LongAbove (judged when it is counted: a new LongAbove holds for the calls after it).
    public static bool IsLong(string model, TokenCounts counts) => Of(model)?.LongAbove is { } above && counts.Prompt > above;

    /// The name prices are looked up by: lower case, without a "[...]" tail or a -YYYYMMDD date tail.
    public static string Normalize(string model)
    {
        string m = model.Trim().ToLowerInvariant();
        int bracket = m.IndexOf('[', StringComparison.Ordinal);
        if (bracket >= 0) m = m[..bracket].TrimEnd();
        return WithoutDate(m);
    }

    /// The name without a -YYYYMMDD tail ("claude-haiku-4-5-20251001" → "claude-haiku-4-5"); its case kept.
    public static string WithoutDate(string model) =>
        model.Length > 9 && model[^9] == '-' && model.AsSpan(model.Length - 8).IndexOfAnyExceptInRange('0', '9') < 0 ? model[..^9] : model;

    /// Reads prices.json again when it changed since the last look (at the start of each pass) → whether the prices did.
    public static bool Reload()
    {
        long stamp = 0;
        try
        {
            var file = new FileInfo(OverridePath);
            if (file.Exists) stamp = file.LastWriteTimeUtc.Ticks ^ file.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (stamp == userStamp) return false;
        userStamp = stamp;
        string? json = null;
        if (stamp != 0)
            try
            {
                json = File.ReadAllText(OverridePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("token prices", e);
            }
        var before = user;
        Use(json);
        return !(before.Exact.Count == 0 && before.Prefixes.Count == 0 && user.Exact.Count == 0 && user.Prefixes.Count == 0);
    }

    /// The user's rows from prices.json's text (null or broken: none; a broken file is logged).
    internal static void Use(string? json)
    {
        if (json is null)
        {
            user = Rows.None;
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var exact = new Dictionary<string, TokenPrice>(StringComparer.Ordinal);
            var prefixes = new List<(string, TokenPrice)>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Object)
                foreach (var row in models.EnumerateObject())
                {
                    if (row.Value.ValueKind != JsonValueKind.Object) continue;
                    string name = row.Name.Trim().ToLowerInvariant();
                    if (name.EndsWith('*')) prefixes.Add((name[..^1], Row(row.Value, Zero)));
                    else
                    {
                        name = Normalize(name);
                        exact[name] = Row(row.Value, BuiltIn.GetValueOrDefault(name) ?? Zero);
                    }
                }
            user = new Rows(exact, prefixes.OrderByDescending(p => p.Item1.Length).ToList());
        }
        catch (JsonException e)
        {
            Log.Error("token prices: prices.json not read", e);
            user = Rows.None;
        }
    }

    /// One row of prices.json over `under` (what a field left out keeps).
    private static TokenPrice Row(JsonElement e, TokenPrice under) =>
        new(Number(e, "input", under.Input), Number(e, "cacheWrite", under.CacheWrite), Number(e, "cacheWriteLong", under.CacheWriteLong), Number(e, "cacheRead", under.CacheRead), Number(e, "output", under.Output))
        {
            Fast = e.TryGetProperty("fast", out var fast) && fast.ValueKind == JsonValueKind.Object ? Row(fast, under.Fast ?? Zero) : under.Fast,
            LongAbove = e.TryGetProperty("longAbove", out var above) && above.ValueKind == JsonValueKind.Number && above.TryGetInt64(out long tokens) ? tokens : under.LongAbove,
            Long = e.TryGetProperty("long", out var longer) && longer.ValueKind == JsonValueKind.Object ? Row(longer, under.Long ?? Zero) : under.Long,
            UsMarkup = e.TryGetProperty("usMarkup", out var us) && us.ValueKind is JsonValueKind.True or JsonValueKind.False ? us.GetBoolean() : under.UsMarkup,
        };

    private static double Number(JsonElement e, string name, double under) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && d >= 0 ? d : under;

    /// An Anthropic model of 4.6 or later: US-only inference costs 1.1×.
    private static TokenPrice Us(double input, double write5m, double write1h, double read, double output) => new(input, write5m, write1h, read, output) { UsMarkup = true };

    /// An OpenAI model; `longer`: a prompt over OpenAiLongAbove pays 2× for input and cache, 1.5× for output.
    private static TokenPrice OpenAi(double input, double write, double writeLong, double read, double output, bool longer) =>
        new(input, write, writeLong, read, output)
        {
            LongAbove = longer ? OpenAiLongAbove : null,
            Long = longer ? new(input * 2, write * 2, writeLong * 2, read * 2, output * 1.5) : null,
        };
}
