using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Bloom.Ai;

/// Codex's logs say nothing of the process behind a thread; this tells it when it can be told.
internal static class CodexProcess
{
    // A log is made when its session starts, so its process began a little before; wider than that, two sessions
    // could be mixed up
    private static readonly TimeSpan StartLead = TimeSpan.FromMinutes(2);

    // a process start and a file's creation time are read from different clocks' granularity: this much later still counts
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

    /// The running Codex process behind the log made at `logCreated` (UTC), or null when it can't be told.
    public static int? Find(DateTime logCreated)
    {
        var running = new List<(int id, DateTime started)>();
        foreach (var p in Process.GetProcessesByName("codex"))
            using (p)
            {
                try
                {
                    running.Add((p.Id, p.StartTime.ToUniversalTime()));
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception)
                {
                    /* ended meanwhile, or not ours to look into */
                }
            }
        return Pick(running, logCreated);
    }

    /// The only one that was already running when the log was made, else the only one that started just before it. A
    /// process that started after the log can't be the one behind it (else a lone Codex is taken for any log).
    internal static int? Pick(IReadOnlyList<(int id, DateTime started)> running, DateTime logCreated)
    {
        var before = running.Where(r => r.started <= logCreated + Slack).ToList();
        if (before.Count == 1) return before[0].id;
        var near = before.Where(r => logCreated - r.started < StartLead).ToList();
        return near.Count == 1 ? near[0].id : null;
    }
}
