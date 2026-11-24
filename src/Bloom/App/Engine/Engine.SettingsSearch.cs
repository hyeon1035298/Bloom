using System.Linq;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// Bloom's own settings in the root search: "유리 효과", "단축키", "glass" list the matching lines of 설정, and Enter opens
/// the settings window on that line. The lines are the settings pages' own (SettingsCatalog).
internal sealed partial class Engine
{
    /// At most this many settings lines under the results.
    private const int SettingsRows = 3;

    /// A single letter would match a line of almost any text, so the search starts at two.
    private const int SettingsMinChars = 2;

    private Section? RootSettings(Query q)
    {
        if (q.Lower.Length < SettingsMinChars) return null;
        var found = Score(SettingsCatalog.All(host), l => l.Match, q).Take(SettingsRows).ToList();
        if (found.Count == 0) return null;
        var s = new Section(Tr("Bloom 설정", "Bloom Settings"));
        foreach (var line in found)
            s.Items.Add(new ResultItem
            {
                Id = $"cfg:{line.PageKey}:{line.Title}",
                Kind = ItemKind.Setting,
                Title = line.Title,
                Subtitle = Tr($"설정 › {line.PageTitle}", $"Settings › {line.PageTitle}"),
                Accessory = Tr("설정", "Settings"),
                Icon = IconSpec.Of(G.Settings),
                Run = () => host.OpenSettings(line.PageKey, line.Title),
            });
        return s;
    }
}
