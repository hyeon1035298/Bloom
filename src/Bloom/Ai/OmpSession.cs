using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// One long-lived `omp --mode rpc` process per conversation: omp keeps the context (nothing is re-sent each turn) and saves
/// it in a session of Bloom's own folder, so a closed chat resumes by id. Tools: web search at most (Settings.OmpSearch), and
/// MCP tools from the conversation's `.omp/mcp.json` (OmpMcp): Bloom's PC tool and the servers switched on in
/// 설정 > PC 작업 · MCP; no file or command tool of omp's own. omp can't ask the user (--no-ui), so Bloom asks in the chat
/// (Ask), as it does for Codex. The system prompt is Bloom's, replacing omp's (OmpCli).
/// Models are "provider/model" strings, the level is omp's --thinking. All events are raised on the reader thread.
internal sealed class OmpSession : IChatSession, IAsksInChat, IContextGauge
{
    /// Bloom's MCP server for omp chats (Host sets it): the command that relays to its pipe; null when omp chats don't get it.
    public static Func<(string command, string pipe)?>? PcTools;

    /// The other MCP servers switched on (read off the UI thread) and Bloom's relay that asks in the chat before each of
    /// their calls (Host).
    public static Func<Func<List<McpServerDef>>?>? PrepareMcp;
    public static Func<(string command, string pipe)>? Relay;

    private Process? proc;
    private QueuedSessionInput? stdin;
    private readonly object gate = new();
    private readonly InputDeliveryWatchdog interruptWatch = new();
    private int counter;
    private long turn;
    private bool ending, disposed;

    public string Model { get; }
    public string Effort { get; }
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;
    public bool Alive => proc is { HasExited: false };

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<string?>? Status;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string>? Exited;

    public event Action<string>? AssistantText { add { } remove { } } // omp streams deltas

    // ── the model's memory: how full it is, and omp's tidying of it ─────────────────────────────
    public OmpUsage? Usage { get; private set; }
    public bool Compacting { get; private set; }
    public event Action? UsageChanged;
    public event Action<string, bool>? CompactNote;

    private bool compactTurn; // the turn is a "/compact" typed in the chat: the end of the tidy-up ends it
    private bool announced; // SessionStarted raised for this process
    private bool tidyWaiting; // a tidy-up is over and waits for the numbers after it to close its line
    private long tidiedFrom = -1; // its tokens before (-1 = unknown)
    private string tidiedAction = "";
    private readonly StringBuilder local = new(); // what a slash command printed this turn

    // omp can't ask the user (--no-ui): Bloom's MCP server and proxy ask here, in this conversation's chat
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;
    private PendingToolAsks? asks;

    private readonly string conversation; // the id the MCP relays name, so Bloom asks in this chat

    public OmpSession(string model, string effort, string conversation)
    {
        Model = model;
        Effort = effort;
        this.conversation = conversation;
    }

    /// An action Bloom's MCP server holds for the user: the chat shows the card, the call ends with the answer — or no,
    /// when the turn ends, it is stopped, or nobody answers within 9 minutes (the server's call timeout is 10).
    public Task<bool> Ask(string tool, string input)
    {
        lock (gate)
        {
            if (!Alive || !Busy || asks is null) return Task.FromResult(false);
            var ask = new ToolAsk(Guid.NewGuid().ToString("N"), tool, input, "");
            var done = asks.Add(ask.RequestId);
            if (!done.IsCompleted) ToolAsked?.Invoke(ask);
            return done;
        }
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        lock (gate) asks?.Answer(ask.RequestId, allow);
    }

    /// The chat's instructions: Bloom's own, for a conversation (no files, no commands), with files the user asks for as fenced blocks.
    public static string SystemPrompt(bool search, bool pc = false) => ClaudeSession.SystemPrompt(null,
        "You are the assistant in a chat inside Bloom, a Windows launcher the user opens with the Win key. "
        + "This is a conversation, not a coding task: you have no tools of your own for files or commands" + (search ? " besides web search" : "") + ". "
        + OmpMcp.Prompt(pc)
        + "Files the user attached come in the message: text files inside <attachment name=\"…\"> blocks, pictures as images. "
        + "When the user asks for a file, put its whole content in a fenced code block whose info string is file:<name> "
        + "(for example ```file:summary.md), one block per file; Bloom saves each as a file the user can open or save. ",
        false, Settings.Current.AiTitles, search);

