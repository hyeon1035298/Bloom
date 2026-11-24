using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Ai;

/// A thread in T3's list (t3_thread_list, or the `thread` of a t3_thread_read): its project, title and status, the
/// provider instance and model it runs on, when it last changed, whether it is settled (marked done in T3), and how
/// many requests wait on the user (0 when the list doesn't say).
internal sealed record T3Row(string Id, string? ProjectId, string Title, string Status, string Instance, string Model, DateTime Updated, bool Settled, int Pending)
{
    /// T3 has a turn of it going or about to (it reads on, and a send steers it).
    public bool Active => T3Wire.ActiveStatus(Status);
}

/// A project (folder) T3 keeps: its name, its folder, and the model new threads in it start with (null: T3's own).
internal sealed record T3Project(string Id, string Title, string? Root, string? Instance, string? Model, JsonNode? Options);

/// One item of a thread (a message, a tool call, a note), at its position; `Done` once T3 has finished writing it.
internal sealed record T3Item(int Position, string Id, string Type, string Status, string Text, string? Title, bool Truncated)
{
    public bool Done => T3Wire.DoneStatus(Status);
}

/// A page of t3_thread_read: the thread as it is now, the items (in position order), the position to read on from,
/// whether there is more after it, and the position before the page (for older ones).
internal sealed record T3Page(T3Row? Thread, List<T3Item> Items, int? Next, bool More, int? Earlier);

/// T3's answers to its agent tools, read as Bloom uses them. Nothing here talks to T3 (T3Agent does).
internal static class T3Wire
{
    /// A thread status of T3's while a turn of it runs, waits on the user, or is about to start.
    public static bool ActiveStatus(string status) => status is "preparing" or "queued" or "starting" or "running" or "waiting";

    /// An item status that means T3 has finished writing it (anything else is still being written).
    public static bool DoneStatus(string status) => status is "completed" or "failed" or "cancelled" or "canceled" or "interrupted" or "declined" or "error" or "stopped" or "rolled_back";

    /// t3_thread_list: the threads Bloom lists (not a subagent's, not archived, not snoozed) and the cursor of the page after.
    public static (List<T3Row> Rows, int? Next) List(JsonNode answer)
    {
        var rows = new List<T3Row>();
        if (answer["threads"] is JsonArray threads)
            foreach (var t in threads)
                if (t is JsonObject o && Shown(o) && Row(o) is { } row) rows.Add(row);
        return (rows, Int(answer["nextCursor"]));
    }

    /// A thread Bloom lists: a top-level one (T3 lists a subagent's even when asked not to, through `parentThreadId`
    /// or `relationshipToParent`), neither archived nor snoozed.
    internal static bool Shown(JsonObject thread) =>
        Str(thread["parentThreadId"]) is null && Str(thread["relationshipToParent"]) is not "subagent"
        && thread["archived"]?.GetValueKind() != JsonValueKind.True && thread["snoozed"]?.GetValueKind() != JsonValueKind.True;

    public static T3Row? Row(JsonNode? thread)
    {
        if (thread is not JsonObject o || Str(o["threadId"]) is not { Length: > 0 } id) return null;
        return new T3Row(id, Str(o["projectId"]), Str(o["title"]) ?? "", Str(o["status"]) ?? "idle", Str(o["providerInstanceId"]) ?? "", Str(o["model"]) ?? "",
            Time(o["updatedAt"]) ?? Time(o["createdAt"]) ?? DateTime.MinValue, o["settled"]?.GetValueKind() == JsonValueKind.True, Int(o["pendingRequestCount"]) ?? 0);
    }

    /// t3_thread_read: the thread and a page of its items, in position order.
    public static T3Page Read(JsonNode answer)
    {
        var items = new List<T3Item>();
        if (answer["items"] is JsonArray list)
            foreach (var i in list)
            {
                if (i is not JsonObject o || Int(o["position"]) is not { } position || Str(o["itemId"]) is not { Length: > 0 } id) continue;
                items.Add(new T3Item(position, id, Str(o["type"]) ?? "", Str(o["status"]) ?? "", Str(o["text"]) ?? "", Str(o["title"]), o["textTruncated"]?.GetValueKind() == JsonValueKind.True));
            }
        items.Sort((a, b) => a.Position.CompareTo(b.Position));
        return new T3Page(Row(answer["thread"]), items, Int(answer["nextPosition"]), answer["hasMore"]?.GetValueKind() == JsonValueKind.True, Int(answer["earlierPosition"]));
    }

    /// t3_project_list: the projects (not archived nor deleted) and the cursor of the page after.
    public static (List<T3Project> Projects, int? Next) Projects(JsonNode answer)
    {
        var list = new List<T3Project>();
        if (answer["projects"] is JsonArray projects)
            foreach (var p in projects)
            {
                if (p is not JsonObject o || Str(o["id"]) is not { Length: > 0 } id || Str(o["archivedAt"]) is not null || Str(o["deletedAt"]) is not null) continue;
                var model = o["defaultModelSelection"] as JsonObject;
                list.Add(new T3Project(id, Str(o["title"]) ?? id, Str(o["workspaceRoot"]), Str(model?["instanceId"]), Str(model?["model"]), model?["options"]?.DeepClone()));
            }
        return (list, Int(answer["nextCursor"]));
    }

    /// The id of the thread t3_thread_launch made (`threadId`, or the `thread` it gives back); null when it names none.
    public static string? Launched(JsonNode answer) => Str(answer["threadId"]) ?? Str(answer["thread"]?["threadId"]) ?? Str(answer["id"]);

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s) ? s : null;

    private static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : null;

    private static DateTime? Time(JsonNode? n) => Str(n) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
