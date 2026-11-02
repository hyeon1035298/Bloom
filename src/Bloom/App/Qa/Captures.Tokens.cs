using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set tokens`: the three token blocks alone on the home with made-up numbers (TokenDemo): two columns (1000)
/// and one (760), dark and light, every period, a span of its own per block, the models cut to two rows, a block
/// folded, 홈 편집, the first count at 42% and with nothing summed yet, no logs at all, a leftover model with no price,
/// and 설정's rows.
internal static partial class Captures
{
    private static async Task TokenScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.En ? "en" : "ko";
        Settings.Current.Home = null;
        foreach (var k in Home.Kinds) Home.Set(k.key, System.Array.IndexOf(TokenStats.Blocks, k.key) >= 0);
        TokenStats.SetForQa(TokenDemo.View);
        double width = w.Width, height = w.Height;
        w.Height = 900;
        void All(TokenPeriod p)
        {
            foreach (string key in TokenStats.Blocks) Home.SetPeriod(key, p.ToString());
        }
        async Task Take(string name, double across)
        {
            w.Width = across;
            w.Refresh(true);
            await Task.Delay(500);
            w.ScrollForQa(0);
            await Snap(w, dir, $"{l}-{name}");
        }

        w.Go(Mode.Root, "");
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            All(TokenPeriod.Day);
            await Take($"{theme}-t01-two-day", 1000);
            await Take($"{theme}-t02-one-day", 760);
        }
        Theme.Apply("dark");
        foreach (var p in new[] { TokenPeriod.Week, TokenPeriod.Month, TokenPeriod.Quarter, TokenPeriod.All })
        {
            All(p);
            await Take($"dark-t03-two-{p.ToString().ToLowerInvariant()}", 1000);
        }
        // a span of each block's own, the models cut to two, the chart folded
        Home.SetPeriod("tokens", nameof(TokenPeriod.Month));
        Home.SetPeriod("tokenchart", nameof(TokenPeriod.Week));
        Home.SetPeriod("tokenmodels", nameof(TokenPeriod.All));
        Home.SetCount("tokenmodels", 2);
        await Take("dark-t04-two-mixed", 1000);
        Theme.Apply("light");
        Home.SetCollapsed("tokenchart", true);
        await Take("light-t05-one-folded", 760);
        Home.SetCollapsed("tokenchart", false);
        w.ToggleHomeEdit();
        await Take("light-t06-two-edit", 1000);
        w.ToggleHomeEdit();
        Theme.Apply("dark");
        TokenStats.SetForQa(TokenDemo.View, 0.42);
        await Take("dark-t07-two-counting", 1000);
        // the first count with nothing summed yet: "세는 중…", not "기록 없음"
        TokenStats.SetForQa(TokenView.Empty, 0.03);
        await Take("dark-t07b-two-counting-empty", 1000);
        TokenStats.SetForQa(TokenView.Empty, null, true);
        await Take("dark-t08-two-nologs", 1000);
        // the models cut to three: the one left over has no price
        TokenStats.SetForQa(TokenDemo.View);
        Home.SetCount("tokenmodels", 3);
        Theme.Apply("light");
        await Take("light-t10-one-rest-unpriced", 760);
        Theme.Apply("dark");
        // 설정's token rows: AI 토큰's two, then 홈 화면's three blocks (their about in two lines)
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (var (filter, name) in new[] { (Tr("AI 토큰", "AI tokens"), "t09-settings"), (Tr("사용량", "usage"), "t11-settings-home") })
        {
            sw.Filter(filter);
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-{name}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
        w.Width = width;
        w.Height = height;
        Settings.Current.Home = null;
    }
}
