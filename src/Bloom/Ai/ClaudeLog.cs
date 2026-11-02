using System;
using System.Text.Json;

namespace Bloom.Ai;

/// One line of a Claude Code session log (<home>\projects\**\*.jsonl) read as an API call. Only an "assistant" line with
/// message.usage counts; the model is message.model (never a "model" in the content or a tool's input, which is skipped
/// unread), and Claude Code's own "<synthetic>" replies are no calls. One response is written as one line per content
/// block, and a resumed or forked session copies the lines into a new file: the key message.id|requestId (message.id
/// alone without a requestId, the line's uuid without either) finds them all (TokenIndex.Add). The line's own
/// (top-level) effort, sessionId and cwd go with the call: a resumed copy and a subagent's lines keep the sessionId of
/// the session they came from. A custom-title or ai-title line names a session (Title); no other line's text is read.
internal static class ClaudeLog
{
    /// The call on `line`, or null for any other line (most are dropped by their bytes, before any JSON is read).
    /// `strings`: the file's, so that a sessionId or cwd the line before had is not made again.
    public static TokenCall? Parse(ReadOnlySpan<byte> line, Strings? strings = null)
    {
        if (line.IndexOf("\"usage\""u8) < 0 || line.IndexOf("\"assistant\""u8) < 0) return null;
        try
        {
            return Read(line, strings ?? new Strings());
        }
        catch (JsonException)
        {
            return null; // a line cut off or broken: not a call
        }
    }

    private static TokenCall? Read(ReadOnlySpan<byte> line, Strings strings)
    {
        var r = new Utf8JsonReader(line, LogJson.Options);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
        bool assistant = false, timed = false;
        DateTime time = default;
        var effort = TokenEffort.Unknown;
        string? session = null, cwd = null;
        (int Start, int Length) request = (-1, 0), uuid = (-1, 0);
        var message = new Message();
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("type"u8))
            {
                r.Read();
                assistant = r.TokenType == JsonTokenType.String && r.ValueTextEquals("assistant"u8);
            }
            else if (r.ValueTextEquals("timestamp"u8))
            {
                r.Read();
                timed = LogJson.Time(ref r, out time);
            }
            else if (r.ValueTextEquals("requestId"u8))
            {
                r.Read();
                request = LogJson.Slice(ref r);
            }
            else if (r.ValueTextEquals("uuid"u8))
            {
                r.Read();
                uuid = LogJson.Slice(ref r);
            }
            else if (r.ValueTextEquals("message"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.StartObject) message.Read(ref r);
                else r.Skip();
            }
            else if (r.ValueTextEquals("effort"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.String && !r.HasValueSequence) effort = TokenVariants.Effort(r.ValueSpan);
            }
            else if (r.ValueTextEquals("sessionId"u8))
            {
                r.Read();
                session = strings.Session(ref r);
            }
            else if (r.ValueTextEquals("cwd"u8))
            {
                r.Read();
                cwd = strings.Cwd(ref r);
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
        if (!assistant || !timed || !message.Usage || message.Model is null or "<synthetic>") return null;
        ulong key = LogJson.Hash(LogJson.Start, "c|"u8);
        if (message.Id.Start >= 0)
        {
            key = LogJson.Hash(key, line.Slice(message.Id.Start, message.Id.Length));
            if (request.Start >= 0) key = LogJson.Hash(LogJson.Hash(key, "|"u8), line.Slice(request.Start, request.Length));
        }
        else if (uuid.Start >= 0) key = LogJson.Hash(key, line.Slice(uuid.Start, uuid.Length));
        else return null;
        return new TokenCall(key, time, "claude", message.Model, message.Variant.WithEffort(effort), message.Counts, session, cwd);
    }

