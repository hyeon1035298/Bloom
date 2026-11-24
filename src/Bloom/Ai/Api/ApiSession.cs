using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// A conversation with an API model (ApiProviders): Bloom keeps the conversation itself and sends all of it each turn,
/// streaming the answer back as TextDelta. A turn is ApiTurn's loop: the model may call tools (ApiToolbox: Bloom's pc
/// tool, files and commands in the chat's folder, the other MCP servers switched on: ApiMcp, which the session's end and
/// a stop close), each action asked in the chat first (ToolAsked → Chats → Answer), and
/// its steps are kept with the answer (StepsDone → ChatMessage.Steps). A session started again (another model, after
/// the idle close) begins from the saved chat, tool calls and all (Past, HistoryOf). Nothing runs between turns;
/// Interrupt cancels the HTTP stream, a running command and the questions waiting. Attached files come inline; files it
/// writes come back through write_file into out\ (or as ```file: blocks, as with ChatGPT, when it has no file tools).
internal sealed class ApiSession : IChatSession
{
    private readonly object gate = new();
    private readonly List<ApiMessage> history = new();
    private IReadOnlyList<ApiPastTurn>? past; // the saved chat's turns, built into `history` by the first turn
    private readonly string conversation, unavailable, folder;
    private CancellationTokenSource? turn;
    private CancellationTokenSource? mcpLife; // the running turn's MCP lifetime: CloseMcpAll ends it, at once
    private PendingToolAsks? asks; // the running turn's questions to the user
    private readonly ApiMcp mcp = new(); // the other MCP servers, started by the first turn that offers tools
    private static readonly ConcurrentDictionary<ApiSession, byte> live = new(); // every session not yet disposed (CloseMcpAll)

    /// 권한's 다른 MCP 도구 set to 사용 안 함: every chat's MCP servers end now and a start under way is cancelled, on a
    /// worker thread (each server may take a moment). Their turns go on without MCP tools: each turn's MCP lifetime (taken
    /// at Send, before the settings are read) ends right here, on the calling thread, so a turn that has not started its
    /// servers yet never will, and one that has stops calling them.
    public static void CloseMcpAll()
    {
        foreach (var s in live.Keys)
        {
            s.EndMcpLifetime();
            _ = Task.Run(() => s.mcp.Close(true));
        }
    }

    private void EndMcpLifetime()
    {
        CancellationTokenSource? life;
        lock (gate) life = mcpLife;
        life?.Cancel();
    }

    /// The running (or last) turn's MCP lifetime token (tests).
    internal CancellationToken McpLifetime
    {
        get
        {
            lock (gate) return mcpLife?.Token ?? default;
        }
    }

    /// `past`: Past(the chat's messages); `unavailable`: the note for a file that can't be read (Chats.UnavailableNote);
    /// `folder`: the chat's own (its attachments' in\, the out\ its files go to; commands run there).
    public ApiSession(string model, string effort, string conversation, IReadOnlyList<ApiPastTurn> past, string unavailable, string folder)
    {
        Model = model;
        Effort = effort;
        this.conversation = conversation;
        this.past = past;
        this.unavailable = unavailable;
        this.folder = folder;
        live[this] = 0;
    }