    private sealed record Startup(string? Cli, string Slug, string Prompt, bool Search, bool Pc,
        Func<List<McpServerDef>>? ReadMcp, (string command, string pipe)? PcServer, (string command, string pipe)? Relay, string? Resume);

    public bool Start(string? resume, out string? error)
    {
        var result = StartAsync(resume, CancellationToken.None).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        // read on the caller's thread (the UI's): the pipes of Bloom's MCP server start here, the settings are read here
        var s = Settings.Current;
        var pc = s.AiToolsOmp ? PcTools?.Invoke() : null;
        var reader = PrepareMcp?.Invoke();
        var relay = reader is null ? null : Relay?.Invoke();
        var plan = new Startup(s.OmpPath, AiModels.OmpSlug(Model) ?? Model, SystemPrompt(s.OmpSearch, pc is not null), s.OmpSearch, pc is not null, reader, pc, relay, resume);
        return Task.Run(() =>
        {
            bool ok = StartPrepared(plan, cancellation, out string? error);
            return new SessionStart(ok, error);
        }, cancellation);
    }

    private bool StartPrepared(Startup plan, CancellationToken cancellation, out string? error)
    {
        error = null;
        cancellation.ThrowIfCancellationRequested();
        string? exe = OmpCli.FindCli(plan.Cli);
        if (exe is null)
        {
            error = AiErrors.OmpMissing;
            return false;
        }
        // this conversation's MCP tools: its own working folder holds the .omp/mcp.json that names it (OmpMcp)
        string? mcp = OmpMcp.Config(conversation, plan.PcServer, plan.ReadMcp?.Invoke() ?? new(), plan.Relay);
        string work = OmpMcp.Prepare(conversation, mcp);
        var psi = OmpCli.Psi(exe, work);
        bool keep = plan.Resume is { Length: > 0 } && OmpCli.HasSession(plan.Resume);
        foreach (string a in OmpCli.ChatArgs(plan.Slug, Effort, plan.Prompt, OmpCli.SessionDir(), keep ? plan.Resume : null, plan.Search))
            psi.ArgumentList.Add(a);
        Process? launched = null;
        QueuedSessionInput? launchedInput = null;
        bool handedOff = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var p = launched = ChildProcess.Spawn(psi);
            if (p is null)
            {
                error = AiErrors.OmpStartFailed;
                return false;
            }
            var input = launchedInput = new QueuedSessionInput(p.StandardInput, lines: true, e =>
            {
                Log.Error("omp stdin", e);
                try { p.Kill(true); }
                catch { /* the reader reports a process that already ended */ }
            });
            bool publish;
            lock (gate)
            {
                publish = !ending && !cancellation.IsCancellationRequested;
                if (publish)
                {
                    proc = p;
                    stdin = input;
                    SessionId = keep ? plan.Resume : null;
                }
            }
            if (!publish)
            {
                error = AiErrors.Stopped;
                return false;
            }
            var stderr = new StringBuilder();
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (stderr)
                {
                    if (stderr.Length < 4000) stderr.AppendLine(e.Data);
                }
            };
            p.BeginErrorReadLine();
            new Thread(() => ReadLoop(p, stderr, input)) { IsBackground = true, Name = "omp-out" }.Start();
            handedOff = true;
            Write(new { id = "state", type = "get_state" }); // the session's id, to resume it later
            Log.Write($"omp: started pid {p.Id} model {(plan.Slug.Length > 0 ? plan.Slug : "(default)")} effort {Effort} resume {(keep ? plan.Resume : "-")}");
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error("omp start", e);
            error = AiErrors.OmpStartFailed;
            return false;
        }
        finally
        {
            if (!handedOff)
            {
                lock (gate)
                {
                    if (ReferenceEquals(proc, launched)) proc = null;
                    if (ReferenceEquals(stdin, launchedInput)) stdin = null;
                }
                launchedInput?.Dispose();
                try { launched?.Kill(true); }
                catch { /* startup failed or was cancelled */ }
                launched?.Dispose();
            }
        }
    }

    /// One rpc command line; false when the session has no process to take it.
    private bool Write(object command)
    {
        lock (gate)
        {
            if (stdin is null || !Alive) return false;
            return stdin.Enqueue(_ => JsonSerializer.Serialize(command));
        }
    }

    public bool Send(string text, IReadOnlyList<string> images)
    {
        if (OmpCli.CompactCommand(text) is { } how)
        {
            lock (gate)
            {
                if (stdin is null || !Alive || Busy || Compacting) return false;
                Busy = true;
                compactTurn = true;
                LastUsed = DateTime.UtcNow;
            }
            if (BeginCompact(how)) return true;
            Busy = false;
            compactTurn = false;
            return false;
        }
        lock (local) local.Clear();
        var pics = images.ToArray();
        lock (gate)
        {
            if (stdin is null || !Alive || Busy) return false;
            interruptWatch.Cancel();
            turn++;
            asks?.Dispose();
            asks = new PendingToolAsks(id => ToolAskWithdrawn?.Invoke(id));
            Busy = true;
            LastUsed = DateTime.UtcNow;
            string id = "p" + Interlocked.Increment(ref counter);
            if (stdin.Enqueue(token =>
            {
                var list = new List<object>();
                foreach (string image in pics)
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(image) && Chats.ImageType(image) is { } type)
                        list.Add(new { type = "image", data = Convert.ToBase64String(File.ReadAllBytes(image)), mimeType = type });
                }
                return JsonSerializer.Serialize(list.Count > 0 ? new { id, type = "prompt", message = text, images = list.ToArray() } : (object)new { id, type = "prompt", message = text });
            })) return true;
            Busy = false;
            return false;
        }
    }

    public bool Compact(string instructions)
    {
        lock (gate)
        {
            if (stdin is null || !Alive || Busy || Compacting) return false;
        }
        return BeginCompact(instructions);
    }

    private bool BeginCompact(string instructions)
    {
        Compacting = true;
        bool sent = Write(instructions.Length > 0 ? new { id = "compact", type = "compact", customInstructions = instructions } : (object)new { id = "compact", type = "compact" });
        if (!sent)
        {
            Compacting = false;
            return false;
        }
        CompactNote?.Invoke(OmpEvent.CompactStartText(""), true);
        UsageChanged?.Invoke();
        return true;
    }

    public void RefreshUsage() => Write(new { id = "usage", type = "get_state" });

    /// A tidy-up (automatic or asked for) is over and the numbers after it are in: the finished line.
    private void TidyDone(string text)
    {
        tidyWaiting = false;
        if (compactTurn)
        {
            compactTurn = false;
            Busy = false;
            LastUsed = DateTime.UtcNow;
            TextDelta?.Invoke(text);
            TurnEnded?.Invoke(true, null);
        }
        else CompactNote?.Invoke(text, false);
    }

    public void Interrupt()
    {
        lock (gate)
        {
            asks?.Dispose();
            if (stdin is null || !Alive || !Busy) return;
            var input = stdin;
            var process = proc!;
            long version = turn;
            var delivered = input.EnqueueTracked(_ => JsonSerializer.Serialize(new { id = "stop", type = "abort" }));
            _ = interruptWatch.Watch(delivered, () =>
            {
                lock (gate)
                {
                    if (!Busy || turn != version || !ReferenceEquals(stdin, input) || !ReferenceEquals(proc, process)) return;
                    // a stop that could not be delivered: retire this process before releasing the gate, so no new turn starts on it
                    ending = true;
                    input.Dispose();
                }
                try { process.Kill(true); }
                catch { /* the reader reports an already-ended process */ }
            });
        }
    }

    private void ReadLoop(Process p, StringBuilder stderr, QueuedSessionInput input)
    {
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                try
                {
                    Handle(OmpEvent.Parse(line));
                }
                catch (Exception e)
                {
                    Log.Error("omp line", e);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("omp read", e);
        }
        input.Dispose();
        interruptWatch.Cancel();
        lock (gate) asks?.Dispose(); // the process is gone: nothing it asked can still be answered
        string err;
        lock (stderr) err = stderr.ToString().Trim();
        Compacting = false;
        if (Busy)
        {
            Busy = false;
            compactTurn = false;
            string last = err.Length > 0 ? err.Split('\n')[^1].Trim() : "";
            TurnEnded?.Invoke(false, last.Length > 0 ? Clip(err) : AiErrors.OmpNoAnswer);
        }
        Exited?.Invoke(err);
    }

    private static string Clip(string s) => s.Length > 600 ? s[..600] + "…" : s;

    private void Handle(OmpEvent e)
    {
        switch (e.Kind)
        {
            case OmpKind.Delta:
                TextDelta?.Invoke(e.Text);
                break;
            case OmpKind.Status:
                Status?.Invoke(e.Text);
                break;
            case OmpKind.StatusClear:
                Status?.Invoke(null);
                break;
            case OmpKind.SessionId:
                if (!announced || e.Text != SessionId)
                {
                    announced = true;
                    SessionId = e.Text;
                    SessionStarted?.Invoke(e.Text);
                }
                goto case OmpKind.Usage;
            case OmpKind.Usage:
                if (e.Usage is { } u) Usage = u;
                if (tidyWaiting) TidyDone(OmpEvent.CompactEndText(tidiedAction, tidiedFrom, e.Usage?.Tokens ?? -1, false, false));
                UsageChanged?.Invoke();
                break;
            case OmpKind.CompactStart:
                Compacting = true;
                CompactNote?.Invoke(OmpEvent.CompactStartText(e.Text), true);
                UsageChanged?.Invoke();
                break;
            case OmpKind.CompactEnd:
                Compacting = false;
                if (e.Detail == "done")
                {
                    tidyWaiting = true;
                    tidiedFrom = e.Before;
                    tidiedAction = e.Text;
                    RefreshUsage(); // the numbers after it close the line
                }
                else CompactNote?.Invoke(OmpEvent.CompactEndText(e.Text, -1, -1, e.Detail == "aborted", e.Detail == "skipped", e.Detail == "failed"), false);
                UsageChanged?.Invoke();
                break;
            case OmpKind.Compacted:
                Compacting = false;
                tidyWaiting = true;
                tidiedFrom = e.Before;
                tidiedAction = "";
                RefreshUsage();
                UsageChanged?.Invoke();
                break;
            case OmpKind.CompactFailed:
                Compacting = false;
                TidyDone(OmpEvent.CompactFailText(e.Text));
                UsageChanged?.Invoke();
                break;
            case OmpKind.CommandOutput:
                if (Busy && !compactTurn)
                    lock (local) local.AppendLine(e.Text);
                break;
            case OmpKind.LocalDone:
                if (Busy && !compactTurn) _ = Task.Delay(250).ContinueWith(_ => EndLocal()); // its output can come just after this answer
                break;
            case OmpKind.TurnEnded:
                interruptWatch.Cancel();
                lock (gate) asks?.Dispose();
                Busy = false;
                LastUsed = DateTime.UtcNow;
                TurnEnded?.Invoke(e.Ok, e.Ok ? null : Clip(e.Text));
                RefreshUsage();
                break;
        }
    }

    /// A slash command that ran in omp itself: what it printed is the answer, and the turn is over.
    private void EndLocal()
    {
        if (!Busy || compactTurn) return;
        string said;
        lock (local)
        {
            said = local.ToString().Trim();
            local.Clear();
        }
        Busy = false;
        LastUsed = DateTime.UtcNow;
        TextDelta?.Invoke(said.Length > 0 ? "```\n" + said + "\n```" : Tr("명령을 실행했습니다.", "The command ran."));
        TurnEnded?.Invoke(true, null);
        RefreshUsage(); // /new and /clear change what the model holds
    }

    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Closed => closed.Task;

    public void Dispose()
    {
        bool again;
        lock (gate)
        {
            again = disposed;
            disposed = true;
            ending = true;
            interruptWatch.Cancel();
            asks?.Dispose();
            try
            {
                stdin?.Dispose();
            }
            catch
            {
                /* closing */
            }
            stdin = null;
        }
        var p = proc;
        proc = null;
        if (p is null)
        {
            if (!again) closed.TrySetResult();
            return;
        }
        Task.Run(() =>
        {
            try
            {
                if (!p.WaitForExit(3000)) p.Kill(true);
            }
            catch
            {
                /* gone */
            }
            p.Dispose();
            closed.TrySetResult();
        });
    }
}
