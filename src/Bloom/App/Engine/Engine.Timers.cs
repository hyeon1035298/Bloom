using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Timers and reminders, and the colour picker's view.
internal sealed partial class Engine
{
    // ── timers ──────────────────────────────────────────────────────────────────────────────────
    /// "10분 타이머 시작" / "15:30 알림: 회의" for a line the parser understood.
    public ResultItem StartTimerItem(TimerAsk ask, bool stay)
    {
        bool timer = ask.Kind == "timer";
        string title = timer
            ? ask.Label.EndsWith(Tr("타이머", "Timer"), StringComparison.OrdinalIgnoreCase) ? Tr($"{ask.Label} 시작", $"Start {ask.Label}") : Tr($"{Timers.DurationText(ask.Length ?? TimeSpan.Zero)} 타이머 시작 · {ask.Label}", $"Start {Timers.DurationText(ask.Length ?? TimeSpan.Zero)} timer · {ask.Label}")
            : ask.Length is { } len ? Tr($"{Timers.DurationText(len)} 뒤 알림 · {ask.Label}", $"Remind in {Timers.DurationText(len)} · {ask.Label}") : Tr($"{Timers.WhenText(ask.Due)} 알림 · {ask.Label}", $"Remind at {Timers.WhenText(ask.Due)} · {ask.Label}");
        return new ResultItem
        {
            Id = "timer:start",
            Kind = ItemKind.Command,
            Title = title,
            Subtitle = timer ? Tr($"{Timers.WhenText(ask.Due)} 종료", $"Ends at {Timers.WhenText(ask.Due)}") : ask.Length is null ? Tr($"{Timers.LeftText(ask.Due - DateTime.Now)} 남음", $"{Timers.LeftText(ask.Due - DateTime.Now)} left") : Tr($"{Timers.WhenText(ask.Due)} 알림", $"Reminder at {Timers.WhenText(ask.Due)}"),
            Icon = IconSpec.Of(timer ? G.Stopwatch : G.Calendar, true),
            PrimaryLabel = Tr("시작", "Start"),
            StayOpen = stay,
            Run = () =>
            {
                // a length counts from the moment Enter is pressed, not from when the line was typed
                var due = ask.Length is { } l ? DateTime.Now + l : ask.Due;
                Timers.Add(ask.Kind, ask.Label, due);
                if (stay) host.Window.Go(Mode.Timers);
                host.Window.ShowToast(timer ? Tr($"타이머 시작 · {Timers.WhenText(due)} 종료", $"Timer started · Ends at {Timers.WhenText(due)}") : Tr($"{Timers.WhenText(due)} 알림 설정됨", $"Reminder set for {Timers.WhenText(due)}"));
            },
        };
    }

    public static ResultItem FromTimer(TimerEntry e) => new()
    {
        Id = "timer:" + e.Id,
        Kind = ItemKind.Command,
        Title = e.Label,
        Subtitle = (e.Kind == "timer" ? Tr($"타이머 · {Timers.WhenText(e.DueAt)}", $"Timer · {Timers.WhenText(e.DueAt)}") : Tr($"알림 · {Timers.WhenText(e.DueAt)}", $"Reminder · {Timers.WhenText(e.DueAt)}")),
        Live = () => Timers.LeftText(e.Left),
        Icon = IconSpec.Of(e.Kind == "timer" ? G.Stopwatch : G.Calendar, true),
        PrimaryLabel = Tr("5분 더", "+5 min"),
        StayOpen = true,
        Run = () => Timers.Extend(e.Id, TimeSpan.FromMinutes(5)),
        MoreActions = () => new List<ActionItem>
        {
            new() { Title = Tr("1분 더", "+1 min"), Glyph = G.Add, StayOpen = true, Run = () => Timers.Extend(e.Id, TimeSpan.FromMinutes(1)) },
            new() { Title = Tr("취소", "Cancel"), Glyph = G.Delete, Danger = true, StayOpen = true, Run = () => Timers.Remove(e.Id) },
        },
    };

