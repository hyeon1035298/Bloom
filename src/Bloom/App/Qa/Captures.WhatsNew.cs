using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set whatsnew`: 바뀐 점 from GitHub with this version's pictures, dark and light, at the top and scrolled down;
    /// in dark also an earlier version picked in the list.
    private static async Task WhatsNewScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var versions = await ReleaseNotes.Versions(); // from GitHub (or --notes-from), as the app reads them
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var news = new WhatsNewWindow(ReleaseNotes.Current, versions) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            news.Show();
            await Task.Delay(1500); // the notes and pictures come
            var scroll = ((StackPanel)((Border)news.Content).Child).Children.OfType<ScrollViewer>().First();
            foreach (double at in new[] { 0.0, 420, 840 })
            {
                scroll.ScrollToVerticalOffset(at);
                await Task.Delay(300);
                await news.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)news.Content, Path.Combine(dir, $"{l}-whatsnew-{theme}-{at:0}.png")), DispatcherPriority.ApplicationIdle);
            }
            if (theme == "dark" && versions?.FirstOrDefault(v => v != ReleaseNotes.Current) is { } other)
            {
                // another version picked in the list: its notes fetched then, its pictures after them
                await news.ShowForQa(other);
                await Task.Delay(1500);
                scroll.ScrollToVerticalOffset(0);
                await news.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)news.Content, Path.Combine(dir, $"{l}-whatsnew-{theme}-{other}.png")), DispatcherPriority.ApplicationIdle);
            }
            news.Close();
        }
    }
}
