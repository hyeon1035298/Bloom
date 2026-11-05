using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set taskbar`: 설정 > 애드온 as a dev copy shows it (hosts never start there), then with the switch on and
/// the statuses a real host reports drawn in place of the dev one — in both themes and the run's language.
internal static partial class Captures
{
    private static async Task TaskbarScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.Code;
        var addon = AddonHub.Taskbar;
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            addon.QaStatus = null;
            Settings.Current.AddonsOn.Remove(addon.Id);
            await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-taskbar-dev.png"));
            Settings.Current.AddonsOn.Add(addon.Id); // this dev copy's own settings only; nothing starts from it
            foreach (var (name, status) in new[] { ("symbols", new AddonStatus(AddonState.Starting, 42)), ("running", new AddonStatus(AddonState.Running)), ("safe-mode", new AddonStatus(AddonState.SafeMode)) })
            {
                addon.QaStatus = status;
                await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-taskbar-{name}.png"));
            }
        }
        addon.QaStatus = null;
        Settings.Current.AddonsOn.Remove(addon.Id);
        sw.Close();
    }

    private static async Task TaskbarShot(SettingsWindow sw, string file, string page = "addons")
    {
        sw.Page(page);
        await Task.Delay(300);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(file), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// `--qa-set addons`: 설정 > 애드온 for the add-ons in this dev copy's user-addons folder (put one there first), as a
    /// dev copy shows it, then with the statuses a real host reports drawn in its place.
    private static async Task AddonScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.Code;
        var list = AddonHub.Users.ToList();
        if (list.Count == 0) return;
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            foreach (var (name, status) in new (string, AddonStatus?)[] { ("dev", null), ("running", new AddonStatus(AddonState.Running)), ("windhawk", new AddonStatus(AddonState.Windhawk)), ("stopped", new AddonStatus(AddonState.Stopped)) })
            {
                foreach (var a in list) a.QaStatus = status;
                await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-addons-{name}.png"));
            }
        }
        foreach (var a in list) a.QaStatus = null;
        sw.Close();
    }
}