    /// The session a custom-title or ai-title line names and the title it gives (one line, cut to its most); null for
    /// any other line, or a title with nothing in it. Only the line's own type and fields are read.
    public static (string? Session, string Title, bool Custom)? Title(ReadOnlySpan<byte> line)
    {
        try
        {
            var r = new Utf8JsonReader(line, LogJson.Options);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
            int type = 0; // 1: custom-title, 2: ai-title
            string? custom = null, ai = null, session = null;
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                if (r.ValueTextEquals("type"u8))
                {
                    r.Read();
                    type = r.TokenType != JsonTokenType.String ? 0 : r.ValueTextEquals("custom-title"u8) ? 1 : r.ValueTextEquals("ai-title"u8) ? 2 : 0;
                    if (type == 0) return null;
                }
                else if (r.ValueTextEquals("customTitle"u8))
                {
                    r.Read();
                    custom = r.TokenType == JsonTokenType.String ? r.GetString() : null;
                }
                else if (r.ValueTextEquals("aiTitle"u8))
                {
                    r.Read();
                    ai = r.TokenType == JsonTokenType.String ? r.GetString() : null;
                }
                else if (r.ValueTextEquals("sessionId"u8))
                {
                    r.Read();
                    session = r.TokenType == JsonTokenType.String ? r.GetString() : null;
                }
                else
                {
                    r.Read();
                    r.Skip();
                }
            }
            string? title = TokenSessionInfo.Clean(type == 1 ? custom : type == 2 ? ai : null);
            return title is null ? null : (string.IsNullOrEmpty(session) ? null : session, title, type == 1);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// The strings of one file's lines that repeat from line to line (sessionId, cwd), each made again only when its
    /// bytes change.
    public sealed class Strings
    {
        private byte[]? sessionBytes, cwdBytes;
        private string? session, cwd;

        public string? Session(ref Utf8JsonReader r) => Get(ref r, ref sessionBytes, ref session);

        public string? Cwd(ref Utf8JsonReader r) => Get(ref r, ref cwdBytes, ref cwd);

        private static string? Get(ref Utf8JsonReader r, ref byte[]? bytes, ref string? value)
        {
            if (r.TokenType != JsonTokenType.String || r.HasValueSequence) return null;
            var span = r.ValueSpan;
            if (span.Length == 0) return null;
            if (bytes is not null && span.SequenceEqual(bytes)) return value;
            value = r.GetString();
            bytes = span.ToArray();
            return value;
        }
    }

    /// The fields of `message` that count: its id, model and usage.
    private struct Message
    {
        public (int Start, int Length) Id;
        public string? Model;
        public bool Usage;
        public TokenCounts Counts;
        public TokenVariant Variant;

        public Message() => Id = (-1, 0);

        public void Read(ref Utf8JsonReader r)
        {
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                if (r.ValueTextEquals("id"u8))
                {
                    r.Read();
                    Id = LogJson.Slice(ref r);
                }
                else if (r.ValueTextEquals("model"u8))
                {
                    r.Read();
                    Model = r.TokenType == JsonTokenType.String ? LogJson.Name(ref r) : null;
                }
                else if (r.ValueTextEquals("usage"u8))
                {
                    r.Read();
                    if (r.TokenType == JsonTokenType.StartObject) ReadUsage(ref r);
                    else r.Skip();
                }
                else
                {
                    r.Read();
                    r.Skip(); // the content above all: text, thinking and tool input are never read
                }
            }
        }

        /// input_tokens leaves out the cache; output_tokens holds the thinking. The cache writes come split by how long
        /// they last (cache_creation); a line from before that split has only their sum, taken as 5-minute writes.
        private void ReadUsage(ref Utf8JsonReader r)
        {
            Usage = true;
            long input = 0, output = 0, read = 0, written = 0, minutes5 = 0, hour1 = 0;
            bool split = false;
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                if (r.ValueTextEquals("input_tokens"u8)) input = Next(ref r);
                else if (r.ValueTextEquals("output_tokens"u8)) output = Next(ref r);
                else if (r.ValueTextEquals("cache_read_input_tokens"u8)) read = Next(ref r);
                else if (r.ValueTextEquals("cache_creation_input_tokens"u8)) written = Next(ref r);
                else if (r.ValueTextEquals("cache_creation"u8))
                {
                    r.Read();
                    if (r.TokenType != JsonTokenType.StartObject)
                    {
                        r.Skip();
                        continue;
                    }
                    split = true;
                    while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                    {
                        if (r.ValueTextEquals("ephemeral_5m_input_tokens"u8)) minutes5 = Next(ref r);
                        else if (r.ValueTextEquals("ephemeral_1h_input_tokens"u8)) hour1 = Next(ref r);
                        else
                        {
                            r.Read();
                            r.Skip();
                        }
                    }
                }
                else if (r.ValueTextEquals("speed"u8))
                {
                    r.Read();
                    if (r.TokenType == JsonTokenType.String && r.ValueTextEquals("fast"u8)) Variant |= TokenVariant.Fast;
                }
                else if (r.ValueTextEquals("inference_geo"u8))
                {
                    r.Read();
                    if (r.TokenType == JsonTokenType.String && r.ValueTextEquals("us"u8)) Variant |= TokenVariant.Us;
                }
                else
                {
                    r.Read();
                    r.Skip();
                }
            }
            Counts = split ? new TokenCounts(1, input, output, read, minutes5, hour1) : new TokenCounts(1, input, output, read, written, 0);
        }

        private static long Next(ref Utf8JsonReader r)
        {
            r.Read();
            long v = LogJson.Tokens(ref r);
            r.Skip();
            return v;
        }
    }
}
