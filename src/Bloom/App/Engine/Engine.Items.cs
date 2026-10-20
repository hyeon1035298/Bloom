using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Ai;
using Bloom.Browser;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.App;

/// Turning what was found (apps, commands, files, links, ids) into result rows.
internal sealed partial class Engine
{
    // ── items ───────────────────────────────────────────────────────────────────────────────────
    public ResultItem FromApp(AppEntry a)
    {
        var item = new ResultItem
        {
            Id = a.Id,
            Kind = ItemKind.App,
            Title = a.Name,
            Subtitle = a.Folder,
            Accessory = a.Source == "uwp" ? Tr("앱", "App") : a.Source == "url" ? Tr("바로 가기", "Shortcut") : Tr("앱", "App"),
            Icon = IconSpec.Shell(a.Path),
            Data = a,
            AsFavorite = new Favorites.Entry { Id = a.Id, Kind = a.Source == "uwp" ? "uwp" : "app", Title = a.Name, Target = a.Path },
        };
        item.Run = () =>
        {
            Usage.Touch(a.Id);
            Launch.Open(a.Path);
        };
        // as administrator: shortcuts and programs; a Store app only through its command alias (pwsh.exe, wt.exe);
        // web / Steam shortcuts never
        string? elevate = a.Source switch { "uwp" => StoreAlias(a.Path), "url" => null, _ => a.Path };
        if (elevate is not null)
            item.Side = new ActionItem { Title = Tr("관리자 권한으로 실행", "Run as administrator"), Keys = "Ctrl+Shift+Enter", Glyph = G.Shield, Run = () => { Usage.Touch(a.Id); Launch.RunAsAdmin(elevate); } };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>();
            if (item.Side is { } admin) list.Add(admin);
            if (a.Source != "uwp")
            {
                list.Add(new ActionItem { Title = Tr("파일 위치 열기", "Show in folder"), Keys = "Ctrl+Enter", Glyph = G.Folder, Run = () => Launch.Reveal(a.Target is { } t && File.Exists(t) ? t : a.Path) });
                list.Add(new ActionItem { Title = Tr("경로 복사", "Copy path"), Keys = "Ctrl+Shift+C", Glyph = G.Copy, Run = () => Launch.Copy(a.Target ?? a.Path) });
            }
            list.Add(new ActionItem { Title = Tr("이름 바꾸기 (Bloom에서만)", "Rename (in Bloom only)"), Glyph = G.Tag, StayOpen = true, Run = () => host.Window.StartForm(AppNameForm(a)) });
            if (a.Name != a.Original && a.Original.Length > 0)
                list.Add(new ActionItem { Title = Tr($"원래 이름으로 ({a.Original})", $"Back to the original name ({a.Original})"), Glyph = G.Refresh, Run = () => { AppNames.Set(a.Id, null); host.AppsChanged(); } });
            if (UninstallAction(a) is { } remove) list.Add(remove);
            return list;
        };
        return item;
    }

    private static readonly Dictionary<string, string?> Aliases = new(StringComparer.OrdinalIgnoreCase);

    /// A Store app's command alias (%LOCALAPPDATA%\Microsoft\WindowsApps\<package family>\*.exe), which Windows can
    /// start as administrator; null when the package has none. Looked up once per package.
    private static string? StoreAlias(string path)
    {
        // shell:AppsFolder\Microsoft.PowerShell_8wekyb3d8bbwe!App → Microsoft.PowerShell_8wekyb3d8bbwe
        int slash = path.LastIndexOf('\\'), bang = path.IndexOf('!', StringComparison.Ordinal);
        if (slash < 0 || bang <= slash + 1) return null;
        string family = path[(slash + 1)..bang];
        lock (Aliases)
        {
            if (Aliases.TryGetValue(family, out var known)) return known;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", family);
            string? exe = null;
            try
            {
                if (Directory.Exists(dir)) exe = Directory.EnumerateFiles(dir, "*.exe").FirstOrDefault();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // no alias to offer
            }
            return Aliases[family] = exe;
        }
    }

    /// The name Bloom shows for an app; the shortcut keeps its own, and the old name still finds it.
    public FormSpec AppNameForm(AppEntry a)
    {
        string original = a.Original.Length > 0 ? a.Original : a.Name;
        var f = new FormSpec { Title = Tr("이름 바꾸기 · Bloom에서만", "Rename · in Bloom only") };
        f.Fields.Add(new FormField
        {
            Label = Tr("이름", "Name"),
            Hint = Tr($"Bloom에서 보일 이름 — 비우면 원래 이름 ({original})", $"The name Bloom shows — empty: the original ({original})"),
            Value = a.Name,
            Optional = true,
            Check = v => (v.Trim(), null),
        });
        f.Done = form =>
        {
            string name = form[0];
            AppNames.Set(a.Id, name.Length == 0 || name == original ? null : name);
            host.AppsChanged();
            host.Window.Go(Mode.Root);
            host.Window.ShowToast(name.Length == 0 || name == original ? Tr($"‘{original}’ 원래 이름 복원 완료", $"‘{original}’ has its original name again") : Tr($"‘{original}’ → ‘{name}’ (Bloom에서만)", $"‘{original}’ → ‘{name}’ (in Bloom only)"));
        };
        return f;
    }

    public ResultItem FromSys(SysEntry e)
    {
        var item = new ResultItem
        {
            Id = e.Id,
            Kind = e.Kind == "cmd" ? ItemKind.Command : e.Kind == "folder" ? ItemKind.Folder : ItemKind.Setting,
            Title = e.Title,
            Subtitle = e.Subtitle,
            Accessory = e.Kind switch { "cmd" => Tr("명령", "Command"), "setting" => Tr("설정", "Setting"), "tool" => Tr("도구", "Tool"), _ => Tr("폴더", "Folder") },
            Data = e,
            AsFavorite = new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target },
        };
        item.Icon = e.Kind switch
        {
            "cmd" => IconSpec.Of(e.Glyph, e.Id == "cmd:ai"),
            "setting" => IconSpec.Shell(SystemItems.SettingsApp, "settings-app"),
            "tool" => IconSpec.Shell(ToolPath(e.Target)),
            _ => IconSpec.Shell(e.Target),
        };
        if (e.Kind == "cmd")
        {
            item.PrimaryLabel = Tr("실행", "Run");
            item.Run = () => host.RunCommand(e.Id);
            item.StayOpen = e.Id is "cmd:ai" or "cmd:layout" or "cmd:winget" or "cmd:threads" or "cmd:files" or "cmd:clipboard" or "cmd:calendar" or "cmd:quicklinks" or "cmd:commands" or "cmd:processes" or "cmd:lens" or "cmd:pin" or "cmd:capture" or "cmd:timer" or "cmd:color" or "cmd:space" or "cmd:settings" or "cmd:welcome" or "cmd:export" or "cmd:import" or "cmd:reindex" or "cmd:restart" or "cmd:shutdown"
                or "cmd:windows" or "cmd:emoji" or "cmd:recent" or "cmd:system" or "cmd:quick" or "cmd:notes" or "cmd:player" or "cmd:tools";
        }
        else
        {
            item.Run = () =>
            {
                Usage.Touch(e.Id);
                Launch.Open(e.Kind == "tool" ? ToolPath(e.Target) : e.Target, e.Args);
            };
            if (e.Kind == "tool")
            {
                item.Side = new ActionItem { Title = Tr("관리자 권한으로 실행", "Run as administrator"), Keys = "Ctrl+Shift+Enter", Glyph = G.Shield, Run = () => Launch.Open(ToolPath(e.Target), e.Args, "runas") };
                item.MoreActions = () => new List<ActionItem> { item.Side };
            }
        }
        return item;
    }

    private static string ToolPath(string exe)
    {
        string sys = Path.Combine(Environment.SystemDirectory, exe);
        return File.Exists(sys) ? sys : exe;
    }

    public ResultItem FromFile(EvItem f, bool detail) => FromPath(f.FullPath, f.IsFolder, detail ? f : null);

    public ResultItem FromPath(string path, bool folder, EvItem? info = null)
    {
        long size = info?.Size ?? -1;
        DateTime? modified = info?.Modified;
        string name = Path.GetFileName(path.TrimEnd('\\'));
        if (name.Length == 0) name = path;
        string dir = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "";
        string ext = folder ? "" : Path.GetExtension(path).ToLowerInvariant();
        string iconKey = folder ? "folder:" : ext is ".exe" or ".lnk" or ".ico" or ".url" or ".appref-ms" or "" ? path.ToLowerInvariant() : "ext:" + ext;
        string id = (folder ? "folder:" : "file:") + path.ToLowerInvariant();
        var item = new ResultItem
        {
            Id = id,
            Kind = folder ? ItemKind.Folder : ItemKind.File,
            Title = name,
            Subtitle = Short(dir),
            Accessory = info is null ? (folder ? Tr("폴더", "Folder") : Tr("파일", "File")) : Detail(info),
            Icon = IconSpec.Shell(folder ? path : path, iconKey),
            Data = path,
            AsFavorite = new Favorites.Entry { Id = id, Kind = folder ? "folder" : "file", Title = name, Subtitle = Short(dir), Target = path },
            ReadDetail = () => Search.Detail.ForPath(path, folder, size, modified),
        };
        item.Run = () =>
        {
            Usage.Touch(id);
            host.OpenFile(path); // a .md one in Bloom's document window when Bloom is Windows' app for it
        };
        if (ext is ".exe" or ".bat" or ".cmd" or ".msi" or ".lnk")
            item.Side = new ActionItem { Title = Tr("관리자 권한으로 실행", "Run as administrator"), Keys = "Ctrl+Shift+Enter", Glyph = G.Shield, Run = () => Launch.RunAsAdmin(path) };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>
            {
                new() { Title = Tr("폴더에서 보기", "Show in folder"), Keys = "Ctrl+Enter", Glyph = G.Folder, Run = () => Launch.Reveal(path) },
                new() { Title = Tr("경로 복사", "Copy path"), Keys = "Ctrl+Shift+C", Glyph = G.Copy, Run = () => Launch.Copy(path) },
            };
            if (folder) list.Add(new ActionItem { Title = Tr("이 폴더 크기 보기 (Space Cleaner)", "See this folder's size (Space Cleaner)"), Glyph = G.Folder, Run = () => host.OpenSpace(path) });
            if (item.Side is { } admin) list.Insert(1, admin);
            if (!folder && DocFile.IsMarkdown(path) && Features.On("docs"))
                list.Insert(0, new ActionItem { Title = Tr("Bloom으로 보기", "View in Bloom"), Glyph = G.Page, Run = () => { Usage.Touch(id); host.OpenDoc(path); } });
            if (!folder && AudioFiles.IsAudio(path) && Features.On("player"))
            {
                list.Insert(0, new ActionItem
                {
                    Title = Tr("대기열에 추가", "Add to the queue"),
                    Glyph = G.Add,
                    StayOpen = true,
                    Run = () =>
                    {
                        MusicPlayer.Enqueue(path);
                        host.Window.ShowToast(Tr($"‘{name}’ 대기열에 추가 요청 · {PlayerWays.NameOf(MusicPlayer.Way)}", $"Asked to add ‘{name}’ to the queue · {PlayerWays.NameOf(MusicPlayer.Way)}"));
                    },
                });
                list.Insert(0, new ActionItem { Title = Tr("미니 플레이어로 재생", "Play in mini player"), Glyph = PlayerGlyph.Play, Run = () => { Usage.Touch(id); host.Tools.Open("player"); MusicPlayer.Play(path); } });
            }
            if (!folder && Playlists.IsPlaylist(path) && Features.On("player") && !MusicPlayer.External)
            {
                list.Insert(0, new ActionItem { Title = Tr("대기열에 추가", "Add to the queue"), Glyph = G.Add, Run = () => { host.Tools.Open("player"); MusicPlayer.OpenPlaylist(path, true); } });
                list.Insert(0, new ActionItem { Title = Tr("미니 플레이어로 재생", "Play in mini player"), Glyph = PlayerGlyph.Play, Run = () => { Usage.Touch(id); host.Tools.Open("player"); MusicPlayer.OpenPlaylist(path); } });
            }
            list.AddRange(FileActions(path, name, folder));
            if (ext is ".ps1" or ".bat" or ".cmd" or ".exe")
                list.Add(new ActionItem { Title = Tr("사용자 명령에 추가…", "Add to custom commands…"), Glyph = G.Bolt, StayOpen = true, Run = () => host.Window.StartForm(CommandForm("script", null, path.Contains(' ') ? $"\"{path}\"" : path)) });
            return list;
        };
        return item;
    }

    private static string Detail(EvItem f)
    {
        string when = f.Modified is { } m ? Ago(m) : "";
        if (f.IsFolder || f.Size < 0) return when;
        return when.Length > 0 ? $"{Size(f.Size)} · {when}" : Size(f.Size);
    }

    public static string Size(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024:0.#} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.##} GB",
    };

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return Tr("방금", "Just now");
        if (d.TotalMinutes < 60) return Tr($"{(int)d.TotalMinutes}분 전", $"{(int)d.TotalMinutes} min ago");
        if (d.TotalHours < 24 && t.Date == DateTime.Today) return Tr($"{(int)d.TotalHours}시간 전", $"{(int)d.TotalHours} h ago");
        if (t.Date == DateTime.Today.AddDays(-1)) return Tr("어제", "Yesterday");
        if (d.TotalDays < 7) return Tr($"{(int)Math.Ceiling(d.TotalDays)}일 전", $"{(int)Math.Ceiling(d.TotalDays)} days ago");
        return t.Year == DateTime.Now.Year ? t.ToString(Tr("M월 d일", "MMM d"), Lang.Culture) : $"{t.Year}. {t.Month}. {t.Day}.";
    }

    public static string Short(string dir)
    {
        string home = Paths.Home;
        if (dir.StartsWith(home, StringComparison.OrdinalIgnoreCase)) return "~" + dir[home.Length..];
        return dir;
    }

    public ResultItem FromUrl(string url, string title, string? sub, ItemKind kind, string accessory)
    {
        string? h = Web.HostOf(url);
        string id = "url:" + url;
        var item = new ResultItem
        {
            Id = id,
            Kind = kind,
            Title = title,
            Subtitle = sub,
            Accessory = accessory,
            Icon = IconSpec.Of(G.Globe),
            Data = url,
            PrimaryLabel = Tr("브라우저에서 열기", "Open in the browser"),
            AsFavorite = new Favorites.Entry { Id = id, Kind = "url", Title = title, Subtitle = h, Target = url },
        };
        item.Run = () =>
        {
            Usage.Touch(id);
            Web.Open(url);
        };
        item.MoreActions = () => new List<ActionItem>
        {
            new() { Title = Tr("기본 브라우저로 열기", "Open in the default browser"), Keys = "Ctrl+Enter", Glyph = G.NewWindow, Run = () => Launch.Open(url) },
            new() { Title = Tr("링크 복사", "Copy link"), Keys = "Ctrl+Shift+C", Glyph = G.Link, Run = () => Launch.Copy(url) },
            new() { Title = Tr("브라우저 바로 가기로 저장", "Save as a browser shortcut"), Glyph = G.Add, StayOpen = true, Run = () => host.Window.StartForm(QuicklinkForm(null, url, title)) },
        };
        return item;
    }

    private ResultItem? FromId(string id)
    {
        if (id.StartsWith("app:", StringComparison.Ordinal) || id.StartsWith("uwp:", StringComparison.Ordinal))
        {
            var a = Apps.Apps.FirstOrDefault(x => x.Id == id);
            return a is null ? null : FromApp(a);
        }
        if (id.StartsWith("file:", StringComparison.Ordinal)) return null; // only favorites keep file paths with case
        if (id.StartsWith("url:", StringComparison.Ordinal)) return null;
        if (id.StartsWith("ql:", StringComparison.Ordinal))
            return Quicklinks.Current.Items.FirstOrDefault(x => "ql:" + x.Id == id) is { TakesQuery: false } ql ? FromQuicklink(ql, null) : null;
        var s = SystemItems.Find(id);
        return s is null || s.Kind == "cmd" ? null : FromSys(s);
    }

    /// "AI에게 묻기" (Tab): in the conversation Tab picks up, named, or a new one.
    internal ResultItem AiItem(string q, ChatThread? continued) => new()
    {
        Id = "ai:ask",
        Kind = ItemKind.Ai,
        Title = Tr($"“{q}” AI에게 묻기", $"Ask AI “{q}”"),
        Subtitle = continued is null ? AiModels.Who(AiModels.DefaultModel()) : Tr($"이어서: {continued.Title}", $"Continues: {continued.Title}"),
        Accessory = "Tab",
        Icon = IconSpec.Of(G.Sparkle, true),
        PrimaryLabel = Tr("묻기", "Ask"),
        StayOpen = true,
        Run = () => host.Ask(q, null),
    };

    /// "새 대화로 묻기" (Shift+Tab), shown while Tab would pick up an earlier conversation.
    private ResultItem AiNewItem(string q) => new()
    {
        Id = "ai:ask-new",
        Kind = ItemKind.Ai,
        Title = Tr($"“{q}” 새 대화로 묻기", $"Ask “{q}” in a new chat"),
        Subtitle = AiModels.Who(AiModels.DefaultModel()),
        Accessory = "Shift+Tab",
        Icon = IconSpec.Of(G.Add, true),
        PrimaryLabel = Tr("묻기", "Ask"),
        StayOpen = true,
        Run = () => host.Ask(q, null, fresh: true),
    };

    public static string ModelName(string m) => AiModels.Name(m);

    private static ResultItem WebItem(string q) => new()
    {
        Id = "web:search",
        Kind = ItemKind.WebSearch,
        Title = Tr($"“{q}” 웹 검색", $"Search the web for “{q}”"),
        Subtitle = $"{Web.EngineName} · {Web.BrowserName}",
        Accessory = Tr("웹", "Web"),
        Icon = IconSpec.Of(G.Globe),
        PrimaryLabel = Tr("브라우저에서 검색", "Search in the browser"),
        Run = () => Web.Search(q),
    };

    private ResultItem FindFilesItem(string q) => new()
    {
        Id = "files:find",
        Kind = ItemKind.Command,
        Title = Tr($"“{q}” 파일 찾기", $"Find files “{q}”"),
        Subtitle = "Everything",
        Accessory = Tr("파일", "File"),
        Icon = IconSpec.Of(G.Search),
        PrimaryLabel = Tr("찾기", "Find"),
        StayOpen = true,
        Run = () => host.OpenView(Mode.Files, q),
    };
}
