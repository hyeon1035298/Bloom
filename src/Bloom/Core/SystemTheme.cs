using Microsoft.Win32;

namespace Bloom.Core;

/// Light or dark as Bloom's theme setting says, without WPF, so it can be read off the UI thread too: "dark", "light",
/// else Windows' app mode.
internal static class SystemTheme
{
    public static bool Dark(string setting) => setting switch { "dark" => true, "light" => false, _ => AppsDark() };

    /// Windows' app mode is dark.
    public static bool AppsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
