using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The tutorial's pages that set Bloom up as they are answered: how it opens, how it looks, the browser, the home's
/// features and the AIs found. Each fills the page's column under its header.
internal sealed partial class OnboardingWindow
{
    // ── pieces ──────────────────────────────────────────────────────────────────────────────────
    /// The tag over a page's title in the pages shown after an update.
    private string? NewTag => UpdateOnly ? Tr("새로 추가된 안내", "Newly added") : null;

    private static T Under<T>(T e, double top) where T : FrameworkElement
    {
        e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }

    // ── how Bloom opens ─────────────────────────────────────────────────────────────────────────
    private void Opening(StackPanel page)
    {
        var s = Settings.Current;
        page.Children.Add(TutorialParts.Header(Tr("Bloom 여는 법", "How to open Bloom"), Tr("Win 키와 Alt + Space를 함께 켜 두어도 됩니다.", "You can keep both the Win key and Alt + Space on.")));
        bool admin = env.AdminOffered;
        double scene = admin ? 132 : 156; // without the row under them the pictures take its room
        ThumbRow? adminRow = null;
        var grid = Under(new TileGrid(3, 9, 9), 18);
        PictureTile Tile(TutorialScene picture, string name, string about, bool on, Action<bool> set)
        {
            var t = new PictureTile(TutorialArt.For(picture, 178, scene), scene, name, about, TileMark.Switch, on);
            t.Toggled += v =>
            {
                set(v);
                Settings.Save();
            };
            grid.Add(t);
            return t;
        }
        Tile(TutorialScene.OpenWin, Tr("Win 키", "Win key"), Tr("눌렀다 떼면 열림", "Press and release"), s.WinKey, v =>
        {
            s.WinKey = v;
            host.ApplyHotkeys();
            if (adminRow is not null) adminRow.Off = !v; // nothing to open over an admin window with
        });
        Tile(TutorialScene.OpenAltSpace, "Alt + Space", Tr("어디서나 열림", "Opens anywhere"), s.AltSpace, v =>
        {
            s.AltSpace = v;
            host.ApplyHotkeys();
        });
        Tile(TutorialScene.OpenAutostart, Tr("Windows와 함께", "With Windows"), Tr("로그인하면 바로 대기", "Ready at sign-in"), s.Autostart, v =>
        {
            s.Autostart = v;
            if (!Paths.IsDev) Autostart.Set(v);
            LogonTask.SyncLater(); // the earlier start at sign-in for a taskbar add-on follows this switch
        });
        page.Children.Add(grid);
        if (admin)
        {
            // the switch follows the outcome: a declined UAC prompt leaves it off
            adminRow = ThumbRow.Switched(TutorialArt.For(TutorialScene.ThumbAdmin, 64, 38), Tr("관리자 권한 창에서도 Win 키로 열기", "Win key over admin windows too"),
                Tr("켜면 Windows가 허락을 한 번 묻습니다", "Windows asks for permission once"), s.WinKeyAdmin, async v =>
                {
                    bool now = await host.SetWinKeyAdmin(v, this);
                    // the row shown now: a page drawn again while Windows asked has a new one
                    if (adminRow?.Switch is { } sw) sw.On = now;
                });
            adminRow.Off = !s.WinKey;
            page.Children.Add(Under(adminRow, 12));
        }
        page.Children.Add(Under(TutorialParts.Note(Tr("Win+E 같은 다른 단축키도 그대로 됩니다.", "Other shortcuts such as Win+E still work.")), 14));
    }

