using System;

namespace Bloom.Ai;

/// One API call read from a log line. Key: an FNV-1a hash that finds the same call again (another line of the same
/// response, a resumed or forked copy of it). Time in UTC. Counts with Calls 1. Session: the session it was made in
/// (Claude Code's sessionId, a Codex rollout's thread), null when unknown; Folder: the whole path it ran in, as the line
/// names it (Claude Code's cwd; only the last name of it is kept, TokenSessionInfo).
internal readonly record struct TokenCall(ulong Key, DateTime Time, string Provider, string Model, TokenVariant Variant, TokenCounts Counts,
    string? Session = null, string? Folder = null);
