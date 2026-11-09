using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Bloom.App;

/// One of the questions Claude Code's AskUserQuestion puts (its question, its short header, its choices and whether
/// several may be picked).
internal sealed record CardQuestion(string Text, string Header, IReadOnlyList<CardChoice> Choices, bool Multi);

internal sealed record CardChoice(string Label, string Description);

/// The questions an approval card asks in place of 허용 (ClaudeApprovals): the card shows them with their choices and a
/// line to type an answer of one's own (기타), and hands the answers to `Answer`.
internal sealed record CardQuestions(IReadOnlyList<CardQuestion> Items, Action<IReadOnlyDictionary<string, string>> Answer)
{
    /// What AskUserQuestion takes for an answer: the picked labels in the order shown, then the typed one, joined with
    /// ", " as Claude Code's own dialog does; empty for a question not answered.
    public static string Joined(CardQuestion q, IEnumerable<int> picked, string? typed)
    {
        var parts = picked.Order().Where(i => i >= 0 && i < q.Choices.Count).Select(i => q.Choices[i].Label).ToList();
        if (!string.IsNullOrWhiteSpace(typed)) parts.Add(typed.Trim());
        return string.Join(", ", q.Multi ? parts : parts.Take(1));
    }

    /// AskUserQuestion's `questions` → the questions; null when they aren't the shape the card can show (none, more
    /// than four, a question without text or choices, two with the same text: the answers go by the text).
    public static IReadOnlyList<CardQuestion>? Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("questions", out var qs) || qs.ValueKind != JsonValueKind.Array) return null;
        var list = new List<CardQuestion>();
        foreach (var q in qs.EnumerateArray())
        {
            if (q.ValueKind != JsonValueKind.Object || Str(q, "question") is not { } text
                || !q.TryGetProperty("options", out var os) || os.ValueKind != JsonValueKind.Array) return null;
            var choices = new List<CardChoice>();
            foreach (var o in os.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object || Str(o, "label") is not { } label) return null;
                choices.Add(new CardChoice(label, Str(o, "description") ?? ""));
            }
            if (choices.Count is 0 or > 8) return null;
            bool multi = q.TryGetProperty("multiSelect", out var m) && m.ValueKind == JsonValueKind.True;
            list.Add(new CardQuestion(text, Str(q, "header") ?? "", choices, multi));
        }
        if (list.Count is 0 or > 4 || list.Select(q => q.Text).Distinct().Count() != list.Count) return null;
        return list;
    }

    /// A text field as it came (the answers go by the question's exact text); null when missing or blank.
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}