    public List<Section> TimersView(string text)
    {
        var sections = new List<Section>();
        string t = text.Trim();
        if (t.Length > 0)
        {
            // in this view a bare length or time is enough: "10분", "15:30 회의" (as in the 타이머 · 알림 tool)
            var ask = TimeParse.Read(t, timerField: true);
            var s = new Section(Tr("새로 시작", "Start New"));
            if (ask is not null) s.Items.Add(StartTimerItem(ask, true));
            else s.Items.Add(new ResultItem { Id = "timer:help", Kind = ItemKind.Info, Title = Tr("시간 인식 실패 · ‘10분’ 또는 ‘오후 3시 회의’처럼 입력하세요", "Could not recognize time · Try ‘10m’ or ‘3pm meeting’"), Subtitle = Tr("예: 10분 · 1시간 30분 · 15:30 회의 · 내일 9시 업로드", "e.g. 10m · 1h 30m · 15:30 meeting · tomorrow 9am upload"), Icon = IconSpec.Of(G.Info), PrimaryLabel = "" });
            sections.Add(s);
        }
        else
        {
            var quick = new Section(Tr("바로 시작", "Quick Start"), Tr("또는 \"25분 집중\", \"6시 퇴근 알림\"처럼 적기", "Or type e.g. \"25m focus\" or \"6pm leave work\""));
            foreach (int m in new[] { 5, 10, 25, 60 })
                quick.Items.Add(StartTimerItem(new TimerAsk("timer", TimeSpan.FromMinutes(m), DateTime.Now.AddMinutes(m), Tr($"{Timers.DurationText(TimeSpan.FromMinutes(m))} 타이머", $"{Timers.DurationText(TimeSpan.FromMinutes(m))} timer")), true));
            for (int i = 0; i < quick.Items.Count; i++) quick.Items[i].Id = "timer:quick" + i;
            sections.Add(quick);
        }
        var waiting = new Section(Tr("진행 중", "In progress"), Tr("Enter 5분 더 · Ctrl+K 취소", "Enter +5 min · Ctrl+K Cancel"));
        foreach (var e in Timers.Waiting) waiting.Items.Add(FromTimer(e));
        if (waiting.Items.Count > 0) sections.Insert(t.Length > 0 ? 1 : 0, waiting);
        return sections;
    }

    // ── colours ─────────────────────────────────────────────────────────────────────────────────
    private static Search.Detail ColorDetail(Color c)
    {
        var d = new Search.Detail { Fill = new SolidColorBrush(c) };
        d.Add("HEX", ColorBook.Hex(c)).Add("RGB", ColorBook.Rgb(c)).Add("HSL", ColorBook.Hsl(c)).Add(Tr("이름", "Name"), ColorBook.Name(c));
        d.Add(Tr("흰 글자 대비", "Contrast on white text"), $"{ColorBook.Contrast(c, System.Windows.Media.Colors.White):0.0}:1").Add(Tr("검은 글자 대비", "Contrast on black text"), $"{ColorBook.Contrast(c, System.Windows.Media.Colors.Black):0.0}:1");
        return d;
    }

    public ResultItem FromColor(Color c, string id, string? title = null)
    {
        string hex = ColorBook.Hex(c);
        return new ResultItem
        {
            Id = id,
            Kind = ItemKind.Command,
            Title = title ?? hex,
            Subtitle = ColorBook.Name(c),
            Icon = IconSpec.Swatch(new SolidColorBrush(c)),
            PrimaryLabel = Tr("HEX 복사", "Copy HEX"),
            Run = () => Search.Launch.Copy(hex),
            Detail = () => ColorDetail(c),
            MoreActions = () => new List<ActionItem>
            {
                new() { Title = Tr("RGB 복사", "Copy RGB"), Glyph = G.Copy, Run = () => Search.Launch.Copy(ColorBook.Rgb(c)) },
                new() { Title = Tr("HSL 복사", "Copy HSL"), Glyph = G.Copy, Run = () => Search.Launch.Copy(ColorBook.Hsl(c)) },
                new() { Title = Tr("이 색으로 그라데이션 보기", "View gradients with this color"), Glyph = G.Palette, StayOpen = true, Run = () => host.ShowColor(c) },
            },
        };
    }

    private ResultItem GradientItem(string id, string name, string about, Color[] stops)
    {
        string css = ColorBook.Css(stops);
        string hexes = string.Join(" → ", stops.Select(ColorBook.Hex));
        return new ResultItem
        {
            Id = id,
            Kind = ItemKind.Command,
            Title = name,
            Subtitle = about,
            Accessory = hexes,
            Icon = IconSpec.Swatch(ColorBook.Brush(stops)),
            PrimaryLabel = Tr("CSS 복사", "Copy CSS"),
            Run = () => Search.Launch.Copy(css),
            Detail = () =>
            {
                var d = new Search.Detail { Fill = ColorBook.Brush(stops) };
                for (int i = 0; i < stops.Length; i++) d.Add(stops.Length == 2 ? (i == 0 ? Tr("시작", "Start") : Tr("끝", "End")) : Tr($"{i + 1}번째", $"Stop {i + 1}"), ColorBook.Hex(stops[i]));
                return d.Add("CSS", css);
            },
            MoreActions = () =>
            {
                var list = new List<ActionItem> { new() { Title = Tr("색 코드만 복사", "Copy color codes only"), Glyph = G.Copy, Run = () => Search.Launch.Copy(string.Join(", ", stops.Select(ColorBook.Hex))) } };
                foreach (var c in stops.Distinct())
                    list.Add(new() { Title = Tr($"{ColorBook.Hex(c)}에서 다시 추천", $"More suggestions from {ColorBook.Hex(c)}"), Glyph = G.Palette, StayOpen = true, Run = () => host.ShowColor(c) });
                return list;
            },
        };
    }

