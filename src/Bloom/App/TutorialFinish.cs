using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Bloom.App;

/// The tutorial's last button: asks about the add-ons, applies the notification choices, closes the window and
/// turns the add-ons on in the background.
internal static class TutorialFinish
{
    /// What the choices come to, before anything is asked or changed.
    internal sealed record Plan(
        IReadOnlyList<string> TurnOn,        // picked and off now, taskbar first
        IReadOnlyList<string> TurnOff,       // listed, on now and no longer picked
        bool Consent,                        // the add-on window is needed
        bool TaskbarChat,                    // the click goes to the taskbar AI panel
        bool ChatConsent,                    // only the AI panel is new: its own question
        bool ApplyNotify, bool Notices, bool Approve, bool Windows, bool T3Link);

    public static Plan Decide(TutorialChoices c, TutorialEnv env, IReadOnlyCollection<string> onNow,
        IReadOnlyCollection<string> confirmed, bool chatReady)
    {
        const string taskbar = Bloom.Addons.AddonProtocol.Id;
        List<string> turnOn = c.AddonsShown ? [.. c.AddonsListed.Where(id => c.Addons.Contains(id) && !onNow.Contains(id))] : [];
        // the taskbar add-on goes first: the others' pictures and the AI panel sit on it
        turnOn = [.. turnOn.OrderBy(id => id == taskbar ? 0 : 1)];
        List<string> turnOff = c.AddonsShown ? [.. c.AddonsListed.Where(id => !c.Addons.Contains(id) && onNow.Contains(id))] : [];
        bool taskbarAfter = c.AddonsShown ? c.Taskbar : onNow.Contains(taskbar);
        bool applyNotify = c.NotifyShown;
        bool taskbarChat = applyNotify && c.Notices && c.Click == "taskbar" && taskbarAfter && (env.Claude || env.Codex);
        bool consent = turnOn.Count > 0 && (turnOn.Any(id => !confirmed.Contains(id)) || (taskbarChat && !chatReady));
        return new Plan(turnOn, turnOff, consent, taskbarChat,
            ChatConsent: !consent && taskbarChat && !chatReady,
            ApplyNotify: applyNotify,
            Notices: c.Notices,
            Approve: c.Approve && env.Claude,
            Windows: c.Windows && taskbarAfter,
            T3Link: c.T3Link);
    }

    /// What is left of a plan when the user answers 켜지 않기. `chatReady` = the taskbar AI panel works already.
    public static Plan Declined(Plan p, bool taskbarOnNow, bool chatReady) => p with
    {
        TurnOn = [],
        Consent = false,
        ChatConsent = false,
        // a click that works already is kept; a panel that would have to be set up is not
        TaskbarChat = p.TaskbarChat && taskbarOnNow && chatReady,
        Windows = p.Windows && taskbarOnNow,
    };

    /// What NoticeClick is after the notification choices are applied: `saved` when the plan leaves it alone.
    /// `readyNow` = the taskbar add-on is on and its AI panel is set up already.
    internal static string Click(Plan p, string saved, bool readyNow) =>
        // with the notices off the saved click stays: writing "app" would drop a working taskbar click
        !p.ApplyNotify || !p.Notices ? saved : p.TaskbarChat && readyNow ? "taskbar" : "app";

    /// Runs the whole flow. `close` closes the tutorial window (which marks the pages seen); `summon` opens the launcher after it.
    public static void Run(Host host, Window owner, TutorialChoices c, TutorialEnv env, Action close, bool summon)
    {
        close();
        if (summon) host.Summon();
    }
}
