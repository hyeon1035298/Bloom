using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bloom.Core;

namespace Bloom.Ai;

/// A sub-session T3 runs for a session (T3's delegate_task: a thread of its own, on Claude, Codex, Gemini...), as the
/// Run Card's `kin` names it (App.RunCardProtocol.KinLine): its thread, its own session id ("" when T3 has none yet),
/// title, company (claude | codex | gemini | other), model id, state (run | ask | done | failed | stopped), and when it
/// started and ended (Unix ms).
internal sealed record T3Kid(string Thread, string Session, string Title, string Company, string Model, string State, long Start, long? End);

/// A session's place among T3's threads: the session of the thread that started its thread (null: none), and the
/// sub-sessions its thread started that are still at work.
internal sealed record T3Family(string? Parent, IReadOnlyList<T3Kid> Kids)
{
    public static readonly T3Family None = new(null, []);
}

internal static partial class T3Threads
{
    /// The most sub-sessions told (the newest); the Run Card takes no more.
    public const int MostKids = 24;

    /// A sub-session T3 still calls running with nothing new in its thread for this long has ended (T3 can leave a
    /// row running after its turn is over); one waiting on a question is still asking.
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    private static bool kinFailed;

    /// A session's thread and the thread that started it as a sub-session (null: none), found once: the session's
    /// lookup is the one unindexed read, and neither ever changes for a session.
    private static readonly ConcurrentDictionary<string, (string Thread, string? Parent)> threadOf = new(StringComparer.Ordinal);

    private const int MostRemembered = 512;

