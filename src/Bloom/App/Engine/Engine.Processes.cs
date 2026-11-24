using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 프로세스 종료: the session's programs by memory or CPU; Enter asks, then ends them.
internal sealed partial class Engine
{
    public bool ProcByMemory = true; // memory keeps the rows still; CPU reorders every sample

    public List<Section> ProcessesView(string text)
    {
        var p = host.Processes;
        var q = new Query(text);
        var list = p.Groups.Where(g => q.IsEmpty || Matcher.Score(q, g.Match) > 0);
        list = ProcByMemory || !p.Measured ? list.OrderByDescending(g => g.Memory) : list.OrderByDescending(g => Math.Round(g.Cpu, 1)).ThenByDescending(g => g.Memory);
        var items = list.ToList();
        var s = new Section(Tr("프로그램", "Programs"), Tr($"{items.Count}개 · {(ProcByMemory || !p.Measured ? "메모리" : "CPU")} 순 · 1.5초마다 갱신", $"{items.Count} · by {(ProcByMemory || !p.Measured ? "memory" : "CPU")} · every 1.5 s"));
        foreach (var g in items) s.Items.Add(FromProc(g));
        return new List<Section> { s };
    }

    private ResultItem FromProc(ProcGroup g)
    {
        int n = g.Pids.Count;
        bool locked = g.Protected || g.Self;
        var item = new ResultItem
        {
            Id = "proc:" + g.Key.ToLowerInvariant(),
            Kind = ItemKind.Process,
            Title = g.Name,
            Subtitle = g.Self ? Tr("Bloom · 현재 앱", "Bloom · Current app") : g.Protected ? Tr($"{g.Exe} · Windows에 필요해서 끌 수 없습니다", $"{g.Exe} · Required by Windows and cannot be closed") : Join(g.Exe, n > 1 ? Tr($"프로세스 {n}개", $"{n} processes") : null),
            Accessory = (g.Cpu < 0 ? "CPU …" : $"CPU {g.Cpu:0.0}%") + " · " + Size(g.Memory),
            Icon = g.Path is { } path ? IconSpec.Shell(path) : IconSpec.Of(G.App),
            Data = g,
            PrimaryLabel = locked ? "" : Tr("강제 종료", "Force Quit"),
            StayOpen = true,
        };
        if (locked) return item;
        string what = Tr($"‘{g.Name}’{(n > 1 ? $" 프로세스 {n}개" : "")}", $"‘{g.Name}’{(n > 1 ? $" ({n} processes)" : "")}");
        item.Run = () => host.Window.Confirm(Tr($"{what}: 강제 종료 · 저장하지 않은 내용 삭제", $"{what}: Force quit · Unsaved changes will be lost"), new ActionItem
        {
            Title = Tr("강제 종료", "Force Quit"),
            Keys = "Enter",
            Glyph = G.Close,
            Danger = true,
            StayOpen = true,
            Run = () => host.EndProcess(g, true),
        });
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>
            {
                new() { Title = Tr("창 닫기로 끝내기 (저장할지 확인 창이 표시됩니다)", "Close window normally (prompts to save)"), Keys = "Ctrl+Enter", Glyph = G.Close, StayOpen = true, Run = () => host.EndProcess(g, false) },
            };
            if (g.Exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                list.Insert(0, new ActionItem { Title = Tr("탐색기 다시 시작", "Restart File Explorer"), Glyph = G.Restart, StayOpen = true, Run = () => host.RestartExplorer(g) });
            if (g.Path is { } p) list.Add(new ActionItem { Title = Tr("파일 위치 열기", "Open file location"), Glyph = G.Folder, Run = () => Launch.Reveal(p) });
            list.Add(new ActionItem
            {
                Title = ProcByMemory ? Tr("CPU 순으로 보기", "Sort by CPU") : Tr("메모리 순으로 보기", "Sort by memory"),
                Keys = "Ctrl+S",
                Glyph = G.Refresh,
                StayOpen = true,
                Run = () =>
                {
                    ProcByMemory = !ProcByMemory;
                    host.Window.Refresh(false);
                },
            });
            return list;
        };
        return item;
    }
}
