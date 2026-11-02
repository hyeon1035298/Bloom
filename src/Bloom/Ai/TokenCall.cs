using System;

namespace Bloom.Ai;

/// One API call read from a log line. Key: an FNV-1a hash that finds the same call again (another line of the same
/// response, a resumed or forked copy of it). Time in UTC. Counts with Calls 1.
internal readonly record struct TokenCall(ulong Key, DateTime Time, string Provider, string Model, TokenVariant Variant, TokenCounts Counts);
