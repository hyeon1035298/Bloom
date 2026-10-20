using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Ai;

/// The MCP tools of an omp chat. omp reads MCP servers from `.omp/mcp.json` of its working folder (measured: also with
/// --tools=web_search and --no-tools, which leave MCP tools alone), so every conversation gets a working folder of its own
/// with that file written when its session starts: the conversation's id is in it, which is how Bloom knows in which chat to
/// ask the user (omp runs `--no-ui` and cannot ask itself). Nothing is written to omp's own settings or to any shared folder.
///
///   bloom        Bloom's PC tool: `Bloom.exe --mcp <pipe> <conversation>` (the hello of a Codex run: Bloom asks in this chat
///                before every action; a connection naming no conversation and no token is refused by McpServer).
///   <other>      a server the user switched on in 설정 > PC 작업 · MCP: started by `Bloom.exe --mcp-proxy …`, which asks in the
///                chat before every call (the definition, with its env values, is read by the proxy; it never sits in this file).
///                Web-address servers can't be put behind the proxy, so omp goes without them (as ChatGPT does).
internal static class OmpMcp
{
    /// The folder of one conversation's omp: "ai\omp-work\c-<id>".
    public static string WorkDir(string conversation)
    {
        string clean = new string(conversation.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
        string dir = OmpCli.WorkDir();
        return Path.Combine(dir, "c-" + (clean.Length > 0 ? clean : "chat"));
    }

    /// omp's tool name for a server's tool ("mcp__bloom_pc"), the way it is shown in its events.
    public static string ToolName(string server, string tool) => "mcp__" + server + "_" + tool;

    /// The `.omp/mcp.json` for a conversation: Bloom's PC tool (null = not offered), the other servers switched on, and the
    /// relay that asks before their calls (null: none of them can be offered). Null when there is nothing to connect — then
    /// no file is written and omp starts without MCP.
    public static string? Config(string conversation, (string command, string pipe)? pc, IEnumerable<McpServerDef> extras, (string command, string pipe)? relay)
    {
        var servers = new JsonObject();
        if (pc is { } p)
            servers["bloom"] = Stdio(p.command, new[] { "--mcp", p.pipe, conversation }, null);
        if (relay is { } r)
            foreach (var d in extras)
            {
                if (!d.Stdio || d.Name == "bloom" || servers.ContainsKey(Key(d.Name))) continue;
                servers[Key(d.Name)] = Stdio(r.command, new[] { "--mcp-proxy", r.pipe, conversation, d.Source, d.Name }, null);
            }
        if (servers.Count == 0) return null;
        return new JsonObject { ["mcpServers"] = servers }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// A server's key in the file: letters, digits, "_" and "-" only (omp builds its tool names from it).
    public static string Key(string name)
    {
        string k = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray()).Trim('_');
        return k.Length == 0 ? "server" : k.Length > 40 ? k[..40] : k;
    }

    private static JsonObject Stdio(string command, IEnumerable<string> args, IReadOnlyDictionary<string, string>? env)
    {
        var o = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = command,
            ["args"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["timeout"] = 600000, // a call can wait that long for the user's yes
        };
        if (env is { Count: > 0 })
        {
            var e = new JsonObject();
            foreach (var (k, v) in env) e[k] = v;
            o["env"] = e;
        }
        return o;
    }

    /// Writes (or removes) the conversation's `.omp/mcp.json` and returns the working folder omp starts in.
    public static string Prepare(string conversation, string? json)
    {
        string work = WorkDir(conversation);
        string omp = Path.Combine(work, ".omp");
        string file = Path.Combine(omp, "mcp.json");
        try
        {
            Directory.CreateDirectory(work);
            if (json is null)
            {
                if (File.Exists(file)) File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(omp);
                File.WriteAllText(file, json);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("omp mcp.json", e);
        }
        return work;
    }

    /// A deleted chat's working folder goes with it.
    public static void Forget(string conversation)
    {
        try
        {
            string dir = WorkDir(conversation);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* Bloom's own folder; a leftover costs a few bytes */
        }
    }

    /// The status line while one of these tools runs; null for tools that aren't MCP.
    public static string? Doing(string tool) =>
        !tool.StartsWith("mcp__", StringComparison.Ordinal) ? null
        : tool == ToolName("bloom", App.Mcp.BloomTools.Name) ? Tr("PC에서 작업하는 중…", "Working on the PC…")
        : Tr("도구 쓰는 중…", "Using a tool…");

    /// What the chat's instructions say about these tools: the PC tool's hint (BloomTools.Hint names it as Claude sees it; omp
    /// joins the server and tool with one "_").
    public static string Prompt(bool pc) =>
        pc ? $"The {ToolName("bloom", App.Mcp.BloomTools.Name)} tool acts on the user's PC (find first, then act); each action waits for the user's OK in the chat. If declined, don't retry. Do only what was asked; say in one line what you did. "
            + (App.Mcp.BloomTools.GroupOn("bloom") ? "Notes, timers, reminders and events are Bloom's own: use that tool's note, timer and event for them, never a file or another app. " : "") : "";
}
