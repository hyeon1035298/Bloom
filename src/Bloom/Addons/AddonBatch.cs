using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bloom.Addons;

/// Several add-ons switched one after another, with what became of each.
internal static class AddonBatch
{
    internal sealed record Outcome(Addon Addon, bool Here, AddonFailure? Failed, bool StartFailed)
    {
        /// Here and started. A failed look for a newer release (offline) leaves the version on the PC to start, so
        /// `Failed` alone does not make it a failure: its settled status says whether it works.
        public bool Ok => Here && !StartFailed;
    }

    /// Tests replace these; the app's are AddonHub.Set and a => a.Status().
    internal static Func<Addon, bool, Task> Switch = AddonHub.Set;
    internal static Func<Addon, AddonStatus> StatusOf = a => a.Status();

    /// The taskbar add-on first, then the catalog's order.
    public static IReadOnlyList<Addon> Ordered(IEnumerable<Addon> addons)
    {
        var all = AddonHub.All;
        int Rank(Addon a) => a.Id == AddonProtocol.Id ? -1 : IndexOf(all, a) is var i and >= 0 ? i : int.MaxValue;
        return [.. addons.OrderBy(Rank)];
    }

    private static int IndexOf(IReadOnlyList<Addon> all, Addon a)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i].Id == a.Id) return i;
        return -1;
    }

    /// Call on the UI thread: every Switch call and `afterEach` run on it (AddonHub.Set changes the settings before
    /// it goes to a worker thread).
    public static async Task<IReadOnlyList<Outcome>> TurnOn(IReadOnlyList<Addon> addons, Func<Outcome, Task>? afterEach = null)
    {
        var done = new List<Outcome>();
        foreach (var a in Ordered(addons))
        {
            await Switch(a, true); // no ConfigureAwait(false): the next Switch must run on the caller's thread
            var o = new Outcome(a, a.Manifest is not null, a.Fetch.Failed, a.StartFailed);
            done.Add(o);
            if (afterEach is not null) await afterEach(o);
        }
        return done;
    }

    /// Call on the UI thread, as TurnOn.
    public static async Task TurnOff(IReadOnlyList<Addon> addons)
    {
        foreach (var a in Ordered(addons))
            await Switch(a, false); // on the caller's thread, as TurnOn
    }

    /// Each add-on's status once none of them is still starting, or when `limit` has passed.
    public static async Task<IReadOnlyList<(Addon Addon, AddonStatus Status)>> Settled(IReadOnlyList<Addon> addons, TimeSpan limit)
    {
        List<(Addon Addon, AddonStatus Status)> Now() => [.. addons.Select(a => (a, StatusOf(a)))];
        var now = Now();
        if (addons.Count == 0) return now;
        var until = DateTime.UtcNow + limit;
        // AddonHub.Changed comes on a worker thread: it only ends the current wait, the status is read back here
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged() => changed.TrySetResult();
        AddonHub.Changed += OnChanged;
        try
        {
            while (now.Any(s => s.Status.State == AddonState.Starting))
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) break;
                var wait = left < TimeSpan.FromSeconds(2) ? left : TimeSpan.FromSeconds(2);
                await Task.WhenAny(changed.Task, Task.Delay(wait));
                if (changed.Task.IsCompleted) changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                now = Now();
            }
        }
        finally
        {
            AddonHub.Changed -= OnChanged;
        }
        return now;
    }
}
