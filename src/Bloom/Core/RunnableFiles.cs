using System;
using System.Collections.Generic;
using System.IO;

namespace Bloom.Core;

/// Files that Windows runs instead of showing when they are opened (ShellExecute "open"): programs, scripts, shortcuts and
/// the handler files that start something. Opening one that an AI wrote or named must never happen by a single click or
/// an approval given for "open" in general; such files are shown in their folder or asked about each time.
internal static class RunnableFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc", ".appref-ms", ".application", ".appx", ".appxbundle", ".msix", ".msixbundle",
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar",
        ".lnk", ".url", ".website", ".reg", ".inf", ".scf", ".settingcontent-ms", ".library-ms", ".searchconnector-ms", ".search-ms",
        ".diagcab", ".xbap", ".chm", ".gadget",
    };

    /// True when opening `path` would run something: by its extension, which is what ShellExecute goes by.
    public static bool Is(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext;
        try
        {
            ext = Path.GetExtension(path.Trim().TrimEnd('.', ' '));
        }
        catch (ArgumentException)
        {
            return true; // a name Windows can't take apart is treated as the worse case
        }
        return Extensions.Contains(ext);
    }

    /// True when a file to run is one the user hasn't had for long, so 자동 asks before running it (ToolPermissions.Auto):
    /// it is in the Downloads folder, Windows marks it as from the internet (its Zone.Identifier says zone 3 or 4), it is
    /// on a network or removable drive, or it was written within the last day (by an AI in the chat's project folder, say).
    /// A path that can't be checked counts as new.
    public static bool IsNew(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Path.TrimEndingDirectorySeparator(Paths.Downloads) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(full).DriveType is not (DriveType.Fixed or DriveType.Ram)) return true;
            var file = new FileInfo(full);
            return !file.Exists || DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromDays(1) || ZoneOf(full) >= 3;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return true;
        }
    }

    /// The zone Windows marked a downloaded file with (its Zone.Identifier stream: 3 internet, 4 restricted); 0 for none.
    private static int ZoneOf(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path + ":Zone.Identifier"))
                if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.AsSpan(7).Trim(), out int zone)) return zone;
        }
        catch (FileNotFoundException)
        {
            /* no mark */
        }
        return 0;
    }
}
