using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

internal enum Mode
{
    Root,
    Files,
    Threads,
    Chat,
    Clipboard,
    Calendar,
    Quicklinks,
    Processes,
    Ocr, // the text read from the screen
    Lens, // Bloom Lens: an area of the screen, what to do with it
    Timers, // timers and reminders
    Color, // the colour picked last: its forms, gradients from it, recent colours
    Group, // what is in a favorites folder
    Form, // a few questions in the search line (new / edit quicklink)
    Windows, // the open windows, to switch to
    Emoji, // emoji and symbols to paste
    Recent, // the files Windows lists as recently opened
    System, // toggles: dark mode, sound, radios, …
    Quick, // AI actions on the selected / copied text
    Commands, // 내 명령: scripts, snippets, chains
    Layout, // 창 배치: where the window in front goes
    Winget, // 앱 설치: winget's catalogue
    Inbox, // 알림 센터: how the AI commands that ran on their own went
    Palette, // 컬러 팔레트: the colours of a picture (Bloom Lens › 7), the one picked with its gradients
}

/// Builds the result sections for each view. Fast sources (apps, commands, settings, calculator,
/// tabs, bookmarks, history) answer synchronously; Everything answers a moment later.
internal sealed partial class Engine
{
    private readonly Host host;
    private AppIndex Apps => host.Apps;

    /// Everything's root query leaves out the usual noise folders.
    private const string RootNoise = " !\\AppData\\ !\\$Recycle.Bin\\ !\\Windows\\WinSxS\\ !\\node_modules\\ !\\.git\\ !\\Library\\PackageCache\\ !\\Temp\\";

    public Engine(Host host, PathLookup? pathLookup = null)
    {
        this.host = host;
        this.pathLookup = pathLookup ?? new PathLookup();
    }

    // ── other views ─────────────────────────────────────────────────────────────────────────────
    /// File search kinds (the ▾ chip in the search line, Ctrl+Tab): an Everything filter put before the words.
    /// The work tools have their own (유니티 · 블렌더 · 어도비), so .unitypackage is not 압축 and .psd not 사진.
    public static readonly (string key, string name, char glyph, string filter)[] FileKinds =
    {
        ("all", Tr("전체", "All"), G.Search, ""),
        ("folder", Tr("폴더", "Folder"), G.Folder, "folder:"),
        ("doc", Tr("문서", "Documents"), G.Document, "file: ext:pdf;doc;docx;hwp;hwpx;txt;md;rtf;odt;xls;xlsx;csv;ppt;pptx"),
        ("pic", Tr("사진", "Photo"), '\uEB9F', "file: ext:jpg;jpeg;png;gif;webp;bmp;tif;tiff;heic;avif;svg;ico"),
        ("video", Tr("영상", "Video"), '\uE714', "file: ext:mp4;mkv;mov;avi;webm;wmv;m4v;flv;ts"),
        ("audio", Tr("음악", "Music"), '\uE8D6', "file: ext:mp3;wav;flac;m4a;aac;ogg;opus;wma"),
        ("unity", Tr("유니티", "Unity"), '\uE7FC', "file: ext:unitypackage;unity"),
        ("blender", Tr("블렌더", "Blender"), '\uE70F', "file: ext:blend;blend1"),
        ("adobe", Tr("어도비", "Adobe"), '\uE790', "file: ext:psd;psb;ai;aep;aet;prproj;mogrt;indd;xd"),
        ("exe", Tr("실행 파일", "Programs"), G.App, "file: ext:exe;msi;bat;cmd;ps1"),
        ("zip", Tr("압축", "Archives"), '\uF012', "file: ext:zip;7z;rar;tar;gz"),
    };

    public static (string key, string name, char glyph, string filter) FileKind(string key) =>
        Array.Find(FileKinds, k => k.key == key) is { key: not null } k ? k : FileKinds[0];

