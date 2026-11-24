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

/// One long-lived `claude -p` process per conversation, fed through stream-json on stdin.
/// The context stays in the CLI session (nothing is re-sent each turn); the system prompt is
/// replaced; the session loads no hooks, none of the user's settings or MCP servers (ClaudeLaunch), runs in this
/// conversation's folder (ai\files\<thread>: attachments in in\, files it makes in out\) and allows WebSearch, Read there
/// and Write in out\. With PcTools on it also gets Bloom's own MCP tools; anything else (WebFetch, a file elsewhere, a
/// tool) comes as a ToolAsk and waits for Answer — under 자동 only what Claude Code's auto mode asks about, and Bloom's
/// own tool.
/// Effort "fast" turns thinking off; the other levels go to --effort. The user's own Claude login (OAuth) is used.
internal sealed class ClaudeSession : IChatSession
{
    private Process? proc;
    private QueuedSessionInput? stdin;
    private readonly object gate = new();
    private int reqCounter;
    private long turn;
    private bool ending, stopped;
    private readonly InputDeliveryWatchdog interruptWatch = new();

    public string Model { get; }
    public string Effort { get; }
    public string? AccountHome { get; init; } // the chat account's folder (CLAUDE_CONFIG_DIR); null = Claude Code's default
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;
    public bool Alive => !ending && proc is { HasExited: false };

    // all raised on the reader thread
    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta;
    public event Action<string?>? Status; // "웹 검색 중…", null = clear
    public event Action<string>? AssistantText; // a whole assistant message (when no deltas came)
    public event Action<bool, string?>? TurnEnded; // ok, error text
    public event Action<string>? Exited; // stderr tail
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    /// Bloom's own MCP tool for the chat (Host sets it): its server entry and a status line for a tool by name.
    /// Null, or 설정 > PC 작업 · MCP off (the switch, or every kind of action): the chat has no PC tool.
    public static Func<(object server, Func<string, string?> doing)?>? PcTools;

    /// The other MCP servers the user switched on (설정 > PC 작업 · MCP), read where they are kept.
    public static Func<Func<List<McpServerDef>>?>? PrepareMcp;

    private Func<string, string?>? doing;
    private string? mcpFile; // this session's --mcp-config file (its servers' env stays off the command line)

    private readonly string files; // the thread's folder relative to the working folder: "." in it, "files/<thread id>" in ai\

    /// The conversation's own folder, which the CLI runs in; null: ai\ itself, for a session made there before (Claude
    /// Code keeps sessions per working folder, so it is resumed where it began).
    public string? Folder { get; init; }

    /// Whether the CLI runs in the conversation's own folder (Chats keeps it with the session id).
    public bool InFolder => Folder is not null;
    private readonly string name; // how Claude Code lists the running session ("Bloom · <title>"), not "ai-3f"

    public ClaudeSession(string model, string effort, string files, string name)
    {
        Model = model;
        Effort = effort;
        this.files = files;
        this.name = name;
    }

