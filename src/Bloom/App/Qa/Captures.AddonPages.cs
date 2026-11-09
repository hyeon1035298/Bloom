using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set addon-pages`: the page of each add-on (the taskbar one's as it shows while running, the others' drawn from
/// their schemas), in both themes and the run's language, scrolled a screen at a time to its end.
internal static partial class Captures
{
    private static async Task AddonPageScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.Code;
        var list = AddonHub.Listed.Concat(AddonHub.Users).ToList();
        // the taskbar add-on's page while it runs (this dev copy's settings only; nothing starts from it)
        var taskbar = AddonHub.Taskbar;
        if (taskbar.Schema is not null)
        {
            Settings.Current.AddonsOn.Add(taskbar.Id);
            taskbar.QaStatus = new AddonStatus(AddonState.Running);
            list.Insert(0, taskbar);
        }
        if (list.Count == 0) return;
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            foreach (var a in list)
            {
                await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-page-{a.Id}-1.png"), SettingsView.AddonPageKey(a.Id));
                var view = sw.ViewForQa;
                for (int i = 2; i <= 12 && view.VerticalOffset + view.ViewportHeight < view.ExtentHeight - 1; i++)
                {
                    view.ScrollToVerticalOffset(view.VerticalOffset + view.ViewportHeight - 80);
                    await Task.Delay(200);
                    await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-{theme}-page-{a.Id}-{i}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
        }
        // the mouse's back button on the last add-on's page: back on 애드온
        var back = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.XButton1) { RoutedEvent = UIElement.PreviewMouseDownEvent };
        sw.RaiseEvent(back);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{l}-back.txt"), $"back button taken: {back.Handled}");
        await Task.Delay(200);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-back.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        taskbar.QaStatus = null;
        Settings.Current.AddonsOn.Remove(taskbar.Id);
        sw.Close();
    }
}
