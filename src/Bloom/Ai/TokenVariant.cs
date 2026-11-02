using System;
using System.Linq;
using System.Text;

namespace Bloom.Ai;

/// What made a call cost more than its model's base price, as its log line tells it: Fast (Claude's speed "fast"), Us
/// (Claude's inference_geo "us"), Long (a prompt over its price's LongAbove, judged when the call is counted). Bits 3…6
/// hold the call's TokenEffort (TokenVariants.Effort), which no price looks at.
[Flags]
internal enum TokenVariant : byte { None = 0, Fast = 1, Us = 2, Long = 4 }

/// The effort a TokenVariant carries, and the effort a log names.
internal static class TokenVariants
{
    private const byte Mask = 0x78;
    private const int Shift = 3;

    /// The effort the call was made at; Unknown when its log named none.
    public static TokenEffort Effort(this TokenVariant v) => (TokenEffort)(((byte)v & Mask) >> Shift);

    /// `v` made at effort `e` (the price bits as they were).
    public static TokenVariant WithEffort(this TokenVariant v, TokenEffort e) => (TokenVariant)(((byte)v & ~Mask) | (((byte)e << Shift) & Mask));

    /// The effort a log writes as `utf8` ("xhigh", "max"), in any case; Unknown for a word not known here.
    public static TokenEffort Effort(ReadOnlySpan<byte> utf8)
    {
        foreach (var e in Known)
            if (Ascii.EqualsIgnoreCase(utf8, e.Utf8)) return e.Effort;
        return TokenEffort.Unknown;
    }

    private static readonly (byte[] Utf8, TokenEffort Effort)[] Known =
        Enum.GetValues<TokenEffort>().Where(e => e != TokenEffort.Unknown).Select(e => (Encoding.ASCII.GetBytes(e.ToString().ToLowerInvariant()), e)).ToArray();
}