    // ── how Bloom looks ─────────────────────────────────────────────────────────────────────────
    private void Looks(StackPanel page)
    {
        var s = Settings.Current;
        page.Children.Add(TutorialParts.Header(Tr("화면 모양", "Appearance"), Tr("고르면 바로 바뀝니다.", "Changes as soon as you pick.")));
        var themes = new (string value, TutorialScene picture, string name, string? about)[]
        {
            ("system", TutorialScene.LookSystem, Tr("시스템", "System"), Tr("Windows를 따라감", "Follows Windows")),
            ("light", TutorialScene.LookLight, Tr("밝게", "Light"), null),
            ("dark", TutorialScene.LookDark, Tr("어둡게", "Dark"), null),
        };
        string theme = themes.Any(t => t.value == s.Theme) ? s.Theme : "system";
        var grid = Under(new TileGrid(3, 9, 9), 18);
        foreach (var (value, picture, name, about) in themes)
            grid.Add(new PictureTile(TutorialArt.For(picture, 178, 120), 120, name, about, TileMark.Pick, value == theme));
        grid.PickOne(i =>
        {
            s.Theme = themes[i].value;
            Settings.Save();
            Theme.Apply(s.Theme); // every window repaints; this page is built again by its window
        });
        page.Children.Add(grid);

        accentDots = AccentDots(s);
        accentRow = new ThumbRow(TutorialArt.For(TutorialScene.ThumbAccent, 64, 38), Tr("강조색", "Accent color"), null, accentDots);
        page.Children.Add(Under(accentRow, 12));
        page.Children.Add(Under(ThumbRow.Switched(TutorialArt.For(TutorialScene.ThumbGlass, 64, 38), Tr("유리 효과", "Glass Effect"),
            Tr("창 뒤가 살짝 비칩니다", "What is behind shows through a little"), s.Transparency != "off", v =>
            {
                s.Transparency = v ? "system" : "off";
                Settings.Save();
                Theme.Apply(s.Theme);
            }), 6));
    }

    // ── the browser ─────────────────────────────────────────────────────────────────────────────
    private void BrowserPick(StackPanel page)
    {
        var s = Settings.Current;
        page.Children.Add(TutorialParts.Header(Tr("브라우저", "Browser"), Tr("Bloom에서 여는 웹 검색과 링크에만 씁니다.", "Used only for web searches and links opened from Bloom.")));
        // Windows' own choice first, then what was found
        var found = new List<(string value, string name, string? exe)> { ("default", Tr("Windows 기본", "Windows default"), env.DefaultBrowserExe) };
        found.AddRange(env.Browsers.Select(b => (b.Key, b.Name, (string?)b.Exe)));
        int n = found.Count;
        bool many = n > 8; // three rows and more: lower pictures, so they fit the page
        double scene = many ? 64 : 108, icon = many ? 36 : 44;
        // one tile alone keeps a tile's width: the second column stays empty
        var grid = Under(new TileGrid(n == 1 ? 2 : Math.Min(n, 4), 8, 8), 18);
        // the one the settings name, as Bloom reads them (a browser saved under an older key counts), if it is among these
        string saved = Browser.Web.Picked?.Key ?? s.WebBrowser;
        string now = env.Browsers.Any(b => b.Key == saved) ? saved : "default";
        foreach (var (value, name, exe) in found)
            grid.Add(new PictureTile(BrowserScene(exe, icon, badge: value == "default"), scene, name, null, TileMark.Pick, value == now));
        grid.PickOne(i =>
        {
            s.WebBrowser = found[i].value;
            Settings.Save();
        });
        page.Children.Add(grid);
        var engine = Choice(new Segmented(Browser.Web.Engines.Select(e => (e.key, e.name)), s.SearchEngine), v =>
        {
            s.SearchEngine = v;
            Settings.Save();
        });
        page.Children.Add(Under(new ThumbRow(null, Tr("검색 엔진", "Search Engine"), null, engine), 12));
    }

    /// A browser's own icon in the middle of its tile; Windows' choice wears the Windows mark on its corner.
    private static FrameworkElement BrowserScene(string? exe, double size, bool badge)
    {
        var box = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(exe is null ? Icons.Tile(G.Globe, false, size) : Icons.Build(IconSpec.Shell(exe), size));
        if (badge)
        {
            // over the icon's lower right corner, a little past it
            var mark = TutorialArt.For(TutorialScene.BadgeWindows, 18, 18);
            Canvas.SetLeft(mark, size - 12);
            Canvas.SetTop(mark, size - 13);
            box.Children.Add(mark);
        }
        var scene = new Grid();
        scene.Children.Add(box);
        return scene;
    }

    // ── the home's features ─────────────────────────────────────────────────────────────────────
    /// A feature offered for the home's row: its command id, what it is for, and whether it starts on.
    private readonly record struct Tool(string Id, string About, bool On);

    private readonly Tool[] tools;
    private HashSet<string> picked = new(); // switched on for the home
    private HashSet<string> already = new(); // on the home when the window opened (switched off = taken out)
    private bool toolsDone;

