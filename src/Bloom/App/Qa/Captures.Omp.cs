using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set omp` (needs omp signed in; five cheap turns on Haiku, one of them asking Bloom's PC tool): a chat window on
/// omp — two turns that show the model remembers, the context gauge, `/session`, a refused slash command, 정리, and the PC
/// tool's approval card (declined) — then the model menu and 설정 > AI 채팅 for the omp side. Writes omp-run.txt and PNGs.
/// `--qa-set omp-missing`: the same screens with omp pretended to be absent (OmpCli.PretendMissing), nothing sent.
internal static partial class Captures
{
    private static async Task OmpScreens(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        OmpCatalog.Ensure();
        for (int i = 0; i < 300 && OmpCatalog.All.Count == 0; i++) await Task.Delay(100);
        log.AppendLine($"omp {OmpCli.FindCli()} · `omp models` read: {OmpCatalog.All.Count} models");
        Settings.Current.OmpSearch = false; // nothing here needs the web
        string model = AiModels.Omp(OmpCatalog.Find("anthropic/claude-haiku-5-5") is not null ? "anthropic/claude-haiku-5-5" : "");
        var t = host.Chats.New();
        t.Model = model;
        t.Effort = AiModels.Fit(model, "low");
        var cw = new ChatWindow(host, t) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, Width = 1060, Height = 720 };
        async Task Cap(string name)
        {
            await Task.Delay(500);
            await cw.Dispatcher.InvokeAsync(() => cw.Capture(Path.Combine(dir, name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        async Task Turn(string text, int seconds = 90, bool watchAsk = false)
        {
            var sent = DateTime.UtcNow;
            cw.Draft(text, null);
            cw.SendChat();
            await Task.Delay(300);
            bool captured = false;
            while (t.Busy && DateTime.UtcNow - sent < TimeSpan.FromSeconds(seconds))
            {
                if (watchAsk && t.Messages[^1].Asks.FirstOrDefault() is { } ask)
                {
                    if (!captured)
                    {
                        captured = true;
                        log.AppendLine($"ASK ({ask.Tool}): {ask.Says} → decline");
                        await Cap("omp-5-approval-card");
                    }
                    host.Chats.Answer(t, ask, false);
                }
                await Task.Delay(100);
            }
            var a = t.Messages[^1];
            log.AppendLine($"Q: {text}");
            log.AppendLine($"A: {a.Text.Trim().Replace("\n", "⏎")} (error={a.Error}, {(DateTime.UtcNow - sent).TotalSeconds:0.0}s)");
            foreach (string d in a.Done ?? new()) log.AppendLine("  " + d);
        }
        try
        {
            cw.Show();
            await Cap("omp-0-new-chat");
            await Turn("내 이름은 모모야. 한 줄로 인사만 해 줘.");
            await Turn("내 이름이 뭐였지? 이름만 답해.");
            await Task.Delay(2500); // omp answers its get_state after the turn
            log.AppendLine($"gauge: {host.Chats.UsageOf(t)?.Gauge ?? "(none)"} · session {t.SessionId} ({t.SessionProvider})");
            await Cap("omp-1-two-turns-gauge");
            await Turn("/session", 30);
            await Cap("omp-2-session");
            await Turn("/fast", 5);
            await Cap("omp-3-refused-slash");
            bool tidy = host.Chats.Compact(t);
            for (int i = 0; i < 300 && host.Chats.IsCompacting(t); i++) await Task.Delay(100);
            await Task.Delay(2500);
            log.AppendLine($"정리 started={tidy} · {string.Join(" | ", t.Messages.Last(m => m.Role == "assistant").Done ?? new())} · gauge {host.Chats.UsageOf(t)?.Gauge ?? "(none)"}");
            await Cap("omp-4-tidy");
            await Turn("Bloom의 PC 도구로 메모장을 열어 줘.", 120, watchAsk: true);
            await Cap("omp-6-after-decline");
        }
        finally
        {
            cw.Close();
        }
        await OmpSideScreens(host, w, dir, "omp");
        await File.WriteAllTextAsync(Path.Combine(dir, "omp-run.txt"), log.ToString());
    }

    /// `--qa-set omp-missing`: omp is not on this PC (pretended): the model menu has no omp, 설정 says so.
    private static async Task OmpMissingScreens(Host host, Launcher w, string dir)
    {
        OmpCli.PretendMissing = true;
        try
        {
            var log = new StringBuilder();
            log.AppendLine($"FindCli: {OmpCli.FindCli() ?? "(null)"}");
            log.AppendLine($"model ids with omp: {string.Join(", ", AiModels.All().Where(AiModels.IsOmp))} (none expected)");
            log.AppendLine($"sides turned through: {string.Join(", ", AiModels.Sides())}");
            log.AppendLine($"translation sides: {string.Join(", ", Translator.Providers())} · now {Translator.Provider()}");
            Settings.Current.TranslateProvider = "omp"; // a saved choice of omp falls back to Claude
            log.AppendLine($"translation with TranslateProvider=omp: {Translator.Provider()}");
            Settings.Current.TranslateProvider = "claude";
            await File.WriteAllTextAsync(Path.Combine(dir, "omp-missing.txt"), log.ToString());
            var t = host.Chats.New();
            t.Model = "sonnet";
            w.ShowChat(t);
            w.PickModel();
            await Snap(w, dir, "omp-m1-model-menu-no-omp");
            await OmpSideScreens(host, w, dir, "omp-m");
        }
        finally
        {
            OmpCli.PretendMissing = false;
        }
    }

    /// 설정 > AI 채팅 narrowed to the omp rows, the model menu, and 설정 > PC 작업 · MCP.
    private static async Task OmpSideScreens(Host host, Launcher w, string dir, string prefix)
    {
        var t = host.Chats.New();
        w.ShowChat(t);
        w.PickModel();
        await Snap(w, dir, prefix + "-a-model-menu");
        w.Go(Mode.Root, "");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("ai");
        await Task.Delay(500);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, prefix + "-b-settings-ai.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Filter("omp");
        await Task.Delay(500);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, prefix + "-c-settings-omp-rows.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Filter("");
        sw.Page("mcp");
        await Task.Delay(500);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, prefix + "-d-settings-mcp.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
    }
}
