using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// The pipe server the add-ons inside explorer.exe connect to (RunCardLink, T3Link): Bloom serves only a pipe its own
/// account made, and takes a connection only from this user's explorer.exe in Bloom's Windows session — any other
/// process that reaches the pipe is turned away. What a connection does is the caller's.
internal static class AddonPipe
{
    /// The longest line an add-on may send (bytes): a `say` at its longest (RunCardProtocol.MaxSay characters, each
    /// escaped by JSON) with room to spare. A longer one closes the connection (BoundedLineReader).
    public const int MaxLine = 256 * 1024;

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    /// Serves the pipe `name` until `stop`: each connection that passes goes to `connected` (which owns it from then on),
    /// and the pipe waits for the next. `what` names the pipe in the log.
    public static async Task Serve(string name, string what, Action<NamedPipeServerStream> connected, CancellationToken stop)
    {
        bool told = false;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                // A later instance of a name takes the first instance's security, so a pipe another account made first
                // with an open ACL would let anyone in: Bloom serves only a pipe its own account made.
                if (!OwnPipe(pipe))
                {
                    pipe.Dispose();
                    if (!told) Log.Write($"[{what}] made by another account; not served");
                    told = true;
                    if (!await Wait(stop)) return;
                    continue;
                }
                await pipe.WaitForConnectionAsync(stop);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                if (!told) Log.Error(what, e); // once per run of failures; tried again every few seconds
                told = true;
                if (!await Wait(stop)) return;
                continue;
            }
            told = false;
            // Only an add-on inside this user's explorer is let in: any other process (another account's, or a script
            // of this user's) that reaches the pipe is turned away, and the connections there are stay.
            if (!Explorer(pipe))
            {
                pipe.Dispose();
                continue;
            }
            connected(pipe);
        }
    }

    /// The retry delay; false when Bloom quits meanwhile.
    private static async Task<bool> Wait(CancellationToken stop)
    {
        try
        {
            await Task.Delay(Retry, stop);
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
}
