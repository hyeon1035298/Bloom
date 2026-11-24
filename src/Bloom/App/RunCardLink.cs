using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.App;

/// Bloom's end of the Run Card add-on (explorer.exe), which shows a live card per running Claude Code session: Bloom
/// is the pipe server and the add-on connects, one at a time — a new connection (explorer started again) replaces the
/// old. While its hello stands, Bloom leaves Claude Code's turn cards to it (Notices), and with 카드에서 승인·질문 on
/// sends it the approval requests first (ClaudeApprovals). The lines: RunCardProtocol.
/// The name is Paths.Pipe's, so the installed Bloom's is `Bloom.run-card.<session>.<SID>` and a dev copy (--data) has
/// its own: it never takes the add-on from the installed Bloom.
internal static class RunCardLink
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, RunCardAsk> asks = new(); // the requests sent and not settled (gate)
    private static readonly CancellationTokenSource stop = new();
    private static RunCardPeer? peer; // the add-on connected now (gate)
    private static Dispatcher? ui;

    public static string Pipe => Paths.Pipe(RunCardProtocol.PipeKind);

    public static bool Connected => peer is not null;

    /// The add-on shows Claude Code's turns: Bloom's own 진행 중 · 작업 완료 · 승인 대기 cards for them stay away.
    public static bool ShowsTurns => peer is { Hello: true };

    /// The add-on takes approval requests on its cards.
    public static bool Approvals => peer is { Hello: true, Approvals: true };

    /// Host, once (not in recording mode).
    public static void Start(Dispatcher dispatcher)
    {
        if (ui is not null) return;
        ui = dispatcher;
        _ = Task.Run(() => AddonPipe.Serve(Pipe, "run card pipe", Take, stop.Token));
    }

    /// Bloom quits: the add-on sees the pipe close now, not when the process is gone.
    public static void Stop()
    {
        stop.Cancel();
        RunCardPeer? now;
        lock (gate) now = peer;
        if (now is not null) Drop(now);
    }

    /// A connection that passed AddonPipe's checks: it replaces the one before.
    private static void Take(NamedPipeServerStream pipe)
    {
        var p = new RunCardPeer(pipe);
        RunCardPeer? old;
        lock (gate)
        {
            old = peer;
            peer = p;
        }
        if (old is not null) Drop(old);
        _ = Task.Run(() => Read(p));
    }

    /// The connection now, as a list (T3Lines).
    internal static RunCardPeer[] Peers() => peer is { } p ? [p] : [];

    private static async Task Read(RunCardPeer p)
    {
        try
        {
            p.Send(RunCardProtocol.HelloLine());
            var reader = new Ai.BoundedLineReader(p.Pipe, AddonPipe.MaxLine);
            while (await reader.ReadLineAsync(stop.Token) is { } line)
                if (RunCardProtocol.Parse(line) is { } m) Heard(p, m); // anything else is ignored
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            /* the add-on went, or Bloom quits */
        }
        finally
        {
            Drop(p);
        }
    }

    private static void Heard(RunCardPeer p, RunCardMessage m)
    {
        switch (m.Kind)
        {
            case RunCardProtocol.Hello:
                p.Approvals = m.Approvals;
                p.Hello = true;
                break;
            case RunCardProtocol.Prefs:
                p.Approvals = m.Approvals;
                break;
            case RunCardProtocol.Open:
                ui?.BeginInvoke(() => Notices.Open(m.Session!, m.Pid));
                break;
            case RunCardProtocol.Stop or RunCardProtocol.Can or RunCardProtocol.Say or RunCardProtocol.Question or RunCardProtocol.Kin:
                T3Lines.Heard(p, m, stop.Token); // the same as on the T3 pipe
                break;
            default: // shown, none, answer: about a request sent to this connection
                RunCardAsk? a;
                lock (gate) a = m.Id is { } id && asks.TryGetValue(id, out var found) && found.Peer == p ? found : null;
                if (a is null) break;
                a.Heard(m);
                // answered, or not shown: nothing more comes about it, and nothing goes back
                if (a.Answered.IsCompleted || (a.Shown.IsCompleted && !a.Shown.Result))
                    lock (gate) asks.Remove(a.Id);
                break;
        }
    }

    /// The connection is over: every request it holds settles as "the add-on went" (ClaudeApprovals falls back to
    /// Bloom's own card), and Bloom's turn cards come back.
    private static void Drop(RunCardPeer p)
    {
        List<RunCardAsk> gone;
        lock (gate)
        {
            if (peer == p) peer = null;
            gone = asks.Values.Where(a => a.Peer == p).ToList();
            foreach (var a in gone) asks.Remove(a.Id);
        }
        p.Dispose();
        foreach (var a in gone) a.Dropped();
    }

    /// A request for the add-on's card; null when it takes none now (not connected, 카드에서 승인·질문 off).
    public static RunCardAsk? Ask(ClaudeApprovals.Request r)
    {
        if (peer is not { Hello: true, Approvals: true } p) return null;
        var a = new RunCardAsk(Guid.NewGuid().ToString("N"), p, r.Questions);
        lock (gate) asks[a.Id] = a;
        if (p.Send(RunCardProtocol.AskLine(a.Id, r))) return a;
        lock (gate) asks.Remove(a.Id);
        return null;
    }

    /// Bloom stopped waiting on a request: the add-on's card for it goes (a connection already gone needs nothing).
    public static void Cancel(RunCardAsk a)
    {
        bool held;
        lock (gate) held = asks.Remove(a.Id);
        if (held) a.Peer.Send(RunCardProtocol.CancelLine(a.Id));
    }
}
