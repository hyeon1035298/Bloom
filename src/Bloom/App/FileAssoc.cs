using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Core;
using Bloom.Native;
using Microsoft.Win32;

namespace Bloom.App;

/// Bloom in Explorer's 연결 프로그램 for .md files (설정 > 기능 > 마크다운 문서): a ProgID whose open command is
/// `"<exe>" --open "%1"`, .md's OpenWithProgids entry, Applications\Bloom.exe, and Bloom's Capabilities under
/// RegisteredApplications for Windows' default-apps page — all in HKCU, no admin rights. Only the user can make Bloom
/// the default (Windows' rule; 기본 앱으로 정하기 opens that page): UserChoice and .md's own default value are never
/// written; an icon the user gave .md files stays (UserIcon). Kept while the feature is on, in every copy but a dev one (like Autostart); removed when it goes off and
/// when Bloom is uninstalled.
internal static class FileAssoc
{
    public const string ProgId = "Bloom.Markdown";
    public const string Capabilities = @"Software\Bloom\Capabilities";
    private const string Registered = @"Software\RegisteredApplications", OpenWith = @"Software\Classes\.md\OpenWithProgids";

    /// Windows' default-apps page for Bloom (its general page on a Windows that doesn't know the parameter).
    public const string DefaultsPage = "ms-settings:defaultapps?registeredAppUser=Bloom";

    /// One registry value: its key under the root, its name ("" = the key's default value), its data and kind.
    internal sealed record Value(string Key, string Name, object Data, RegistryValueKind Kind);