    public static string? FindCli(string? custom)
    {
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
        string native = Path.Combine(Paths.Home, @".local\bin\claude.exe");
        if (File.Exists(native)) return native;
        try
        {
            string desktop = Path.Combine(Paths.AppData, @"Claude\claude-code");
            if (Directory.Exists(desktop))
            {
                var best = Directory.GetDirectories(desktop)
                    .Select(d => (dir: d, ver: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
                    .Where(x => x.ver is not null && File.Exists(Path.Combine(x.dir, "claude.exe")))
                    .OrderByDescending(x => x.ver)
                    .FirstOrDefault();
                if (best.dir is not null) return Path.Combine(best.dir, "claude.exe");
            }
        }
        catch
        {
            /* keep looking */
        }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), "claude.exe");
                if (dir.Length > 0 && File.Exists(p)) return p;
            }
            catch
            {
                /* bad PATH entry */
            }
        }
        return null;
    }

    /// The chats' instructions. They stay the same from start to start (no clock in them: the time comes with each
    /// message, LocalTime), so a CLI started again for the same conversation reads its prompt cache instead of writing it
    /// all again (~140k tokens a restart while the clock was in here).
    /// `web`: the model can search the web (the CLIs can; an API chat can't).
    public static string SystemPrompt(string? outDir = null, string intro = "You are the quick assistant inside Bloom, a Windows launcher the user opens with the Win key. ", bool pc = false, bool titles = false, bool web = true)
    {
        return intro
            + (titles ? ChatTitles.Instruction : "")
            + "Reply in the user's language (Korean when they write Korean). Be concise: lead with the answer, then only the detail that helps. "
            + "Use Markdown when it helps (short lists, tables, fenced code with a language). "
            + (web ? "You may use web search for current facts, prices, news or anything time-sensitive; mention sources briefly when you do. " : "")
            + "Each message has a <local_time> line: the user's local date and time when they sent it. "
            + "A <page> block in the message is the page the user has open in their browser; answer about it. "
            + "An <earlier_conversation> block is this conversation so far, held with another model. "
            + (outDir is null ? "" : "Files: an <attachments> block lists files the user attached, as paths relative to your working folder; read them with your file tool. "
            + $"When the user asks for a file (a document, code, a table, a list, an SVG picture...), write it into {outDir}/ with a fitting name and say its name in one line; "
            + "it appears in the chat as a file the user can open or save. Never write anywhere else. ")
            + (pc ? "The bloom tools act on the user's PC (find first, then act); each action waits for the user's OK. If declined, don't retry. Do only what was asked; say in one line what you did. "
                + (App.Mcp.BloomTools.GroupOn("bloom") ? "Notes, timers, reminders and events are Bloom's own: use the bloom tool's note, timer and event for them, never a file or another app. " : "") : "");
    }

    /// The line each chat message starts with: the local time it was sent ("<local_time>2026-10-09 14:05 (Friday), Korea
    /// Standard Time</local_time>").
    public static string LocalTime()
    {
        var now = DateTime.Now;
        return $"<local_time>{now:yyyy-MM-dd HH:mm} ({now:dddd}), {TimeZoneInfo.Local.Id}</local_time>";
    }

    private sealed record Startup(string? Cli, string Cwd, string Prompt, JsonElement? Server,
        Func<List<McpServerDef>>? ReadMcp, string? Resume, bool Auto);

    public bool Start(string? resume, out string? error)
    {
        var result = StartAsync(resume, CancellationToken.None).GetAwaiter().GetResult();
        error = result.Error;
        return result.Success;
    }

    public Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        var pc = Settings.Current.AiTools ? PcTools?.Invoke() : null;
        doing = pc?.doing;
        var plan = new Startup(Settings.Current.ClaudePath, Folder ?? Path.Combine(Paths.Local, "ai"),
            SystemPrompt(files + "/out", pc: pc is not null, titles: Settings.Current.AiTitles),
            pc is { } tools ? JsonSerializer.SerializeToElement(tools.server) : null, PrepareMcp?.Invoke(), resume,
            ToolPermissions.Of(ToolPermissions.Claude) == ToolPermissions.Auto);
        return Task.Run(() =>
        {
            bool started = false;
            try
            {
                started = StartPrepared(plan, cancellation, out string? error);
                return new SessionStart(started, error);
            }
            finally { if (!started && mcpFile is { } file) TryDelete(file); }
        }, cancellation);
    }

    private bool StartPrepared(Startup plan, CancellationToken cancellation, out string? error)
    {
        error = null;
        cancellation.ThrowIfCancellationRequested();
        string? cli = FindCli(plan.Cli);
        if (cli is null)
        {
            error = AiErrors.ClaudeMissing;
            return false;
        }
        cancellation.ThrowIfCancellationRequested();
        string cwd = plan.Cwd;
        Directory.CreateDirectory(cwd);
        if (Folder is null) File.WriteAllText(Path.Combine(cwd, ".keep"), "");
        string mcpDir = ClaudeLaunch.McpDir;
        ClearOldMcpFiles(mcpDir, TimeSpan.FromDays(1));
        ClearOldMcpFiles(Path.Combine(Paths.Local, "ai"), TimeSpan.Zero); // where earlier versions wrote them, inside the CLIs' folder
        var psi = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = cwd,
        };
        bool fast = Effort == "fast" || !AiModels.HasEffort(Model);
        // Read / Write reach this conversation's folder without asking; anything else is asked in the chat
        // (--permission-prompt-tool stdio), under 자동 what Claude Code's auto mode asks about
        foreach (string a in ClaudeLaunch.Args(Model, plan.Prompt, files, name, fast, ClaudeLaunch.UserSettings(AccountHome), plan.Auto)) psi.ArgumentList.Add(a);
        // Bloom's tool and the other servers switched on, in a file of this session's outside the CLI's folder; every
        // call asks in the chat
        var servers = new Dictionary<string, object>();
        if (plan.Server is { } server) servers["bloom"] = server;
        foreach (var d in plan.ReadMcp?.Invoke() ?? new())
            servers.TryAdd(d.Name, d.Raw is { } raw ? raw : d.Stdio ? new { type = "stdio", command = d.Command, args = d.Args, env = d.Env } : new { type = "http", url = d.Url });
        if (servers.Count > 0)
        {
            Directory.CreateDirectory(mcpDir);
            mcpFile = Path.Combine(mcpDir, $"mcp-{Guid.NewGuid():N}.json");
            File.WriteAllText(mcpFile, JsonSerializer.Serialize(new { mcpServers = servers }));
            psi.ArgumentList.Add("--mcp-config");
            psi.ArgumentList.Add(mcpFile);
        }
        if (!fast)
        {
            psi.ArgumentList.Add("--effort");
            psi.ArgumentList.Add(Effort);
        }
        if (!string.IsNullOrEmpty(plan.Resume))
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(plan.Resume);
        }
        Cli.CleanEnvironment(psi); // a Claude session that launched us must not leak its routing into the user's CLI
        AiAccounts.UseHome(psi, "claude", AccountHome);
        Process? launched = null;
        QueuedSessionInput? launchedInput = null;
        bool handedOff = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var p = launched = ChildProcess.Spawn(psi);
            if (p is null)
            {
                error = AiErrors.ClaudeStartFailed;
                return false;
            }
            var input = launchedInput = new QueuedSessionInput(p.StandardInput, lines: true, e =>
            {
                Log.Error("claude stdin", e);
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
                    SessionId = plan.Resume;
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
            var reader = new Thread(() => ReadLoop(p, stderr, input)) { IsBackground = true, Name = "claude-out" };
            int pid = p.Id;
            reader.Start();
            handedOff = true;
            Log.Write($"claude: started pid {pid} model {Model} effort {Effort} resume {plan.Resume ?? "-"}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("claude start", e);
            error = AiErrors.ClaudeStartFailed;
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

    public bool Send(string text, IReadOnlyList<string> images)
    {
        lock (gate)
        {
            if (stdin is null || !Alive || Busy) return false;
            var pictures = images.ToArray();
            interruptWatch.Cancel();
            turn++;
            stopped = false;
            Busy = true;
            LastUsed = DateTime.UtcNow;
            if (stdin.Enqueue(token =>
            {
                var content = new List<object>();
                foreach (string image in pictures)
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(image) && Chats.ImageType(image) is { } type)
                        content.Add(new { type = "image", source = new { type = "base64", media_type = type, data = Convert.ToBase64String(File.ReadAllBytes(image)) } });
                }
                content.Add(new { type = "text", text });
                return JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = content.ToArray() } });
            })) return true;
            Busy = false;
            return false;
        }
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        var response = new Dictionary<string, object> { ["behavior"] = allow ? "allow" : "deny" };
        if (allow)
        {
            using var input = JsonDocument.Parse(ask.Input);
            response["updatedInput"] = input.RootElement.Clone();
        }
        else response["message"] = "The user declined this. Don't try it again unless they ask.";
        Reply(new { subtype = "success", request_id = ask.RequestId, response });
    }

    /// A control_response line to the CLI (dropped when the session has ended).
    private void Reply(object response)
    {
        lock (gate)
        {
            if (stdin is null || !Alive) return;
            stdin.Enqueue(_ => JsonSerializer.Serialize(new { type = "control_response", response }));
        }
    }

    public void Interrupt()
    {
        lock (gate)
        {
            if (stdin is null || !Alive || !Busy) return;
            var input = stdin;
            var process = proc!;
            long version = turn;
            string id = "int" + Interlocked.Increment(ref reqCounter);
            var delivered = input.EnqueueTracked(_ => JsonSerializer.Serialize(new { type = "control_request", request_id = id, request = new { subtype = "interrupt" } }));
            _ = interruptWatch.Watch(delivered, () =>
            {
                lock (gate)
                {
                    if (!Busy || turn != version || !ReferenceEquals(stdin, input) || !ReferenceEquals(proc, process)) return;
                    // Retire this process before releasing the gate: no new turn can start on it
                    // between the identity check and the (potentially slow) process-tree kill.
                    ending = stopped = true;
                    input.Dispose();
                }
                try { process.Kill(true); }
                catch { /* reader reports an already-ended process */ }
            });
        }
    }

    private void ReadLoop(Process p, StringBuilder stderr, QueuedSessionInput input)
    {
        bool sawDelta = false;
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try
                {
                    Handle(line, ref sawDelta);
                }
                catch (Exception e)
                {
                    Log.Error("claude line", e);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("claude read", e);
        }
        input.Dispose();
        interruptWatch.Cancel();
        if (Busy)
        {
            Busy = false;
            string tail;
            lock (stderr) tail = stderr.ToString().Trim();
            TurnEnded?.Invoke(false, stopped ? AiErrors.Stopped : tail.Length > 0 ? Trim(tail) : AiErrors.ClaudeNoAnswer);
        }
        string err;
        lock (stderr) err = stderr.ToString();
        Exited?.Invoke(err);
    }

    private static string Trim(string s) => s.Length > 600 ? s[..600] + "…" : s;

    private void Handle(string line, ref bool sawDelta)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        string type = Str(r, "type");
        switch (type)
        {
            case "system":
                if (Str(r, "subtype") == "init" && Str(r, "session_id") is { Length: > 0 } sid)
                {
                    SessionId = sid;
                    SessionStarted?.Invoke(sid);
                }
                break;
            case "stream_event":
                if (!r.TryGetProperty("event", out var ev)) break;
                string et = Str(ev, "type");
                if (et == "message_start") sawDelta = false;
                else if (et == "content_block_start" && ev.TryGetProperty("content_block", out var cb))
                {
                    string bt = Str(cb, "type");
                    if (bt is "tool_use" or "server_tool_use")
                    {
                        string tool = Str(cb, "name");
                        Status?.Invoke(tool switch
                        {
                            "WebSearch" or "web_search" => Tr("웹 검색 중…", "Searching the web…"),
                            "WebFetch" or "web_fetch" => Tr("페이지 읽는 중…", "Reading page…"),
                            "Read" => Tr("첨부 파일 읽는 중…", "Reading attachment…"),
                            "Write" => Tr("파일 만드는 중…", "Creating file…"),
                            _ => doing?.Invoke(tool) ?? Tr($"{tool} 사용 중…", $"Using {tool}…"),
                        });
                    }
                    else if (bt == "text") Status?.Invoke(null);
                }
                else if (et == "content_block_delta" && ev.TryGetProperty("delta", out var d) && Str(d, "type") == "text_delta")
                {
                    string t = Str(d, "text");
                    if (t.Length > 0)
                    {
                        sawDelta = true;
                        TextDelta?.Invoke(t);
                    }
                }
                break;
            case "assistant":
                if (!sawDelta && r.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var c in content.EnumerateArray())
                        if (Str(c, "type") == "text") sb.Append(Str(c, "text"));
                    if (sb.Length > 0) AssistantText?.Invoke(sb.ToString());
                }
                sawDelta = false;
                break;
            case "control_request":
                // a tool call that needs the user's yes; anything else (no hooks or SDK servers are set) gets an error
                // back at once, so the CLI never waits on a reply that won't come
                if (r.TryGetProperty("request", out var req) && Str(req, "subtype") == "can_use_tool")
                    ToolAsked?.Invoke(ClaudeLaunch.Worded(new ToolAsk(Str(r, "request_id"), Str(req, "tool_name"), req.TryGetProperty("input", out var input) ? input.GetRawText() : "{}", "")));
                else Reply(new { subtype = "error", request_id = Str(r, "request_id"), error = "Not supported by Bloom." });
                break;
            case "control_cancel_request":
                ToolAskWithdrawn?.Invoke(Str(r, "request_id"));
                break;
            case "result":
                interruptWatch.Cancel();
                Busy = false;
                LastUsed = DateTime.UtcNow;
                if (r.TryGetProperty("usage", out var u))
                    Log.Write($"claude turn: in {Num(u, "input_tokens")} · cache write {Num(u, "cache_creation_input_tokens")} · cache read {Num(u, "cache_read_input_tokens")} · out {Num(u, "output_tokens")}");
                if (Str(r, "session_id") is { Length: > 0 } rs) SessionId = rs;
                bool isError = r.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                string sub = Str(r, "subtype");
                if (isError || (sub.Length > 0 && sub != "success"))
                {
                    string msg = Str(r, "result");
                    if (msg.Length == 0 && r.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
                        msg = string.Join("\n", errs.EnumerateArray().Select(e => e.ToString()));
                    TurnEnded?.Invoke(false, msg.Length > 0 ? Trim(msg) : sub);
                }
                else TurnEnded?.Invoke(true, null);
                break;
        }
    }

    private static long Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt64(out long n) ? n : 0;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

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
            if (mcpFile is { } f) TryDelete(f);
            closed.TrySetResult();
        });
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* the next start clears it */
        }
    }

    /// Session files a crash left behind (older than `age`) go when the next session starts.
    private static void ClearOldMcpFiles(string dir, TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, "mcp-*.json"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) >= age) TryDelete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* next time */
        }
    }
}
