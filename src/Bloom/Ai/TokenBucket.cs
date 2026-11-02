using System;

namespace Bloom.Ai;

/// One sum of the index: the calls of one model and variant in one UTC hour (Hour, its start; a whole UTC day, at its
/// noon, once it is older than TokenIndex.HourDays) and one session. Provider "claude" or "codex"; Model as the log
/// names it. Session: the top session the calls belong to (a Codex subagent's thread is its parent's), shared by the
/// buckets of one snapshot; null when unknown or folded into a day.
internal readonly record struct TokenBucket(DateTime Hour, string Provider, string Model, TokenVariant Variant, TokenCounts Counts,
    TokenSessionInfo? Session = null);
