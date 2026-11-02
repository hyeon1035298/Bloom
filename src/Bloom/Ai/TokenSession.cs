using System;
using System.Collections.Generic;

namespace Bloom.Ai;

/// One session in a TokenView: a Claude Code session (by its lines' sessionId, its subagents' calls in it) or a Codex
/// thread (its subagent and guardian threads folded in). Title: the name Claude Code (custom-title, else ai-title) or
/// Codex (session_index.jsonl's thread_name) gave it; null when there is none, and always for Bloom's own. Folder: the
/// last name of the folder it ran in, null when unknown. Bloom: started by Bloom's AI chat (its name is the chat's,
/// found in Chats by Id; Provider "codex" is the chat's "chatgpt"). Cost: priced models only, null when none of its
/// models has a price; UnpricedTokens: its tokens of models with no price. Share: of the view's Sum.Total, 0…1. First,
/// Last: the start of its first and last hour in the period, local. Models: its models (date tail off), most tokens
/// first. Efforts: its tokens by effort, the highest effort first, Unknown last.
internal sealed record TokenSession(string Provider, string Id, string? Title, string? Folder, bool Bloom,
    TokenCounts Counts, double? Cost, long UnpricedTokens, double Share, DateTime First, DateTime Last,
    IReadOnlyList<(string Model, long Tokens)> Models, IReadOnlyList<(TokenEffort Effort, long Tokens)> Efforts);
