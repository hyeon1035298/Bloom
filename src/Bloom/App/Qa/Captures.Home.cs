using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set home`: the home at the launcher's size (settings.json's width and height): as it comes, in 홈 편집, a
/// block held over another place (lifted, the gap open) and let go, "+ 블록 추가" opened and a hidden block's card held
/// over the columns and let go, rows · folds · columns changed, the keyboard's way through it, then sized across the
/// two columns' width while edited, then about 1000 and 1300 wide with the columns' split dragged; the arrangement after
/// each change, the rows the keys went to and the columns' widths in home.txt.
internal static partial class Captures
{
    private static async Task HomeScreens(Host host, Launcher w, string dir)
    {
        Settings.Current.Home = null;
        Theme.Apply("dark");
        Timers.Add("timer", Tr("집중", "Focus"), DateTime.Now.AddMinutes(25));
        Timers.Add("reminder", Tr("회의", "Meeting"), DateTime.Now.AddHours(2));
        for (int i = 0; i < 60 && !host.Apps.Ready; i++) await Task.Delay(100);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), step = 0;
        foreach (var app in host.Apps.Apps.Take(9)) Usage.Current.Items[app.Id] = new Usage.Entry { Count = 3, Last = now - 60_000 * step++ };
        Home.Set("clipboard", true);
        var log = new StringBuilder();
        void Note(string what) => log.AppendLine($"{what}: {Home.Arrangement()}");
        Note("defaults");
        await Shot(w, dir, "dark-h01-home", Mode.Root, "", 900);
        w.ToggleHomeEdit();
        await Task.Delay(500);
        await Snap(w, dir, "dark-h02-edit");
        w.HoldForQa("week", false, 0.55, 0.02); // over the right column's top (one column: the top)
        await Snap(w, dir, "dark-h03-held");
        w.DropForQa();
        Note("week dropped");
        await Task.Delay(400);
        await Snap(w, dir, "dark-h04-dropped");
        // "+ 블록 추가": its cards, then one dragged up to the left column's top (the list scrolled there) and let go
        w.ToggleAddForQa();
        await Task.Delay(400);
        await Snap(w, dir, "dark-h04b-picker");
        w.ScrollForQa(0);
        await Task.Delay(300);
        w.HoldForQa("threads", true, 0.25, 0.08);
        await Snap(w, dir, "dark-h04c-card-held");
        w.DropForQa();
        Note("threads' card dropped");
        await Task.Delay(400);
        await Snap(w, dir, "dark-h04d-card-dropped");
        w.ScrollForQa(0);
        Home.SetCount("recent", 8);
        Home.SetCount("clipboard", 99); // bounded to Home.CountMax
        Home.SetCollapsed("usage", true);
        Home.MoveAcross("commands", null);
        log.AppendLine($"counts: recent {Home.Count(Home.Of("recent")!)}, clipboard {Home.Count(Home.Of("clipboard")!)}");
        Note("changed");
        w.Refresh(true);
        await Task.Delay(400);
        await Snap(w, dir, "dark-h05-edit-changed");
        w.ScrollForQa(2000);
        await Snap(w, dir, "dark-h06-edit-bottom");
        w.ToggleHomeEdit();
        await Task.Delay(400);
        await Snap(w, dir, "dark-h07-home-changed");
        var keys = new StringBuilder("keys:");
        for (int i = 0; i < 6; i++)
        {
            keys.Append(" > ").Append(w.Selected?.Title);
            w.MoveForQa(1);
        }
        log.AppendLine(keys.ToString());
        await Snap(w, dir, "dark-h08-keys");
        Theme.Apply("light");
        w.ToggleHomeEdit();
        await Task.Delay(400);
        await Snap(w, dir, "light-h09-edit");
        // sized across the two columns' width while it is edited: the tools follow the new layout
        double was = w.Width;
        w.Width = was < HomeColumns.TwoColumnWidth ? 1100 : 760;
        await Task.Delay(500);
        w.ScrollForQa(0);
        await Snap(w, dir, "light-h10-edit-resized");
        w.ToggleHomeEdit();
        // the columns' split as dragged: about 1000 wide pulled left to its least, about 1300 wide at 60 % with the
        // handle lit as the mouse shows it
        Theme.Apply("dark");
        var splitter = w.HomeForQa.SplitterForQa;
        foreach (var (width, share, name) in new[] { (1000.0, 0.2, "dark-h11-split-1000"), (1300.0, 0.6, "dark-h12-split-1300") })
        {
            w.Width = width;
            Settings.Current.HomeSplit = share;
            w.Refresh(true);
            splitter.LitForQa = share > 0.5;
            await Task.Delay(500);
            w.ScrollForQa(0);
            await Snap(w, dir, name);
            log.AppendLine($"split {share} at {width}: left column {w.HomeForQa.ColumnWidthForQa(false):0}, right {w.HomeForQa.ColumnWidthForQa(true):0} of {w.HomeForQa.ActualWidth:0}");
        }
        splitter.LitForQa = false;
        Settings.Current.HomeSplit = HomeColumns.DefaultSplit;
        w.Width = was;
        File.WriteAllText(Path.Combine(dir, "home.txt"), log.ToString());
        Settings.Current.Home = null;
    }
}
