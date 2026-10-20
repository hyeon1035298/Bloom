using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Browser;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App.Mcp;

/// One thing the AI can do through the "pc" tool, in one of the groups 설정 > PC 작업 switches (look | open | bloom |
/// files | clip). `Asks`: it changes something, so the chat asks the user first (the look-ups run straight away). `Says` is how
/// the approval card and the chat's record name a call.
internal sealed record PcAction(string Do, string Group, Func<string, string, Task<string>> Run, Func<string, string, string> Says)
{
    public bool Asks => Group != "look";
}

/// What Bloom does for the AI, as ONE MCP tool ("pc") whose `do` picks the action: look up apps, files, recent files,
/// windows, timers, notes and the user's saved commands; open things; run a launcher line ("볼륨 30", "yt lofi");
/// write in Bloom's own notes, start its timers and put events on its calendar (so "메모에 적어 줘" or "내일 3시 회의
/// 잡아 줘" land in Bloom, not in a file or another app); rename, move, copy or recycle a file; the clipboard. One short definition goes with every request of a
/// chat instead of one per action (measured on a session's first request: +400 tokens, against +1,520 for 15 tools and
/// +720 for six). Every call reaches Bloom as a question (--permission-prompt-tool stdio); Bloom lets the look-ups
/// through itself (Quiet) and asks about the rest.
internal sealed class BloomTools : IPcTools
{
    public const string Server = "bloom";
    public const string Prefix = "mcp__bloom__";
    public const string Name = "pc";
    private readonly Host host;
    private readonly Dispatcher ui;
    private readonly IReadOnlyList<PcAction> actions;

    public BloomTools(Host host, Dispatcher ui)
    {
        this.host = host;
        this.ui = ui;
        actions = Build();
    }

    /// The groups 설정 > PC 작업 switches, in the order the description tells them.
    public static readonly string[] Groups = { "look", "open", "bloom", "files", "clip" };

    /// What the description says of each group (with every group on, the wording the tool picking was tested with).
    private static readonly Dictionary<string, string> About = new()
    {
        ["look"] = "Look up: find_apps, find_files (Everything syntax: ext:pdf dm:today), find_recent, find_windows, find_timers (timers, reminders, events), find_notes, find_commands (q = words).",
        ["open"] = "run_line (q = what the user would type in Bloom's search box + Enter: volume 볼륨 30, dark mode, site shortcuts yt lofi), "
            + "open (q = app, path or URL), run_command (q = saved command, to = its input)",
        ["bloom"] = "Bloom's own notes, timers and calendar (for 메모, 타이머, 일정; never files or other apps): note (q = text, to = an existing note's title, else a new note), "
            + "timer (q = 10m, to = label), event (q = what, to = yyyy-MM-dd HH:mm)",
        ["files"] = "rename, move, copy, recycle (q = full path, to = new name or folder)",
        ["clip"] = "clip_read, clip_write (q = text)",
    };

    public static bool GroupOn(string group) => !Settings.Current.AiToolsOff.Contains(group);

    /// A provider's chats (claude | chatgpt | omp | api) get the tool: its switch is on, so is one of the groups at least, and
    /// 권한's PC 조작 is not 사용 안 함.
    public static bool OfferedTo(string provider) => (provider switch { "chatgpt" => Settings.Current.AiToolsGpt, "omp" => Settings.Current.AiToolsOmp, "api" => Settings.Current.AiToolsApi, _ => Settings.Current.AiTools })
        && Groups.Any(GroupOn) && ToolPermissions.Of(ToolPermissions.Pc) != ToolPermissions.Off;

    /// Asks the user in a conversation's chat before an action and says whether they allowed it (Host: Chats); for
    /// Codex runs, which don't ask on their own. (conversation id, tool, input JSON) → allowed.
    public Func<string, string, string, Task<bool>>? Approve;

