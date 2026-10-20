using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

internal sealed partial class Host
{
    public readonly Options Opt;
    public AppIndex Apps = null!;
    public EverythingClient Everything = null!;
    public Clip.ClipboardHistory Clipboard = null!;
    public readonly Processes Processes = new();
    public Hotkeys Shortcuts = null!;
    public Ocr.OcrOutput? LastOcr;
    private bool ocrBusy;
    private DispatcherTimer? procTick;
    public Chats Chats = null!;
    public App.Mcp.McpServer Mcp = null!;
    public Engine Engine = null!;
    public Launcher Window = null!;
    public ToolDock Tools = null!;
    private SettingsWindow? settingsWindow;
    private ChatWindow? chatWindow;

    /// The AI 채팅 window while it shows (else zero): what a tool the AI brings up floats beside.
    public IntPtr ChatWindowHandle => chatWindow is { IsVisible: true } c ? new System.Windows.Interop.WindowInteropHelper(c).Handle : IntPtr.Zero;
    private WinKeyHook? winKey;
    private ElevatedWinKey? winKeyAdmin;
    private bool winKeyOffered; // the reinstall of a helper of another protocol was offered this run
    private StallWatch? stalls;
    private Tray? tray;
    private Dispatcher ui = null!;
    private DispatcherTimer? dataTick;
    private DispatcherTimer? alarmTick;
    private bool colorBusy;
    private bool dataDirty;
    private int dataQueued;

    public Host(Options opt) => Opt = opt;

