using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The home's AI 사용량 cards for API keys whose provider tells a balance (ApiProvider.Balance: OpenRouter, DeepSeek),
/// asked with the other cards (AiUsage's pass, on a worker thread) and kept in memory only. A provider without a key has
/// no card; one whose look failed keeps its last card (the card shows how old it is).
internal static class ApiBalances
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, UsageAccount> known = new();
    private static readonly HashSet<string> keyOnly = new(); // OpenRouter refused /credits (not a management key): /key from then on
    private static readonly Dictionary<string, long> generations = new(); // per provider: moves on when its key is removed or replaced (Forget)

    /// Some provider would have a card (a balance endpoint and a key stored); the usage block shows then too.
    public static bool Wanted => ApiProviders.All.Any(p => p.Balance != ApiBalanceWay.None && ApiKeys.Has(p.Id));

    /// The cards, in the providers' order.
    public static List<UsageAccount> Cards
    {
        get
        {
            lock (Gate) return ApiProviders.All.Select(p => known.GetValueOrDefault(p.Id)).OfType<UsageAccount>().ToList();
        }
    }

    /// One look at every provider with a balance and a key, on a worker thread (it opens the keys); → any answered.
    public static async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        bool any = false;
        foreach (var p in ApiProviders.All.Where(p => p.Balance != ApiBalanceWay.None))
        {
            // the provider's generation when this look began: an answer for a key that was removed or replaced since
            // (Forget moved it on) is dropped, so a removed key's card never comes back and a new key's is not overwritten
            long at = Generation(p.Id);
            if (ApiKeys.Get(p.Id) is not { Length: > 0 } key)
            {
                lock (Gate)
                    if (Generation(p.Id) == at) known.Remove(p.Id);
                continue;
            }
            if (await AskAsync(p, key, ct, at).ConfigureAwait(false) is not { } balance) continue;
            if (!Keep(p, at, balance)) continue;
            any = true;
        }
        return any;
    }

    private static long Generation(string provider)
    {
        lock (Gate) return generations.GetValueOrDefault(provider);
    }

    /// Takes a look's answer in as the provider's card, unless its key changed since the look began (`at`).
    internal static bool Keep(ApiProvider p, long at, UsageBalance balance)
    {
        lock (Gate)
        {
            if (Generation(p.Id) != at) return false;
            known[p.Id] = Card(p, balance);
            return true;
        }
    }

    /// The provider's generation now: a look that began with it can still be kept (Keep).
    internal static long Begin(string provider) => Generation(provider);

    /// A provider's balance as its endpoint says; null when it could not be told. `at`: the generation the look began in,
    /// so that what it learns of the key (OpenRouter's management key) is not kept for another key.
    internal static async Task<UsageBalance?> AskAsync(ApiProvider p, string key, CancellationToken ct, long? at = null)
    {
        if (p.Balance == ApiBalanceWay.DeepSeek)
        {
            var (status, body) = await ApiClient.GetAsync(p, "user/balance", key, ct).ConfigureAwait(false);
            return status == 200 ? DeepSeek(body) : null;
        }
        if (p.Balance != ApiBalanceWay.OpenRouter) return null;
        bool tryCredits;
        lock (Gate) tryCredits = !keyOnly.Contains(p.Id);
        if (tryCredits)
        {
            // the account's whole balance, which only a management key may read; an ordinary key is refused
            var (status, body) = await ApiClient.GetAsync(p, "credits", key, ct).ConfigureAwait(false);
            if (status == 200 && OpenRouterCredits(body) is { } account) return account;
            if (status is 401 or 403)
                lock (Gate)
                    if (at is null || Generation(p.Id) == at) keyOnly.Add(p.Id);
        }
        var (keyStatus, keyBody) = await ApiClient.GetAsync(p, "key", key, ct).ConfigureAwait(false);
        return keyStatus == 200 ? OpenRouterKey(keyBody) : null;
    }

    internal static UsageAccount Card(ApiProvider p, UsageBalance balance) => new(p.Name, null, new(), DateTime.Now) { Id = "api:" + p.Id, Balance = balance };

    /// A key's provider changed (the key added, replaced or removed): its card goes until the next look.
    public static void Forget(string provider)
    {
        lock (Gate)
        {
            generations[provider] = generations.GetValueOrDefault(provider) + 1; // a look under way is for the old key
            known.Remove(provider);
            keyOnly.Remove(provider);
        }
    }

    /// OpenRouter's GET /credits: {"data":{"total_credits","total_usage"}} → what is left.
    internal static UsageBalance? OpenRouterCredits(string json) => Read(json, data =>
        Num(data, "total_credits") is { } credits && Num(data, "total_usage") is { } usage
            ? new UsageBalance(Tr("잔액", "Balance"), Money(credits - usage, "USD"), Tr("OpenRouter 계정의 남은 크레딧입니다.", "Remaining credits in your OpenRouter account."))
            : null);

    /// OpenRouter's GET /key: the key's own limit left (limit_remaining) when it has one, else what it has spent (usage).
    internal static UsageBalance? OpenRouterKey(string json) => Read(json, data =>
        Num(data, "limit_remaining") is { } left
            ? new UsageBalance(Tr("잔액", "Balance"), Money(left, "USD"), Tr("이 API 키의 사용 한도에서 남은 금액입니다.", "Remaining amount on this API key's usage limit."))
            : Num(data, "usage") is { } used
                ? new UsageBalance(Tr("사용", "Used"), Money(used, "USD"), Tr("이 API 키로 지금까지 사용한 금액입니다. 계정 잔액은 관리 키로만 조회할 수 있습니다.", "Total amount spent with this API key. Account balance can only be checked with a management key."))
                : null);

    /// DeepSeek's GET /user/balance: {"balance_infos":[{"currency","total_balance"}]} → each currency's total.
    internal static UsageBalance? DeepSeek(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array) return null;
            var amounts = new List<string>();
            foreach (var b in infos.EnumerateArray())
                if (decimal.TryParse(ApiJson.Str(b, "total_balance"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal total))
                    amounts.Add(Money(total, ApiJson.Str(b, "currency")));
            return amounts.Count == 0 ? null
                : new UsageBalance(Tr("잔액", "Balance"), string.Join(" · ", amounts), Tr("DeepSeek 계정의 잔액입니다 (충전액과 지급액 포함).", "DeepSeek account balance (including topped-up and granted balance)."));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsageBalance? Read(string json, Func<JsonElement, UsageBalance?> data)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? data(d) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out decimal n) ? n : null;

    /// "$12.34", "¥110.00", or the amount and its currency's code.
    internal static string Money(decimal amount, string currency)
    {
        string n = Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture); // as money is written: 12.345 → 12.35
        return currency switch
        {
            "USD" => "$" + n,
            "CNY" => "¥" + n,
            _ => n + " " + currency,
        };
    }
}
