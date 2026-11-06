using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Bloom.Core;

/// Starts a program as a child of the Windows shell (explorer) instead of Bloom. Task Manager counts a process under the
/// app that started it, so an add-on host started this way shows as a program of its own and its memory and CPU are not
/// added to Bloom's. Bloom still gets the Process back and watches it as before (its exit, its exit code, Stop).
internal static class ShellChild
{
    private const uint ProcessCreateProcess = 0x0080;                // PROCESS_CREATE_PROCESS
    private const uint ExtendedStartupInfo = 0x80000, CreateNoWindow = 0x08000000, CreateSuspended = 0x4;
    private const int ParentProcessAttribute = 0x00020000;           // PROC_THREAD_ATTRIBUTE_PARENT_PROCESS
    private static readonly SearchValues<char> NeedQuotes = SearchValues.Create(" \t\n\v\"");

    /// The started process, or null when there is no shell to start it under or Windows refused; the caller then starts
    /// it the usual way. `args` are passed as separate arguments (quoted as the C runtime reads them).
    public static Process? Start(string exe, IEnumerable<string> args, string workingDirectory)
    {
        IntPtr shell = IntPtr.Zero, list = IntPtr.Zero, parent = IntPtr.Zero;
        try
        {
            IntPtr window = GetShellWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint shellPid) == 0 || shellPid == 0) return null;
            shell = OpenProcess(ProcessCreateProcess, false, shellPid);
            if (shell == IntPtr.Zero) return null;

            IntPtr size = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); // fails by design: it tells the size
            list = Marshal.AllocHGlobal(size);
            parent = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parent, shell);
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size) ||
                !UpdateProcThreadAttribute(list, 0, (IntPtr)ParentProcessAttribute, parent, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var info = new StartupInfoEx { Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, AttributeList = list };
            var line = new StringBuilder(CommandLine(exe, args));
            // suspended until Bloom holds a Process for it: an exit before that would leave nothing to watch
            if (!CreateProcessW(null, line, IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfo | CreateNoWindow | CreateSuspended, IntPtr.Zero,
                    workingDirectory, ref info, out var created))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var p = Process.GetProcessById(created.ProcessId);
                if (ResumeThread(created.Thread) == uint.MaxValue)
                {
                    _ = TerminateProcess(created.Process, 1);
                    p.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return p;
            }
            catch (ArgumentException)
            {
                _ = TerminateProcess(created.Process, 1);
                return null;
            }
            finally
            {
                _ = CloseHandle(created.Thread);
                _ = CloseHandle(created.Process);
            }
        }
        catch (Win32Exception e)
        {
            Log.Write($"start under the shell refused ({e.NativeErrorCode}): {System.IO.Path.GetFileName(exe)}");
            return null;
        }
        finally
        {
            if (list != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
            }
            if (parent != IntPtr.Zero) Marshal.FreeHGlobal(parent);
            if (shell != IntPtr.Zero) _ = CloseHandle(shell);
        }
    }

    /// `exe` and `args` as one command line, each quoted so the C runtime splits it back into the same arguments.
    public static string CommandLine(string exe, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        Append(sb, exe);
        foreach (string a in args)
        {
            sb.Append(' ');
            Append(sb, a);
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.AsSpan().IndexOfAny(NeedQuotes) < 0)
        {
            sb.Append(arg);
            return;
        }
        sb.Append('"');
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            // backslashes before a quote are doubled, and the quote escaped
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', slashes * 2); // before the closing quote
        sb.Append('"');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedData, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags,
        IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation created);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
