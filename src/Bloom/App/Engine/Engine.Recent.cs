using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 최근 파일: what Windows lists as recently opened, with the preview pane; also the root's file results
/// while Everything is off, and a home block.
internal sealed partial class Engine
{
    public List<Section> RecentView(string text)
    {
        var q = new Query(text);
        var all = RecentFiles.List();
        var list = q.IsEmpty ? all : Score(all, f => new MatchTarget(Path.GetFileName(f.Path.TrimEnd('\\'))), q).ToList();
        var s = new Section(q.IsEmpty ? Tr("최근에 연 항목", "Recently Opened") : Tr("결과", "Results"), Tr($"{list.Count}개", $"{list.Count}"));
        foreach (var f in list.Take(100)) s.Items.Add(FromRecent(f));
        if (s.Items.Count == 0)
            s.Items.Add(new ResultItem { Id = "recent:none", Kind = ItemKind.Info, Title = Tr("최근 항목 없음", "No recent items"), Subtitle = Tr("최근 파일을 표시하려면 Windows 시작 설정에서 최근 항목 표시를 켜세요", "To see recent files, turn on “Show recently opened items” in Windows Start settings"), Icon = IconSpec.Of(G.Info), MoreActions = () => new List<ActionItem> { new() { Title = Tr("Windows 시작 설정 열기", "Open Windows Start settings"), Glyph = G.Settings, Run = () => Launch.Open("ms-settings:personalization-start") } } });
        return new List<Section> { s };
    }

    /// The root without Everything: recent files whose name matches (at most 4) — files in the root switched off
    /// (설정 > 검색 > 파일도 함께 찾기) keeps these out too.
    private Section? RootRecent(Query q)
    {
        if (!Settings.Current.FilesInRoot || host.Everything.Running) return null;
        if (q.Lower.Length < 2) return null;
        var found = Score(RecentFiles.List(), f => new MatchTarget(Path.GetFileName(f.Path.TrimEnd('\\'))), q).Take(4).ToList();
        if (found.Count == 0) return null;
        var s = new Section(Tr("최근 파일", "Recent Files"));
        foreach (var f in found) s.Items.Add(FromRecent(f));
        return s;
    }

    private ResultItem FromRecent(RecentFile f)
    {
        var item = FromPath(f.Path, f.Folder);
        item.Accessory = Ago(f.Opened);
        return item;
    }
}