    // file search and the AI chat start on only where what they need was found
    private Tool[] ToolTable() =>
    [
        new("cmd:quicklinks", Tr("사이트 바로 가기", "Jump to sites"), true),
        new("cmd:files", Tr("이름으로 찾기", "Find by name"), env.Everything),
        new("cmd:clipboard", Tr("복사한 것 다시", "Copied items again"), true),
        new("cmd:timer", Tr("10분 타이머", "10 min timer"), true),
        new("cmd:calendar", Tr("알림을 달력으로", "Reminders on a calendar"), true),
        new("cmd:lens", Tr("화면 골라 묻기", "Ask about the screen"), true),
        new("cmd:space", Tr("디스크 정리", "Free up disk"), true),
        new("cmd:ai", Tr("AI에게 묻기", "Ask AI"), env.AnyProvider),
        new("cmd:inbox", Tr("AI 작업 기록", "AI task log"), false),
        new("cmd:record", Tr("영역을 MP4로", "Region to MP4"), false),
        new("cmd:commands", Tr("상용구와 명령", "Snippets and commands"), false),
        new("cmd:layout", Tr("창 나란히 놓기", "Windows side by side"), false),
    ];

    /// The features this Bloom has, in tile order, each with its launcher entry.
    private IEnumerable<(Tool tool, SysEntry entry)> Offered()
    {
        foreach (var t in tools)
            if (Features.Shows(t.Id) && SystemItems.Find(t.Id) is { } e) yield return (t, e);
    }

    /// The tiles start from the home as it is: the features already in the favourites, or the default ones while the
    /// home is still empty (a first run).
    private void ResetTools()
    {
        already = tools.Where(t => Favorites.Has(t.Id)).Select(t => t.Id).ToHashSet();
        picked = Favorites.Current.Items.Count == 0 ? Offered().Where(o => o.tool.On).Select(o => o.tool.Id).ToHashSet() : new HashSet<string>(already);
    }

