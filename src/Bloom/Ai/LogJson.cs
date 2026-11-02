using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Bloom.Ai;

/// What ClaudeLog and CodexLog share when they read a log line: the call's key hash, model names made into strings once
/// for the whole run, a token count and a timestamp.
internal static class LogJson
{
    private const ulong Offset = 14695981039346656037, Prime = 1099511628211;

    private static readonly object Gate = new();
    private static readonly List<(byte[] Utf8, string Name)> names = [];

    /// The reader's options for a log line: deep enough for the content of any message (which is skipped, not read).
    public static readonly JsonReaderOptions Options = new() { MaxDepth = 4096 };

    /// FNV-1a 64 of `bytes` (the same in every run, unlike string.GetHashCode); `h` from Start or an earlier Hash.
    public static ulong Hash(ulong h, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            h ^= b;
            h *= Prime;
        }
        return h;
    }

    /// Where a key's hash starts.
    public static ulong Start => Offset;

    /// The string the reader stands on, made once per distinct name (a model's name repeats on every line).
    public static string Name(ref Utf8JsonReader r)
    {
        if (r.ValueIsEscaped || r.HasValueSequence) return string.Intern(r.GetString() ?? "");
        var span = r.ValueSpan;
        lock (Gate)
        {
            foreach (var (utf8, name) in names)
                if (span.SequenceEqual(utf8)) return name;
            string made = Encoding.UTF8.GetString(span);
            names.Add((span.ToArray(), made));
            return made;
        }
    }

    /// A token count: a whole number, 0 for null, a missing or a negative value.
    public static long Tokens(ref Utf8JsonReader r) => r.TokenType == JsonTokenType.Number && r.TryGetInt64(out long v) && v > 0 ? v : 0;

    /// An ISO 8601 timestamp as UTC; false for anything else.
    public static bool Time(ref Utf8JsonReader r, out DateTime utc)
    {
        utc = default;
        if (r.TokenType != JsonTokenType.String || !r.TryGetDateTimeOffset(out var at)) return false;
        utc = at.UtcDateTime;
        return true;
    }

    /// Where the string value the reader stands on lies in the line (start, length); (-1, 0) when it is not a string.
    public static (int Start, int Length) Slice(ref Utf8JsonReader r) =>
        r.TokenType == JsonTokenType.String && !r.HasValueSequence ? ((int)r.TokenStartIndex + 1, r.ValueSpan.Length) : (-1, 0);
}
