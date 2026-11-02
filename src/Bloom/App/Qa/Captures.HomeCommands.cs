using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set homecmd`: the home's 명령 block in 홈 편집: its rows with their ✕, two taken off, “+ 명령 추가” opened on
/// the commands left off and one put back; the block's commands after each step in homecmd.txt.
internal static partial class Captures
{
    private static async Task HomeCommandScreens(Host host, Launcher w, string dir)
    {
        Settings.Current.Home = null;
        Settings.Current.HomeCommands = null;
        Theme.Apply("dark");
        var log = new StringBuilder();
        void Note(string what) => log.AppendLine($"{what}: {string.Join(" ", HomeCommands.Shown().Select(e => e.Id))}");
        Row? RowOf(string id) => Descendants(w.HomeForQa).OfType<Row>().FirstOrDefault(r => r.Item?.Id == id);
        // the home again with an empty search line (a key that reached the window would leave it), its late rows in
        async Task Clear()
        {
            w.Go(Mode.Root, "");
            await Task.Delay(400);
        }
        async Task Press(string id)
        {
            await Clear();
            if (RowOf(id)?.Item?.Run is { } run) run();
            else log.AppendLine("FAIL  no row " + id);
            await Task.Delay(500);
        }
        Note("defaults");
        await Shot(w, dir, "dark-c01-home", Mode.Root, "", 900);
        w.ToggleHomeEdit();
        await Clear();
        w.ScrollForQa(2000);
        await Snap(w, dir, "dark-c02-edit");
        foreach (string id in new[] { "cmd:emoji", "cmd:recent" })
        {
            await Clear();
            if (RowOf(id)?.Item?.Remove is { } remove) remove();
            else log.AppendLine("FAIL  no ✕ on " + id);
            await Task.Delay(400);
        }
        Note("two taken off");
        await Press("home:cmd:add");
        await Clear();
        w.ScrollForQa(2000);
        await Snap(w, dir, "dark-c03-add-open");
        await Press("home:cmd:card:cmd:emoji");
        Note("emoji put back");
        w.ToggleHomeEdit();
        await Task.Delay(400);
        w.ScrollForQa(2000);
        await Snap(w, dir, "dark-c04-home-after");
        log.AppendLine($"kept: {Settings.Current.HomeCommands is { } p && p.Count == 1 && !p["cmd:recent"]}");
        File.WriteAllText(Path.Combine(dir, "homecmd.txt"), log.ToString());
        Settings.Current.HomeCommands = null;
    }
}