    /// The tools/list entry: only the groups switched on (fewer of them, a shorter definition with every request).
    public object Definition
    {
        get
        {
            var does = Groups.Where(g => g != "look" && GroupOn(g)).Select(g => About[g]).ToList();
            string description = "Act on the user's PC." + (GroupOn("look") ? " " + About["look"] : "") + (does.Count > 0 ? " Do: " + string.Join(", ", does) + "." : "");
            return new
            {
                name = Name,
                description,
                inputSchema = new
                {
                    type = "object",
                    properties = new { @do = new { type = "string", @enum = actions.Where(x => GroupOn(x.Group)).Select(x => x.Do).ToArray() }, q = new { type = "string" }, to = new { type = "string" } },
                    required = new[] { "do" },
                },
            };
        }
    }

    /// For Codex's instructions: the tool and its actions in one line. Codex lists MCP tools only on demand, so without
    /// this the model first spends a request looking for one (measured: +55 tokens without, one round trip more).
    public string Hint => $"Bloom's PC tool is {Prefix}{Name} (server bloom, tool {Name}) taking {{do, q, to}}; do = "
        + string.Join(", ", actions.Where(x => GroupOn(x.Group)).Select(x => x.Do))
        + (GroupOn("open") ? ". run_line q = what the user would type in Bloom's search box (볼륨 30, yt lofi)" : "")
        + (GroupOn("bloom") ? ". note/timer/event = Bloom's notes, timers, calendar (for 메모, 타이머, 일정; never files or other apps; event to = yyyy-MM-dd HH:mm)" : "")
        + (GroupOn("files") ? ". rename/move/copy/recycle q = full path, to = new name or folder" : "") + ". Call it directly; no need to look it up. ";

    private static bool IsPc(string tool) => tool == Prefix + Name || tool == Name;

    private PcAction? ActionOf(JsonElement a) => actions.FirstOrDefault(x => x.Do == S(a, "do"));

    /// A look-up: Bloom answers the CLI's question itself, nothing shows in the chat. A file search that can read into
    /// files (Everything's content: and the like, EverythingQuery) is not one: it waits for the same approval as reading
    /// a file outside the chat's folder, in every chat.
    public bool Quiet(string tool, string input) => IsPc(tool) && Parse(input, a => ActionOf(a) is { Asks: false } x && !ReadsContents(x, a));

    /// A find_files whose query is more than plain text for names (EverythingQuery).
    private static bool ReadsContents(PcAction action, JsonElement a) => action.Do == "find_files" && !EverythingQuery.IsPlainName(S(a, "q"));

    /// What 이 대화에서 항상 허용 covers: one action of the pc tool ("pc:move"), or another tool by name; a content search
    /// is the file-read permission's (ToolPermissions.ReadOutside).
    public string Key(string tool, string input) => IsPc(tool)
        ? Parse(input, a => ActionOf(a) is { } x && ReadsContents(x, a) ? ToolPermissions.ReadOutside : "pc:" + S(a, "do"), "pc:")
        : tool;

    /// What the approval card shows whole (ToolText.Visible throughout): a pc action's every argument, the paths a file
    /// action works on resolved as it will resolve them (variables expanded); Claude's own file tools' paths and texts;
    /// any other tool's input.
    public string Detail(string tool, string input) => Parse(input, a => IsPc(tool) ? PcDetail(a) : OtherDetail(tool, a), "");

