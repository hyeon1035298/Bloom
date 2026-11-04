using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// The usage file other programs read: its shape (v 1, UTC seconds, percentages used), what it leaves out (e-mails, folders, API
/// key cards) and when the user's own name for an account goes in.
internal static class UsageExportTests
{
    public static void FileHasTheNumbersAndNoIdentity()
    {
        var list = Settings.Current.AiAccounts;
        list.Clear();
        list.Add(new AiAccount { Id = "claude", Kind = "claude", Email = "someone@example.com", Alias = "Work" });
        list.Add(new AiAccount { Id = "chatgpt", Kind = "chatgpt", Email = "other@example.com" });
        list.Add(new AiAccount { Id = "claude-0a1b2c3d", Kind = "claude", Home = @"C:\Made\Up\accounts\claude-0a1b2c3d", Email = "third@example.com" });
        int minutes = Settings.Current.UsageMinutes;
        try
        {
            Settings.Current.UsageMinutes = 5;
            var fetched = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Local);
            var resets = new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Local);
            var cards = new List<UsageAccount>
            {
                new("Claude", "Max", new List<UsageLimit> { new("5시간", 32, resets, UsageLimit.SessionMins), new("주간", 47, null, UsageLimit.WeekMins) }, fetched) { Id = "claude", Who = "someone@example.com" },
                new("ChatGPT", "Pro 200", new List<UsageLimit> { new("주간", 21.5, resets) }, fetched) { Id = "chatgpt", Who = "other@example.com" },
                new("Claude", null, new List<UsageLimit>(), fetched) { Id = "claude-0a1b2c3d", Who = "third@example.com" },
                new("OpenRouter", null, new List<UsageLimit>(), fetched) { Id = "openrouter", Balance = new UsageBalance("잔액", "$4.20", "") },
            };
            string text = UsageExport.Build(cards, online: true, now: 1762238400);
            foreach (var secret in new[] { "example.com", "someone", @"C:\", "$4.20", "OpenRouter", "\n" })
                Check.True(!text.Contains(secret, StringComparison.Ordinal), "nothing of " + secret + " in the file");

            var root = JsonNode.Parse(text)!.AsObject();
            Check.Eq((int)root["v"]!, 1, "version");
            Check.Eq((string)root["app"]!, "Bloom", "app");
            Check.Eq((long)root["writtenAt"]!, 1762238400L, "written at");
            Check.True((bool)root["online"]!, "online");
            Check.Eq((string)root["show"]!, "used", "show");
            var accounts = root["accounts"]!.AsArray();
            Check.Eq(accounts.Count, 3, "two defaults and the added one; the API key's card is left out");

            var claude = accounts[0]!.AsObject();
            Check.Eq((string)claude["id"]!, "claude", "id");
            Check.Eq((string)claude["kind"]!, "claude", "kind");
            Check.Eq((string)claude["name"]!, "Claude", "name");
            Check.Eq((string)claude["plan"]!, "Max", "plan");
            Check.Eq((string)claude["label"]!, "Work", "the user's own name is the label");
            Check.True(!(bool)claude["signedOut"]!, "signed in");
            Check.Eq((long)claude["fetchedAt"]!, new DateTimeOffset(fetched).ToUnixTimeSeconds(), "fetched, UTC seconds");
            Check.Eq((int)claude["everyMins"]!, 5, "a default account follows the interval");
            var limits = claude["limits"]!.AsArray();
            Check.Eq((int)limits[0]!["mins"]!, 300, "the session's window");
            Check.Eq((double)limits[0]!["used"]!, 32.0, "used, not left");
            Check.Eq((long)limits[0]!["resetsAt"]!, new DateTimeOffset(resets).ToUnixTimeSeconds(), "reset, UTC seconds");
            Check.True(limits[1]!.AsObject()["resetsAt"] is null, "an unknown reset is left out");

            var gpt = accounts[1]!.AsObject();
            Check.True(gpt["label"] is null, "no name of the user's: no label, and the e-mail doesn't take its place");
            Check.True(gpt["limits"]!.AsArray()[0]!.AsObject()["mins"] is null, "an unknown window is left out");
            Check.Eq((double)gpt["limits"]!.AsArray()[0]!["used"]!, 21.5, "a fraction stays");
            Check.Eq((int)accounts[2]!["everyMins"]!, AiUsage.ExtraMinutes, "an added account is asked less often");

            Check.True(UsageExport.Build(cards, online: false, now: 1762238500).Contains("\"online\":false", StringComparison.Ordinal), "the closing file says offline");
        }
        finally
        {
            list.Clear();
            Settings.Current.UsageMinutes = minutes;
        }
    }
}