    /// `session`'s parent and sub-sessions at work, read from T3's state (read only, one open): None when T3 Code isn't
    /// on this computer or has no thread with that session. Only the subs T3 itself runs for the thread are read
    /// (app_owned); Claude Code's own subagents are in the session's transcript, which the Run Card reads itself.
    public static T3Family Kin(string session)
    {
        if (!SessionId().IsMatch(session)) return T3Family.None; // it goes into the query as it is
        string db = Path.Combine(UserData, "statev2.sqlite");
        try
        {
            if (!File.Exists(db)) return T3Family.None;
            var family = T3Family.None;
            Native.SystemSqlite.ReadOnly(db, h => family = ReadKin(h, session, DateTimeOffset.UtcNow));
            return family;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        {
            // asked every few seconds while a card shows: another T3 version's tables are logged once
            if (!kinFailed) Log.Error("t3 kin", e);
            kinFailed = true;
            return T3Family.None;
        }
    }

    private static T3Family ReadKin(IntPtr h, string session, DateTimeOffset now)
    {
        if (!threadOf.TryGetValue(session, out var known))
        {
            string? found = null, payload = null;
            Native.SystemSqlite.Query(h,
                "select t.thread_id, t.payload_json from orchestration_v2_projection_provider_threads p " +
                "join orchestration_v2_projection_threads t on t.thread_id = p.thread_id " +
                $"where p.payload_json like '%\"nativeId\":\"{session}\"%' and t.deleted_at is null order by p.updated_at desc limit 1",
                row =>
                {
                    found = Native.SystemSqlite.Text(row, 0);
                    payload = Native.SystemSqlite.Text(row, 1);
                });
            if (found is not { Length: > 0 }) return T3Family.None; // not kept: T3 may start it later
            if (threadOf.Count >= MostRemembered) threadOf.Clear();
            known = threadOf[session] = (found, ParentThread(payload));
        }
        string thread = known.Thread;

        string? parent = null;
        if (known.Parent is { } up)
            Native.SystemSqlite.Query(h,
                $"select payload_json from orchestration_v2_projection_provider_threads where thread_id = {Literal(up)} order by updated_at desc limit 1",
                row => parent = NativeId(Native.SystemSqlite.Text(row, 0)));

        var kids = new List<T3Kid>();
        Native.SystemSqlite.Query(h,
            "select s.child_thread_id, s.driver, s.status, s.started_at, s.completed_at, s.updated_at, t.title, t.payload_json, t.updated_at, " +
            "(select r.status from orchestration_v2_projection_runs r where r.thread_id = s.child_thread_id order by r.ordinal desc limit 1), " +
            "(select r.requested_at from orchestration_v2_projection_runs r where r.thread_id = s.child_thread_id order by r.ordinal desc limit 1), " +
            "(select count(*) from orchestration_v2_projection_runtime_requests q where q.thread_id = s.child_thread_id and q.status = 'pending'), " +
            "(select p.payload_json from orchestration_v2_projection_provider_threads p where p.thread_id = s.child_thread_id order by p.updated_at desc limit 1) " +
            "from orchestration_v2_projection_subagents s join orchestration_v2_projection_threads t on t.thread_id = s.child_thread_id " +
            $"where s.thread_id = {Literal(thread)} and s.origin = 'app_owned' and t.deleted_at is null order by s.started_at",
            row =>
            {
                string? child = Native.SystemSqlite.Text(row, 0);
                if (child is not { Length: > 0 }) return;
                DateTimeOffset? seen = Latest(Native.SystemSqlite.Text(row, 5), Native.SystemSqlite.Text(row, 8), Native.SystemSqlite.Text(row, 10));
                string state = StateOf(Native.SystemSqlite.Text(row, 2), Native.SystemSqlite.Text(row, 9), (int)Native.SystemSqlite.Int64(row, 11), seen, now);
                if (state is not (KidRun or KidAsk)) return; // only the subs at work are told
                kids.Add(new T3Kid(child, NativeId(Native.SystemSqlite.Text(row, 12)) ?? "", Native.SystemSqlite.Text(row, 6) ?? "",
                    CompanyOf(Native.SystemSqlite.Text(row, 1)), ModelOf(Native.SystemSqlite.Text(row, 7)) ?? "", state,
                    UnixMs(Native.SystemSqlite.Text(row, 3)) ?? 0, UnixMs(Native.SystemSqlite.Text(row, 4))));
            });
        return new T3Family(parent == session ? null : parent, kids.Count > MostKids ? kids[^MostKids..] : kids);
    }

    // the `state` words of the Run Card's kin line
    internal const string KidRun = "run", KidAsk = "ask", KidDone = "done", KidFailed = "failed", KidStopped = "stopped";

    /// A sub-session's state from T3's words for it: its latest run's status when it has one (the sub-agent row's
    /// can stay running after the run ended), else the row's own; a question it waits on makes one at work asking,
    /// and one at work with nothing new since `seen` for longer than Quiet has ended.
    internal static string StateOf(string? subagent, string? run, int pending, DateTimeOffset? seen, DateTimeOffset now)
    {
        string state = Word(run) ?? Word(subagent) ?? KidRun;
        if (state != KidRun) return state;
        if (pending > 0) return KidAsk;
        return seen is { } at && now - at > Quiet ? KidDone : KidRun;

        static string? Word(string? status) => status switch
        {
            null or "" => null,
            "completed" or "idle" => KidDone,
            "failed" => KidFailed,
            "interrupted" or "cancelled" or "canceled" or "rolled_back" => KidStopped,
            _ => KidRun, // running, and any word for one under way
        };
    }

    /// The company a T3 driver runs: claude, codex (local models run through Codex too), gemini (T3's antigravity
    /// driver), or other.
    internal static string CompanyOf(string? driver)
    {
        string d = driver?.ToLowerInvariant() ?? "";
        return d.Contains("claude") ? "claude" : d.Contains("codex") ? "codex" : d.Contains("gemini") || Antigravity(d) ? "gemini" : "other";
    }

    /// A provider thread's payload → the session (or Codex thread) it runs: `nativeThreadRef.nativeId`.
    internal static string? NativeId(string? payload) => Dig(payload, "nativeThreadRef", "nativeId");

    /// A thread's payload → the thread that started it as a sub-session: `lineage.parentThreadId` when
    /// `lineage.relationshipToParent` is "subagent" (a fork or a side chat names its source thread too: not a parent).
    internal static string? ParentThread(string? payload) =>
        Dig(payload, "lineage", "relationshipToParent") == "subagent" ? Dig(payload, "lineage", "parentThreadId") : null;

    /// A thread's payload → the model it runs on: `modelSelection.model`.
    internal static string? ModelOf(string? payload) => Dig(payload, "modelSelection", "model");

    private static string? Dig(string? json, string outer, string inner)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(outer, out var o) && o.ValueKind == JsonValueKind.Object &&
                   o.TryGetProperty(inner, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// T3's ISO time ("2026-11-23T19:42:35.555Z") → Unix ms; null when it is none.
    internal static long? UnixMs(string? iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t.ToUnixTimeMilliseconds() : null;

    private static DateTimeOffset? Latest(params string?[] times)
    {
        var all = times.Select(UnixMs).OfType<long>().ToArray();
        return all.Length > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(all.Max()) : null;
    }

    /// A value T3's own rows gave, as an SQL string (SystemSqlite binds nothing).
    private static string Literal(string s) => "'" + s.Replace("'", "''") + "'";
}
