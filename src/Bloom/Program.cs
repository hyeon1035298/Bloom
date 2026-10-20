using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Bloom.App;
using Bloom.Core;
using Velopack;

[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace Bloom;

internal static class Program
{
    /// Args: --data <dir> (dev copy with its own data), --show, --settings, --quit, --no-hook,
    ///       --no-tray, --display=<n> (dev: open on that monitor), --capture <dir> (render the
    ///       views to PNG files and exit), --autostart (started from the Run key).
    ///       --export <file> (the settings into a file, the same as 설정 > 정보 > 설정 내보내기; Bloom must not be running).
    ///       --restart-from <pid> / --reset-from <pid> (started by a Bloom that is quitting: wait for it, then start).
    ///       --uninstall-from <pid> [--wipe] (설정 > 정보 > Bloom 삭제: wait for it, delete the data when asked, run the
    ///       setup's uninstaller; nothing starts).
    ///       --mcp <pipe> [<conversation>] (started by Claude Code or Codex: the "bloom" MCP server, carried to the
    ///       running Bloom; Codex's runs name their conversation, whose chat then asks the user for each action).
    ///       --mcp-proxy <pipe> <conversation> <source> <name> (started by Codex: another MCP server the user switched on,
    ///       started here and asked about in that conversation's chat before each tool call).
    ///       --console-window <pid> (a notice card's click: the console window of that process as the exit code; nothing
    ///       starts).
    ///       --claude-approve [--data <dir>] (Claude Code's PermissionRequest hook: the request to the running Bloom's
    ///       card, its answer as the hook's JSON; nothing starts).
    ///       --open <file> (Explorer's 연결 프로그램: a .md or other text file in its document window, an audio file in the
    ///       mini player — handed to the running Bloom, else Bloom starts in the tray with it open, without the launcher).
    [STAThread]
    private static int Main(string[] args)
    {
        // Claude Code or Codex starting Bloom's MCP server: a relay to the running Bloom, before anything else
        if (args.Length is 2 or 3 && args[0] == "--mcp") return App.Mcp.McpBridge.Run(args[1], args.Length == 3 ? args[2] : null);
        if (args.Length == 5 && args[0] == "--mcp-proxy") return App.Mcp.McpProxy.Run(args[1], args[2], args[3], args[4]);
        // a notice card's click asking which console window a CLI runs in (Native\SessionWindow.cs): this run attaches to
        // that console instead of the running Bloom, answers in its exit code and ends
        if (args.Length == 2 && args[0] == Native.SessionWindow.ProbeArg) return Native.SessionWindow.Probe(args[1]);
        // Claude Code's PermissionRequest hook (설정 > 알림 > 알림 카드에서 승인): carries the request to the running Bloom's card
        // and prints its answer; no answer (Bloom not running, 터미널에서 답하기) leaves the terminal's own question
        if (args.Length >= 1 && args[0] == App.ClaudeApprovalHook.Arg) return App.ClaudeApprovalHook.Run(args);
        // the setup's install / update / uninstall steps start Bloom with their own arguments and end in Run. A downloaded
        // update never goes in at a start of Velopack's choosing (a second Bloom started only to signal the first would
        // close it): Updates.ApplyAtStart and Updates.ApplyOnQuit do that.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ =>
            {
                // the Run entry, Claude Code's approval hook and Explorer's 연결 프로그램 would point at a removed exe
                Autostart.Set(false);
                Ai.ClaudeHooks.Remove();
                FileAssoc.Set(false);
            })
            .Run();
        var opt = new Options();
        bool quit = false;
        string? export = null;
        int resetFrom = -1, restartFrom = -1, uninstallFrom = -1;
        bool wipe = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--data" when i + 1 < args.Length:
                    opt.Data = args[++i];
                    break;
                case "--capture" when i + 1 < args.Length:
                    opt.CaptureDir = args[++i];
                    break;
                case "--qa-chat" when i + 1 < args.Length:
                    opt.ChatFile = args[++i];
                    break;
                case "--qa-warm" when i + 1 < args.Length:
                    opt.WarmFile = args[++i];
                    break;
                case "--qa-glass" when i + 1 < args.Length:
                    opt.GlassFile = args[++i];
                    break;
                case "--qa-model" when i + 1 < args.Length:
                    opt.QaModel = args[++i];
                    break;
                case "--qa-effort" when i + 1 < args.Length:
                    opt.QaEffort = args[++i];
                    break;
                case "--qa-set" when i + 1 < args.Length:
                    opt.QaSet = args[++i];
                    break;
                case "--qa-paste" when i + 1 < args.Length:
                    opt.PasteFile = args[++i];
                    break;
                case "--qa-ocr" when i + 1 < args.Length:
                    opt.OcrFile = args[++i];
                    break;
                case "--qa-translate":
                    opt.QaTranslate = true;
                    break;
                case "--lang" when i + 1 < args.Length:
                    opt.Lang = args[++i];
                    break;
                case "--qa-measure" when i + 1 < args.Length:
                    opt.MeasureSeconds = int.TryParse(args[++i], out int ms) ? ms : 60;
                    break;
                case "--qa-summon" when i + 1 < args.Length:
                    opt.SummonRounds = int.TryParse(args[++i], out int rounds) ? Math.Clamp(rounds, 1, 50) : 10;
                    break;
                case "--qa-responsive" when i + 1 < args.Length:
                    opt.ResponsiveRounds = int.TryParse(args[++i], out int responsive) ? Math.Clamp(responsive, 1, 10) : 3;
                    break;
                case "--qa-md" when i + 1 < args.Length:
                    opt.MdBenchFile = args[++i];
                    break;
                case "--qa-md-in" when i + 1 < args.Length:
                    opt.MdBenchIn = args[++i];
                    break;
                case "--qa-md-rounds" when i + 1 < args.Length:
                    opt.MdBenchRounds = int.TryParse(args[++i], out int mdRounds) ? Math.Clamp(mdRounds, 1, 20) : 5;
                    break;
                case "--qa-space" when i + 1 < args.Length:
                    opt.SpaceDir = args[++i];
                    break;
                case "--qa-space-path" when i + 1 < args.Length:
                    opt.SpacePath = args[++i];
                    break;
                case "--qa-ask" when i + 1 < args.Length:
                    opt.QaAsk = args[++i];
                    break;
                case "--show":
                    opt.Show = true;
                    break;
                case DocHandoff.Arg when i + 1 < args.Length:
                    opt.Open = args[++i];
                    break;
                case "--settings":
                    opt.Settings = true;
                    break;
                case "--reset-from" when i + 1 < args.Length:
                    resetFrom = int.TryParse(args[++i], out int pid) ? pid : 0;
                    break;
                case "--restart-from" when i + 1 < args.Length:
                    restartFrom = int.TryParse(args[++i], out int rpid) ? rpid : 0;
                    break;
                case "--uninstall-from" when i + 1 < args.Length:
                    uninstallFrom = int.TryParse(args[++i], out int upid) ? upid : 0;
                    break;
                case "--wipe":
                    wipe = true;
                    break;
                case "--export" when i + 1 < args.Length:
                    export = args[++i];
                    break;
                case "--quit":
                    quit = true;
                    break;
                case "--no-hook":
                    opt.NoHook = true;
                    break;
                case "--no-tray":
                    opt.NoTray = true;
                    break;
                case "--demo":
                    opt.Demo = true;
                    break;
                case "--qa-scale" when i + 1 < args.Length:
                    opt.QaScale = double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double qs) && qs is >= 1 and <= 4 ? qs : null;
                    break;
                default:
                    if (a.StartsWith("--display=", StringComparison.Ordinal) && int.TryParse(a["--display=".Length..], out int d)) opt.Display = d;
                    break;
            }
        }
        if (opt.ResponsiveRounds is not null && string.IsNullOrEmpty(opt.Data)) return 2;
        if ((opt.CaptureDir is not null || opt.GlassFile is not null || opt.ChatFile is not null || opt.WarmFile is not null || opt.MeasureSeconds is not null || opt.SummonRounds is not null || opt.ResponsiveRounds is not null || opt.Demo) && opt.Data is null) return 2; // QA and recording never touch the real data
        Paths.Init(opt.Data);
        if (uninstallFrom >= 0) return Uninstall(uninstallFrom, wipe);
        // the setup's Start-menu shortcut starts Bloom without arguments: that opens the launcher (the Run entry has --autostart)
        if (args.Length == 0 && Updates.Installed) opt.Show = true;
        if (resetFrom >= 0) Reset(resetFrom);
        else if (restartFrom >= 0) WaitFor(restartFrom);

        opt.Open = FullPath(opt.Open); // from the folder it was started in (this process moves to the home folder below)

        string id = Paths.InstanceId;
        using var mutex = new Mutex(true, @"Local\" + id, out bool first);
        if (!first && opt.Open is { } doc && !quit)
        {
            // the running Bloom opens it. One still starting has no pipe yet: waited for (30 s). One that has just quit lets
            // go of the mutex: this start takes it and opens the file itself (Explorer starts one Bloom per file selected).
            // A file that doesn't open says so in a message box.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!first)
            {
                switch (DocHandoff.Send(DocHandoff.Pipe, doc))
                {
                    case DocHandoff.Answer.Opened:
                        return 0;
                    case DocHandoff.Answer.Refused:
                        DocHandoff.Tell(doc, DocHandoff.Refused);
                        return 5;
                    case DocHandoff.Answer.Silent:
                        DocHandoff.Tell(doc, Tr("실행 중인 Bloom이 응답하지 않았습니다", "The running Bloom didn't answer"));
                        return 7;
                }
                if (clock.ElapsedMilliseconds > 30000)
                {
                    DocHandoff.Tell(doc, Tr("Bloom이 30초 안에 준비되지 않았습니다", "Bloom wasn't ready within 30 seconds"));
                    return 6;
                }
                Thread.Sleep(150);
                first = Take(mutex);
            }
        }
        if (!first && export is not null) return 3; // quit Bloom first: it may hold unsaved settings
        if (!first)
        {
            string signal = quit ? ".Quit" : opt.Settings ? ".Settings" : ".Show";
            if (EventWaitHandle.TryOpenExisting(@"Local\" + id + signal, out var h)) h.Set();
            return 0;
        }
        if (quit) return 0; // nothing running: never start just to quit
        if (export is not null)
        {
            // straight from the data on disk, so only while no Bloom is holding newer settings in memory
            try
            {
                SettingsTransfer.Export(Path.GetFullPath(export), typeof(Host).Assembly.GetName().Version?.ToString(3) ?? "");
                return 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write("export: " + e.Message);
                return 4;
            }
        }

        if (Updates.ApplyAtStart(args)) return 0; // an update downloaded earlier goes in; Bloom starts again in it

        // apps started from Bloom take over its working folder: never the install folder, which an update has to move
        // aside (an app left running from there kept every update from going in). Relative dev/QA paths stay valid.
        static string? Full(string? path) => path is null ? null : Path.GetFullPath(path);
        (opt.CaptureDir, opt.GlassFile, opt.ChatFile, opt.PasteFile) = (Full(opt.CaptureDir), Full(opt.GlassFile), Full(opt.ChatFile), Full(opt.PasteFile));
        opt.WarmFile = Full(opt.WarmFile);
        (opt.OcrFile, opt.SpaceDir, opt.SpacePath) = (Full(opt.OcrFile), Full(opt.SpaceDir), Full(opt.SpacePath));
        Directory.SetCurrentDirectory(Paths.Home);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var host = new Host(opt);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("[ui] " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal " + e.ExceptionObject);
        app.SessionEnding += (_, _) =>
        {
            host.SaveAll();
            Ui.DocWindow.RescueAll(); // no one to ask: what a document couldn't write goes to a copy beside it
        };
        app.Startup += (_, _) =>
        {
            // first: a start handing a file over waits for this pipe; what it hands over opens once the start is done
            DocHandoff.Listen(DocHandoff.Pipe, path => app.Dispatcher.InvokeAsync(() => host.OpenHanded(path)).Task);
            host.Start();
            Listen(id + ".Show", () => host.Summon());
            Listen(id + ".Settings", host.OpenSettings);
            Listen(id + ".Quit", () => host.QuitForGood(ask: false)); // a script waits on it (install.ps1): nothing asks
        };
        app.Run();
        return 0;
    }

    /// The reset (설정 > 정보 > Bloom 초기화): once the Bloom that asked for it has ended, both data folders go and
    /// are made again empty, so this start is a first run.
    private static void Reset(int oldPid)
    {
        WaitFor(oldPid);
        DeleteData();
        Paths.Init(Paths.IsDev ? Paths.Data : null);
        Log.Write("reset: data deleted, starting fresh");
    }

    /// The uninstall (설정 > 정보 > Bloom 삭제): once the Bloom that asked for it has ended, the data folders go when
    /// asked (nothing is logged after that, which would make them again), then the setup's uninstaller removes the app;
    /// it ends this process too, which runs from the app's folder.
    private static int Uninstall(int oldPid, bool wipe)
    {
        WaitFor(oldPid);
        if (wipe) DeleteData();
        if (Updates.Uninstaller() is not { } updater) return 0;
        var psi = new System.Diagnostics.ProcessStartInfo(updater) { UseShellExecute = false };
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--uninstall");
        Bloom.Ai.ChildProcess.Spawn(psi);
        return 0;
    }

    /// Both data folders (settings, chats, favorites … and the local caches and logs), retried while a file still closes.
    private static void DeleteData()
    {
        foreach (string dir in new[] { Paths.Data, Paths.Local }.Distinct().Where(Paths.Deletable))
            for (int attempt = 0; attempt < 10 && Directory.Exists(dir); attempt++)
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(300); // a file still closing
                }
    }

    /// The Bloom that started this one (restart, reset) has to end first: it holds the mutex and the data.
    private static void WaitFor(int oldPid)
    {
        try
        {
            using var old = System.Diagnostics.Process.GetProcessById(oldPid);
            old.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // already gone
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // the id now belongs to a process this one can't open: ours is gone
        }
    }

    /// The single-instance mutex, when the Bloom that held it has ended (abandoned or let go): this start's now.
    private static bool Take(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true; // its Bloom ended without letting go: the mutex is this thread's all the same
        }
    }

    /// A path made full; null for none, or for one that isn't a path.
    private static string? FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// A named event another start of the app sets (second instance → show / settings / quit).
    private static void Listen(string name, Action act)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name);
        var t = new Thread(() =>
        {
            while (ev.WaitOne()) Application.Current?.Dispatcher.BeginInvoke(act);
        })
        { IsBackground = true, Name = "signal " + name };
        t.Start();
    }
}
