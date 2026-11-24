using System;
using System.Linq;
using Bloom.Addons;

namespace Bloom.Ui;

/// 설정 > 애드온 > 작업 카드: where its cards go — the monitor by chips with each one's
/// number and name (no map of them: the names are enough), the place on a picture of that monitor
/// (RunCardPictures); 크기 as one row of steps. The other lines are SchemaRow's (늘어놓기 and 알약 길이 draw the picture
/// again).
internal sealed partial class SettingsView
{
    private static string RunCardScreenValue(Native.MonitorNames.Screen s) => "display" + s.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// 모니터: a chip per monitor connected now ("1 · <its name> (주)"), one more for a number picked that isn't connected.
    /// False when no monitor could be read (SchemaRow's list instead).
    private bool RunCardMonitor(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key, title = item.Name.Shown;
        string? sub = item.About?.Shown;
        if (Record(title, sub, filter)) return true;
        var screens = Native.MonitorNames.Read();
        if (screens.Count == 0) return false;
        string value = v.Text(key);
        var chosen = RunCardPictures.Chosen(value, screens);
        var options = screens.Select(s => (RunCardScreenValue(s), $"{s.Number} · {RunCardPictures.NameOf(s)}" + (s.Primary ? Tr(" (주)", " (Primary)") : ""))).ToList();
        if (chosen is null && RunCardPictures.Number(value) is > 0 and var n) options.Add((value, $"{n} · {Tr("연결 안 됨", "Not connected")}"));
        void Pick(string x)
        {
            v.Set(key, x);
            Dispatcher.BeginInvoke(TaskbarRebuild); // the screen below follows
        }
        var chips = new Segmented(options, chosen is null ? value : RunCardScreenValue(chosen));
        chips.Picked += Pick;
        Place(title, sub, () => Line(title, sub, chips, folding && filter.Length > 0), () => Pick(chips.Cycle()), d =>
        {
            Pick(chips.Step(d));
            return true;
        }, filter);
        return true;
    }

    /// 자리: the chosen monitor with its eight places; the place picked after the title in the accent; ←→ step through
    /// the places in the add-on's order, Enter goes round.
    private void RunCardPlace(AddonSetting item, AddonValues v, string filter)
    {
        string key = item.Key, title = item.Name.Shown;
        string? sub = item.About?.Shown;
        if (Record(title, sub, filter)) return;
        var places = RunCardPictures.Places.Select(p => p.value).ToArray();
        int at = Math.Max(0, Array.IndexOf(places, v.Text(key)));
        string Name(string x) => item.Options.FirstOrDefault(o => o.Value == x)?.Name.Shown ?? x;
        void Pick(string x)
        {
            v.Set(key, x);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        }
        var screens = Native.MonitorNames.Read();
        var screen = RunCardPictures.Chosen(v.Text("card.monitor"), screens) ?? screens.FirstOrDefault(s => s.Primary);
        int size = int.TryParse(v.Text("card.size"), out int z) ? z : 100;
        var stage = RunCardPictures.Stage(screen, places[at], Name, v.Int("card.margin"), size, v.Text("card.arrange") == "row", v.Text("pill.width") != "fit", Pick,
            Tr("자리를 누르면 카드가 그리로 옮겨 갑니다 · 여백과 크기를 바꾸면 그림도 따라 바뀝니다", "Click a position to move the card there · Preview updates as you adjust margins and size"));
        Place(title, sub, () => Line(title, sub, null, folding && filter.Length > 0, stage, picked: Name(places[at])), () => Pick(places[(at + 1) % places.Length]), d =>
        {
            Pick(places[Math.Clamp(at + d, 0, places.Length - 1)]);
            return true;
        }, filter);
    }

    /// 크기: its six steps side by side ("80 … 150%"), the picture above following a pick.
    private void RunCardSize(AddonSetting item, AddonValues v, string filter)
    {
        var options = item.Options.Select((o, i) => (o.Value, i == item.Options.Count - 1 ? o.Name.Shown : o.Value)).ToArray();
        Choice(item.Name.Shown, item.About?.Shown, Glyph(item), options, v.Text(item.Key), x =>
        {
            v.Set(item.Key, x);
            Dispatcher.BeginInvoke(TaskbarRebuild);
        }, filter, most: options.Length);
    }
}
