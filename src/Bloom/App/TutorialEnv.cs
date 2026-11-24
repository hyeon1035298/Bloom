using System.Collections.Generic;
using System.Linq;
using Bloom.Core;

namespace Bloom.App;

/// What the tutorial found on this PC. Pages read this instead of probing, so QA can show any state.
internal sealed record TutorialEnv
{
    public bool Claude { get; init; }
    public bool Codex { get; init; }
    public bool Omp { get; init; }
    public bool T3 { get; init; }
    public bool Everything { get; init; }
    public bool AdminOffered { get; init; }
    public bool AddonsOffered { get; init; }
    public IReadOnlyList<Browser.InstalledBrowser> Browsers { get; init; } = [];
    public string? DefaultBrowserExe { get; init; }
    /// QA pictures: the hero keeps its poster, nothing is fetched, nothing animates.
    public bool Still { get; init; }

    /// Chat providers found, in Bloom's order: "claude", "chatgpt", "omp".
    public IReadOnlyList<string> Providers =>
        [.. new[] { (Claude, "claude"), (Codex, "chatgpt"), (Omp, "omp") }.Where(p => p.Item1).Select(p => p.Item2)];
    public bool AnyProvider => Claude || Codex || Omp;
    public bool Notifier => Claude || Codex || T3;
    public TutorialSteps.Has Has => new(AddonsOffered, Notifier);

    /// QA: stands in for Detect.
    internal static TutorialEnv? Forced;
    public static TutorialEnv Now => Forced ?? Detect();

    public static TutorialEnv Detect()
    {
        var s = Settings.Current;
        return new()
        {
            Claude = Ai.ClaudeSession.FindCli(s.ClaudePath) is not null,
            Codex = Ai.CodexSession.FindCli(s.CodexPath) is not null,
            Omp = Ai.OmpCli.FindCli(s.OmpPath) is not null,
            T3 = Ai.T3Agent.Installed(),
            Everything = Search.EverythingClient.ExePath() is not null,
            AdminOffered = ElevatedWinKey.Offered,
            AddonsOffered = Addons.AddonHub.Offered,
            Browsers = [.. Browser.Web.Installed],
            DefaultBrowserExe = Native.Win32.AssocExecutable("http"),
        };
    }
}
