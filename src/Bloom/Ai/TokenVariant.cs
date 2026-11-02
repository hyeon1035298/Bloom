using System;

namespace Bloom.Ai;

/// What made a call cost more than its model's base price, as its log line tells it: Fast (Claude's speed "fast"), Us
/// (Claude's inference_geo "us"), Long (a prompt over its price's LongAbove, judged when the call is counted).
[Flags]
internal enum TokenVariant : byte { None = 0, Fast = 1, Us = 2, Long = 4 }
