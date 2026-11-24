using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-md <out.txt> [--qa-md-in "label=path|label=path"] [--qa-md-rounds n]` (with --data, a throwaway folder): the
/// Markdown view (Ui\MdView.cs) in a document window off-screen, timed. Each input is copied into the data folder first
/// (ticks and edits go to the copy) and opened `rounds` times: open → first paint, until idle, a top→bottom scroll in
/// 120 px steps (the UI work of each step and the CompositionTarget.Rendering intervals), a resize 600→1400 px, a text
/// size step, a checkbox tick and another program's edit, with a 16 ms Input-priority heartbeat whose gaps ≥ 50 ms are
/// stalls. Besides the given inputs it makes a ~250 KB and a ~2 MB document of its own. Only numbers are written:
/// `<out>.txt` (medians) and `<out>.json` (every sample).
internal static class MdBench
{
    private const int Step = 120, MaxSteps = 400, StallMs = 50;

    private sealed class Round
    {
        public double OpenMs, SettleMs, OpenGapMs, ScrollP95, ScrollMax, FrameP95, FrameMax, ResizeP95, ResizeMax, SizeSettleMs, SizeGapMs, TickMs, TickGapMs, OutsideMs, OutsideGapMs, MaxGapMs, SliceMaxMs, SliceGcMs, SizeDrawnMs, OutsideDrawnMs, JumpMax, ScrollGapMs, ResizeGapMs, CloseGapMs, GcPauseMs, ScrollBusyMs, ResizeBusyMs, MaxBusyMs, NarrowSizeSettleMs, NarrowSizeBusyMs, NarrowSizeDrawnMs;
        public int ScrollSteps, Stalls50, Stalls100, Gen2, Busy50, Busy100;
        public bool SweptAll;
    }

    public static async Task Run(Host host, string outFile, string? inputs, int rounds)
    {
        var text = new StringBuilder();
        var all = new Dictionary<string, List<Round>>();
        try
        {
            if (!Paths.IsDev) throw new InvalidOperationException("The Markdown bench needs a throwaway --data folder.");
            Settings.Current.MarkTutorialSeen();
            Settings.Current.DocSize = [900, 1000];
            Settings.Current.MdTextSize = Settings.MdTextDefault;
            DocWindow.OffScreenForQa = true;
            Theme.Apply("dark");
            string dir = Path.Combine(Paths.Data, "mdbench");
            Directory.CreateDirectory(dir);
            var list = new List<(string label, string path)>();
            foreach (string part in (inputs ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                string label = eq > 0 ? part[..eq] : Path.GetFileNameWithoutExtension(part);
                string src = eq > 0 ? part[(eq + 1)..] : part;
                list.Add((label, src));
            }
            list.Add(("generated-250KB", Generate(Path.Combine(dir, "gen-250k.md"), 250_000)));
            list.Add(("generated-2MB", Generate(Path.Combine(dir, "gen-2m.md"), 1_950_000)));
            await Task.Delay(1500); // start-up settles
            text.AppendLine($"render mode={RenderOptions.ProcessRenderMode} tier={RenderCapability.Tier >> 16} rounds={rounds} window=900x1000 step={Step}px");
            foreach (var (label, src) in list)
            {
                string copy = Path.Combine(dir, label + ".md");
                if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase)) File.Copy(src, copy, true);
                Picture(Path.Combine(dir, "pic.png"));
                string original = File.ReadAllText(copy);
                var results = new List<Round>();
                for (int r = 0; r < rounds; r++)
                {
                    File.WriteAllText(copy, original, new UTF8Encoding(false));
                    results.Add(await One(copy));
                    Log.Write($"mdbench {label} round {r + 1} done");
                }
                all[label] = results;
                text.AppendLine();
                text.AppendLine($"[{label}] {original.Length:N0} chars, cold round 1: open {results[0].OpenMs:0} ms, settle {results[0].SettleMs:0} ms");
                Line(text, "open → first paint ms", results, x => x.OpenMs);
                Line(text, "open → idle (full layout) ms", results, x => x.SettleMs);
                Line(text, "open: longest UI gap ms", results, x => x.OpenGapMs);
                Line(text, "scroll step UI p95 ms", results, x => x.ScrollP95);
                Line(text, "scroll step UI max ms", results, x => x.ScrollMax);
                Line(text, "scroll Rendering interval p95 ms", results, x => x.FrameP95);
                Line(text, "scroll Rendering interval max ms", results, x => x.FrameMax);
                Line(text, "scroll jump (middle, end, 30 %) UI max ms", results, x => x.JumpMax);
                Line(text, "resize step UI p95 ms", results, x => x.ResizeP95);
                Line(text, "resize step UI max ms", results, x => x.ResizeMax);
                Line(text, "text size: settle − 120 ms debounce", results, x => x.SizeSettleMs);
                Line(text, "text size: longest UI gap ms", results, x => x.SizeGapMs);
                Line(text, "text size: redraw (Show → drawn) ms", results, x => x.SizeDrawnMs);
                Line(text, "narrow (480 px) text size: settle − 120 ms debounce", results, x => x.NarrowSizeSettleMs);
                Line(text, "narrow (480 px) text size: busy probe longest ms", results, x => x.NarrowSizeBusyMs);
                Line(text, "narrow (480 px) text size: redraw (Show → drawn) ms", results, x => x.NarrowSizeDrawnMs);
                Line(text, "checkbox tick settle ms", results, x => x.TickMs);
                Line(text, "checkbox tick longest UI gap ms", results, x => x.TickGapMs);
                Line(text, "outside edit settle ms", results, x => x.OutsideMs);
                Line(text, "outside edit longest UI gap ms", results, x => x.OutsideGapMs);
                Line(text, "outside edit redraw (Show → drawn) ms", results, x => x.OutsideDrawnMs);
                Line(text, "scroll + jumps: longest UI gap ms", results, x => x.ScrollGapMs);
                Line(text, "resize: longest UI gap ms", results, x => x.ResizeGapMs);
                Line(text, "closing the window: longest UI gap ms", results, x => x.CloseGapMs);
                Line(text, "GC pause after open, total ms", results, x => x.GcPauseMs);
                Line(text, "gen2 collections after open", results, x => x.Gen2);
                Line(text, "longest UI gap overall ms", results, x => x.MaxGapMs);
                Line(text, "longest idle layout slice ms", results, x => x.SliceMaxMs);
                Line(text, "  of it GC pause ms", results, x => x.SliceGcMs);
                Line(text, "stalls ≥ 50 ms (count)", results, x => x.Stalls50);
                Line(text, "stalls ≥ 100 ms (count)", results, x => x.Stalls100);
                Line(text, "busy probe (Send): scroll + jumps longest ms", results, x => x.ScrollBusyMs);
                Line(text, "busy probe (Send): resize longest ms", results, x => x.ResizeBusyMs);
                Line(text, "busy probe (Send): longest overall ms", results, x => x.MaxBusyMs);
                Line(text, "busy probe (Send): ≥ 50 ms (count)", results, x => x.Busy50);
                Line(text, "busy probe (Send): ≥ 100 ms (count)", results, x => x.Busy100);
                text.AppendLine($"  scroll steps per round: {string.Join(", ", results.Select(x => x.ScrollSteps + (x.SweptAll ? "" : " (capped)")))}");
            }
        }
        catch (Exception e)
        {
            Log.Error("md bench", e);
            text.AppendLine("ERROR " + e);
        }
        finally
        {
            File.WriteAllText(outFile, text.ToString());
            File.WriteAllText(Path.ChangeExtension(outFile, ".json"), JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            host.Quit(save: false);
        }
    }

    private static void Line(StringBuilder text, string what, List<Round> rs, Func<Round, double> of)
    {
        var v = rs.Select(of).ToList();
        text.AppendLine(FormattableString.Invariant($"  {what}: median {Median(v):0.0} (rounds {string.Join(" ", v.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)))})"));
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    private static double Pct(List<double> v, double p)
    {
        var s = v.OrderBy(x => x).ToArray();
        return s.Length == 0 ? 0 : s[Math.Clamp((int)Math.Ceiling(p * s.Length) - 1, 0, s.Length - 1)];
    }

    /// The stall probe (as Core\StallWatch.cs): a thread of its own asks the UI thread for an Input-priority moment every
    /// 10 ms and notes how long each took to come (UI work in between holds it back).
    /// With Send priority it asks for the very next moment instead: how long the UI thread stays in one piece of work,
    /// whatever else is queued (the Input probe also waits behind a stream of render work the sweep keeps queuing).
    private sealed class Probe
    {
        private static readonly Stopwatch watch = Stopwatch.StartNew(); // one clock for both probes' marks
        private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
        private readonly DispatcherPriority priority;
        private readonly System.Threading.Thread thread;
        private volatile bool stop;
        private readonly List<(double at, double ms)> answers = [];

        public Probe(DispatcherPriority priority = DispatcherPriority.Input)
        {
            this.priority = priority;
            thread = new System.Threading.Thread(Run) { IsBackground = true, Name = "md bench probe", Priority = System.Threading.ThreadPriority.AboveNormal };
            thread.Start();
        }

        private void Run()
        {
            using var answered = new System.Threading.AutoResetEvent(false);
            while (!stop)
            {
                double sent = watch.Elapsed.TotalMilliseconds;
                ui.BeginInvoke(priority, () =>
                {
                    double now = watch.Elapsed.TotalMilliseconds;
                    lock (answers) answers.Add((now, now - sent));
                    answered.Set();
                });
                while (!stop && !answered.WaitOne(100)) { }
                System.Threading.Thread.Sleep(10);
            }
        }

        /// The UI thread's longest wait answered from `mark` (Mark's time) on.
        public double Mark() => watch.Elapsed.TotalMilliseconds;

        public double Longest(double mark)
        {
            lock (answers) return answers.Where(a => a.at >= mark).Select(a => a.ms).DefaultIfEmpty(0).Max();
        }

        public List<double> Gaps
        {
            get
            {
                lock (answers) return answers.Select(a => a.ms).ToList();
            }
        }

        public void Stop()
        {
            stop = true;
            thread.Join(500);
        }
    }

    private static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).Task;

    private static Task Loaded() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded).Task;

    /// The next CompositionTarget.Rendering, and when it came.
    private static Task<double> Frame(Stopwatch watch)
    {
        var done = new TaskCompletionSource<double>();
        void On(object? s, EventArgs e)
        {
            CompositionTarget.Rendering -= On;
            done.TrySetResult(watch.Elapsed.TotalMilliseconds);
        }
        CompositionTarget.Rendering += On;
        return done.Task;
    }

    private static async Task<Round> One(string path)
    {
        var r = new Round();
        var probe = new Probe();
        var busy = new Probe(DispatcherPriority.Send);
        var watch = Stopwatch.StartNew();
        await Idle();
        double mark = probe.Mark();
        double t0 = watch.Elapsed.TotalMilliseconds;
        var d = DocWindow.Show(path);
        await Loaded();
        r.OpenMs = watch.Elapsed.TotalMilliseconds - t0;
        await Idle();
        r.SettleMs = watch.Elapsed.TotalMilliseconds - t0;
        r.OpenGapMs = probe.Longest(mark);
        var pane = d.PaneForQa;
        if (pane.KeyTarget is not MdView view) throw new InvalidOperationException("the document didn't open in 보기");
        await Task.Delay(300);

        // scroll: top → bottom, a step a frame
        double gc0 = GC.GetTotalPauseDuration().TotalMilliseconds;
        int gen2 = GC.CollectionCount(2);
        mark = probe.Mark();
        var steps = new List<double>();
        var frames = new List<double>();
        double prev = await Frame(watch);
        view.ScrollTo(0);
        while (true)
        {
            double at = view.VerticalOffset;
            if (at >= view.ScrollableHeight - 0.5) break;
            if (steps.Count >= MaxSteps) break;
            var sw = Stopwatch.StartNew();
            view.ScrollTo(at + Step);
            view.UpdateLayout();
            steps.Add(sw.Elapsed.TotalMilliseconds);
            double f = await Frame(watch);
            frames.Add(f - prev);
            prev = f;
        }
        r.ScrollSteps = steps.Count;
        r.SweptAll = view.VerticalOffset >= view.ScrollableHeight - 0.5;
        r.ScrollP95 = Pct(steps, .95);
        r.ScrollMax = steps.DefaultIfEmpty(0).Max();
        r.FrameP95 = Pct(frames, .95);
        r.FrameMax = frames.DefaultIfEmpty(0).Max();
        // jumps (the scroll bar's thumb dragged far): the middle, the end, back to 30 %
        foreach (double share in new[] { 0.5, 1.0, 0.3 })
        {
            await Frame(watch);
            var sw = Stopwatch.StartNew();
            view.ScrollTo(view.ScrollableHeight * share);
            view.UpdateLayout();
            r.JumpMax = Math.Max(r.JumpMax, sw.Elapsed.TotalMilliseconds);
        }
        await Idle();
        r.ScrollGapMs = probe.Longest(mark);
        r.ScrollBusyMs = busy.Longest(mark);

        // resize: 600 → 1400 px wide, 20 px a frame
        mark = probe.Mark();
        var sizes = new List<double>();
        for (double w = 600; w <= 1400; w += 20)
        {
            await Frame(watch);
            var sw = Stopwatch.StartNew();
            d.Width = w;
            d.UpdateLayout();
            sizes.Add(sw.Elapsed.TotalMilliseconds);
        }
        r.ResizeP95 = Pct(sizes, .95);
        r.ResizeMax = sizes.DefaultIfEmpty(0).Max();
        d.Width = 900;
        await Idle();
        r.ResizeGapMs = probe.Longest(mark);
        r.ResizeBusyMs = busy.Longest(mark);

        // text size: one step up (MdPane redraws 120 ms after the last step)
        mark = probe.Mark();
        t0 = watch.Elapsed.TotalMilliseconds;
        pane.SetTextSize(MdPane.TextSize + 1);
        await Task.Delay(130);
        await Idle();
        r.SizeSettleMs = watch.Elapsed.TotalMilliseconds - t0 - 120;
        r.SizeGapMs = probe.Longest(mark);
        r.SizeDrawnMs = view.Surface.ShowToDrawnMs;
        pane.SetTextSize(Settings.MdTextDefault);
        await Task.Delay(200);
        await Idle();

        // text size in a narrow window (the column the same at both sizes): one step up
        d.Width = 480;
        await Idle();
        mark = busy.Mark();
        t0 = watch.Elapsed.TotalMilliseconds;
        pane.SetTextSize(MdPane.TextSize + 1);
        await Task.Delay(130);
        await Idle();
        r.NarrowSizeSettleMs = watch.Elapsed.TotalMilliseconds - t0 - 120;
        r.NarrowSizeBusyMs = busy.Longest(mark);
        r.NarrowSizeDrawnMs = view.Surface.ShowToDrawnMs;
        pane.SetTextSize(Settings.MdTextDefault);
        d.Width = 900;
        await Task.Delay(200);
        await Idle();

        // a checkbox ticked
        view.ScrollTo(0);
        await Idle();
        mark = probe.Mark();
        t0 = watch.Elapsed.TotalMilliseconds;
        bool ticked = pane.TickForQa();
        await Idle();
        r.TickMs = ticked ? watch.Elapsed.TotalMilliseconds - t0 : -1;
        r.TickGapMs = probe.Longest(mark);
        await Task.Delay(1200); // the save goes out (0.8 s) and comes back
        await Idle();

        // another program's edit
        File.AppendAllText(path, "\n\n추가된 문단: 다른 프로그램이 고친 줄입니다.\n");
        mark = probe.Mark();
        t0 = watch.Elapsed.TotalMilliseconds;
        d.OutsideForQa();
        await Idle();
        r.OutsideMs = watch.Elapsed.TotalMilliseconds - t0;
        r.OutsideGapMs = probe.Longest(mark);
        r.OutsideDrawnMs = view.Surface.ShowToDrawnMs;
        await Task.Delay(300);
        r.SliceMaxMs = view.Surface.LongestSliceMs;
        r.SliceGcMs = view.Surface.LongestSliceGcMs;

        mark = probe.Mark();
        d.Close();
        await Idle();
        await Task.Delay(100);
        r.CloseGapMs = probe.Longest(mark);
        r.GcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds - gc0;
        r.Gen2 = GC.CollectionCount(2) - gen2;
        probe.Stop();
        busy.Stop();
        var gaps = probe.Gaps;
        r.MaxGapMs = gaps.DefaultIfEmpty(0).Max();
        r.Stalls50 = gaps.Count(g => g >= StallMs);
        r.Stalls100 = gaps.Count(g => g >= 100);
        var held = busy.Gaps;
        r.MaxBusyMs = held.DefaultIfEmpty(0).Max();
        r.Busy50 = held.Count(g => g >= StallMs);
        r.Busy100 = held.Count(g => g >= 100);
        return r;
    }

    /// A small picture beside the documents (the fixture shows pic.png).
    private static void Picture(string file)
    {
        if (File.Exists(file)) return;
        const int w = 640, h = 360;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                pixels[i] = (byte)(x * 255 / w);
                pixels[i + 1] = (byte)(y * 255 / h);
                pixels[i + 2] = 160;
                pixels[i + 3] = 255;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4)));
        using var s = File.Create(file);
        enc.Save(s);
    }

    /// A long document of about `chars` characters: sections of headings, paragraphs (Korean and English, inline code,
    /// links), lists, checklists, code and a table now and then.
    private static string Generate(string file, int chars)
    {
        var rng = new Random(20261019);
        string[] ko = "화면 설정 캡처 결과 저장 표정 조명 카메라 배경 아바타 의상 비교 항목 동작 방식 지원 성능 메모리 속도 품질 단계 사용자 버튼 메뉴 크기 위치 순서 목록 선택 확인 적용 변경 유지 자동 렌더링 셰이더 텍스처".Split(' ');
        string[] en = "the view lays out each block once and draws only what is on screen while the rest waits for idle time".Split(' ');
        string Words(int n, bool korean) => string.Join(' ', Enumerable.Range(0, n).Select(_ => korean ? ko[rng.Next(ko.Length)] + (rng.Next(4) == 0 ? "을" : "") : en[rng.Next(en.Length)]));
        var sb = new StringBuilder();
        int section = 0;
        while (sb.Length < chars)
        {
            section++;
            sb.Append("## ").Append(section).Append(". ").Append(Words(3, true)).Append("\n\n");
            for (int p = 0; p < 3; p++)
                sb.Append(Words(18 + rng.Next(30), p % 2 == 0)).Append(" `Assets/Contoso/Studio").Append(rng.Next(100)).Append(".cs` ").Append(Words(10, true)).Append(" [링크](https://example.com/").Append(section).Append(").\n\n");
            for (int k = 0; k < 4; k++) sb.Append(k % 2 == 0 ? "- " : "- [ ] ").Append(Words(6 + rng.Next(10), true)).Append('\n');
            sb.Append('\n');
            if (section % 3 == 0)
            {
                sb.Append("```csharp\n");
                for (int k = 0; k < 6; k++) sb.Append("var value").Append(k).Append(" = Compute(").Append(k).Append(", \"").Append(Words(3, false)).Append("\");\n");
                sb.Append("```\n\n");
            }
            if (section % 4 == 0)
            {
                sb.Append("| 항목 | 설명 | 값 |\n|---|---|---|\n");
                for (int k = 0; k < 8; k++) sb.Append("| ").Append(Words(2, true)).Append(" | ").Append(Words(6 + rng.Next(8), true)).Append(" | `").Append(k).Append("` |\n");
                sb.Append('\n');
            }
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        return file;
    }
}