    private string PcDetail(JsonElement a)
    {
        string action = S(a, "do"), q = S(a, "q"), to = S(a, "to");
        var sb = new StringBuilder();
        sb.Append("do: ").Append(ToolText.Visible(action));
        if (q.Length > 0) sb.Append("\nq: ").Append(ToolText.Visible(q));
        if (to.Length > 0) sb.Append("\nto: ").Append(ToolText.Visible(to));
        switch (action)
        {
            case "rename" or "move" or "copy" or "recycle":
                string source = Resolved(q);
                sb.Append('\n').Append(Tr("원본", "Source")).Append(": ").Append(ToolText.Visible(source));
                if (action == "rename") sb.Append('\n').Append(Tr("바뀐 이름", "New name")).Append(": ").Append(ToolText.Visible(Path.Combine(Path.GetDirectoryName(source) ?? "", to.Trim())));
                else if (action != "recycle") sb.Append('\n').Append(Tr("대상", "Destination")).Append(": ").Append(ToolText.Visible(Path.Combine(Resolved(to), FileName(source))));
                break;
            case "open":
                string target = q.Trim().Trim('"');
                // lexically only: whether the path exists (a file or folder, or else an app by that name) is not looked at
                // before the user has said yes, as a network path would be reached by it
                string where = Web.AsUrl(target) ?? Tr($"{Resolved(target)} (그 위치에 없으면 앱 ‘{target}’)", $"{Resolved(target)} (or, if nothing is there, the app ‘{target}’)");
                sb.Append('\n').Append(Tr("열 대상", "Opens")).Append(": ").Append(ToolText.Visible(where));
                break;
        }
        // a path that is not on a local drive is said so (RealPath.Kind classes it from its text alone)
        foreach (string path in action is "rename" or "move" or "copy" or "recycle" ? new[] { Resolved(q), Resolved(to) } : action == "open" && Web.AsUrl(q.Trim().Trim('"')) is null ? new[] { Resolved(q) } : [])
            if (path.Length > 0 && RealPath.Kind(path) != PathKind.Local) { sb.Append('\n').Append(Tr("로컬 드라이브가 아닌 경로가 있습니다.", "A path is not on a local drive.")); break; }
        return sb.ToString();
    }

    private static string OtherDetail(string tool, JsonElement a)
    {
        string path = S(a, "file_path");
        return tool switch
        {
            "Write" => ToolText.Visible(path) + "\n" + ToolText.Block(Tr("쓸 내용", "Content"), S(a, "content")),
            "Edit" => ToolText.Visible(path) + "\n" + ToolText.Block(Tr("바꾸기 전", "Before"), S(a, "old_string")) + "\n" + ToolText.Block(Tr("바꾼 후", "After"), S(a, "new_string")),
            "Read" => ToolText.Visible(path),
            "Bash" => ToolText.Visible(S(a, "command")),
            _ => ToolText.Visible(JsonSerializer.Serialize(a, Pretty)),
        };
    }

