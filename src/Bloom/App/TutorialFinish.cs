using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Bloom.Addons;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

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
        const string taskbar = AddonProtocol.Id;
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
        var taskbar = AddonHub.Taskbar;
        List<string> onNow = [.. AddonHub.All.Where(a => a.On).Select(a => a.Id)];
        bool taskbarOnNow = taskbar.On;
        bool chatReady = TaskbarChatSetup.Ready(taskbar);
        var plan = Decide(c, env, onNow, Settings.Current.AddonsConfirmed, chatReady);

        // the questions, over the tutorial window
        if (plan.Consent)
        {
            if (!AddonConsent.AskMany(owner, Of(plan.TurnOn), taskbarChat: plan.TaskbarChat && !chatReady))
                plan = Declined(plan, taskbarOnNow, chatReady);
        }
        else if (plan.ChatConsent && !AddonConsent.AskTaskbarChat(owner)) plan = plan with { TaskbarChat = false };
        // Bloom ended while a question was open: nothing is applied, closed or opened over the quit
        if (host.Quitting) return;

        if (plan.ApplyNotify) ApplyNotify(plan, env, owner.Dispatcher, readyNow: chatReady && taskbarOnNow);
        var s = Settings.Current;
        if (plan.TurnOff.Contains(AddonProtocol.Id) && s.NoticeClick == "taskbar") s.NoticeClick = "app"; // no panel without the add-on
        Settings.Save();
        close();
        if (summon) host.Summon();

        if (plan.TurnOn.Count == 0 && plan.TurnOff.Count == 0 && !(plan.TaskbarChat && !chatReady)) return;
        if (!AddonHub.Offered)
        {
            // a dev or QA copy: AddonHub.Set would still write the list and download (only the start is held back)
            Log.Write("tutorial: add-ons are not switched in this copy");
            return;
        }
        _ = SwitchAddons(host, plan, chatReady);
    }

    private static List<Addon> Of(IEnumerable<string> ids) => [.. ids.Select(id => AddonHub.All.FirstOrDefault(a => a.Id == id)).OfType<Addon>()];

    /// The notification choices, with the same calls as the notices page of Settings.
    private static void ApplyNotify(Plan plan, TutorialEnv env, Dispatcher ui, bool readyNow)
    {
        var s = Settings.Current;
        s.Notices = plan.Notices;
        Notices.Apply();
        s.NoticeWindows = plan.Windows;
        WindowsToasts.Apply(ui); // it only polls while both are on, so the add-on may come later
        s.T3Link = plan.T3Link;
        T3Lines.Changed();
        if (env.Claude && s.NoticeApprove != plan.Approve)
        {
            bool v = plan.Approve;
            s.NoticeApprove = v; // first: Bloom's start-up ClaudeHooks.Follow reads it in its turn on the file
            if (!ClaudeHooks.Set(v))
            {
                s.NoticeApprove = !v;
                Notices.Result(false, Tr("Bloom · 알림", "Bloom · Notification"), Tr("Claude Code 설정을 바꾸지 못했습니다", "Could not update Claude Code settings"), ClaudeHooks.SettingsFile);
            }
            else ClaudeApprovals.Apply();
        }
        // a panel that still has to be set up gets the click once that has worked (SwitchAddons)
        s.NoticeClick = Click(plan, s.NoticeClick, readyNow);
    }

    /// After the window has closed, on the UI thread (no ConfigureAwait(false): AddonHub.Set must be called there):
    /// the add-ons switched, the taskbar AI panel set up, and one card for what became of them.
    private static async Task SwitchAddons(Host host, Plan plan, bool chatReady)
    {
        try
        {
            await AddonBatch.TurnOff(Of(plan.TurnOff));
            var outcomes = await AddonBatch.TurnOn(Of(plan.TurnOn), o =>
            {
                if (o.Ok && plan.TaskbarChat && o.Addon.Id == AddonProtocol.Id) SetUpChat(o.Addon);
                return Task.CompletedTask;
            });
            if (plan.TaskbarChat && !chatReady && !plan.TurnOn.Contains(AddonProtocol.Id)) SetUpChat(AddonHub.Taskbar);
            if (outcomes.Count == 0) return;
            var settled = await AddonBatch.Settled([.. outcomes.Where(o => o.Ok).Select(o => o.Addon)], TimeSpan.FromSeconds(120));
            ShowCard(host, Card(outcomes, settled));
        }
        catch (Exception e)
        {
            Log.Error("tutorial add-ons", e);
        }
    }

    /// The click goes to the panel only once the add-on's values are set (TaskbarChat.Open falls back to the app until
    /// the host runs).
    private static void SetUpChat(Addon taskbar)
    {
        if (!TaskbarChatSetup.Enable(taskbar))
        {
            Log.Write("tutorial: the taskbar add-on has no AI panel settings, the click stays with the app");
            return;
        }
        Settings.Current.NoticeClick = "taskbar";
        Settings.Save();
    }

    internal enum CardKind { On, Starting, Failed }

    /// What the result card says: the add-ons that failed (not here, not started, or settled in a state that is not
    /// running), else those still starting when the wait ended, else all of them on.
    internal static (CardKind Kind, IReadOnlyList<Addon> Named) Card(IReadOnlyList<AddonBatch.Outcome> outcomes,
        IReadOnlyList<(Addon Addon, AddonStatus Status)> settled)
    {
        List<Addon> failed = [.. outcomes.Where(o => !o.Ok).Select(o => o.Addon),
            .. settled.Where(x => x.Status.State is not (AddonState.Running or AddonState.Starting)).Select(x => x.Addon)];
        if (failed.Count > 0) return (CardKind.Failed, failed);
        List<Addon> starting = [.. settled.Where(x => x.Status.State == AddonState.Starting).Select(x => x.Addon)];
        return starting.Count > 0 ? (CardKind.Starting, starting) : (CardKind.On, [.. outcomes.Select(o => o.Addon)]);
    }

    private static void ShowCard(Host host, (CardKind Kind, IReadOnlyList<Addon> Named) card)
    {
        string head = Tr("Bloom · 애드온", "Bloom · Add-ons");
        string open = Tr("애드온 설정 열기", "Open add-on settings");
        void Open() => host.OpenSettings("addons");
        string names = string.Join(", ", card.Named.Select(a => AddonConsent.Describe(a).Name));
        switch (card.Kind)
        {
            case CardKind.On:
                Notices.Result(true, head, Tr("애드온을 켰습니다", "Add-ons are on"), names);
                break;
            case CardKind.Starting:
                // not the success wording while nothing works yet (a slow symbol download)
                Notices.Result(true, head, Tr("애드온을 켜는 중입니다", "Add-ons are starting"), names, open, Open);
                break;
            default:
                string detail = string.Join("\n", card.Named.Select(a => AddonConsent.Describe(a).Name + " " + AddonConsent.StatusText(a)));
                Notices.Result(false, head, Tr("켜지 못한 애드온이 있습니다", "Some add-ons could not be turned on"), detail, open, Open, keep: true);
                break;
        }
    }
}
