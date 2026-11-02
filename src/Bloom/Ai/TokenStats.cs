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

    /// A home drawn this soon after a pass ended starts none (once the first count is over).
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);

    private static Func<TokenPeriod, TokenView>? madeUp;
    private static Dispatcher? ui;
    private static IReadOnlyList<TokenBucket> buckets = [];
    private static int version; // the sums or the prices changed: the views made before are old
    private static readonly Dictionary<TokenPeriod, (int Version, long Hour, TokenView View)> views = new();
    private static bool running, again, forget, complete, shown;
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
    public static void Start(Dispatcher ui) => TokenStats.ui = ui;

    /// The home draws a token block: a pass, unless one ended in the last 2 minutes and the first count is complete.
    /// Nothing before Start, after SetForQa, or while no block wants the numbers.
    public static void Look()
    {
        if (ui is null || madeUp is not null || running || !Wanted) return;
        if (complete && ended is { } at && DateTime.UtcNow - at < Fresh) return;
        Run();
    }

    /// ↻: a pass now; asked while one runs, one more follows it.
    public static void Refresh()
    {
        if (ui is null || madeUp is not null) return;
        if (running) again = true;
        else Run();
    }

    /// The numbers for a period from what is in memory: no disk, no waiting. The same object until the numbers or the hour change.
    public static TokenView Query(TokenPeriod period)
    {
        if (madeUp is not null) return madeUp(period);
        var now = DateTime.UtcNow;
        long hour = now.Ticks / TimeSpan.TicksPerHour;
        if (views.TryGetValue(period, out var made) && made.Version == version && made.Hour == hour) return made.View;
        var view = TokenQuery.Make(buckets, TokenPrices.Of, period, now, TimeZoneInfo.Local);
        views[period] = (version, hour, view);
        return view;
    }

    /// 설정's "토큰 집계 지우기": stops a pass, deletes the index, forgets the numbers, counts again from the logs left (if wanted).
    public static void Reset()
    {
        if (ui is null || madeUp is not null) return;
        Interlocked.Increment(ref generation);
        forget = true;
        Use([]);
        (Indexing, Counted, ended, complete, percent) = (null, null, null, false, -1);
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

    /// A block shows the numbers: on and not folded.
    private static bool Wanted => Blocks.Any(k => Home.Of(k) is { On: true, Collapsed: false });

    /// What one pass is to do, fixed on the UI thread: the accounts' log homes, the generation it counts for, whether
    /// the index goes first (Reset), whether any block wants the numbers.
    private sealed record Job(List<(string Kind, string Home)> Homes, int Generation, bool Forget, bool Wanted);

    private static void Run()
    {
        running = true;
        again = true;
        new Thread(Passes) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "token count" }.Start();
    }

    /// The counting thread: passes while the UI thread hands out jobs (one, and one more for each ask during it).
    private static void Passes()
    {
        TokenScan.Background(true);
        try
        {
            while (OnUi(Next) is { } job)
                try
                {
                    Pass(job);
                }
                catch (Exception e)
                {
                    Log.Error("token count", e); // this pass ends; one asked for meanwhile still follows
                }
        }
        finally
        {
            TokenScan.Background(false);
        }
    }

    /// On the UI thread: the next job, or null (and no pass runs any more).
    private static Job? Next()
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

    private static void Pass(Job job)
    {
        string path = IndexPath;
        if (job.Forget) Delete(path);
        if (!job.Wanted) return;
        bool prices = TokenPrices.Reload();
        var index = job.Forget ? new TokenIndex() : TokenIndex.Load(path);
        index.Now = DateTime.UtcNow;
        bool first = !index.Complete;
        if (!shown)
        {
            shown = true; // the sums kept, shown before anything is read
            var kept = index.Snapshot();
            Log.Write($"token count: started, {kept.Length} sums and {index.Files.Count} files known{(first ? ", first count" : "")}");
            Publish(job, kept, first ? 0 : null, null, changed: true, done: false);
        }
        var roots = TokenScan.Roots(job.Homes);
        double share = 0;
        var scan = new TokenScan(index, roots)
        {
            Wanted = () => Current(job) && OnUi(() => Wanted),
            Progress = p =>
            {
                share = p;
                Publish(job, index.Snapshot(), p, null, changed: true, done: false);
            },
            Checkpoint = () =>
            {
                if (Current(job)) index.Save(path);
            },
        };
        scan.Run();
        if (!Current(job)) return;
        if (scan.Dirty) index.Save(path);
        if (first) Log.Write($"token count: {scan.FilesRead} files, {scan.BytesRead >> 20} MB read, {(index.Complete ? "the first count is done" : "stopped (no block wants it)")}");
        Publish(job, index.Snapshot(), index.Complete ? null : share, scan.NoLogs, changed: scan.Changed || prices || first, done: true, index.Complete);
        if (first && index.Complete) MemoryTrim.Soon(aggressive: true); // the reading's buffers and the keys' tables go back
    }

    /// No Reset came since the job was handed out.
    private static bool Current(Job job) => Volatile.Read(ref generation) == job.Generation;

    /// Hands the sums and the progress to the UI thread; `done`: the pass ended (`noLogs` and `whole`, the first count over, as it found).
    /// Updated fires when the numbers changed at the end, or when the first count's percent moved.
    private static void Publish(Job job, TokenBucket[] sums, double? indexing, bool? noLogs, bool changed, bool done, bool whole = false)
    {
        ui?.BeginInvoke(() =>
        {
            if (job.Generation != generation || madeUp is not null) return;
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
                ended = DateTime.UtcNow;
                Counted = DateTime.Now;
                complete = whole;
                tell |= changed || noLogs != NoLogs;
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
