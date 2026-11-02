using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bloom.Ai;

namespace Bloom.Tests;

/// The log lines the token blocks count (Ai\ClaudeLog, Ai\CodexLog) and the reader under them (Ai\LogLines): which
/// lines are calls, which fields are read and how the two CLIs' fields fold into TokenCounts, the keys that find a call
/// again, and whole lines only, as bytes, from an offset.
internal static class TokenLogTests
{
    private static ReadOnlySpan<byte> B(string line) => Encoding.UTF8.GetBytes(line);

    private static string Claude(string usage, string model = "claude-opus-5-5", string id = "msg_1", string? request = "req_1", string type = "assistant", string content = "[]") =>
        $$"""{"parentUuid":"p","message":{"model":"{{model}}","id":"{{id}}","type":"message","role":"assistant","content":{{content}},"usage":{{usage}}},{{(request is null ? "" : $"\"requestId\":\"{request}\",")}}"type":"{{type}}","uuid":"u-1","timestamp":"2026-11-02T10:20:30.123Z"}""";

    private const string Usage = """{"input_tokens":2,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":50,"cache_creation":{"ephemeral_1h_input_tokens":200,"ephemeral_5m_input_tokens":100},"inference_geo":"global","speed":"standard"}""";

    public static void ClaudeUsageIsRead()
    {
        var call = ClaudeLog.Parse(B(Claude(Usage))) ?? throw new InvalidOperationException("no call");
        Check.Eq(call.Counts, new TokenCounts(1, 2, 50, 4000, 100, 200), "input, output, cache read, 5-minute and 1-hour writes");
        Check.Eq(call.Model, "claude-opus-5-5", "message.model");
        Check.Eq(call.Provider, "claude", "the provider");
        Check.Eq(call.Time, new DateTime(2026, 11, 2, 10, 20, 30, 123, DateTimeKind.Utc), "the timestamp in UTC");
        Check.Eq(call.Time.Kind, DateTimeKind.Utc, "as UTC");
        Check.Eq(call.Variant, TokenVariant.None, "standard speed, global");

        var old = ClaudeLog.Parse(B(Claude("""{"input_tokens":1,"cache_creation_input_tokens":70,"cache_read_input_tokens":9,"output_tokens":3}""")))!.Value;
        Check.Eq(old.Counts, new TokenCounts(1, 1, 3, 9, 70, 0), "a line from before the split: its writes as 5-minute ones");
        var nulls = ClaudeLog.Parse(B(Claude("""{"input_tokens":null,"output_tokens":5}""")))!.Value;
        Check.Eq(nulls.Counts, new TokenCounts(1, 0, 5, 0, 0, 0), "null and missing fields as 0");
    }

    public static void ClaudeFastAndUsAreMarked()
    {
        var fast = ClaudeLog.Parse(B(Claude("""{"input_tokens":1,"output_tokens":1,"speed":"fast","inference_geo":"us"}""")))!.Value;
        Check.Eq(fast.Variant, TokenVariant.Fast | TokenVariant.Us, "speed fast and inference_geo us");
    }

    public static void ClaudeOtherLinesAreNoCalls()
    {
        Check.True(ClaudeLog.Parse(B(Claude(Usage, model: "<synthetic>"))) is null, "Claude Code's own <synthetic> reply");
        Check.True(ClaudeLog.Parse(B(Claude(Usage, type: "user"))) is null, "a line that is not the assistant's");
        string progress = """{"type":"progress","data":{"message":{"type":"assistant","message":{"model":"claude-opus-5-5","id":"m","usage":{"input_tokens":5,"output_tokens":5}}}},"uuid":"x","timestamp":"2026-11-02T10:00:00Z"}""";
        Check.True(ClaudeLog.Parse(B(progress)) is null, "an assistant message nested in another line");
        Check.True(ClaudeLog.Parse(B("""{"type":"assistant","message":{"model":"claude-opus-5-5","id":"m"},"timestamp":"2026-11-02T10:00:00Z","note":"usage"}""")) is null, "no usage");
        Check.True(ClaudeLog.Parse(B(Claude(Usage)[..120])) is null, "a line cut off");
        Check.True(ClaudeLog.Parse(B("""{"type":"summary","summary":"x"}""")) is null, "a line dropped by its bytes");
    }

