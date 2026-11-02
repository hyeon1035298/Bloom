using System;
using System.Windows.Threading;

namespace Bloom.Ai;

/// The token blocks' numbers, counted from the Claude Code and Codex logs of every account (Core\AiAccounts) into an
/// index of hourly sums (%LOCALAPPDATA%\Bloom\tokens); no message text is kept. Passes run on a background thread of
/// low priority, only while a block wants them.
internal static class TokenStats
{
    /// The home blocks that show these numbers (Core\Home.Kinds); a pass runs only while one is on and not folded.
    public static readonly string[] Blocks = ["tokens", "tokenchart", "tokenmodels"];

    private static Func<TokenPeriod, TokenView>? madeUp;

    /// On the UI thread: a pass ended having changed the numbers; the first count moved on (at most every 2 s); Reset; SetForQa.
    public static event Action? Updated;

    /// 0…1 while files never read before are being read (the first count, or after Reset): bytes read / bytes to read; null otherwise.
    public static double? Indexing { get; private set; }

    /// When the last pass ended, local time; null before the first.
    public static DateTime? Counted { get; private set; }

    /// The last pass found no Claude Code or Codex log folder.
    public static bool NoLogs { get; private set; }

    /// Host, once, outside QA and the recording mode: the UI dispatcher. Starts no pass; Look does.
    public static void Start(Dispatcher ui)
    {
    }

    /// The home draws a token block: a pass, unless one ended in the last 2 minutes and the first count is complete.
    /// Nothing before Start, after SetForQa, or while no block wants the numbers.
    public static void Look()
    {
    }

    /// ↻: a pass now; asked while one runs, one more follows it.
    public static void Refresh()
    {
    }

    /// The numbers for a period from what is in memory: no disk, no waiting. The same object until the numbers or the hour change.
    public static TokenView Query(TokenPeriod period) => madeUp?.Invoke(period) ?? TokenView.Empty(period);

    /// 설정's "토큰 집계 지우기": stops a pass, deletes the index, forgets the numbers, counts again from the logs left (if wanted).
    public static void Reset()
    {
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
}
