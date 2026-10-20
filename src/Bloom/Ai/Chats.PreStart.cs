using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// Pre-start. A Claude chat is one `claude -p` process, a ChatGPT one one `codex app-server` (CodexAppSession) and an omp one
/// one `omp --mode rpc` (OmpSession), each only launched with the first message (~0.9 s of CLI start before the API call;
/// omp ~1.8 s). When a conversation is likely to send —
/// it is shown, or its box is typed in — the process starts ahead, with nothing sent, under the model, level, account,
/// tools and resumed session the real send would use (app-server: initialized and its thread started or resumed, no
/// turn); the first message finds it running. Only the provider the next send would use is pre-started (Prewarm), and at
/// most WarmMax (1) such unused process exists across both; one nobody sends to ends after WarmIdle, with its chat's view
/// (CancelPreStart), or when the conversation changes model or level. A failed pre-start shows nothing: the real send
/// starts again and reports the error as it always did. An API chat has no process, so it is not pre-started.
internal sealed partial class Chats
{
    private sealed class WarmSession(IChatSession session, string? resume, string provider, string account, string print)
    {
        public readonly IChatSession Session = session;
        public readonly string? Resume = resume;
        public readonly string Provider = provider, Account = account;
        public readonly string Print = print; // what the process was launched with (LaunchPrint): a send with other inputs can't use it
        public readonly CancellationTokenSource Cancellation = new();
        public Task<SessionStart> Start = Task.FromResult(new SessionStart(false));
        public DateTime Used = DateTime.UtcNow;
        public string? InitId; // the session id Claude Code reported before any turn: kept for the real one
    }

    private readonly Dictionary<string, WarmSession> warm = new();
    private DispatcherTimer? warmTimer, warmDelay;
    private ChatThread? pendingWarm;
    private bool warmDeferred; // pendingWarm waits for dropped processes still ending (the cap counts them)
    private readonly HashSet<Task> dying = new(); // Closed of dropped pre-started sessions, until their processes are gone
    /// Off for QA pictures and demos (nothing runs there).
    public bool PreStartOn = true;
    internal int WarmMax = 1; // across all chats and providers
    internal TimeSpan WarmIdle = TimeSpan.FromMinutes(2.5);
    /// How long a request waits for the next one (typing, flipping through conversations) before a process starts.
    internal TimeSpan PreStartDelay = TimeSpan.FromMilliseconds(250);
    internal int WarmCount => warm.Count;
    internal bool IsWarm(ChatThread t) => warm.ContainsKey(t.Id);
    internal IChatSession? SessionOf(ChatThread t) => sessions.GetValueOrDefault(t.Id); // WarmBench: the process it measures

    /// UI thread. This conversation is likely to send: its process starts in the background if it isn't running. Cheap
    /// to call on every key.
    public void PreStart(ChatThread t)
    {
        if (shutdown || !PreStartOn || Covered(t)) return;
        pendingWarm = t;
        warmDeferred = false;
        if (PreStartDelay <= TimeSpan.Zero)
        {
            BeginPreStart();
            return;
        }
        warmDelay ??= NewTimer(PreStartDelay, BeginPreStart);
        warmDelay.Interval = PreStartDelay;
        warmDelay.Stop();
        warmDelay.Start();
    }

    /// UI thread. The conversation's view closed: a process nobody sent to ends (a used session stays as it was).
    public void CancelPreStart(ChatThread t)
    {
        if (ReferenceEquals(pendingWarm, t)) CancelPending();
        DropWarm(t.Id);
    }

    /// A start still waiting (for the debounce, or for a process to finish ending) doesn't happen.
    private void CancelPending()
    {
        pendingWarm = null;
        warmDeferred = false;
        warmDelay?.Stop();
    }

    /// The one place that decides which provider is pre-started for the model the next send would use: Claude (a
    /// long-lived `claude -p`) and ChatGPT (a long-lived `codex app-server`, unless the chats are on `codex exec`, which
    /// starts a process per turn), omp (a long-lived `omp --mode rpc`, ~1.8 s to start, measured). API models return null.
    private string? Prewarm(string model) => AiModels.Provider(model) switch
    {
        "claude" => "claude",
        "omp" => "omp",
        "chatgpt" when CodexAppServer => "chatgpt",
        _ => null,
    };

