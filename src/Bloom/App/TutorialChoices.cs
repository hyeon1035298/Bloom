using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;

namespace Bloom.App;

/// What the addons and notify pages hold until the finish flow applies it. Skipping or closing drops it.
internal sealed class TutorialChoices
{
    /// Page order of the add-on tiles; catalog ids not named here follow in catalog order.
    public static readonly string[] AddonOrder = ["pill-taskbar", "run-card", "memory-clean", "brightness", "keep-awake", "alt-tab-per-monitor"];
    public const string RunCard = "run-card";

    public bool AddonsShown { get; set; }             // the addons page is part of this run
    public List<string> AddonsListed { get; } = [];   // ids of the tiles shown, in page order
    public HashSet<string> Addons { get; } = [];      // ids switched on in the page
    public bool NotifyShown { get; set; }             // the notify page is part of this run
    public bool Notices { get; set; } = true;
    public bool Approve { get; set; }
    public bool Windows { get; set; }
    public bool T3Link { get; set; } = true;
    public string Click { get; set; } = "app";        // "app" | "taskbar"

    public bool Taskbar => Addons.Contains(Bloom.Addons.AddonProtocol.Id);

    /// The listed add-ons that were on when the pages started. The finish switches only what the page changed from
    /// this, so one switched in Settings meanwhile (the last page links there) is left as it was set.
    public HashSet<string> StartOn { get; } = [];

    /// The notification settings as they were saved when the pages started: the finish writes only the ones the pages
    /// changed, over what Settings may have set since.
    public sealed record Saved(bool Notices, bool Approve, bool Windows, bool T3Link, string Click);

    public Saved Start { get; private init; } = new(true, false, false, true, "app");

    private bool startingApprove;
    private bool approveMoved;

    /// The pages' starting state: what is on now. `on` says whether an add-on is switched on.
    public static TutorialChoices From(Settings s, IReadOnlyList<string> catalogIds, Func<string, bool> on, IReadOnlyList<string> run, TutorialEnv env)
    {
        var c = new TutorialChoices
        {
            AddonsShown = run.Contains(TutorialSteps.Addons),
            NotifyShown = run.Contains(TutorialSteps.Notify),
            Notices = s.Notices,
            Approve = s.NoticeApprove,
            Windows = s.NoticeWindows,
            T3Link = s.T3Link,
            Click = s.NoticeClick == "taskbar" && on(Bloom.Addons.AddonProtocol.Id) ? "taskbar" : "app",
            Start = new(s.Notices, s.NoticeApprove, s.NoticeWindows, s.T3Link, s.NoticeClick),
        };
        c.startingApprove = c.Approve;
        // known ids in page order, then the rest as the catalog gives them (OrderBy is stable)
        int Rank(string id) => Array.IndexOf(AddonOrder, id) is var i and >= 0 ? i : AddonOrder.Length;
        c.AddonsListed.AddRange(catalogIds.Where(id => env.Claude || id != RunCard).OrderBy(Rank));
        foreach (string id in c.AddonsListed)
            if (on(id)) c.Addons.Add(id);
        c.StartOn.UnionWith(c.Addons);
        return c;
    }

    /// The user moved the approve switch: it is theirs from here on.
    public void SetApprove(bool value)
    {
        Approve = value;
        approveMoved = true;
    }

    /// Called when the notify page is built: approve is preselected while run-card is picked and Claude Code is
    /// here (until the user moves it), and the click goes back to "app" without the taskbar add-on.
    public void Settle(TutorialEnv env)
    {
        if (!approveMoved) Approve = startingApprove || (Addons.Contains(RunCard) && env.Claude);
        if (!Taskbar) Click = "app";
    }
}
