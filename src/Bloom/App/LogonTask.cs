using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.WinKey;

namespace Bloom.App;

/// Bloom at sign-in sooner while a taskbar add-on is on. Windows runs the Run entry (Autostart) a while after the shell is
/// up, and until the add-on's host loads, the stock taskbar shows. A logon task of the user's own (no admin needed, in
/// the root folder of Task Scheduler) starts `Bloom.exe --autostart` as the user signs in. The Run entry stays as it is;
/// of the two starts the later one just ends (Program: --autostart never signals the running Bloom). Only while
/// 윈도우 시작 시 실행 and an add-on are both on; never from a dev copy.
internal static class LogonTask
{
    public static string Name(string sid) => "BloomLogon-" + sid;

    public static bool Wanted => Settings.Current.Autostart && Settings.Current.AddonsOn.Count > 0;

    /// This user's sign-in, their own token without elevation, no time limit, not held back on battery, normal
    /// priority (a task's default is below normal), and a second start while one runs is ignored.
    public static string Xml(string sid, string user, string exe) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>Bloom</Author>
            <Description>Bloom: starts at sign-in, ahead of its Run entry, while a taskbar add-on is on</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(user)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(sid)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>4</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"{SecurityElement.Escape(exe)}"</Command>
              <Arguments>--autostart</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    /// The task as the settings want it: (re)registered pointing at this exe, or removed. Task Scheduler is COM: a worker
    /// thread.
    public static void Sync() => Apply(Wanted);

    public static void SyncLater() => _ = Task.Run(Sync);

    /// Gone whatever the settings say (reset, uninstall).
    public static void Remove() => Apply(false);

    private static void Apply(bool want)
    {
        if (Paths.IsDev || Paths.InstanceId != "Bloom" || Environment.ProcessPath is not { } exe) return;
        try
        {
            using var me = WindowsIdentity.GetCurrent();
            string sid = me.User!.Value;
            using var ts = TaskService.Connect();
            bool there = ts.ExistsOwn(Name(sid));
            if (want)
            {
                ts.RegisterOwn(Name(sid), Xml(sid, me.Name, exe)); // again each time: the exe may have moved
                if (!there) Log.Write("logon task registered");
            }
            else if (there)
            {
                ts.DeleteOwn(Name(sid));
                Log.Write("logon task removed");
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or IOException)
        {
            Log.Error("logon task", e);
        }
    }

    /// How long ago this Windows session signed in, as "12.3 s" ("?" when Windows doesn't say).
    public static string SinceSignIn()
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, -1 /* this session */, 24 /* WTSSessionInfo */, out IntPtr buf, out _)) return "?";
        try
        {
            long logon = Marshal.PtrToStructure<WtsInfo>(buf).LogonTime;
            if (logon <= 0) return "?";
            return $"{(DateTime.UtcNow - DateTime.FromFileTimeUtc(logon)).TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }

    /// WTSINFOW.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsInfo
    {
        public int State;
        public int SessionId, IncomingBytes, OutgoingBytes, IncomingFrames, OutgoingFrames, IncomingCompressedBytes, OutgoingCompressedBytes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string Domain;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string UserName;
        public long ConnectTime, DisconnectTime, LastInputTime, LogonTime, CurrentTime;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}