    public static void ClaudeContentIsNotRead()
    {
        // a "model" and a "usage" in the text and in a tool's input, before the message's own model
        string content = """[{"type":"text","text":"\"model\":\"opus\" \"usage\":{\"input_tokens\":999}"},{"type":"tool_use","name":"Agent","input":{"model":"sonnet","usage":{"input_tokens":777,"output_tokens":777},"nested":[[[{"deep":true}]]]}}]""";
        string line = $$"""{"message":{"content":{{content}},"id":"msg_9","usage":{"input_tokens":3,"output_tokens":4},"model":"claude-sonnet-5-5"},"requestId":"r","type":"assistant","uuid":"u","timestamp":"2026-11-02T10:00:00Z"}""";
        var call = ClaudeLog.Parse(B(line))!.Value;
        Check.Eq(call.Model, "claude-sonnet-5-5", "message.model, after the content");
        Check.Eq(call.Counts, new TokenCounts(1, 3, 4, 0, 0, 0), "message.usage only");
    }

    public static void ClaudeKeysFindTheSameCall()
    {
        ulong a = ClaudeLog.Parse(B(Claude(Usage)))!.Value.Key;
        Check.Eq(ClaudeLog.Parse(B(Claude("""{"input_tokens":2,"output_tokens":90}""", content: """[{"type":"text","text":"more"}]""")))!.Value.Key, a, "another line of the same response");
        Check.True(ClaudeLog.Parse(B(Claude(Usage, request: "req_2")))!.Value.Key != a, "another request");
        ulong alone = ClaudeLog.Parse(B(Claude(Usage, request: null)))!.Value.Key;
        Check.True(alone != a, "no requestId: message.id alone");
        Check.Eq(ClaudeLog.Parse(B(Claude(Usage, request: null)))!.Value.Key, alone, "the same each time");
        string noId = """{"message":{"model":"claude-opus-5-5","usage":{"input_tokens":1,"output_tokens":1}},"type":"assistant","uuid":"u-7","timestamp":"2026-11-02T10:00:00Z"}""";
        string noId2 = noId.Replace("u-7", "u-8", StringComparison.Ordinal);
        Check.True(ClaudeLog.Parse(B(noId))!.Value.Key != ClaudeLog.Parse(B(noId2))!.Value.Key, "no message.id: the line's uuid");
        Check.True(ClaudeLog.Parse(B(Claude(Usage, id: "msg_1x")))!.Value.Key != a, "another message");
    }

    public static void ClaudeEffortAndSessionAreTheLinesOwn()
    {
        const string message = """{"model":"claude-opus-5-5","id":"m","usage":{"input_tokens":1,"output_tokens":1,"speed":"fast"},"content":[{"type":"text","text":"\"effort\":\"max\""},{"type":"tool_use","input":{"effort":"max","sessionId":"x","cwd":"C:\\no"}}]}""";
        string Line(string tail) => """{"parentUuid":"p","message":@M,"requestId":"r","type":"assistant","uuid":"u","timestamp":"2026-11-02T10:00:00Z"@T}""".Replace("@M", message, StringComparison.Ordinal).Replace("@T", tail, StringComparison.Ordinal);
        var call = ClaudeLog.Parse(B(Line(""","effort":"xhigh","perTurnEffort":"low","sessionId":"0b1c","cwd":"D:\\Work\\Repo","version":"2.1.300" """)))!.Value;
        Check.Eq(call.Variant.Effort(), TokenEffort.XHigh, "the top-level effort, after the message");
        Check.True(call.Variant.HasFlag(TokenVariant.Fast), "beside fast mode");
        Check.Eq((call.Session, call.Folder), ("0b1c", @"D:\Work\Repo"), "sessionId and cwd");
        var bare = ClaudeLog.Parse(B(Line("")))!.Value;
        Check.Eq(bare.Variant.Effort(), TokenEffort.Unknown, "none named: not the text's or a tool input's");
        Check.True(bare.Session is null && bare.Folder is null, "nor their sessionId and cwd");
        Check.Eq(ClaudeLog.Parse(B(Line(""","effort":"HIGH" """)))!.Value.Variant.Effort(), TokenEffort.High, "in any case");
        Check.Eq(ClaudeLog.Parse(B(Line(""","effort":"turbo" """)))!.Value.Variant.Effort(), TokenEffort.Unknown, "a word not known");
        Check.Eq(ClaudeLog.Parse(B(Line(""","effort":3""")))!.Value.Variant.Effort(), TokenEffort.Unknown, "not a string");

        var strings = new ClaudeLog.Strings();
        var a = ClaudeLog.Parse(B(Line(""","sessionId":"s-1","cwd":"C:\\a" """)), strings)!.Value;
        var b = ClaudeLog.Parse(B(Line(""","sessionId":"s-1","cwd":"C:\\a" """)), strings)!.Value;
        Check.True(ReferenceEquals(a.Session, b.Session) && ReferenceEquals(a.Folder, b.Folder), "the file's strings made once while they repeat");
        Check.Eq(ClaudeLog.Parse(B(Line(""","sessionId":"s-2" """)), strings)!.Value.Session, "s-2", "and again when they change");
    }

