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
    /// `--qa-set whatsnew`: 바뀐 점 with this version's pictures, dark and light, at the top and scrolled down.
    private static async Task WhatsNewScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var news = new WhatsNewWindow(ReleaseNotes.Current, ReleaseNotes.For(ReleaseNotes.Current) ?? "-") { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            news.Show();
            await Task.Delay(500);
            var scroll = ((StackPanel)((Border)news.Content).Child).Children.OfType<ScrollViewer>().First();
            foreach (double at in new[] { 0.0, 420, 840 })
            {
                scroll.ScrollToVerticalOffset(at);
                await Task.Delay(300);
                await news.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)news.Content, Path.Combine(dir, $"{l}-whatsnew-{theme}-{at:0}.png")), DispatcherPriority.ApplicationIdle);
            }
            news.Close();
        }
    }
}
