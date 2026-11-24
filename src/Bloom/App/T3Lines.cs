using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// The lines about a session an add-on sends Bloom to act on it (RunCardProtocol's can, say, question, stop, kin), the same
/// over the Run Card's pipe (RunCardLink) and the T3 pipe (T3Link): each answered off the pipe's reader, back to the
/// connection that asked. A T3 session goes to its thread (T3Agent); a stop for any other goes to the terminal it is
/// typed in (SessionStop), when the line names its process.
internal static class T3Lines
{
    private static readonly ConcurrentDictionary<(RunCardPeer, string), byte> kinReads = new();

    /// The connection's line `m` handled when it is one of these five; false for any other kind.
    public static bool Heard(RunCardPeer p, RunCardMessage m, CancellationToken ct)
    {
        string session = m.Session!;
        switch (m.Kind)
        {
            case RunCardProtocol.Can:
                p.Asked(session);
                _ = Task.Run(() => Tell(p, session), ct);
                return true;
            case RunCardProtocol.Say:
                _ = Task.Run(async () =>
                {
                    var sent = await T3Agent.Say(session, m.Text ?? "", m.Picks, ct);
                    p.Send(RunCardProtocol.SaidLine(session, sent.Ok, sent.How));
                }, ct);
                return true;
            case RunCardProtocol.Question:
                _ = Task.Run(async () => p.Send(RunCardProtocol.QuestionLine(session, await T3Agent.Question(session, ct))));
                return true;
            case RunCardProtocol.Kin:
                // asked every few seconds while a card shows: nothing of T3 is read while the link is off, and one read
                // a session at a time (the add-on asks again)
                if (!kinReads.TryAdd((p, session), 0)) return true;
                _ = Task.Run(() =>
                {
                    try
                    {
                        p.Send(RunCardProtocol.KinLine(session, Settings.Current.T3Link ? T3Threads.Kin(session) : null));
                    }
                    finally
                    {
                        kinReads.TryRemove((p, session), out _);
                    }
                }, CancellationToken.None); // always runs, so its finally frees the session's slot
                return true;
            case RunCardProtocol.Stop:
                _ = Task.Run(async () =>
                {
                    // an Antigravity thread is view-only: not stopped, here nor in a terminal
                    if (T3Agent.BarredSession(session) is { } why) p.Send(RunCardProtocol.StoppedLine(session, false, why));
                    else if (T3Agent.Can(session))
                    {
                        var stopped = await T3Agent.Stop(session, ct);
                        p.Send(RunCardProtocol.StoppedLine(session, stopped.Ok, stopped.How));
                    }
                    else
                    {
                        bool ok = m.Pid > 0 && SessionStop.Stop(session, m.Pid);
                        p.Send(RunCardProtocol.StoppedLine(session, ok, "not-stopped"));
                    }
                }, ct);
                return true;
            default:
                return false;
        }
    }

    /// 설정 > AI 'T3 Code 연결' changed: every connection hears again what each session it asked about takes now, so
    /// 보내기 and 정지 come and go on its cards at once.
    public static void Changed()
    {
        foreach (var p in RunCardLink.Peers()) Retell(p);
        foreach (var p in T3Link.Peers()) Retell(p);
    }

    private static void Retell(RunCardPeer p) => _ = Task.Run(() =>
    {
        foreach (string session in p.AskedAbout()) Tell(p, session);
    });

    /// Whether `session` takes words and a stop through Bloom: both when T3 runs it, neither on Antigravity (T3Agent.Can;
    /// the card still shows it, its kin too). An answer made before the switch
    /// changed is not sent: the retell that change started sends the one made after it (`CanGate` keeps the two in order).
    private static void Tell(RunCardPeer p, string session)
    {
        bool on = Settings.Current.T3Link;
        bool t3 = T3Agent.Can(session);
        lock (p.CanGate)
        {
            if (Settings.Current.T3Link != on) return;
            p.Send(RunCardProtocol.CanLine(session, t3, t3));
        }
    }
}