    public static void ClaudeTitlesAreTheirLinesOnly()
    {
        Check.Eq(ClaudeLog.Title(B("""{"type":"custom-title","customTitle":"Home blocks","sessionId":"s1"}""")), ("s1", "Home blocks", true), "custom-title");
        Check.Eq(ClaudeLog.Title(B("""{"type":"ai-title","sessionId":"s1","aiTitle":"Fix the build"}""")), ("s1", "Fix the build", false), "ai-title");
        Check.Eq(ClaudeLog.Title(B("""{"type":"custom-title","customTitle":"x"}"""))!.Value.Session, null, "no sessionId: the file's (TokenScan)");
        Check.True(ClaudeLog.Title(B("""{"type":"last-prompt","lastPrompt":"a secret prompt","sessionId":"s1"}""")) is null, "a last-prompt line");
        Check.True(ClaudeLog.Title(B("""{"type":"user","message":{"content":"{\"type\":\"custom-title\",\"customTitle\":\"no\"}"},"customTitle":"no","sessionId":"s1"}""")) is null, "a title in a message's text, or on another line");
        Check.True(ClaudeLog.Title(B("""{"type":"custom-title","customTitle":"  \n ","sessionId":"s1"}""")) is null, "a blank title");
        string longTitle = new('가', 70);
        Check.Eq(ClaudeLog.Title(B($$"""{"type":"custom-title","customTitle":"{{longTitle}}","sessionId":"s1"}"""))!.Value.Title, longTitle[..TokenSessionInfo.TitleMost], "cut at 64");
        Check.Eq(ClaudeLog.Title(B("""{"type":"custom-title","customTitle":" one\ntwo\t","sessionId":"s1"}"""))!.Value.Title, "one two", "one line, trimmed");
        Check.True(ClaudeLog.Title(B("""{"type":"custom-title","customTitle":"cut""")) is null, "a line cut off");
    }

    public static void CodexEffortAndSessionFollowTheFile()
    {
        var file = new TokenFile { Thread = "01a195f8-1b89-7e01-bc47-41dae7935ad7" };
        CodexLog.Parse(B("""{"timestamp":"2026-11-01T07:17:10.000Z","type":"turn_context","payload":{"model":"gpt-6.1-sol","effort":"high","collaboration_mode":{"mode":"default","settings":{"model":"gpt-x","reasoning_effort":"low"}}}}"""), file);
        Check.Eq((file.Model, file.Effort), ("gpt-6.1-sol", TokenEffort.High), "turn_context's own model and effort first");
        var call = CodexLog.Parse(B(Record("resp_1", 10, 0, 0, 1)), file)!.Value;
        Check.Eq(call.Variant.Effort(), TokenEffort.High, "the call made at it");
        Check.Eq(call.Session, file.Thread, "in the rollout's thread");
        CodexLog.Parse(B(Context("gpt-6-astra")), file);
        Check.Eq(CodexLog.Parse(B(Record("resp_2", 10, 0, 0, 1)), file)!.Value.Variant.Effort(), TokenEffort.High, "a turn naming no effort keeps the last one");
        CodexLog.Parse(B("""{"timestamp":"2026-11-01T07:17:10.000Z","type":"turn_context","payload":{"collaboration_mode":{"settings":{"reasoning_effort":"ultra"}}}}"""), file);
        Check.Eq((file.Model, file.Effort), ("gpt-6-astra", TokenEffort.Ultra), "else collaboration_mode.settings.reasoning_effort");
        CodexLog.Parse(B("""{"timestamp":"2026-11-01T07:17:10.000Z","type":"turn_context","payload":{"effort":"warp"}}"""), file);
        Check.Eq(file.Effort, TokenEffort.Unknown, "a word not known");
        file.Effort = TokenEffort.Max;
        file.Restart();
        Check.Eq(file.Effort, TokenEffort.Unknown, "a file read again from the start forgets it");

        const string meta = """{"timestamp":"2026-11-01T07:17:08.000Z","type":"session_meta","payload":{"id":"01a195f8","base_instructions":{"text":"\"originator\":\"bloom\" \"cwd\":\"C:\\x\""},"cwd":"D:\\Work\\Repo\\","originator":"codex_exec","parent_thread_id":"01a19000","forked_from_id":"01a18000","source":{"subagent":"review"}}}""";
        var sub = new TokenFile { Thread = "01a195f8" };
        Check.True(CodexLog.Parse(B(meta), sub) is null && sub.Met, "session_meta is no call, but tells the thread");
        Check.Eq((sub.Parent, sub.Folder, sub.Bloom), ("01a19000", "Repo", false), "its parent and its folder's last name, not the base instructions'");
        var bloom = new TokenFile { Thread = "t" };
        CodexLog.Parse(B(meta.Replace("codex_exec", "bloom", StringComparison.Ordinal).Replace("D:\\\\Work\\\\Repo\\\\", "D:\\\\", StringComparison.Ordinal)), bloom);
        Check.Eq((bloom.Bloom, bloom.Folder), (true, "D:"), "Bloom's own (originator bloom); a drive's root");
        var later = new TokenFile { Thread = "t" };
        CodexLog.Parse(B(meta), later, at: 500);
        Check.True(!later.Met, "only the first line's session_meta (a fork's copy of its parent's is not)");

        Check.Eq(CodexLog.Name(B("""{"id":"01a1","thread_name":"Fix the tests","updated_at":"2026-10-20T08:00:00Z"}""")), ("01a1", (string?)"Fix the tests"), "a session_index.jsonl line");
        Check.Eq(CodexLog.Name(B("""{"id":"01a1","updated_at":"x"}"""))!.Value.Name, null, "no name");
        Check.True(CodexLog.Name(B("""{"thread_name":"x"}""")) is null && CodexLog.Name(B("not json")) is null, "no id, or no JSON");
    }

