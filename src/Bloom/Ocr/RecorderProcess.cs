using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Ocr;

/// Bloom's side of a screen recording: the recorder runs as `Bloom.exe --record …` (ScreenRecorder) with no window, so
/// its memory goes back to Windows when it ends.
///   RecorderProcess.Start(area, file, systemAudio, mic) — starts it; never throws (a failed start faults Ready).
///   Ready     — completes on "ready"; faults with RecorderException(Code, Message) when it reports an error or ends first.
///   Go()      — the countdown is over: recording starts now.
///   Stop()    — finishes the file: RecordingResult(Ok, File, Duration, ErrorCode, Message). Waits up to `timeout`
///               (30 s by default), then kills it ("timeout"). Before Go it ends with no file ("cancelled").
///   Failed    — (code, message) when it reports an error or dies after Go and before Stop; on a thread-pool thread.
///   Dispose() — closes its input (a started recording still finishes its file) and kills it if it is still there 15 s on.
/// A file left by a recorder that died (a crash, a kill) is deleted, unless it was there before Start.
internal sealed class RecorderProcess : IDisposable
{
    /// The exe to start: Bloom.exe next to this assembly unless set (the tests).
    public static string? ExePath { get; set; }

    private readonly Process? process;
    private readonly string file;
    private readonly bool existed;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<RecordingResult> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool going, stopping;

    public Task Ready => ready.Task;
    public event Action<string, string>? Failed;

    private RecorderProcess(string file, Process? process)
    {
        this.file = file;
        this.process = process;
        existed = File.Exists(file);
    }

    public static RecorderProcess Start(ScreenRect area, string outFile, bool systemAudio, bool mic)
    {
        var psi = new ProcessStartInfo(Exe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add(ScreenRecorder.Arg);
        foreach (int n in new[] { area.X, area.Y, area.Width, area.Height }) psi.ArgumentList.Add(n.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add(outFile);
        psi.ArgumentList.Add((systemAudio, mic) switch { (true, true) => "sys+mic", (true, false) => "sys", (false, true) => "mic", _ => "-" });
        RecorderProcess r;
        try
        {
            var p = new Process { StartInfo = psi };
            r = new RecorderProcess(outFile, p);
            p.Start();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            r = new RecorderProcess(outFile, null);
            r.Finish(false, 0, "start", e.Message);
            return r;
        }
        _ = Task.Run(r.Read);
        return r;
    }

    private static string Exe()
    {
        if (ExePath is { } set) return set;
        string? self = Environment.ProcessPath;
        if (self is not null && Path.GetFileName(self).Equals("Bloom.exe", StringComparison.OrdinalIgnoreCase)) return self;
        return Path.Combine(Path.GetDirectoryName(typeof(RecorderProcess).Assembly.Location) ?? AppContext.BaseDirectory, "Bloom.exe");
    }

    public void Go()
    {
        going = true;
        Send("go");
    }

    public async Task<RecordingResult> Stop(TimeSpan? timeout = null)
    {
        stopping = true;
        Send("stop");
        var limit = Task.Delay(timeout ?? TimeSpan.FromSeconds(30));
        if (await Task.WhenAny(ended.Task, limit).ConfigureAwait(false) == ended.Task) return await ended.Task.ConfigureAwait(false);
        Kill();
        Finish(false, 0, "timeout", "the recorder did not finish in time");
        return await ended.Task.ConfigureAwait(false);
    }

    private void Send(string line)
    {
        try
        {
            process?.StandardInput.WriteLine(line);
            process?.StandardInput.Flush();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            /* gone: Read sees its end */
        }
    }

    private async Task Read()
    {
        try
        {
            while (await process!.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                string[] parts = line.Trim().Split(' ', 3);
                switch (parts[0])
                {
                    case "ready":
                        ready.TrySetResult();
                        break;
                    case "done":
                        Finish(true, parts.Length > 1 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ms) ? ms : 0, null, null);
                        break;
                    case "error":
                        Finish(false, 0, parts.Length > 1 ? parts[1] : "error", parts.Length > 2 ? parts[2] : "");
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            /* the pipe broke: the same as its end */
        }
        int code = -1;
        try
        {
            if (process!.WaitForExit(5000)) code = process.ExitCode;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            /* disposed meanwhile */
        }
        if (ended.Task.IsCompleted) return;
        if (stopping && !going)
            Finish(false, 0, "cancelled", "stopped before recording started");
        else
            Finish(false, 0, "exit", $"the recorder ended without finishing the file (exit code {code})");
        DeleteLeftover();
    }

    private void Finish(bool ok, long ms, string? code, string? message)
    {
        if (!ended.TrySetResult(new RecordingResult(ok, file, TimeSpan.FromMilliseconds(ms), code, message))) return;
        if (ok)
        {
            ready.TrySetResult();
            return;
        }
        ready.TrySetException(new RecorderException(code!, message ?? ""));
        if (code == "cancelled") return;
        Core.Log.Error("recorder", new RecorderException(code!, message ?? ""));
        if (going && !stopping) Failed?.Invoke(code!, message ?? "");
    }

    private void Kill()
    {
        try
        {
            if (process is { HasExited: false }) process.Kill(true);
            process?.WaitForExit(5000);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            /* already gone */
        }
        DeleteLeftover();
    }

    /// A recorder that died leaves a file without its index, which no player opens.
    private void DeleteLeftover()
    {
        if (existed) return;
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* held: left as it is */
        }
    }

    public void Dispose()
    {
        if (process is null) return;
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            /* gone already */
        }
        _ = Task.Run(async () =>
        {
            if (await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false) != ended.Task)
            {
                Kill();
                Finish(false, 0, "timeout", "the recorder did not end");
            }
            process.Dispose();
        });
    }
}

/// How a recording ended. Duration is the video's length when Ok.
internal sealed record RecordingResult(bool Ok, string File, TimeSpan Duration, string? ErrorCode, string? Message);

/// The recorder's "error <code> <message>" (codes encoder, audio, capture, io, args; Bloom's own: start, exit,
/// timeout, cancelled).
internal sealed class RecorderException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
