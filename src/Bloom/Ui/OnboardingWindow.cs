using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The first run: a few screens that set how Bloom opens, which browser it hands searches to, how it looks and which
/// of its tools sit on the home, say which helper programs were found, and show the handful of keys worth knowing.
/// Shown once (Settings.Onboarded); "Bloom 둘러보기" opens it again. Enter / → next, ← back, Esc skips; every choice
/// is saved the moment it is made, the home tools when the window closes.
internal sealed class OnboardingWindow : Window
{
    private readonly Host host;
    private readonly Grid root = new() { Background = Theme.P.Solid };
    private readonly StackPanel petals = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl page = new() { Focusable = false };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock skip;
    private int step;
    private readonly List<Func<FrameworkElement>> steps;

    /// The tools offered for the home's favorites: command id, what it is for, on by default.
    private static readonly (string id, string about, bool on)[] Tools =
    {
        ("cmd:quicklinks", Tr("고른 브라우저로 바로 검색 · 사이트 바로 가기 (예: yt 고양이)", "Search in your browser · site shortcuts (e.g. yt cats)"), true),
        ("cmd:files", Tr("PC 안의 파일을 이름으로 바로 찾기 (Everything)", "Find files on this PC by name (Everything)"), true),
        ("cmd:clipboard", Tr("복사했던 글 · 그림 · 파일을 다시 붙여 넣기", "Paste text, pictures and files you copied before"), true),
        ("cmd:timer", Tr("\"10분 타이머\", \"3시에 회의 알림\"처럼 적어서 시작", "Type \"10 min timer\" or \"remind me at 3pm meeting\""), true),
        ("cmd:calendar", Tr("알림을 달력으로 보기", "See reminders on a calendar"), true),
        ("cmd:lens", Tr("화면 영역을 골라 AI에게 묻기 · 번역 · 글자 복사", "Pick a screen area: ask AI, translate, copy text"), true),
        ("cmd:space", Tr("디스크를 지도로 보고 큰 파일 찾아 정리", "See your disk as a map and clear big files"), true),
        ("cmd:color", Tr("화면 색 찍기 · HEX 복사 · 그라데이션 추천", "Pick colours · copy HEX · gradient ideas"), false),
        ("cmd:ai", Tr("Claude · ChatGPT에게 바로 묻기", "Ask Claude or ChatGPT right away"), false),
        ("cmd:processes", Tr("멈춘 프로그램을 CPU · 메모리를 보고 끝내기", "End stuck programs by CPU and memory"), false),
        ("cmd:commands", Tr("스크립트 · 자주 쓰는 글 · 여러 동작 묶음을 단축어로 실행", "Run scripts, saved texts and chains of actions by keyword"), false),
    };

    private HashSet<string> picked = new(); // switched on for the home
    private HashSet<string> already = new(); // on the home when the screens opened (switched off = taken out)
    private bool toolsDone;