    /// Every value written for this exe.
    internal static List<Value> Plan(string exe)
    {
        string command = $"\"{exe}\" {DocHandoff.Arg} \"%1\"", app = AppKey(exe);
        string type = Tr("Markdown 문서", "Markdown document");
        const RegistryValueKind Text = RegistryValueKind.String;
        return new()
        {
            new(@"Software\Classes\" + ProgId, "", type, Text),
            new(@"Software\Classes\" + ProgId, "FriendlyTypeName", type, Text),
            new(@"Software\Classes\" + ProgId + @"\DefaultIcon", "", $"\"{exe}\",0", Text),
            new(@"Software\Classes\" + ProgId + @"\shell\open\command", "", command, Text),
            new(OpenWith, ProgId, Array.Empty<byte>(), RegistryValueKind.None),
            new(app, "FriendlyAppName", "Bloom", Text),
            new(app + @"\SupportedTypes", ".md", "", Text),
            new(app + @"\shell\open\command", "", command, Text),
            new(Capabilities, "ApplicationName", "Bloom", Text),
            new(Capabilities, "ApplicationDescription", Tr("Markdown 문서(.md)를 보고 고치는 Bloom 문서 창", "Bloom's document window to read and edit Markdown (.md) files"), Text),
            new(Capabilities + @"\FileAssociations", ".md", ProgId, Text),
            new(Registered, "Bloom", Capabilities, Text),
        };
    }

    /// The feature's switch and this copy decide: written (again, when it points elsewhere or reads differently) or
    /// removed. At the start, after the switch, after settings brought in.
    public static void Repair()
    {
        if (Paths.IsDev || Environment.ProcessPath is not { } exe) return;
        bool on = Features.On("docs");
        if (on ? !IsCurrent(Registry.CurrentUser, exe) : Owns(Registry.CurrentUser, exe)) Set(on);
    }

    /// Written for this exe, or removed (the uninstall); never in a dev copy. Explorer hears of it either way.
    public static void Set(bool on)
    {
        if (Paths.IsDev || Environment.ProcessPath is not { } exe) return;
        try
        {
            if (on) Write(Registry.CurrentUser, exe);
            else Remove(Registry.CurrentUser, exe);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("file assoc", e);
        }
        Win32.SHChangeNotify(Win32.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// Written, and for this exe.
    public static bool IsCurrent() => Environment.ProcessPath is { } exe && IsCurrent(Registry.CurrentUser, exe);

    /// Bloom is among Windows' registered apps (설정 > 기능 shows 기본 앱으로 정하기 then).
    public static bool IsRegistered() => IsRegistered(Registry.CurrentUser);

    /// Windows opens .md files with this very Bloom now (the user made it the default).
    public static bool DefaultIsBloom() => Same(Win32.AssocExecutable(".md"), Environment.ProcessPath);

    internal static bool Same(string? a, string? b)
    {
        if (a is not { Length: > 0 } || b is not { Length: > 0 }) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // ── under a root (HKCU, or a test's own key) ────────────────────────────────────────────────

    internal static void Write(RegistryKey root, string exe)
    {
        bool ownIcon = UserIcon(root, exe);
        foreach (var v in Plan(exe))
        {
            if (ownIcon && v.Key == IconKey) continue;
            using var k = root.CreateSubKey(v.Key);
            k.SetValue(v.Name, v.Data, v.Kind);
        }
    }

    /// This copy's entries go: the ProgID with the Capabilities, .md's OpenWithProgids value and the RegisteredApplications
    /// value that come with it, and Applications\Bloom.exe — each only when its open command starts this exe (or has
    /// none), so another copy's (the installed one, while a portable one switches off) stay. Bloom's keys go whole; in
    /// keys others share only Bloom's value, and such a key left empty goes too.
    internal static void Remove(RegistryKey root, string exe)
    {
        string prog = @"Software\Classes\" + ProgId, app = AppKey(exe);
        if (Ours(root, prog, exe, missing: true))
        {
            root.DeleteSubKeyTree(prog, false);
            root.DeleteSubKeyTree(Capabilities, false);
            DeleteValue(root, OpenWith, ProgId);
            DeleteValue(root, Registered, "Bloom");
        }
        if (Ours(root, app, exe, missing: true)) root.DeleteSubKeyTree(app, false);
        foreach (string key in new[] { OpenWith, @"Software\Classes\.md", @"Software\Bloom" }) DeleteIfEmpty(root, key);
    }

    /// Some entry opens files with this exe (what switching off removes).
    internal static bool Owns(RegistryKey root, string exe) => Ours(root, @"Software\Classes\" + ProgId, exe, missing: false) || Ours(root, AppKey(exe), exe, missing: false);

    private static string AppKey(string exe) => @"Software\Classes\Applications\" + Path.GetFileName(exe);

    private const string IconKey = @"Software\Classes\" + ProgId + @"\DefaultIcon";

    /// The .md icon is one the user picked (their own .ico in the ProgID's DefaultIcon), which stays: Bloom writes its
    /// own only where there is none or it is a Bloom.exe's (an older copy's path is brought up to date).
    internal static bool UserIcon(RegistryKey root, string exe)
    {
        using var k = root.OpenSubKey(IconKey);
        if (k?.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string { Length: > 0 } icon) return false;
        string file = IconFile(icon);
        return file.Length > 0 && !string.Equals(Path.GetFileName(file), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase);
    }

    /// The file of an icon location: `"C:\a b\x.exe",0`, `C:\x.ico`, `%SystemRoot%\x.dll,-3`.
    internal static string IconFile(string icon)
    {
        string s = icon.Trim();
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end > 0 ? s[1..end] : s[1..];
        }
        int comma = s.LastIndexOf(',');
        return comma > 0 && int.TryParse(s.AsSpan(comma + 1).Trim(), out _) ? s[..comma].TrimEnd() : s;
    }

    /// `key`'s open command starts this exe; `missing`: what a key without one counts as.
    private static bool Ours(RegistryKey root, string key, string exe, bool missing)
    {
        using var k = root.OpenSubKey(key + @"\shell\open\command");
        return k?.GetValue("") is string command ? Same(ExeOf(command), exe) : missing;
    }

    /// The program a command line starts: its first, maybe quoted, part.
    internal static string ExeOf(string command)
    {
        string c = command.TrimStart();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c[1..];
        }
        int space = c.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? c : c[..space];
    }

    internal static bool IsCurrent(RegistryKey root, string exe)
    {
        bool ownIcon = UserIcon(root, exe);
        foreach (var v in Plan(exe))
        {
            if (ownIcon && v.Key == IconKey) continue;
            using var k = root.OpenSubKey(v.Key);
            if (k is null || !k.GetValueNames().Contains(v.Name, StringComparer.OrdinalIgnoreCase) || k.GetValueKind(v.Name) != v.Kind) return false;
            if (v.Data is string s && k.GetValue(v.Name) as string != s) return false;
        }
        return true;
    }

    internal static bool IsRegistered(RegistryKey root)
    {
        using var k = root.OpenSubKey(Registered);
        return k?.GetValue("Bloom") is string;
    }

    private static void DeleteValue(RegistryKey root, string key, string name)
    {
        using var k = root.OpenSubKey(key, true);
        k?.DeleteValue(name, false);
    }

    private static void DeleteIfEmpty(RegistryKey root, string key)
    {
        using (var k = root.OpenSubKey(key))
            if (k is null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
        root.DeleteSubKey(key, false);
    }
}
