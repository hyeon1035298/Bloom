using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// `Bloom.exe --open <file>` (Explorer's 연결 프로그램, FileAssoc) while a Bloom runs: this start hands the file's full
/// path to the running Bloom over a pipe only this Windows user can open, lets it take the foreground, waits for its
/// one-line answer and ends; the running Bloom opens the file (Host.OpenHanded: its document window, the mini player for
/// an audio file).
internal static class DocHandoff
{
    public const string Arg = "--open";

    /// The running Bloom's pipe for files handed over.
    public static string Pipe => Paths.Pipe("open");

    private static readonly UTF8Encoding Utf8 = new(false);

    /// A path the pipe takes: a full one, of a file that is there.
    public static bool Acceptable(string path) => path.Length is > 0 and < 32768 && Path.IsPathFullyQualified(path) && File.Exists(path);

    /// The running Bloom, from its start: every path handed over that is Acceptable goes to `open` (true: opened), and
    /// the answer back is "ok" or "no".
    public static void Listen(string name, Func<string, Task<bool>> open) => _ = Task.Run(() => Accept(name, open));

    private static async Task Accept(string name, Func<string, Task<bool>> open)
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
                if (!told) Log.Error("open pipe", e); // once per run of failures; tried again every few seconds
                told = true;
                await Task.Delay(5000);
                continue;
            }
            told = false;
            var connected = pipe;
            _ = Task.Run(() => Serve(connected, open));
        }
    }

    /// One start's path in, its answer out. A start that connects and says nothing is let go after a few seconds.
    private static async Task Serve(NamedPipeServerStream pipe, Func<string, Task<bool>> open)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8);
                using var writer = new StreamWriter(pipe, Utf8) { AutoFlush = true, NewLine = "\n" };
                using var quiet = new CancellationTokenSource(5000);
                string? path = await reader.ReadLineAsync(quiet.Token);
                bool opened = path is not null && Acceptable(path) && await open(path);
                if (!opened) Log.Write("open: refused " + path);
                await writer.WriteLineAsync(opened ? "ok" : "no");
            }
            catch (Exception e) when (e is IOException or OperationCanceledException)
            {
                /* the start gave up waiting, or never said anything */
            }
        }
    }

    /// What the running Bloom said to a file handed over.
    public enum Answer { Opened, Refused, Silent }

    /// The handing start: Opened once the running Bloom opened the file, Refused when it said no, Silent when it didn't
    /// answer in time; null when no Bloom serves the pipe (none yet, or it just ended) — the caller waits or starts one
    /// itself.
    public static Answer? Send(string name, string path, int answerMs = 15000)
    {
        if (!Directory.EnumerateFiles(@"\\.\pipe\", name).Any()) return null; // no Bloom listening: no wait for one
        try
        {
            using var link = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            link.Connect(1000);
            // this start may own the foreground (Explorer gave it): the running Bloom gets to bring its window forward
            Win32.AllowSetForegroundWindow(Win32.GetNamedPipeServerProcessId(link.SafePipeHandle, out uint pid) ? (int)pid : -1);
            using var writer = new StreamWriter(link, Utf8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine(path);
            using var reader = new StreamReader(link, Utf8, leaveOpen: true); // the link closes last
            var answer = reader.ReadLineAsync();
            if (!answer.Wait(answerMs)) return Answer.Silent;
            return answer.Result is { } said ? said == "ok" ? Answer.Opened : Answer.Refused : null; // closed without a word: it ended meanwhile
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        catch (AggregateException e) when (e.InnerException is IOException)
        {
            return null; // the Bloom went away before answering: whoever starts next opens it
        }
    }

    /// A file that didn't open, said in a plain message box (from the start that was asked to open it): never silently.
    public static void Tell(string path, string why)
    {
        Log.Write("open: failed " + path + " · " + why);
        MessageBox.Show(Tr("Bloom이 파일을 열지 못했습니다", "Bloom couldn't open the file") + "\n\n" + path + "\n\n" + why, "Bloom", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// Why: the file isn't there, isn't a full path, or the running Bloom said no.
    public static string Refused => Tr("파일이 없거나 열 수 없는 파일입니다", "The file isn't there, or it can't be opened");
}
