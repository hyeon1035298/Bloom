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
}
