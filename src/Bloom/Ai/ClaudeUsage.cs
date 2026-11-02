using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Claude's plan usage from Claude Code's own `/usage` (`claude -p /usage`): no model call, nothing counted against
/// the plan, the login stays Claude Code's. The answer is the text /usage shows, read line by line.
internal static partial class ClaudeUsage
{
    // "Current session: 3% used · resets Oct 8, 12:29pm (Asia/Seoul)"
    [GeneratedRegex(@"^(.+?):\s*(\d+(?:\.\d+)?)%\s*used(?:\s*[·•\-–]\s*resets?\s+(.+?))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UsageLine();

    [GeneratedRegex(@"current session|5[- ]?hour", RegexOptions.IgnoreCase)]
    private static partial Regex SessionName();

    [GeneratedRegex(@"\([^)]*\)|\s+")]
    private static partial Regex ZoneAndSpaces();

    [GeneratedRegex(@"^(?:([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2}),?\s+)?(\d{1,2})(?::(\d{2}))?\s*(am|pm)$", RegexOptions.IgnoreCase)]
    private static partial Regex ResetTime();

    [GeneratedRegex(@"max_(\d+)x", RegexOptions.IgnoreCase)]
    private static partial Regex MaxTier();

    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    /// One look at one account (no MCP servers, hooks or saved session: /usage needs none of them); null when it gave
    /// no numbers. An added account's folder goes in as CLAUDE_CONFIG_DIR.
    public static async Task<UsageAccount?> Ask(string cli, AiAccount account)
    {
        var psi = OneShot.Psi(cli);
        AiAccounts.UseHome(psi, "claude", account.Home);
        foreach (string a in new[] { "-p", "/usage", "--output-format", "json", "--no-session-persistence", "--strict-mcp-config", "--name", "Bloom", "--settings", "{\"disableAllHooks\":true}" })
            psi.ArgumentList.Add(a);
        var (_, output, error) = await OneShot.RunCli(psi, "", CancellationToken.None);
        string text = output;
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String) text = r.GetString() ?? "";
        }
        catch (JsonException)
        {
            /* not JSON: an error line, read as text */
        }
        var limits = Parse(text, DateTime.Now);
        if (limits.Count > 0)
        {
            var (plan, email) = Account(account.Home);
            return new UsageAccount("Claude", plan, limits, DateTime.Now) { Who = email };
        }
        Log.Write("ai usage: claude gave no limits — " + OneShot.Short((text + " " + error).Trim()));
        return null;
    }

    /// The 5-hour session and the all-models week (the per-model weeks are left out: the card has room for two).
    public static List<UsageLimit> Parse(string text, DateTime now)
    {
        UsageLimit? session = null, week = null;
        foreach (string raw in text.Split('\n'))
        {
            var m = UsageLine().Match(raw.Trim());
            if (!m.Success) continue;
            string name = m.Groups[1].Value;
            double used = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var resets = ParseReset(m.Groups[3].Success ? m.Groups[3].Value : null, now);
            if (SessionName().IsMatch(name)) session ??= new UsageLimit(Tr("5시간", "5h"), used, resets, UsageLimit.SessionMins);
            else if (IsAllModelsWeek(name)) week ??= new UsageLimit(Tr("주간", "Weekly"), used, resets, UsageLimit.WeekMins);
        }
        var list = new List<UsageLimit>(2);
        if (session is not null) list.Add(session);
        if (week is not null) list.Add(week);
        return list;
    }

    /// "Current week" or "Current week (all models)"; "Current week (Fable)" is one model's week.
    private static bool IsAllModelsWeek(string name) => name.Contains("week", StringComparison.OrdinalIgnoreCase)
        && (!name.Contains('(') || name.Contains("all models", StringComparison.OrdinalIgnoreCase));

    /// "Oct 8, 12:29pm (Asia/Seoul)", "Oct 13, 6am", "3pm" → local time (the CLI writes the PC's own zone).
    public static DateTime? ParseReset(string? text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = ResetTime().Match(ZoneAndSpaces().Replace(text, " ").Trim());
        if (!m.Success) return null;
        int hour = (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) % 12) + (m.Groups[5].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        var clock = new TimeSpan(hour, m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0, 0);
        if (!m.Groups[1].Success)
        {
            var today = now.Date + clock; // a bare time is the next one
            return today < now.AddMinutes(-1) ? today.AddDays(1) : today;
        }
        int month = Array.IndexOf(Months, m.Groups[1].Value.ToLowerInvariant()) + 1;
        if (month == 0) return null;
        var t = new DateTime(now.Year, month, 1).AddDays(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1) + clock;
        return t < now.AddDays(-3) ? t.AddYears(1) : t; // "Jan 2" seen in late December
    }

    /// "Max", "Ultra", "Pro" … and the e-mail, from the account details Claude Code keeps beside its sign-in (not the login
    /// itself): ~/.claude.json for the default folder, <folder>\.claude.json for an added one.
    private static (string? plan, string? email) Account(string? home)
    {
        try
        {
            // the default folder is the user's own CLAUDE_CONFIG_DIR when set (Claude Code keeps .claude.json in it), else ~
            home ??= Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir : Paths.Home;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, ".claude.json")));
            if (!doc.RootElement.TryGetProperty("oauthAccount", out var oa) || oa.ValueKind != JsonValueKind.Object) return (null, null);
            return (Plan(oa), Str(oa, "emailAddress"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    internal static string? Plan(JsonElement oa)
    {
        string tier = Str(oa, "userRateLimitTier") is { Length: > 0 } u ? u : Str(oa, "organizationRateLimitTier") ?? "";
        if (MaxTier().Match(tier) is { Success: true } x) return MaxName(x.Groups[1].Value);
        string type = Str(oa, "organizationType") ?? "";
        foreach (string plan in new[] { "Max", "Pro", "Team", "Enterprise" })
            if (type.Contains(plan, StringComparison.OrdinalIgnoreCase)) return plan;
        return null;
    }

    /// Claude's names since the Max plans were renamed: the old Max 5x is "Max", the old Max 20x "Ultra"; the sign-in
    /// still says max_5x / max_20x. Another multiple keeps its number.
    private static string MaxName(string times) => times switch { "5" => "Max", "20" => "Ultra", _ => $"Max {times}x" };

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