    /// An unknown tool's input on the card: indented, non-ASCII text as it is.
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// A path as the file actions read it: variables expanded, quotes trimmed, made a full path.
    private static string Resolved(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }
    }

    /// The chat's status line while a Bloom call runs; null for other tools.
    public static string? Doing(string tool) => IsPc(tool) ? Tr("PC에서 작업하는 중…", "Working on the PC…") : null;

    /// How a tool call reads in the chat: Bloom's by their own wording, Claude's file and web tools by path or address,
    /// anything else by name.
    public string Says(string tool, string input) => Parse(input, a =>
        IsPc(tool) ? ActionOf(a) is { } x ? x.Says(S(a, "q"), S(a, "to")) : S(a, "do")
        : tool switch
        {
            // the ✓ line is short; the card's Detail has the whole path
            "Read" => Tr($"파일 읽기: {ToolText.Line(S(a, "file_path"))}", $"Read a file: {ToolText.Line(S(a, "file_path"))}"),
            "Write" or "Edit" => Tr($"파일 쓰기: {ToolText.Line(S(a, "file_path"))}", $"Write a file: {ToolText.Line(S(a, "file_path"))}"),
            "WebFetch" => Tr($"페이지 열기: {Clip(S(a, "url"))}", $"Open a page: {Clip(S(a, "url"))}"),
            // another MCP server's tool: "github · create_issue: {…}"
            _ when tool.StartsWith("mcp__", StringComparison.Ordinal) && tool.Split("__") is { Length: >= 3 } p =>
                $"{p[1]} · {string.Join("__", p[2..])}" + (a.ValueKind == JsonValueKind.Object && a.EnumerateObject().Any() ? ": " + Clip(a.GetRawText(), 70) : ""),
            _ => $"{tool}: {Clip(a.GetRawText(), 80)}",
        }, tool);

    /// Runs one call. `conversation` (a Codex run): an action that changes something waits for the user's yes in that
    /// conversation's chat first; Claude Code asked the user before the call came.
    public async Task<(string text, bool error)> Call(string tool, JsonElement args, string? conversation = null)
    {
        if (!IsPc(tool)) return ($"No tool named {tool}.", true);
        if (ActionOf(args) is not { } action) return ($"Unknown do: {S(args, "do")}.", true);
        // a session started before PC 조작 went to 사용 안 함 still has the tool: it does nothing now
        if (!GroupOn(action.Group) || ToolPermissions.Of(ToolPermissions.Pc) == ToolPermissions.Off) return ("The user switched this off in Bloom's settings.", true);
        if (conversation is not null && (action.Asks || ReadsContents(action, args)) && !(Approve is { } approve && await approve(conversation, Name, args.GetRawText())))
            return ("The user declined this. Don't try it again unless they ask.", true);
        try
        {
            return (await action.Run(S(args, "q"), S(args, "to")), false);
        }
        catch (Exception e) // whatever goes wrong is this call's error; the session keeps its tools
        {
            if (e is not ArgumentException) Log.Error("bloom tool " + action.Do, e);
            return (e.Message, true);
        }
    }

    private static T Parse<T>(string json, Func<JsonElement, T> read, T fallback = default!)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.Length > 0 ? json : "{}");
            return read(doc.RootElement);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string S(JsonElement a, string key) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";

    private static string Clip(string s, int max = 60) => s.Length > max ? s[..max] + "…" : s;

    private Task<T> OnUi<T>(Func<T> f) => ui.InvokeAsync(f).Task;

    private static string FileName(string path) => Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;

    /// " (C:\…\Downloads)": the folder a file is in, its end kept when the path is long.
    private static string InFolder(string path) => Path.GetDirectoryName(path.TrimEnd('\\')) is { Length: > 0 } d ? $" ({(d.Length > 48 ? "…" + d[^47..] : d)})" : "";

    private List<PcAction> Build() => new()
    {
        new("find_apps", "look", (q, _) => Look("apps", q), (q, _) => Tr($"앱 찾기: {q}", $"Find apps: {q}")),
        new("find_files", "look", (q, _) => Look("files", q), (q, _) => EverythingQuery.IsPlainName(q) ? Tr($"파일 찾기: {Clip(q, 80)}", $"Find files: {Clip(q, 80)}") : Tr($"파일 내용 검색: {Clip(q, 80)}", $"Search file contents: {Clip(q, 80)}")),
        new("find_recent", "look", (q, _) => Look("recent", q), (_, _) => Tr("최근 파일 보기", "Recent files")),
        new("find_windows", "look", (q, _) => Look("windows", q), (_, _) => Tr("열린 창 보기", "Open windows")),
        new("find_timers", "look", (q, _) => Look("timers", q), (_, _) => Tr("타이머 보기", "Timers")),
        new("find_commands", "look", (q, _) => Look("commands", q), (_, _) => Tr("사용자 명령 보기", "Custom commands")),
        new("find_notes", "look", (q, _) => OnUi(() => FindNotes(q)), (q, _) => Tr($"메모 찾기: {q}", $"Find notes: {q}").TrimEnd(' ', ':')),
        new("open", "open", (q, _) => OnUi(() => Open(q)).Unwrap(), (q, _) => Tr($"열기: {Clip(q)}", $"Open: {Clip(q)}")),
        new("run_line", "open", (q, _) => OnUi(() => RunLine(q)), (q, _) => Tr($"Bloom에서 실행: {Clip(q)}", $"Run in Bloom: {Clip(q)}")),
        new("run_command", "open", (q, to) => OnUi(() => RunCommand(q, to)), (q, to) => Tr($"사용자 명령 실행: {q} {Clip(to, 40)}", $"Run custom command: {q} {Clip(to, 40)}").Trim()),
        new("note", "bloom", (q, to) => OnUi(() => WriteNote(q, to)), (q, to) => to.Trim().Length > 0
            ? Tr($"메모에 적기: ‘{Clip(to.Trim(), 30)}’에 {Clip(Line(q), 40)}", $"Add to the note ‘{Clip(to.Trim(), 30)}’: {Clip(Line(q), 40)}")
            : Tr($"새 메모: {Clip(Line(q), 50)}", $"New note: {Clip(Line(q), 50)}")),
        new("timer", "bloom", (q, to) => OnUi(() => StartTimer(q, to)), (q, to) => Tr($"타이머: {q} {Clip(to, 30)}", $"Timer: {q} {Clip(to, 30)}").Trim()),
        new("event", "bloom", (q, to) => OnUi(() => AddEvent(q, to)), (q, to) => Tr($"일정: {to} {Clip(q, 40)}", $"Event: {to} {Clip(q, 40)}")),
        new("rename", "files", (q, to) => OnUi(() => FileAct("rename", q, to)), (q, to) => Tr($"이름 바꾸기: {FileName(q)} → {Clip(to)}", $"Rename: {FileName(q)} → {Clip(to)}")),
        new("move", "files", (q, to) => OnUi(() => FileAct("move", q, to)), (q, to) => Tr($"옮기기: {FileName(q)} → {Clip(to)}", $"Move: {FileName(q)} → {Clip(to)}")),
        new("copy", "files", (q, to) => OnUi(() => FileAct("copy", q, to)), (q, to) => Tr($"복사: {FileName(q)} → {Clip(to)}", $"Copy: {FileName(q)} → {Clip(to)}")),
        new("recycle", "files", (q, _) => OnUi(() => FileAct("recycle", q, "")), (q, _) => Tr($"휴지통으로: {FileName(q)}{InFolder(q)}", $"To the Recycle Bin: {FileName(q)}{InFolder(q)}")),
        new("clip_read", "clip", (_, _) => OnUi(() => host.ClipText() is { Length: > 0 } t ? t : "No text on the clipboard."), (_, _) => Tr("클립보드 읽기", "Read the clipboard")),
        new("clip_write", "clip", (q, _) => OnUi(() =>
        {
            Launch.Copy(q);
            return "Copied.";
        }), (q, _) => Tr($"클립보드에 넣기: {Clip(q.Replace('\n', ' '), 50)}", $"Copy to the clipboard: {Clip(q.Replace('\n', ' '), 50)}")),
    };

    /// A look-up: one item a line, at most 20 (files newest first); "Nothing found." rather than an empty answer.
    private async Task<string> Look(string kind, string query) => await Found(kind, query) is { Length: > 0 } list ? list : "Nothing found.";

    private async Task<string> Found(string kind, string query)
    {
        var q = new Query(query);
        bool Hit(MatchTarget m) => q.IsEmpty || Matcher.Score(q, m) > 0;
        switch (kind)
        {
            case "files":
                var list = await host.Everything.Query(query, 20, EverythingClient.SortDateModifiedDesc);
                if (list is null) return "Everything isn't running; try do=find_recent.";
                return list.Count == 0 ? "Nothing found." : string.Join("\n", list.Select(f => $"{f.FullPath}{(f.IsFolder ? "\\" : "")} {f.Modified:yyyy-MM-dd}"));
            case "recent":
                return string.Join("\n", (await RecentFiles.Refresh()).Take(60).Where(r => Hit(new MatchTarget(Path.GetFileName(r.Path)))).Take(20).Select(r => $"{r.Path} {r.Opened:yyyy-MM-dd}"));
            case "windows":
                return string.Join("\n", (await OpenWindows.Refresh()).Where(w => Hit(w.Match)).Take(20).Select(w => $"{w.Title} ({w.App})"));
            case "timers":
                return await OnUi(() => Timers.Waiting.Any() ? string.Join("\n", Timers.Waiting.Select(t => $"{t.Label} {t.DueAt:MM-dd HH:mm}")) : "None.");
            case "commands":
                return await OnUi(() => UserCommands.Current.Items.Count == 0 ? "None." : string.Join("\n", UserCommands.Current.Items.Where(c => Hit(c.Match)).Select(c => $"{c.Name} ({c.Kind}{(c.TakesInput ? ", takes input" : "")})")));
            default:
                return await OnUi(() =>
                {
                    var found = host.Apps.Apps.Select(x => (x, score: Matcher.Score(q, x.Match))).Where(x => x.score > 0).OrderByDescending(x => x.score).Take(10).Select(x => x.x.Name).ToList();
                    return found.Count == 0 ? "No app matches." : string.Join("\n", found);
                });
        }
    }

    private static string Line(string s) => s.Replace('\n', ' ').Trim();

    /// Bloom's notes matching the words (all of them when none), newest first: title, time, text (at most 20).
    private static string FindNotes(string query)
    {
        var q = new Query(query);
        var found = Notes.Ordered.Where(n => q.IsEmpty || Matcher.Score(q, n.Match) > 0).Take(20).ToList();
        return found.Count == 0 ? "No notes." : string.Join("\n\n", found.Select(n => $"# {n.Title} ({n.UpdatedAt:yyyy-MM-dd HH:mm})\n{Clip(n.Text, 600)}"));
    }

    /// Writes into Bloom's notes: added to the end of the note titled `title` (or the one that clearly matches it),
    /// else a new note (titled `title` when one was given); 메모 then shows it beside the chat.
    private string WriteNote(string text, string title)
    {
        if (!Features.On("notes")) throw new ArgumentException("Bloom's notes are switched off in its settings (설정 > 기능).");
        text = text.Trim();
        title = title.Trim();
        if (text.Length == 0) throw new ArgumentException("q must be the text to write.");
        var note = title.Length == 0 ? null
            : Notes.Ordered.FirstOrDefault(n => string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase))
              ?? Notes.Ordered.Select(n => (n, score: Matcher.Score(new Query(title), new MatchTarget(n.Title)))).Where(x => x.score >= 800).OrderByDescending(x => x.score).Select(x => x.n).FirstOrDefault();
        string done;
        if (note is null)
        {
            note = Notes.New(title.Length > 0 ? title + "\n" + text : text);
            done = $"Wrote a new note in Bloom (\u201c{note.Title}\u201d)";
        }
        else
        {
            Notes.SetText(note, note.Text.TrimEnd() + "\n" + text);
            done = $"Added it to Bloom's note \u201c{note.Title}\u201d";
        }
        host.Tools.ShowBeside("notes", note.Id, host.ChatWindowHandle);
        return done + "; it shows beside the chat.";
    }

    /// Starts one of Bloom's timers: q = its length ("10m", "25분", "1h 30m"), to = what it is for; 타이머 · 알림
    /// then shows beside the chat.
    private string StartTimer(string length, string label)
    {
        if (!Features.On("timers")) throw new ArgumentException("Bloom's timers are switched off in its settings (설정 > 기능).");
        if (TimeParse.Read(length.Trim(), timerField: true) is not { Length: { } span } ask) throw new ArgumentException("q must be a length like 10m, 25분 or 1h 30m.");
        var due = DateTime.Now + span;
        var t = Timers.Add("timer", label.Trim().Length > 0 ? label.Trim() : ask.Label, due);
        host.Tools.ShowBeside("timers", null, host.ChatWindowHandle);
        return $"Started a {Timers.DurationText(span)} timer in Bloom (\u201c{t.Label}\u201d); it rings at {due:HH:mm}.";
    }

    private static readonly string[] EventTimes = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" };

    /// Puts an event on Bloom's calendar (a reminder at that moment): q = what, to = "yyyy-MM-dd HH:mm" (a date alone is
    /// 9:00; words Bloom reads, like "오후 3시", work too); 타이머 · 알림, which lists it, then shows beside the chat.
    private string AddEvent(string what, string when)
    {
        if (!Features.On("timers")) throw new ArgumentException("Bloom's timers and calendar are switched off in its settings (설정 > 기능).");
        what = what.Trim();
        when = when.Trim();
        if (what.Length == 0) throw new ArgumentException("q must say what the event is.");
        DateTime due;
        if (DateTime.TryParseExact(when, EventTimes, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) due = at;
        else if (DateTime.TryParseExact(when, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var day)) due = day.AddHours(9);
        else if (TimeParse.Read(when, timerField: true) is { } ask) due = ask.Length is { } l ? DateTime.Now + l : ask.Due;
        else throw new ArgumentException("to must be a time as yyyy-MM-dd HH:mm.");
        if (due <= DateTime.Now) throw new ArgumentException("That time has already passed; ask the user when they mean.");
        Timers.Add("reminder", what, due);
        host.Tools.ShowBeside("timers", null, host.ChatWindowHandle);
        return $"Put \u201c{what}\u201d on Bloom's calendar for {due.ToString("yyyy-MM-dd (ddd) HH:mm", CultureInfo.InvariantCulture)}; Bloom reminds the user then.";
    }

    private string RunCommand(string name, string input)
    {
        var c = UserCommands.Current.Items.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Keyword, name, StringComparison.OrdinalIgnoreCase));
        if (c is null) return $"No saved command named {name}.";
        host.RunMine(c, input);
        return c.Kind == "script" ? $"Started {c.Name}; its result comes to the user as a notice." : $"Ran {c.Name}.";
    }

    private static string FileAct(string action, string path, string to)
    {
        path = Existing(path);
        switch (action)
        {
            case "rename":
                string name = to.Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("to must be a valid new name.");
                return ShellFiles.Rename(path, name, IntPtr.Zero) ? $"Renamed to {name}." : "Not renamed (in use, no permission, or cancelled).";
            case "move":
                return ShellFiles.MoveTo(path, Folder(to), IntPtr.Zero) ? "Moved." : "Not moved (in use, no permission, or cancelled).";
            case "copy":
                return ShellFiles.CopyTo(path, Folder(to), IntPtr.Zero) ? "Copied." : "Not copied (no permission, or cancelled).";
            default:
                return ShellFiles.Recycle(path, IntPtr.Zero) ? "Moved to the Recycle Bin." : "Not recycled (in use or no permission).";
        }
    }

    private static string Existing(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return File.Exists(path) || Directory.Exists(path) ? path : throw new ArgumentException($"There is no file or folder at {path}.");
    }

    private static string Folder(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Directory.Exists(path) ? path : throw new ArgumentException($"There is no folder at {path}.");
    }

    private async Task<string> Open(string target)
    {
        target = target.Trim().Trim('"');
        if (Web.AsUrl(target) is { } url)
        {
            Web.Open(url);
            return $"Opened {url} in the browser.";
        }
        string path = Environment.ExpandEnvironmentVariables(target);
        if (File.Exists(path) || Directory.Exists(path)) return await Launch.Start(path) ? $"Opened {path}." : $"Couldn't open {path}.";
        var q = new Query(target);
        var app = host.Apps.Apps.Select(x => (x, score: Matcher.Score(q, x.Match))).Where(x => x.score > 0).OrderByDescending(x => x.score).Select(x => x.x).FirstOrDefault();
        if (app is null) return $"No app, file or address matches \"{target}\".";
        host.Engine.FromApp(app).Run?.Invoke();
        return $"Opened {app.Name}.";
    }

    private string RunLine(string line)
    {
        if (host.Engine.StepAction(line, 0) is not { } run) return $"Nothing in Bloom answers \"{line}\".";
        string what = host.Engine.StepTitle(line) ?? line;
        run();
        return $"Ran: {what}.";
    }
}
