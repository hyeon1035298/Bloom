using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// A T3 thread as Bloom's chat shows it, from the items T3 has written (t3_thread_read): every item kept by its id, a
/// newer read of the same item replacing the older (a message still being written comes back longer), and the items in
/// position order read as Bloom's messages: a user message each, and between them one answer holding the model's words,
/// a line per tool call (ℹ) and, while T3 still runs it, the tool being used as its status. Nothing here talks to T3.
internal sealed class T3Mirror
{
    private readonly Dictionary<string, T3Item> items = new();

    /// How many tool lines an answer shows before the older ones fold into one "N more" line.
    internal const int MaxToolLines = 8;

    public int Count => items.Count;

    public void Take(IEnumerable<T3Item> page)
    {
        foreach (var i in page) items[i.Id] = i;
    }

    /// The last position read; null before anything is.
    public int? Last => items.Count == 0 ? null : items.Values.Max(i => i.Position);

    /// Where reading goes on from (t3_thread_read's afterPosition): just before the first item T3 hasn't finished
    /// writing (so it is read again until it is), else after the last; -1 before anything is read.
    public int After() => items.Values.Where(i => !i.Done).Select(i => (int?)i.Position).Min() is { } open ? open - 1 : Last ?? -1;

    /// Where to read on from when the thread is opened mid-turn: from its last user message (its turn's tool calls
    /// weren't in the messages view), else as After.
    public int AfterOpen() => items.Values.Where(i => i.Type == "user_message").Select(i => (int?)i.Position).Max() is { } user ? Math.Min(user - 1, After()) : After();

    /// The messages, in order. `running`: T3 runs a turn of the thread now, so the last answer is being written (one
    /// with nothing in it yet when the turn has written nothing).
    public List<ChatMessage> Messages(bool running)
    {
        var list = new List<ChatMessage>();
        ChatMessage? answer = null;
        string? inUse = null;
        bool thinking = false;
        int tools = 0;
        foreach (var item in items.Values.OrderBy(i => i.Position))
        {
            switch (Kind(item.Type))
            {
                case Part.User:
                    answer = null;
                    inUse = null;
                    thinking = false;
                    tools = 0;
                    list.Add(new ChatMessage { Role = "user", Text = Whole(item), Sent = item.Text });
                    break;
                case Part.Words:
                    answer ??= Add(list);
                    string words = Whole(item);
                    if (words.Length == 0) break;
                    answer.Text = answer.Text.Length == 0 ? words : answer.Text.TrimEnd() + "\n\n" + words;
                    thinking = false;
                    break;
                case Part.Tool:
                    answer ??= Add(list);
                    if (item.Done)
                    {
                        (answer.Done ??= new()).Add("ℹ " + ToolLine(item));
                        tools++;
                        if (inUse == item.Id) inUse = null;
                    }
                    else inUse = item.Id;
                    break;
                case Part.Thinking:
                    if (!item.Done)
                    {
                        answer ??= Add(list);
                        thinking = true;
                    }
                    break;
            }
            if (answer?.Done is { Count: > MaxToolLines } lines)
            {
                // the older ones fold: "도구 3개 더" and the last ones as they were
                int folded = tools - (MaxToolLines - 1);
                answer.Done = new List<string> { "ℹ " + Tr($"도구 {folded}개 더 씀", $"{folded} more tool calls") }.Concat(lines.Skip(lines.Count - (MaxToolLines - 1))).ToList();
            }
        }
        if (!running) return list;
        if (list.Count == 0 || list[^1].Role == "user") list.Add(answer = new ChatMessage { Role = "assistant" });
        var last = list[^1];
        last.Streaming = true;
        last.Status = inUse is not null && items.TryGetValue(inUse, out var tool) ? ToolLine(tool) + " …"
            : thinking ? Tr("생각하는 중…", "Thinking…") : null;
        return list;
    }

    private static ChatMessage Add(List<ChatMessage> list)
    {
        var m = new ChatMessage { Role = "assistant" };
        list.Add(m);
        return m;
    }

    private enum Part { User, Words, Tool, Thinking, Other }

    private static Part Kind(string type) => type switch
    {
        "user_message" => Part.User,
        "assistant_message" => Part.Words,
        "reasoning" => Part.Thinking,
        // a checkpoint, a note of T3's own (a background command that finished), the plan: not words of the chat
        "checkpoint" or "notification" or "plan" or "todo_list" or "context_compaction" or "" => Part.Other,
        _ => Part.Tool, // dynamic_tool, command_execution, file_change, mcp_tool_call, web_search …
    };

    /// An item's text, with … where T3 cut it short.
    private static string Whole(T3Item item) => item.Truncated ? item.Text.TrimEnd() + "…" : item.Text;

    /// One line for a tool call: its title, else the tool's name in its text, else its kind.
    internal static string ToolLine(T3Item item)
    {
        if (item.Title is { Length: > 0 } title) return ToolText.Line(title);
        if (item.Text.TrimStart().StartsWith('{'))
        {
            try
            {
                if (JsonNode.Parse(item.Text) is JsonObject o && (o["toolName"] ?? o["command"]) is JsonValue v && v.TryGetValue(out string? name) && name.Length > 0) return ToolText.Line(name);
            }
            catch (JsonException)
            {
                // cut short (textTruncated) or not JSON: the kind says it
            }
        }
        return item.Type.Replace('_', ' ');
    }

    /// `into` made to read as `fresh`, keeping the messages that are still the same ones (the chat view redraws only what
    /// changed); true when anything changed.
    public static bool Apply(List<ChatMessage> into, IReadOnlyList<ChatMessage> fresh)
    {
        bool changed = into.Count != fresh.Count;
        for (int i = 0; i < fresh.Count; i++)
        {
            var f = fresh[i];
            if (i >= into.Count)
            {
                into.Add(f);
                continue;
            }
            var m = into[i];
            if (m.Role != f.Role)
            {
                into[i] = f;
                changed = true;
                continue;
            }
            if (m.Text != f.Text || m.Streaming != f.Streaming || m.Status != f.Status || m.Error != f.Error || !Same(m.Done, f.Done)
                || !ReferenceEquals(m.Question, f.Question) && m.Question?.RequestId != f.Question?.RequestId || m.ApprovalWait != f.ApprovalWait)
            {
                m.Text = f.Text;
                m.Sent = f.Sent;
                m.Streaming = f.Streaming;
                m.Status = f.Status;
                m.Error = f.Error;
                m.Done = f.Done;
                m.Question = f.Question;
                m.ApprovalWait = f.ApprovalWait;
                changed = true;
            }
        }
        if (into.Count > fresh.Count) into.RemoveRange(fresh.Count, into.Count - fresh.Count);
        return changed;
    }

    private static bool Same(List<string>? a, List<string>? b) => (a?.Count ?? 0) == (b?.Count ?? 0) && (a is null || b is null || a.SequenceEqual(b));
}
