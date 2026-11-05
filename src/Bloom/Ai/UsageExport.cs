using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Ai;

/// The plan usage as a small file other programs may read (설정 > 홈 화면 > AI 사용량, off by default):
/// `%LOCALAPPDATA%\Bloom\export\ai-usage.json`, the numbers the home shows. It is written when a look has ended (and when
/// something the file shows was changed), never while one runs, and not again when nothing but the time of writing would
/// differ. A reader that watches the folder wakes only for that. The file has percentages used, reset times and the
/// sign-in state; never an e-mail, a token or a folder, and the account's name only as the user wrote it (✎).
internal static class UsageExport
{
    private const string Rel = @"export\ai-usage.json";
    private static readonly JsonSerializerOptions Compact = new(Json.Options) { WriteIndented = false };
    private static string? last; // what was written most recently, without the time: an unchanged one isn't written again

    public static bool On => Settings.Current.ExportUsage;

    /// The switch: on → the numbers are asked now, and the file follows the look; off → the file goes.
    public static void Set(bool on)
    {
        Settings.Current.ExportUsage = on;
        if (on)
        {
            _ = AiUsage.Refresh(all: false);
            return;
        }
        last = null;
        try
        {
            File.Delete(Path.Combine(Paths.Local, Rel));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("usage export off", e);
        }
    }

    /// A look ended (AiUsage.Updated; one that runs says nothing yet), or the order, a name, the interval or what's shown changed.
    public static void Changed()
    {
        if (On && !AiUsage.Busy && !AiUsage.IsMadeUp) Write(true);
    }

    /// Bloom ends: the file says so (`online` false), so a reader doesn't take the last numbers for current ones. Only
    /// when this run wrote it; a file from an earlier run already said so.
    public static void Closed()
    {
        if (On && last is not null && !AiUsage.IsMadeUp) Write(false);
    }

    private static void Write(bool online)
    {
        var snapshot = Make(AiUsage.Accounts, online, 0);
        string body = JsonSerializer.Serialize(snapshot, Compact);
        if (body == last) return;
        last = body;
        Json.SaveText(Paths.InLocal(Rel), JsonSerializer.Serialize(snapshot with { WrittenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, Compact));
    }

    /// The file's text for these cards (`now`: the time written, UTC seconds).
    public static string Build(List<UsageAccount> cards, bool online, long now) => JsonSerializer.Serialize(Make(cards, online, now), Compact);

    private static Snapshot Make(List<UsageAccount> cards, bool online, long now)
    {
        var s = Settings.Current;
        var accounts = new List<Entry>();
        foreach (var c in cards)
        {
            if (c.Balance is not null || AiAccounts.Find(c.Id) is not { } a) continue; // an API key's card has no limits
            int every = a.IsDefault ? (s.UsageMinutes == 5 ? 5 : 10) : AiUsage.ExtraMinutes;
            var taken = new HashSet<string>();
            var limits = Ordered(c.Limits).ConvertAll(l => new Cap(l.WindowMins, l.Percent, l.Resets is { } r ? Unix(r) : null, Key(l, taken), l.Title));
            // the taskbar's pick only while the account still has that limit, the logo only while Bloom's copy is there
            string? bar = a.Bar is { } b && limits.Exists(l => l.Key == b) ? b : null;
            string? icon = a.Icon is { } i && File.Exists(i) ? i : null;
            accounts.Add(new Entry(a.Id, a.Kind, c.Name, c.Plan, a.Alias is { Length: > 0 } alias ? alias : null, AiUsage.Failed(a.Id), Unix(c.Fetched), every, limits, bar, icon));
        }
        string? tag = s.UsageTag is "name" or "plan" ? s.UsageTag : null; // none is left out
        return new Snapshot(1, "Bloom", now, online, s.UsageLeft ? "left" : "used", accounts, tag);
    }

    /// An account's limits the taskbar can show, as the file keys them: "session", "week", "week-<model>", with names.
    public static List<(string key, string name)> Bars(UsageAccount card)
    {
        var taken = new HashSet<string>();
        var list = new List<(string, string)>();
        foreach (var l in Ordered(card.Limits))
            if (Key(l, taken) is { } key)
                list.Add((key, key == "session" ? Tr("5시간", "5 hours") : l.Model is { } m ? Tr($"{m} 주간", $"{m} weekly") : Tr("주간", "Weekly")));
        return list;
    }

    // A reader takes the longest window (the later one on a tie) and only the first four limits: the 5-hour session
    // first, then at most two models' weeks, the all-models week last, so it stays the week a reader finds and the cap never cuts it.
    private static List<UsageLimit> Ordered(List<UsageLimit> limits)
    {
        var models = limits.Where(l => l.Model is not null).Take(2);
        var weeks = limits.Where(l => l.Model is null && l.WindowMins == UsageLimit.WeekMins);
        return limits.Where(l => l.Model is null && l.WindowMins != UsageLimit.WeekMins).Concat(models).Concat(weeks).ToList();
    }

    /// "session", "week" or "week-<model>" (lower case, anything but letters and digits as -); none for an odd window.
    /// A model with no letters or digits of its own is "week-model"; a key already given to another limit of the account
    /// (`taken`, in export order) gets -2, -3 and so on, so no two limits of an account share one.
    private static string? Key(UsageLimit l, HashSet<string> taken)
    {
        string? key = l.Model is { } m
            ? "week-" + (Regex.Replace(m.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } slug ? slug : "model")
            : l.WindowMins switch { UsageLimit.SessionMins => "session", UsageLimit.WeekMins => "week", _ => null };
        if (key is null) return null;
        string unique = key;
        for (int n = 2; !taken.Add(unique); n++) unique = key + "-" + n;
        return unique;
    }

    private static long Unix(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();

    // The file's shape (v 1); the names follow Json.Options (camelCase, null left out).
    private sealed record Snapshot(int V, string App, long WrittenAt, bool Online, string Show, List<Entry> Accounts, string? Tag);
    private sealed record Entry(string Id, string Kind, string Name, string? Plan, string? Label, bool SignedOut, long FetchedAt, int EveryMins, List<Cap> Limits, string? Bar, string? Icon);
    private sealed record Cap(int? Mins, double Used, long? ResetsAt, string? Key, string Name);
}
