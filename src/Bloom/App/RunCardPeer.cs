using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Bloom.App;

/// One connection of the Run Card add-on (RunCardLink), or of another add-on on the T3 pipe (T3Link): what its hello
/// and prefs said, the sessions it asked about (T3Lines), and its lines out. A send only queues the line; one writer
/// puts them on the pipe in order, so no caller (the UI thread among them) ever waits on explorer reading.
internal sealed class RunCardPeer : IDisposable
{
    /// The most sessions kept as asked about; past it the list starts again (asking again costs one line).
    private const int MostAsked = 512;

    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<string> asked = new(StringComparer.Ordinal); // (itself)
    private volatile bool hello, approvals;

    public RunCardPeer(NamedPipeServerStream pipe)
    {
        Pipe = pipe;
        _ = Task.Run(Write);
    }

    public NamedPipeServerStream Pipe { get; }

    /// Its hello came: it shows the Claude Code sessions' turns itself.
    public bool Hello { get => hello; set => hello = value; }

    /// 카드에서 승인·질문 is on in the add-on.
    public bool Approvals { get => approvals; set => approvals = value; }

    /// False once the connection is closed.
    public bool Send(string line) => outbox.Writer.TryWrite(line);

    /// Held while a `can` answer is checked against the switch and queued (T3Lines.Tell).
    public object CanGate { get; } = new();

    /// It asked whether `session` takes words (`can`): told again when 'T3 Code 연결' changes.
    public void Asked(string session)
    {
        lock (asked)
        {
            if (asked.Count >= MostAsked && !asked.Contains(session)) asked.Clear();
            asked.Add(session);
        }
    }

    /// The sessions it asked about.
    public string[] AskedAbout()
    {
        lock (asked) return asked.ToArray();
    }

    private async Task Write()
    {
        try
        {
            using var writer = new StreamWriter(Pipe, Utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            await foreach (string line in outbox.Reader.ReadAllAsync()) await writer.WriteLineAsync(line);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            /* the add-on went: RunCardLink's reader sees it too */
        }
    }

    public void Dispose()
    {
        outbox.Writer.TryComplete();
        Pipe.Dispose();
    }
}