    private DispatcherTimer NewTimer(TimeSpan every, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = every };
        timer.Tick += (_, _) => tick();
        return timer;
    }

    /// The conversation already has a process for its model and level: a pre-started one is kept alive, a running one is enough.
    private bool Covered(ChatThread t)
    {
        if (!sessions.TryGetValue(t.Id, out var s) || s.Model != t.Model || s.Effort != EffortOf(t)) return false;
        if (!warm.TryGetValue(t.Id, out var w) || !ReferenceEquals(w.Session, s)) return s.Alive;
        if (!Usable(w) || w.Print != PrintOf(t)) return false; // dead, or the settings moved on since: it starts again
        w.Used = DateTime.UtcNow;
        return true;
    }

    /// A pre-started process a send can take over: still starting, or started and running.
    private static bool Usable(WarmSession w) => !w.Start.IsCompleted || w.Start.IsCompletedSuccessfully && w.Start.Result.Success && w.Session.Alive;

    /// What goes into the launch of a conversation's process (Claude's or Codex's) besides the message: the model, level,
    /// account and its folder, the session to resume, the CLI, Bloom's tools and permissions, and the other MCP servers
    /// switched on. A process pre-started under one print serves a send under the same print only.
    private static string PrintOf(ChatThread t)
    {
        string provider = AiModels.Provider(t.Model);
        var account = AiAccounts.Chat(provider);
        var s = Settings.Current;
        static string Sorted(IEnumerable<string> items) => string.Join(',', items.Order(StringComparer.Ordinal));
        return string.Join('\n', t.Model, EffortOf(t), account.Id, account.Home, ResumeFor(t, provider, account.Id), provider switch { "chatgpt" => s.CodexPath, "omp" => s.OmpPath, _ => s.ClaudePath },
            s.AiTools, s.AiToolsOmp, s.OmpSearch, s.AiTitles, App.Mcp.BloomTools.OfferedTo(provider), Sorted(s.AiToolsOff), Sorted(s.McpOn), Sorted(s.McpFiles),
            Sorted(s.Permissions.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));
    }

    /// The session a conversation resumes under this account (an API chat and another account's start fresh). omp has no
    /// accounts of Bloom's: its session is Bloom's own folder's, resumed while that file is there.
    private static string? ResumeFor(ChatThread t, string provider, string accountId) =>
        provider == "omp" ? t.SessionProvider == "omp" && t.SessionId is { } ompId && OmpCli.HasSession(ompId) ? ompId : null
        : provider != "api" && (t.SessionProvider ?? "claude") == provider && (t.SessionAccount ?? provider) == accountId ? t.SessionId : null;

    private void BeginPreStart()
    {
        warmDelay?.Stop();
        warmDeferred = false;
        if (pendingWarm is not { } t) return;
        pendingWarm = null;
        if (shutdown || t.Busy || preparing.ContainsKey(t.Id) || finishing.ContainsKey(t.Id) || Covered(t)) return;
        DropWarm(t.Id); // one made for another model or level
        if (Prewarm(t.Model) is null)
        {
            foreach (string id in warm.Keys.ToList()) DropWarm(id); // the provider in use has no pre-start: a Claude one no longer matches
            return;
        }
        while (warm.Count >= WarmMax) DropWarm(warm.MinBy(p => p.Value.Used).Key);
        dying.RemoveWhere(d => d.IsCompleted);
        if (warm.Count + dying.Count >= WarmMax)
        {
            // dropped processes take a moment to end: the cap counts them, so this waits until they are gone (Resume)
            pendingWarm = t;
            warmDeferred = true;
            return;
        }
        SessionFor(t, out string? resume, out _, out _, warming: true);
        var w = warm[t.Id];
        w.Start = StartWarm(t, w, resume);
        warmTimer ??= NewTimer(TimeSpan.FromSeconds(15), ReapWarm);
        warmTimer.Start();
    }

    private async Task<SessionStart> StartWarm(ChatThread t, WarmSession w, string? resume)
    {
        SessionStart result;
        try { result = await w.Session.StartAsync(resume, w.Cancellation.Token).ConfigureAwait(false); }
        catch (Exception e) { result = new SessionStart(false, e.Message); }
        if (result.Success)
        {
            if (w.Cancellation.IsCancellationRequested) w.Session.Dispose(); // ended while it was starting: it must not outlive that
            return result;
        }
        try
        {
            await OnUi(() =>
            {
                if (warm.TryGetValue(t.Id, out var current) && ReferenceEquals(current, w)) DropWarm(t.Id); // silently; the send reports it
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* closing */ }
        return result;
    }

    /// The pre-started process ends (and leaves the session list) without anything having been sent to it.
    private bool DropWarm(string id)
    {
        if (!warm.Remove(id, out var w)) return false;
        _ = w.Cancellation.CancelAsync();
        if (sessions.TryGetValue(id, out var s) && ReferenceEquals(s, w.Session)) sessions.Remove(id);
        w.Session.Dispose();
        Track(w.Session.Closed);
        return true;
    }

    /// A dropped process takes a while to end (Dispose only starts it): until it is gone it counts against the cap.
    private void Track(Task closed)
    {
        if (closed.IsCompleted) return;
        dying.Add(closed);
        _ = closed.ContinueWith(_ => ui.BeginInvoke(Resume, DispatcherPriority.Background), TaskScheduler.Default);
    }

    private void Resume()
    {
        dying.RemoveWhere(d => d.IsCompleted);
        if (warmDeferred && pendingWarm is not null) BeginPreStart();
    }

    /// The send takes over the conversation's pre-started process (null when it has none): from here it is an ordinary
    /// session, and the id it reported while waiting is kept as if it had come with the turn.
    private WarmSession? TakeWarm(ChatThread t, IChatSession session)
    {
        if (!warm.TryGetValue(t.Id, out var w) || !ReferenceEquals(w.Session, session)) return null;
        warm.Remove(t.Id);
        if (w.InitId is { } id)
        {
            t.SessionId = id;
            t.SessionProvider = w.Provider;
            t.SessionAccount = w.Account;
            Save(t);
        }
        return w;
    }

    /// A pre-started process nobody has sent to for WarmIdle ends (every 15 s).
    internal void ReapWarm()
    {
        foreach (var (id, w) in warm.ToList())
            if (!preparing.ContainsKey(id) && DateTime.UtcNow - w.Used >= WarmIdle) DropWarm(id);
        if (warm.Count == 0) warmTimer?.Stop();
    }

    private void StopPreStart()
    {
        warmTimer?.Stop();
        CancelPending();
        foreach (var w in warm.Values) _ = w.Cancellation.CancelAsync();
        warm.Clear();
    }
}
