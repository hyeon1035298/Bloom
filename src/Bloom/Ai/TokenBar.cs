using System;

namespace Bloom.Ai;

/// One bar: its start in local time, the sums in it and their cost (priced models only).
internal readonly record struct TokenBar(DateTime Start, TokenCounts Counts, double Cost);
