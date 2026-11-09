using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// `Bloom.exe --claude-approve [--data <dir>]`: Claude Code's PermissionRequest hook (Ai\ClaudeHooks). Carries the
/// request and the Claude Code process that asks to the running Bloom (ClaudeApprovals) over a pipe only this Windows
/// user can open, then prints the answer as the hook's JSON. No Bloom running, or an answer of "the terminal", prints
/// nothing: Claude Code then asks in the terminal as it would without the hook. No window, no WPF.
internal static class ClaudeApprovalHook
{
    public const string Arg = "--claude-approve";

    /// Bloom's approval pipe.
    public static string Pipe => Paths.Pipe("approve");

    private static readonly UTF8Encoding Utf8 = new(false);

    public static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--data");
        Paths.Init(at >= 0 && at + 1 < args.Length ? args[at + 1] : null);
        string input;
        using (var reader = new StreamReader(Console.OpenStandardInput(), Utf8)) input = reader.ReadToEnd();
        int parent = Win32.ProcessTree().TryGetValue(Environment.ProcessId, out var me) ? me.parent : 0; // the Claude Code that asks
        if (Output(Ask(Pipe, parent, input), input) is { } json)
        {
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(Utf8.GetBytes(json));
        }
        return 0;
    }

    /// Bloom's answer to AskUserQuestion: this, then the answers as a JSON object (question text → answer).
    public const string Answers = "answers:";

    /// The running Bloom's answer: "allow", "deny", "answers:{…}", or null (no Bloom, or the terminal asks).
    internal static string? Ask(string pipe, int pid, string input)
    {
        if (!Directory.EnumerateFiles(@"\\.\pipe\", pipe).Any()) return null; // Bloom isn't running: no wait for it
        try
        {
            using var link = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            link.Connect(1000);
            using var writer = new StreamWriter(link, Utf8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new { pid, input }));
            using var reader = new StreamReader(link, Utf8, leaveOpen: true); // the link closes last
            string? answer = reader.ReadLine();
            return answer is "allow" or "deny" || answer?.StartsWith(Answers, StringComparison.Ordinal) == true ? answer : null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// The hook's JSON for an answer (null: no decision). Answers go back as an allow whose updatedInput is the tool's
    /// input with them in it, which Claude Code takes as the user's answers to AskUserQuestion.
    internal static string? Output(string? answer, string? input = null) => answer switch
    {
        "allow" => JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "allow" } } }),
        not null when answer.StartsWith(Answers, StringComparison.Ordinal) => Answered(input, answer[Answers.Length..]) is { } updated
            ? new JsonObject { ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = new JsonObject { ["behavior"] = "allow", ["updatedInput"] = updated } } }.ToJsonString()
            : null,
        "deny" => JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior = "deny", message = "The user declined this on Bloom's notice card." } } }),
        _ => null,
    };

    /// The tool's input as the hook got it, with `answers` in it; null when either isn't the JSON object it should be.
    private static JsonObject? Answered(string? input, string answers)
    {
        try
        {
            if (JsonNode.Parse(input ?? "") is not JsonObject hook || hook["tool_input"] is not JsonObject tool
                || JsonNode.Parse(answers) is not JsonObject given || given.Any(a => a.Value?.GetValueKind() != JsonValueKind.String)) return null;
            var updated = (JsonObject)tool.DeepClone();
            updated["answers"] = given.DeepClone();
            return updated;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
