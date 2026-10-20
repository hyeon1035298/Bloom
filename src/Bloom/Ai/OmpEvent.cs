using System;
using System.Linq;
using System.Text.Json;

namespace Bloom.Ai;

/// What one line of omp's rpc output means for a chat. CompactStart/CompactEnd: omp's own (automatic) tidying of the model's memory; Compacted/CompactFailed: the answer to the rpc
/// `compact` Bloom sent; CommandOutput: what a builtin slash command printed; LocalDone: such a command finished without a turn
/// (no prompt_result follows); Usage: a get_state answer with the context numbers (SessionId carries them too).
internal enum OmpKind { None, Delta, Status, StatusClear, SessionId, TurnEnded, CompactStart, CompactEnd, Compacted, CompactFailed, CommandOutput, LocalDone, Usage }

/// What the model holds now: `Tokens` is null when omp can't tell yet (right after a new session or a compaction).
internal readonly record struct OmpUsage(long? Tokens, long Window)
{
    public double Percent => Tokens is { } t && Window > 0 ? t * 100.0 / Window : 0;

    /// "14.2k / 200k"; "? / 200k" while the count is unknown.
    public string Gauge => (Tokens is { } t ? Count(t) : "?") + " / " + Count(Window);

    /// 12400 → "12.4k", 1000000 → "1M", 850 → "850".
    public static string Count(long n)
    {
        if (n >= 1_000_000) return Trim(n / 1_000_000.0) + "M";
        if (n >= 1000) return Trim(n / 1000.0) + "k";
        return n.ToString();
    }

    private static string Trim(double v) => v >= 100 ? v.ToString("0") : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}

