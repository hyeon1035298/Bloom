using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Bloom.App;

/// One connection of the Run Card add-on (RunCardLink): what its hello and prefs said, and its lines out. A send only
/// queues the line; one writer puts them on the pipe in order, so no caller (the UI thread among them) ever waits on
/// explorer reading.
internal sealed class RunCardPeer : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
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
