using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Ai;

/// How a chat's `claude -p` is started (ClaudeSession), so that Bloom alone decides what runs without a card: none of
/// the user's own Claude Code settings are loaded (their permission rules, a bypassPermissions default mode, hooks), only
/// the keys that sign in are carried over; Read reaches the chat's folder, Write its out\, WebSearch runs, and anything
/// else comes to the chat as an approval card — WebFetch too, as an address can carry away what the chat has read.
internal static class ClaudeLaunch
{
    /// Where a session's --mcp-config file goes: outside every folder a CLI is started in (Local\ai and below), as it holds
    /// the other MCP servers' env values in plain text.
    public static string McpDir => Path.Combine(Paths.Local, "claude-mcp");

    /// The settings.json keys that sign in or reach the API (an API key helper, a gateway or proxy in env, a forced login
    /// method or organization): carried over so the account works as it does in the user's terminal.
    private static readonly string[] SignIn = { "apiKeyHelper", "awsAuthRefresh", "awsCredentialExport", "env", "forceLoginMethod", "forceLoginOrgUUID" };

    /// The arguments before the MCP servers, the effort and the session to resume. `files`: the chat's folder relative to
    /// the working folder ("." when the CLI runs in it); `userSettings`: the account's settings.json text, or null.
    public static List<string> Args(string model, string prompt, string files, string name, bool fast, string? userSettings) => new()
    {
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
        "--model", model, "--system-prompt", prompt,
        "--tools", "WebSearch,WebFetch,Read,Write", "--allowedTools", $"WebSearch,Read({Under(files, "**")}),Edit({Under(files, "out/**")})", // Edit rules cover Write
        "--strict-mcp-config", "--disable-slash-commands", "--name", name,
        // "default" rather than its newer name "manual", which older CLIs refuse; no setting source: no rule of the user's
        // own allows anything here
        "--permission-mode", "default", "--setting-sources", "", "--permission-prompt-tool", "stdio",
        "--settings", Settings(fast, userSettings),
    };

    /// A path pattern under the chat's folder as the session sees it.
    public static string Under(string files, string sub) => files == "." ? "./" + sub : $"./{files}/{sub}";

    /// The --settings JSON: hooks off (and thinking, `fast`), with the sign-in keys of the user's settings.json.
    public static string Settings(bool fast, string? userSettings)
    {
        var o = new JsonObject();
        if (userSettings is not null)
        {
            try
            {
                if (JsonNode.Parse(userSettings, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is JsonObject theirs)
                    foreach (string key in SignIn)
                        if (theirs[key] is { } value) o[key] = value.DeepClone();
            }
            catch (JsonException)
            {
                /* a file Claude Code can't read either */
            }
        }
        o["disableAllHooks"] = true;
        if (fast) o["alwaysThinkingEnabled"] = false;
        return o.ToJsonString();
    }

    /// The account's settings.json (`home`: its CLAUDE_CONFIG_DIR, else Claude Code's own folder); null when there is none.
    public static string? UserSettings(string? home)
    {
        try
        {
            string file = Path.Combine(home ?? ClaudeWatch.Home, "settings.json");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// A can_use_tool request as the chat's card shows it. A page fetch names the host it goes to and shows the whole
    /// address; 이 대화에서 항상 허용 keeps that host only. An address that names no web host is asked every time.
    public static ToolAsk Worded(ToolAsk ask)
    {
        if (ask.Tool != "WebFetch") return ask;
        string url = "", prompt = "";
        try
        {
            using var doc = JsonDocument.Parse(ask.Input);
            url = Str(doc.RootElement, "url");
            prompt = Str(doc.RootElement, "prompt");
        }
        catch (JsonException)
        {
            /* asked about as it came */
        }
        string line = ToolText.Line(url);
        string detail = Tr("주소", "Address") + ": " + ToolText.Visible(url)
            + (prompt.Length > 0 ? "\n" + Tr("읽을 내용", "What to read") + ": " + ToolText.Visible(prompt) : "");
        return Host(url) is { } host
            ? ask with { Says = Tr($"{host} 페이지 열기: {line}", $"Open a page on {host}: {line}"), Key = "WebFetch:" + host, Detail = detail }
            : ask with { Says = Tr($"페이지 열기: {line}", $"Open a page: {line}"), Key = "WebFetch", Detail = detail, MustAsk = true };
    }

    /// The host a fetch goes to, as the card names it and 항상 허용 keeps it: in ASCII (an internationalized name in its
    /// xn-- form, so a look-alike shows as one), lower case, with a port that isn't the scheme's own; null for anything
    /// but an http(s) address.
    public static string? Host(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.IdnHost.Length > 0
            ? (u.IsDefaultPort ? u.IdnHost : $"{u.IdnHost}:{u.Port}").ToLowerInvariant()
            : null;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
