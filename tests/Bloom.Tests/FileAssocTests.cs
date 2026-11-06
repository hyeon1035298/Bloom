using System;
using System.Linq;
using Bloom.App;
using Microsoft.Win32;

namespace Bloom.Tests;

/// Bloom in Explorer's 연결 프로그램 (App\FileAssoc.cs), written and removed under a throwaway key of the test's own
/// (HKCU\Software\Bloom.Tests.<guid>), never the real file types.
internal static class FileAssocTests
{
    private const string Exe = @"C:\Users\Someone\AppData\Local\Bloom\current\Bloom.exe";

    public static void PlanNeverTakesTheDefault()
    {
        var plan = FileAssoc.Plan(Exe);
        Check.True(plan.All(v => !v.Key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase)), "UserChoice is never written");
        Check.True(!plan.Any(v => v.Key.Equals(@"Software\Classes\.md", StringComparison.OrdinalIgnoreCase)), ".md's own default value is never written");
        Check.True(plan.All(v => v.Key.StartsWith(@"Software\", StringComparison.Ordinal)), "all of it under HKCU\\Software");
        string open = $"\"{Exe}\" --open \"%1\"";
        Check.Eq(plan.Single(v => v.Key == @"Software\Classes\Bloom.Markdown\shell\open\command").Data, (object)open, "the ProgID opens with --open");
        Check.Eq(plan.Single(v => v.Key == @"Software\Classes\Applications\Bloom.exe\shell\open\command").Data, (object)open, "so does Applications\\Bloom.exe");
        var progids = plan.Single(v => v.Key == @"Software\Classes\.md\OpenWithProgids");
        Check.True(progids.Name == "Bloom.Markdown" && progids.Kind == RegistryValueKind.None, "listed in .md's OpenWithProgids (REG_NONE)");
        Check.Eq(plan.Single(v => v.Key == @"Software\RegisteredApplications").Data, (object)@"Software\Bloom\Capabilities", "Windows' default-apps list points at Bloom's Capabilities");
        Check.Eq(plan.Single(v => v.Key == @"Software\Bloom\Capabilities\FileAssociations").Data, (object)"Bloom.Markdown", ".md → Bloom.Markdown");
    }

    public static void WrittenAndRemovedUnderAThrowawayRoot()
    {
        string name = @"Software\Bloom.Tests." + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            // another app's entries in the shared keys
            using (var k = root.CreateSubKey(@"Software\Classes\.md\OpenWithProgids")) k.SetValue("Other.md", Array.Empty<byte>(), RegistryValueKind.None);
            using (var k = root.CreateSubKey(@"Software\Classes\.md")) k.SetValue("", "markdown_file");
            using (var k = root.CreateSubKey(@"Software\RegisteredApplications")) k.SetValue("Other", @"Software\Other\Capabilities");
            Check.True(!FileAssoc.IsCurrent(root, Exe) && !FileAssoc.IsRegistered(root), "nothing yet");
            FileAssoc.Write(root, Exe);
            Check.True(FileAssoc.IsCurrent(root, Exe) && FileAssoc.IsRegistered(root), "written");
            Check.True(!FileAssoc.IsCurrent(root, @"D:\Moved\Bloom.exe"), "another exe: not current (repaired at the start)");
            using (var k = root.OpenSubKey(@"Software\Classes\.md")!) Check.Eq(k.GetValue(""), (object)"markdown_file", ".md's default untouched");
            FileAssoc.Remove(root, Exe);
            Check.True(!FileAssoc.IsCurrent(root, Exe) && !FileAssoc.IsRegistered(root), "removed");
            foreach (string gone in new[] { @"Software\Classes\Bloom.Markdown", @"Software\Classes\Applications\Bloom.exe", @"Software\Bloom" })
                Check.True(root.OpenSubKey(gone) is null, gone + " goes");
            using (var k = root.OpenSubKey(@"Software\Classes\.md\OpenWithProgids")!)
                Check.True(k.GetValueNames().SequenceEqual(new[] { "Other.md" }), "the other app's OpenWithProgids entry stays, Bloom's goes");
            using (var k = root.OpenSubKey(@"Software\RegisteredApplications")!)
                Check.True(k.GetValueNames().SequenceEqual(new[] { "Other" }), "the other registered app stays");
            using (var k = root.OpenSubKey(@"Software\Classes\.md")!) Check.Eq(k.GetValue(""), (object)"markdown_file", "and .md's default");
            // alone, Bloom's keys leave nothing behind
            using var bare = root.CreateSubKey("bare");
            FileAssoc.Write(bare, Exe);
            FileAssoc.Remove(bare, Exe);
            Check.Eq(bare.SubKeyCount, 1, "only Software stays (its Classes and RegisteredApplications)");
            Check.True(bare.OpenSubKey(@"Software\Classes\.md") is null, "a .md key Bloom made empty goes");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, false);
        }
    }

