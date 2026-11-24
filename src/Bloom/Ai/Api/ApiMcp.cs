using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The other MCP servers an API chat uses (the ones switched on in 설정 > PC 작업 · MCP, McpCatalog.Picked): started on
/// demand by the first turn that offers tools, side by side, each given StartLimit; their tools offered as functions
/// (McpNames), at most MaxTools of them across the servers, taken in turn so each server gets its share. A server that
/// fails is left out with one note in the chat, never holding the turn past StartLimit. A server whose definition
/// changed (command, arguments, env, address, headers: McpServerDef.ConfigHash) is not the one running: that one ends
/// and the new one starts; a failure is remembered with the definition it was for. Each running server keeps the
/// secrets of the definition it started with (McpSecrets) until it ends, and what it says — tool descriptions, schemas,
/// results — has them taken out before a provider or a saved chat sees it. Close (a stop, the session's end — 세션 종료,
/// the idle close, the chat deleted, 권한's 다른 MCP 도구 set to 사용 안 함) ends them all and cancels a start under
/// way; the next turn starts them again.
internal sealed class ApiMcp : IDisposable
{
    /// The most MCP tools one request offers (providers take about 128 functions; Bloom's own and room to spare).
    public const int MaxTools = 64;

    /// A tool's description is cut to this (some servers write pages; every request carries them).
    public const int MaxDescription = 1000;

    public static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan CallLimit = TimeSpan.FromMinutes(5);

    /// A server running: its tools, the hash of the definition it started from (McpServerDef.ConfigHash), the identity
    /// its approvals are kept under, and its secrets.
    private sealed record Entry(McpClient Client, List<McpTool> Tools, string Hash, string Identity, IReadOnlyList<string> Secrets);

    /// A function's way to its server: the approval key is the server's identity and the tool's own name.
    private sealed record Route(McpClient Client, string Server, string Tool, string Key, IReadOnlyList<string> Secrets);

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> running = new(); // McpServerDef.Key →
    private readonly Dictionary<string, string> failed = new(); // Key → ConfigHash tried this session and failed: told once, not tried again
    private readonly Func<McpServerDef, CancellationToken, Task<McpClient>> start;
    private Dictionary<string, Route> routes = new();
    private CancellationTokenSource closing = new(); // Close cancels it: a start still under way ends
    private int generation; // Close moves it on: a start still under way then is not kept
    private int toldLeftOut;
    private CancellationToken lifetime; // the running turn's MCP lifetime (PrepareAsync): once it ends nothing is offered or called
    private IReadOnlyList<ApiTool> offered = [];

    /// `start`: how a server starts (tests start theirs; else McpClient.StartAsync).
    public ApiMcp(Func<McpServerDef, CancellationToken, Task<McpClient>>? start = null) => this.start = start ?? McpClient.StartAsync;

    /// The functions offered now (PrepareAsync made them); none once the turn's MCP lifetime has ended.
    public IReadOnlyList<ApiTool> Tools
    {
        get
        {
            lock (gate) return lifetime.IsCancellationRequested ? [] : offered;
        }
    }

    /// What no result may show: the running servers' secrets.
    public IReadOnlyList<string> Secrets { get; private set; } = [];

    public bool Has(string function)
    {
        lock (gate) return !lifetime.IsCancellationRequested && routes.ContainsKey(function);
    }

    /// The server and tool a function stands for, and the key its approval is remembered under.
    public (string Server, string Tool, string Key)? Of(string function)
    {
        lock (gate) return !lifetime.IsCancellationRequested && routes.TryGetValue(function, out var r) ? (r.Server, r.Tool, r.Key) : null;
    }

