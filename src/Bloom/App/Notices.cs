using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// One card: its kind (start | done | waiting | limit | recover), the small line over it ("Claude Code · 작업 완료"),
/// what it is about, a line under that, and a key for a card that stays until its state ends (승인 대기). Action is the
/// button under the card; Click is what a click on the card itself does before it closes (a turn's card: go to its
/// session window). Gone runs when the card leaves for any reason; Buttons are a row of choices (an approval's 허용 · 거절);
/// Body is text shown whole, wrapped (what an approval would allow); Sample marks a made-up card of 미리 보기. Questions
/// are shown in place of Body, to answer on the card (AskUserQuestion), with Buttons after their 답하기.
internal sealed record Notice(string Kind, string Head, string Title, string? Detail, string? Key = null, string? ActionText = null, Action? Action = null, Action? Click = null,
    Action? Gone = null, IReadOnlyList<(string Text, Action Run)>? Buttons = null, string? Body = null, bool Sample = false, CardQuestions? Questions = null);

/// 알림: Claude Code and Codex turns (ClaudeWatch, CodexWatch — 진행 중, 작업 완료, 승인 대기) and the plan limits
/// (AiUsage — 한도 임박 · 소진, 한도 복구) as cards (NoticeWindow) with a sound each (Chimes). Every kind has its switch
/// in 설정 > 알림, and all of it stops with the main one. What is already true when watching starts (a turn already
/// running, a limit already low) is taken in silently, so a restart never repeats old news.
internal static class Notices
{
    public static Action? OpenUpdates { get; set; }
    public static Action? UpdateNow { get; set; } // Host.UpdateNow: saved, then the downloaded update goes in
    // 진행 중 waits a moment (quick turns stay quiet; a program's run — a tool checking something — longer), and
    // 작업 완료 needs a turn that took a while
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(4), HeadlessStartDelay = TimeSpan.FromSeconds(15), ShortestDone = TimeSpan.FromSeconds(10);
    private const double LowLeft = 20; // 한도 임박 at this much (%) left

    private static readonly ClaudeWatch claude = new();
    private static readonly CodexWatch codex = new();
    private static readonly Dictionary<string, DispatcherTimer> starting = new(); // 진행 중 waits a moment: quick turns stay quiet
    private static readonly Dictionary<string, (int level, DateTime? resets)> levels = new(); // account|limit → 0 fine, 1 low, 2 used up
    private static readonly Dictionary<string, int> approving = new(); // a session's key → its approval cards open now (ClaudeApprovals)
    private static Dictionary<string, AgentTask> last = new();
    private static DispatcherTimer? tick;
    private static NoticeWindow? window;
    private static bool first = true, polling, holding;

    /// QA measuring runs: cards stay off-screen and make no sound.
    public static bool Quiet;

