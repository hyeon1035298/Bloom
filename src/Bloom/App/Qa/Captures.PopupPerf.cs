using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// popup-perf: what the 더보기 panel costs to draw — the process's CPU time while it opens (its 150 ms growth) and
/// while the selection steps down its rows, one step a frame or so, as a pointer moving over it would. Run it with
/// GPU drawing off (the default) to see the software drawing. Written to popup-perf.txt.
internal static partial class Captures
{
    private const int PerfSteps = 120, PerfOpens = 12;

    private static async Task PopupPerf(Host host, Launcher w, string dir)
    {
        w.Width = 760;
        w.Height = 476;
        Theme.Apply("dark");
        // on a screen (--display=<n>, 2 by default): a window out at -32000 is never drawn, so it would cost nothing
        var mons = Launcher.Monitors();
        var info = new Native.Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.Win32.MONITORINFO>() };
        Native.Win32.GetMonitorInfo(mons[Math.Clamp((host.Opt.Display ?? 2) - 1, 0, mons.Count - 1)], ref info);
        w.Left = info.rcWork.Left + 80;
        w.Top = info.rcWork.Top + 80;
        await Task.Delay(300);
        var items = new List<ActionItem>();
        for (int i = 0; i < 10; i++) items.Add(new ActionItem { Title = $"Action {i + 1}", Glyph = G.App, Keys = "Ctrl+" + i, Run = () => { } });
        var proc = Process.GetCurrentProcess();

        // warm-up: every path once
        w.Choose("popup-perf", items);
        await Task.Delay(400);
        for (int i = 0; i < 10; i++)
        {
            w.ActionKeyForQa(Key.Down);
            await Task.Delay(16);
        }
        w.CloseActionsForQa();
        await Task.Delay(300);

        var log = new StringBuilder();
        log.AppendLine($"popup-perf · {DateTime.Now:yyyy-MM-dd HH:mm} · render mode {System.Windows.Media.RenderOptions.ProcessRenderMode} · {Environment.ProcessorCount} logical CPUs");
        for (int round = 0; round < 3; round++)
        {
            double idle = await Cpu(proc, async () => await Task.Delay(PerfSteps * 16));
            double open = await Cpu(proc, async () =>
            {
                for (int i = 0; i < PerfOpens; i++)
                {
                    w.Choose("popup-perf", items);
                    await Task.Delay(220);
                    w.CloseActionsForQa();
                    await Task.Delay(30);
                }
            });
            w.Choose("popup-perf", items);
            await Task.Delay(400);
            double step = await Cpu(proc, async () =>
            {
                for (int i = 0; i < PerfSteps; i++)
                {
                    w.ActionKeyForQa(Key.Down);
                    await Task.Delay(16);
                }
            });
            w.CloseActionsForQa();
            await Task.Delay(300);
            log.AppendLine($"round {round + 1}: idle {idle / PerfSteps:0.00} ms/16ms · open+grow {open / PerfOpens:0.0} ms CPU per opening · step {step / PerfSteps:0.00} ms CPU per selection step");
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "popup-perf.txt"), log.ToString());
        // the panel's look, its shadow too: popup-dark.png, popup-light.png
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Task.Delay(300);
            w.Choose("popup-perf", items);
            await Task.Delay(400);
            Shots.Save(w, Path.Combine(dir, $"popup-{theme}.png"));
            w.CloseActionsForQa();
        }
    }

    /// The process's CPU time (all threads, the render thread too) spent while `work` runs, in ms.
    private static async Task<double> Cpu(Process proc, Func<Task> work)
    {
        proc.Refresh();
        var before = proc.TotalProcessorTime;
        await work();
        proc.Refresh();
        return (proc.TotalProcessorTime - before).TotalMilliseconds;
    }
}