    private static string Context(string model) => """{"timestamp":"2026-11-01T07:17:10.000Z","ordinal":3,"type":"turn_context","payload":{"cwd":"x","model":"@MODEL"}}""".Replace("@MODEL", model, StringComparison.Ordinal);

    private static string Record(string id, int input, int cached, int written, int output, int ordinal = 11) =>
        """{"timestamp":"2026-11-01T07:17:15.875Z","ordinal":@ORDINAL,"type":"token_usage_record","payload":{"thread_id":"t","response_id":"@ID","usage":{"input_tokens":@INPUT,"cached_input_tokens":@CACHED,"cache_write_input_tokens":@WRITTEN,"output_tokens":@OUTPUT,"reasoning_output_tokens":7,"total_tokens":1},"turn_token_usage":{"input_tokens":99999,"cached_input_tokens":0,"output_tokens":99999}}}""".Replace("@ORDINAL", ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@ID", id, StringComparison.Ordinal).Replace("@INPUT", input.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@CACHED", cached.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@WRITTEN", written.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@OUTPUT", output.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string Count(int total, int input, int cached, int output, int ordinal) =>
        """{"timestamp":"2026-11-01T07:17:16.000Z","ordinal":@ORDINAL,"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":@TOTAL,"cached_input_tokens":0,"output_tokens":1,"total_tokens":@TOTAL},"last_token_usage":{"input_tokens":@INPUT,"cached_input_tokens":@CACHED,"cache_write_input_tokens":0,"output_tokens":@OUTPUT,"reasoning_output_tokens":0,"total_tokens":0},"model_context_window":475000},"rate_limits":null}}""".Replace("@ORDINAL", ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@TOTAL", total.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@INPUT", input.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@CACHED", cached.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("@OUTPUT", output.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    public static void CodexRecordsAreCalls()
    {
        var file = new TokenFile { Thread = "01a195f8-1b89-7e01-bc47-41dae7935ad7" };
        Check.True(CodexLog.Parse(B(Context("gpt-6.1-sol")), file) is null, "turn_context is no call");
        Check.Eq(file.Model, "gpt-6.1-sol", "it names the model");
        var call = CodexLog.Parse(B(Record("resp_1", 13139, 12160, 500, 22)), file)!.Value;
        Check.Eq(call.Counts, new TokenCounts(1, 13139 - 12160 - 500, 22, 12160, 500, 0), "input less cached and written; output with the reasoning");
        Check.Eq(call.Model, "gpt-6.1-sol", "the turn_context's model");
        Check.Eq(call.Provider, "codex", "the provider");
        Check.True(file.Records, "the file has records now");
        Check.Eq(CodexLog.Parse(B(Record("resp_1", 1, 0, 0, 1, ordinal: 40)), file)!.Value.Key, call.Key, "the key is the response_id (a fork's copy finds it)");
        Check.True(CodexLog.Parse(B(Count(13139, 13139, 12160, 22, 12)), file) is null, "token_count after a record: not counted twice");
        CodexLog.Parse(B(Context("gpt-6-astra")), file);
        Check.Eq(CodexLog.Parse(B(Record("resp_2", 10, 0, 0, 1)), file)!.Value.Model, "gpt-6-astra", "a later turn_context holds from there");
        Check.True(CodexLog.Parse(B("""{"timestamp":"2026-11-01T07:17:15.875Z","type":"response_item","payload":{"type":"message","content":"token_count"}}"""), file) is null, "another line");
    }

    public static void CodexTokenCountsAreCallsInOldFiles()
    {
        var file = new TokenFile { Thread = "01a17880-284e-78b0-9929-06b89368c332" };
        CodexLog.Parse(B(Context("gpt-6-astra")), file);
        var first = CodexLog.Parse(B(Count(100, 100, 40, 5, 4)), file)!.Value;
        Check.Eq(first.Counts, new TokenCounts(1, 60, 5, 40, 0, 0), "last_token_usage");
        Check.True(CodexLog.Parse(B(Count(100, 100, 40, 5, 5)), file) is null, "the same total again");
        Check.True(CodexLog.Parse(B("""{"timestamp":"2026-11-01T07:17:16.000Z","ordinal":6,"type":"event_msg","payload":{"type":"token_count","info":null}}"""), file) is null, "no info");
        var second = CodexLog.Parse(B(Count(180, 80, 0, 9, 7)), file)!.Value;
        Check.Eq(second.Counts.Output, 9L, "a new total");
        Check.True(second.Key != first.Key, "keyed by the thread and the ordinal");
        var other = new TokenFile { Thread = "01a17880-0000-0000-0000-000000000000", Model = "gpt-6-astra" };
        Check.True(CodexLog.Parse(B(Count(100, 100, 40, 5, 4)), other)!.Value.Key != first.Key, "another thread");
        string noOrdinal = Count(300, 1, 0, 1, 0).Replace("\"ordinal\":0,", "", StringComparison.Ordinal);
        ulong at10 = CodexLog.Parse(B(noOrdinal), new TokenFile { Thread = "t", Model = "m" }, 10)!.Value.Key;
        Check.True(at10 != CodexLog.Parse(B(noOrdinal), new TokenFile { Thread = "t", Model = "m" }, 20)!.Value.Key, "no ordinal: the line's place in the file");
    }

    public static void LinesAreReadWholeFromAnOffset()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-tokens-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "a.jsonl");
            File.WriteAllText(path, "one\r\ntwo\nthr");
            var reader = new LogLines();
            var got = new List<string>();
            long offset = reader.Read(path, 0, (line, at) => got.Add($"{Encoding.UTF8.GetString(line).TrimEnd('\r')}@{at}"));
            Check.Eq(string.Join(" ", got), "one@0 two@5", "whole lines and where they start");
            Check.Eq(offset, 9L, "the unfinished line is left");
            File.AppendAllText(path, "ee\nfour");
            got.Clear();
            offset = reader.Read(path, offset, (line, at) => got.Add($"{Encoding.UTF8.GetString(line)}@{at}"));
            Check.Eq(string.Join(" ", got), "three@9", "read on from the offset");
            Check.Eq(offset, 15L, "past its '\\n'");
            Check.Eq(reader.Read(path, offset, (_, _) => throw new InvalidOperationException("no line")), 15L, "nothing whole yet");

            string big = Path.Combine(dir, "b.jsonl");
            string longLine = new('x', LogLines.First * 3);
            File.WriteAllText(big, "a\n" + longLine + "\nb\n");
            got.Clear();
            Check.Eq(reader.Read(big, 0, (line, _) => got.Add(line.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))), (long)(longLine.Length + 5), "to the end");
            Check.Eq(string.Join(" ", got), $"1 {longLine.Length} 1", "a line longer than the first buffer");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    public static void LinesOverTheMostAreSkipped()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-tokens-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "a.jsonl");
            File.WriteAllText(path, "a\n" + new string('x', 40) + "\nb\n" + new string('y', 40));
            var reader = new LogLines(16);
            var got = new List<string>();
            long offset = reader.Read(path, 0, (line, at) => got.Add($"{Encoding.UTF8.GetString(line)}@{at}"));
            Check.Eq(string.Join(" ", got), "a@0 b@43", "the long line passed over, the next one where it starts");
            Check.Eq(offset, 45L, "a long line not finished yet: read again from its start");
            File.AppendAllText(path, "\nc\n");
            got.Clear();
            offset = reader.Read(path, offset, (line, at) => got.Add($"{Encoding.UTF8.GetString(line)}@{at}"));
            Check.Eq(string.Join(" ", got), "c@86", "finished, it is passed over too");
            Check.Eq(offset, 88L, "past the last line");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