    /// Before a turn, on a worker thread: the servers in `defs` started (those not running and not failed before),
    /// those no longer in it, or changed, closed, and the functions made, none named as one in `reserved`. → the notes
    /// for the chat (a server left out, tools left out). `turnLife`: the turn's MCP lifetime (ApiSession), which 권한's
    /// 다른 MCP 도구 사용 안 함 ends at once: one that has ended starts nothing, and one that ends during it cancels the starts.
    /// Throws OperationCanceledException when `stop` ends it; Close during it ends it with no servers and no tools, and no
    /// error.
    public async Task<List<string>> PrepareAsync(IReadOnlyList<McpServerDef> defs, IEnumerable<string> reserved, CancellationToken stop, CancellationToken turnLife = default)
    {
        var notes = new List<string>();
        List<McpServerDef> due;
        int at;
        CancellationToken closed;
        var gone = new List<McpClient>();
        lock (gate)
        {
            lifetime = turnLife;
            if (turnLife.IsCancellationRequested) return notes; // 권한 switched MCP off since the message was sent
            at = generation;
            closed = closing.Token;
            var wanted = defs.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First().ConfigHash);
            foreach (string key in running.Keys.ToList())
                if (!wanted.TryGetValue(key, out string? hash) || hash != running[key].Hash)
                {
                    gone.Add(running[key].Client);
                    running.Remove(key);
                }
            foreach (string key in failed.Keys.ToList())
                if (!wanted.ContainsKey(key)) failed.Remove(key); // switched off meanwhile: switched on again, it is tried again
            due = defs.Where(d => !running.ContainsKey(d.Key) && failed.GetValueOrDefault(d.Key) != d.ConfigHash).ToList();
        }
        foreach (var c in gone) c.Dispose();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop, closed, turnLife);
        var started = await Task.WhenAll(due.Select(d => StartOne(d, cancel.Token))).ConfigureAwait(false);
        var late = new List<McpClient>();
        lock (gate)
        {
            foreach (var (def, client, tools, error) in started)
            {
                if (client is null || tools is null)
                {
                    if (!cancel.IsCancellationRequested && failed.GetValueOrDefault(def.Key) != def.ConfigHash)
                    {
                        failed[def.Key] = def.ConfigHash;
                        notes.Add(error is { Refused: true }
                            ? Tr($"MCP 서버 {def.Name}의 설정이 안전하지 않아(암호화되지 않은 http 주소 또는 허용되지 않는 헤더) 이번 대화에서는 제외합니다.",
                                $"Skipped MCP server {def.Name} for this chat due to an insecure configuration (unencrypted http URL or restricted headers).")
                            : Tr($"MCP 서버 {def.Name}을(를) 시작하지 못해 이번 대화에서는 제외합니다.", $"Could not start MCP server {def.Name}, so it was skipped for this chat."));
                    }
                    continue;
                }
                if (generation != at || cancel.IsCancellationRequested) late.Add(client);
                else running[def.Key] = new Entry(client, tools, def.ConfigHash, def.Identity, McpSecrets.Of(def));
            }
        }
        foreach (var c in late) c.Dispose();
        stop.ThrowIfCancellationRequested();
        if (closed.IsCancellationRequested || turnLife.IsCancellationRequested) return notes; // Close, or 권한, ended it all
        int total = Offer(defs, reserved);
        if (total > MaxTools && total != toldLeftOut)
        {
            toldLeftOut = total;
            int left = total - MaxTools;
            notes.Add(Tr($"MCP 도구가 총 {total}개로 많아 {MaxTools}개만 활성화하고 {left}개는 제외했습니다. 자주 쓰지 않는 서버를 끄면 모두 제공됩니다.",
                $"Too many MCP tools ({total} total); enabled {MaxTools} and skipped {left}. Turn off unused servers to include all tools."));
        }
        return notes;
    }

    /// One server started and its tools listed, within StartLimit; (def, null, null, why) when it failed.
    private async Task<(McpServerDef Def, McpClient? Client, List<McpTool>? Tools, McpException? Error)> StartOne(McpServerDef def, CancellationToken stop)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(StartLimit);
        McpClient? client = null;
        try
        {
            client = await Task.Run(() => start(def, limit.Token), limit.Token).ConfigureAwait(false);
            var tools = await client.ToolsAsync(limit.Token).ConfigureAwait(false);
            return (def, client, tools, null);
        }
        catch (Exception e) when (e is McpException or OperationCanceledException or IOException or HttpRequestException or JsonException or InvalidOperationException)
        {
            client?.Dispose();
            if (!stop.IsCancellationRequested) Log.Write($"mcp server did not start: {e.GetType().Name}"); // its name and command stay out of the log
            return (def, null, null, e as McpException);
        }
    }

    /// The functions for the running servers, in `defs` order, a tool of each in turn up to MaxTools. → how many tools
    /// the servers have in all.
    private int Offer(IReadOnlyList<McpServerDef> defs, IEnumerable<string> reserved)
    {
        lock (gate)
        {
            var taken = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
            var made = new Dictionary<string, Route>();
            var tools = new List<ApiTool>();
            var lists = defs.Where(d => running.ContainsKey(d.Key)).Select(d => (Def: d, Entry: running[d.Key])).ToList();
            int total = lists.Sum(l => l.Entry.Tools.Count);
            for (int i = 0; tools.Count < MaxTools && lists.Any(l => i < l.Entry.Tools.Count); i++)
                foreach (var (def, entry) in lists)
                {
                    if (i >= entry.Tools.Count || tools.Count >= MaxTools) continue;
                    var t = entry.Tools[i];
                    string name = McpNames.Function(def.Name, t.Name, taken);
                    made[name] = new Route(entry.Client, def.Name, t.Name, McpNames.ApprovalKey(entry.Identity, t.Name), entry.Secrets);
                    // what the server wrote of itself, with its secrets out, before any provider reads it
                    string about = McpSecrets.Redact($"{t.Description} (MCP server {def.Name}, its tool {t.Name}.)".Trim(), entry.Secrets);
                    tools.Add(new ApiTool(name, about.Length > MaxDescription ? about[..MaxDescription] + "…" : about, Schema(t.Schema, entry.Secrets)));
                }
            routes = made;
            offered = tools;
            Secrets = lists.SelectMany(l => l.Entry.Secrets).Distinct().ToList();
            return total;
        }
    }

    /// A tool's input schema as every provider takes it: an object schema (with properties), without the `$schema` and
    /// `$id` some servers add and some providers refuse, and with `secrets` taken out of its texts.
    internal static string Schema(string schema, IReadOnlyList<string>? secrets = null)
    {
        JsonObject? o = null;
        try
        {
            o = schema.Length > 0 ? JsonNode.Parse(schema) as JsonObject : null;
        }
        catch (JsonException)
        {
            /* not JSON: an empty object schema */
        }
        o ??= new JsonObject();
        o.Remove("$schema");
        o.Remove("$id");
        o["type"] = "object";
        if (o["properties"] is not JsonObject) o["properties"] = new JsonObject();
        return (secrets is { Count: > 0 } ? McpSecrets.RedactJson(o, secrets) : o)!.ToJsonString();
    }

    /// One call, its server's answer as text with that server's secrets out (the decoded values: McpClient.Text). The
    /// call also ends when the servers are closed (`closing`) or the turn's MCP lifetime ends (PrepareAsync's). Throws
    /// OperationCanceledException only when `stop` ends it.
    public async Task<(string Text, bool Error)> CallAsync(string function, JsonElement arguments, CancellationToken stop)
    {
        Route? route;
        CancellationToken closed, turn;
        lock (gate)
        {
            route = lifetime.IsCancellationRequested ? null : routes.GetValueOrDefault(function);
            closed = closing.Token;
            turn = lifetime;
        }
        if (route is null) return ($"No tool named {function}.", true);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop, closed, turn);
        limit.CancelAfter(CallLimit);
        try
        {
            var (text, error) = await route.Client.CallAsync(route.Tool, arguments, route.Secrets, limit.Token).ConfigureAwait(false);
            return (McpSecrets.Redact(text, route.Secrets), error);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return (closed.IsCancellationRequested || turn.IsCancellationRequested
                ? "The MCP server was closed, so the tool was stopped."
                : $"The MCP tool gave no answer in {(int)CallLimit.TotalMinutes} minutes, so it was stopped.", true);
        }
        catch (Exception e) when (e is McpException or IOException or HttpRequestException or JsonException)
        {
            return (McpSecrets.Redact("The MCP server failed: " + e.Message, route.Secrets), true);
        }
    }

    /// Every server ends, on a worker thread (each may take a moment), and a start under way is cancelled. The next
    /// PrepareAsync starts them again; a server that failed stays left out for the conversation (a stop doesn't undo
    /// that), unless `forgetFailed` (the MCP settings changed).
    public void Close(bool forgetFailed = false)
    {
        List<McpClient> all;
        CancellationTokenSource old;
        lock (gate)
        {
            generation++;
            old = closing;
            closing = new CancellationTokenSource();
            all = running.Values.Select(r => r.Client).ToList();
            running.Clear();
            if (forgetFailed) failed.Clear();
            routes = new();
            offered = [];
            Secrets = [];
            toldLeftOut = 0;
        }
        old.Cancel();
        foreach (var c in all) c.Dispose();
    }

    public void Dispose() => Close();
}
