using System;
using System.IO;
using Bloom.Core;

namespace Bloom.Ai;

/// One session in the index's table (TokenIndex): Provider "claude" or "codex"; Id: Claude Code's sessionId or a Codex
/// rollout's thread; Parent: the thread a Codex subagent or guardian was started from (its calls show under the top
/// one, TokenIndex.Snapshot); Title and its Kind: the name Claude Code (custom-title, else ai-title) or Codex
/// (session_index.jsonl) gave it, at most TitleMost characters on one line, never for Bloom's own; Folder: the last name
/// of the folder it ran in (a drive's root as "D:"), never the whole path; Bloom: started by Bloom's AI chat; Seen: the
/// latest hour (since 0001-01-01, UTC) a call of it was counted or its log written. An entry is never changed: a new one
/// takes its place, so a snapshot handed to the UI thread stays as it was.
internal sealed record TokenSessionInfo(string Provider, string Id, string? Parent, string? Title, TokenSessionInfo.Named Kind,
    string? Folder, bool Bloom, int Seen)
{
    /// Where a title came from; a custom one is not replaced by an ai one.
    public enum Named : byte { None, Ai, Custom }

    public const int TitleMost = 64;

    /// The title as kept: one line, trimmed, at most TitleMost characters; null when nothing is left.
    public static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var chars = title.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i])) chars[i] = ' ';
        string one = new string(chars).Trim();
        if (one.Length > TitleMost) one = one[..(char.IsHighSurrogate(one[TitleMost - 1]) ? TitleMost - 1 : TitleMost)].TrimEnd();
        return one.Length == 0 ? null : one;
    }

    /// The last name of `path` ("D:\Claude\Bloom\" → "Bloom", "D:\" → "D:"); null for none.
    public static string? Leaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string t = path.Trim().TrimEnd('\\', '/');
        int i = t.LastIndexOfAny(['\\', '/']);
        string leaf = i < 0 ? t : t[(i + 1)..];
        return leaf.Length == 0 ? null : leaf;
    }

    /// How Bloom names the Claude Code sessions of its AI chats (Chats): a custom title starting so is Bloom's own.
    public const string BloomTitle = "Bloom · ";

    /// `cwd` is Bloom's AI working folder or below it, where its chats and quick jobs run Claude Code: this copy's
    /// (Paths.Local\ai), the installed one's (%LOCALAPPDATA%\Bloom\ai) or a dev copy's (--data <dir>: <dir>\local\ai).
    public static bool IsBloom(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return false;
        string path = cwd.Trim().TrimEnd('\\', '/').Replace('/', '\\');
        if (Under(path, BloomAi.Value.Own) || Under(path, BloomAi.Value.Installed)) return true;
        const string dev = @"\local\ai";
        for (int i = path.IndexOf(dev, StringComparison.OrdinalIgnoreCase); i >= 0; i = path.IndexOf(dev, i + 1, StringComparison.OrdinalIgnoreCase))
            if (i + dev.Length == path.Length || path[i + dev.Length] == '\\') return true;
        return false;
    }

    private static readonly Lazy<(string Own, string Installed)> BloomAi = new(() =>
        (Paths.Local.Length == 0 ? "" : Path.Combine(Paths.Local, "ai"), Path.Combine(Paths.LocalAppData, "Bloom", "ai")));

    private static bool Under(string path, string root) =>
        root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (path.Length == root.Length || path[root.Length] == '\\');
}
