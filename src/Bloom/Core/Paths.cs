using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bloom.Core;

internal static class Paths
{
    /// Settings, favorites, usage, chats and the bridge token. `--data <dir>` moves all of it (dev runs).
    public static string Data { get; private set; } = "";

    /// Throwaway state: icon cache, copied browser databases, the AI working folder.
    public static string Local { get; private set; } = "";

    public static string AppDir => AppContext.BaseDirectory;

    /// A dev copy (own --data folder) never writes the user's Run key or real data.
    public static bool IsDev { get; private set; }

    /// This copy's name among the running Blooms (its single-instance mutex, its signals, the approval pipe): "Bloom",
    /// or one name per dev --data folder.
    public static string InstanceId => IsDev ? "Bloom.Dev." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Data.ToLowerInvariant())))[..10] : "Bloom";

    /// One of this copy's named pipes ("approve": Claude Code's approval hook, "open": a file handed over by
    /// `--open`). Pipe names are machine-wide: this Windows user's sign-in session is part of it.
    public static string Pipe(string what)
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        using var user = System.Security.Principal.WindowsIdentity.GetCurrent();
        return $"{InstanceId}.{what}.{me.SessionId}.{user.User?.Value}";
    }

    public static void Init(string? dataOverride)
    {
        if (!string.IsNullOrEmpty(dataOverride))
        {
            Data = Path.GetFullPath(dataOverride);
            Local = Path.Combine(Data, "local");
            IsDev = true;
        }
        else
        {
            Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bloom");
            Local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloom");
        }
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Local);
    }

    /// A folder the reset and the uninstall may delete whole: the real data (a folder named Bloom right under
    /// %APPDATA% or %LOCALAPPDATA%), or a dev --data folder that holds settings.json and its local\ inside it. Anything
    /// else — a relative path, a drive's root, a folder chosen by mistake — never goes.
    public static bool Deletable(string dir)
    {
        if (!Path.IsPathFullyQualified(dir)) return false;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        if (Path.GetDirectoryName(full) is not { Length: > 0 } parent) return false; // a drive's root
        if (IsDev)
            return File.Exists(Path.Combine(full, "settings.json"))
                || (string.Equals(full, Path.TrimEndingDirectorySeparator(Local), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Data, "settings.json")));
        return string.Equals(Path.GetFileName(full), "Bloom", StringComparison.OrdinalIgnoreCase)
            && (Same(parent, AppData) || Same(parent, LocalAppData));
    }

    private static bool Same(string a, string b) =>
        b.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    public static string In(string name) => Path.Combine(Data, name);

    public static string InLocal(string name)
    {
        string p = Path.Combine(Local, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        return p;
    }

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// The user's Screenshots folder, where Windows' Snipping Tool saves: asked of Windows (FOLDERID_Screenshots), so a
    /// moved Pictures folder is followed; Pictures\Screenshots when Windows doesn't say.
    public static string Screenshots
    {
        get
        {
            if (SHGetKnownFolderPath(new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"), 0, IntPtr.Zero, out IntPtr p) == 0)
                try
                {
                    if (Marshal.PtrToStringUni(p) is { Length: > 0 } dir) return dir;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(p);
                }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        if (Paths.Data.Length == 0) return; // no data folder yet (the tests): nowhere of Bloom's to write
        try
        {
            lock (Gate)
            {
                string file = Paths.In("log.txt");
                var fi = new FileInfo(file);
                if (fi.Exists && fi.Length > 512 * 1024) File.Move(file, Paths.In("log.old.txt"), true);
                File.AppendAllText(file, $"{DateTime.Now:MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            /* logging never throws */
        }
    }

    /// 설정 > 데이터 > 로그: log.txt and log.old.txt go under the lock the writes take (the next line starts a new file).
    /// False when one is held open by another program and stays.
    public static bool Clear()
    {
        if (Paths.Data.Length == 0) return true;
        lock (Gate)
        {
            bool all = true;
            foreach (string name in new[] { "log.txt", "log.old.txt" })
                try
                {
                    File.Delete(Paths.In(name));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    all = false;
                }
            return all;
        }
    }

    public static void Error(string where, Exception e) => Write($"[{where}] {e.GetType().Name}: {e.Message}");
}
