using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set addonart`: the moving picture of each add-on Bloom draws one for (AddonArt, on 설정 > 애드온), held at
/// moments through its loop, one row per add-on, one sheet per theme in the run's language.
internal static partial class Captures
{
    private static readonly string[] ArtIds = [AddonProtocol.Id, "alt-tab-per-monitor", "brightness", "memory-clean", "keep-awake", "run-card"];
    private static readonly double[] ArtMoments = [0, .15, .25, .40, .55, .65, .80];

    private static async Task AddonArtScreens(Host host, Launcher w, string dir)
    {
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            var sheet = new StackPanel { Background = Theme.P.Solid };
            foreach (string id in ArtIds)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
                foreach (double at in ArtMoments)
                {
                    if (AddonArt.For(id) is not { } art) break;
                    art.Hold = at;
                    row.Children.Add(new SmoothBorder { Width = AddonArt.W, Height = AddonArt.H, CornerRadius = new CornerRadius(8), Background = Theme.P.Card, Margin = new Thickness(4), Child = art });
                }
                sheet.Children.Add(row);
            }
            var win = new Window
            {
                Content = sheet,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
            };
            win.Show();
            await Task.Delay(300);
            await win.Dispatcher.InvokeAsync(() => Shots.Save(sheet, Path.Combine(dir, $"{Lang.Code}-{theme}-addonart.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            win.Close();
        }
    }
}
