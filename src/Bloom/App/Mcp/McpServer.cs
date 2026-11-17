using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.App.Mcp;

/// Bloom's MCP server: tools only (BloomTools), JSON-RPC lines over a named pipe that only this Windows user can open,
/// reached through `Bloom.exe --mcp <pipe>` (McpBridge), which Claude Code starts from the --mcp-config Bloom gives a
/// chat session. The pipe's name is new on every start of Bloom.
internal sealed class McpServer
{
    public string Pipe { get; } = "bloom-mcp-" + Guid.NewGuid().ToString("N")[..16];
    public readonly BloomTools Tools;
    private bool started;

    public McpServer(BloomTools tools) => Tools = tools;

    /// What starts the relay: Bloom itself (`Bloom.exe --mcp <pipe>`).
    public static string Command => Environment.ProcessPath ?? "Bloom.exe";

    /// Claude Code's --mcp-config entry for Bloom itself: the "bloom" stdio server, relaying to this pipe with this run's
    /// key (McpKey) in its environment.
    public object Server => new { type = "stdio", command = Command, args = new[] { "--mcp", Pipe }, env = McpKey.Env };

    /// Listens from the first chat session that wants it on.
    public void Start()
    {
        if (started) return;
        started = true;
        _ = Task.Run(Accept);
    }

    private async Task Accept()
    {
        while (true)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(Pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("mcp pipe", e);
                await Task.Delay(1000);
                continue;
            }
            _ = Task.Run(() => Serve(pipe));
        }
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                string? line, conversation = null;
                bool first = true, proxy = false;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (line.Length == 0) continue;
                    if (first)
                    {
                        first = false;
                        // only Bloom's own relays, which carry this run's key: anything else is closed unanswered
                        if (Hello(line) is not { } h)
                        {
                            Log.Write("mcp pipe: a connection without Bloom's key was closed");
                            return;
                        }
                        // with a conversation (Codex, omp) its actions are asked in that chat; a proxy only asks; with none
                        // it is Claude Code's relay, which asks through its own permission prompt
                        (conversation, proxy) = h;
                        continue;
                    }
                    string? reply = proxy ? await Ask(line, conversation!) : await Answer(line, conversation);
                    if (reply is not null) await writer.WriteLineAsync(reply);
                }
            }
            catch (IOException)
            {
                /* the session ended */
            }
        }
    }

    /// The first line of every connection: {"bloom_key": "<McpKey>"} plus {"bloom_conversation": "<id>"} (Bloom's own
    /// tools for a Codex or omp chat) or {"bloom_proxy": "<id>"} (McpProxy, in front of another MCP server), or neither
    /// (Claude Code's relay). Null — and the connection closed — without this run's key.
    internal static (string? conversation, bool proxy)? Hello(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("bloom_key", out var k) || k.ValueKind != JsonValueKind.String || !McpKey.Matches(k.GetString())) return null;
            if (r.TryGetProperty("bloom_conversation", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } id) return (id, false);
            if (r.TryGetProperty("bloom_proxy", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } pid) return (pid, true);
            return (null, false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// A proxy's question {"ask_id": n, "tool": "mcp__server__tool", "input": {…}} → {"ask_id": n, "allow": yes or no},
    /// answered in the conversation's chat.
    private async Task<string?> Ask(string line, string conversation)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ask_id", out var id) || !r.TryGetProperty("tool", out var tool)) return null;
            string input = r.TryGetProperty("input", out var i) ? i.GetRawText() : "{}";
            bool allow = Tools.Approve is { } approve && await approve(conversation, tool.GetString() ?? "", input);
            return JsonSerializer.Serialize(new { ask_id = id.Clone(), allow });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// One JSON-RPC message → the reply line (null for notifications and replies). `conversation`: the chat that asks
    /// the user before an action (Codex runs; Claude Code asks through its own permission prompt).
    public async Task<string?> Answer(string line, string? conversation = null)
    {
        JsonElement id;
        string method;
        JsonElement pars = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("method", out var m) || !r.TryGetProperty("id", out var i)) return null;
            id = i.Clone();
            method = m.GetString() ?? "";
            if (r.TryGetProperty("params", out var p)) pars = p.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
        object? result = null;
        (int code, string message)? error = null;
        switch (method)
        {
            case "initialize":
                string version = pars.ValueKind == JsonValueKind.Object && pars.TryGetProperty("protocolVersion", out var v) && v.GetString() is { } pv ? pv : "2025-06-18";
                result = new
                {
                    protocolVersion = version,
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = BloomTools.Server, version = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
                };
                break;
            case "ping":
                result = new { };
                break;
            case "tools/list":
                result = new { tools = new[] { Tools.Definition } };
                break;
            case "tools/call":
                string name = pars.ValueKind == JsonValueKind.Object && pars.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var args = pars.ValueKind == JsonValueKind.Object && pars.TryGetProperty("arguments", out var a) ? a : default;
                var (text, isError) = await Tools.Call(name, args, conversation);
                result = new { content = new[] { new { type = "text", text } }, isError };
                break;
            default:
                error = (-32601, "Method not found: " + method);
                break;
        }
        return error is { } e
            ? JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = e.code, message = e.message } })
            : JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
    }
}
