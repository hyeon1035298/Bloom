using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Search;

namespace Bloom.Core;

/// One block of the empty view (the home): which, whether it shows, and how (홈 편집).
internal sealed class HomeWidget
{
    public string Key { get; set; } = "";
    public bool On { get; set; } = true;
    public bool? Right { get; set; } // its column on a wide home: false left, true right; null in a list kept before columns (Home.Order picks)
    public int? Count { get; set; } // a list block's rows (Home.Count); null = its kind's
    public bool Collapsed { get; set; } // only its header shows
    public string? Period { get; set; } // a token block's span (Ai\TokenPeriod's name); null = the last 24 hours
}

/// The home (the launcher with an empty search line) as blocks the user turns on and orders
/// (설정 > 홈 화면, or Ctrl+K "홈에 고정" on the calendar / clipboard / AI chat commands). On a wide launcher the blocks
/// stand in two columns (Ui\Controls\HomeColumns.cs); the list keeps the left column's blocks first, so in one column
/// they stack in its order. `count`: a list block's rows by default (0: not a list); `right`: the column it starts in.
internal static class Home
{
    public static readonly (string key, string name, string about, char glyph, bool on, int count, bool right)[] Kinds =
    {
        ("usage", Tr("AI 사용량", "AI usage"), Tr("Claude Code · Codex에 물어본 5시간 · 주간 사용량", "5-hour and weekly usage, asked of Claude Code and Codex"), G.Model, true, 0, false),
        ("tokens", Tr("토큰 사용량", "Token usage"), Tr("API 환산액 · 토큰 · 호출 · 캐시 적중률 · 처음 켜면 기록 전체를 백그라운드에서 한 번 읽음 · 압축된 Codex 기록 제외", "API-priced cost, tokens, calls, cache hits · the first time on, all logs are read once in the background · compressed Codex logs left out"), G.Speed, false, 0, false),
        ("tokenchart", Tr("토큰 그래프", "Token chart"), Tr("시간 · 날짜별 토큰 막대그래프 · 처음 켜면 기록 전체를 백그라운드에서 한 번 읽음 · 압축된 Codex 기록 제외", "Tokens by hour or day as bars · the first time on, all logs are read once in the background · compressed Codex logs left out"), G.Clock, false, 0, false),
        ("tokenmodels", Tr("모델별 사용량", "Usage by model"), Tr("토큰을 많이 쓴 모델과 그 비율 · 처음 켜면 기록 전체를 백그라운드에서 한 번 읽음 · 압축된 Codex 기록 제외", "The models that used the most tokens, and their share · the first time on, all logs are read once in the background · compressed Codex logs left out"), G.Sliders, false, 5, true),
        ("timers", Tr("타이머 · 알림", "Timers · reminders"), Tr("진행 중인 타이머와 알림 (있을 때만 표시)", "Running timers and reminders (only while there are some)"), G.Stopwatch, true, 3, false),
        ("week", Tr("이번 주", "This week"), Tr("오늘부터 7일의 알림 — 날짜를 누르면 캘린더", "Reminders for the next 7 days — click a day for the calendar"), G.Calendar, true, 0, false),
        ("favorites", Tr("즐겨찾기", "Favorites"), Tr("아이콘 줄 · Ctrl+1…9", "A row of icons · Ctrl+1…9"), G.Star, true, 0, true),
        ("recent", Tr("최근 사용", "Recent"), Tr("최근에 연 앱 · 파일", "The apps and files you opened last"), G.History, true, 5, true),
        ("recentfiles", Tr("최근 파일", "Recent files"), Tr("Windows가 기억하는 최근에 연 파일", "Files Windows remembers you opened"), G.Document, false, 3, true),
        ("clipboard", Tr("최근 복사", "Recently copied"), Tr("클립보드 기록의 최근 항목", "The latest clipboard entries"), G.Paste, false, 3, true),
        ("threads", Tr("최근 AI 채팅", "Recent AI chats"), Tr("이어 가기 좋은 대화", "Chats to pick up again"), G.Chat, false, 3, true),
        ("commands", Tr("명령", "Commands"), Tr("Bloom의 기능 목록", "Bloom's features"), G.Settings, true, 0, true),
    };

    /// The rows a list block can be set to show (홈 편집's − n +).
    public const int CountMin = 1, CountMax = 10;

