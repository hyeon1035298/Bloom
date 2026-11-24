using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Search;

/// USD-based exchange rates from ExchangeRate-API's open access endpoint, cached for 12 hours.
/// Its terms ask for the credit "Rates By Exchange Rate API" wherever the rates are shown (Calculator.Money, README).
internal static class Rates
{
    private const string Url = "https://open.er-api.com/v6/latest/USD";
    private static Dictionary<string, double>? rates;
    private static DateTime fetchedUtc;
    private static int fetching;

    public static event Action? Updated;

    private sealed class CacheFile
    {
        public DateTime FetchedUtc { get; set; }
        public Dictionary<string, double> Rates { get; set; } = new();
    }

    public static void Load()
    {
        var c = Json.Load<CacheFile>(Paths.InLocal("rates.json"));
        if (c.Rates.Count > 0)
        {
            rates = c.Rates;
            fetchedUtc = c.FetchedUtc;
        }
    }

    public static bool Known(string code) => rates?.ContainsKey(code) == true;

    public static bool TryGet(out Dictionary<string, double> r, out string age)
    {
        r = rates ?? new();
        var span = DateTime.UtcNow - fetchedUtc;
        age = span.TotalMinutes < 60 ? Tr("방금 받은 환율", "Exchange rate updated just now") : span.TotalHours < 24 ? Tr($"{(int)span.TotalHours}시간 전 환율", $"Exchange rate from {(int)span.TotalHours}h ago") : Tr($"{(int)span.TotalDays}일 전 환율", $"Exchange rate from {(int)span.TotalDays}d ago");
        return rates is { Count: > 0 };
    }

    public static void EnsureFresh()
    {
        if (rates is { Count: > 0 } && DateTime.UtcNow - fetchedUtc < TimeSpan.FromHours(12)) return;
        if (System.Threading.Interlocked.Exchange(ref fetching, 1) == 1) return;
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Bloom");
                string json = await http.GetStringAsync(Url);
                using var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, double>();
                foreach (var p in doc.RootElement.GetProperty("rates").EnumerateObject()) dict[p.Name] = p.Value.GetDouble();
                if (dict.Count > 0)
                {
                    rates = dict;
                    fetchedUtc = DateTime.UtcNow;
                    Json.Save(Paths.InLocal("rates.json"), new CacheFile { FetchedUtc = fetchedUtc, Rates = dict });
                    Updated?.Invoke();
                }
            }
            catch (Exception e)
            {
                Log.Error("rates", e);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref fetching, 0);
            }
        });
    }
}
