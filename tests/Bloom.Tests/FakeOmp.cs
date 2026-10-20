using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Bloom.Tests;

/// A stand-in for `omp --mode rpc` (dev only): this test program, started with omp's own arguments (OmpCli.ChatArgs), answers
/// the rpc lines OmpSession sends the way omp does, so the session's whole path is run without a model. The arguments it got
/// go to the file $FAKE_OMP_ARGS, one per line.
///   get_state  → the session id "fake-session" and the context numbers (tokens null until a turn has happened)
///   prompt     → "hello": text_start, two text_delta, prompt_result completed · "SEARCH ": a web_search tool first
///                "FAIL": prompt_result error "401 unauthorized" · "SLOW": text, then waits for an abort
///                "/session": a builtin slash command (command_output, agentInvoked false)
///   abort      → ends a SLOW turn (prompt_result aborted)
///   compact    → success, tokensBefore 12000, and the numbers are unknown again
internal static class FakeOmp
{
    /// The program was started as omp's rpc mode.
    public static bool Called(string[] args) => args.Length >= 2 && args[0] == "--mode" && args[1] == "rpc";

    public static int Run(string[] args)
    {
        if (Environment.GetEnvironmentVariable("FAKE_OMP_ARGS") is { Length: > 0 } file) File.WriteAllLines(file, args);
        Say(new JsonObject { ["type"] = "ready", ["protocolVersion"] = 1 });
        int turns = 0;
        bool slow = false, known = false;
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            var cmd = JsonNode.Parse(line)!;
            string type = (string)cmd["type"]!, id = (string?)cmd["id"] ?? "";
            switch (type)
            {
                case "get_state":
                    var usage = new JsonObject { ["tokens"] = known ? turns * 1200 : null, ["contextWindow"] = 200000 };
                    Say(new JsonObject
                    {
                        ["id"] = id, ["type"] = "response", ["command"] = "get_state", ["success"] = true,
                        ["data"] = new JsonObject { ["sessionId"] = "fake-session", ["contextUsage"] = usage },
                    });
                    break;
                case "prompt":
                    string message = (string)cmd["message"]!;
                    if (message.StartsWith("/session", StringComparison.Ordinal))
                    {
                        Say(new JsonObject { ["type"] = "command_output", ["text"] = "Session: fake-session" });
                        Say(new JsonObject { ["id"] = id, ["type"] = "response", ["command"] = "prompt", ["success"] = true, ["data"] = new JsonObject { ["agentInvoked"] = false } });
                        break;
                    }
                    Say(new JsonObject { ["id"] = id, ["type"] = "response", ["command"] = "prompt", ["success"] = true });
                    turns++;
                    known = true;
                    if (message.Contains("FAIL", StringComparison.Ordinal))
                    {
                        Say(new JsonObject { ["type"] = "prompt_result", ["id"] = id, ["status"] = "error", ["error"] = "401 unauthorized" });
                        break;
                    }
                    if (message.Contains("SEARCH", StringComparison.Ordinal))
                        Say(new JsonObject { ["type"] = "tool_execution_start", ["toolCallId"] = "t1", ["toolName"] = "web_search", ["args"] = new JsonObject() });
                    Say(Event("text_start"));
                    Say(Event("text_delta", "Hello, "));
                    if (message.Contains("SLOW", StringComparison.Ordinal))
                    {
                        slow = true;
                        break;
                    }
                    Say(Event("text_delta", "다시"));
                    Say(new JsonObject { ["type"] = "prompt_result", ["id"] = id, ["status"] = "completed" });
                    break;
                case "abort":
                    Say(new JsonObject { ["id"] = id, ["type"] = "response", ["command"] = "abort", ["success"] = true });
                    if (slow)
                    {
                        slow = false;
                        Say(new JsonObject { ["type"] = "prompt_result", ["status"] = "aborted" });
                    }
                    break;
                case "compact":
                    known = false;
                    Say(new JsonObject { ["id"] = id, ["type"] = "response", ["command"] = "compact", ["success"] = true, ["data"] = new JsonObject { ["summary"] = "s", ["tokensBefore"] = 12000 } });
                    break;
            }
        }
        return 0;
    }

    private static JsonObject Event(string kind, string? delta = null)
    {
        var e = new JsonObject { ["type"] = kind, ["contentIndex"] = 0 };
        if (delta is not null) e["delta"] = delta;
        return new JsonObject { ["type"] = "message_update", ["assistantMessageEvent"] = e };
    }

    private static void Say(JsonObject o)
    {
        Console.Out.WriteLine(o.ToJsonString());
        Console.Out.Flush();
    }
}
