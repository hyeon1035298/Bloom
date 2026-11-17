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

/// ChatGPT through the user's Codex CLI and its ChatGPT login: one `codex exec --json` per turn,
/// later turns continuing the same Codex thread with `codex exec resume <id>` (a new model or level
/// goes with the next turn). It runs read-only in the launcher's AI folder without the user's
/// config.toml (so no MCP servers start), and as a chat rather than a coding agent: our own base
/// instructions instead of Codex's, live web search, no sandbox or environment notes (about half the
/// input tokens). Codex reports whole messages, not text deltas. With 설정 > PC 작업 on, each run also gets Bloom's
/// own MCP server (`-c mcp_servers.bloom…`, the relay named after this conversation); Codex in exec mode can't ask the
/// user, so it is told to approve the tool itself and Bloom's server holds every action for the user's yes (Ask).
/// The chats run on CodexAppSession (one long-lived `codex app-server`); this is its fallback when that won't start.
internal sealed class CodexSession : IChatSession, IAsksInChat
{
    /// Bloom's MCP server for a run (Host: starts it on first use); null when ChatGPT chats don't get it.
    public static Func<(string command, string pipe, string hint)?>? PcTools;

    /// The other MCP servers the user switched on, and Bloom's relay to put in front of each (Host).
    public static Func<Func<List<McpServerDef>>?>? PrepareMcp;
    public static Func<(string command, string pipe)>? Relay;

    private Process? proc;
    private QueuedSessionInput? stdin;
    private readonly object gate = new();
    private string? exe;
    private string cwd = "";
    private bool stopped;
    private bool disposed;
    private long turn;

