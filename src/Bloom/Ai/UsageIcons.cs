using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bloom.Core;

namespace Bloom.Ai;

/// The logo an account wears on its home card and in the taskbar's usage (설정 > AI 계정 > 아이콘): a file the user picks
/// (.svg, .png or .ico; Bloom ships no company logos) copied into Local\usage-icons, so moving or deleting the original
/// changes nothing. The usage file names that copy.
internal static class UsageIcons
{
    public static readonly string[] Types = { ".svg", ".png", ".ico" };

    private static string Dir => Path.Combine(Paths.Local, "usage-icons");

    /// Copies `file` in for the account (named by its content, so the same picture twice is one file) and saves the
    /// settings. Throws IOException / UnauthorizedAccessException when it can't be read or written.
    public static void Pick(AiAccount a, string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (!Types.Contains(ext)) throw new IOException("not an .svg, .png or .ico file");
        byte[] bytes = File.ReadAllBytes(file);
        Directory.CreateDirectory(Dir);
        string copy = Path.Combine(Dir, $"{a.Id}-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..12]}{ext}");
        if (!File.Exists(copy)) File.WriteAllBytes(copy, bytes);
        string? old = a.Icon;
        a.Icon = copy;
        Settings.Save();
        Forget(old, a);
        UsageExport.Changed();
    }

    /// The picture the account shows: the user's pick while Bloom's copy is there, else its company's logo
    /// (OfficialLogos), else none (the letter tile).
    public static string? Shown(AiAccount a) => a.Icon is { } i && File.Exists(i) ? i : OfficialLogos.For(a.Kind);

    /// Back to no picked logo: the card and the taskbar show its company's logo, or the letter without one.
    public static void Clear(AiAccount a)
    {
        string? old = a.Icon;
        a.Icon = null;
        Settings.Save();
        Forget(old, a);
        UsageExport.Changed();
    }

    /// A copy no account names any more goes (only one inside Bloom's own folder).
    private static void Forget(string? file, AiAccount a)
    {
        if (file is null || file == a.Icon || AiAccounts.All.Any(o => o.Icon == file)) return;
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(Dir), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("usage icon", e);
        }
    }
}
