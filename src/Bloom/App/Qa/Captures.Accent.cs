using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set accent`: 설정 > 일반 with 직접 chosen and its picker open, dark and light: a blue that stands out, and a
/// pale yellow that gets lost on the light window (the warning and 보이게 맞추기).
internal static partial class Captures
{
    private static async Task AccentScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.En ? "en" : "ko";
        var s = Settings.Current;
        var (key, custom, theme) = (s.Accent, s.AccentCustom, s.Theme);
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, Height = 900 };
        sw.Show();
        foreach (var (name, code) in new[] { ("blue", "#3A7BD5"), ("low", "#F2E15A") })
            foreach (string t in new[] { "dark", "light" })
            {
                s.Accent = Accents.CustomKey;
                s.AccentCustom = code;
                s.Theme = t;
                Theme.Apply(t);
                sw.Page("general");
                await Task.Delay(300);
                for (int i = 0; i < 2; i++) // the second once the picker has its size
                {
                    sw.SelectForQa(SettingsView.AccentRowForQa);
                    await Task.Delay(300);
                }
                await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-{t}-a01-{name}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        sw.Close();
        (s.Accent, s.AccentCustom, s.Theme) = (key, custom, theme);
        Theme.Apply(theme);
    }
}
