using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 창 전환: the open windows, front to back, and the few that match in the root search.
internal sealed partial class Engine
{
    public List<Section> WindowsView(string text)
    {
        var q = new Query(text);
        var all = OpenWindows.List();
        var list = q.IsEmpty ? all : Score(all, w => w.Match, q).ToList();
        var s = new Section(Tr("열린 창", "Open Windows"), Tr($"{list.Count}개", $"{list.Count}"));
        foreach (var w in list) s.Items.Add(FromWindow(w));
        if (s.Items.Count == 0)
            s.Items.Add(new ResultItem { Id = "win:none", Kind = ItemKind.Info, Title = Tr("검색된 창 없음", "No matching windows"), Icon = IconSpec.Of(G.Info) });
        return new List<Section> { s };
    }

    /// The root search: windows whose title or app matches well (at most 3), under the results.
    private Section? RootWindows(Query q)
    {
        if (!Settings.Current.WindowsInRoot || q.Lower.Length < 2) return null;
        var found = Score(OpenWindows.List(), w => w.Match, q).Take(3).ToList();
        if (found.Count == 0) return null;
        var s = new Section(Tr("열린 창", "Open Windows"));
        foreach (var w in found) s.Items.Add(FromWindow(w));
        return s;
    }

    public ResultItem FromWindow(OpenWindow w)
    {
        IntPtr h = w.Handle;
        var item = new ResultItem
        {
            Id = "win:" + h.ToInt64(),
            Kind = ItemKind.Window,
            Title = w.Title,
            Subtitle = w.App == w.Title ? null : w.App,
            Accessory = w.Minimized ? Tr("최소화됨", "Minimized") : Tr("창", "Window"),
            Icon = w.Exe is not null ? IconSpec.Shell(w.Exe) : IconSpec.Of(G.NewWindow),
            Data = w,
            PrimaryLabel = Tr("전환", "Switch"),
            Run = () => OpenWindows.Switch(h),
        };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>
            {
                new() { Title = Tr("창 닫기", "Close window"), Keys = "Ctrl+W", Glyph = G.Close, StayOpen = true, Run = () => CloseWindow(h, w.Title) },
                new() { Title = Tr("최소화", "Minimize"), Glyph = G.Down, StayOpen = true, Run = () => { OpenWindows.Minimize(h); host.Window.Refresh(true); } },
                new() { Title = Tr("최대화해서 전환", "Maximize and switch"), Glyph = G.Up, Run = () => OpenWindows.Maximize(h) },
            };
            if (Features.On("layout")) list.AddRange(LayoutActions(h));
            if (w.Exe is { } exe)
            {
                list.Add(new ActionItem { Title = Tr("파일 위치 열기", "Open file location"), Keys = "Ctrl+Enter", Glyph = G.Folder, Run = () => Launch.Reveal(exe) });
                list.Add(new ActionItem { Title = Tr("제목 복사", "Copy title"), Keys = "Ctrl+Shift+C", Glyph = G.Copy, Run = () => Launch.Copy(w.Title) });
            }
            return list;
        };
        return item;
    }

    private void CloseWindow(IntPtr h, string title)
    {
        OpenWindows.Close(h);
        host.Window.ShowToast(Tr($"‘{Clip(title, 24)}’ 창 닫기 요청 완료", $"Requested to close ‘{Clip(title, 24)}’"));
        // the app may ask to save first; the list catches up a moment later
        _ = System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => host.Window.Dispatcher.BeginInvoke(() =>
        {
            OpenWindows.Invalidate();
            host.Window.Refresh(true);
        }), System.Threading.Tasks.TaskScheduler.Default);
    }
}
