using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Ocr;
using Bloom.Search;

namespace Bloom.App;

/// Isolated synthetic sources, one half-duty CPU worker, and actual launcher input/render code.
internal static class ResponsivenessBench
{
    private const int AppCount = 8000, RenderRows = 320;
    private sealed record RenderTiming(double InitialMs, double CompleteMs, bool Complete);
    private sealed record InputTiming(double LatestDeliveryMs, bool LatestDelivered);
    private sealed record DrawingTiming(int Points, bool FitToCurve, bool DefaultFitToCurve, double CreateAndInsertMs, double LayoutAndRenderMs, double MergeMs, bool MergeComplete);

    public static async Task Run(Host host, Dispatcher ui, int rounds)
    {
        using var load = new CancellationTokenSource();
        Thread? worker = null;
        DispatcherTimer? heartbeat = null;
        try
        {
            if (!Paths.IsDev) throw new InvalidOperationException("The responsiveness probe requires an isolated --data folder.");
            var settings = Settings.Current;
            settings.FilesInRoot = false;
            settings.WindowsInRoot = false;
            settings.MarkTutorialSeen();
            host.Window.Capturing = true;
            host.Window.ShowActivated = false;
            host.Window.Topmost = false;
            host.Window.Left = -20000;
            host.Window.Top = -20000;
            // AppIndex is not started in this QA branch, so no scanner can replace this synthetic list.
            if (host.Apps.Apps is not List<AppEntry> apps) throw new InvalidOperationException("Expected an unstarted in-memory app index.");
            apps.Clear();
            for (int i = 0; i < AppCount; i++)
            {
                string title = "probe " + i.ToString("D5", CultureInfo.InvariantCulture);
                apps.Add(new AppEntry { Id = "qa-app:" + i, Name = title, Original = title, Match = new MatchTarget(title), Path = "", Source = "lnk" });
            }
            host.Window.Show(); // retained WPF layout, off-screen, never activates or captures user pixels
            host.Window.QaQuery("probe");
            await Task.Delay(400);
            // Warm JIT and the common ranking path before measurements.
            _ = host.Engine.Root("probe");

            // Completed-stroke costs only: no OS pen/mouse injection and no pixels from the user's desktop.
            _ = MeasureDrawing(100, true);
            _ = MeasureDrawing(100, false);
            var drawings = new List<DrawingTiming>();
            foreach (int points in new[] { 100, 1000, 5000 })
                foreach (bool curve in new[] { true, false })
                {
                    drawings.Add(MeasureDrawing(points, curve));
                    await Task.Delay(10);
                }

            var watch = Stopwatch.StartNew();
            var gaps = new List<double>();
            double previous = watch.Elapsed.TotalMilliseconds;
            heartbeat = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(16) };
            heartbeat.Tick += (_, _) =>
            {
                double now = watch.Elapsed.TotalMilliseconds;
                gaps.Add(now - previous);
                previous = now;
            };
            heartbeat.Start();
            worker = new Thread(() => Contend(load.Token)) { IsBackground = true, Name = "bloom-qa-cpu" };
            worker.Start();
            await Task.Delay(100);

            var searches = new List<double>();
            var renders = new List<RenderTiming>();
            var inputs = new List<InputTiming>();
            for (int round = 0; round < rounds; round++)
            {
                var searchWatch = Stopwatch.StartNew();
                var root = host.Engine.Root("probe");
                searches.Add(searchWatch.Elapsed.TotalMilliseconds);
                if (!root.SelectMany(s => s.Items).Any(item => item.Id.StartsWith("qa-app:", StringComparison.Ordinal)))
                    throw new InvalidOperationException("The synthetic app search returned no app result.");
                await Task.Delay(30);

                string prefix = "qa-render:" + round + ":";
                var section = new Section("Responsiveness probe");
                for (int i = 0; i < RenderRows; i++)
                    section.Items.Add(new ResultItem { Id = prefix + i, Title = "Synthetic result " + i, Subtitle = "A bounded render workload", Icon = IconSpec.Of(G.Search), Kind = ItemKind.Info });
                var renderWatch = Stopwatch.StartNew();
                host.Window.QaRender(new List<Section> { section });
                double initial = renderWatch.Elapsed.TotalMilliseconds;
                bool complete = await WaitFor(() => host.Window.QaRowCount == RenderRows && host.Window.QaLastRowId == prefix + (RenderRows - 1), TimeSpan.FromSeconds(8));
                renders.Add(new RenderTiming(initial, renderWatch.Elapsed.TotalMilliseconds, complete));
                await Task.Delay(30);

                // Input requests originate independently of the UI, so a blocked dispatcher accumulates real delay.
                var inputWatch = Stopwatch.StartNew();
                double latestRequested = 0;
                await Task.Run(async () =>
                {
                    for (int i = 0; i < 12; i++)
                    {
                        string query = i == 11 ? "probe 07999" : "probe " + i.ToString("D5", CultureInfo.InvariantCulture);
                        if (i == 11) latestRequested = inputWatch.Elapsed.TotalMilliseconds;
                        _ = ui.BeginInvoke(DispatcherPriority.Input, new Action(() => host.Window.QaQuery(query)));
                        await Task.Delay(8);
                    }
                });
                bool delivered = await WaitFor(() => host.Window.QaQueryText == "probe 07999" && host.Window.QaHasResult("qa-app:7999"), TimeSpan.FromSeconds(8));
                inputs.Add(new InputTiming(Math.Max(0, inputWatch.Elapsed.TotalMilliseconds - latestRequested), delivered));
                await Task.Delay(30);
            }
            heartbeat.Stop();
            load.Cancel();
            worker.Join(1000);
            // Keep allocation/GC samples separate from the established search/render/drawing phase.
            var gcProbe = await GcResponsivenessBench.Run(ui);
            var report = new
            {
                schema = 1,
                captured = DateTimeOffset.Now.ToString("o"),
                rounds,
                syntheticApps = AppCount,
                syntheticRenderRows = RenderRows,
                contentionWorkers = 1,
                contentionDutyPercent = 50,
                heartbeatIntervalMs = 16,
                processRenderMode = System.Windows.Media.RenderOptions.ProcessRenderMode.ToString(),
                renderingTier = RenderCapability.Tier >> 16,
                launcherAllowsTransparency = host.Window.AllowsTransparency,
                drawingProbeScope = "Completed synthetic stroke insertion, off-screen canvas layout/render and Merge; excludes wet OS input and display latency.",
                drawingSamples = drawings,
                rootSearchMedianMs = Percentile(searches, .5),
                rootSearchMaxMs = searches.Max(),
                renderInitialMedianMs = Percentile(renders.Select(r => r.InitialMs), .5),
                renderCompleteMedianMs = Percentile(renders.Select(r => r.CompleteMs), .5),
                renderCompleteMaxMs = renders.Max(r => r.CompleteMs),
                inputHeartbeatP95Ms = Percentile(gaps, .95),
                inputHeartbeatMaxMs = gaps.Count == 0 ? 0 : gaps.Max(),
                latestQueryMaxMs = inputs.Max(i => i.LatestDeliveryMs),
                allRendersComplete = renders.All(r => r.Complete),
                allLatestQueriesDelivered = inputs.All(i => i.LatestDelivered),
                renderSamples = renders,
                inputSamples = inputs,
                searchSamplesMs = searches,
                heartbeatSamplesMs = gaps,
                gcProbe,
            };
            string json = JsonSerializer.Serialize(report, Json.Options);
            await File.WriteAllTextAsync(Paths.In("responsiveness.json"), json);
            var text = new StringBuilder();
            text.AppendLine($"apps={AppCount} rows={RenderRows} rounds={rounds} CPU workers=1 duty=50% heartbeat=16ms");
            text.AppendLine(FormattableString.Invariant($"root search median={report.rootSearchMedianMs:0.00}ms max={report.rootSearchMaxMs:0.00}ms"));
            text.AppendLine(FormattableString.Invariant($"render initial median={report.renderInitialMedianMs:0.00}ms complete median={report.renderCompleteMedianMs:0.00}ms max={report.renderCompleteMaxMs:0.00}ms"));
            text.AppendLine(FormattableString.Invariant($"Input heartbeat p95={report.inputHeartbeatP95Ms:0.00}ms max={report.inputHeartbeatMaxMs:0.00}ms latest query max={report.latestQueryMaxMs:0.00}ms"));
            text.AppendLine($"all renders complete={report.allRendersComplete}; all latest queries delivered={report.allLatestQueriesDelivered}");
            text.AppendLine($"process render mode={report.processRenderMode}; tier={report.renderingTier}; drawing samples exclude wet OS input");
            foreach (var draw in drawings)
                text.AppendLine(FormattableString.Invariant($"stroke points={draw.Points} curve={draw.FitToCurve} defaultCurve={draw.DefaultFitToCurve}: insert={draw.CreateAndInsertMs:0.00}ms layout/render={draw.LayoutAndRenderMs:0.00}ms merge={draw.MergeMs:0.00}ms complete={draw.MergeComplete}"));
            text.AppendLine(FormattableString.Invariant($"GC phase: duration={gcProbe.DurationMs:0.00}ms allocated={gcProbe.WorkerAllocatedBytes / 1048576.0:0.0}MiB server={gcProbe.ServerGc} concurrent={gcProbe.ConcurrentGc} Gen2={gcProbe.Gen2After - gcProbe.Gen2Before} automaticGen2Observed={gcProbe.AutomaticGen2Observed} CPU={gcProbe.ProcessCpuMs:0.00}ms"));
            text.AppendLine(FormattableString.Invariant($"GC phase Input main p95={gcProbe.MainInput.P95Ms:0.00}ms max={gcProbe.MainInput.MaxMs:0.00}ms capture p95={gcProbe.CaptureInput.P95Ms:0.00}ms max={gcProbe.CaptureInput.MaxMs:0.00}ms"));
            await File.WriteAllTextAsync(Paths.In("responsiveness.txt"), text.ToString());
            Log.Write("responsiveness probe completed");
        }
        catch (Exception e)
        {
            Log.Error("responsiveness probe", e);
            await File.WriteAllTextAsync(Paths.In("responsiveness-error.txt"), e.ToString());
        }
        finally
        {
            heartbeat?.Stop();
            load.Cancel();
            worker?.Join(1000);
            host.Quit(save: false);
        }
    }

    private static async Task<bool> WaitFor(Func<bool> ready, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed >= timeout) return false;
            await Task.Delay(10);
        }
        return true;
    }

    private static DrawingTiming MeasureDrawing(int count, bool curve)
    {
        const int width = 1000, height = 600;
        var canvas = new Canvas { Width = 1280, Height = 800 };
        var layer = new DrawLayer(canvas, new Rect(40, 40, width, height), new Size(1280, 800), _ => { });
        var ink = canvas.Children.OfType<InkCanvas>().Single();
        bool defaultCurve = ink.DefaultDrawingAttributes.FitToCurve;
        var attributes = ink.DefaultDrawingAttributes.Clone();
        attributes.FitToCurve = curve;
        var points = new StylusPointCollection(count);
        for (int i = 0; i < count; i++)
            points.Add(new StylusPoint(500 + 400 * Math.Sin(i * .01), 300 + 220 * Math.Sin(i * .013), .5f));
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 70;
            pixels[i + 1] = 60;
            pixels[i + 2] = 50;
            pixels[i + 3] = 255;
        }
        var watch = Stopwatch.StartNew();
        ink.Strokes.Add(new Stroke(points, attributes));
        double insertion = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        canvas.Measure(new Size(1280, 800));
        canvas.Arrange(new Rect(0, 0, 1280, 800));
        canvas.UpdateLayout();
        var rendered = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(canvas);
        double layout = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        byte[] merged = layer.Merge(pixels, width, height);
        return new DrawingTiming(count, curve, defaultCurve, insertion, layout, watch.Elapsed.TotalMilliseconds, merged.Length == pixels.Length && !ReferenceEquals(merged, pixels));
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private static void Contend(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var slice = Stopwatch.StartNew();
            while (slice.ElapsedMilliseconds < 8 && !stop.IsCancellationRequested) Thread.SpinWait(1000);
            if (stop.WaitHandle.WaitOne(8)) return;
        }
    }
}
