using System.IO;
using Bloom.Core;

namespace Bloom.Ai;

/// API prices per model (USD per 1M tokens): a built-in table checked on Checked, under the user's own prices.json.
internal static class TokenPrices
{
    /// When the built-in table was checked against the providers' pages.
    public const string Checked = "2026-11-02";

    /// The user's own prices over the built-in ones: prices.json in the data folder.
    public static string OverridePath => Paths.In("prices.json");

    /// Makes OverridePath as {"version":1,"models":{}} when it is missing (설정 opens it next).
    public static void EnsureOverride()
    {
        if (!File.Exists(OverridePath)) File.WriteAllText(OverridePath, "{\"version\":1,\"models\":{}}\n");
    }
}
