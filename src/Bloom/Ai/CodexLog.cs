using System;
using System.Text;
using System.Text.Json;

namespace Bloom.Ai;

/// One line of a Codex rollout (<home>\sessions\YYYY\MM\DD\rollout-<time>-<uuid>.jsonl) read as an API call. The model
/// is the last turn_context's (kept in the file's TokenFile, so that a file read in parts keeps it). A newer CLI writes
/// one token_usage_record per call, keyed by its response_id; an older one only event_msg/token_count, whose
/// last_token_usage is the call (a line repeating the total before it, or with no info, is none). Once a file has a
/// record its token_count lines are not counted: the CLI writes both for each call. Codex's input_tokens holds the
/// cached and cache-written tokens; its output_tokens holds the reasoning.
internal static class CodexLog
{
    /// The call on `line` (starting `at` bytes into the file), or null; a turn_context or a record changes `file`.
    public static TokenCall? Parse(ReadOnlySpan<byte> line, TokenFile file, long at = 0)
    {
        if (line.IndexOf("\"turn_context\""u8) < 0 && line.IndexOf("\"token_usage_record\""u8) < 0 && line.IndexOf("\"token_count\""u8) < 0) return null;
        try
        {
            return Read(line, file, at);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TokenCall? Read(ReadOnlySpan<byte> line, TokenFile file, long at)
    {
        var r = new Utf8JsonReader(line, LogJson.Options);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
        DateTime time = default;
        bool timed = false;
        Kind kind = Kind.Other;
        (int Start, int Length) ordinal = (-1, 0), payload = (-1, 0);
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("type"u8))
            {
                r.Read();
                kind = r.TokenType != JsonTokenType.String ? Kind.Other
                    : r.ValueTextEquals("turn_context"u8) ? Kind.Context
                    : r.ValueTextEquals("token_usage_record"u8) ? Kind.Record
                    : r.ValueTextEquals("event_msg"u8) ? Kind.Event : Kind.Other;
            }
            else if (r.ValueTextEquals("timestamp"u8))
            {
                r.Read();
                timed = LogJson.Time(ref r, out time);
            }
            else if (r.ValueTextEquals("ordinal"u8))
            {
                r.Read();
                ordinal = r.TokenType == JsonTokenType.Number ? ((int)r.TokenStartIndex, r.ValueSpan.Length) : (-1, 0);
            }
            else if (r.ValueTextEquals("payload"u8))
            {
                r.Read();
                int start = (int)r.TokenStartIndex;
                r.Skip();
                payload = (start, (int)r.BytesConsumed - start); // read once the line's type is known
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
        if (payload.Start < 0) return null;
        var p = line.Slice(payload.Start, payload.Length);
        switch (kind)
        {
            case Kind.Context:
                if (ContextModel(p) is { } model) file.Model = model;
                return null;
            case Kind.Record:
                file.Records = true;
                if (!timed || !Record(p, out var id, out var counts)) return null;
                ulong key = id.Start >= 0 ? LogJson.Hash(LogJson.Hash(LogJson.Start, "x|"u8), p.Slice(id.Start, id.Length)) : Own(file, line, ordinal, at);
                return new TokenCall(key, time, "codex", file.Model ?? Unknown, TokenVariant.None, counts);
            case Kind.Event:
                if (file.Records || !timed || !Count(p, file, out var last)) return null;
                return new TokenCall(Own(file, line, ordinal, at), time, "codex", file.Model ?? Unknown, TokenVariant.None, last);
            default:
                return null;
        }
    }

    /// The model of a call made before any turn_context named one.
    public const string Unknown = "unknown";

    private enum Kind { Other, Context, Record, Event }

    /// A call with no id of its own: the thread's uuid and the line's ordinal (or, before ordinals, its place in the file).
    private static ulong Own(TokenFile file, ReadOnlySpan<byte> line, (int Start, int Length) ordinal, long at)
    {
        ulong h = LogJson.Hash(LogJson.Hash(LogJson.Start, "x|"u8), Encoding.UTF8.GetBytes(file.Thread ?? ""));
        h = LogJson.Hash(h, "|"u8);
        return ordinal.Start >= 0 ? LogJson.Hash(h, line.Slice(ordinal.Start, ordinal.Length)) : LogJson.Hash(LogJson.Hash(h, "@"u8), Encoding.ASCII.GetBytes(at.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// turn_context's model, or its collaboration_mode.settings.model.
    private static string? ContextModel(ReadOnlySpan<byte> payload)
    {
        var r = new Utf8JsonReader(payload, LogJson.Options);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
        string? model = null, mode = null;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("model"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.String && r.ValueSpan.Length > 0) model = LogJson.Name(ref r);
            }
            else if (r.ValueTextEquals("collaboration_mode"u8))
            {
                r.Read();
                mode = Nested(ref r, "settings"u8, "model"u8);
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
        return model ?? mode;
    }

    /// The string at outer.inner below the object the reader stands on (the object is read to its end).
    private static string? Nested(ref Utf8JsonReader r, ReadOnlySpan<byte> outer, ReadOnlySpan<byte> inner)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            r.Skip();
            return null;
        }
        string? found = null;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            bool match = r.ValueTextEquals(outer);
            r.Read();
            if (!match || r.TokenType != JsonTokenType.StartObject)
            {
                r.Skip();
                continue;
            }
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                bool hit = r.ValueTextEquals(inner);
                r.Read();
                if (hit && r.TokenType == JsonTokenType.String && r.ValueSpan.Length > 0) found = LogJson.Name(ref r);
                else r.Skip();
            }
        }
        return found;
    }

    /// A token_usage_record's response_id (its place in the payload) and usage.
    private static bool Record(ReadOnlySpan<byte> payload, out (int Start, int Length) id, out TokenCounts counts)
    {
        id = (-1, 0);
        counts = default;
        bool found = false;
        var r = new Utf8JsonReader(payload, LogJson.Options);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return false;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("response_id"u8))
            {
                r.Read();
                id = LogJson.Slice(ref r);
            }
            else if (r.ValueTextEquals("usage"u8))
            {
                r.Read();
                found = Usage(ref r, out counts);
            }
            else
            {
                r.Read();
                r.Skip(); // turn_token_usage above all: the turn's sum, not this call's
            }
        }
        return found;
    }

    /// An event_msg that is a token_count with info, whose total differs from the last one counted in the file: its
    /// last_token_usage. The total is remembered by a hash of its text.
    private static bool Count(ReadOnlySpan<byte> payload, TokenFile file, out TokenCounts last)
    {
        last = default;
        var r = new Utf8JsonReader(payload, LogJson.Options);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return false;
        bool tokenCount = false, haveLast = false;
        ulong total = 0;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("type"u8))
            {
                r.Read();
                tokenCount = r.TokenType == JsonTokenType.String && r.ValueTextEquals("token_count"u8);
            }
            else if (r.ValueTextEquals("info"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    r.Skip();
                    continue;
                }
                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (r.ValueTextEquals("total_token_usage"u8))
                    {
                        r.Read();
                        int start = (int)r.TokenStartIndex;
                        r.Skip();
                        total = LogJson.Hash(LogJson.Start, payload[start..(int)r.BytesConsumed]);
                    }
                    else if (r.ValueTextEquals("last_token_usage"u8))
                    {
                        r.Read();
                        haveLast = Usage(ref r, out last);
                    }
                    else
                    {
                        r.Read();
                        r.Skip();
                    }
                }
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
        if (!tokenCount || !haveLast || total == 0 || total == file.LastTotal) return false;
        file.LastTotal = total;
        return true;
    }

    /// A usage object in TokenCounts' terms: the input that neither came from nor went to the cache is input_tokens less
    /// cached_input_tokens and cache_write_input_tokens.
    private static bool Usage(ref Utf8JsonReader r, out TokenCounts counts)
    {
        counts = default;
        if (r.TokenType != JsonTokenType.StartObject)
        {
            r.Skip();
            return false;
        }
        long input = 0, cached = 0, written = 0, output = 0;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("input_tokens"u8)) input = Next(ref r);
            else if (r.ValueTextEquals("cached_input_tokens"u8)) cached = Next(ref r);
            else if (r.ValueTextEquals("cache_write_input_tokens"u8)) written = Next(ref r);
            else if (r.ValueTextEquals("output_tokens"u8)) output = Next(ref r);
            else
            {
                r.Read();
                r.Skip();
            }
        }
        counts = new TokenCounts(1, Math.Max(0, input - cached - written), output, cached, written, 0);
        return true;
    }

    private static long Next(ref Utf8JsonReader r)
    {
        r.Read();
        long v = LogJson.Tokens(ref r);
        r.Skip();
        return v;
    }
}
