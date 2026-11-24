using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A session whose CLI leaves the user's yes to Bloom's MCP server (ChatGPT: Codex never asks on its own).
internal interface IAsksInChat
{
    Task<bool> Ask(string tool, string input);
}

/// The stdio ends of a running `codex app-server`, one JSON-RPC message per line each way (a test stands in for the
/// process).
internal sealed class AppServerPipe
{
    public int Id { get; init; }
    public required TextReader Output { get; init; }
    public required Func<string, CancellationToken, Task> Write { get; init; } // one message, flushed
    public required Action CloseInput { get; init; }
    public required Action Kill { get; init; } // the process and everything it started (WaitForExit tells when they're gone)
    public required Func<int, bool> WaitForExit { get; init; } // true once the process and every one it started are gone
    public Func<string> Errors { get; init; } = () => ""; // the tail of its stderr
    public Action Release { get; init; } = () => { };

    /// `codex app-server` as a child process in a job of its own (CommandJob: created suspended, joined, then resumed),
    /// so what it starts — Bloom's `--mcp` server, the relays, a command's sandbox — is in the job too: its exit is the
    /// job's last process gone, and ending it ends them all.
    public static AppServerPipe Spawn(ProcessStartInfo psi)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in psi.Environment)
            if (value is not null) env[name] = value;
        string line = McpLaunch.CommandLine(psi.FileName, string.Join(" ", psi.ArgumentList.Select(McpLaunch.Quote)));
        var job = CommandJob.Create() ?? throw new InvalidOperationException("codex app-server could not get a job of its own");
        ChildProcess p;
        try
        {
            p = ChildProcess.Start(line, psi.WorkingDirectory, env, job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
        var utf8 = new UTF8Encoding(false);
        var output = new StreamReader(p.Output, utf8, false);
        var errors = new StreamReader(p.Error, utf8, false);
        var stderr = new StringBuilder();
        new Thread(() =>
        {
            try
            {
                string? l;
                while ((l = errors.ReadLine()) is not null)
                    lock (stderr)
                    {
                        if (stderr.Length < 4000) stderr.AppendLine(l);
                    }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                /* released */
            }
        })
        { IsBackground = true, Name = "codex-app-err" }.Start();
        var input = p.Input;
        return new AppServerPipe
        {
            Id = p.Id,
            Output = output,
            Write = async (message, token) =>
            {
                await input.WriteAsync(utf8.GetBytes(message + "\n"), token).ConfigureAwait(false);
                await input.FlushAsync(token).ConfigureAwait(false);
            },
            CloseInput = () =>
            {
                try { input.Dispose(); }
                catch (IOException) { /* closed */ }
            },
            Kill = () =>
            {
                try { job.Terminate(); }
                catch (ObjectDisposedException) { /* released: nothing was left */ }
            },
            WaitForExit = ms =>
            {
                try { return job.WaitEmpty(ms); }
                catch (ObjectDisposedException) { return true; }
            },
            Errors = () =>
            {
                lock (stderr) return stderr.ToString().Trim();
            },
            Release = () =>
            {
                output.Dispose();
                errors.Dispose();
                p.Dispose();
                job.Dispose();
            },
        };
    }
}

