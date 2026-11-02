using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// The token blocks' numbers, counted from the Claude Code and Codex logs of every account (Core\AiAccounts) into an
/// index of hourly sums (%LOCALAPPDATA%\Bloom\tokens); no message text is kept. Passes run on a background thread of
/// low priority, only while a block wants them.
///
/// A pass (TokenScan) loads the index, reads what the logs added since, saves and lets go of it: only the sums stay in
/// memory (`buckets`), handed to the UI thread, where everything else here lives. Query sums them for a period without
/// touching the disk. The first count shows how far it got and saves as it goes, so a Bloom closed meanwhile goes on
/// from there.
internal static class TokenStats
{
    /// The home blocks that show these numbers (Core\Home.Kinds); a pass runs only while one is on and not folded.
    public static readonly string[] Blocks = ["tokens", "tokenchart", "tokenmodels"];

    /// A home drawn this soon after a pass ended starts none, unless that pass stopped only because no block wanted
    /// the numbers: it goes on at once. A pass that broke off waits too (no pass after pass on the same fault); a file
    /// that can't be read, or a line too long to keep, does not keep the first count from being over (TokenScan).
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);

    private static Func<TokenPeriod, TokenView>? madeUp;
    private static Dispatcher? ui;
    private static Action<Action>? post; // hands work to the UI thread without waiting (Start; Attach in the tests)
    private static Func<bool> blocks = BlocksWant; // a block wants the numbers
    private static Action spawn = Spawn; // starts the counting thread
    private static IReadOnlyList<TokenBucket> buckets = [];
    private static int version; // the sums or the prices changed: the views made before are old
    private static readonly Dictionary<TokenPeriod, (int Version, long Hour, long LocalHour, TokenView View)> views = new();
    private static bool running, again, forget, resume, shown; // resume: the last pass stopped as no block wanted it
    private static int generation; // Reset: whatever an older pass still does is dropped
    private static DateTime? ended; // UTC
    private static int percent = -1;

    /// On the UI thread: a pass ended having changed the numbers; the first count moved on (at most every 2 s); Reset; SetForQa.
    public static event Action? Updated;

    /// 0…1 while files never read before are being read (the first count, or after Reset): bytes read / bytes to read; null otherwise.
    public static double? Indexing { get; private set; }

    /// When the last pass ended, local time; null before the first.
    public static DateTime? Counted { get; private set; }

    /// The last pass found no Claude Code or Codex log folder.
    public static bool NoLogs { get; private set; }

    private static string IndexPath => Paths.InLocal(Path.Combine("tokens", "index.bin"));

    /// Host, once, outside QA and the recording mode: the UI dispatcher. Starts no pass; Look does.
    public static void Start(Dispatcher ui)
    {
        TokenStats.ui = ui;
        post = a => ui.BeginInvoke(a);
    }

    /// The home draws a token block: a pass, unless one ended in the last 2 minutes (see Fresh). Nothing before Start,
    /// after SetForQa, while a pass runs, or while no block wants the numbers.
    public static void Look()
    {
        if (post is null || madeUp is not null || running || !Wanted) return;
        if (!resume && ended is { } at && DateTime.UtcNow - at < Fresh) return;
        Run();
    }

    /// ↻: a pass now; asked while one runs, one more follows it.
    public static void Refresh()
    {
        if (post is null || madeUp is not null) return;
        if (running) again = true;
        else Run();
    }

    /// The numbers for a period from what is in memory: no disk, no waiting. The same object until the numbers, the
    /// UTC hour or the local hour change (a zone half an hour off UTC starts its days on the half hour).
    public static TokenView Query(TokenPeriod period)
    {
        if (madeUp is not null) return madeUp(period);
        var now = DateTime.UtcNow;
        var zone = TimeZoneInfo.Local;
        long hour = now.Ticks / TimeSpan.TicksPerHour, local = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Ticks / TimeSpan.TicksPerHour;
        if (views.TryGetValue(period, out var made) && made.Version == version && made.Hour == hour && made.LocalHour == local) return made.View;
        var view = TokenQuery.Make(buckets, TokenPrices.Of, period, now, zone);
        views[period] = (version, hour, local, view);
        return view;
    }

    /// 설정's "토큰 집계 지우기": stops a pass, deletes the index, forgets the numbers, counts again from the logs left (if wanted).
    public static void Reset()
    {
        if (post is null || madeUp is not null) return;
        Interlocked.Increment(ref generation);
        forget = true;
        Use([]);
        (Indexing, Counted, ended, resume, percent) = (null, null, null, false, -1);
        Updated?.Invoke();
        if (running) again = true;
        else Run();
    }

    /// QA pictures and the recording mode: made-up views; nothing is read or counted from then on.
    public static void SetForQa(Func<TokenPeriod, TokenView> views, double? indexing = null, bool noLogs = false)
    {
        madeUp = views;
        Indexing = indexing;
        NoLogs = noLogs;
        Counted = DateTime.Now;
        Updated?.Invoke();
    }

    /// The sums the views are made of (the UI thread; also the tests).
    internal static void Use(IReadOnlyList<TokenBucket> sums)
    {
        buckets = sums;
        version++;
        views.Clear();
    }

    /// The tests, in place of Start: `post` hands work to their UI thread, `wanted` stands for the blocks and `start`
    /// for the counting thread (the test calls Next and Publish itself); everything else as at launch. A null `post`
    /// leaves it as before Start.
    internal static void Attach(Action<Action>? post, Func<bool>? wanted = null, Action? start = null)
    {
        Interlocked.Increment(ref generation);
        (ui, TokenStats.post, blocks, spawn, madeUp) = (null, post, wanted ?? BlocksWant, start ?? Spawn, null);
        running = again = forget = resume = shown = false;
        (Indexing, Counted, ended, percent, NoLogs) = (null, null, null, -1, false);
        Use([]);
    }

    /// A block shows the numbers.
    private static bool Wanted => blocks();

    /// A token block is on and not folded.
    private static bool BlocksWant() => Blocks.Any(k => Home.Of(k) is { On: true, Collapsed: false });

    /// What one pass is to do, fixed on the UI thread: the accounts' log homes, the generation it counts for, whether
    /// the index goes first (Reset), whether any block wants the numbers.
    internal sealed record Job(List<(string Kind, string Home)> Homes, int Generation, bool Forget, bool Wanted);

    private static void Run()
    {
        running = true;
        again = true;
        spawn();
    }

    private static void Spawn() => new Thread(Passes) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "token count" }.Start();

    /// The counting thread: passes while the UI thread hands out jobs (one, and one more for each ask during it).
    private static void Passes()
    {
        TokenScan.Background(true);
        try
        {
            while (OnUi(Next) is { } job)
                if (Pass(job)) MemoryTrim.Soon(aggressive: true); // the first count is over: its buffers and the keys' tables, unreachable now, go back
        }
        finally
        {
            TokenScan.Background(false);
        }
    }

    /// On the UI thread: the next job, or null (and no pass runs any more).
    internal static Job? Next()
    {
        bool wanted = madeUp is null && Wanted;
        if (!again || madeUp is not null || (!wanted && !forget))
        {
            running = again = false;
            return null;
        }
        var job = new Job(Homes(), generation, forget, wanted);
        again = forget = false;
        return job;
    }

    /// Each account's folder: its own, or the CLI's default (~\.claude, ~\.codex), hidden ones too (Hidden only takes
    /// the usage card off the home).
    private static List<(string Kind, string Home)> Homes() =>
        AiAccounts.All.Select(a => (a.Kind, a.Home ?? (a.Kind == "chatgpt" ? CodexAuth.DefaultHome() : ClaudeWatch.Home))).ToList();

    /// One pass → whether it ended the first count. Whatever breaks it off is logged and still sends the end notice (the
    /// blocks no longer show a count going on); one asked for meanwhile still follows.
    private static bool Pass(Job job)
    {
        try
        {
            string path = IndexPath;
            if (job.Forget) Delete(path);
            if (!job.Wanted) return false;
            bool prices = TokenPrices.Reload();
            var index = job.Forget ? new TokenIndex() : TokenIndex.Load(path);
            index.Now = DateTime.UtcNow;
            bool first = !index.Complete;
            double? left = first ? 0 : null; // the first count's share read
            if (!shown)
            {
                shown = true; // the sums kept, shown before anything is read
                var kept = index.Snapshot();
                Log.Write($"token count: started, {kept.Length} sums and {index.Files.Count} files known{(first ? ", first count" : "")}");
                Publish(job, kept, left, null, changed: true, done: false);
            }
            var roots = TokenScan.Roots(job.Homes);
            var scan = new TokenScan(index, roots)
            {
                Wanted = () => Current(job) && OnUi(() => madeUp is null && Wanted),
                Progress = p =>
                {
                    left = p;
                    Publish(job, index.Snapshot(), p, null, changed: true, done: false);
                },
                Checkpoint = () =>
                {
                    if (Current(job)) Save(index, path);
                },
            };
            scan.Run();
            if (!Current(job)) return false;
            if (scan.Dirty) Save(index, path);
            if (first) Log.Write($"token count: {scan.FilesRead} files, {scan.BytesRead >> 20} MB read, {(index.Complete ? "the first count is done" : "stopped (no block wants it)")}");
            Publish(job, index.Snapshot(), index.Complete ? null : left, scan.NoLogs, changed: scan.Changed || prices || first, done: true, stopped: scan.Stopped);
            return first && index.Complete;
        }
        catch (Exception e)
        {
            Log.Error("token count", e);
            Publish(job, [], null, null, changed: false, done: true, failed: true);
            return false;
        }
    }

    /// Saves the index; a save that fails (the file held by a scanner, say) is logged and the count goes on: the next
    /// save writes everything.
    private static void Save(TokenIndex index, string path)
    {
        try
        {
            index.Save(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("token count: index not saved", e);
        }
    }

    /// No Reset came since the job was handed out.
    private static bool Current(Job job) => Volatile.Read(ref generation) == job.Generation;

    /// Hands the sums and the progress to the UI thread, where one of an older generation (a Reset since) or one after
    /// SetForQa is dropped; `done`: the pass ended (`noLogs` as it found; `stopped`: before every file was read, as no
    /// block wanted the numbers; `failed`: it broke off, so the numbers and the note stay as they were). Updated fires
    /// when the numbers changed at the end, at the first end since Bloom started or since Reset, or when the first
    /// count's percent moved.
    internal static void Publish(Job job, TokenBucket[] sums, double? indexing, bool? noLogs, bool changed, bool done, bool stopped = false, bool failed = false)
    {
        post?.Invoke(() =>
        {
            if (job.Generation != generation || madeUp is not null) return;
            if (failed)
            {
                ended = DateTime.UtcNow; // Look waits out Fresh before the next try
                resume = false;
                return;
            }
            bool tell = false;
            if (changed) Use(sums);
            int p = indexing is { } i ? (int)(i * 100) : -1;
            if (p != percent)
            {
                percent = p;
                tell = true;
            }
            Indexing = indexing;
            if (done)
            {
                bool firstEnd = Counted is null; // the blocks said "counting" until now
                ended = DateTime.UtcNow;
                Counted = DateTime.Now;
                resume = stopped;
                tell |= changed || firstEnd || noLogs != NoLogs;
                NoLogs = noLogs ?? NoLogs;
            }
            else tell |= changed && p < 0;
            if (tell) Updated?.Invoke();
        });
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("token count: index not deleted", e);
        }
    }

    /// Runs `f` on the UI thread and waits for it; default when Bloom is closing.
    private static T? OnUi<T>(Func<T> f)
    {
        try
        {
            return ui is null ? default : ui.Invoke(f);
        }
        catch (Exception e) when (e is OperationCanceledException or InvalidOperationException)
        {
            return default;
        }
    }
}