    public static void AnotherCopysEntriesStay()
    {
        const string Portable = @"D:\Portable Bloom\Bloom.exe";
        string name = @"Software\Bloom.Tests." + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            FileAssoc.Write(root, Exe); // the installed copy's
            Check.True(FileAssoc.Owns(root, Exe) && !FileAssoc.Owns(root, Portable), "whose they are");
            FileAssoc.Remove(root, Portable); // a portable copy switching off or uninstalling
            Check.True(FileAssoc.IsCurrent(root, Exe) && FileAssoc.IsRegistered(root), "the installed copy's entries all stay");
            using (var k = root.OpenSubKey(@"Software\Classes\.md\OpenWithProgids")!) Check.True(k.GetValueNames().Contains("Bloom.Markdown"), "its OpenWithProgids value too");
            FileAssoc.Remove(root, Exe.ToUpperInvariant());
            Check.True(!FileAssoc.IsRegistered(root) && !FileAssoc.Owns(root, Exe), "its own copy removes them (any case)");
            foreach (string gone in new[] { @"Software\Classes\Bloom.Markdown", @"Software\Classes\Applications\Bloom.exe", @"Software\Bloom" })
                Check.True(root.OpenSubKey(gone) is null, gone + " goes");
            Check.Eq(FileAssoc.ExeOf("\"C:\\a b\\Bloom.exe\" --open \"%1\""), @"C:\a b\Bloom.exe", "a quoted command's program");
            Check.Eq(FileAssoc.ExeOf(@"C:\x\Bloom.exe --open %1"), @"C:\x\Bloom.exe", "an unquoted one's");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, false);
        }
    }

    public static void AUserIconStays()
    {
        const string Icon = @"Software\Classes\Bloom.Markdown\DefaultIcon";
        string name = @"Software\Bloom.Tests." + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            FileAssoc.Write(root, Exe);
            string Read() { using var k = root.OpenSubKey(Icon)!; return (string)k.GetValue("")!; }
            Check.Eq(Read(), $"\"{Exe}\",0", "Bloom's icon at first");
            using (var k = root.OpenSubKey(Icon, true)!) k.SetValue("", @"%USERPROFILE%\Icons\md.ico", RegistryValueKind.ExpandString);
            Check.True(FileAssoc.IsCurrent(root, Exe), "the user's icon counts as current (no repair at the start)");
            FileAssoc.Write(root, Exe);
            using (var k = root.OpenSubKey(Icon)!)
                Check.Eq(k.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames), (object)@"%USERPROFILE%\Icons\md.ico", "writing again keeps it");
            using (var k = root.OpenSubKey(Icon, true)!) k.SetValue("", "\"D:\\Old Copy\\Bloom.exe\",0");
            Check.True(!FileAssoc.IsCurrent(root, Exe), "an older Bloom.exe's icon is not current");
            FileAssoc.Write(root, Exe);
            Check.Eq(Read(), $"\"{Exe}\",0", "and is brought up to date");
            Check.Eq(FileAssoc.IconFile(@"C:\Icons\md.ico,0"), @"C:\Icons\md.ico", "an icon location's file");
            Check.Eq(FileAssoc.IconFile(@"%SystemRoot%\x.dll,-3"), @"%SystemRoot%\x.dll", "with a resource id");
            Check.Eq(FileAssoc.IconFile(@"C:\a,b\md.ico"), @"C:\a,b\md.ico", "a comma in the path");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, false);
        }
    }

    public static void DefaultMeansThisExe()
    {
        Check.True(FileAssoc.Same(@"C:\Apps\Bloom\current\Bloom.exe", @"c:\apps\bloom\CURRENT\bloom.EXE"), "case doesn't matter");
        Check.True(!FileAssoc.Same(@"C:\Windows\notepad.exe", @"C:\Apps\Bloom.exe"), "another app");
        Check.True(!FileAssoc.Same(null, @"C:\Apps\Bloom.exe"), "no default set");
    }
}