    public string Model { get; }
    public string Effort { get; }
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public bool Alive { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string?>? Status;
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    /// Raised before TurnEnded with the steps the answer took (results cut to ApiStep.MaxSaved), for the chat's file.
    public event Action<List<ApiStep>>? StepsDone;

    /// Raised before TurnEnded with the tokens the provider counted for the answer.
    public event Action<ApiTokens>? Counted;

    /// A note for the chat about the turn's tools (an MCP server that did not start, tools left out).
    public event Action<string>? Noted;

    // never raised: whole answers come as deltas, no process ends
    public event Action<string>? AssistantText { add { } remove { } }
    public event Action<string>? Exited { add { } remove { } }

    /// Nothing to start: the session is marked (so a later Claude or ChatGPT session knows it hasn't seen these turns).
    public bool Start(string? resume, out string? error)
    {
        error = null;
        lock (gate) Alive = true;
        SessionId = "api-" + conversation;
        SessionStarted?.Invoke(SessionId);
        return true;
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        lock (gate) asks?.Answer(ask.RequestId, allow);
    }

    /// A question for the user in the chat; its answer, or no when the turn ends or stops first.
    private Task<bool> Ask(PendingToolAsks pending, ToolAsk ask)
    {
        var done = pending.Add(ask.RequestId);
        if (!done.IsCompleted) ToolAsked?.Invoke(ask);
        return done;
    }

    public bool Send(string text, IReadOnlyList<string> images)
    {
        var asked = new ApiMessage(ApiMessage.User, text, images.ToArray());
        CancellationTokenSource stop, life;
        lock (gate)
        {
            if (!Alive || Busy) return false;
            Busy = true;
            LastUsed = DateTime.UtcNow;
            stop = turn = new CancellationTokenSource();
            life = mcpLife = new CancellationTokenSource(); // before the settings below: a switch to 사용 안 함 after this ends it
        }
        // the settings as they are now: 설정 > PC 작업's switches and 권한 apply from the next message, as in the CLI chats
        var s = Settings.Current;
        var pc = App.Mcp.BloomTools.OfferedTo("api") ? Chats.Tools as IPcTools : null;
        var rules = new Dictionary<string, string>(s.Permissions);
        // the servers switched on (read from their programs' files on the worker); 다른 MCP 도구 사용 안 함: none at all
        var servers = ToolPermissions.Of(rules, ToolPermissions.Mcp) == ToolPermissions.Off ? null : McpCatalog.PreparePicked();
        _ = Task.Run(() => RunTurn(asked, stop, pc, rules, servers, life.Token));
        return true;
    }

    /// One turn, on a worker thread: the history built from the saved chat (the first turn: its files are read again),
    /// the other MCP servers started or closed as now switched on, the answer's steps (ApiTurn), and they into the
    /// conversation (also a partial answer, so the next turn knows what was said).
    private async Task RunTurn(ApiMessage asked, CancellationTokenSource stop, IPcTools? pc, Dictionary<string, string> rules, Func<List<McpServerDef>>? servers, CancellationToken life)
    {
        ApiReply reply;
        var steps = new List<ApiStep>();
        using var pending = new PendingToolAsks(id => ToolAskWithdrawn?.Invoke(id));
        lock (gate) asks = pending;
        try
        {
            using var stopping = stop.Token.Register(pending.Dispose); // a stop answers every waiting question no
            List<ApiMessage> sent;
            IReadOnlyList<ApiPastTurn>? earlier;
            lock (gate) (earlier, past) = (past, null);
            var built = earlier is null ? null : HistoryOf(earlier, unavailable);
            lock (gate)
            {
                if (built is not null) history.InsertRange(0, built);
                sent = [.. history, asked];
            }
            var (request, error) = await ApiClient.PrepareAsync(Model, Effort, "", sent, conversation).ConfigureAwait(false);
            if (request is null) reply = new ApiReply(false, "", error);
            else
            {
                ApiToolbox? tools = null;
                if (request.TakesTools)
                {
                    var defs = servers?.Invoke() ?? [];
                    if (defs.Count > 0) Status?.Invoke(Tr("MCP 서버 준비 중…", "Preparing MCP servers…"));
                    foreach (string note in await mcp.PrepareAsync(defs, ApiToolbox.Reserved, stop.Token, life).ConfigureAwait(false)) Noted?.Invoke(note);
                    if (defs.Count > 0) Status?.Invoke(null);
                    tools = new ApiToolbox(folder, pc, rules, ask => Ask(pending, ask), ApiToolbox.Secrets(), life.IsCancellationRequested ? null : mcp);
                }
                // fenced ```file: blocks are saved only under 파일 쓰기 (Chats.Finish), so the model is only told of them then
                request = request with { System = Instructions(tools, pc is not null && tools is not null, ToolPermissions.Of(rules, ToolPermissions.Write) != ToolPermissions.Off) };
                (reply, steps) = await ApiTurn.RunAsync(request, tools, d => TextDelta?.Invoke(d), st => Status?.Invoke(st), stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            reply = new ApiReply(false, "", AiErrors.Stopped); // stopped while the MCP servers started
        }
        catch (Exception e)
        {
            Log.Error("api turn", e);
            reply = new ApiReply(false, "", AiErrors.Failed);
        }
        lock (gate)
        {
            if (steps.Count > 0)
            {
                history.Add(asked);
                history.AddRange(steps.SelectMany(step => step.Messages()));
            }
            else if (reply.Text.Length > 0)
            {
                history.Add(asked);
                history.Add(new ApiMessage(ApiMessage.Assistant, reply.Text, Array.Empty<string>()));
            }
            asks = null;
            Busy = false;
            LastUsed = DateTime.UtcNow;
            if (ReferenceEquals(turn, stop)) turn = null;
            stop.Dispose();
        }
        if (steps.Any(step => step.Keeps)) StepsDone?.Invoke(steps.Select(Saved).ToList());
        if (reply.Tokens is { } used) Counted?.Invoke(used);
        TurnEnded?.Invoke(reply.Ok, reply.Ok ? null : reply.Error ?? AiErrors.Failed);
    }

    /// A step as the chat's file keeps it: long results cut.
    private static ApiStep Saved(ApiStep step) => step with
    {
        Calls = step.Calls.Select(c => c.Result.Length > ApiStep.MaxSaved ? c with { Result = c.Result[..ApiStep.MaxSaved] + "\n… (cut in the saved chat)" } : c).ToList(),
    };

    /// The turn stops, and the MCP servers end with it (on a worker: each may take a moment); the next turn starts them.
    public void Interrupt()
    {
        lock (gate) turn?.Cancel();
        _ = Task.Run(() => mcp.Close());
    }

    public void Dispose()
    {
        lock (gate)
        {
            Alive = false;
            turn?.Cancel();
        }
        live.TryRemove(this, out _);
        _ = Task.Run(mcp.Dispose);
    }

    private const string Intro = "You are the assistant in a chat inside Bloom, a Windows launcher the user opens with the Win key. "
        + "You change nothing by saying so: never claim you created, wrote, changed or ran something unless a tool call (or a file block, when you have no file tools) in this very answer did it; "
        + "call the tool first, then report what it returned, and say plainly when you could not. ";

    /// The instructions: the chats' own (no web search here). Without write_file (no file tools, or 파일 쓰기 사용 안 함),
    /// files go inline as with ChatGPT; with it, the working folder and its out\ for the files the user asks for (Chats
    /// shows what lands there).
    internal static string Instructions(ApiToolbox? tools, bool pc, bool saveFenced)
    {
        string folder = tools is not null && tools.Offers(ApiToolbox.ReadFile)
            ? "You have file tools on the user's Windows PC; your working folder is this chat's own, relative paths start there, and the user's attached files have copies in in\\ there. "
                + (tools.Offers(ApiToolbox.RunCommand) ? "run_command runs Windows PowerShell. " : "")
                + "Write, edit or run a command only when the request needs it: each may wait for the user's OK, and if declined, don't retry. "
            : "";
        if (tools?.OffersMcp == true) folder += "Tools named <server>__<tool> are other MCP servers' on the user's PC; each call may wait for the user's OK. ";
        if (tools is null || !tools.Offers(ApiToolbox.WriteFile))
            return ClaudeSession.SystemPrompt(null, Intro + (saveFenced ? CodexSession.InlineFiles : "Files can't be saved from this chat: give what the user asks for in your message. ") + folder, pc: pc, titles: false, web: false); // no title line asked of an API model: small ones then write their tool calls as text
        string files = "Files the user attached come in the message: text files inside <attachment name=\"…\"> blocks, pictures as images. "
            + "When the user asks for a file (a document, code, a table, an SVG picture...), make it by calling the write_file tool with the path out\\<a fitting name> (a file you only describe does not exist) and say its name in one line; "
            + "it appears in the chat as a file the user can open or save. ";
        return ClaudeSession.SystemPrompt(null, Intro + files + folder, pc: pc, titles: false, web: false);
    }

    /// The saved chat's earlier turns, on the UI thread (strings only): each question with the answer it got, the
    /// question as it was sent (ChatMessage.Sent: the time, a page's contents; older chats: its text); the turn being
    /// sent (the last two messages) is left out. A failed or stopped answer keeps the tool calls it finished, with their
    /// results (the model must know what already ran), but not its text; one with nothing finished drops the pair.
    public static List<ApiPastTurn> Past(IReadOnlyList<ChatMessage> messages)
    {
        var list = new List<ApiPastTurn>();
        int end = messages.Count - 2;
        for (int i = 0; i + 1 < end; i++)
        {
            ChatMessage q = messages[i], a = messages[i + 1];
            if (q.Role != ApiMessage.User || a.Role != ApiMessage.Assistant) continue;
            i++;
            string asked = q.Sent ?? q.Text;
            var steps = a.Steps?.Where(step => !a.Error || step.Calls.Count > 0).ToArray() ?? [];
            if (asked.Length == 0 || (a.Error || a.Text.Length == 0) && steps.Length == 0) continue;
            list.Add(new ApiPastTurn(asked, q.Image, q.Files?.ToArray() ?? [], a.Error ? "" : a.Text) { Steps = steps });
        }
        return list;
    }

    /// Those turns as an API takes them, on a worker thread: text files go into the question again as when it was
    /// sent (Chats.Inline), pictures that are still there along with it; an answer that called tools as its steps.
    public static List<ApiMessage> HistoryOf(IEnumerable<ApiPastTurn> past, string unavailable)
    {
        var list = new List<ApiMessage>();
        foreach (var t in past)
        {
            var files = t.Files.ToList();
            string[] images = [.. (t.Image is { } image ? [image] : Array.Empty<string>()).Concat(files.Where(f => Chats.ImageType(f) is not null)).Where(File.Exists)];
            string text = files.Count > 0 ? t.Asked + Chats.Inline(files, unavailable) : t.Asked;
            list.Add(new ApiMessage(ApiMessage.User, text, images));
            if (t.Steps.Count > 0) list.AddRange(t.Steps.SelectMany(step => step.Messages()));
            else list.Add(new ApiMessage(ApiMessage.Assistant, t.Answer, Array.Empty<string>()));
        }
        return list;
    }
}
