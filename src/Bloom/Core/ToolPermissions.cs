using System;
using System.Collections.Generic;

namespace Bloom.Core;

/// 설정 > PC 작업 · MCP > 권한: per action 자동 (Auto), 매번 묻기 (Ask), 항상 허용 (Allow) or 사용 안 함 (Off), kept in
/// Settings.Permissions under the action's key — the key 이 대화에서 항상 허용 remembers too (ToolAsk.Key), with every
/// action of Bloom's pc tool ("pc:move", "pc:note"…) under "pc". The four file and command actions are an API chat's
/// (ApiToolbox); "pc" is Bloom's own tool in every chat; "mcp" is the other MCP servers' tools in an API chat (each
/// tool's key "mcp:<function>", ApiMcp); "claude" is a Claude chat's own tools (Read, Write, WebFetch) and its other MCP
/// servers' ("mcp__<server>__<tool>"). Other tools ask as before.
internal static class ToolPermissions
{
    public const string Auto = "auto", Ask = "ask", Allow = "allow", Off = "off";

    public const string ReadOutside = "file:read", Write = "file:write", Edit = "file:edit", Command = "command", Pc = "pc", Mcp = "mcp", Claude = "claude";

    /// The four an API chat's file and command tools ask under (read: outside the chat's folder only).
    public static readonly string[] FileActions = { ReadOutside, Write, Edit, Command };

    /// The actions 자동 is offered for, and their default (as Claude Code's auto mode, the rules being what overrides it):
    /// Bloom judges its own pc tool (IToolPolicy.AutoAsks), Claude Code a Claude chat's tools. An API chat's file, command
    /// and MCP tools have nothing to judge them, so they ask unless set otherwise.
    public static readonly string[] AutoActions = { Pc, Claude };

    /// The action a ToolAsk.Key falls under; null for a tool these rules don't cover.
    public static string? ActionOf(string key) =>
        key == Pc || key.StartsWith(Pc + ":", StringComparison.Ordinal) ? Pc
        : key == Mcp || key.StartsWith(Mcp + ":", StringComparison.Ordinal) ? Mcp
        : Array.IndexOf(FileActions, key) >= 0 ? key
        : key is Claude or "Read" or "Write" or "Edit" or "WebFetch" || key.StartsWith("WebFetch:", StringComparison.Ordinal) || key.StartsWith("mcp__", StringComparison.Ordinal) ? Claude
        : null;

    /// An action's rule when none is set: 자동 where it is offered, else 매번 묻기.
    public static string DefaultOf(string action) => Array.IndexOf(AutoActions, action) >= 0 ? Auto : Ask;

    /// The rule for a key in `rules` (a snapshot of Settings.Permissions); its default when none is set (or one it
    /// doesn't offer), Ask for a tool these rules don't cover.
    public static string Of(IReadOnlyDictionary<string, string> rules, string key)
    {
        if (ActionOf(key) is not { } action) return Ask;
        string fallback = DefaultOf(action);
        return rules.TryGetValue(action, out var rule) && (rule is Allow or Off || rule == Ask && fallback == Auto) ? rule : fallback;
    }

    /// The rule as set now.
    public static string Of(string key) => Of(Settings.Current.Permissions, key);

    /// A file or command action: its card says 파일 · 명령 승인 rather than PC 작업 승인.
    public static bool IsFileOrCommand(string key) => ActionOf(key) is { } action && Array.IndexOf(FileActions, action) >= 0;

    /// Another MCP server's tool in an API chat: its card says MCP 도구 승인.
    public static bool IsMcp(string key) => ActionOf(key) == Mcp;
}