/// ChatGPT through the user's Codex login on one long-lived `codex app-server --listen stdio://` per conversation:
/// JSON-RPC over its stdin and stdout, written against codex-cli 0.159.3 (its v2 methods). The start is initialize →
/// initialized → thread/start, or thread/resume with the saved thread id (the ids `codex exec` saved resume too); each
/// message is a turn/start whose answer streams as item/agentMessage/delta and ends with turn/completed (completed,
/// interrupted or failed); a stop is turn/interrupt. The chat is CodexSession's: read-only sandbox in Local\ai, our base
/// instructions, live web search, no environment or permission notes, Bloom's MCP server and the relayed ones (approved
/// here, asked in the chat by Bloom's server). app-server has no --ignore-user-config, so it runs in a CODEX_HOME of
/// Bloom's (CodexAppHome) holding the account's sign-in and sessions but not its config.toml. A process that won't start,
/// initialize or start its thread leaves the conversation on `codex exec` (the fallback, CodexSession). A process whose
/// account changes sign-in under it is ended (a turn on it with it; the chat's next message starts a new one), and one a
/// turn finds so is replaced first by a new one resuming the thread; a sign-in that can't be told or linked sends nothing
/// on app-server: the conversation goes on with `codex exec`.
internal sealed class CodexAppSession : IChatSession, IAsksInChat
{
    private readonly object gate = new();
    private readonly string conversation; // the relay's name for this chat, so Bloom's server knows where to ask
    private readonly Func<ProcessStartInfo, AppServerPipe?> launch;
    private readonly Func<IChatSession>? makeFallback;
    private AppServerPipe? pipe;
    private QueuedSessionInput? input;
    private IChatSession? fallback;
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> calls = new();
    private long nextCall, turn;
    private string? threadId, turnId, sessionId, turnError;
    private bool busy, ending, disposed, stopped, interruptLater, launching, wroteText, statusSinceText;
    private bool opening, lost; // the start may still fall back; the process ended meanwhile (nothing told: the fallback takes over)
    private bool revoked; // the account's sign-in changed under the current process: it is being ended
    private CodexAppHome.Ticket? ticket; // the current process's place in its folder (the sign-in it started on)
    private Plan? planned; // what the start was made with: a process replaced after a sign-in change starts with it again
    private int running; // processes launched and not yet gone
    private DateTime lastUsed = DateTime.UtcNow;
    private PendingToolAsks? asks;
    private JsonElement? usage; // the turn's token counts, as last reported
    private readonly HashSet<string> streamed = new(); // the turn's agent messages that came as deltas
    private readonly CitationFilter citations = new();
    private Timer? interruptWatch;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// The longest the start may take (initialize to the thread), and how long a stop waits for its turn to end before
    /// the process is ended instead.
    internal TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal TimeSpan FirstStartTimeout { get; init; } = TimeSpan.FromMinutes(5); // the first in its CODEX_HOME indexes the account's sessions (CodexAppHome)
    internal TimeSpan InterruptGrace { get; init; } = TimeSpan.FromSeconds(5);
    /// How long an ending process (and what it started) gets before it is ended, and how long each end is then given.
    internal int ExitGrace { get; init; } = 3000;
    internal int KillWait { get; init; } = 5000;
    /// Tests: the CLI and the working folder instead of FindCli's and Local\ai.
    internal string? CliPath { get; init; }
    internal string? WorkDir { get; init; }

    public string Model { get; }
    public string Effort { get; }
    public string? AccountHome { get; init; } // the chat account's folder (CODEX_HOME); null = Codex's default
    public string? SessionId => fallback?.SessionId ?? sessionId;
    public bool Busy => fallback?.Busy ?? busy;
    public bool Alive => fallback?.Alive ?? (!ending && pipe is not null && threadId is not null);
    public DateTime LastUsed => fallback?.LastUsed ?? lastUsed;
    /// The app-server's process id (WarmBench); null on the fallback or before the start.
    internal int? ProcessId => fallback is null ? pipe?.Id : null;
    internal bool OnFallback => fallback is not null;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<string?>? Status;
    public event Action<string>? AssistantText;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string>? Exited;
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    public CodexAppSession(string model, string effort, string conversation, Func<IChatSession>? fallback = null, Func<ProcessStartInfo, AppServerPipe?>? launch = null)
    {
        Model = model;
        Effort = effort;
        this.conversation = conversation;
        makeFallback = fallback;
        this.launch = launch ?? AppServerPipe.Spawn;
    }

    private static string Version => typeof(CodexAppSession).Assembly.GetName().Version?.ToString(3) ?? "0";

    private sealed record Plan(string? CodexPath, string Cwd, string? Slug, string Reasoning, string Instructions,
        (string command, string pipe, string hint)? Tools, (string command, string pipe)? Relay, Func<List<McpServerDef>>? ReadMcp, string? Resume);

    public bool Start(string? resume, out string? error)
    {
        var result = StartAsync(resume, CancellationToken.None).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        // what CodexSession reads for each turn is read once here, on the caller's thread
        var tools = CodexSession.PcTools?.Invoke();
        var reader = CodexSession.PrepareMcp?.Invoke();
        var relay = reader is null ? null : CodexSession.Relay?.Invoke();
        var plan = new Plan(Settings.Current.CodexPath, WorkDir ?? Path.Combine(Paths.Local, "ai"), AiModels.GptSlug(Model) ?? GptCatalog.DefaultSlug(),
            AiModels.Fit(Model, Effort), CodexSession.Instructions(tools?.hint, Settings.Current.AiTitles), tools, relay, reader, resume);
        lock (gate) planned = plan;
        return Task.Run(async () =>
        {
            string? why;
            try
            {
                var started = await Open(plan, cancellation).ConfigureAwait(false);
                if (started.Success || cancellation.IsCancellationRequested) return started;
                why = started.Error;
            }
            catch (Exception e) when (!cancellation.IsCancellationRequested)
            {
                why = e.Message;
            }
            return await FallBack(resume, why, cancellation).ConfigureAwait(false);
        }, cancellation);
    }

