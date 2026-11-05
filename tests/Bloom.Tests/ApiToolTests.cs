using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;

namespace Bloom.Tests;

/// API chats' tool calls: each wire's round trip against FakeApiServer (the call streamed in pieces, its result sent
/// back, the answer after it), the approval every write and command waits for, Bloom's pc tool, the step limit, and the
/// steps a resumed chat sends again.
internal static class ApiToolTests
{
    /// Bloom's pc tool for the tests: find_* are look-ups, the rest are actions; every call is recorded.
    private sealed class FakePc : IPcTools
    {
        public readonly List<string> Calls = new();
        public object Definition => new { name = "pc", description = "Act on the PC.", inputSchema = new { type = "object", properties = new { @do = new { type = "string" } }, required = new[] { "do" } } };
        public bool Quiet(string tool, string input) => input.Contains("find_", StringComparison.Ordinal);
        public string Says(string tool, string input) => tool;
        public string Key(string tool, string input) => tool;
        public Task<(string text, bool error)> Call(string tool, JsonElement args, string? conversation = null)
        {
            lock (Calls) Calls.Add(args.GetProperty("do").GetString()!);
            return Task.FromResult(("done on the PC", false));
        }
    }

    /// A chat folder with notes.txt in it, gone afterwards.
    internal sealed class Folder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bloom-tools-" + Guid.NewGuid().ToString("N")[..8]);
        public Folder()
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "notes.txt"), "the notes' contents");
        }
        // a command just stopped may hold the folder (its working directory) for a moment after it ends
        public void Dispose()
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    Directory.Delete(Path, true);
                    return;
                }
                catch (IOException) when (i < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    private static ApiRequest Request(FakeApiServer server, ApiWire wire) =>
        new(new ApiProvider("fake", "Fake", server.BaseUrl, wire, ""), "sk-tools", "model-x", wire, "Be brief.",
            new[] { new ApiMessage(ApiMessage.User, "hi", Array.Empty<string>()) }, null, null);

    private static (ApiReply Reply, List<ApiStep> Steps) Turn(ApiRequest request, ApiToolbox tools, int maxSteps = ApiTurn.MaxSteps) =>
        ApiTurn.RunAsync(request, tools, _ => { }, _ => { }, CancellationToken.None, maxSteps).GetAwaiter().GetResult();

    /// Answers the n-th request (from 0) with `steps[n]`.
    private static FakeApiServer Server(params string[] steps)
    {
        int n = -1;
        return new FakeApiServer(_ => steps[Math.Min(Interlocked.Increment(ref n), steps.Length - 1)]);
    }

    /// A toolbox in `f` (`folder`: another root) under `rules` (none: every action 매번 묻기), recording what it asks.
    internal static ApiToolbox Box(Folder f, Func<ToolAsk, bool> answer, List<ToolAsk>? asked = null, IPcTools? pc = null,
        Dictionary<string, string>? rules = null, IEnumerable<string>? secrets = null, string? folder = null) =>
        new(folder ?? f.Path, pc, rules ?? new Dictionary<string, string>(), ask =>
        {
            if (asked is not null)
                lock (asked) asked.Add(ask);
            return Task.FromResult(answer(ask));
        }, secrets);

    internal static (string Text, bool Error) Run(ApiToolbox box, string tool, object arguments) =>
        box.RunAsync(new ApiToolCall("c-" + Guid.NewGuid().ToString("N")[..6], tool, JsonSerializer.Serialize(arguments)), CancellationToken.None).GetAwaiter().GetResult();

    public static void ChatCompletionsToolRoundTrip()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        using var server = Server(
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"pa\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"th\\\":\\\"notes.txt\\\"}\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"),
            FakeApiServer.Sse("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"It says hi.\"},\"finish_reason\":\"stop\"}]}", "data: [DONE]"));
        var asked = new List<ToolAsk>();
        var (reply, steps) = Turn(Request(server, ApiWire.ChatCompletions), Box(f, _ => true, asked));
        Check.True(reply.Ok && reply.Text == "It says hi.", "the answer after the tool");
        Check.Eq(asked.Count, 0, "a read in the chat's folder asks nothing");
        Check.Eq(steps.Count, 2, "two model calls");
        Check.Eq(steps[0].Calls[0].Arguments, "{\"path\":\"notes.txt\"}", "the streamed argument pieces joined");
        server.Requests.TryDequeue(out var first);
        server.Requests.TryDequeue(out var second);
        var tools = JsonNode.Parse(first!.Body)!["tools"]!.AsArray();
        Check.True(tools.Any(t => (string?)t!["function"]!["name"] == "write_file"), "the tools offered as functions");
        var messages = JsonNode.Parse(second!.Body)!["messages"]!.AsArray();
        Check.Eq((string?)messages[2]!["tool_calls"]![0]!["id"], "call_1", "the call sent back");
        Check.True(messages[3] is JsonObject { } result && (string?)result["role"] == "tool" && (string?)result["tool_call_id"] == "call_1"
            && (string?)result["content"] == "the notes' contents", "its result as a tool message");
    }

    public static void MessagesToolRoundTripWithAnError()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        using var server = Server(
            FakeApiServer.Sse(
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Let me look.\"}}",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"read_file\",\"input\":{}}}",
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"path\\\": \\\"gone\"}}",
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\".txt\\\"}\"}}",
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"}}",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}"),
            FakeApiServer.Sse(
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"No such file.\"}}",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}"));
        var (reply, steps) = Turn(Request(server, ApiWire.Messages), Box(f, _ => true));
        Check.True(reply.Ok && reply.Text == "Let me look.\n\nNo such file.", "both steps' text");
        Check.True(steps[0].Calls[0] is { Error: true, Name: "read_file" }, "the failed read is the call's error");
        server.Requests.TryDequeue(out var first);
        server.Requests.TryDequeue(out var second);
        Check.Eq((string?)JsonNode.Parse(first!.Body)!["tools"]![0]!["input_schema"]!["type"], "object", "tools with input_schema");
        var messages = JsonNode.Parse(second!.Body)!["messages"]!.AsArray();
        var use = messages[1]!["content"]!.AsArray();
        Check.True((string?)use[0]!["text"] == "Let me look." && (string?)use[1]!["type"] == "tool_use" && (string?)use[1]!["input"]!["path"] == "gone.txt", "the text and the tool_use, its input as an object");
        var result = messages[2]!["content"]![0]!;
        Check.True((string?)messages[2]!["role"] == "user" && (string?)result["type"] == "tool_result" && (string?)result["tool_use_id"] == "toolu_1" && (bool?)result["is_error"] == true,
            "the result in the next user message, marked as an error");
    }

    public static void ResponsesToolRoundTrip()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        var pc = new FakePc();
        using var server = Server(
            FakeApiServer.Sse(
                "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_9\",\"name\":\"pc\",\"arguments\":\"\"}}",
                "event: response.function_call_arguments.delta\ndata: {\"type\":\"response.function_call_arguments.delta\",\"output_index\":0,\"item_id\":\"fc_1\",\"delta\":\"{\\\"do\\\":\"}",
                "event: response.function_call_arguments.delta\ndata: {\"type\":\"response.function_call_arguments.delta\",\"output_index\":0,\"item_id\":\"fc_1\",\"delta\":\"\\\"timer\\\"}\"}",
                "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_9\",\"name\":\"pc\",\"arguments\":\"{\\\"do\\\":\\\"timer\\\"}\"}}",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}"),
            FakeApiServer.Sse(
                "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"Timer set.\"}",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{}}"));
        var asked = new List<ToolAsk>();
        var (reply, _) = Turn(Request(server, ApiWire.Responses), Box(f, _ => true, asked, pc));
        Check.True(reply.Ok && reply.Text == "Timer set.", "the answer");
        Check.True(asked.Count == 1 && asked[0] is { Tool: "pc", Says: "" }, "a pc action asks, worded by the pc tool's own policy");
        Check.Eq(string.Join(",", pc.Calls), "timer", "Bloom's handler ran it once");
        server.Requests.TryDequeue(out var first);
        server.Requests.TryDequeue(out var second);
        var tools = JsonNode.Parse(first!.Body)!["tools"]!.AsArray();
        Check.True((string?)tools[0]!["name"] == "pc" && (string?)tools[0]!["parameters"]!["properties"]!["do"]!["type"] == "string", "the pc tool's own schema");
        var input = JsonNode.Parse(second!.Body)!["input"]!.AsArray();
        Check.True(input[1] is JsonObject call && (string?)call["type"] == "function_call" && (string?)call["call_id"] == "call_9" && (string?)call["arguments"] == "{\"do\":\"timer\"}", "the call sent back");
        Check.True(input[2] is JsonObject output && (string?)output["type"] == "function_call_output" && (string?)output["output"] == "done on the PC", "its output");
    }

    public static void DefaultPermissionsOfferTheFileAndCommandToolsAndTellTheModelToUseThem()
    {
        using var f = new Folder();
        var box = Box(f, _ => true); // no rule set: every action 매번 묻기
        Check.True(new[] { ApiToolbox.ReadFile, ApiToolbox.ListFolder, ApiToolbox.WriteFile, ApiToolbox.EditFile, ApiToolbox.RunCommand }.All(box.Offers), "all five tools are offered");
        string text = ApiSession.Instructions(box, false, true);
        Check.True(text.Contains("calling the write_file tool", StringComparison.Ordinal) && text.Contains("never claim you created", StringComparison.Ordinal), "the model is told to call the tool, not to claim");
    }

    public static void WritesAndCommandsWaitForTheUser()
    {
        using var f = new Folder();
        var asked = new List<ToolAsk>();
        var no = Box(f, _ => false, asked);
        string target = Path.Combine(f.Path, "out", "a.txt");
        var (text, error) = no.RunAsync(new ApiToolCall("c1", ApiToolbox.WriteFile, "{\"path\":\"out\\\\a.txt\",\"content\":\"x\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error && text.Contains("declined", StringComparison.Ordinal) && !File.Exists(target), "a declined write writes nothing");
        string marker = Path.Combine(f.Path, "ran.txt");
        (_, error) = no.RunAsync(new ApiToolCall("c2", ApiToolbox.RunCommand, JsonSerializer.Serialize(new { command = $"Set-Content -Path '{marker}' -Value 1" })), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error && !File.Exists(marker), "a declined command runs nothing");
        (_, error) = no.RunAsync(new ApiToolCall("c3", ApiToolbox.EditFile, "{\"path\":\"notes.txt\",\"old_text\":\"notes\",\"new_text\":\"x\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error && File.ReadAllText(Path.Combine(f.Path, "notes.txt")) == "the notes' contents", "a declined edit changes nothing");
        Check.Eq(string.Join(",", asked.Select(a => a.Key)), "file:write,command,file:edit", "each asked, 항상 허용 per action");
        Check.True(asked[0].Says.Contains(target, StringComparison.Ordinal), "the card names the full path");
        (_, error) = no.RunAsync(new ApiToolCall("c4", ApiToolbox.ReadFile, "{\"path\":\"notes.txt\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!error && asked.Count == 3, "a read in the chat's folder runs without asking");
        (_, error) = no.RunAsync(new ApiToolCall("c5", ApiToolbox.ReadFile, JsonSerializer.Serialize(new { path = Path.GetTempPath() + "elsewhere.txt" })), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error && asked[^1].Key == "file:read", "a read elsewhere asks");

        var yes = Box(f, _ => true);
        (text, error) = yes.RunAsync(new ApiToolCall("c6", ApiToolbox.WriteFile, "{\"path\":\"out\\\\a.txt\",\"content\":\"안녕\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!error && File.ReadAllText(target) == "안녕", "an allowed write writes, folders and all");
        (text, error) = yes.RunAsync(new ApiToolCall("c7", ApiToolbox.RunCommand, "{\"command\":\"Write-Output ('h' + 'i'); (Get-Location).Path\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(!error && text.Contains("hi", StringComparison.Ordinal) && text.Contains(f.Path, StringComparison.OrdinalIgnoreCase) && text.Contains("exit code 0", StringComparison.Ordinal),
            "an allowed command runs in the chat's folder, its output back");
        (text, error) = yes.RunAsync(new ApiToolCall("c8", ApiToolbox.RunCommand, "{\"command\":\"exit 3\"}"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error && text.Contains("exit code 3", StringComparison.Ordinal), "a failing command is the call's error");
        (_, error) = yes.RunAsync(new ApiToolCall("c9", ApiToolbox.ReadFile, "{\"path\":"), CancellationToken.None).GetAwaiter().GetResult();
        Check.True(error, "broken arguments are the call's error");
    }

    public static void CommandsStopWithTheTurn()
    {
        using var f = new Folder();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var started = DateTime.UtcNow;
        bool cancelled = false;
        try
        {
            ApiCommand.RunAsync("Start-Sleep -Seconds 30", f.Path, stop.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check.True(cancelled && DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "a stop kills the command at once");
        var (text, error) = ApiCommand.RunAsync("Start-Sleep -Seconds 30", f.Path, CancellationToken.None, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        Check.True(error && text.Contains("still running", StringComparison.Ordinal), "the time limit ends it as an error");
    }

    public static void StepLimitStopsTheLoop()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        using var server = Server(FakeApiServer.Sse(
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"again\",\"function\":{\"name\":\"list_folder\",\"arguments\":\"{\\\"path\\\":\\\".\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
            "data: [DONE]"));
        var (reply, steps) = Turn(Request(server, ApiWire.ChatCompletions), Box(f, _ => true), maxSteps: 3);
        Check.True(!reply.Ok && reply.Error == AiErrors.ApiTooManySteps(3), "stopped, and says why");
        Check.True(server.Requests.Count == 3 && steps.Count == 3, "three model calls, no more");
    }

    public static void GeminiParallelCallsStaySeparateAndKeepTheirSignature()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        // as Gemini's compat endpoint streams them: each call whole in its own chunk, the same index or none, the
        // thought signature on the first only
        using var server = Server(
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"function-call-1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"notes.txt\\\"}\"},\"extra_content\":{\"google\":{\"thought_signature\":\"SIG_A==\"}}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"function-call-2\",\"type\":\"function\",\"function\":{\"name\":\"list_folder\",\"arguments\":\"{\\\"path\\\":\\\".\\\"}\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"gone.txt\\\"}\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}"),
            FakeApiServer.Sse("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Read both.\"},\"finish_reason\":\"stop\"}]}"));
        var (reply, steps) = Turn(Request(server, ApiWire.ChatCompletions), Box(f, _ => true));
        Check.True(reply.Ok && reply.Text == "Read both.", "the answer after the calls");
        var calls = steps[0].Calls;
        Check.Eq(string.Join(",", calls.Select(c => c.Name + c.Arguments)), "read_file{\"path\":\"notes.txt\"},list_folder{\"path\":\".\"},read_file{\"path\":\"gone.txt\"}", "three calls, none run together");
        Check.True(!calls[0].Error && !calls[1].Error && calls[2].Error && calls[2].Id.StartsWith("call_", StringComparison.Ordinal), "each ran on its own; the one with no id got one");
        Check.Eq(calls[0].Extra, "{\"google\":{\"thought_signature\":\"SIG_A==\"}}", "the signature kept on its call");
        server.Requests.TryDequeue(out _);
        server.Requests.TryDequeue(out var second);
        var messages = JsonNode.Parse(second!.Body)!["messages"]!.AsArray();
        var turn = messages[2]!.AsObject();
        Check.True(!turn.ContainsKey("content"), "a turn that only calls tools sends no content");
        var sent = turn["tool_calls"]!.AsArray();
        Check.True(sent.Count == 3 && (string?)sent[0]!["extra_content"]!["google"]!["thought_signature"] == "SIG_A==" && sent[1]!["extra_content"] is null, "the signature back on the first call only, as it came");
        Check.Eq(string.Join(",", messages.Skip(3).Select(m => (string?)m!["tool_call_id"])), string.Join(",", calls.Select(c => c.Id)), "every result after the calls, in order");

        // a resumed chat: the saved step sends the signature again
        string json = JsonSerializer.Serialize(steps[0], Bloom.Core.Json.Options);
        var saved = JsonSerializer.Deserialize<ApiStep>(json, Bloom.Core.Json.Options)!;
        var body = JsonNode.Parse(ChatCompletionsWire.Instance.Body(Request(server, ApiWire.ChatCompletions) with { Messages = saved.Messages().ToList() }))!;
        Check.Eq((string?)body["messages"]![1]!["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"], "SIG_A==", "the replayed call keeps it");
    }

    public static void BrokenArgumentsAreNotSentBack()
    {
        var call = new ApiMessage(ApiMessage.Assistant, "", []) { Calls = [new ApiToolCall("c1", "run_command", "{\"path\":\"a\"}{\"command\":\"x\"}")] };
        var body = JsonNode.Parse(ChatCompletionsWire.Instance.Body(new ApiRequest(new ApiProvider("fake", "Fake", "http://localhost/v1", ApiWire.ChatCompletions, ""), "k", "m", ApiWire.ChatCompletions, "",
            [new ApiMessage(ApiMessage.User, "hi", []), call, ApiMessage.ResultOf("c1", "bad", true)], null, null)))!;
        Check.Eq((string?)body["messages"]![2]!["tool_calls"]![0]!["function"]!["arguments"], "{}", "arguments that are no JSON object go back as {}");
    }

    public static void ResumedChatsSendTheirToolCallsAgain()
    {
        var steps = new List<ApiStep>
        {
            new("Looking.", new() { new ApiCallRecord("call_1", "read_file", "{\"path\":\"a.txt\"}", "the contents", false) }),
            new("It says hi.", new()),
        };
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Text = "q" },
            new() { Role = "assistant", Text = "Looking.\n\nIt says hi.", Steps = steps },
            new() { Role = "user", Text = "now" },
            new() { Role = "assistant", Text = "", Streaming = true },
        };
        string json = JsonSerializer.Serialize(messages[1], Bloom.Core.Json.Options);
        Check.Eq(JsonSerializer.Deserialize<ChatMessage>(json, Bloom.Core.Json.Options)?.Steps?[0].Calls[0].Result, "the contents", "the steps kept in the chat's file");
        var history = ApiSession.HistoryOf(ApiSession.Past(messages), Chats.UnavailableNote());
        Check.Eq(string.Join("|", history.Select(m => m.Role + (m.Calls.Count > 0 ? "+call" : "") + ":" + m.Text)), "user:q|assistant+call:Looking.|tool:the contents|assistant:It says hi.", "the steps as they went");
        Check.Eq(history[2].CallId, "call_1", "the result names its call");
    }

    public static void DeepSeekReasoningGoesBackWithTools()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        using var server = Server(
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"reasoning_content\":\"Need the \"}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"file.\"}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"notes.txt\\\"}\"}}]}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
                "data: [DONE]"),
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"Got it.\"}}]}",
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"It says hi.\"},\"finish_reason\":\"stop\"}]}",
                "data: [DONE]"));
        var provider = new ApiProvider("deepseek", "DeepSeek", server.BaseUrl, ApiWire.ChatCompletions, "") { ReplaysReasoning = true };
        var request = new ApiRequest(provider, "sk-tools", "deepseek-v4", ApiWire.ChatCompletions, "Be brief.",
            [new ApiMessage(ApiMessage.User, "earlier", []), new ApiMessage(ApiMessage.Assistant, "an answer from before", []), new ApiMessage(ApiMessage.User, "hi", [])], null, null);
        var (reply, steps) = Turn(request, Box(f, _ => true));
        Check.True(reply.Ok && reply.Text == "It says hi.", "the answer");
        Check.True(steps[0].Reasoning == "Need the file." && steps[1].Reasoning == "Got it." && steps[0].From == "deepseek:deepseek-v4", "each step keeps its reasoning, joined, and whose it is");
        server.Requests.TryDequeue(out _);
        server.Requests.TryDequeue(out var second);
        var messages = JsonNode.Parse(second!.Body)!["messages"]!.AsArray();
        Check.Eq((string?)messages[2]!["reasoning_content"], "", "an earlier turn without reasoning sends it empty when tools go with the request");
        Check.True((string?)messages[4]!["reasoning_content"] == "Need the file." && messages[4]!["tool_calls"] is JsonArray, "the tool turn sends its reasoning back");

        // a resumed chat: the saved steps send it again, to that model only
        var saved = JsonSerializer.Deserialize<List<ApiStep>>(JsonSerializer.Serialize(steps, Bloom.Core.Json.Options), Bloom.Core.Json.Options)!;
        var again = request with { Messages = [new ApiMessage(ApiMessage.User, "q", []), .. saved.SelectMany(s => s.Messages()), new ApiMessage(ApiMessage.User, "next", [])], Tools = [new ApiTool("t", "d", "{}")] };
        var body = JsonNode.Parse(ChatCompletionsWire.Instance.Body(again))!["messages"]!.AsArray();
        Check.True((string?)body[2]!["reasoning_content"] == "Need the file." && (string?)body[4]!["reasoning_content"] == "Got it.", "every turn's reasoning, also the final answer's");
        body = JsonNode.Parse(ChatCompletionsWire.Instance.Body(again with { Model = "deepseek-other" }))!["messages"]!.AsArray();
        Check.True((string?)body[2]!["reasoning_content"] == "" && (string?)body[4]!["reasoning_content"] == "", "another model gets none of it");
        body = JsonNode.Parse(ChatCompletionsWire.Instance.Body(again with { Provider = new ApiProvider("openai", "OpenAI", server.BaseUrl, ApiWire.ChatCompletions, "") }))!["messages"]!.AsArray();
        Check.True(body[2]!["reasoning_content"] is null, "a provider that doesn't take it back gets no field");
    }

    public static void ResponsesReasoningGoesBackInOrder()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new Folder();
        using var server = Server(
            FakeApiServer.Sse(
                "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[]}}",
                "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[],\"encrypted_content\":\"ENC_ONE\"}}",
                "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"output_index\":1,\"delta\":\"Looking.\"}",
                "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":2,\"item\":{\"type\":\"reasoning\",\"id\":\"rs_2\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"list it\"}],\"encrypted_content\":\"ENC_TWO\"}}",
                "event: response.output_item.added\ndata: {\"type\":\"response.output_item.added\",\"output_index\":3,\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_1\",\"name\":\"list_folder\",\"arguments\":\"\"}}",
                "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":3,\"item\":{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_1\",\"name\":\"list_folder\",\"arguments\":\"{\\\"path\\\":\\\".\\\"}\"}}",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{}}"),
            FakeApiServer.Sse(
                "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"One file.\"}",
                "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{}}"));
        var request = Request(server, ApiWire.Responses);
        var (reply, steps) = Turn(request, Box(f, _ => true));
        Check.True(reply.Ok && reply.Text == "Looking.\n\nOne file.", "the answer");
        Check.Eq(string.Join(",", steps[0].Thoughts!.Select(t => t.At)), "0,1", "the first before the text, the second before the call");
        server.Requests.TryDequeue(out var first);
        server.Requests.TryDequeue(out var second);
        var sent = JsonNode.Parse(first!.Body)!;
        Check.True((bool?)sent["store"] == false && (string?)sent["include"]![0] == "reasoning.encrypted_content", "nothing stored, the reasoning asked for encrypted");
        var input = JsonNode.Parse(second!.Body)!["input"]!.AsArray();
        string Kind(JsonNode? n) => (string?)n!["type"] ?? (string?)n["role"] ?? "";
        Check.Eq(string.Join(",", input.Select(Kind)), "user,reasoning,assistant,reasoning,function_call,function_call_output", "each reasoning item just before what it led to");
        Check.True((string?)input[1]!["encrypted_content"] == "ENC_ONE" && (string?)input[3]!["id"] == "rs_2" && (string?)input[3]!["summary"]![0]!["text"] == "list it", "the items as they came");

        // a resumed chat sends them again, to the same model only
        var saved = JsonSerializer.Deserialize<List<ApiStep>>(JsonSerializer.Serialize(steps, Bloom.Core.Json.Options), Bloom.Core.Json.Options)!;
        var again = request with { Messages = [new ApiMessage(ApiMessage.User, "q", []), .. saved.SelectMany(s => s.Messages()), new ApiMessage(ApiMessage.User, "next", [])] };
        var replayed = JsonNode.Parse(ResponsesWire.Instance.Body(again))!["input"]!.AsArray();
        Check.Eq(string.Join(",", replayed.Select(Kind)), "user,reasoning,assistant,reasoning,function_call,function_call_output,assistant,user", "the saved steps in the same order");
        replayed = JsonNode.Parse(ResponsesWire.Instance.Body(again with { Model = "model-y" }))!["input"]!.AsArray();
        Check.True(replayed.All(n => Kind(n) != "reasoning"), "another model gets none of it");
    }

    public static void StoppedAnswersKeepTheirFinishedCalls()
    {
        var steps = new List<ApiStep>
        {
            new("Looking.", new() { new ApiCallRecord("call_1", "read_file", "{\"path\":\"a.txt\"}", "the contents", false) }),
            new("It sa", new()),
        };
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Text = "q" },
            new() { Role = "assistant", Text = "Looking.\n\nIt sa\n\nAnswer stopped", Error = true, Steps = steps },
            new() { Role = "user", Text = "lost" },
            new() { Role = "assistant", Text = "Network error", Error = true },
            new() { Role = "user", Text = "now" },
            new() { Role = "assistant", Text = "", Streaming = true },
        };
        var history = ApiSession.HistoryOf(ApiSession.Past(messages), Chats.UnavailableNote());
        Check.Eq(string.Join("|", history.Select(m => m.Role + (m.Calls.Count > 0 ? "+call" : "") + ":" + m.Text)), "user:q|assistant+call:Looking.|tool:the contents",
            "the finished call and its result stay, the stopped text and a failed answer with nothing finished don't");

        // Messages: the next question joins the results' user message (no two user messages in a row)
        var body = JsonNode.Parse(MessagesWire.Instance.Body(new ApiRequest(new ApiProvider("anthropic", "Anthropic", "http://localhost/v1", ApiWire.Messages, ""), "k", "m", ApiWire.Messages, "",
            [.. history, new ApiMessage(ApiMessage.User, "now", [])], null, null)))!["messages"]!.AsArray();
        Check.Eq(body.Count, 3, "user, assistant, user");
        var last = body[2]!["content"]!.AsArray();
        Check.True((string?)last[0]!["type"] == "tool_result" && (string?)last[1]!["text"] == "now", "the results first, then the question");
    }
}
