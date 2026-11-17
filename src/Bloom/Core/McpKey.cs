using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Bloom.Core;

/// The secret of this run of Bloom's MCP pipe (App\Mcp\McpServer). Every relay Bloom has a CLI start for it (`Bloom.exe
/// --mcp`, `--mcp-proxy`) gets it in its environment, from the MCP server entry Bloom writes for that chat, and puts it in
/// its first line; a connection without it is closed before it can ask anything. The pipe's random name alone could be
/// found by listing the pipes, and a connection that said nothing used to count as Claude Code's relay.
internal static class McpKey
{
    /// The environment variable the relays read it from.
    public const string Variable = "BLOOM_MCP_KEY";

    /// New on every start of Bloom; never logged or shown.
    public static string Value { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// The `env` of an MCP server entry that starts one of Bloom's relays.
    public static IReadOnlyDictionary<string, string> Env => new Dictionary<string, string> { [Variable] = Value };

    /// Whether a hello's key is this run's (compared in fixed time).
    public static bool Matches(string? key) =>
        key is not null && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(Value));

    /// In a relay: the key it was started with, taken out of its own environment so a server it starts never inherits it.
    public static string? Take()
    {
        string? key = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, null);
        return string.IsNullOrEmpty(key) ? null : key;
    }
}