    public List<Section> ColorView(string text)
    {
        var typed = ColorBook.Parse(text);
        var pickItem = new ResultItem { Id = "color:pick", Kind = ItemKind.Command, Title = Tr("화면에서 색 찍기", "Pick color from screen"), Subtitle = Tr("돋보기로 한 픽셀까지 · 방향키로 미세 이동", "Pixel-accurate magnifier · Arrow keys to nudge"), Icon = IconSpec.Of(G.Eyedrop, true), PrimaryLabel = Tr("찍기", "Pick"), Run = () => host.StartColor() };
        if ((typed ?? ColorBook.Now) is not { } c)
        {
            var empty = new Section(Tr("컬러 피커", "Color Picker"));
            empty.Items.Add(pickItem);
            empty.Items.Add(new ResultItem { Id = "color:help", Kind = ItemKind.Info, Title = Tr("색 코드 입력 가능", "Enter a color code"), Subtitle = "#ff5a8a · rgb(255, 90, 138) · hsl(342, 100%, 68%)", Icon = IconSpec.Of(G.Info), PrimaryLabel = "" });
            var none = new List<Section> { empty };
            if (SavedPalettes(null, 4) is { } palettes) none.Add(palettes);
            return none;
        }
        var sections = new List<Section>();
        var now = new Section(typed is null ? Tr("찍은 색", "Picked Color") : Tr("입력한 색", "Entered Color"), ColorBook.Name(c));
        string key = ColorBook.Hex(c);
        now.Items.Add(FromColor(c, "color:hex:" + key));
        var rgb = FromColor(c, "color:rgb:" + key, ColorBook.Rgb(c));
        rgb.PrimaryLabel = Tr("RGB 복사", "Copy RGB");
        rgb.Run = () => Search.Launch.Copy(ColorBook.Rgb(c));
        now.Items.Add(rgb);
        var hsl = FromColor(c, "color:hsl:" + key, ColorBook.Hsl(c));
        hsl.PrimaryLabel = Tr("HSL 복사", "Copy HSL");
        hsl.Run = () => Search.Launch.Copy(ColorBook.Hsl(c));
        now.Items.Add(hsl);
        if (typed is not null)
        {
            var keep = FromColor(c, "color:keep:" + key, Tr("최근 색에 추가", "Add to recent colors"));
            keep.Icon = IconSpec.Of(G.Add);
            keep.PrimaryLabel = Tr("추가", "Add");
            keep.StayOpen = true;
            keep.Run = () => host.ShowColor(c);
            now.Items.Add(keep);
        }
        else now.Items.Add(pickItem);
        sections.Add(now);
        sections.AddRange(Suggestions(c));

        var recent = new Section(Tr("최근 색", "Recent Colors"), Tr("Enter HEX 복사", "Enter to copy HEX"));
        foreach (string hex in ColorBook.Current.Recent.Skip(typed is null ? 1 : 0).Take(8))
            if (ColorBook.Parse(hex) is { } rc)
            {
                var item = FromColor(rc, "recent:" + hex);
                recent.Items.Add(item);
            }
        if (recent.Items.Count > 0) sections.Add(recent);
        if (SavedPalettes(null, 4) is { } kept) sections.Add(kept);
        return sections;
    }

    /// The gradients made from a colour and Bloom's presets near it: the colour view's, and the palette view's for the
    /// swatch picked.
    private List<Section> Suggestions(Color c)
    {
        string key = ColorBook.Hex(c);
        var grads = new Section(Tr("그라데이션 추천", "Suggested Gradients"), Tr("Enter CSS 복사", "Enter to copy CSS"));
        int n = 0;
        foreach (var (name, about, stops) in ColorBook.Suggest(c)) grads.Items.Add(GradientItem($"grad:{key}:{n++}", name, about, stops));
        var sections = new List<Section> { grads };
        var named = new Section(Tr("어울리는 프리셋", "Matching Presets"));
        foreach (var (name, a, b) in ColorBook.PresetsNear(c, 4)) named.Items.Add(GradientItem("preset:" + name, name, Tr("Bloom 프리셋", "Bloom preset"), new[] { a, b }));
        if (named.Items.Count > 0) sections.Add(named);
        return sections;
    }

    /// A colour typed on the search line: "#ff5a8a" → its swatch, Enter opens the colour view.
    private Section? TypedColor(string text)
    {
        if (ColorBook.Parse(text) is not { } c) return null;
        var item = FromColor(c, "color:typed:" + ColorBook.Hex(c));
        item.Subtitle = $"{ColorBook.Name(c)} · {ColorBook.Rgb(c)}";
        item.PrimaryLabel = Tr("색 보기", "View color");
        item.StayOpen = true;
        item.Run = () => host.ShowColor(c);
        var s = new Section(Tr("색", "Color"));
        s.Items.Add(item);
        return s;
    }
}