    public OnboardingWindow(Host host)
    {
        this.host = host;
        Title = Tr("Bloom 시작", "Bloom setup");
        Width = 640;
        Height = 580;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);
        steps = new() { Welcome, Opening, BrowserPick, Looks, HomeTools, Helpers };
        if (FoundAis().Count > 0) steps.Add(PickAi); // which AI does what, when there is one to pick
        if (FoundAis().Contains("chatgpt")) steps.Add(PickUsage); // how ChatGPT's usage is asked, when Codex is there
        steps.Add(KeysToKnow);
        steps.Add(Done);
        ResetTools();

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        page.Margin = new Thickness(44, 36, 44, 16);
        root.Children.Add(page);
        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(line, 1);
        root.Children.Add(line);
        var foot = new Grid { Margin = new Thickness(44, 0, 28, 0) };
        foot.Children.Add(petals);
        skip = Theme.T(Tr("건너뛰기", "Skip"), 12.5, Theme.P.TextFaint, Theme.Medium, false);
        skip.Cursor = Cursors.Hand;
        skip.VerticalAlignment = VerticalAlignment.Center;
        skip.HorizontalAlignment = HorizontalAlignment.Center;
        skip.MouseLeftButtonUp += (_, _) => Finish(false);
        foot.Children.Add(skip);
        foot.Children.Add(buttons);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox { Tag: Func<Key, bool> fieldKey }) // a number being typed (DurationField)
            {
                e.Handled = fieldKey(e.Key);
                return;
            }
            switch (e.Key)
            {
                case Key.Enter or Key.Right when Keyboard.FocusedElement is not TextBox:
                    Next();
                    e.Handled = true;
                    break;
                case Key.Left when step > 0:
                    Go(step - 1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Finish(false);
                    e.Handled = true;
                    break;
            }
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
        Theme.Changed += OnTheme;
        Closed += (_, _) =>
        {
            Theme.Changed -= OnTheme;
            MarkDone(); // closing the window counts as seen
            AddTools();
        };
        Go(0);
    }

    private void OnTheme()
    {
        Shots.SolidCaption(this);
        Go(step); // swatches and switches take the new colours
    }

    public void Go(int i)
    {
        step = Math.Clamp(i, 0, steps.Count - 1);
        page.Content = steps[step]();
        petals.Children.Clear();
        for (int k = 0; k < steps.Count; k++) // the step marks: Row's selection bar, lying down
            petals.Children.Add(new Rectangle { Width = 16, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = k == step ? Theme.P.Accent : Theme.P.Track, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true });
        buttons.Children.Clear();
        if (step > 0) buttons.Children.Add(ActionButton.Make(Tr("이전", "Back"), false, () => Go(step - 1)));
        bool last = step == steps.Count - 1;
        buttons.Children.Add(ActionButton.Make(last ? Tr("Bloom 열기", "Open Bloom") : step == 0 ? Tr("시작하기", "Get started") : Tr("다음", "Next"), true, Next));
        skip.Visibility = last || step == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    private void Next()
    {
        if (step < steps.Count - 1) Go(step + 1);
        else Finish(true);
    }

    private void Finish(bool open)
    {
        MarkDone();
        Close();
        if (open) host.Summon();
    }

    private static void MarkDone()
    {
        if (Settings.Current.Onboarded == true) return;
        Settings.Current.Onboarded = true;
        Settings.Save();
    }

    public void Capture(string file) => Shots.Save(root, file);

    public int StepCount => steps.Count;

    public void ToggleToolForQa(string id)
    {
        if (!picked.Remove(id)) picked.Add(id);
        Go(step);
    }

    // ── the screens ─────────────────────────────────────────────────────────────────────────────
    private FrameworkElement Welcome()
    {
        var sp = Column(Tr("검색과 실행", "Search and launch"), Tr("Win 키를 누르고 입력하면 앱 실행, 계산, 파일 검색, AI 채팅, 타이머를 사용할 수 있습니다.\n실행 방법과 사용할 기능을 선택하세요.", "Press Win and type to open apps, calculate, find files, chat with AI or set timers.\nChoose how to open Bloom and which features to use."));
        var mark = Launcher.MarkImage(72);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.Margin = new Thickness(0, 0, 0, 26);
        sp.Children.Insert(0, mark);
        var import = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        import.Children.Add(Theme.T(Tr("다른 PC의 설정 가져오기", "Settings from another PC"), 13, Theme.P.TextFaint, Theme.Regular, false));
        var link = Link(Tr("설정 불러오기", "Import settings"), () =>
        {
            if (!host.ImportSettings(this)) return;
            ResetTools(); // the imported favorites are the home
            Go(steps.Count - 1);
        });
        link.Margin = new Thickness(10, 0, 0, 0);
        import.Children.Add(link);
        sp.Children.Add(import);
        // the language first of all: a pick restarts Bloom, which opens these screens again in it
        var lang = new Segmented(Lang.Choices.Where(c => c.value != "auto").ToArray(), Code);
        lang.Picked += v =>
        {
            if (v == Code) return;
            Settings.Current.Language = v;
            Settings.Flush();
            host.Restart();
        };
        var langRow = Labeled("언어 · Language", lang);
        langRow.Margin = new Thickness(0, 22, 0, 0);
        sp.Children.Add(langRow);
        return sp;
    }

    private FrameworkElement Opening()
    {
        var s = Settings.Current;
        var sp = Column(Tr("Bloom 실행 방법", "Open Bloom"), Tr("두 단축키를 함께 사용할 수 있습니다.", "Both shortcuts can be on."));
        sp.Children.Add(SwitchRow(Tr("Win 키로 열기", "Open with the Win key"), Tr("Win 키만 눌렀다 떼면 Bloom이 열립니다. Win+E 같은 조합은 그대로입니다.", "Tap Win alone and Bloom opens. Combinations like Win+E still work."), s.WinKey, v =>
        {
            s.WinKey = v;
            host.ApplyHotkeys();
        }));
        sp.Children.Add(SwitchRow(Tr("Alt+Space로 열기", "Open with Alt+Space"), Tr("Alt+Space를 눌러 Bloom을 엽니다.", "Press Alt+Space to open Bloom."), s.AltSpace, v =>
        {
            s.AltSpace = v;
            host.ApplyHotkeys();
        }));
        sp.Children.Add(SwitchRow(Tr("Windows 시작할 때 함께 켜기", "Start with Windows"), Tr("Windows에 로그인하면 트레이에서 실행됩니다.", "Runs in the tray when you sign in to Windows."), s.Autostart, v =>
        {
            s.Autostart = v;
            if (!Paths.IsDev) Autostart.Set(v);
        }));
        return sp;
    }

    private FrameworkElement BrowserPick()
    {
        var s = Settings.Current;
        var sp = Column(Tr("검색과 링크를 열 브라우저", "Browser for searches and links"), Tr("검색어를 적고 '웹 검색'을 고르면, 그리고 바로 가기와 링크도 이 브라우저로 엽니다.", "Web searches, shortcuts and links open in this browser."));
        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            string now = Browser.Web.Picked?.Key ?? "default";
            foreach (var (value, label) in Browser.Web.Choices)
            {
                var b = Browser.Web.Installed.FirstOrDefault(x => x.Key == value);
                var icon = b is null ? Icons.Tile(G.Globe, false, 26) : Icons.Build(IconSpec.Shell(b.Exe), 26);
                list.Children.Add(Option(icon, label, b is null ? Tr("Windows 설정에서 정한 브라우저", "The browser set in Windows") : null, value == now, () =>
                {
                    s.WebBrowser = value;
                    Settings.Save();
                    Build();
                }));
            }
        }
        Build();
        sp.Children.Add(Scroll(list, 230));
        var seg = new Segmented(Browser.Web.Engines.Select(e => (e.key, e.name)), s.SearchEngine);
        seg.Picked += v =>
        {
            s.SearchEngine = v;
            Settings.Save();
        };
        var engine = Labeled(Tr("검색 엔진", "Search engine"), seg);
        engine.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(engine);
        return sp;
    }

    private FrameworkElement HomeTools()
    {
        var sp = Column(Tr("홈에 둘 기능", "Features on the home"), Tr("선택한 기능이 즐겨찾기에 추가됩니다. Ctrl+K로 빼거나 옮길 수 있습니다.", "Adds selected features to favorites. Ctrl+K removes or moves them."));
        var list = new StackPanel();
        var note = Theme.T("", 12.5, Theme.P.TextDim, Theme.Regular, false);
        note.Margin = new Thickness(2, 0, 0, 10);
        void Build()
        {
            note.Text = Tr($"사용할 기능만 즐겨찾기에 추가 · 현재 {picked.Count}개", $"Enabled features added to favorites · {picked.Count} selected");
            list.Children.Clear();
            foreach (var (id, about, _) in Tools)
            {
                if (SystemItems.Find(id) is not { } e) continue;
                void Toggle()
                {
                    if (!picked.Remove(id)) picked.Add(id);
                    Build();
                }
                list.Children.Add(Option(Icons.Tile(e.Glyph, e.Glyph == G.Sparkle, 26), e.Title, about, picked.Contains(id), Toggle, check: true));
            }
        }
        Build();
        sp.Children.Add(note);
        sp.Children.Add(Scroll(list, 300));
        return sp;
    }

    /// The switches start from the home as it is: the tools already in the favorites, or the default ones on a first run.
    private void ResetTools()
    {
        already = Tools.Where(t => Favorites.Has(t.id)).Select(t => t.id).ToHashSet();
        picked = Favorites.Current.Items.Count == 0 ? Tools.Where(t => t.on).Select(t => t.id).ToHashSet() : new HashSet<string>(already);
    }

    /// The home screen's switches into the favorites (once): the ones switched on go into the row, the ones switched
    /// off that were there come out.
    private void AddTools()
    {
        if (toolsDone) return;
        toolsDone = true;
        foreach (var id in already.Where(id => !picked.Contains(id))) Favorites.Remove(id);
        foreach (var t in Tools)
            if (picked.Contains(t.id) && !Favorites.Has(t.id) && SystemItems.Find(t.id) is { } e)
                Favorites.Add(new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target });
    }

    private FrameworkElement Looks()
    {
        var s = Settings.Current;
        var sp = Column(Tr("화면 모양", "Appearance"), Tr("선택 즉시 적용", "Applies immediately"));
        var seg = new Segmented(new[] { ("system", Tr("시스템", "System")), ("light", Tr("밝게", "Light")), ("dark", Tr("어둡게", "Dark")) }, s.Theme);
        seg.Picked += v =>
        {
            s.Theme = v;
            Settings.Save();
            Theme.Apply(v);
        };
        sp.Children.Add(Labeled(Tr("테마", "Theme"), seg));
        var accent = Accents.Find(s.Accent) ?? Accents.All[0];
        var sw = new Swatches(Accents.All.Select(a => (a.Key, a.Name, Accents.Swatch(a, Theme.P.Dark))), accent.Key);
        sw.Picked += v =>
        {
            s.Accent = v;
            Settings.Save();
            Theme.Apply(s.Theme);
        };
        sp.Children.Add(Labeled(Tr("색", "Colour"), sw));
        // how it draws: the CPU by default; the GPU is experimental (it froze the whole screen for seconds on one PC)
        var draw = new Segmented(new[] { ("cpu", Tr("CPU (기본)", "CPU (default)")), ("gpu", Tr("GPU (실험적)", "GPU (experimental)")) }, s.GpuDrawing ? "gpu" : "cpu");
        draw.Picked += v =>
        {
            s.GpuDrawing = v == "gpu";
            Settings.Save();
        };
        sp.Children.Add(Labeled(Tr("그리기", "Drawing"), draw));
        var about = Theme.T(Hangul.KeepAll(Tr("기본은 CPU로 그립니다. GPU로 그리기는 실험적 기능이며, 일부 PC에서는 화면이 끊기거나 몇 초 동안 멈출 수 있습니다. 다음 실행부터 적용됩니다.", "Bloom draws with the CPU by default. GPU drawing is experimental: some PCs may stutter or freeze for seconds. Applies at next start.")), 12, Theme.P.TextFaint, Theme.Regular, false);
        about.TextWrapping = TextWrapping.Wrap;
        about.Margin = new Thickness(2, 10, 2, 0);
        sp.Children.Add(about);
        return sp;
    }

    private FrameworkElement Helpers()
    {
        var sp = Column(Tr("함께 쓰는 프로그램", "Programs Bloom works with"), Tr("연동할 프로그램을 공식 페이지에서 설치하세요. 설치하지 않아도 Bloom의 다른 기능은 사용할 수 있습니다.", "Bloom works without them; each one turns its feature on. Install them from their official pages."));
        sp.Children.Add(Found("Everything", Tr("파일 이름으로 PC 전체를 바로 찾기", "Find any file on the PC by name"), EverythingClient.ExePath() is not null, "https://www.voidtools.com/"));
        sp.Children.Add(Found("Claude Code CLI", Tr("AI 채팅 · Bloom Lens · 번역 (본인 계정으로 로그인)", "AI chat · Bloom Lens · translation (with your own login)"), ClaudeSession.FindCli(Settings.Current.ClaudePath) is not null, "https://docs.anthropic.com/en/docs/claude-code"));
        sp.Children.Add(Found("Codex CLI", Tr("ChatGPT로 대화 · 번역 (본인 계정으로 로그인)", "Chat and translate with ChatGPT (with your own login)"), CodexSession.FindCli(Settings.Current.CodexPath) is not null, "https://github.com/openai/codex"));
        sp.Children.Add(Found("omp", Tr("AI 채팅 · Bloom Lens · 번역 — 로그인해 둔 여러 모델 중에서 선택 (omp login)", "AI chat · Bloom Lens · translation — pick among the models you signed in to (omp login)"), OmpCli.FindCli(Settings.Current.OmpPath) is not null, "https://github.com/can1357/oh-my-pi"));
        return sp;
    }

    /// The AIs found on this PC (Claude Code, Codex, omp), in the order offered.
    private static List<string> FoundAis()
    {
        var s = Settings.Current;
        var ais = new List<string>();
        if (ClaudeSession.FindCli(s.ClaudePath) is not null) ais.Add("claude");
        if (CodexSession.FindCli(s.CodexPath) is not null) ais.Add("chatgpt");
        if (OmpCli.FindCli(s.OmpPath) is not null) ais.Add("omp");
        return ais;
    }

    /// Which AI answers in the chat and which reads the screen for Bloom Lens and translation — only the ones found,
    /// each said in one line. A step of its own, shown when at least one is found.
    private FrameworkElement PickAi()
    {
        var s = Settings.Current;
        var ais = FoundAis();
        if (!ais.Contains(s.AiProvider)) s.AiProvider = ais[0];
        if (!ais.Contains(s.TranslateProvider)) s.TranslateProvider = ais[0];
        Settings.Save();
        var sp = Column(Tr("기능별 AI 선택", "Choose AI by feature"), Tr("이 PC에서 찾은 AI만 선택할 수 있습니다.", "Choose from AI found on this PC."));
        // beside the chat's label: when an idle chat's CLI closes (its memory back), in minutes or hours
        var idle = new DurationField(s.AiIdleMinutes, 24 * 60, DurationField.MinutesHours) { Done = () => Focus() };
        idle.Changed += v =>
        {
            s.AiIdleMinutes = v;
            Settings.Save();
        };
        var idleLabel = Theme.T(Tr("대화 메모리 해제 시간", "Release idle chat memory after"), 12.5, Theme.P.TextFaint, Theme.Semi, false);
        idleLabel.VerticalAlignment = VerticalAlignment.Center;
        idleLabel.Margin = new Thickness(0, 0, 10, 0);
        var idleRow = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = Tr("사용하지 않은 대화의 메모리 해제 · 0이면 유지", "Releases idle chat memory · 0 = keep it"), Children = { idleLabel, idle } };
        sp.Children.Add(AiChoice(Tr("AI 채팅", "AI chat"), ais, s.AiProvider, v => s.AiProvider = v, v => v switch
        {
            "chatgpt" => Tr("Codex로 답변하고 승인한 PC 작업을 실행합니다.", "Answers through Codex and runs approved PC actions."),
            "omp" => Tr("omp로 답변합니다. 로그인해 둔 여러 모델 중에서 고르고, 승인한 PC 작업을 실행합니다.", "Answers through omp: pick among the models you signed in to; runs approved PC actions."),
            _ => Tr("Claude Code로 답변하고 승인한 PC 작업을 실행합니다.", "Answers through Claude Code and runs approved PC actions."),
        }, idleRow));
        var lens = AiChoice(Tr("Bloom Lens · 번역", "Bloom Lens · translation"), ais, s.TranslateProvider, v => s.TranslateProvider = v, v => v switch
        {
            "chatgpt" => Tr("ChatGPT로 화면 글자를 읽고 번역합니다.", "Reads and translates screen text with ChatGPT."),
            "omp" => Tr("omp의 빠른 작업 모델로 화면 글자를 읽고 번역합니다. 모델은 설정에서 고릅니다.", "Reads and translates screen text with omp's quick-job model, set in Settings."),
            _ => Tr("Claude Haiku로 화면 글자를 읽고 번역합니다.", "Reads and translates screen text with Claude Haiku."),
        });
        lens.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(lens);
        return sp;
    }

    /// How the home asks ChatGPT's usage: Codex's own app-server per account, or one HTTPS request with its sign-in.
    /// 설정 > AI 계정 has the same choice, with the accounts.
    private FrameworkElement PickUsage()
    {
        var s = Settings.Current;
        var sp = Column(Tr("ChatGPT 사용량 조회", "ChatGPT usage checks"), Tr("홈의 AI 사용량을 받는 방식입니다. 계정 추가와 AI 채팅 계정은 설정 > AI 계정에서 고릅니다.", "How the home gets ChatGPT's usage. Add accounts and pick the AI chat account in Settings > AI accounts."));
        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            foreach (var (value, name, about) in new[]
            {
                ("cli", Tr("공식 CLI", "Official CLI"), Tr("계정마다 Codex를 잠깐 실행 · 1회 약 110 MB · 1초", "Runs Codex briefly per account · ~110 MB for 1 s each")),
                ("light", Tr("가벼운 방식", "Lightweight"), Tr("Codex 로그인으로 HTTPS 요청 1번 · 비공식 경로라 바뀔 수 있음", "One HTTPS request with the Codex sign-in · unofficial, may change")),
            })
                list.Children.Add(Option(Icons.Tile(value == "cli" ? G.Code : G.Speed, false, 26), name, about, (s.ChatGptUsage == "light" ? "light" : "cli") == value, () =>
                {
                    s.ChatGptUsage = value;
                    Settings.Save();
                    Build();
                }));
        }
        Build();
        sp.Children.Add(list);
        return sp;
    }

    /// A job (with `side` at the right of its label), then one row per AI found: its name and what it is like in one
    /// line; the picked one ticked in rose.
    private static FrameworkElement AiChoice(string job, List<string> ais, string value, Action<string> set, Func<string, string> about, FrameworkElement? side = null)
    {
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 8), LastChildFill = false };
        if (side is not null)
        {
            DockPanel.SetDock(side, Dock.Right);
            head.Children.Add(side);
        }
        var label = Theme.T(job, 12.5, Theme.P.TextFaint, Theme.Semi, false);
        label.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(label);
        sp.Children.Add(head);
        var rows = new List<(string ai, SmoothBorder card, TextBlock check)>();
        void Paint(string picked)
        {
            foreach (var (ai, card, check) in rows)
            {
                bool on = ai == picked;
                card.BorderBrush = on ? Theme.P.Accent : Theme.P.CardLine;
                check.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
        }
        foreach (string ai in ais)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Theme.T(ai == "chatgpt" ? "ChatGPT" : ai == "omp" ? "omp" : "Claude", 14, Theme.P.Text, Theme.Semi));
            var line = Theme.T(about(ai), 12, Theme.P.TextDim);
            line.Margin = new Thickness(0, 3, 12, 0);
            text.Children.Add(line);
            g.Children.Add(text);
            var check = Theme.Glyph(G.Check, 13, Theme.P.Accent);
            check.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(check, 1);
            g.Children.Add(check);
            var card = new SmoothBorder
            {
                Background = Theme.P.Card,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 10, 16, 11),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = g,
            };
            string pick = ai;
            card.MouseLeftButtonUp += (_, e) =>
            {
                set(pick);
                Settings.Save();
                Paint(pick);
                e.Handled = true;
            };
            rows.Add((ai, card, check));
            sp.Children.Add(card);
        }
        Paint(value);
        return sp;
    }

    private FrameworkElement KeysToKnow()
    {
        var sp = Column(Tr("기본 단축키", "Basic shortcuts"), null);
        sp.Children.Add(Tip("Win", Tr("열고 적기 — 앱, 설정, 계산(12*3), 파일, 웹 검색", "Open and type — apps, settings, maths (12*3), files, web search")));
        sp.Children.Add(Tip("Tab", Tr("적은 말을 그대로 AI에게 묻기", "Ask AI what you typed")));
        sp.Children.Add(Tip("Ctrl+K", Tr("더보기 — 고른 항목으로 할 수 있는 일 전부", "More — everything you can do with the selected item")));
        sp.Children.Add(Tip("Ctrl+Alt+L", Tr("Bloom Lens — 화면 영역을 AI에게 묻기 · 번역 · 글자 복사", "Bloom Lens — ask AI about a screen area, translate, copy text")));
        sp.Children.Add(Tip("Ctrl+Alt+V", Tr("클립보드 기록", "Clipboard history")));
        var line = Theme.T(Tr("문장으로도 됩니다:  \"10분 타이머\"  \"3시 반에 회의 알림\"  \"#ff5a8a\"", "Sentences work too:  \"10 min timer\"  \"remind me at 3:30pm meeting\"  \"#ff5a8a\""), 13, Theme.P.TextDim, Theme.Regular, false);
        line.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(line);
        return sp;
    }

    private FrameworkElement Done()
    {
        var sp = Column(Tr("설정 완료", "Setup complete"), Tr("선택한 단축키로 Bloom을 여세요. 설정과 사용 안내는 런처에서 다시 열 수 있습니다.", "Open Bloom with the shortcut you chose. Settings and the tour are available from the launcher."));
        var mark = Launcher.MarkImage(56);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.Margin = new Thickness(0, 0, 0, 24);
        sp.Children.Insert(0, mark);
        sp.Children.Add(ToolUi.TextButton(Tr("Bloom 설정 열기", "Open Bloom settings"), () => host.OpenSettings("general")));
        return sp;
    }

    // ── pieces ──────────────────────────────────────────────────────────────────────────────────
    private static StackPanel Column(string title, string? body)
    {
        var sp = new StackPanel();
        var t = Theme.T(title, 26, Theme.P.Text, Theme.Semi, false);
        t.Margin = new Thickness(0, 0, 0, 10);
        sp.Children.Add(t);
        if (body is not null)
        {
            var b = Theme.T(Hangul.KeepAll(body), 14, Theme.P.TextDim, Theme.Regular, false);
            b.TextWrapping = TextWrapping.Wrap;
            b.LineHeight = 22;
            b.Margin = new Thickness(0, 0, 0, 26);
            sp.Children.Add(b);
        }
        else sp.Children.Add(new FrameworkElement { Height = 16 });
        return sp;
    }

    private static FrameworkElement Card(UIElement inner) => new SmoothBorder
    {
        Background = Theme.P.Card,
        BorderBrush = Theme.P.CardLine,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 12, 14, 12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = inner,
    };

    private static FrameworkElement SwitchRow(string title, string sub, bool value, Action<bool> set)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(title, 14, Theme.P.Text, Theme.Medium));
        var st = Theme.T(sub, 12, Theme.P.TextFaint);
        st.Margin = new Thickness(0, 3, 12, 0);
        text.Children.Add(st);
        g.Children.Add(text);
        var sw = new Switch(value);
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        var card = Card(g);
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) =>
        {
            sw.On = !sw.On;
            set(sw.On);
            Settings.Save();
        };
        return card;
    }

    /// A choice line: icon, name, what it is; a rose ring (one of several) or a switch (any of several) shows the state.
    private static FrameworkElement Option(FrameworkElement icon, string name, string? about, bool on, Action? click, bool check = false)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(name, 13.5, Theme.P.Text, Theme.Medium));
        if (about is not null)
        {
            var a = Theme.T(about, 12, Theme.P.TextFaint);
            a.Margin = new Thickness(0, 2, 8, 0);
            text.Children.Add(a);
        }
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (check)
        {
            var sw = new Switch(on) { IsHitTestVisible = false };
            Grid.SetColumn(sw, 2);
            g.Children.Add(sw);
        }
        else if (click is not null && on)
        {
            var mark = Theme.Glyph(G.Check, 13, Theme.P.Accent);
            mark.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(mark, 2);
            g.Children.Add(mark);
        }
        var card = new SmoothBorder
        {
            Background = Theme.P.Card,
            BorderBrush = on && !check && click is not null ? Theme.P.Accent : Theme.P.CardLine,
            BorderThickness = new Thickness(on && !check && click is not null ? 1.5 : 1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = g,
        };
        if (click is not null)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, e) =>
            {
                click();
                e.Handled = true;
            };
        }
        return card;
    }

    private static ScrollViewer Scroll(UIElement content, double max) => new()
    {
        Content = content,
        MaxHeight = max,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
        Template = Launcher.ThinScroll(),
    };

    private static FrameworkElement Labeled(string label, UIElement control)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Theme.T(label, 14, Theme.P.Text, Theme.Medium);
        l.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(l);
        Grid.SetColumn((FrameworkElement)control, 1);
        g.Children.Add(control);
        return Card(g);
    }

    private static FrameworkElement Found(string name, string what, bool found, string url)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Tile(found ? G.Check : G.Info, found, 28);
        icon.Margin = new Thickness(0, 0, 12, 0);
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(name, 14, Theme.P.Text, Theme.Medium));
        var st = Theme.T(what, 12, Theme.P.TextFaint);
        st.Margin = new Thickness(0, 3, 12, 0);
        text.Children.Add(st);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        FrameworkElement state = found
            ? Theme.T(Tr("설치됨", "Installed"), 12.5, Theme.P.AccentText, Theme.Medium, false)
            : Link(Tr("설치 안내  ↗", "How to install  ↗"), () => Launch.Open(url));
        state.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(state, 2);
        g.Children.Add(state);
        return Card(g);
    }

    private static TextBlock Link(string label, Action click)
    {
        var t = Theme.T(label, 12.5, Theme.P.AccentText, Theme.Medium, false);
        t.Cursor = Cursors.Hand;
        t.TextDecorations = TextDecorations.Underline;
        t.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return t;
    }

    private static FrameworkElement Tip(string keys, string what)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caps = Ui.Keys.Caps(keys);
        caps.HorizontalAlignment = HorizontalAlignment.Left;
        caps.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(caps);
        var t = Theme.T(what, 14, Theme.P.Text, Theme.Regular);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }
}