    /// The tiles into the favourites (once): the ones switched on go into the row, the ones switched off that were
    /// there come out.
    private void AddTools()
    {
        if (toolsDone) return;
        toolsDone = true;
        foreach (var id in already.Where(id => !picked.Contains(id))) Favorites.Remove(id);
        foreach (var (t, e) in Offered())
            if (picked.Contains(t.Id) && !Favorites.Has(t.Id))
                Favorites.Add(new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target });
    }

    /// The features switched on, in tile order.
    private List<SysEntry> HomeRow() => [.. Offered().Where(o => picked.Contains(o.tool.Id)).Select(o => o.entry)];

    private void HomeTools(StackPanel page)
    {
        page.Children.Add(TutorialParts.Header(Tr("홈에 둘 기능", "Features on Home"), Tr("켠 기능은 Bloom을 열면 바로 보입니다.", "What you turn on shows as soon as Bloom opens.")));
        var grid = Under(new TileGrid(4, 8, 7), 14);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Strip()
        {
            row.Children.Clear();
            var on = HomeRow();
            for (int i = 0; i < on.Count; i++) row.Children.Add(HomeIcon(on[i].Glyph, first: i == 0, gap: i == 0 ? 0 : 9));
        }
        foreach (var (tool, entry) in Offered())
        {
            string id = tool.Id;
            var tile = new PictureTile(TutorialArt.For(TutorialArt.Home(id)!.Value, 132, 46), 46, entry.Title, tool.About, TileMark.Badge, picked.Contains(id), small: true);
            tile.Toggled += v =>
            {
                if (v) picked.Add(id);
                else picked.Remove(id);
                Strip(); // the row under the tiles is the home as it will be
            };
            grid.Add(tile);
        }
        page.Children.Add(grid);

        var label = new TutorialParts.EvenLines(Theme.T(Hangul.KeepAll(Tr("홈에 보일 줄", "Row on Home")), 11.5, Theme.P.TextFaint), 14, 2)
        {
            Width = 52,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var strip = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(label, Dock.Left);
        strip.Children.Add(label);
        strip.Children.Add(row);
        Strip();
        page.Children.Add(Under(new SmoothBorder
        {
            CornerRadius = new CornerRadius(11),
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            MinHeight = 44,
            Child = strip,
        }, 8));
        page.Children.Add(Under(TutorialParts.Note(Tr("나중에 런처에서 Ctrl+K로 빼고, 끌어서 순서를 바꿉니다.", "Later, remove one with Ctrl+K in the launcher and drag to reorder.")), 8));
    }

    /// A feature's icon as the home's row shows it; the first wears the row's selection line under it.
    private static FrameworkElement HomeIcon(char glyph, bool first, double gap)
    {
        var g = new Grid { Width = 26, Height = 26, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(Icons.Tile(glyph, false, 26));
        if (first)
            g.Children.Add(new Rectangle
            {
                Width = 14,
                Height = 2,
                RadiusX = 1,
                RadiusY = 1,
                Fill = Theme.P.Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -5), // 3 px under the icon, outside its box
                SnapsToDevicePixels = true,
            });
        return g;
    }

    // ── the AIs ─────────────────────────────────────────────────────────────────────────────────
    private void Ais(StackPanel page)
    {
        var s = Settings.Current;
        var providers = env.Providers;
        bool many = providers.Count >= 2;
        // a choice that names an AI this PC does not have goes to the first one found
        if (providers.Count > 0 && (!providers.Contains(s.AiProvider) || !providers.Contains(s.TranslateProvider)))
        {
            if (!providers.Contains(s.AiProvider)) s.AiProvider = providers[0];
            if (!providers.Contains(s.TranslateProvider)) s.TranslateProvider = providers[0];
            Settings.Save();
        }
        page.Children.Add(TutorialParts.Header(Tr("AI 프로그램", "AI programs"), Tr("찾은 AI로 채팅하고 번역합니다.", "Bloom chats and translates with the AI it finds.")));
        double scene = many ? 104 : 150; // with nothing to choose under them the pictures take the room
        var grid = Under(new TileGrid(4, 8, 8), 18);
        foreach (var (picture, name, here, guide) in new (TutorialScene, string, bool, string?)[]
        {
            (TutorialScene.AiClaude, "Claude Code", env.Claude, "https://docs.anthropic.com/en/docs/claude-code"),
            (TutorialScene.AiCodex, "Codex", env.Codex, "https://github.com/openai/codex"),
            (TutorialScene.AiT3, "T3 Code", env.T3, null), // no page of its own to send a user to yet
            (TutorialScene.AiOmp, "omp", env.Omp, "https://github.com/can1357/oh-my-pi"),
        })
        {
            var tile = new PictureTile(TutorialArt.For(picture, 132, scene), scene, name);
            if (here)
            {
                tile.About = "✓ " + Tr("설치됨", "Installed");
                tile.AboutAccent = true;
            }
            else
            {
                tile.Dim = true;
                if (guide is null) tile.About = Tr("설치되지 않음", "Not installed");
                else tile.Link = (Tr("설치 안내 ↗", "Setup guide ↗"), () => Launch.Open(guide));
            }
            grid.Add(tile);
        }
        page.Children.Add(grid);
        if (many)
        {
            Segmented Pick(string value, Action<string> set) => Choice(new Segmented(providers.Select(p => (p, ProviderName(p), (FrameworkElement?)TutorialArt.Logo(p, 14))), value), v =>
            {
                set(v);
                Settings.Save();
            });
            page.Children.Add(Under(new ThumbRow(TutorialArt.For(TutorialScene.ThumbChat, 64, 38), Tr("AI 채팅", "AI Chat"), null, Pick(s.AiProvider, v => s.AiProvider = v)), 12));
            page.Children.Add(Under(new ThumbRow(TutorialArt.For(TutorialScene.ThumbTranslate, 64, 38), Tr("번역과 AI 빠른 동작", "Translation and quick AI"), null,
                Pick(s.TranslateProvider, v => s.TranslateProvider = v)), 6));
        }
        page.Children.Add(Under(TutorialParts.Note(Tr("로그인은 각 프로그램에서 본인 계정으로 합니다.", "You sign in inside each program with your own account.")), many ? 14 : 16));
    }

    /// A chat provider by the name of its program.
    private static string ProviderName(string provider) => provider switch { "chatgpt" => "Codex", "omp" => "omp", _ => "Claude Code" };
}
