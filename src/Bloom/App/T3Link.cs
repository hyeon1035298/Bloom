using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.App;

/// Bloom's end of the T3 pipe, for the add-ons other than the Run Card (PillTaskbar's chat panel) that send to, answer
/// and stop T3 Code's threads through Bloom: the Run Card's lines about a session (T3Lines; no hello, a connection
/// starts asking at once), several connections at once, each from this user's explorer.exe only (AddonPipe). The name
/// is Paths.Pipe's: `Bloom.t3.<session>.<SID>` for the installed Bloom, a dev copy's own with --data.
internal static class T3Link
{
    /// The pipe's part of the name (Paths.Pipe).
    public const string PipeKind = "t3";

    /// Connections at once: one per add-on, a few to spare; one more is closed as it comes.
    private const int Most = 8;

    private static readonly object gate = new();
    private static readonly List<RunCardPeer> peers = new(); // (gate)
    private static readonly CancellationTokenSource stop = new();
    private static bool started; // (gate)

    public static string Pipe => Paths.Pipe(PipeKind);

    /// Host, once (not in recording mode).
    public static void Start()
    {
        lock (gate)
        {
            if (started) return;
            started = true;
        }
        _ = Task.Run(() => AddonPipe.Serve(Pipe, "t3 pipe", Take, stop.Token));
    }

    /// Bloom quits: the add-ons see the pipe close now, not when the process is gone.
    public static void Stop()
    {
        stop.Cancel();
        RunCardPeer[] all;
        lock (gate)
        {
            all = peers.ToArray();
            peers.Clear();
        }
        foreach (var p in all) p.Dispose();
    }

    /// The connections now.
    internal static RunCardPeer[] Peers()
    {
        lock (gate) return peers.ToArray();
    }

    private static void Take(NamedPipeServerStream pipe)
    {
        var p = new RunCardPeer(pipe);
        bool room;
        lock (gate)
        {
            room = peers.Count < Most;
            if (room) peers.Add(p);
        }
        if (!room)
        {
            p.Dispose();
            return;
        }
        _ = Task.Run(() => Read(p));
    }

    private static async Task Read(RunCardPeer p)
    {
        try
        {
            var reader = new Ai.BoundedLineReader(p.Pipe, AddonPipe.MaxLine);
            while (await reader.ReadLineAsync(stop.Token) is { } line)
                if (RunCardProtocol.Parse(line) is { } m) T3Lines.Heard(p, m, stop.Token); // anything else is ignored
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            /* the add-on went, or Bloom quits */
        }
        finally
        {
            lock (gate) peers.Remove(p);
            p.Dispose();
        }
    }
}
