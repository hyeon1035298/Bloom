using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// Bloom's end of the Run Card add-on (explorer.exe), which shows a live card per running Claude Code session: Bloom
/// is the pipe server and the add-on connects, one at a time — a new connection (explorer started again) replaces the
/// old. While its hello stands, Bloom leaves Claude Code's turn cards to it (Notices), and with 카드에서 승인·질문 on
/// sends it the approval requests first (ClaudeApprovals). The lines: RunCardProtocol.
/// The name is Paths.Pipe's, so the installed Bloom's is `Bloom.run-card.<session>.<SID>` and a dev copy (--data) has
/// its own: it never takes the add-on from the installed Bloom.
internal static class RunCardLink
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
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
        _ = Task.Run(Accept);
    }

    /// Bloom quits: the add-on sees the pipe close now, not when the process is gone.
    public static void Stop()
    {
        stop.Cancel();
        RunCardPeer? now;
        lock (gate) now = peer;
        if (now is not null) Drop(now);
    }

    private static async Task Accept()
    {
        bool told = false;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(Pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                // A later instance of a name takes the first instance's security, so a pipe another account made first
                // with an open ACL would let anyone in: Bloom serves only a pipe its own account made.
                if (!OwnPipe(pipe))
                {
                    pipe.Dispose();
                    if (!told) Log.Write("[run card pipe] made by another account; not served");
                    told = true;
                    if (!await Wait()) return;
                    continue;
                }
                await pipe.WaitForConnectionAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                if (!told) Log.Error("run card pipe", e); // once per run of failures; tried again every few seconds
                told = true;
                if (!await Wait()) return;
                continue;
            }
            told = false;
            // Only the add-on, inside this user's explorer, answers approvals: any other process (another account's,
            // or a script of this user's) that reaches the pipe is turned away, and the connected add-on stays.
            if (!Explorer(pipe))
            {
                pipe.Dispose();
                continue;
            }
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
    }

    /// The retry delay; false when Bloom quits meanwhile.
    private static async Task<bool> Wait()
    {
        try
        {
            await Task.Delay(Retry, stop.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// The pipe's owner is Bloom's account (CurrentUserOnly sets the token's owner, which for an elevated
    /// administrator is the Administrators group).
    internal static bool OwnPipe(NamedPipeServerStream pipe)
    {
        using var me = WindowsIdentity.GetCurrent();
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        return owner is not null && (owner == me.User || owner == me.Owner);
    }

    /// The client is explorer.exe of Windows' own folder, running as Bloom's account in Bloom's Windows session; false
    /// whenever any of it can't be asked.
    internal static bool Explorer(NamedPipeServerStream pipe)
    {
        try
        {
            if (Win32.PipeClient(pipe.SafePipeHandle) is not { } pid) return false;
            using var self = Process.GetCurrentProcess();
            if (Win32.ProcessSession(pid) is not { } session || session != self.SessionId) return false;
            var (user, path) = Win32.ProcessIdentity(pid);
            using var me = WindowsIdentity.GetCurrent();
            return user is not null && user == me.User && IsExplorer(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// A program path is `explorer.exe` right in the Windows folder (any letter case).
    internal static bool IsExplorer(string? path, string windows) =>
        !string.IsNullOrEmpty(path) && windows.Length > 0 && string.Equals(path, Path.Combine(windows, "explorer.exe"), StringComparison.OrdinalIgnoreCase);

    private static async Task Read(RunCardPeer p)
    {
        try
        {
            p.Send(RunCardProtocol.HelloLine());
            using var reader = new StreamReader(p.Pipe, Utf8, leaveOpen: true);
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
