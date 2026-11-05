using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bloom.Core;

/// How Bloom deletes its own data (설정 > 데이터, the reset, the uninstall): only inside the two data folders, by
/// Paths.Deletable's rule, and never through a junction or a symbolic link. A link met inside is taken away itself, not
/// what it points to (Local\codex-app-server\sessions and an added account's bloom-app-server\sessions are junctions to
/// Codex's own sessions); a hard link (codex-app-server\auth.json) is one name of a file, and only that name goes. A file
/// in use stays where it is and is counted, never thrown.
internal static class DataWipe
{
    /// `path` lies inside Paths.Data or Paths.Local (not one of them itself), that folder is one Paths.Deletable allows,
    /// and no folder between them is a link (a path through a junction would land outside).
    public static bool Inside(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return false;
        string full = Trim(Path.GetFullPath(path));
        foreach (string root in new[] { Paths.Data, Paths.Local })
        {
            if (root.Length == 0 || !Paths.Deletable(root)) continue;
            string top = Trim(Path.GetFullPath(root));
            if (!full.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            for (string? up = Path.GetDirectoryName(full); up is not null && up.Length > top.Length; up = Path.GetDirectoryName(up))
                if (IsLink(up)) return false;
            return true;
        }
        return false;
    }

    /// Bytes in a file or a folder tree; links are not followed (what they point to is counted where it is). 0 when
    /// missing or unreadable.
    public static long Size(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            var dir = new DirectoryInfo(path);
            if (!dir.Exists || IsLink(dir)) return 0;
            return Tree(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    public static long Size(IEnumerable<string> paths) => paths.Sum(Size);

    private static long Tree(DirectoryInfo dir)
    {
        long sum = 0;
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        foreach (var e in entries)
            if (e is FileInfo f) sum += f.Length;
            else if (e is DirectoryInfo d && !IsLink(d)) sum += Tree(d);
        return sum;
    }

    /// A file or a folder tree inside the data folders goes (Inside) → how many entries stayed (in use, denied). A path
    /// outside them is refused and counted as one that stayed; a missing one is nothing to do.
    public static int Delete(string path)
    {
        if (!Inside(path))
        {
            Log.Write("data: refused a path outside the data folders");
            return 1;
        }
        if (File.Exists(path)) return DeleteFile(path);
        var dir = new DirectoryInfo(path);
        if (!dir.Exists && dir.LinkTarget is null) return 0;
        return IsLink(dir) ? Unlink(dir.FullName) : DeleteTree(dir);
    }

    public static int Delete(IEnumerable<string> paths) => paths.Sum(Delete);

    /// The reset and the uninstall: a whole data folder (Paths.Deletable only, asked once before anything goes), links
    /// inside it unlinked, tried again while a file still closes → whether it is gone.
    public static bool DeleteRoot(string dir, int attempts = 10)
    {
        if (!Paths.Deletable(dir)) return false;
        var info = new DirectoryInfo(dir);
        for (int i = 0; i < attempts && info.Exists; i++)
        {
            if (DeleteTree(info) == 0) return true;
            System.Threading.Thread.Sleep(300);
            info.Refresh();
        }
        return !info.Exists;
    }

    private static int DeleteTree(DirectoryInfo dir)
    {
        int left = 0;
        List<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("data delete", e);
            return 1;
        }
        foreach (var e in entries)
            if (e is DirectoryInfo d) left += IsLink(d) ? Unlink(d.FullName) : DeleteTree(d);
            else left += DeleteFile(e.FullName);
        if (left > 0) return left;
        try
        {
            dir.Attributes &= ~FileAttributes.ReadOnly;
            dir.Delete(false);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("data delete", e);
            return 1;
        }
    }

    /// A junction or a symbolic link to a folder: the link goes, what it points to stays.
    private static int Unlink(string link)
    {
        try
        {
            Directory.Delete(link, false);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("data unlink", e);
            return 1;
        }
    }

    private static int DeleteFile(string file)
    {
        try
        {
            var f = new FileInfo(file);
            if (f.IsReadOnly) f.IsReadOnly = false;
            f.Delete();
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("data delete", e);
            return 1;
        }
    }

    private static bool IsLink(string dir)
    {
        try
        {
            return IsLink(new DirectoryInfo(dir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true; // unreadable: taken as one, so nothing is deleted through it
        }
    }

    private static bool IsLink(FileSystemInfo info) => info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static string Trim(string p) => Path.TrimEndingDirectorySeparator(p);
}

/// The files and folders behind each line of 설정 > 데이터, for measuring them and deleting the plain ones. Nothing here
/// makes a folder (Paths.InLocal would).
internal static class DataParts
{
    private static string L(string rel) => Path.Combine(Paths.Local, rel);

    public static string[] Chats => new[] { Paths.In("chats"), Paths.In("chat-groups.json"), L(@"ai\files"), L(@"ai\paste"), L(@"ai\omp-sessions"), L(@"ai\omp-work") };
    public static string[] Clipboard => new[] { L("clipboard") };
    public static string[] Lens => new[] { L("lens") };
    public static string[] Playlists => new[] { Paths.In("playlists") };
    public static string Recovered => Paths.In("recovered");
    public static string[] Logs => new[] { Paths.In("log.txt"), Paths.In("log.old.txt"), Paths.In("memory.txt") };
    public static string[] Tokens => new[] { L("tokens") };
    public static string Prices => Paths.In("prices.json");
    public static string[] Addons => new[] { L("addons"), L("addon-downloads"), L("user-addons") };

    /// One file of the data folder (notes.json, usage.json …).
    public static string[] File(string name) => new[] { Paths.In(name) };

    /// The folders added accounts' CLIs keep their conversations in: Claude Code's projects, Codex's sessions and
    /// archived_sessions. Only accounts with a folder of Bloom's (Local\accounts\…); a default account has none.
    public static IEnumerable<string> CliHistory(IEnumerable<(string kind, string home)> added)
    {
        foreach (var (kind, home) in added)
            if (kind == "chatgpt")
            {
                yield return Path.Combine(home, "sessions");
                yield return Path.Combine(home, "archived_sessions");
            }
            else yield return Path.Combine(home, "projects");
    }

    /// Copies of settings files made before an import (backups\) and files a failed read set aside (*.broken).
    public static IEnumerable<string> Backups()
    {
        yield return Paths.In("backups");
        foreach (string dir in new[] { Paths.Data, Paths.Local, Paths.In("chats") })
        {
            string[] broken;
            try
            {
                broken = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.broken") : Array.Empty<string>();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string f in broken) yield return f;
        }
    }

    /// What can be made again: Bloom's Codex folder for the default account (its sessions junction is unlinked, not
    /// followed), the API model lists, exchange rates, the update marker, the top-level work files of Local\ai and the
    /// usage file written for other programs.
    public static IEnumerable<string> Cache()
    {
        yield return L("codex-app-server");
        yield return Paths.In("api-models.json");
        yield return Paths.In("api-unusable.json");
        yield return L("rates.json");
        yield return L("update-tried.txt");
        yield return L(@"export\ai-usage.json");
        string ai = L("ai");
        string[] top;
        try
        {
            top = Directory.Exists(ai) ? Directory.GetFiles(ai) : Array.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (string f in top)
            if (!string.Equals(Path.GetFileName(f), ".keep", StringComparison.Ordinal)) yield return f;
    }
}
