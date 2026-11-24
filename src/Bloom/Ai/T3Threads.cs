using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Ai;

/// T3 Code, a desktop app that runs Claude Code and Codex for its threads: the thread a Claude Code session or Codex
/// thread runs in, read from T3's own state (userdata\statev2.sqlite, read only: each provider thread's payload names
/// its `nativeId`, the session's id), and the link that brings T3 forward at that thread
/// (t3code://threads/<environment>/<thread>, the scheme T3 registers). Read on a click, never on a poll.
internal static partial class T3Threads
{
    private static string UserData => Path.Combine(Paths.Home, ".t3", "userdata");

    [GeneratedRegex(@"^[0-9A-Za-z-]{8,64}$")]
    private static partial Regex SessionId();

    /// The link to the T3 thread `session` runs in; null when T3 Code isn't on this computer, or no thread it keeps (and
    /// hasn't deleted) has that session.
    public static string? Link(string session) => Find(session) is var (environment, thread) ? LinkTo(environment, thread) : null;

    /// The id of the T3 thread `session` runs in (what T3's own tools take); null as for Link.
    public static string? Thread(string session) => Find(session)?.Thread;

    /// T3's environment and the thread `session` runs in.
    private static (string Environment, string Thread)? Find(string session)
    {
        if (!SessionId().IsMatch(session)) return null; // it goes into the query as it is
        string db = Path.Combine(UserData, "statev2.sqlite"), env = Path.Combine(UserData, "environment-id");
        try
        {
            if (!File.Exists(db) || !File.Exists(env)) return null;
            string environment = File.ReadAllText(env).Trim();
            string? thread = null;
            Native.SystemSqlite.ReadOnly(db, h => Native.SystemSqlite.Query(h,
                "select p.thread_id from orchestration_v2_projection_provider_threads p " +
                "join orchestration_v2_projection_threads t on t.thread_id = p.thread_id " +
                $"where p.payload_json like '%\"nativeId\":\"{session}\"%' and t.deleted_at is null order by p.updated_at desc limit 1",
                row => thread = Native.SystemSqlite.Text(row, 0)));
            return thread is { Length: > 0 } && environment.Length > 0 ? (environment, thread) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error("t3 thread", e); // another T3 version's tables, or no SQLite: the caller raises the window instead
            return null;
        }
    }

    internal static string LinkTo(string environment, string thread) => $"t3code://threads/{Uri.EscapeDataString(environment)}/{Uri.EscapeDataString(thread)}";

    /// T3 brought forward at the thread `session` runs in; false when there is none (the caller raises the session's
    /// window instead).
    public static bool Open(string session) => Link(session) is { } link && Go(link);

    /// T3 brought forward at its thread `thread` (an id T3's tools gave); false when T3 Code isn't on this computer.
    public static bool OpenThread(string thread)
    {
        try
        {
            string env = Path.Combine(UserData, "environment-id");
            if (!File.Exists(env) || File.ReadAllText(env).Trim() is not { Length: > 0 } environment) return false;
            return Go(LinkTo(environment, thread));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("t3 thread", e);
            return false;
        }
    }

    private static bool Go(string link)
    {
        try
        {
            Native.Win32.AllowSetForegroundWindow(-1); // T3's running copy gets the link and comes forward
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("t3 thread", e); // the scheme isn't registered
            return false;
        }
    }
}
