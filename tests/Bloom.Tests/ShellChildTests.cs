using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Core;

namespace Bloom.Tests;

/// Add-on hosts start under the shell (explorer), so Task Manager does not count them inside Bloom.
internal static class ShellChildTests
{
    /// Each argument comes back whole after Windows splits the command line (CommandLineToArgvW, the C runtime's rule).
    public static void ArgumentsSurviveTheCommandLine()
    {
        string[] args = { "--serve", "123", @"C:\Program Files\Bloom\Bloom.exe", @"C:\폴더\a b\", "", "say \"hi\"", @"back\\""slash", "tab\there", @"C:\end\" };
        string line = ShellChild.CommandLine(@"C:\Some Dir\Host.exe", args);
        IntPtr argv = CommandLineToArgvW(line, out int count);
        try
        {
            var back = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
            Check.Eq(back[0], @"C:\Some Dir\Host.exe", "the program");
            Check.Eq(string.Join("|", back.Skip(1)), string.Join("|", args), "every argument as it was");
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    /// The stand-in host started by the runner has explorer as its parent, and Bloom still sees it end.
    public static void HostRunsUnderTheShell()
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) return; // no desktop shell (a service session): the usual start is the only one
        _ = GetWindowThreadProcessId(shell, out uint shellPid);
        string args = Path.Combine(Path.GetTempPath(), "bloom-shell-args-" + Guid.NewGuid().ToString("N"));
        string exe = Environment.ProcessPath!;
        string[] lead = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { typeof(ShellChildTests).Assembly.Location } : Array.Empty<string>();
        Environment.SetEnvironmentVariable(FakeAddonHost.Mode, "hang");
        Environment.SetEnvironmentVariable(FakeAddonHost.ArgsFile, args);
        try
        {
            using var p = ShellChild.Start(exe, lead.Concat(new[] { Addons.AddonProtocol.ServeArg, "0" }), Path.GetDirectoryName(exe)!);
            Check.True(p is not null, "started under the shell");
            Check.Eq(Parent(p!.Id), (int)shellPid, "explorer is its parent, not Bloom");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(args) && clock.ElapsedMilliseconds < 15000) Thread.Sleep(20);
            Check.True(File.Exists(args), "it runs with Bloom's environment");
            p.Kill();
            Check.True(p.WaitForExit(5000), "Bloom still sees it end");
        }
        finally
        {
            Environment.SetEnvironmentVariable(FakeAddonHost.Mode, null);
            Environment.SetEnvironmentVariable(FakeAddonHost.ArgsFile, null);
            File.Delete(args);
        }
    }

    private static int Parent(int pid)
    {
        using var p = System.Diagnostics.Process.GetProcessById(pid);
        var info = new BasicInformation();
        int status = NtQueryInformationProcess(p.Handle, 0, ref info, Marshal.SizeOf<BasicInformation>(), out _);
        Check.Eq(status, 0, "the parent can be read");
        return checked((int)info.ParentProcessId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, ParentProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref BasicInformation info, int size, out int returned);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