    public async Task<List<Section>> Files(string text, string kind = "all")
    {
        var fk = FileKind(kind);
        var sections = new List<Section>();
        if (!host.Everything.Running)
        {
            var s = new Section(null);
            s.Items.Add(new ResultItem
            {
                Id = "ev:start",
                Kind = ItemKind.Info,
                Title = Tr("Everything 실행 안 됨", "Everything is not running"),
                Subtitle = Tr("Enter로 Everything 실행", "Enter starts Everything"),
                Icon = IconSpec.Of(G.Info),
                PrimaryLabel = Tr("실행", "Run"),
                StayOpen = true,
                Run = () =>
                {
                    if (EverythingClient.ExePath() is { } exe) Launch.Open(exe, "-startup");
                },
            });
            sections.Add(s);
            return sections;
        }
        string search = text.Trim().Length == 0
            ? "dm:last7days " + (fk.filter.Length > 0 ? fk.filter : "file:") + RootNoise
            : (fk.filter.Length > 0 ? fk.filter + " " : "") + text.Trim();
        var list = await host.Everything.Query(search, 200, EverythingClient.SortDateModifiedDesc);
        string what = fk.key == "all" ? "" : " · " + fk.name;
        var sec = new Section((text.Trim().Length == 0 ? Tr("최근 수정한 항목", "Recently changed") : Tr("결과", "Results")) + what, list is null ? null : Tr($"{list.Count}{(list.Count >= 200 ? "+" : "")}개", $"{list.Count}{(list.Count >= 200 ? "+" : "")}"));
        if (list is not null)
            foreach (var f in list) sec.Items.Add(FromFile(f, true));
        if (sec.Items.Count == 0)
            sec.Items.Add(new ResultItem { Id = "ev:none", Kind = ItemKind.Info, Title = Tr("검색 결과 없음", "Nothing found"), Subtitle = Tr("Everything 문법도 됩니다: ext:pdf, size:>10mb, dm:today", "Everything's syntax works too: ext:pdf, size:>10mb, dm:today"), Icon = IconSpec.Of(G.Info) });
        sections.Add(sec);
        return sections;
    }

    public List<Section> Threads(string text)
    {
        var sections = new List<Section>();
        var q = new Query(text);
        var s = new Section(Tr("AI 채팅", "AI chats"), Tr($"{host.Chats.Threads.Count}개", $"{host.Chats.Threads.Count}"));
        var top = new Section(null);
        top.Items.Add(new ResultItem
        {
            Id = "thread:new",
            Kind = ItemKind.Ai,
            Title = q.IsEmpty ? Tr("새 대화", "New chat") : Tr($"“{text.Trim()}” 새 대화로 묻기", $"Ask “{text.Trim()}” in a new chat"),
            Subtitle = AiModels.Who(AiModels.DefaultModel()),
            Icon = IconSpec.Of(G.Sparkle, true),
            PrimaryLabel = Tr("시작", "Start"),
            StayOpen = true,
            Run = () =>
            {
                if (q.IsEmpty) host.OpenChat(null);
                else host.Ask(text.Trim(), host.Chats.New());
            },
        });
        sections.Add(top);
        foreach (var t in host.Chats.Threads)
        {
            if (!q.IsEmpty && Matcher.Score(q, new MatchTarget(t.Title)) <= 0) continue;
            string preview = t.Messages.LastOrDefault(m => m.Role == "assistant")?.Text ?? "";
            preview = preview.Replace('\n', ' ');
            if (preview.Length > 80) preview = preview[..80] + "…";
            var th = t;
            s.Items.Add(new ResultItem
            {
                Id = "thread:" + t.Id,
                Kind = ItemKind.Thread,
                Title = t.Title,
                Subtitle = preview,
                Accessory = (host.Chats.GroupOf(t) is { } g ? g.Name + " · " : "") + Ago(DateTimeOffset.FromUnixTimeMilliseconds(t.Updated).LocalDateTime),
                Icon = IconSpec.Of(G.Chat),
                PrimaryLabel = Tr("열기", "Open"),
                StayOpen = true,
                Run = () => host.OpenChat(th),
                MoreActions = () => new List<ActionItem>
                {
                    new() { Title = Tr("대화 삭제", "Delete chat"), Glyph = G.Delete, Danger = true, StayOpen = true, Run = () => host.DeleteThread(th) },
                },
            });
        }
        if (s.Items.Count > 0) sections.Add(s);
        return sections;
    }
}