    /// The commands whose feature can sit on the home (Ctrl+K → 홈에 고정).
    public static string? ForCommand(string id) => id switch
    {
        "cmd:calendar" => "week",
        "cmd:clipboard" => "clipboard",
        "cmd:threads" => "threads",
        _ => null,
    };

    public static string Name(string key) => Kinds.FirstOrDefault(k => k.key == key).name ?? key;

    /// Every known block once, in the user's order, the left column's first (new kinds join with their defaults). A list
    /// kept before columns is split where it keeps its order (Split); a null in a hand-edited list goes.
    public static List<HomeWidget> Order()
    {
        var s = Settings.Current;
        if (s.Home is not { Count: > 0 })
            s.Home = Kinds.Select(k => new HomeWidget { Key = k.key, On = k.on, Right = k.right }).ToList();
        var known = Kinds.Select(k => k.key).ToHashSet();
        s.Home = s.Home.Where(w => w is not null && known.Contains(w.Key)).GroupBy(w => w.Key).Select(g => g.First()).ToList();
        if (s.Home.TrueForAll(w => w.Right is null)) Split(s.Home);
        foreach (var k in Kinds)
            if (!s.Home.Exists(w => w.Key == k.key)) s.Home.Add(new HomeWidget { Key = k.key, On = k.on, Right = k.right });
        foreach (var w in s.Home) w.Right ??= Kind(w.Key).right;
        Stack(s.Home);
        return s.Home;
    }

    private static (string key, string name, string about, char glyph, bool on, int count, bool right) Kind(string key) => Kinds.FirstOrDefault(k => k.key == key);

    /// A list from before columns: the left column is its first blocks, as many as put the fewest blocks in a column
    /// other than their kind's (the default order splits as the defaults do), so one column shows it as it was.
    private static void Split(List<HomeWidget> list)
    {
        int best = 0, fewest = int.MaxValue;
        for (int k = 0; k <= list.Count; k++)
        {
            int off = 0;
            for (int i = 0; i < list.Count; i++)
                if (Kind(list[i].Key).right != (i >= k)) off++;
            if (off < fewest) (fewest, best) = (off, k);
        }
        for (int i = 0; i < list.Count; i++) list[i].Right = i >= best;
    }

    /// The left column's blocks first, each column in its order.
    private static void Stack(List<HomeWidget> list)
    {
        var stacked = list.OrderBy(w => w.Right is true).ToList();
        list.Clear();
        list.AddRange(stacked);
    }

    public static HomeWidget? Of(string key) => Order().Find(w => w.Key == key);

    public static bool IsOn(string key) => Of(key)?.On ?? false;

    public static void Set(string key, bool on)
    {
        if (Of(key) is not { } w) return;
        w.On = on;
        Settings.Save();
    }

    /// The rows a list block shows: its own count within CountMin…CountMax, else its kind's; 0 for a block of no rows.
    public static int Count(HomeWidget w) => Kind(w.Key).count is > 0 and var kind ? Math.Clamp(w.Count ?? kind, CountMin, CountMax) : 0;

    public static void SetCount(string key, int count)
    {
        if (Of(key) is not { } w || Count(w) == 0) return;
        w.Count = Math.Clamp(count, CountMin, CountMax);
        Settings.Save();
    }

    public static void SetCollapsed(string key, bool collapsed)
    {
        if (Of(key) is not { } w) return;
        w.Collapsed = collapsed;
        Settings.Save();
    }

    /// A token block's span (Ai\TokenPeriod's name), kept as its chips set it.
    public static void SetPeriod(string key, string period)
    {
        if (Of(key) is not { } w) return;
        w.Period = period;
        Settings.Save();
    }

    /// The blocks' order, columns and switches in a word: the home's look hangs on it (Ui\Launcher\Launcher.Kept.cs).
    public static string Arrangement() => string.Join(",", Order().Select(w => w.Key + (w.On ? "" : "-") + (w.Right is true ? ">" : "<")));

