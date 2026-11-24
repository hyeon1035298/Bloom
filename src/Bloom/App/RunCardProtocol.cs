using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Ai;

namespace Bloom.App;

/// The lines Bloom and the Run Card add-on trade over RunCardLink's pipe (v1): one JSON object per line, its kind in
/// `t`. Reading and writing only — no pipe, no state — so every shape is checked without either end running. The
/// lines about a session (can, say, question, stop, kin) are also the T3 pipe's (T3Link), for the other add-ons.
internal static class RunCardProtocol
{
    /// The protocol both ends speak; a hello of any other is not taken (Bloom keeps its own cards).
    public const int Version = 1;

    /// The pipe's part of the name (Paths.Pipe).
    public const string PipeKind = "run-card";

    /// An answer of "the terminal asks": Bloom answers nothing and brings the session's window to the front.
    public const string Terminal = "terminal";

    // the add-on's lines
    public const string Hello = "hello", Prefs = "prefs", Shown = "shown", None = "none", Answer = "answer", Open = "open", Stop = "stop";

    // the add-on's lines for 보내기 (a T3 session's card): whether a session takes words, the question it waits on
    // (asked back with its choices), and words for it (with the choices picked for that question)
    public const string Can = "can", Say = "say", Question = "question";

    /// The add-on's line for a card: the session that started this one, and the sub-sessions it started at work (T3Threads.Kin).
    public const string Kin = "kin";

    /// Bloom's answer to a `stop`.
    public const string Stopped = "stopped";

    /// The longest words a `say` carries (characters); longer ones are not taken.
    public const int MaxSay = 20000;

    // the add-on reads UTF-8 as it is: only what JSON needs escaped is
    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string HelloLine() => new JsonObject { ["t"] = Hello, ["v"] = Version }.ToJsonString(Plain);

    public static string CancelLine(string id) => new JsonObject { ["t"] = "cancel", ["id"] = id }.ToJsonString(Plain);

    /// Whether `session`'s card shows 보내기 (`say`) and, while it works, 정지 (`stop`) through Bloom: T3 runs it
    /// (Ai.T3Agent.Can). A terminal session's 정지 is the card's own to know (its session file).
    public static string CanLine(string session, bool say, bool stop) =>
        new JsonObject { ["t"] = Can, ["session"] = session, ["say"] = say, ["stop"] = stop }.ToJsonString(Plain);

    /// What came of a `say` (Ai.T3Agent.Sent): `how` it went (answered | steered | started | queued), or `why` not.
    public static string SaidLine(string session, bool ok, string word) =>
        new JsonObject { ["t"] = "said", ["session"] = session, ["ok"] = ok, [ok ? "how" : "why"] = word }.ToJsonString(Plain);

    /// The answer to a `kin`: `session`'s parent (null: none) and its sub-sessions at work; none of either when
    /// `family` is null ('T3 Code 연결' is off).
    public static string KinLine(string session, T3Family? family)
    {
        var kids = new JsonArray();
        foreach (var k in family?.Kids ?? [])
            kids.Add(new JsonObject
            {
                ["thread"] = k.Thread,
                ["session"] = k.Session,
                ["title"] = k.Title,
                ["company"] = k.Company,
                ["model"] = k.Model,
                ["state"] = k.State,
                ["start"] = k.Start,
                ["end"] = k.End,
            });
        return new JsonObject { ["t"] = Kin, ["session"] = session, ["parent"] = family?.Parent, ["kids"] = kids }.ToJsonString(Plain);
    }

    /// What came of a `stop`: sent, or `why` not (Ai.T3Agent.Sent's words; "not-stopped" for a terminal session Bloom
    /// would not or could not stop).
    public static string StoppedLine(string session, bool ok, string? why)
    {
        var o = new JsonObject { ["t"] = Stopped, ["session"] = session, ["ok"] = ok };
        if (!ok) o["why"] = why ?? "failed";
        return o.ToJsonString(Plain);
    }

    /// A request for the add-on's card: what Bloom's own card would show, AskUserQuestion's questions with it.
    public static string AskLine(string id, ClaudeApprovals.Request r)
    {
        var o = new JsonObject { ["t"] = "ask", ["id"] = id, ["session"] = r.Session, ["pid"] = r.Pid, ["tool"] = r.Tool, ["body"] = r.Body };
        if (r.Questions is { } questions) o["questions"] = Questions(questions);
        return o.ToJsonString(Plain);
    }

    /// The question a T3 session waits on, for its card to show with its choices (none: no question, or T3 not
    /// reached; the card keeps its row to type in).
    public static string QuestionLine(string session, IReadOnlyList<CardQuestion>? questions) =>
        new JsonObject { ["t"] = Question, ["session"] = session, ["questions"] = Questions(questions ?? []) }.ToJsonString(Plain);

    private static JsonArray Questions(IEnumerable<CardQuestion> questions) => new(questions.Select(q => (JsonNode)new JsonObject
    {
        ["header"] = q.Header,
        ["text"] = q.Text,
        ["multi"] = q.Multi,
        ["choices"] = new JsonArray(q.Choices.Select(c => (JsonNode)new JsonObject { ["label"] = c.Label, ["description"] = c.Description }).ToArray()),
    }).ToArray());

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
                case Can or Question or Kin:
                    return Str(o, "session") is { } asked ? new RunCardMessage(kind, Session: asked) : null;
                case Say:
                    return Said(o);
                case Open or Stop:
                    if (Str(o, "session") is not { } session) return null;
                    // a terminal session's stop names the process it goes to; a T3 session's needs none (T3Lines)
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

    /// `say`: the words as typed (only an overlong line is cut off: not taken), the choices picked for the question
    /// the session waits on, or both; not neither.
    private static RunCardMessage? Said(JsonElement o)
    {
        if (Str(o, "session") is not { } to) return null;
        string text = "";
        if (o.TryGetProperty("text", out var t))
        {
            if (t.ValueKind != JsonValueKind.String || t.GetString() is not { } typed || typed.Length > MaxSay) return null;
            text = typed;
        }
        IReadOnlyList<IReadOnlyList<int>>? picks = null;
        if (o.TryGetProperty("picks", out var p) && (picks = Picked(p)) is null) return null;
        bool picked = picks?.Any(q => q.Count > 0) == true;
        return string.IsNullOrWhiteSpace(text) && !picked ? null : new RunCardMessage(Say, Session: to, Text: text, Picks: picked ? picks : null);
    }

    /// `[[0],[1,2]]`: the choice indexes picked, per question in order; null for anything else.
    private static List<IReadOnlyList<int>>? Picked(JsonElement picks)
    {
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
        return all;
    }

    /// `answer`: a word (allow | deny | terminal) or the picks per question, never both.
    private static RunCardMessage? Answered(JsonElement o)
    {
        if (Str(o, "id") is not { } id) return null;
        bool hasValue = o.TryGetProperty("value", out var value), hasPicks = o.TryGetProperty("picks", out var picks);
        if (hasValue == hasPicks) return null;
        if (hasValue)
            return value.ValueKind == JsonValueKind.String && value.GetString() is "allow" or "deny" or Terminal ? new RunCardMessage(Answer, Id: id, Value: value.GetString()) : null;
        return Picked(picks) is { } all ? new RunCardMessage(Answer, Id: id, Picks: all) : null;
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