    /// Host, once (not in QA runs): a look every 2 seconds while 알림 is on.
    public static void Start(Dispatcher ui)
    {
        claude.Finished += (t, took) => ui.BeginInvoke(() => Done(t, took));
        codex.Finished += (t, took) => ui.BeginInvoke(() => Done(t, took));
        tick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2) };
        tick.Tick += async (_, _) => await Look();
        Apply();
    }

    /// The main switch changed (설정 > 알림): watching runs only while it is on; turned off, everything is forgotten.
    public static void Apply()
    {
        if (tick is null) return;
        tick.IsEnabled = Settings.Current.Notices;
        if (Settings.Current.Notices) return;
        foreach (var t in starting.Values) t.Stop();
        starting.Clear();
        levels.Clear();
        last = new();
        first = true;
        window?.Clear();
        NoticeLog.SettleAll();
    }

    /// 미리 보기 (설정 > 알림, and after a position or size change): a made-up card of each kind switched on, in place of
    /// the made-up ones shown before; real cards on screen stay.
    public static void Preview(bool one = false)
    {
        var s = Settings.Current;
        var all = new (bool on, Notice n)[]
        {
            (s.NoticeStart, new Notice("start", Tr("Claude Code · 진행 중", "Claude Code · Working"), Tr("알림 미리 보기", "Notice preview"), Tr("현재 작업 표시", "Current task"), Sample: true)),
            (s.NoticeDone, new Notice("done", Tr("Codex · 작업 완료", "Codex · Done"), Tr("알림 미리 보기", "Notice preview"), Tr("소요 시간 3분", "Elapsed: 3 min"), Sample: true)),
            (s.NoticeWaiting, new Notice("waiting", Tr("Claude Code · 승인 대기", "Claude Code · Needs approval"), Tr("알림 미리 보기", "Notice preview"), Tr("승인 대기 · 클릭하여 세션으로 이동", "Approval pending · click to go to session"), Click: () => { }, Sample: true)), // no session behind a preview: the click only closes it
            (s.NoticeLimit, new Notice("limit", Tr("Claude · 한도 임박", "Claude · Limit near"), Tr("주간 한도 18% 남음", "18% of the weekly limit left"), Tr("2일 뒤 초기화", "Resets in 2 d"), Sample: true)),
            (s.NoticeRecover, new Notice("recover", Tr("ChatGPT · 한도 복구", "ChatGPT · Limit reset"), Tr("5시간 한도 초기화 완료", "5-hour limit reset"), null, Sample: true)),
        };
        var shown = all.Where(x => x.on).Select(x => x.n).ToList();
        if (shown.Count == 0) shown.Add(all[1].n);
        window?.ClearSamples();
        bool stayed = false;
        foreach (var n in one ? shown.Take(1) : shown) stayed |= Show(n, sound: false, asked: true);
        if (stayed) Chimes.Play(shown[0].Kind);
    }

    /// 직접 옮기기 (설정 > 알림): the cards on screen step out of sight while a card is dragged to its place on the whole
    /// screen (NoticePlacer); kept, a preview card shows there with the cards that were up. `after` runs either way.
    public static void Arrange(Action? after = null)
    {
        holding = true;
        window?.Hold(true);
        NoticePlacer.Open(kept =>
        {
            holding = false;
            window?.Hold(false);
            if (kept) Preview(one: true);
            after?.Invoke();
        });
    }

    /// Windows says now is not the time: the PC is locked, or a full-screen app, game or presentation is in front.
    private static bool UserBusy() => Native.Win32.SHQueryUserNotificationState(out int state) == 0 && state is >= 1 and <= 4;

    private static async Task Look()
    {
        if (polling) return;
        polling = true;
        try
        {
            var now = await Task.Run(() =>
            {
                var list = claude.Poll();
                list.AddRange(codex.Poll());
                return list;
            });
            Compare(now);
            Limits();
        }
        catch (Exception e)
        {
            Log.Error("notices", e);
        }
        finally
        {
            polling = false;
        }
    }

    /// 진행 중 for a turn that is new (after a moment), 승인 대기 for one that started waiting; a wait card goes when it ends.
    private static void Compare(List<AgentTask> tasks)
    {
        var now = tasks.ToDictionary(t => t.Key);
        foreach (var t in tasks)
        {
            last.TryGetValue(t.Key, out var before);
            if (!first && before is null && !t.Waiting) Starting(t);
            if (t.Waiting && before?.Waiting != true)
            {
                Cancel(t.Key);
                if (!first && !approving.ContainsKey(t.Key))
                {
                    var n = new Notice("waiting", Head(t.Tool, Tr("승인 대기", "Needs approval")), t.Title, Tr("승인 대기", "Approval pending"), t.Key, Click: Jump(t));
                    NoticeLog.Wait(t.Key, n.Head, n.Title, n.Detail, n.Click);
                    if (Settings.Current.NoticeWaiting) Show(n);
                }
            }
            else if (!t.Waiting && before?.Waiting == true) Settled(t.Key);
        }
        foreach (var key in last.Keys.Where(k => !now.ContainsKey(k)))
        {
            Cancel(key);
            Settled(key);
        }
        last = now;
        first = false;
    }

    /// A wait (or the turn) ended: its card goes, and 알림 센터's waiting line with it.
    private static void Settled(string key)
    {
        window?.Remove(key);
        NoticeLog.Settle(key);
    }

    private static void Starting(AgentTask t)
    {
        if (!Settings.Current.NoticeStart || tick is null) return;
        var wait = new DispatcherTimer(DispatcherPriority.Background, tick.Dispatcher) { Interval = t.Headless ? HeadlessStartDelay : StartDelay };
        wait.Tick += (_, _) =>
        {
            Cancel(t.Key);
            if (!last.TryGetValue(t.Key, out var now) || now.Waiting) return; // done already, or asking
            Show(new Notice("start", Head(now.Tool, Tr("진행 중", "Working")), now.Title, now.Prompt is { } p ? $"“{p}”" : null, Click: Jump(now)));
        };
        starting[t.Key] = wait;
        wait.Start();
    }

    private static void Cancel(string key)
    {
        if (starting.Remove(key, out var t)) t.Stop();
    }

    private static void Done(AgentTask t, TimeSpan took)
    {
        Cancel(t.Key);
        window?.Remove(t.Key);
        if (!Settings.Current.Notices || took < ShortestDone) return;
        Card(Settings.Current.NoticeDone, new Notice("done", Head(t.Tool, Tr("작업 완료", "Done")), t.Title, Tr($"소요 시간 {Span(took)}", $"Elapsed: {Span(took)}"), Click: Jump(t)), true);
    }

    /// The click of a turn's card: to the window its CLI runs in. A headless run (a program's `claude -p`, `codex exec`)
    /// has no session window, and a turn whose process isn't known only closes.
    private static Action? Jump(AgentTask t) => t.Pid is { } pid && !t.Headless ? () => _ = Raise(pid) : null;

    private static async Task Raise(int pid)
    {
        try
        {
            await Native.SessionWindow.Raise(pid); // none found: the card just closes
        }
        catch (Exception e)
        {
            Log.Error("notice jump", e);
        }
    }

    /// 한도 임박 at 20 % left, 소진 when used up, 복구 when a low one is back after its reset time (the usage shows a
    /// passed reset as 0 % at once, so this needs no new look).
    private static void Limits()
    {
        var s = Settings.Current;
        var all = AiUsage.Accounts;
        foreach (var a in all)
            foreach (var l in a.Shown)
            {
                string key = a.Id + "|" + l.Title;
                double left = Math.Max(0, 100 - l.Percent);
                int level = l.Percent >= 99.5 ? 2 : left <= LowLeft ? 1 : 0;
                bool had = levels.TryGetValue(key, out var before);
                levels[key] = (level, l.Resets ?? before.resets);
                if (!had || level == before.level) continue;
                string who = Who(a, all);
                if (level == 0)
                {
                    if (before.resets is { } r && DateTime.Now >= r.AddMinutes(-1))
                        Card(s.NoticeRecover, new Notice("recover", Head(who, Tr("한도 복구", "Limit reset")), Tr($"{l.Title} 한도 초기화 완료", $"{l.Title} limit reset"), null), true);
                    continue;
                }
                if (level < before.level) continue;
                Card(s.NoticeLimit, level == 2
                    ? new Notice("limit", Head(who, Tr("한도 소진", "Limit used up")), Tr($"{l.Title} 한도 소진", $"{l.Title} limit used up"), ResetsIn(l))
                    : new Notice("limit", Head(who, Tr("한도 임박", "Limit near")), Tr($"{l.Title} 한도 {Math.Round(left)}% 남음", $"{l.Title} limit · {Math.Round(left)}% left"), ResetsIn(l)), false);
            }
    }

    /// A card of a kind with its own switch in 설정 > 알림 (`on`); 알림 센터 keeps it either way (`ok`: how it reads there).
    private static void Card(bool on, Notice n, bool ok)
    {
        NoticeLog.Add(ok, n.Head, n.Title, n.Detail, null, open: n.Click);
        if (on) Show(n);
    }

    /// "40분 뒤 초기화" for a limit whose reset is ahead.
    private static string? ResetsIn(UsageLimit l) => l.Resets is { } at && at > DateTime.Now ? Tr($"{Span(at - DateTime.Now)} 뒤 초기화", $"Resets in {Span(at - DateTime.Now)}") : null;

    /// The account as the cards name it: "Claude Ultra", and whose when one kind has several.
    private static string Who(UsageAccount a, List<UsageAccount> all) => (a.Plan is { Length: > 0 } plan ? $"{a.Name} {plan}" : a.Name) + (AiUsage.Who(a, all) is { } whose ? $" · {whose}" : "");

    /// The end of something the user started in Bloom (a hidden 내 명령 script, a chain with skipped lines, a file moved
    /// or copied): shown whether or not 작업 알림 is on, as it answers the user's own action.
    /// `click`: what a click on the card itself does (an AI command's result: its chat). `keep`: 알림 센터 keeps it too
    /// (내 명령's results, with the chat `thread` an AI command's answer went into).
    public static void Result(bool ok, string head, string title, string? detail, string? actionText = null, Action? action = null, Action? click = null, bool keep = false, string? thread = null)
    {
        QaResults?.Add((ok ? "✓ " : "✕ ") + title + (detail is null ? "" : " · " + detail));
        if (keep) NoticeLog.Add(ok, head, title, detail, thread, open: thread is null ? action : null);
        Show(new Notice(ok ? "done" : "limit", head, title, detail, ActionText: actionText, Action: action, Click: click));
    }

    /// QA runs (`--qa-set datawipe`): the result cards' lines, in order.
    internal static List<string>? QaResults;

    /// A new Bloom is downloaded (Updates): it goes in at the next start, or now with 다시 시작해서 업데이트 (the same as
    /// 설정 > 정보's); a card without a sound.
    public static void UpdateReady(string version)
    {
        var choices = new List<(string Text, Action Run)>();
        if (UpdateNow is { } now) choices.Add((Tr("다시 시작해서 업데이트", "Restart to update"), now));
        if (OpenUpdates is { } open) choices.Add((Tr("업데이트 설정", "Update settings"), open));
        Show(new Notice("done", Tr("Bloom · 업데이트", "Bloom · Update"), Tr($"새 버전 {version} 다운로드 완료", $"Version {version} downloaded"),
            Tr("다음 실행 시 적용", "Applies at next start"), Buttons: choices), sound: false);
    }

    /// A Bloom chat's AI waits for a yes or no while no Bloom window is in front.
    /// Something finished, with buttons under it (화면 녹화: 열기 · 폴더에서 보기).
    public static void Done(string head, string title, string? detail, IReadOnlyList<(string Text, Action Run)> buttons)
    {
        QaResults?.Add("✓ " + title + (detail is null ? "" : " · " + detail));
        Show(new Notice("done", head, title, detail, Buttons: buttons));
    }

    public static void Ask(string what, Action? action = null) => Show(new Notice("waiting", Tr("Bloom AI · 승인 대기", "Bloom AI · Approval pending"), what, Tr("허용 또는 거절을 선택하세요", "Choose Allow or Decline"), ActionText: Tr("AI 채팅 열기", "Open AI chat"), Action: action));

    /// Claude Code asks through Bloom's hook (ClaudeApprovals): its card, in place of the session's 승인 대기 card. False
    /// when no card can show now (알림 off, a full-screen app in front): the terminal asks.
    public static bool Approval(string session, Notice n)
    {
        if (!Settings.Current.Notices || (!Quiet && UserBusy())) return false;
        approving[session] = approving.GetValueOrDefault(session) + 1;
        Settled(session); // this card takes the session's 승인 대기 place
        if (n.Key is { } key) NoticeLog.Wait(key, n.Head, n.Title, n.Detail ?? n.Body, n.Click);
        Show(n, asked: true);
        return true;
    }

    /// The approval was answered or given up: its card goes.
    public static void Approved(string session, string key)
    {
        if (approving.TryGetValue(session, out int open) && open > 1) approving[session] = open - 1;
        else approving.Remove(session);
        Settled(key);
    }

    /// A card (and its sound) — not over a full-screen game or presentation, unless asked for (미리 보기).
    private static bool Show(Notice n, bool sound = true, bool asked = false)
    {
        if (!asked && !Quiet && UserBusy()) return false;
        if (window is null)
        {
            window = new NoticeWindow { Offscreen = Quiet };
            if (holding) window.Hold(true); // the place is being picked: the new card waits with the others
        }
        bool stayed = window.Add(n);
        if (stayed && sound && !Quiet) Chimes.Play(n.Kind);
        return stayed;
    }

    private static string Head(string who, string what) => $"{who} · {what}";

    /// "40초", "12분", "3시간 5분", "2일 4시간".
    public static string Span(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return Tr($"{Math.Max(1, (int)t.TotalSeconds)}초", $"{Math.Max(1, (int)t.TotalSeconds)} s");
        if (t.TotalHours < 1) return Tr($"{(int)t.TotalMinutes}분", $"{(int)t.TotalMinutes} min");
        if (t.TotalDays < 1) return t.Minutes > 0 ? Tr($"{(int)t.TotalHours}시간 {t.Minutes}분", $"{(int)t.TotalHours} h {t.Minutes} min") : Tr($"{(int)t.TotalHours}시간", $"{(int)t.TotalHours} h");
        return t.Hours > 0 ? Tr($"{(int)t.TotalDays}일 {t.Hours}시간", $"{(int)t.TotalDays} d {t.Hours} h") : Tr($"{(int)t.TotalDays}일", $"{(int)t.TotalDays} d");
    }
}
