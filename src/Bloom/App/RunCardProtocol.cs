using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.App;

/// The lines Bloom and the Run Card add-on trade over RunCardLink's pipe (v1): one JSON object per line, its kind in
/// `t`. Reading and writing only — no pipe, no state — so every shape is checked without either end running.
internal static class RunCardProtocol
{
    /// The protocol both ends speak; a hello of any other is not taken (Bloom keeps its own cards).
    public const int Version = 1;

    /// The pipe's part of the name (Paths.Pipe).
    public const string PipeKind = "run-card";

    /// An answer of "the terminal asks": Bloom answers nothing and brings the session's window to the front.
    public const string Terminal = "terminal";

    // the add-on's lines
    public const string Hello = "hello", Prefs = "prefs", Shown = "shown", None = "none", Answer = "answer", Open = "open";

    // the add-on reads UTF-8 as it is: only what JSON needs escaped is
    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string HelloLine() => new JsonObject { ["t"] = Hello, ["v"] = Version }.ToJsonString(Plain);

    public static string CancelLine(string id) => new JsonObject { ["t"] = "cancel", ["id"] = id }.ToJsonString(Plain);

    /// A request for the add-on's card: what Bloom's own card would show, AskUserQuestion's questions with it.
    public static string AskLine(string id, ClaudeApprovals.Request r)
    {
        var o = new JsonObject { ["t"] = "ask", ["id"] = id, ["session"] = r.Session, ["pid"] = r.Pid, ["tool"] = r.Tool, ["body"] = r.Body };
        if (r.Questions is { } questions)
            o["questions"] = new JsonArray(questions.Select(q => (JsonNode)new JsonObject
            {
                ["header"] = q.Header,
                ["text"] = q.Text,
                ["multi"] = q.Multi,
                ["choices"] = new JsonArray(q.Choices.Select(c => (JsonNode)new JsonObject { ["label"] = c.Label, ["description"] = c.Description }).ToArray()),
            }).ToArray());
        return o.ToJsonString(Plain);
    }

    /// One of the add-on's lines; null for anything not of a known kind and shape (it is ignored).
    public static RunCardMessage? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object || Str(o, "t") is not { } kind) return null;
            switch (kind)
            {
                case Hello:
                    if (!o.TryGetProperty("v", out var v) || Int(v) != Version) return null;
                    return new RunCardMessage(kind, Approvals: Bool(o, "approvals") ?? false);
                case Prefs:
                    return Bool(o, "approvals") is { } approvals ? new RunCardMessage(kind, Approvals: approvals) : null;
                case Shown or None:
                    return Str(o, "id") is { } id ? new RunCardMessage(kind, Id: id) : null;
                case Answer:
                    return Answered(o);
                case Open:
                    if (Str(o, "session") is not { } session) return null;
                    int pid = o.TryGetProperty("pid", out var p) && Int(p) is > 0 and int n ? n : 0;
                    return new RunCardMessage(kind, Session: session, Pid: pid);
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// `answer`: a word (allow | deny | terminal) or the picks per question, never both.
    private static RunCardMessage? Answered(JsonElement o)
    {
        if (Str(o, "id") is not { } id) return null;
        bool hasValue = o.TryGetProperty("value", out var value), hasPicks = o.TryGetProperty("picks", out var picks);
        if (hasValue == hasPicks) return null;
        if (hasValue)
            return value.ValueKind == JsonValueKind.String && value.GetString() is "allow" or "deny" or Terminal ? new RunCardMessage(Answer, Id: id, Value: value.GetString()) : null;
        if (picks.ValueKind != JsonValueKind.Array) return null;
        var all = new List<IReadOnlyList<int>>();
        foreach (var question in picks.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Array) return null;
            var one = new List<int>();
            foreach (var pick in question.EnumerateArray())
            {
                if (Int(pick) is not { } i || i < 0) return null;
                one.Add(i);
            }
            all.Add(one);
        }
        return new RunCardMessage(Answer, Id: id, Picks: all);
    }

    /// An `answer` as ClaudeApprovals hands it to the hook: "allow", "deny", Terminal, or for AskUserQuestion
    /// ClaudeApprovalHook.Answers and the answers, made as Bloom's own card makes them (CardQuestions.Joined, keyed by
    /// the question's text). Null when it doesn't fit the request: 허용 on questions, picks for a tool, a question
    /// missed or a choice that isn't there — like Bloom's card, every question needs its answer.
    public static string? AnswerFor(RunCardMessage m, IReadOnlyList<CardQuestion>? questions)
    {
        if (m.Kind != Answer) return null;
        if (m.Value is { } word) return word == "allow" && questions is not null ? null : word;
        if (m.Picks is not { } picks || questions is null || picks.Count != questions.Count) return null;
        var given = new Dictionary<string, string>();
        for (int i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            if (picks[i].Any(p => p >= q.Choices.Count) || picks[i].Distinct().Count() != picks[i].Count || (!q.Multi && picks[i].Count > 1)) return null;
            string answer = CardQuestions.Joined(q, picks[i], null);
            if (answer.Length == 0) return null;
            given[q.Text] = answer;
        }
        return ClaudeApprovalHook.Answers + JsonSerializer.Serialize(given);
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    /// A whole number in int range; null for anything else (a string, a fraction).
    private static int? Int(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int n) ? n : null;

    private static bool? Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
}