    public string Model { get; }
    public string Effort { get; }
    public string? AccountHome { get; init; } // the chat account's folder (CODEX_HOME); null = Codex's default
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public bool Alive { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta
    {
        add { } // Codex sends whole messages (AssistantText)
        remove { }
    }
    public event Action<string?>? Status;
    public event Action<string>? AssistantText;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string>? Exited;

    // Bloom's MCP server asks here before an action (Ask); the chat answers through Answer
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    private PendingToolAsks? asks;

    /// An action Bloom's MCP server holds for the user: the chat shows the card, the task ends with their answer — or
    /// no, when the turn ends, it is stopped, or nobody answers within 9 minutes (Codex gives up on the call at 10).
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

    private readonly string files; // "files/<thread id>", relative to the working folder
    private readonly string conversation; // the relay's name for this chat, so Bloom's server knows where to ask

    public CodexSession(string model, string effort, string files, string conversation)
    {
        Model = model;
        Effort = effort;
        this.files = files;
        this.conversation = conversation;
    }

    /// The Codex model: the one picked, else Codex's own default.
    private string? Slug => AiModels.GptSlug(Model) ?? GptCatalog.DefaultSlug();

    /// A reasoning level this model takes (an older thread's "fast" becomes its lowest).
    private string Reasoning => AiModels.Fit(Model, Effort);

    public static string? FindCli(string? custom)
    {
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
        // npm's @openai/codex carries the native exe; codex.cmd / codex.ps1 only start node
        string npm = Path.Combine(Paths.AppData, @"npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe");
        if (File.Exists(npm)) return npm;
        try
        {
            string app = Path.Combine(Paths.LocalAppData, @"OpenAI\Codex\bin");
            if (Directory.Exists(app) && Directory.GetFiles(app, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } desktop)
                return desktop;
        }
        catch
        {
            /* keep looking */
        }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), "codex.exe");
                if (dir.Length > 0 && File.Exists(p)) return p;
            }
            catch
            {
                /* bad PATH entry */
            }
        }
        return null;
    }

    public bool Start(string? resume, out string? error)
    {
        var result = StartAsync(resume, CancellationToken.None).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        string? custom = Settings.Current.CodexPath;
        string directory = Path.Combine(Paths.Local, "ai");
        return Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            string? cli = FindCli(custom);
            if (cli is null) return new SessionStart(false, AiErrors.CodexMissing);
            Directory.CreateDirectory(directory);
            lock (gate)
            {
                if (disposed || cancellation.IsCancellationRequested) return new SessionStart(false, AiErrors.Stopped);
                exe = cli;
                cwd = directory;
                SessionId = resume;
                Alive = true;
            }
            return new SessionStart(true);
        }, cancellation);
    }

    private sealed record TurnPlan(string Exe, string Cwd, string? Slug, string Reasoning, string Instructions,
        (string command, string pipe, string hint)? Tools, (string command, string pipe)? Relay,
        Func<List<McpServerDef>>? ReadMcp, string? Resume, string[] Images, long Version, PendingToolAsks Pending);

    public bool Send(string text, IReadOnlyList<string> images) => SendAsync(text, images, CancellationToken.None).GetAwaiter().GetResult();

    public Task<bool> SendAsync(string text, IReadOnlyList<string> images, CancellationToken cancellation)
    {
        var tools = PcTools?.Invoke();
        var reader = PrepareMcp?.Invoke();
        var relay = reader is null ? null : Relay?.Invoke();
        string instructions = Instructions(tools?.hint, Settings.Current.AiTitles);
        string? slug = Slug;
        string reasoning = Reasoning;
        TurnPlan plan;
        lock (gate)
        {
            if (!Alive || disposed || Busy || exe is null || cancellation.IsCancellationRequested) return Task.FromResult(false);
            Busy = true;
            stopped = false;
            LastUsed = DateTime.UtcNow;
            var pending = new PendingToolAsks(id => ToolAskWithdrawn?.Invoke(id));
            asks = pending;
            plan = new(exe, cwd, slug, reasoning, instructions, tools, relay, reader, SessionId, images.ToArray(), ++turn, pending);
        }
        return Task.Run(() => SendPrepared(text, plan, cancellation), cancellation);
    }

    private bool SendPrepared(string text, TurnPlan plan, CancellationToken cancellation)
    {
        string instructionFile = Path.Combine(plan.Cwd, $"chatgpt-{Guid.NewGuid():N}.md");
        bool published = false;
        bool handedOff = false;
        Process? launched = null;
        QueuedSessionInput? launchedInput = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllText(instructionFile, plan.Instructions);
            var utf8 = new UTF8Encoding(false);
            var psi = new ProcessStartInfo(plan.Exe)
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
            var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--ignore-user-config", "--sandbox", "read-only", "--cd", plan.Cwd, "--color", "never" };
            args.AddRange(Cli.CodexNoShell);
            if (plan.Slug is { Length: > 0 } model)
            {
                args.Add("--model");
                args.Add(model);
            }
            args.Add("-c");
            args.Add($"model_reasoning_effort=\"{plan.Reasoning}\"");
            foreach (string c in new[]
            {
                $"model_instructions_file={Toml(instructionFile)}",
                "web_search=\"live\"",
                "include_environment_context=false",
                "include_permissions_instructions=false",
            })
            {
                args.Add("-c");
                args.Add(c);
            }
            if (plan.Tools is { } t)
                foreach (string c in new[]
                {
                    "mcp_servers.bloom.command=" + Toml(t.command),
                    $"mcp_servers.bloom.args=[\"--mcp\", {Toml(t.pipe)}, {Toml(conversation)}]",
                    "mcp_servers.bloom.env=" + Cli.CodexRelayEnv,
                    "mcp_servers.bloom.default_tools_approval_mode=\"approve\"", // Bloom asks itself, in the chat
                    "mcp_servers.bloom.tool_timeout_sec=600", // a call can wait that long for the user's yes
                    "mcp_servers.bloom.startup_timeout_sec=20",
                })
                {
                    args.Add("-c");
                    args.Add(c);
                }
            // the other servers switched on: each started through Bloom's proxy, which asks in the chat before a call
            // (web-address servers can't be put behind it, so ChatGPT goes without them)
            if (plan.ReadMcp?.Invoke().Where(d => d.Stdio && d.Name != "bloom").ToList() is { Count: > 0 } extra && plan.Relay is { } relay)
                foreach (var d in extra)
                {
                    string key = "mcp_servers." + TomlKey(d.Name);
                    foreach (string c in new[]
                    {
                        $"{key}.command={Toml(relay.command)}",
                        $"{key}.args=[\"--mcp-proxy\", {Toml(relay.pipe)}, {Toml(conversation)}, {Toml(d.Source)}, {Toml(d.Name)}]",
                        $"{key}.env={Cli.CodexRelayEnv}",
                        $"{key}.default_tools_approval_mode=\"approve\"",
                        $"{key}.tool_timeout_sec=600",
                        $"{key}.startup_timeout_sec=30",
                    })
                    {
                        args.Add("-c");
                        args.Add(c);
                    }
                }
            if (plan.Resume is { } id)
            {
                args.Add("resume");
                args.Add(id);
            }
            foreach (string image in plan.Images)
                if (File.Exists(image)) args.Add("--image=" + image); // one value each: "-" stays the prompt
            args.Add("-"); // the prompt comes on stdin
            foreach (string a in args) psi.ArgumentList.Add(a);
            Cli.CleanEnvironment(psi); // the ChatGPT login only
            AiAccounts.UseHome(psi, "chatgpt", AccountHome);
            _ = CodexAuth.Unsaved(AccountHome); // tokens the light way refreshed go in first (they were the folder's before it was picked)
            cancellation.ThrowIfCancellationRequested();
            Process p;
            try
            {
                p = launched = ChildProcess.Spawn(psi)!;
            }
            catch (Exception e)
            {
                Log.Error("codex start", e);
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
            var input = launchedInput = new QueuedSessionInput(p.StandardInput, lines: false, e =>
            {
                Log.Error("codex stdin", e);
                try { p.Kill(true); }
                catch { /* the reader reports a process that already ended */ }
            });
            lock (gate)
            {
                published = !disposed && Alive && turn == plan.Version && !cancellation.IsCancellationRequested;
                if (published)
                {
                    proc = p;
                    stdin = input;
                }
            }
            if (!published)
            {
                return false;
            }
            int pid = p.Id;
            new Thread(() => ReadLoop(p, stderr, plan.Pending, input, instructionFile)) { IsBackground = true, Name = "codex-out" }.Start();
            handedOff = true;
            input.Enqueue(_ => text, end: true);
            Log.Write($"codex: started pid {pid} model {plan.Slug ?? "-"} effort {plan.Reasoning} resume {plan.Resume ?? "-"}");
            return true;
        }
        finally
        {
            if (!handedOff)
            {
                plan.Pending.Dispose();
                lock (gate)
                {
                    if (turn == plan.Version) Busy = false;
                    if (ReferenceEquals(proc, launched)) proc = null;
                    if (ReferenceEquals(stdin, launchedInput)) stdin = null;
                }
                launchedInput?.Dispose();
                try { launched?.Kill(true); }
                catch { /* startup failed or was cancelled */ }
                launched?.Dispose();
                TryDeleteInstructions(instructionFile);
            }
        }
    }

    private void ReadLoop(Process p, StringBuilder stderr, PendingToolAsks pending, QueuedSessionInput input, string instructionFile)
    {
        bool ended = false;
        string? error = null;
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(asks, pending)) Handle(line, pending, ref ended, ref error);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("codex line", e);
                }
            }
            p.WaitForExit(3000);
        }
        catch (Exception e)
        {
            Log.Error("codex read", e);
        }
        lock (gate)
        {
            if (ReferenceEquals(proc, p)) proc = null;
            input.Dispose();
            pending.Dispose(); // only this run's requests; a new turn may already be running
            if (!ended && ReferenceEquals(asks, pending))
            {
                Busy = false;
                string tail;
                lock (stderr) tail = stderr.ToString().Trim();
                TurnEnded?.Invoke(false, stopped ? AiErrors.Stopped : error is { Length: > 0 } ? error : tail.Length > 0 ? Trim(tail) : AiErrors.CodexNoAnswer);
            }
        }
        p.Dispose();
        TryDeleteInstructions(instructionFile);
    }

    private static string Trim(string s) => s.Length > 600 ? s[^600..] : s;

    /// A TOML key: bare when it can be ("files-mcp"), else quoted.
    private static string TomlKey(string k) => k.Length > 0 && k.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? k : "\"" + k.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// A TOML string for a -c value: a literal one (backslashes stay) unless it holds a quote.
    private static string Toml(string s) => s.Contains('\'') ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : "'" + s + "'";

    /// The base instructions Codex reads instead of its own coding-agent ones (CodexAppSession gives the same once, as
    /// the thread's baseInstructions).
    internal static string Instructions(string? pc, bool titles)
    {
        return ClaudeSession.SystemPrompt(null,
                "You are ChatGPT, answering in a chat inside Bloom, a Windows launcher the user opens with the Win key. "
                + "This is a conversation, not a coding task: do not run commands and do not read or change files"
                + (pc is not null ? " (the bloom tool is the way to act on the PC). " + pc : ". ")
                + InlineFiles, pc is not null, titles);
    }

    /// How a model without file tools gets attachments and gives files (ChatGPT here, the API chats too): both in the
    /// messages, a written file as a ```file:<name> block that Chats saves.
    internal const string InlineFiles = "Files the user attached come in the message: text files inside <attachment name=\"…\"> blocks, pictures as images. "
        + "When the user asks for a file, put its whole content in a fenced code block whose info string is file:<name> "
        + "(for example ```file:summary.md), one block per file; Bloom saves each as a file the user can open or save. ";

    private static void TryDeleteInstructions(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("codex instructions cleanup", e); }
    }

    /// Web-search citations arrive as private-use markers (U+E200 cite U+E202 turn0search0 U+E201)
    /// that only ChatGPT's own apps turn into links; here they would show as stray text.
    private static readonly System.Text.RegularExpressions.Regex Citations = new("[^]*[ \t]*");

    internal static string Clean(string text) => Citations.Replace(text, "").TrimEnd();

    private static long Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt64(out long n) ? n : 0;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private void Handle(string line, PendingToolAsks pending, ref bool ended, ref string? error)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        switch (Str(r, "type"))
        {
            case "thread.started":
                if (Str(r, "thread_id") is { Length: > 0 } id && id != SessionId)
                {
                    SessionId = id;
                    SessionStarted?.Invoke(id);
                }
                break;
            case "item.started":
                if (r.TryGetProperty("item", out var it))
                    Status?.Invoke(Str(it, "type") switch
                    {
                        "reasoning" => Tr("생각하는 중…", "Thinking…"),
                        "web_search" => Tr("웹 검색 중…", "Searching the web…"),
                        "command_execution" => Tr("파일 다루는 중…", "Working with files…"),
                        "mcp_tool_call" when Str(it, "server") == "bloom" => Tr("PC에서 작업하는 중…", "Working on the PC…"),
                        "mcp_tool_call" => Tr("도구 쓰는 중…", "Using a tool…"),
                        _ => null,
                    });
                break;
            case "item.completed":
                if (r.TryGetProperty("item", out var done) && Str(done, "type") == "agent_message" && Clean(Str(done, "text")) is { Length: > 0 } text)
                {
                    Status?.Invoke(null);
                    AssistantText?.Invoke(text);
                }
                break;
            case "turn.completed":
                ended = true;
                Busy = false;
                pending.Dispose();
                if (r.TryGetProperty("usage", out var u))
                    Log.Write($"codex turn: in {Num(u, "input_tokens")} · cached {Num(u, "cached_input_tokens")} · out {Num(u, "output_tokens")}");
                LastUsed = DateTime.UtcNow;
                TurnEnded?.Invoke(true, null);
                break;
            case "turn.failed":
                ended = true;
                Busy = false;
                pending.Dispose();
                string msg = "";
                if (r.TryGetProperty("error", out var err)) msg = Str(err, "message") is { Length: > 0 } m ? m : err.ToString();
                TurnEnded?.Invoke(false, msg.Length > 0 ? msg : error ?? Tr("Codex 오류", "Codex error"));
                break;
            case "error":
                error = Str(r, "message");
                break;
        }
    }

    public void Interrupt()
    {
        lock (gate)
        {
            asks?.Dispose();
            stdin?.Dispose();
            if (proc is not { HasExited: false } p) return;
            stopped = true;
            _ = Task.Run(() =>
            {
                try { p.Kill(true); }
                catch { /* already ending */ }
            });
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; Alive = false; }
        Interrupt();
        Exited?.Invoke("");
    }
}
