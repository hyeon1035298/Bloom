using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// 창 배치: the window in front onto a half, a third, a quarter of its screen, the middle or the next screen. Found by
/// name in the root search ("왼쪽 절반", "left half"), listed in their own view, pinnable and given shortcuts like any
/// favorite (Ctrl+K › 단축키 지정).
internal sealed partial class Engine
{
    public List<Section> LayoutView(string text)
    {
        var q = new Query(text);
        var target = LayoutTarget();
        var s = new Section(Tr("창 배치", "Window Layout"), target != IntPtr.Zero ? Clip(WindowTitle(target), 40) : Demo.On ? null : Tr("배치할 창 없음", "No window to arrange"));
        foreach (var l in WindowLayout.All)
            if (q.IsEmpty || Matcher.Score(q, l.Match) >= 560) s.Items.Add(LayoutItem(l, target));
        if (s.Items.Count == 0)
            s.Items.Add(new ResultItem { Id = "layout:none", Kind = ItemKind.Info, Title = Tr("검색된 배치 없음", "No matching layout"), Icon = IconSpec.Of(G.Info) });
        return new List<Section> { s };
    }

    /// The root search: the layouts whose name matches, scored like commands.
    internal IEnumerable<ResultItem> RootLayouts(Query q)
    {
        IntPtr? target = null;
        foreach (var l in WindowLayout.All)
        {
            int sc = Matcher.Score(q, l.Match);
            if (sc < 560) continue;
            var item = LayoutItem(l, target ??= LayoutTarget());
            item.Score = sc + Usage.Boost(l.Id);
            yield return item;
        }
    }

    public ResultItem? LayoutItem(string id) => WindowLayout.Find(id) is { } l ? LayoutItem(l, LayoutTarget()) : null;

    private ResultItem LayoutItem(Layout l, IntPtr target) => new()
    {
        Id = l.Id,
        Kind = ItemKind.Command,
        Title = Tr(l.Ko, l.En),
        Subtitle = target == IntPtr.Zero ? Tr("앞의 창 이동", "Move active window") : Clip(WindowTitle(target), 48),
        Accessory = Tr("창 배치", "Window Layout"),
        Icon = l.Glyph != '\0' ? IconSpec.Of(l.Glyph) : IconSpec.Share(l.X, l.Y, l.W, l.H),
        PrimaryLabel = Tr("배치", "Arrange"),
        AsFavorite = new Favorites.Entry { Id = l.Id, Kind = "layout", Title = Tr(l.Ko, l.En) },
        Run = () => PlaceWindow(l),
    };

    /// From the launcher (it has gone by now) or a shortcut: the window in front goes there.
    public void PlaceWindow(Layout l)
    {
        Usage.Touch(l.Id);
        IntPtr h = LayoutTarget();
        if (h == IntPtr.Zero || !WindowLayout.Apply(l, h))
            host.Window.ShowToast(l.Kind == "next" && h != IntPtr.Zero ? Tr("연결된 화면 1개", "1 display connected") : Tr("배치할 창 없음", "No window to arrange"));
        OpenWindows.Invalidate();
    }

    private IntPtr LayoutTarget() => Demo.On ? IntPtr.Zero : WindowLayout.Target(host.Clipboard?.LastForeground ?? IntPtr.Zero);

    private static string WindowTitle(IntPtr h)
    {
        int n = Win32.GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new System.Text.StringBuilder(n + 1);
        _ = Win32.GetWindowText(h, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    /// 창 전환's Ctrl+K: the few layouts people reach for most, for that window.
    private static IEnumerable<ActionItem> LayoutActions(IntPtr h) =>
        new[] { "layout:left", "layout:right", "layout:maximize", "layout:next-screen" }
            .Select(WindowLayout.Find)
            .OfType<Layout>()
            .Select(l => new ActionItem { Title = Tr(l.Ko, l.En), Glyph = l.Glyph != '\0' ? l.Glyph : '', Run = () => WindowLayout.Apply(l, h) });
}
