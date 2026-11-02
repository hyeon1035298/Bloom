using System;

namespace Bloom.Ai;

/// One sum of the index: the calls of one model and variant in one UTC hour (Hour, its start; a whole UTC day once it
/// is older than TokenIndex.HourDays). Provider "claude" or "codex"; Model as the log names it.
internal readonly record struct TokenBucket(DateTime Hour, string Provider, string Model, TokenVariant Variant, TokenCounts Counts);