    /// A block put down between two others (dragged, or moved a place): before `below`, else after `above` (null and
    /// null: an empty column's start). `right`: the column (two columns: the one it was put in); null when the home is
    /// one column, then a block between two of a column joins it and one between the columns keeps its own.
    public static void Drop(string key, string? above, string? below, bool? right)
    {
        var list = Order();
        if (list.Find(w => w.Key == key) is not { } w) return;
        var a = list.Find(x => x.Key == above && x != w);
        var b = list.Find(x => x.Key == below && x != w);
        bool column = right ?? (a?.Right is true || (b?.Right is not false && w.Right is true));
        list.Remove(w);
        int at = b is not null ? list.IndexOf(b)
            : a is not null ? list.IndexOf(a) + 1
            : column ? list.Count : list.FindIndex(x => x.Right is true) is >= 0 and var first ? first : list.Count;
        w.Right = column;
        list.Insert(at, w);
        Stack(list);
        Settings.Save();
    }

    /// The blocks 홈 편집's "+ 블록 추가" offers, in their order: those switched off (a block of a feature switched off in
    /// 설정 > 기능 would not show on).
    public static List<HomeWidget> Hidden() => Order().Where(w => !w.On && Features.Shows(w.Key)).ToList();

    /// "+ 블록 추가": a block on again at the end of a column (`right`; null: the home in one column, after its last block).
    public static void Add(string key, bool? right)
    {
        var rest = Order().Where(w => w.Key != key && (right is null || w.Right == right)).ToList();
        AddAt(key, rest.Count > 0 ? rest[^1].Key : null, null, right);
    }

    /// A block on again where its card from "+ 블록 추가" was dropped: between two others, as Drop puts it.
    public static void AddAt(string key, string? above, string? below, bool? right)
    {
        if (Of(key) is not { } w) return;
        w.On = true;
        Drop(key, above, below, right);
    }

    /// The blocks a move goes past, in order: those on the home (`shown`; null: every block, 설정's list), all of them
    /// in one column, the block's column's in two (`wide`).
    private static List<HomeWidget> Line(HomeWidget w, bool wide, IReadOnlyCollection<string>? shown) =>
        Order().Where(x => (shown is null || shown.Contains(x.Key) || x == w) && (!wide || x.Right == w.Right)).ToList();

    /// Whether a block can move a place up (-1) or down (+1) on the home as laid out (`wide`: two columns).
    public static bool CanStep(string key, int d, bool wide, IReadOnlyCollection<string>? shown)
    {
        if (Of(key) is not { } w) return false;
        var line = Line(w, wide, shown);
        int j = line.IndexOf(w) + d;
        return j >= 0 && j < line.Count;
    }

    /// One place up (-1) or down (+1) past the blocks as laid out (Line): in one column a block past its column's end
    /// joins the other column there.
    public static void Step(string key, int d, bool wide, IReadOnlyCollection<string>? shown)
    {
        if (!CanStep(key, d, wide, shown)) return;
        var w = Of(key)!;
        var line = Line(w, wide, shown);
        int gap = line.IndexOf(w) + d;
        line.Remove(w);
        Drop(key, gap > 0 ? line[gap - 1].Key : null, gap < line.Count ? line[gap].Key : null, wide ? w.Right is true : null);
    }

    /// ‹ ›: into the other column, at the same place among the blocks on the home.
    public static void MoveAcross(string key, IReadOnlyCollection<string>? shown)
    {
        if (Of(key) is not { } w) return;
        bool to = w.Right is not true;
        int i = Line(w, true, shown).IndexOf(w);
        var other = Order().Where(x => x.Right == to && (shown is null || shown.Contains(x.Key))).ToList();
        int gap = Math.Min(i, other.Count);
        Drop(key, gap > 0 ? other[gap - 1].Key : null, gap < other.Count ? other[gap].Key : null, to);
    }

    /// Dragged in the settings list: the block goes before what was at `before` (Count = at the end).
    public static void MoveTo(string key, int before)
    {
        var list = Order();
        int i = list.FindIndex(w => w.Key == key);
        if (i < 0) return;
        var rest = list.Where(w => w.Key != key).ToList();
        int gap = Math.Clamp(before > i ? before - 1 : before, 0, rest.Count);
        Drop(key, gap > 0 ? rest[gap - 1].Key : null, gap < rest.Count ? rest[gap].Key : null, null);
    }

    /// 설정's ▲ ▼: one place up (-1) or down (+1) in the whole list, hidden blocks too.
    public static void Move(string key, int d) => Step(key, d, false, null);
}
