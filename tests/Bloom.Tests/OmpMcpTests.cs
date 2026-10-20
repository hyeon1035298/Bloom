using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bloom.Ai;

namespace Bloom.Tests;

/// The MCP tools of an omp chat: the .omp/mcp.json of a conversation and what its status line and instructions say about them.
internal static class OmpMcpTests
{
    private static McpServerDef Def(string name, string source, string command, params string[] args) =>
        new(name, source, command, args, new Dictionary<string, string>(), null, null);

    public static void ConfigNamesTheConversation()
    {
        var git = new McpServerDef("git hub!", "claude", "npx", new[] { "-y", "git-mcp" }, new Dictionary<string, string> { ["TOKEN"] = "secret" }, null, null);
        var web = new McpServerDef("remote", "claude", null, new List<string>(), new Dictionary<string, string>(), "https://x.test/mcp", null);
        var imposter = Def("bloom", "claude", "evil");
        string json = OmpMcp.Config("c0ffee123456", (@"C:\Bloom\Bloom.exe", "bloom-mcp-abc"), new[] { git, web, imposter }, (@"C:\Bloom\Bloom.exe", "bloom-mcp-abc"))!;
        using var doc = JsonDocument.Parse(json);
        var servers = doc.RootElement.GetProperty("mcpServers");
        var bloom = servers.GetProperty("bloom");
        Check.Eq(bloom.GetProperty("command").GetString(), @"C:\Bloom\Bloom.exe", "Bloom's own relay");
        Check.True(bloom.GetProperty("args").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "--mcp", "bloom-mcp-abc", "c0ffee123456" }), "the relay names the pipe and this conversation");
        var proxy = servers.GetProperty("git_hub");
        Check.True(proxy.GetProperty("args").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "--mcp-proxy", "bloom-mcp-abc", "c0ffee123456", "claude", "git hub!" }), "another server goes through the asking proxy, by source and name");
        Check.True(!json.Contains("secret") && !json.Contains("git-mcp") && !json.Contains("evil"), "its command and env never sit in the file, nor an imposter's");
        Check.True(!servers.TryGetProperty("remote", out _), "a web-address server can't be put behind the proxy");
        Check.Eq(servers.EnumerateObject().Count(), 2, "bloom and one proxied server");
        Check.True(bloom.GetProperty("timeout").GetInt32() >= 600000, "a call can wait for the user's yes");
    }

    public static void ConfigWithNothingIsNull()
    {
        Check.True(OmpMcp.Config("abc", null, new List<McpServerDef>(), null) is null, "no tools: no file");
        Check.True(OmpMcp.Config("abc", null, new[] { Def("git", "claude", "npx") }, null) is null, "a server that needs asking without a relay is left out");
        Check.True(OmpMcp.Config("abc", (@"C:\b.exe", "p"), new List<McpServerDef>(), null) is not null, "Bloom's tool alone is a file");
    }

    public static void WorkFolderPerConversation()
    {
        using var data = new SettingsTransferTests.DataFolder();
        string a = OmpMcp.WorkDir("abc123"), b = OmpMcp.WorkDir("abc124");
        Check.True(a != b && Path.GetFileName(a) == "c-abc123", "each conversation has its own folder");
        Check.True(!Path.GetFileName(OmpMcp.WorkDir(@"..\..\x")).Contains('.') && !Path.GetFileName(OmpMcp.WorkDir(@"..\..\x")).Contains('\\'), "an odd id stays one folder name");
        string dir = OmpMcp.Prepare("t-mcp-test", "{\"mcpServers\":{}}");
        Check.True(File.Exists(Path.Combine(dir, ".omp", "mcp.json")), "the file is where omp reads it");
        OmpMcp.Prepare("t-mcp-test", null);
        Check.True(!File.Exists(Path.Combine(dir, ".omp", "mcp.json")), "nothing to connect: the old file goes");
        OmpMcp.Forget("t-mcp-test");
        Check.True(!Directory.Exists(dir), "a deleted chat's folder goes");
    }

    public static void ToolsShowInTheStatusLine()
    {
        Check.Eq(OmpMcp.ToolName("bloom", "pc"), "mcp__bloom_pc", "omp's name for a tool");
        Check.Eq(OmpMcp.Doing("mcp__bloom_pc"), "PC에서 작업하는 중…", "the PC tool");
        Check.Eq(OmpMcp.Doing("mcp__git_issue"), "도구 쓰는 중…", "another server's tool");
        Check.True(OmpMcp.Doing("web_search") is null, "not an MCP tool");
        Check.True(OmpSession.SystemPrompt(false, true).Contains("mcp__bloom_pc"), "the instructions name the PC tool when it is on");
        Check.True(!OmpSession.SystemPrompt(false).Contains("mcp__bloom_pc"), "and only then");
        Check.True(OmpSession.SystemPrompt(true).Contains("besides web search") && !OmpSession.SystemPrompt(false).Contains("besides web search"), "web search is said only when it is on");
    }
}
