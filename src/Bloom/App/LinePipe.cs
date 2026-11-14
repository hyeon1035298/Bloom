using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// How a second start of Bloom hands one thing to the running Bloom: one line in, "ok" or "no" back, over a pipe only
/// this Windows user can open. DocHandoff hands a file over this way, SettingsHandoff a settings page.
internal static class LinePipe
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// What the running Bloom said to a line handed over.
    public enum Answer { Done, Refused, Silent }

    /// The running Bloom, from its start: every line handed over that is `acceptable` goes to `act` (true: done), and
    /// the answer back is "ok" or "no". `what` names the pipe in the log.
    public static void Listen(string name, Func<string, bool> acceptable, Func<string, Task<bool>> act, string what) =>
        _ = Task.Run(() => Accept(name, acceptable, act, what));

    private static async Task Accept(string name, Func<string, bool> acceptable, Func<string, Task<bool>> act, string what)
    {
        bool told = false;
        while (true)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                if (!told) Log.Error(what + " pipe", e); // once per run of failures; tried again every few seconds
                told = true;
                await Task.Delay(5000);
                continue;
            }
            told = false;
            var connected = pipe;
            _ = Task.Run(() => Serve(connected, acceptable, act, what));
        }
    }

    /// One start's line in, its answer out. A start that connects and says nothing is let go after a few seconds.
    private static async Task Serve(NamedPipeServerStream pipe, Func<string, bool> acceptable, Func<string, Task<bool>> act, string what)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8);
                using var writer = new StreamWriter(pipe, Utf8) { AutoFlush = true, NewLine = "\n" };
                using var quiet = new CancellationTokenSource(5000);
                string? line = await reader.ReadLineAsync(quiet.Token);
                bool done = line is not null && acceptable(line) && await act(line);
                if (!done) Log.Write(what + ": refused " + Shown(line));
                await writer.WriteLineAsync(done ? "ok" : "no");
            }
            catch (Exception e) when (e is IOException or OperationCanceledException)
            {
                /* the start gave up waiting, or never said anything */
            }
        }
    }

    /// A refused line as the log shows it: its start, on one line (any program of this user can send one).
    private static string Shown(string? line) => line is null ? "(nothing)"
        : new string(line.Take(200).Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? '?' : c).ToArray()) + (line.Length > 200 ? "…" : "");

    /// Whether a Bloom serves the pipe now.
    public static bool Listening(string name) => Directory.EnumerateFiles(@"\\.\pipe\", name).Any();

    /// The handing start: Done once the running Bloom did it, Refused when it said no, Silent when it didn't answer in
    /// time; null when no Bloom serves the pipe (none yet, or it just ended) — the caller waits or starts one itself.
    public static Answer? Send(string name, string line, int answerMs)
    {
        if (!Listening(name)) return null; // no Bloom listening: no wait for one
        try
        {
            using var link = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            link.Connect(1000);
            // this start may own the foreground (Explorer gave it): the running Bloom gets to bring its window forward
            // (that Bloom only: with its id unknown, no other process gets the right)
            if (Win32.GetNamedPipeServerProcessId(link.SafePipeHandle, out uint pid)) Win32.AllowSetForegroundWindow((int)pid);
            using var writer = new StreamWriter(link, Utf8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine(line);
            using var reader = new StreamReader(link, Utf8, leaveOpen: true); // the link closes last
            var answer = reader.ReadLineAsync();
            if (!answer.Wait(answerMs)) return Answer.Silent;
            return answer.Result is { } said ? said == "ok" ? Answer.Done : Answer.Refused : null; // closed without a word: it ended meanwhile
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        catch (AggregateException e) when (e.InnerException is IOException)
        {
            return null; // the Bloom went away before answering: whoever starts next does it
        }
    }
}
