using System;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set usagecards`: the home's AI usage cards over made-up numbers, in both themes: Claude Max with a Fable week
/// near its limit, a second Claude with no plan and old numbers (its e-mail under the name), ChatGPT signed out.
internal static partial class Captures
{
    private static async Task UsageCardScreens(Host host, Launcher w, string dir)
    {
        Settings.Current.Home = null;
        var resets = DateTime.Now.AddHours(3);
        AiUsage.SetForQa(
            new UsageAccount("Claude", "Max", new() { new("5시간", 32, resets, UsageLimit.SessionMins), new("주간", 47, resets, UsageLimit.WeekMins), new("Fable 주간", 93, resets, UsageLimit.WeekMins, "Fable") }, DateTime.Now) { Id = AiAccounts.DefaultClaude, Who = "work@example.com" },
            new UsageAccount("ChatGPT", "Pro 200", new() { new("주간", 21, resets, UsageLimit.WeekMins) }, DateTime.Now) { Id = AiAccounts.DefaultChatGpt, Who = "work@example.com" },
            new UsageAccount("Claude", null, new() { new("5시간", 8, resets, UsageLimit.SessionMins), new("주간", 22, resets, UsageLimit.WeekMins), new("Fable 주간", 3, resets, UsageLimit.WeekMins, "Fable") }, DateTime.Now.AddMinutes(-40)) { Id = "qa-side", Who = "side@example.com" });
        AiUsage.FailForQa(AiAccounts.DefaultChatGpt);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{theme}-usagecards", Mode.Root, "", 900);
        }
    }
}