    /// The process, its handshake and the conversation's thread.
    private async Task<SessionStart> Open(Plan plan, CancellationToken cancellation)
    {
        string? cli = CliPath ?? CodexSession.FindCli(plan.CodexPath);
        if (cli is null) return new SessionStart(false, AiErrors.CodexMissing);
        Directory.CreateDirectory(plan.Cwd);
        _ = CodexAuth.Unsaved(AccountHome); // tokens the light way refreshed go in first (they replace auth.json, so it is linked after)
        CodexAppHome.Ticket? claimed;
        string? why;
        try
        {
            (claimed, why) = await CodexAppHome.Claim(AccountHome, Revoked, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SessionStart(false, AiErrors.Stopped);
        }
        if (claimed is null) return new SessionStart(false, "its CODEX_HOME can't be made ready: " + why);
        string codexHome = claimed.Home;
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            StandardInputEncoding = utf8,
            WorkingDirectory = plan.Cwd,
        };
        foreach (string a in new[] { "app-server", "--listen", "stdio://" }.Concat(Cli.CodexNoShell)) psi.ArgumentList.Add(a);
        Cli.CleanEnvironment(psi); // the ChatGPT login only
        psi.Environment["CODEX_HOME"] = codexHome;
        AppServerPipe? p;
        lock (gate)
        {
            if (disposed || cancellation.IsCancellationRequested)
            {
                claimed.Leave();
                return new SessionStart(false, AiErrors.Stopped);
            }
            launching = true;
            ticket = claimed;
            revoked = false;
        }
        try
        {
            p = launch(psi);
        }
        catch (Exception e)
        {
            Log.Error("codex app-server start", e);
            p = null;
        }
        QueuedSessionInput? q = null;
        bool endNow = false;
        lock (gate)
        {
            launching = false;
            if (p is not null)
            {
                running++;
                var launched = p;
                q = new QueuedSessionInput(p.Write, p.CloseInput, e =>
                {
                    Log.Error("codex app-server stdin", e);
                    launched.Kill();
                });
                pipe = p;
                input = q;
                opening = true;
                lost = false;
                endNow = revoked |= claimed.Ended; // its sign-in changed while it was launched
            }
            else
            {
                if (ReferenceEquals(ticket, claimed)) ticket = null;
                if (disposed && running == 0) closed.TrySetResult();
            }
        }
        if (p is null || q is null)
        {
            claimed.Leave();
            return new SessionStart(false, AiErrors.StartFailed);
        }
        new Thread(() => ReadLoop(p, claimed)) { IsBackground = true, Name = "codex-app-out" }.Start();
        if (endNow) p.Kill();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            bool first = !CodexAppHome.Ready(codexHome);
            if (first) Log.Write("codex app-server: first start in " + codexHome + " (Codex indexes the account's sessions once)");
            timeout.CancelAfter(first ? FirstStartTimeout : StartTimeout);
            var token = timeout.Token;
            lock (gate)
            {
                if (disposed) throw new OperationCanceledException(cancellation);
            }
            await Call("initialize", new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "bloom", ["title"] = "Bloom", ["version"] = Version } }, token).ConfigureAwait(false);
            Notify("initialized");
            var extra = plan.Relay is null ? null : plan.ReadMcp?.Invoke();
            var servers = Servers(plan.Tools, plan.Relay, extra, conversation);
            var parameters = ThreadParams(plan.Cwd, plan.Slug, plan.Reasoning, plan.Instructions, servers, plan.Resume);
            var thread = await Call(plan.Resume is null ? "thread/start" : "thread/resume", parameters, token).ConfigureAwait(false);
            string id = thread.TryGetProperty("thread", out var t) ? Str(t, "id") : "";
            if (id.Length == 0) throw new InvalidDataException("codex app-server gave no thread id");
            lock (gate)
            {
                if (disposed || cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                if (lost || (revoked |= claimed.Ended) || !ReferenceEquals(pipe, p)) throw new IOException("codex app-server ended");
                threadId = id;
                sessionId = plan.Resume; // a new thread is told with its first turn (SessionStarted), as `codex exec` did
                opening = false; // from here its end is the chat's to hear (Exited)
            }
            if (first) CodexAppHome.MarkReady(codexHome);
            Log.Write($"codex app-server: pid {p.Id} model {plan.Slug ?? "-"} effort {plan.Reasoning} thread {id} resume {plan.Resume ?? "-"}");
            return new SessionStart(true);
        }
        catch (Exception e)
        {
            Retire(p);
            if (cancellation.IsCancellationRequested) return new SessionStart(false, AiErrors.Stopped);
            string tail = p.Errors();
            return new SessionStart(false, (e is OperationCanceledException ? "codex app-server did not answer in time" : e.Message) + (tail.Length > 0 ? ": " + Trim(tail) : ""));
        }
    }

    /// The process leaves this session without a word to the chat (a failed start; the fallback takes over).
    private void Retire(AppServerPipe p)
    {
        QueuedSessionInput? q = null;
        lock (gate)
        {
            if (ReferenceEquals(pipe, p))
            {
                pipe = null;
                q = input;
                input = null;
                threadId = null;
                ticket = null; // its reader lets it go once the process is gone
                opening = false;
                FailCalls();
            }
        }
        q?.Dispose();
        p.Kill();
    }

    /// The account's sign-in changed under the process (CodexAppHome): it ends now, a turn on it with it; the chat hears of
    /// the end, and its next message starts a new process that resumes the thread.
    private void Revoked(CodexAppHome.Ticket t)
    {
        AppServerPipe? p;
        lock (gate)
        {
            if (!ReferenceEquals(ticket, t)) return; // retired: its reader is ending it already
            revoked = true;
            p = pipe;
        }
        if (p is null) return; // still launching: Open ends it once it has it
        Log.Write($"codex app-server: the sign-in pid {p.Id} started on has changed; it ends");
        p.Kill();
    }

    /// The conversation goes on with `codex exec`; its events become this session's.
    private async Task<SessionStart> FallBack(string? resume, string? why, CancellationToken cancellation)
    {
        if (makeFallback is null) return new SessionStart(false, why ?? AiErrors.StartFailed);
        Log.Write($"codex app-server: {why ?? "no start"} · this chat goes on with codex exec");
        var f = makeFallback();
        f.SessionStarted += id => SessionStarted?.Invoke(id);
        f.TextDelta += d => TextDelta?.Invoke(d);
        f.Status += s => Status?.Invoke(s);
        f.AssistantText += a => AssistantText?.Invoke(a);
        f.TurnEnded += (ok, e) => TurnEnded?.Invoke(ok, e);
        f.Exited += e => Exited?.Invoke(e);
        f.ToolAsked += a => ToolAsked?.Invoke(a);
        f.ToolAskWithdrawn += id => ToolAskWithdrawn?.Invoke(id);
        lock (gate)
        {
            if (disposed)
            {
                f.Dispose();
                return new SessionStart(false, AiErrors.Stopped);
            }
            fallback = f;
        }
        return await f.StartAsync(resume, cancellation).ConfigureAwait(false);
    }

    // ── what the thread starts with ─────────────────────────────────────────────────────────────

    /// The thread's MCP servers: Bloom's own (설정 > PC 작업) and each other server switched on behind Bloom's relay, as
    /// CodexSession gives them. Every name is taken as it is given out, so a name met again (the same server name from two
    /// sources, or one that is another's name with "_bloom" added) gets "_bloom" added until it is free: none is written over.
    internal static JsonObject Servers((string command, string pipe, string hint)? tools, (string command, string pipe)? relay,
        List<McpServerDef>? extra, string conversation)
    {
        var servers = new JsonObject();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        string Key(string name)
        {
            while (!taken.Add(name)) name += "_bloom";
            return name;
        }
        if (tools is { } t)
            servers[Key("bloom")] = Server(t.command, new JsonArray("--mcp", t.pipe, conversation), 20);
        if (extra is not null && relay is { } r)
            foreach (var d in extra.Where(d => d.Stdio && d.Name != "bloom"))
                servers[Key(d.Name)] = Server(r.command, new JsonArray("--mcp-proxy", r.pipe, conversation, d.Source, d.Name), 30);
        return servers;
    }

    private static JsonObject Server(string command, JsonArray args, int startup) => new()
    {
        ["command"] = command,
        ["args"] = args,
        ["env"] = new JsonObject { [McpKey.Variable] = McpKey.Value }, // the relay's hello carries it (McpKey)
        ["enabled"] = true,
        ["default_tools_approval_mode"] = "approve", // Bloom asks itself, in the chat
        ["tool_timeout_sec"] = 600, // a call can wait that long for the user's yes
        ["startup_timeout_sec"] = startup,
    };

    /// thread/start's (or, with `resume`, thread/resume's) parameters: CodexSession's chat. Nothing of the account's
    /// config.toml needs switching off here: it isn't read (CodexAppHome).
    internal static JsonObject ThreadParams(string cwd, string? slug, string reasoning, string instructions, JsonObject servers, string? resume)
    {
        var config = new JsonObject
        {
            ["model_reasoning_effort"] = reasoning,
            ["web_search"] = "live",
            ["include_environment_context"] = false,
            ["include_permissions_instructions"] = false,
            ["mcp_servers"] = servers,
        };
        var p = new JsonObject
        {
            ["cwd"] = cwd,
            ["sandbox"] = "read-only",
            ["approvalPolicy"] = "never",
            ["approvalsReviewer"] = "user",
            ["baseInstructions"] = instructions,
            ["config"] = config,
        };
        if (slug is { Length: > 0 }) p["model"] = slug;
        if (resume is not null)
        {
            p["threadId"] = resume;
            p["excludeTurns"] = true; // the chat has its own copy of the conversation
        }
        return p;
    }

    // ── JSON-RPC ────────────────────────────────────────────────────────────────────────────────

    private Task<JsonElement> Call(string method, JsonObject parameters, CancellationToken token)
    {
        var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;
        lock (gate)
        {
            if (input is not { } q || ending) return Task.FromException<JsonElement>(new IOException("codex app-server is not running"));
            id = ++nextCall;
            calls[id] = done;
            string line = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();
            if (!q.Enqueue(_ => line))
            {
                calls.Remove(id);
                return Task.FromException<JsonElement>(new IOException("codex app-server is not running"));
            }
        }
        if (!token.CanBeCanceled) return done.Task;
        var registration = token.Register(() =>
        {
            lock (gate) calls.Remove(id);
            done.TrySetCanceled(token);
        });
        _ = done.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return done.Task;
    }

    private void Notify(string method)
    {
        lock (gate) input?.Enqueue(_ => new JsonObject { ["method"] = method }.ToJsonString());
    }

    /// Every call still waiting fails (the process is gone or retired). Under the gate.
    private void FailCalls()
    {
        foreach (var done in calls.Values) done.TrySetException(new IOException("codex app-server ended"));
        calls.Clear();
    }

    private void ReadLoop(AppServerPipe p, CodexAppHome.Ticket claimed)
    {
        try
        {
            string? line;
            while ((line = p.Output.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try
                {
                    Handle(p, line);
                }
                catch (Exception e)
                {
                    Log.Error("codex app-server line", e);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("codex app-server read", e);
        }
        bool current, wasBusy = false, cut = false;
        PendingToolAsks? waiting = null;
        lock (gate)
        {
            current = ReferenceEquals(pipe, p);
            if (current && opening)
            {
                // ended during the start: the start fails (its calls with it) and goes on with `codex exec` (Open); the
                // chat hears nothing, so the session it holds stays the one that carries the fallback
                lost = true;
                FailCalls();
                current = false;
            }
            if (current)
            {
                ending = true;
                cut = revoked || claimed.Ended;
                input?.Dispose();
                FailCalls();
                interruptWatch?.Dispose();
                interruptWatch = null;
                wasBusy = busy;
                busy = false;
                waiting = asks;
                asks = null;
            }
        }
        bool gone = p.WaitForExit(ExitGrace);
        if (!gone) p.Kill();
        string tail = p.Errors();
        if (current)
        {
            waiting?.Dispose();
            if (wasBusy) TurnEnded?.Invoke(false, stopped ? AiErrors.Stopped : cut ? AiErrors.SignInChanged : turnError ?? (tail.Length > 0 ? Trim(tail) : AiErrors.CodexNoAnswer));
            Exited?.Invoke(tail);
        }
        // Closed waits for the process and all it started to be gone (the pre-start's cap counts them until then): ended
        // again until they are
        for (int tries = 0; !gone; tries++)
        {
            gone = p.WaitForExit(KillWait);
            if (gone) break;
            if (tries == 0) Log.Write($"codex app-server: pid {p.Id} or a process it started is still running; ending it again");
            p.Kill();
        }
        try
        {
            p.Release();
        }
        finally
        {
            claimed.Leave(); // gone: its folder may link the account's sign-in anew
        }
        lock (gate)
        {
            running--;
            if (disposed && running == 0 && !launching) closed.TrySetResult();
        }
    }

    private static string Trim(string s) => s.Length > 600 ? s[^600..] : s;

    private void Handle(AppServerPipe p, string line)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        bool hasId = r.TryGetProperty("id", out var id);
        string method = Str(r, "method");
        if (method.Length == 0)
        {
            // an answer to one of our calls
            TaskCompletionSource<JsonElement>? done = null;
            lock (gate)
            {
                if (hasId && id.TryGetInt64(out long n) && ReferenceEquals(pipe, p)) calls.Remove(n, out done);
            }
            if (done is null) return;
            if (r.TryGetProperty("error", out var error)) done.TrySetException(new InvalidOperationException(Str(error, "message") is { Length: > 0 } m ? m : error.GetRawText()));
            else done.TrySetResult(r.TryGetProperty("result", out var result) ? result.Clone() : default);
            return;
        }
        if (hasId)
        {
            // the server asks something of us (an approval, a form): with approvals off nothing should; refused, so it never waits
            var refusal = new JsonObject { ["id"] = JsonNode.Parse(id.GetRawText()), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Not supported by Bloom." } }.ToJsonString();
            lock (gate)
            {
                if (ReferenceEquals(pipe, p)) input?.Enqueue(_ => refusal);
            }
            return;
        }
        if (!r.TryGetProperty("params", out var n2) || n2.ValueKind != JsonValueKind.Object) return;
        lock (gate)
        {
            if (!ReferenceEquals(pipe, p) || (Str(n2, "threadId") is { Length: > 0 } thread && thread != threadId)) return;
            Notification(method, n2);
        }
    }

    /// A notification about this conversation's thread. Under the gate.
    private void Notification(string method, JsonElement n)
    {
        switch (method)
        {
            case "turn/started":
                if (busy && turnId is null && n.TryGetProperty("turn", out var started)) Begun(Str(started, "id"));
                break;
            case "item/agentMessage/delta":
                if (!busy || !SameTurn(n)) break;
                string item = Str(n, "itemId");
                string text = Str(n, "delta");
                if (streamed.Add(item))
                {
                    citations.Reset();
                    // a second message in the turn starts a paragraph of its own (after a tool's status the chat adds it)
                    if (wroteText && !statusSinceText) text = "\n\n" + text;
                }
                text = citations.Take(text);
                if (text.Length == 0) break;
                wroteText = true;
                statusSinceText = false;
                TextDelta?.Invoke(text);
                break;
            case "item/started":
                if (!busy || !SameTurn(n) || !n.TryGetProperty("item", out var it)) break;
                string? status = Str(it, "type") switch
                {
                    "reasoning" => Tr("생각하는 중…", "Thinking…"),
                    "webSearch" => Tr("웹 검색 중…", "Searching the web…"),
                    "commandExecution" or "fileChange" => Tr("파일 다루는 중…", "Handling files…"),
                    "mcpToolCall" when Str(it, "server") == "bloom" => Tr("PC에서 작업하는 중…", "Working on PC…"),
                    "mcpToolCall" or "dynamicToolCall" => Tr("도구 쓰는 중…", "Using tools…"),
                    "agentMessage" => null,
                    _ => "",
                };
                if (status is { Length: 0 }) break; // the user's own message and the like: the status stays
                if (status is not null) statusSinceText = true;
                Status?.Invoke(status);
                break;
            case "item/completed":
                // a message that came whole, without deltas
                if (busy && SameTurn(n) && n.TryGetProperty("item", out var done) && Str(done, "type") == "agentMessage" && !streamed.Contains(Str(done, "id"))
                    && CodexSession.Clean(Str(done, "text")) is { Length: > 0 } whole)
                {
                    streamed.Add(Str(done, "id"));
                    wroteText = true;
                    statusSinceText = false;
                    Status?.Invoke(null);
                    AssistantText?.Invoke(whole);
                }
                break;
            case "mcpServer/startupStatus/updated":
                if (Str(n, "status") == "failed") Log.Write($"codex app-server: MCP server {Str(n, "name")} failed: {Str(n, "error")}");
                break;
            case "thread/tokenUsage/updated":
                if (busy && SameTurn(n) && n.TryGetProperty("tokenUsage", out var tokens) && tokens.TryGetProperty("last", out var last)) usage = last.Clone();
                break;
            case "error":
                // a failure the turn reports before it ends; one Codex retries by itself is not the answer's end
                if (busy && SameTurn(n) && !(n.TryGetProperty("willRetry", out var retry) && retry.ValueKind == JsonValueKind.True) && n.TryGetProperty("error", out var err))
                    turnError = Describe(err);
                break;
            case "turn/completed":
                if (!busy || !n.TryGetProperty("turn", out var ended) || (turnId is not null && Str(ended, "id") != turnId)) break;
                Announce();
                busy = false;
                lastUsed = DateTime.UtcNow;
                interruptWatch?.Dispose();
                interruptWatch = null;
                asks?.Dispose();
                asks = null;
                if (usage is { } u)
                    Log.Write($"codex turn: in {Num(u, "inputTokens")} · cached {Num(u, "cachedInputTokens")} · out {Num(u, "outputTokens")} · reasoning {Num(u, "reasoningOutputTokens")}");
                switch (Str(ended, "status"))
                {
                    case "completed":
                        TurnEnded?.Invoke(true, null);
                        break;
                    case "interrupted":
                        TurnEnded?.Invoke(false, AiErrors.Stopped);
                        break;
                    default:
                        string? why = ended.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? Describe(e) : null;
                        TurnEnded?.Invoke(false, why ?? turnError ?? Tr("Codex 오류", "Codex error"));
                        break;
                }
                turnId = null;
                break;
        }
    }

    private bool SameTurn(JsonElement n) => turnId is null || Str(n, "turnId") is not { Length: > 0 } t || t == turnId;

    /// A TurnError in words AiErrors.Friendly knows (a usage limit, a sign-in), its details under it.
    internal static string Describe(JsonElement error)
    {
        string message = Str(error, "message");
        string info = Str(error, "codexErrorInfo");
        if (info is "usageLimitExceeded" or "rateLimitExceeded" && !message.Contains("limit", StringComparison.OrdinalIgnoreCase)) message = "Usage limit reached. " + message;
        else if (info == "unauthorized" && !message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)) message += " (unauthorized)";
        if (Str(error, "additionalDetails") is { Length: > 0 } more) message += "\n" + more;
        return message.Trim().Length > 0 ? message.Trim() : Tr("Codex 오류", "Codex error");
    }

    // ── turns ────────────────────────────────────────────────────────────────────────────────────

    public bool Send(string text, IReadOnlyList<string> images) =>
        fallback is { } f ? f.Send(text, images) : SendAsync(text, images, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<bool> SendAsync(string text, IReadOnlyList<string> images, CancellationToken cancellation)
    {
        if (fallback is null && !await OnItsSignIn(cancellation).ConfigureAwait(false)) return false;
        if (fallback is { } f) return await f.SendAsync(text, images, cancellation).ConfigureAwait(false);
        return !cancellation.IsCancellationRequested && SendTurn(text, images);
    }

    /// Before a turn: the process is still on the sign-in it started on (CodexAppHome.Ticket.Holds), or a new one takes its
    /// place first — the old ends and is gone before the account's file is linked again — resuming the thread. When the
    /// sign-in can't be told or linked, the turn goes to `codex exec` instead: nothing is sent on unknown credentials.
    private async Task<bool> OnItsSignIn(CancellationToken cancellation)
    {
        CodexAppHome.Ticket? t;
        AppServerPipe? old;
        Plan? again;
        string? resume;
        lock (gate)
        {
            if (!Alive || busy || disposed) return true; // SendTurn says no
            t = ticket;
            old = pipe;
            again = planned;
            resume = sessionId;
        }
        if (t is null || old is null || again is null || await Task.Run(t.Holds, CancellationToken.None).ConfigureAwait(false)) return true;
        Log.Write($"codex app-server: the sign-in pid {old.Id} started on has changed; a new process takes over thread {resume ?? "(new)"}");
        Retire(old);
        var started = await Open(again with { Resume = resume }, cancellation).ConfigureAwait(false);
        if (started.Success) return true;
        if (cancellation.IsCancellationRequested) return false;
        return (await FallBack(resume, started.Error, cancellation).ConfigureAwait(false)).Success;
    }

    private bool SendTurn(string text, IReadOnlyList<string> images)
    {
        var content = new JsonArray();
        foreach (string image in images)
            if (File.Exists(image)) content.Add(new JsonObject { ["type"] = "localImage", ["path"] = image });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        long version;
        Task<JsonElement> call;
        lock (gate)
        {
            if (!Alive || busy || disposed || threadId is null) return false;
            busy = true;
            stopped = interruptLater = wroteText = statusSinceText = false;
            turnId = turnError = null;
            usage = null;
            streamed.Clear();
            lastUsed = DateTime.UtcNow;
            version = ++turn;
            asks = new PendingToolAsks(id => ToolAskWithdrawn?.Invoke(id));
            call = Call("turn/start", new JsonObject { ["threadId"] = threadId, ["input"] = content }, CancellationToken.None);
            if (call.IsFaulted)
            {
                busy = false;
                asks.Dispose();
                asks = null;
                return false;
            }
        }
        _ = call.ContinueWith(c =>
        {
            lock (gate)
            {
                if (turn != version || !busy) return;
                if (c.IsCompletedSuccessfully)
                {
                    if (turnId is null) Begun(c.Result.TryGetProperty("turn", out var t) ? Str(t, "id") : "");
                    return;
                }
                busy = false;
                asks?.Dispose();
                asks = null;
                TurnEnded?.Invoke(false, stopped ? AiErrors.Stopped : c.Exception?.InnerException?.Message ?? AiErrors.CodexNoAnswer);
            }
        }, TaskScheduler.Default);
        return true;
    }

    /// The turn has its id: the thread is told (Announce), and a stop that came first goes out. Under the gate.
    private void Begun(string id)
    {
        if (id.Length == 0) return;
        turnId = id;
        Announce();
        if (interruptLater) SendInterrupt();
    }

    /// A new thread is told with its first turn: it exists on disk from here (a thread without one can't be resumed). Under the gate.
    private void Announce()
    {
        if (sessionId is not null || threadId is not { } thread) return;
        sessionId = thread;
        SessionStarted?.Invoke(thread);
    }

    public void Interrupt()
    {
        if (fallback is { } f)
        {
            f.Interrupt();
            return;
        }
        lock (gate)
        {
            asks?.Dispose();
            if (!busy || ending) return;
            stopped = true;
            if (turnId is null) interruptLater = true;
            else SendInterrupt();
            // a turn that doesn't end on its own soon ends with its process (the next message starts a new one)
            long version = turn;
            var process = pipe;
            interruptWatch?.Dispose();
            interruptWatch = new Timer(_ =>
            {
                lock (gate)
                {
                    if (!busy || turn != version || !ReferenceEquals(pipe, process)) return;
                    ending = true;
                }
                process?.Kill();
            }, null, InterruptGrace, Timeout.InfiniteTimeSpan);
        }
    }

    /// turn/interrupt for the turn going on. Under the gate.
    private void SendInterrupt()
    {
        interruptLater = false;
        if (threadId is null || turnId is null) return;
        _ = Call("turn/interrupt", new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId }, CancellationToken.None)
            .ContinueWith(c => Log.Error("codex interrupt", c.Exception!), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// An action Bloom's MCP server holds for the user (CodexSession.Ask): the card shows, the task ends with the answer, or
    /// no when the turn ends, it is stopped, or nobody answers within 9 minutes.
    public Task<bool> Ask(string tool, string input)
    {
        if (fallback is IAsksInChat f) return f.Ask(tool, input);
        lock (gate)
        {
            if (!Alive || !busy || asks is null) return Task.FromResult(false);
            var ask = new ToolAsk(Guid.NewGuid().ToString("N"), tool, input, "");
            var done = asks.Add(ask.RequestId);
            if (!done.IsCompleted) ToolAsked?.Invoke(ask);
            return done;
        }
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        if (fallback is { } f)
        {
            f.Answer(ask, allow);
            return;
        }
        lock (gate) asks?.Answer(ask.RequestId, allow);
    }

    /// Completes once the app-server process and every process it started are gone (closing its stdin ends it; it is
    /// ended after 3 s, and again until it is gone).
    public Task Closed => closed.Task;

    public void Dispose()
    {
        IChatSession? f;
        AppServerPipe? p;
        QueuedSessionInput? q;
        lock (gate)
        {
            if (disposed) return;
            disposed = ending = true;
            f = fallback;
            p = pipe;
            q = input;
            interruptWatch?.Dispose();
            interruptWatch = null;
            asks?.Dispose();
            asks = null;
            if (running == 0 && !launching) closed.TrySetResult();
        }
        f?.Dispose();
        q?.Dispose(); // app-server ends when its input closes
        if (p is not null)
            _ = Task.Run(() =>
            {
                if (!p.WaitForExit(ExitGrace)) p.Kill(); // one that keeps running with its input closed (its reader then ends it)
            });
    }

    private static long Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt64(out long n) ? n : 0;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// Web-search citations (U+E200 … U+E201, and the spaces after) taken out of streamed text, a marker split across
    /// deltas too (CodexSession.Clean does the same to whole messages).
    internal sealed class CitationFilter
    {
        private bool inside, trimming;

        public void Reset() => inside = trimming = false;

        public string Take(string text)
        {
            if (!inside && !trimming && text.IndexOf('\uE200') < 0) return text;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (inside)
                {
                    if (c == '\uE201')
                    {
                        inside = false;
                        trimming = true;
                    }
                    continue;
                }
                if (trimming && c is ' ' or '\t') continue;
                trimming = false;
                if (c == '\uE200') inside = true;
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
