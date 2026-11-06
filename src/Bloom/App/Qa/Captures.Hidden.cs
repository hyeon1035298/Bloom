using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set hidden`: Ctrl+K › 검색에서 숨기기 on the first result, the same search after it, and 설정 › 런처 › 숨긴 항목;
/// the rows before and after in hidden.txt.
internal static partial class Captures
{
    private static async Task HiddenScreens(Host host, Launcher w, string dir)
    {
        Theme.Apply("dark");
        string query = Tr("메모장", "Notepad");
        var log = new StringBuilder();
        string Rows() => string.Join(" | ", host.Engine.Root(query).SelectMany(s => s.Items).Select(i => i.Id));
        log.AppendLine("before: " + Rows());
        await Shot(w, dir, "dark-h1-search", Mode.Root, query, 700);
        if (w.Selected is not { } item)
        {
            log.AppendLine("FAIL  no result for " + query);
            File.WriteAllText(Path.Combine(dir, "hidden.txt"), log.ToString());
            return;
        }
        w.OpenActions();
        await Snap(w, dir, "dark-h2-actions");
        w.CloseActionsForQa();
        string hide = Tr("검색에서 숨기기", "Hide from search");
        if (w.ActionsFor(item).FirstOrDefault(a => a.Title == hide) is { } action) action.Run();
        else log.AppendLine("FAIL  no ‘" + hide + "’ on " + item.Id);
        log.AppendLine("hidden: " + item.Id + " · in store: " + HiddenResults.Has(item.Id));
        log.AppendLine("after: " + Rows());
        await Shot(w, dir, "dark-h3-after", Mode.Root, query, 700);

        var sw = new SettingsWindow(host) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Filter(Tr("숨긴", "hidden"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-h4-settings.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        File.WriteAllText(Path.Combine(dir, "hidden.txt"), log.ToString());
    }
}
