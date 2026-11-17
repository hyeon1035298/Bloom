using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using Bloom.Ai;

namespace Bloom.App.Mcp;

/// `Bloom.exe --mcp-proxy <pipe> <conversation> <source> <name>`: what Codex starts for another MCP server the user
/// switched on. Codex can't ask the user in exec mode, so this stands in between: it starts the real server (read
/// fresh from where it is kept, McpCatalog) and passes every line through — except a tools/call, which first asks
/// the running Bloom over its pipe; Bloom shows the card in that conversation's chat, and a no goes back to Codex as
/// the call's error without the server ever seeing it.
internal static class McpProxy
{
    public static int Run(string pipe, string conversation, string source, string name)
    {
        // taken out of this process's environment before the real server starts from it: only Bloom's relays hold the key
        if (Core.McpKey.Take() is not { } key)
        {
            Console.Error.WriteLine("Bloom's key is missing: start this through a Bloom chat.");
            return 1;
        }
        if (McpCatalog.Find(source, name) is not { Command: { } command } def)
        {
            Console.Error.WriteLine($"No MCP server named {name} in {source}.");
            return 1;
        }
        NamedPipeClientStream link;
        try
        {
            link = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            link.Connect(3000);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Bloom is not running: " + e.Message);
            return 1;
        }
        var utf8 = new UTF8Encoding(false);
        // not disposed one by one: the pipe goes at the end, and with it these
        var bloomIn = new StreamReader(link, utf8);
        var bloomOut = new StreamWriter(link, utf8) { AutoFlush = true, NewLine = "\n" };
        bloomOut.WriteLine(JsonSerializer.Serialize(new { bloom_key = key, bloom_proxy = conversation }));

        // Bloom.exe has no console of its own, so a console server started from here would open a window per server
        var psi = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = utf8, StandardOutputEncoding = utf8 };
        foreach (string a in def.Args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in def.Env) psi.Environment[k] = v;
        Process server;
        try
        {
            server = ChildProcess.Spawn(psi)!;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Couldn't start {name}: {e.Message}");
            return 1;
        }
        var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
        var gate = new object();
        void Out(string line)
        {
            lock (gate) stdout.WriteLine(line);
        }
        // the server's answers go straight back
        var down = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = server.StandardOutput.ReadLine()) is not null) Out(line);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                /* the server ended */
            }
        });
        down.IsBackground = true;
        down.Start();
        // Codex's requests go on, a tool call only after Bloom's yes
        using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
        int asked = 0;
        try
        {
            string? line;
            while ((line = stdin.ReadLine()) is not null && !server.HasExited)
            {
                if (ToolCall(line) is { } call)
                {
                    int id = ++asked;
                    bloomOut.WriteLine(JsonSerializer.Serialize(new { ask_id = id, tool = $"mcp__{name}__{call.tool}", input = call.input }));
                    bool allow = Answer(bloomIn.ReadLine(), id);
                    if (!allow)
                    {
                        Out(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = call.id, result = new { content = new[] { new { type = "text", text = "The user declined this. Don't try it again unless they ask." } }, isError = true } }));
                        continue;
                    }
                }
                server.StandardInput.WriteLine(line);
                server.StandardInput.Flush();
            }
        }
        catch (IOException)
        {
            /* Codex or Bloom went away */
        }
        // the server goes (its last answers still get out), then the pipe
        try
        {
            server.StandardInput.Close();
            if (!server.WaitForExit(3000)) server.Kill(true);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            /* already gone */
        }
        down.Join(1000);
        try
        {
            link.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            /* Bloom closed it first */
        }
        return 0;
    }

    /// A tools/call request: its id, the tool's name and its arguments (raw JSON).
    private static (JsonElement id, string tool, JsonElement input)? ToolCall(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("method", out var m) || m.GetString() != "tools/call" || !r.TryGetProperty("id", out var id)) return null;
            var p = r.TryGetProperty("params", out var ps) ? ps : default;
            string tool = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var input = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("arguments", out var a) ? a.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
            return (id.Clone(), tool, input);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// Bloom's reply to ask `id`: {"ask_id": id, "allow": true}; anything else is a no.
    private static bool Answer(string? line, int id)
    {
        if (line is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return r.TryGetProperty("ask_id", out var i) && i.TryGetInt32(out int got) && got == id && r.TryGetProperty("allow", out var a) && a.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
