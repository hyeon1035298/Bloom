using System;
using System.Diagnostics;
using System.Linq;

namespace Bloom.Ai;

internal static class Cli
{
    /// Codex's own shell tool stays off in Bloom's chats and one-shot asks: its read-only sandbox still reads the whole
    /// disk and approvals are off (the chat can't answer them), so a model a page talked into it could read anything.
    /// Bloom's chats act on the PC only through Bloom's MCP tool, which asks in the chat.
    public static readonly string[] CodexNoShell = { "--disable", "shell_tool" };

    /// The `env` of a Codex MCP server entry that starts one of Bloom's relays (`-c mcp_servers.<name>.env=…`): this
    /// run's key (Core.McpKey).
    public static string CodexRelayEnv => "{ " + Core.McpKey.Variable + " = \"" + Core.McpKey.Value + "\" }";

    /// The CLIs run on their own logins only (Claude OAuth, Codex's ChatGPT login): no API keys and
    /// no routing from a Claude / Codex session that may have started us. Their config folders stay.
    public static void CleanEnvironment(ProcessStartInfo psi)
    {
        foreach (string k in psi.Environment.Keys.ToList())
            if ((k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) && !k.Equals("CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase))
                || k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase)
                || (k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && !k.Equals("CODEX_HOME", StringComparison.OrdinalIgnoreCase)))
                psi.Environment.Remove(k);
    }
}