/// `Tokens`/`Window` of a get_state answer's contextUsage (Usage non-null), or the tokens before and after of a compaction.
/// `Detail` of CompactEnd: "done", "skipped", "aborted" or "failed".
internal readonly record struct OmpEvent(OmpKind Kind, string Text = "", bool Ok = true, OmpUsage? Usage = null, long Before = -1, string Detail = "")
{
    /// omp's tidying methods in words: "remote" (the provider summarises), "soft"/"handoff" (a summary), "snapcompact" (the
    /// old part printed as pictures), "shake" (long tool results cut out, nothing summarised).
    public static string CompactStartText(string action) => action switch
    {
        "shake" => Tr("대화가 길어져 긴 도구 결과를 덜어내는 중…", "The chat got long — trimming long tool results…"),
        _ => Tr("대화가 길어져 앞부분을 정리하는 중…", "The chat got long — summarising the earlier part…"),
    };

    /// omp's refusal of a compact in words Bloom's user can act on.
    public static string CompactFailText(string error)
    {
        string l = error.ToLowerInvariant();
        if (l.Contains("too small")) return Tr("아직 정리할 만큼 길지 않습니다", "The chat isn't long enough to tidy yet");
        if (l.Contains("already compacted")) return Tr("이미 정리했습니다 — 더 줄일 내용이 없습니다", "Already tidied — nothing more to cut");
        return Tr("정리하지 못했습니다: ", "Couldn't tidy: ") + error;
    }

    /// The line when it is over; `before`/`after` are the model's tokens around it (-1 = unknown). `skipped`: omp found nothing to gain.
    public static string CompactEndText(string action, long before, long after, bool aborted, bool skipped, bool failed = false)
    {
        if (aborted) return Tr("정리를 멈췄습니다", "Tidying stopped");
        if (skipped) return Tr("정리할 만큼 줄어들지 않아 그대로 두었습니다", "Nothing worth cutting, left as it was");
        if (failed) return Tr("앞부분을 정리하지 못했습니다", "Couldn't tidy the earlier part");
        string span = before >= 0 && after >= 0 ? $" ({OmpUsage.Count(before)}→{OmpUsage.Count(after)} {Tr("토큰", "tokens")})" : before >= 0 ? $" ({Tr("이전", "was")} {OmpUsage.Count(before)} {Tr("토큰", "tokens")})" : "";
        return action == "shake"
            ? Tr("긴 도구 결과를 덜어냈습니다", "Trimmed long tool results") + span
            : Tr("앞부분을 요약했습니다", "Summarised the earlier part") + span;
    }

    /// The tool status line ("웹 검색 중…") for a tool name.
    public static string ToolStatus(string tool) => tool switch
    {
        "web_search" => Tr("웹 검색 중…", "Searching the web…"),
        _ when OmpMcp.Doing(tool) is { } doing => doing,
        _ => tool + Tr(" 사용 중…", " in use…"),
    };

    /// One stdout line of `omp --mode rpc`. Frames Bloom does not use (ready, events of the agent's inner turns, extension
    /// frames) are OmpKind.None. A turn ends with its `prompt_result`: completed and aborted (the user's stop: the text so
    /// far stays) are fine, anything else is an error with omp's own words.
    public static OmpEvent Parse(string line)
    {
        if (line.Length == 0 || line[0] != '{') return default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            switch (Str(r, "type"))
            {
                case "message_update":
                    if (r.TryGetProperty("assistantMessageEvent", out var ev))
                    {
                        string et = Str(ev, "type");
                        if (et == "text_delta" && Str(ev, "delta") is { Length: > 0 } d) return new OmpEvent(OmpKind.Delta, d);
                        if (et == "text_start") return new OmpEvent(OmpKind.StatusClear);
                    }
                    return default;
                case "tool_execution_start":
                    return new OmpEvent(OmpKind.Status, ToolStatus(Str(r, "toolName")));
                case "prompt_result":
                    string status = Str(r, "status");
                    if (status is "completed" or "aborted") return new OmpEvent(OmpKind.TurnEnded);
                    string err = Str(r, "error");
                    return new OmpEvent(OmpKind.TurnEnded, err.Length > 0 ? err : status.Length > 0 ? status : "error", false);
                case "auto_compaction_start":
                    return new OmpEvent(OmpKind.CompactStart, Str(r, "action"));
                case "auto_compaction_end":
                    {
                        bool aborted = Flag(r, "aborted"), skipped = Flag(r, "skipped");
                        bool has = r.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object && res.EnumerateObject().Any();
                        long before = has ? Num(res, "tokensBefore") ?? -1 : -1;
                        string state = aborted ? "aborted" : skipped ? "skipped" : has ? "done" : "failed";
                        return new OmpEvent(OmpKind.CompactEnd, Str(r, "action"), state == "done", null, before, state);
                    }
                case "command_output":
                    return Str(r, "text") is { Length: > 0 } said ? new OmpEvent(OmpKind.CommandOutput, said) : default;
                case "response":
                    string command = Str(r, "command");
                    bool success = Flag(r, "success");
                    r.TryGetProperty("data", out var data);
                    if (command == "get_state" && success)
                    {
                        OmpUsage? usage = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("contextUsage", out var cu) && cu.ValueKind == JsonValueKind.Object && Num(cu, "contextWindow") is { } win && win > 0
                            ? new OmpUsage(Num(cu, "tokens"), win)
                            : null;
                        if (Str(data, "sessionId") is { Length: > 0 } sid) return new OmpEvent(OmpKind.SessionId, sid, true, usage);
                        return new OmpEvent(OmpKind.Usage, "", true, usage);
                    }
                    if (command == "compact")
                        return success
                            ? new OmpEvent(OmpKind.Compacted, "", true, null, data.ValueKind == JsonValueKind.Object ? Num(data, "tokensBefore") ?? -1 : -1)
                            : new OmpEvent(OmpKind.CompactFailed, Str(r, "error") is { Length: > 0 } ce ? ce : "compact failed", false);
                    if (command == "prompt" && !success) return new OmpEvent(OmpKind.TurnEnded, Str(r, "error") is { Length: > 0 } e ? e : "prompt failed", false);
                    // a builtin slash command that finished here: no prompt_result and no agent_end will follow
                    if (command == "prompt" && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("agentInvoked", out var ai) && ai.ValueKind == JsonValueKind.False)
                        return new OmpEvent(OmpKind.LocalDone);
                    return default;
                default:
                    return default;
            }
        }
        catch (JsonException)
        {
            return default; // a log line
        }
    }

    private static bool Flag(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

    private static long? Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? (long)Math.Round(d) : null;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