    public void Start()
    {
        ui = Application.Current.Dispatcher;
        Demo.On = Opt.Demo || Opt.ResponsiveRounds is not null; // before anything reads the PC
        if (Opt.Lang is { } lang) Lang.Init(lang); // before the settings, which would fix it otherwise
        Settings.Load();
        Usage.Load();
        QueryMemory.Load();
        Favorites.Load();
        AppNames.Load();
        Quicklinks.Load();
        UserCommands.Load();
        Rates.Load();
        Timers.Load();
        Notes.Load();
        ColorBook.Load();
        if (!Settings.Current.GpuDrawing) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly; // 설정 › 성능: about 30 MB less without the GPU driver
        Theme.InitScale();
        Theme.Apply(Settings.Current.Theme);

        Apps = new AppIndex();
        Everything = new EverythingClient();
        Chats = new Chats(ui) { PreStartOn = Opt.CaptureDir is null && !Opt.Demo }; // QA pictures and the demo never start a CLI
        var tools = new App.Mcp.BloomTools(this, ui);
        Mcp = new App.Mcp.McpServer(tools);
        // Bloom's MCP server for the chats (설정 > PC 작업 · MCP): Claude Code asks the user itself; for Codex runs the
        // server asks in the chat (Approve)
        ClaudeSession.PcTools = () =>
        {
            if (!App.Mcp.BloomTools.OfferedTo("claude")) return null;
            Mcp.Start();
            return (Mcp.Server, App.Mcp.BloomTools.Doing);
        };
        ClaudeSession.PrepareMcp = McpCatalog.PreparePicked;
        CodexSession.PrepareMcp = McpCatalog.PreparePicked;
        CodexSession.Relay = () =>
        {
            Mcp.Start();
            return (App.Mcp.McpServer.Command, Mcp.Pipe);
        };
        CodexSession.PcTools = () =>
        {
            if (!App.Mcp.BloomTools.OfferedTo("chatgpt")) return null;
            Mcp.Start();
            return (App.Mcp.McpServer.Command, Mcp.Pipe, tools.Hint);
        };
        // omp chats: the same server, started per conversation from its .omp/mcp.json (Ai\OmpMcp.cs); Bloom asks in the chat
        OmpSession.PrepareMcp = McpCatalog.PreparePicked;
        OmpSession.Relay = CodexSession.Relay;
        OmpSession.PcTools = () =>
        {
            if (!App.Mcp.BloomTools.OfferedTo("omp")) return null;
            Mcp.Start();
            return (App.Mcp.McpServer.Command, Mcp.Pipe);
        };
        tools.Approve = (conversation, tool, input) => ui.InvokeAsync(() => Chats.AskInConversation(conversation, tool, input)).Task.Unwrap();
        Chats.Tools = tools;
        Engine = new Engine(this);
        Clipboard = new Clip.ClipboardHistory(ui);
        Icons.Start(ui);
        Window = new Launcher(this) { Capturing = Opt.CaptureDir is not null };
        Tools = new ToolDock(this);
        DocWindow.Gone += Window.TrimSoon;
        if (Opt.ResponsiveRounds is { } responsive)
        {
            _ = ResponsivenessBench.Run(this, ui, responsive);
            return; // synthetic sources only: no watchers, clipboard listeners, provider polling or global hooks
        }
        if (Opt.MdBenchFile is { } mdBench)
        {
            _ = MdBench.Run(this, mdBench, Opt.MdBenchIn, Opt.MdBenchRounds);
            return; // documents only, as the responsiveness probe
        }
        if (!Demo.On) _ = GptCatalog.RefreshAsync();
        if (!Demo.On && OmpCli.FindCli() is not null && (Settings.Current.AiProvider == "omp" || Settings.Current.TranslateProvider == "omp" || Settings.Current.OmpModels.Count > 0)) OmpCatalog.Ensure(); // `omp models`, once in 3 days
        _ = CodexAppHome.Sweep(); // a sign-in link a crash left in Bloom's Codex folders
        if (Opt.ChatFile is not null)
        {
            _ = Captures.Chat(this, Opt.ChatFile, Opt.QaModel ?? "haiku", Opt.QaEffort, Opt.QaAsk?.Split('|'));
            return;
        }
        if (Opt.WarmFile is not null)
        {
            _ = WarmBench.Run(this, Opt.WarmFile, Opt.QaModel, Opt.QaEffort);
            return;
        }
        if (Opt.SpaceDir is not null)
        {
            _ = Captures.Space(this, Opt.SpaceDir, Opt.SpacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return;
        }
        if (Opt.OcrFile is not null)
        {
            _ = Captures.OcrTest(this, Opt.OcrFile, Opt.QaTranslate);
            return;
        }
        if (Opt.PasteFile is not null)
        {
            _ = Captures.Paste(this, Opt.PasteFile, Opt.Display ?? 2);
            return;
        }
        if (Opt.GlassFile is not null)
        {
            Apps.Start();
            Window.Capturing = false;
            _ = Captures.Glass(this, Opt.GlassFile, Opt.Display ?? 2);
            return;
        }

        if (Opt.Demo) BeginDemo();
        Chats.Updated += t => Window.ChatChanged(t);
        Chats.SessionsChanged += () => Window.SessionsChanged();
        // a tool call waiting while no Bloom window is in front: a card says so
        Notices.OpenUpdates = () => OpenSettings("about", Tr("업데이트", "Update"));
        Notices.UpdateNow = UpdateNow;
        Chats.AskWaiting += (thread, ask) =>
        {
            if (!Window.IsActive && chatWindow?.IsActive != true) Notices.Ask(ask.Says, () => OpenChat(thread));
        };
        Rates.Updated += () => Later();
        RecentFiles.Updated += Later;
        OpenWindows.Updated += Later;
        Clipboard.Changed += () => Later();
        Quicklinks.Changed += () => Later();
        UserCommands.Changed += () => Later();
        Apps.Changed += () => ui.BeginInvoke(Later);
        Timers.Changed += Later;
        Timers.Changed += SyncAlarm;
        AiUsage.Updated += Later;
        // bursts of change events become one refresh, 300 ms after the first; the timer sleeps in between
        dataTick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(300) };
        dataTick.Tick += (_, _) =>
        {
            dataTick.Stop();
            Interlocked.Exchange(ref dataQueued, 0);
            if (!dataDirty) return;
            dataDirty = false;
            Window.DataChanged();
        };

        Apps.Start();
        if (Opt.CaptureDir is null)
        {
            // timers and reminders: due ones ring (also those that came due while Bloom was off), the countdowns tick
            alarmTick = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(1) };
            alarmTick.Tick += (_, _) =>
            {
                foreach (var e in Timers.TakeDue()) Ring(e);
                Window.TickLive();
            };
            SyncAlarm();
            AiUsage.Start(ui);
            Notices.Quiet = Opt.MeasureSeconds is not null || Opt.SummonRounds is not null;
            if (!Opt.Demo) Notices.Start(ui); // recording mode: no cards from the real Claude Code and Codex
            if (!Opt.Demo) ClaudeApprovals.Start(ui);
            if (Opt.MeasureSeconds is { } measure) MeasureLater(measure);
            if (Opt.SummonRounds is { } rounds) _ = SummonBench.Run(this, ui, rounds);
            if (!Opt.Demo) Clipboard.Start(); // recording mode: the PC's clipboard stays out, the samples stay in
            Shortcuts = new Hotkeys();
            ApplyHotkeys();
            if (!Opt.NoTray)
            {
                tray = new Tray();
                tray.Toggle += Summon;
                tray.OpenSettings += OpenSettings;
                tray.Quit += () => QuitForGood();
            }
            if (Settings.Current.Autostart && !Paths.IsDev && !Autostart.IsCurrent()) Autostart.Set(true);
            FileAssoc.Repair(); // Explorer's 연결 프로그램 for .md: this exe (an install moved, settings brought in)
            if (!Opt.Demo) Updates.Start(ui);
            // UI-thread stalls go to the log; watched once start-up has settled
            ui.BeginInvoke(DispatcherPriority.ApplicationIdle, () => stalls = new StallWatch(ui, Log.Write));
            _ = System.Threading.Tasks.Task.Run(() => StallWatch.RemoveOldDumps(System.IO.Path.Combine(Paths.Local, "stalls")));
            Window.Prewarm();
            Tools.Restore(); // floating tool windows come back now, attached ones with the launcher
            // started by Explorer's 연결 프로그램: the document only — no launcher, and the first-run screens wait for the next start
            if (Opt.Open is { } doc)
            {
                if (!OpenDoc(doc)) ui.BeginInvoke(() => DocHandoff.Tell(doc, DocHandoff.Refused)); // never silently
            }
            else if (Settings.Current.Onboarded != true) ShowOnboarding();
            else if (Opt.Settings) OpenSettings();
            else if (Opt.Show) Summon();
            if (!Opt.Demo && Opt.MeasureSeconds is null && Opt.SummonRounds is null && ReleaseNotes.JustUpdated())
            {
                // the first start on a new version: 바뀐 점 a moment later, without taking the focus
                var soon = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
                soon.Tick += (_, _) =>
                {
                    soon.Stop();
                    ShowWhatsNew(quiet: true);
                };
                soon.Start();
            }
        }
        else _ = Captures.Run(this, Opt.CaptureDir);
        Log.Write($"started {(Paths.IsDev ? "dev " + Paths.Data : "")} pid {Environment.ProcessId}");
    }

    /// 바뀐 점: this version's notes from the changelog (설정 › 정보, and once after an update with `quiet`: no focus).
    public void ShowWhatsNew(bool quiet = false)
    {
        string v = ReleaseNotes.Current;
        var w = new WhatsNewWindow(v, ReleaseNotes.For(v) ?? Tr("이번 버전의 변경 기록 없음", "No release notes for this version")) { ShowActivated = !quiet };
        w.Closed += (_, _) => Window.TrimSoon();
        w.Show();
    }

    /// QA (`--qa-measure <s>`): how much memory (and CPU time) this run holds after `seconds` of ordinary running, then
    /// again after the idle trim (MemoryTrim) → memory.txt in the data folder, then quit. Private bytes is the number to
    /// compare (the working set is trimmed while hidden).
    private void MeasureLater(int seconds)
    {
        var wait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(seconds) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            static string Line()
            {
                using var me = System.Diagnostics.Process.GetCurrentProcess();
                return $"private {me.PrivateMemorySize64 / 1048576.0:0.0} MB · working set {me.WorkingSet64 / 1048576.0:0.0} MB · managed {GC.GetTotalMemory(false) / 1048576.0:0.0} MB (allocated since start {GC.GetTotalAllocatedBytes() / 1048576.0:0} MB, {GC.CollectionCount(0)} GCs) · cpu {me.TotalProcessorTime.TotalSeconds:0.00} s";
            }
            string before = Line();
            MemoryTrim.Now(); // explicit diagnostic collection; routine idle cleanup does not force GC
            System.IO.File.WriteAllText(Paths.In("memory.txt"), before + "\n" + "after trim: " + Line() + "\n");
            Quit();
        };
        wait.Start();
    }

    /// Something the lists show changed (any thread): one refresh soon.
    private void Later()
    {
        dataDirty = true;
        if (Interlocked.Exchange(ref dataQueued, 1) == 0) ui.BeginInvoke(() => dataTick?.Start());
    }

    /// The once-a-second tick (due timers ring, countdowns move) runs only while a timer or reminder waits.
    private void SyncAlarm()
    {
        if (alarmTick is not null) alarmTick.IsEnabled = Timers.Current.Items.Count > 0;
    }

    /// A Bloom-only app name changed: names and order again, the list follows.
    public void AppsChanged() => Apps.ApplyNames();

    public void Summon() => Window.Summon(Opt.Display);

    /// Opens the hidden launcher straight into a view (no home built first); Chat = the caller shows the conversation.
    private void SummonInto(Mode mode, string text = "") => Window.Summon(Opt.Display, mode, text);

    /// A key's own Summon (hook thread or hotkey): the key is marked for SummonTrace, the opening runs on the UI thread.
    private void SummonByKey()
    {
        SummonTrace.Key();
        ui.BeginInvoke(Summon);
    }

    // ── hotkeys ─────────────────────────────────────────────────────────────────────────────────
    public string AltSpaceStatus => Shortcuts?.Status("altspace") ?? "off";

    public void ApplyHotkeys()
    {
        if (Opt.CaptureDir is not null) return;
        var s = Settings.Current;
        static string F(string feature, string keys) => Features.On(feature) ? keys : "off"; // 설정 > 기능 lets go of an off feature's keys
        Clipboard.Apply();
        Shortcuts?.Set("altspace", s.AltSpace ? "alt+space" : "off", SummonByKey);
        Shortcuts?.Set("clipboard", s.ClipHistory ? s.ClipHotkey : "off", () => ToggleView(Mode.Clipboard));
        Shortcuts?.Set("color", F("color", s.ColorHotkey), StartColor);
        Shortcuts?.Set("lens", F("lens", s.LensHotkey), () => StartLens("menu"));
        Shortcuts?.Set("ask", F("lens", s.AskHotkey), () => StartLens("ask"));
        Shortcuts?.Set("ocr", F("lens", s.OcrHotkey), () => StartLens("text"));
        Shortcuts?.Set("translate", F("lens", s.TranslateHotkey), () => StartLens("translate"));
        Shortcuts?.Set("live", F("lens", s.LiveHotkey), () => StartLens("live"));
        Shortcuts?.Set("capture", F("lens", s.CaptureHotkey), () => StartLens("capture"));
        Shortcuts?.Set("windows", F("windows", s.WindowsHotkey), () =>
        {
            OpenWindows.Invalidate();
            ToggleView(Mode.Windows);
        });
        Shortcuts?.Set("emoji", F("emoji", s.EmojiHotkey), () => ToggleView(Mode.Emoji));
        Shortcuts?.Set("quick", F("quick", s.QuickHotkey), () => StartQuick(true));
        Shortcuts?.Set("chat", F("chat", s.ChatHotkey), () => ui.BeginInvoke(ToggleChatWindow));
        // shortcuts given to single items (Ctrl+K › 단축키 지정), after Bloom's own, which win a clash
        var items = new HashSet<string>();
        foreach (var k in ItemKeys.All)
        {
            var entry = k.Item;
            items.Add("item:" + entry.Id);
            Shortcuts?.Set("item:" + entry.Id, Features.Shows(entry.Id) ? k.Combo : "off", () => ui.BeginInvoke(() => RunItemKey(entry)));
        }
        Shortcuts?.Drop("item:", items);
        bool wantHook = Settings.Current.WinKey && !Opt.NoHook;
        if (wantHook && winKey is null)
        {
            winKey = new WinKeyHook(SummonByKey);
            winKey.Start();
        }
        else if (!wantHook && winKey is not null)
        {
            winKey.Dispose();
            winKey = null;
        }
        // over apps run as administrator: the elevated helper beside the hook, never instead of it
        bool wantAdmin = wantHook && Settings.Current.WinKeyAdmin && ElevatedWinKey.Offered;
        if (wantAdmin && winKeyAdmin is null) StartWinKeyAdmin();
        else if (!wantAdmin && winKeyAdmin is not null)
        {
            winKeyAdmin.Dispose();
            winKeyAdmin = null;
        }
    }

    private void StartWinKeyAdmin()
    {
        switch (ElevatedWinKey.Check())
        {
            case ElevatedWinKey.Copy.Ready:
                winKeyAdmin = ElevatedWinKey.Start(SummonByKey);
                break;
            case ElevatedWinKey.Copy.None: // removed outside Bloom (Windows' Apps list): the switch is off again
                Log.Write("win-key helper not installed: switched off");
                Settings.Current.WinKeyAdmin = false;
                Settings.Save();
                break;
            case ElevatedWinKey.Copy.Other when !winKeyOffered:
                winKeyOffered = true;
                ui.BeginInvoke(OfferWinKeyReinstall);
                break;
        }
    }

    /// The installed helper speaks another protocol (an update changed it): installing it again needs the admin.
    private async void OfferWinKeyReinstall()
    {
        if (ConfirmWindow.Ask(null, Tr("Win 키 도우미 업데이트", "Update the Win-key helper"),
                Tr("이 버전의 Bloom에 맞게 Win 키 도우미를 다시 설치해야 합니다. 설치 전까지 관리자 앱 위에서는 Win 키로 Bloom이 열리지 않습니다.", "This version of Bloom needs the Win-key helper installed again. Until then, Win does not open Bloom over admin apps."),
                (Tr("다시 설치", "Reinstall"), MessageBoxResult.Yes), (Tr("나중에", "Later"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
        var r = await ElevatedWinKey.RunElevated("--install");
        if (r == ElevatedWinKey.Elevated.Done) ApplyHotkeys();
        else if (r == ElevatedWinKey.Elevated.Failed) TellWinKeyInstallFailed(null);
    }

    /// 설정 › 일반 › 관리자 앱 위에서도 Win 키: on installs the helper (UAC), off stops and removes it (UAC). Returns
    /// the switch's state after: a declined or failed install leaves it off.
    public async System.Threading.Tasks.Task<bool> SetWinKeyAdmin(bool on, System.Windows.Window? owner)
    {
        if (on)
        {
            var r = await ElevatedWinKey.RunElevated("--install");
            if (r != ElevatedWinKey.Elevated.Done)
            {
                if (r == ElevatedWinKey.Elevated.Failed) TellWinKeyInstallFailed(owner);
                return false;
            }
            Settings.Current.WinKeyAdmin = true;
            Settings.Save();
            ApplyHotkeys();
            return true;
        }
        Settings.Current.WinKeyAdmin = false;
        Settings.Save();
        ApplyHotkeys(); // the helper is told to stop before it is removed
        if (await ElevatedWinKey.RunElevated("--remove") != ElevatedWinKey.Elevated.Done)
            ConfirmWindow.Tell(owner, WinKeyLeftTitle, Tr("관리자 승인을 받지 못해 도우미가 설치된 채로 남았습니다. 꺼진 동안에는 동작하지 않습니다. Windows 설정 › 앱에서 ‘Bloom Win-key helper’를 삭제할 수 있습니다.", "Without admin approval the helper stays installed. It does nothing while off. Remove ‘Bloom Win-key helper’ in Windows Settings › Apps."));
        return false;
    }

    private static string WinKeyLeftTitle => Tr("Win 키 도우미 남음", "Win-key helper still installed");

    private static void TellWinKeyInstallFailed(System.Windows.Window? owner) =>
        ConfirmWindow.Tell(owner, Tr("Win 키 도우미 설치 실패", "Win-key helper install failed"), Tr("도우미를 설치하지 못했습니다. 다시 시도하세요.", "Could not install the helper. Try again."));

    public void OpenView(Mode mode, string text = "")
    {
        if (!Window.IsVisible) SummonInto(mode, text);
        else Window.Go(mode, text);
    }

    /// Bloom Space Cleaner (one window; a folder given starts a scan of it there).
    public void OpenSpace(string? path)
    {
        Window.Dismiss();
        if (spaceWindow is null)
        {
            spaceWindow = new Space.SpaceWindow(path);
            spaceWindow.Closed += (_, _) =>
            {
                spaceWindow = null;
            };
            spaceWindow.Show();
        }
        else if (path is not null) spaceWindow.Scan(path);
        if (spaceWindow.WindowState == WindowState.Minimized) spaceWindow.WindowState = WindowState.Normal;
        spaceWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(spaceWindow).Handle);
    }

    /// Kill Process: a snapshot now, then one every 1.5 s while the view is in front.
    private void OpenProcesses()
    {
        Processes.Reset();
        Processes.Sample();
        OpenView(Mode.Processes);
        if (procTick is null)
        {
            procTick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(1500) };
            procTick.Tick += (_, _) =>
            {
                if (!Window.IsVisible || Window.Mode != Mode.Processes)
                {
                    procTick.Stop();
                    return;
                }
                Task.Run(Processes.Sample).ContinueWith(_ => Later(), TaskScheduler.Default);
            };
        }
        procTick.Start();
    }

    public void EndProcess(ProcGroup g, bool force)
    {
        var (ended, refused) = Processes.End(g, force);
        string msg = refused > 0 && ended == 0 ? Tr($"‘{g.Name}’ 종료 실패 · 관리자 권한으로 실행 중이면 작업 관리자에서 종료하세요", $"Could not end ‘{g.Name}’. If it runs as administrator, close it in Task Manager.")
            : refused > 0 ? Tr($"{ended}개 종료 · {refused}개 종료 실패", $"{ended} ended · {refused} could not be ended")
            : force ? Tr($"‘{g.Name}’ 강제 종료 완료", $"Force-quit ‘{g.Name}’") : Tr($"‘{g.Name}’ 창 닫기 요청 완료", $"Close requested for ‘{g.Name}’");
        Window.ShowToast(msg);
        _ = Task.Delay(400).ContinueWith(_ =>
        {
            Processes.Sample();
            Later();
        }, TaskScheduler.Default);
    }

    /// Windows starts the shell again by itself; if it has not after 2.5 s, it is started here.
    public async void RestartExplorer(ProcGroup g)
    {
        Processes.End(g, true);
        Window.ShowToast(Tr("탐색기를 다시 시작하는 중…", "Restarting Explorer…"));
        await Task.Delay(2500);
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0) Launch.Open("explorer.exe");
        Processes.Sample();
        Later();
    }

    /// A view's own hotkey: opens it, or hides the launcher when that view is already in front.
    public void ToggleView(Mode mode)
    {
        if (Window.IsVisible && Window.IsActive && Window.Mode == mode)
        {
            Window.Dismiss();
            return;
        }
        OpenView(mode);
    }

    /// Settings live in their own window; a second call brings it back to the front.
    /// The first-run screens (also "Bloom 둘러보기").
    public void ShowOnboarding()
    {
        var w = new OnboardingWindow(this);
        w.Show();
        w.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(w).Handle);
    }

    public void OpenSettings() => OpenSettings(null);

    public void OpenSettings(string page, string filter)
    {
        OpenSettings(page);
        settingsWindow?.Filter(filter);
    }

    public void OpenSettings(string? page)
    {
        Window.Dismiss();
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(this);
            settingsWindow.Closed += (_, _) =>
            {
                settingsWindow = null;
                Window.TrimSoon();
            };
            settingsWindow.Activated += (_, _) => settingsWindow?.Refresh();
            settingsWindow.Show();
        }
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        if (page is not null) settingsWindow.Page(page);
        settingsWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(settingsWindow).Handle);
    }

    public void OpenChat(ChatThread? t)
    {
        if (!Window.IsVisible) SummonInto(Mode.Chat);
        Window.ShowChat(Recent(t) ?? Chats.New());
    }

    /// The conversation Tab picks up: the last one, while 이어가기 is on and it was used in the last 30 minutes.
    public ChatThread? TabThread => Settings.Current.TabContinuesLastThread && Chats.Latest is { } last
        && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last.Updated < 30 * 60 * 1000 && !last.Busy ? last : null;

    /// Ask in a conversation: the one given, else the one Tab picks up, else a new one (`fresh`: always a new one).
    public void Ask(string text, ChatThread? t, bool fresh = false)
    {
        if (t is null && !fresh) t = TabThread;
        t ??= Chats.New();
        if (!Window.IsVisible) SummonInto(Mode.Chat);
        Window.ShowChat(t);
        Chats.Send(t, text);
    }

    public void DeleteThread(ChatThread t) => DeleteThreads(new[] { t });

    /// Several at once (the chat window's 선택): the lists redraw once.
    public void DeleteThreads(IReadOnlyList<ChatThread> list)
    {
        foreach (var t in list)
        {
            Chats.Delete(t);
            Window.Forget(t);
        }
        chatWindow?.Removed(list);
        if (Window.Mode == Mode.Threads) Window.Refresh(false);
    }

    /// AI 채팅 in its own window (cmd:ai, Ctrl+Shift+O in the quick chat): the conversation given, else the last one
    /// while 이어가기 is on and it was used in the last 30 minutes, else a new one. An open window comes forward.
    public void OpenChatWindow(ChatThread? t = null, string? draft = null, IReadOnlyList<string>? files = null)
    {
        Window.Dismiss();
        if (chatWindow is null)
        {
            var last = Settings.Current.TabContinuesLastThread ? Recent(Chats.Latest) : null;
            chatWindow = new ChatWindow(this, t ?? last ?? Chats.New());
            chatWindow.Closed += (_, _) =>
            {
                chatWindow = null;
                Window.TrimSoon();
            };
            chatWindow.Show();
        }
        else if (t is not null) chatWindow.Open(t);
        if (draft is { Length: > 0 } || files is { Count: > 0 }) chatWindow.Draft(draft, files);
        if (chatWindow.WindowState == WindowState.Minimized) chatWindow.WindowState = WindowState.Normal;
        chatWindow.Activate();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(chatWindow).Handle);
    }

    /// The AI 채팅 shortcut: the window comes forward, or goes down when it is already in front.
    public void ToggleChatWindow()
    {
        if (chatWindow is { IsActive: true, WindowState: not WindowState.Minimized }) chatWindow.WindowState = WindowState.Minimized;
        else OpenChatWindow();
    }

    /// The conversation, if it was used in the last 30 minutes (an older one isn't "the last one" any more).
    private static ChatThread? Recent(ChatThread? t) => t is not null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t.Updated <= 30 * 60 * 1000 ? t : null;

    // ── import ──────────────────────────────────────────────────────────────────────────────────
    /// Pending debounced writes go to disk now (quit, restart, Windows signing out).
    public void SaveAll()
    {
        DocWindow.FlushAll(); // documents being written go to their files
        Chats.Flush(); // explicit save/export boundary; normal chat updates queue immutable snapshots
        Settings.Flush();
        Usage.Flush();
        QueryMemory.Flush();
        Favorites.Flush();
        AppNames.Flush();
        Quicklinks.Flush();
        UserCommands.Flush();
        Timers.Flush();
        Notes.Flush();
        ColorBook.Flush();
        Clipboard?.Flush();
    }

    /// `save: false` is the reset: nothing in memory goes back to the data that is about to be deleted.
    /// Quitting on purpose (the tray, 끝내기, --quit): a downloaded update goes in once Bloom has ended. `ask`: a document
    /// window with edits not in its file yet asks first (취소 keeps Bloom running); --quit doesn't (a script waits on it):
    /// Quit keeps them in a copy beside the file.
    public void QuitForGood(bool ask = true)
    {
        if (ask && !DocWindow.SettleAll()) return;
        Updates.ApplyOnQuit();
        Quit();
    }

    /// 설정 > 정보 > 다시 시작해서 업데이트: everything saved, then the update goes in and the new Bloom starts (unless a
    /// document's edits that can't be written yet are kept by 취소).
    public void UpdateNow()
    {
        if (!DocWindow.SettleAll()) return;
        stalls?.Dispose(); // the saving and shutting down below may hold the UI thread on purpose
        Tools.Save();
        SaveAll();
        Chats.Shutdown();
        Updates.ApplyAndRestart();
    }

    public void Quit(bool save = true)
    {
        Log.Write("quit");
        stalls?.Dispose(); // the saving and shutting down below may hold the UI thread on purpose
        if (save)
        {
            Tools.Save(); // where the tool windows are, before the settings are written
            SaveAll();
        }
        DocWindow.RescueAll(); // a document is the user's file, kept even by the reset: what can't be written goes to a copy
        DocWindow.Quitting();
        Chats.Shutdown(save);
        winKey?.Dispose();
        winKeyAdmin?.Dispose();
        Clipboard.Dispose();
        Shortcuts?.Dispose();
        tray?.Dispose();
        Everything.Dispose();
        Apps.Dispose();
        Window.AllowClose = true;
        Application.Current.Shutdown();
    }
}
