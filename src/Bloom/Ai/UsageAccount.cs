using System;
using System.Collections.Generic;
using System.Linq;

namespace Bloom.Ai;

/// One account's card as one provider answered; AiUsage sets Id.
internal sealed record UsageAccount(string Name, string? Plan, List<UsageLimit> Limits, DateTime Fetched)
{
    public string Id { get; init; } = ""; // the AiAccount it belongs to
    public string? Who { get; init; } // the account's e-mail, shown when one kind has more than one card
    public UsageBalance? Balance { get; init; } // an API key's card (ApiBalances): its balance, no limits; not an AiAccount

    /// The limits the home, the notices and the tooltip show: a model's own week (Model set) is only in the usage file.
    public IEnumerable<UsageLimit> Shown => Limits.Where(l => l.Model is null);

    /// ChatGPT's plan as Codex's app-server and wham name it, in the card's words: the three Pro tiers by their monthly
    /// price (the user's call, 10-12), from Codex's own names (prolite, pro, promax; OpenAI publishes no table), the
    /// rest with a capital (plus → Plus).
    public static string? ChatGptPlan(string? type) => type?.ToLowerInvariant() switch
    {
        null or "" => null,
        "prolite" => "Pro 100",
        "pro" => "Pro 200",
        "promax" => "Pro 500",
        _ => char.ToUpperInvariant(type[0]) + type[1..],
    };
}

/// A limit's title, percent used, reset time (null means the reset time is unknown) and the window's length in minutes
/// (null when the provider doesn't say); Model is set for one model's own week (Claude's "Current week (Fable)").
internal sealed record UsageLimit(string Title, double Percent, DateTime? Resets, int? WindowMins = null, string? Model = null)
{
    /// The two windows every plan has: the 5-hour session and the week.
    public const int SessionMins = 300, WeekMins = 10080;

    /// The window length as the providers report it, or null for none (0 or less).
    public static int? Mins(double mins) => mins > 0 ? (int)mins : null;

    /// Names a limit window from its length in minutes, as Codex's app-server and wham report it (Claude's /usage names
    /// its own): 300 → "5시간", 10080 → "주간".
    public static string Window(int mins) => mins switch
    {
        WeekMins => Tr("주간", "Weekly"),
        >= 1440 when mins % 1440 == 0 => Tr($"{mins / 1440}일", $"{mins / 1440}d"),
        >= 60 => Tr($"{mins / 60}시간", $"{mins / 60}h"),
        > 0 => Tr($"{mins}분", $"{mins} min"),
        _ => Tr("한도", "Limit"),
    };
}
