using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 내 명령: scripts, snippets and chains as results (by name, or keyword + space + input), their view, and the small
/// form in the search line that makes and edits them. Host.Mine runs them.
internal sealed partial class Engine
{
    private static readonly string[] KindsOrder = { "ai", "script", "snippet", "chain" };

    private static string KindName(string kind) => kind switch
    {
        "snippet" => Tr("스니펫", "Snippet"),
        "chain" => Tr("묶음", "Chain"),
        "ai" => Tr("AI 명령", "AI command"),
        _ => Tr("스크립트", "Script"),
    };

    private static char KindGlyph(string kind) => kind switch
    {
        "snippet" => G.Paste,
        "chain" => G.Bolt,
        "ai" => G.Sparkle,
        _ => G.Code,
    };

    public ResultItem FromCommand(UserCommand c, string? input)
    {
        string id = "my:" + c.Id;
        bool needsInput = c.TakesInput && string.IsNullOrWhiteSpace(input);
        bool running = host.IsRunning(c);
        string what = c.Kind switch
        {
            "snippet" => UserCommands.SnippetText(c.Body).Replace('\n', ' '),
            "chain" => string.Join("  →  ", UserCommands.Steps(c.Body)),
            "ai" => AiInput.Name(c.Input) + (c.Every is { } every ? " · " + AiSchedule.Name(every) : "") + " · " + string.Join("  →  ", AiCommandRun.Split(c.Body)),
            _ => c.Body.Replace(Paths.Home, "~", StringComparison.OrdinalIgnoreCase),
        };
        var item = new ResultItem
        {
            Id = id,
            Kind = ItemKind.Command,
            Title = input is { Length: > 0 } && !needsInput ? $"{c.Name} — “{input.Trim()}”" : c.Name,
            Subtitle = running ? Tr("실행 중… · Ctrl+K에서 멈추기", "Running… · stop it from Ctrl+K")
                : needsInput && c.Keyword is { Length: > 0 } k ? Tr($"{k} + 입력 · {what}", $"{k} + input · {what}") : what,
            Accessory = KindName(c.Kind),
            Icon = IconSpec.Of(KindGlyph(c.Kind), running),
            Data = c,
            PrimaryLabel = needsInput ? Tr("입력 넣기", "Type the input") : c.Kind switch { "snippet" => Tr("붙여넣기", "Paste"), "chain" => Tr("차례로 실행", "Run in turn"), _ => Tr("실행", "Run") },
            StayOpen = needsInput,
        };
        item.Run = needsInput
            ? () => host.OpenView(Mode.Root, (c.Keyword is { Length: > 0 } kw ? kw : c.Name) + " ")
            : () =>
            {
                Usage.Touch(id);
                host.RunMine(c, input ?? "");
            };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>();
            if (!needsInput && c.Kind == "script")
            {
                list.Add(new ActionItem { Title = c.ShowWindow ? Tr("창 없이 실행", "Run without a window") : Tr("창에서 실행", "Run in a window"), Keys = "Ctrl+Enter", Glyph = G.NewWindow, Run = () => host.RunMine(c, input ?? "", show: !c.ShowWindow) });
                list.Add(new ActionItem { Title = Tr("관리자 권한으로 실행", "Run as administrator"), Keys = "Ctrl+Shift+Enter", Glyph = G.Shield, Run = () => host.RunMine(c, input ?? "", admin: true) });
            }
            if (running) list.Insert(0, new ActionItem { Title = Tr("중지", "Stop"), Glyph = G.Stop, Danger = true, StayOpen = true, Run = () => host.StopMine(c) });
            if (host.LastOutput(c) is { Length: > 0 } output) list.Add(new ActionItem { Title = Tr("마지막 출력 복사", "Copy the last output"), Glyph = G.Copy, Run = () => Launch.Copy(output) });
            if (!needsInput && c.Kind == "snippet") list.Add(new ActionItem { Title = Tr("복사만 하기", "Copy only"), Keys = "Ctrl+Enter", Glyph = G.Copy, Run = () => Launch.Copy(UserCommands.Expand(UserCommands.SnippetText(c.Body), input ?? "", host.ClipText)) });
            if (c.IsAi) list.AddRange(AiCommandActions(c));
            if (c.Kind == "script")
                list.Add(new ActionItem
                {
                    Title = c.ShowWindow ? Tr("늘 창 없이 실행하기", "Always run without a window") : Tr("늘 창에서 실행하기", "Always run in a window"),
                    Glyph = G.Settings,
                    StayOpen = true,
                    Run = () =>
                    {
                        c.ShowWindow = !c.ShowWindow;
                        UserCommands.Upsert(c);
                        host.Window.ShowToast(c.ShowWindow ? Tr("창에서 실행", "Runs in a window") : Tr("창 없이 실행 · 결과는 알림으로 표시", "Runs without a window · results appear as notifications"));
                    },
                });
            list.Add(new ActionItem { Title = Tr("명령 고치기", "Edit command"), Glyph = G.Settings, StayOpen = true, Run = () => host.Window.StartForm(CommandForm(c.Kind, c, null)) });
            list.Add(new ActionItem
            {
                Title = Tr("명령 지우기", "Delete command"),
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () => host.Window.Confirm(Tr($"‘{c.Name}’ 명령을 삭제합니다.", $"Deletes the command ‘{c.Name}’."), new ActionItem
                {
                    Title = Tr("지우기", "Delete"),
                    Keys = "Enter",
                    Glyph = G.Delete,
                    Danger = true,
                    StayOpen = true,
                    Run = () =>
                    {
                        UserCommands.Remove(c);
                        host.Window.Refresh(false);
                    },
                }),
            });
            return list;
        };
        return item;
    }

    /// "bk D:\사진" in the root list: the command with the input, above everything else.
    private Section? TypedCommand(string text)
    {
        if (UserCommands.Typed(text) is not { } t) return null;
        var s = new Section(Tr("사용자 명령", "Custom commands"));
        s.Items.Add(FromCommand(t.command, t.input));
        return s;
    }

    public List<Section> CommandsView(string text)
    {
        var sections = new List<Section>();
        var q = new Query(text);
        var top = new Section(null);
        foreach (string kind in KindsOrder)
        {
            string k = kind;
            top.Items.Add(new ResultItem
            {
                Id = "my:new-" + kind,
                Kind = ItemKind.Command,
                Title = NewTitle(kind),
                Subtitle = kind switch
                {
                    "ai" => Tr("자주 쓰는 AI 지시를 이름으로 저장 · 선택한 글 · 클립보드에 실행 · >> 로 여러 단계", "Save an AI instruction you use often · run it on the selection or clipboard · >> for several steps"),
                    "snippet" => Tr("자주 쓰는 글을 단축어로 붙여넣기 · {date} {time} {clipboard} {input}", "Paste a text you use often by its keyword · {date} {time} {clipboard} {input}"),
                    "chain" => Tr("검색창에 적듯이 여러 줄을 차례로 실행 — 예) 메모장 ; 볼륨 30 ; 기다리기 2초", "Run several search lines in turn — e.g. notepad ; volume 30 ; wait 2"),
                    _ => Tr(".ps1 · .bat · 프로그램 파일, 또는 PowerShell 한 줄을 실행", "Run a .ps1, .bat or program, or one PowerShell line"),
                },
                Icon = IconSpec.Of(G.Add, true),
                PrimaryLabel = Tr("만들기", "Create"),
                StayOpen = true,
                Run = () => host.Window.StartForm(CommandForm(k, null, null)),
            });
        }
        sections.Add(top);
        var mine = new Section(Tr("사용자 명령", "Custom commands"), Tr($"{UserCommands.Current.Items.Count}개 · 단축어 + 스페이스 + 입력", $"{UserCommands.Current.Items.Count} · keyword + space + input"));
        foreach (var c in UserCommands.Current.Items)
            if (q.IsEmpty || Matcher.Score(q, c.Match) > 0)
                mine.Items.Add(FromCommand(c, null));
        if (mine.Items.Count > 0) sections.Add(mine);
        return sections;
    }

    /// What a chain step runs: a command typed with its input, else the first answer the root search gives the line
    /// (not the AI / web / file fallbacks, not a view that waits for more). `depth` stops chains inside chains.
    public Action? StepAction(string step, int depth) => Step(step, depth).run;

    /// What the step would run, by its result's title ("메모장", "볼륨 30%").
    public string? StepTitle(string step) => Step(step, 0).title;

    private (Action? run, string? title) Step(string step, int depth)
    {
        if (UserCommands.Typed(step) is { } typed) return (() => host.RunMine(typed.command, typed.input, depth: depth + 1), typed.command.Name);
        foreach (var s in Root(step))
        {
            if (s.Fallback) continue;
            foreach (var item in s.Items)
            {
                if (item.Run is null || item.StayOpen) continue;
                if (item.Data is UserCommand c) return (() => host.RunMine(c, "", depth: depth + 1), c.Name);
                return (item.Run, item.Title);
            }
        }
        return (null, null);
    }

    /// New (or changed) command, asked in the search line: what it does → name → keyword.
    public FormSpec CommandForm(string kind, UserCommand? edit, string? body)
    {
        var f = new FormSpec { Title = edit is null ? NewTitle(kind) : Tr("명령 고치기", "Edit command"), Back = Mode.Commands };
        f.Fields.Add(kind switch
        {
            "ai" => new FormField
            {
                Label = Tr("프롬프트", "Prompt"),
                Hint = Tr("AI에게 시킬 일 — 단계는 >> 로 나눔  예) 핵심 3줄로 요약 >> 영어로 번역", "What the AI should do — split steps by >>  e.g. Summarize in 3 lines >> Translate into Korean"),
                Value = edit?.Body ?? body ?? "",
                Check = v => AiCommandRun.Split(v).Count > 0 ? (string.Join(" " + AiCommandRun.StepMark + " ", AiCommandRun.Split(v)), null) : (null, Tr("AI에게 시킬 일을 입력하세요", "Enter what the AI should do")),
            },
            "snippet" => new FormField
            {
                Label = Tr("내용", "Text"),
                Hint = Tr("붙여넣을 글 — 줄바꿈은 \\n · {date} {time} {clipboard} {input}", "The text to paste — \\n for a new line · {date} {time} {clipboard} {input}"),
                Value = edit?.Body ?? body ?? "",
                Check = v => v.Length > 0 ? (v, null) : (null, Tr("붙여넣을 글을 입력하세요", "Enter the text to paste")),
            },
            "chain" => new FormField
            {
                Label = Tr("단계", "Steps"),
                Hint = Tr("검색창에 적듯이, ; 로 나눠서 — 예) 메모장 ; 볼륨 30 ; 기다리기 2초 ; yt lofi", "As you'd type them, split by ; — e.g. notepad ; volume 30 ; wait 2 ; yt lofi"),
                Value = edit?.Body ?? body ?? "",
                Check = v =>
                {
                    var steps = UserCommands.Steps(v);
                    if (steps.Count == 0) return (null, Tr("단계를 하나 이상 입력하세요", "Enter at least one step"));
                    var missing = steps.Where(s => UserCommands.Wait(s) is null && !s.Contains("{input}", StringComparison.OrdinalIgnoreCase) && StepAction(s, 0) is null).ToList();
                    return missing.Count == 0 ? (string.Join(" ; ", steps), null)
                        : (null, Tr($"‘{string.Join(", ", missing)}’ 실행 항목 없음 · 앱 이름이나 명령을 확인하세요", $"No action for ‘{string.Join(", ", missing)}’. Check the app name or command."));
                },
            },
            _ => new FormField
            {
                Label = Tr("실행할 것", "What to run"),
                Hint = Tr("파일 경로나 PowerShell 한 줄 — 입력 자리는 {input}  예) C:\\scripts\\backup.ps1 {input}", "A file path or one PowerShell line — {input} marks the input  e.g. C:\\scripts\\backup.ps1 {input}"),
                Value = edit?.Body ?? body ?? "",
                Check = v =>
                {
                    string line = v.Trim();
                    if (line.Length == 0) return (null, Tr("실행할 파일이나 명령을 입력하세요", "Enter a file or command to run"));
                    // a line that starts with a path must start with a file that is there (C:\…, "C:\…", \\server\…)
                    string head = line.TrimStart('"');
                    bool path = head.Length > 2 && (head[1] == ':' && head[2] == '\\' || head.StartsWith(@"\\", StringComparison.Ordinal));
                    if (path && UserCommands.SplitFile(UserCommands.Expand(line, "").Trim()).file is null) return (null, Tr("파일이 없습니다. 경로를 확인하세요.", "File not found. Check the path."));
                    return (line, null);
                },
            },
        });
        f.Fields.Add(new FormField
        {
            Label = Tr("이름", "Name"),
            Hint = Tr("검색에 사용할 이름", "Name used in search"),
            Value = edit?.Name ?? "",
            Check = v => v.Trim().Length > 0 ? (v.Trim(), null) : (null, Tr("이름을 입력하세요", "Enter a name")),
        });
        f.Fields.Add(new FormField
        {
            Label = Tr("단축어", "Keyword"),
            Hint = Tr("단축어 (선택) — 예) bk 를 정하면 “bk D:\\사진”처럼 입력과 함께 실행", "Keyword (optional) — e.g. with bk, “bk D:\\photos” runs it with that input"),
            Value = edit?.Keyword ?? "",
            Optional = true,
            Check = v =>
            {
                string k = v.Trim().ToLowerInvariant();
                bool input = kind != "ai" && f[0].Contains("{input}", StringComparison.OrdinalIgnoreCase);
                if (k.Length == 0) return input ? (null, Tr("{input}을 사용하려면 단축어를 입력하세요", "Enter a keyword to use {input}")) : ("", null);
                if (k.Contains(' ')) return (null, Tr("공백 없는 단축어를 입력하세요", "Enter a keyword without spaces"));
                if (!UserCommands.KeywordFree(k, edit?.Id)) return (null, Tr($"‘{k}’ 단축어 사용 중 · 다른 단축어를 입력하세요", $"‘{k}’ is in use. Enter another keyword."));
                return (k, null);
            },
        });
        if (kind == "ai") AddAiFields(f, edit);
        f.Prepare = (form, step) =>
        {
            var field = form.Fields[step];
            if (field.Value.Length > 0) return;
            if (step == 1) field.Value = SuggestCommandName(kind, form[0]);
        };
        f.Done = form =>
        {
            var c = edit is null ? UserCommands.New(kind, form[1], form[0], form[2].Length > 0 ? form[2] : null)
                : new UserCommand { Id = edit.Id, Kind = edit.Kind, Name = form[1], Body = form[0], Keyword = form[2].Length > 0 ? form[2] : null, ShowWindow = edit.ShowWindow, Thread = edit.Thread };
            if (kind == "ai") SetAiFields(c, edit, form[3], form[4]);
            UserCommands.Upsert(c);
            host.Window.Go(Mode.Commands);
            host.Window.ShowToast(edit is null ? Tr($"‘{c.Name}’ 명령 추가 완료", $"Added ‘{c.Name}’") : Tr("명령 저장 완료", "Command saved"));
        };
        return f;
    }

    /// A name to start from: the script's file name, the chain's first step, the snippet's first words.
    private static string SuggestCommandName(string kind, string body)
    {
        string s = kind switch
        {
            "chain" => UserCommands.Steps(body).FirstOrDefault() ?? "",
            "ai" => AiCommandRun.Split(body).FirstOrDefault() ?? "",
            "snippet" => UserCommands.SnippetText(body).Split('\n')[0],
            _ => UserCommands.SplitFile(body.Trim()).file is { } file ? Path.GetFileNameWithoutExtension(file) : body.Trim().Split(' ')[0],
        };
        return s.Length > 24 ? s[..24].Trim() : s.Trim();
    }
}
