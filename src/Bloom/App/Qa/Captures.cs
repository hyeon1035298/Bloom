using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// QA: `--capture <dir> --data <dev dir>` draws each view off-screen with RenderTargetBitmap (no
/// window on the user's screens, no focus stolen) and exits. Acrylic is not in such pictures; the
/// solid fallback stands in for it.
internal static partial class Captures
{
    /// `--qa-set <name>`: one set of screens each (no set = the first, general one).
    private static readonly System.Collections.Generic.Dictionary<string, Func<Host, Launcher, string, Task>> Sets = new()
    {
        ["v3"] = V3Screens,
        ["v4"] = V4Screens,
        ["v5"] = V5Screens,
        ["v6"] = V6Screens,
        ["v7"] = V7Screens,
        ["v8"] = V8Screens,
        ["v9"] = V9Screens,
        ["v10"] = V10Screens,
        ["v11"] = V11Screens,
        ["v12"] = V12Screens,
        ["v13"] = V13Screens,
        ["v14"] = V14Screens,
        ["v15"] = V15Screens,
        ["v16"] = V16Screens,
        ["v17"] = V17Screens,
        ["lang"] = LangScreens,
        ["keys"] = KeysScreens,
        ["chat"] = ChatScreens,
        ["chatwin"] = ChatWindowScreens,
        ["mine"] = MineScreens,
        ["mcp"] = McpScreens,
        ["tools"] = ToolsScreens,
        ["notesmd"] = NotesMdScreens,
        ["docs"] = DocsScreens,
        ["mdview"] = MdViewScreens,
        ["usage"] = UsageScreens,
        ["notices"] = NoticeScreens,
        ["promo"] = PromoScreens,
        ["motion"] = MotionScreens,
        ["long"] = LongScreens,
        ["readme"] = ReadmeScreens,
        ["new"] = NewScreens,
        ["draw"] = DrawScreens,
        ["guide"] = GuideScreens,
        ["home"] = HomeScreens,
        ["homeinput"] = HomeInputScreens,
        ["omp"] = OmpScreens,
        ["omp-missing"] = OmpMissingScreens,
        ["palette"] = PaletteScreens,
        ["palette-perf"] = PalettePerf,
        ["providers"] = ProviderScreens,
    };

    public static async Task Run(Host host, string dir)
    {
        Directory.CreateDirectory(dir);
        var w = host.Window;
        try
        {
            w.Left = -32000;
            w.Top = -32000;
            w.ShowActivated = false;
            w.Show();
            for (int i = 0; i < 40 && !host.Apps.Ready; i++) await Task.Delay(100);
            await Task.Delay(300);
            if (host.Opt.QaScale is { } scale) Shots.Scale = scale;
            if (host.Opt.QaSet is { } set && Sets.TryGetValue(set, out var screens))
            {
                await screens(host, w, dir);
                host.Quit();
                return;
            }
            foreach (string theme in new[] { "light", "dark" })
            {
                Theme.Apply(theme);
                await Shot(w, dir, theme + "-01-root", Mode.Root, "");
                await Shot(w, dir, theme + "-02-search", Mode.Root, "메모");
                await Shot(w, dir, theme + "-03-calc", Mode.Root, "3억 5천만 × 2");
                await Shot(w, dir, theme + "-04-hangul-typo", Mode.Root, "ㄷㅍㄷ교");
                if (theme == "light") await Shot(w, dir, theme + "-04b-chosung", Mode.Root, "ㅁㅁㅈ");
                if (theme == "light")
                {
                    await Shot(w, dir, theme + "-05-units", Mode.Root, "30평 to m2");
                    await Shot(w, dir, theme + "-06-currency", Mode.Root, "100 usd", 2500);
                    await Shot(w, dir, theme + "-07-files", Mode.Files, "TASK.md", 900);
                    await Shot(w, dir, theme + "-09-question", Mode.Root, "내일 서울 날씨 어때", 900);
                }
                // chat: a local example thread (never sent)
                var t = new ChatThread { Id = "capture", Title = "런처 단축키 정리", Model = "sonnet" };
                t.Messages.Add(new ChatMessage { Role = "user", Text = "이 런처 단축키 정리해줘" });
                t.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Text = "자주 쓰는 것만 추렸어요.\n\n| 키 | 동작 |\n|---|---|\n| `Win` | 런처 열기·닫기 |\n| `Tab` | 입력한 내용을 **AI에게** 묻기 |\n| `Ctrl` + `K` | 선택한 항목의 동작 |\n| `Ctrl` + `D` | 즐겨찾기 추가·빼기 |\n\n- 파일은 Everything에서 바로 찾아요\n- 탭에서 `Ctrl` + `Enter`를 누르면 그 페이지로 질문할 수 있어요",
                });
                w.ShowChat(t);
                await Snap(w, dir, theme + "-10-chat");
                if (theme == "light")
                {
                    // the model menu (Ctrl+M; nothing picked) and a ChatGPT conversation's chips
                    w.PickModel();
                    await Snap(w, dir, theme + "-10b-models");
                    t.Model = AiModels.Normalize("chatgpt");
                    w.ShowChat(t);
                    await Snap(w, dir, theme + "-10c-chat-gpt");
                }
                // settings: a window of their own, drawn off-screen as well
                var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
                sw.Show();
                await Task.Delay(400);
                await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + "-11-settings.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (theme == "light")
                {
                    foreach (var (word, name) in new[] { ("모델", "-11c-settings-models"), ("생각 수준", "-11d-settings-levels"), ("색", "-11e-settings-colour") })
                    {
                        sw.Filter(word);
                        await Task.Delay(250);
                        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                }
                sw.Close();
                w.Go(Mode.Root, "메모");
                await Task.Delay(300);
                w.OpenActions();
                await Snap(w, dir, theme + "-12-actions");
                if (theme == "light")
                {
                    // only the question; nothing here presses Enter on it
                    w.Go(Mode.Root, "재시작");
                    await Task.Delay(300);
                    host.RunCommand("cmd:restart");
                    await Snap(w, dir, theme + "-13-restart-confirm");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("capture", e);
            File.WriteAllText(Path.Combine(dir, "error.txt"), e.ToString());
        }
        host.Quit();
    }

    // ── one real window: does the DWM acrylic show? ─────────────────────────────────────────────
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Native.Win32.RECT rect, int size);

    /// `--qa-glass <png>`: a plain backdrop over the given monitor, the launcher shown and focused on
    /// top of it for a moment, a screen grab of the launcher's rectangle, then everything closes.
    public static async Task Glass(Host host, string file, int display)
    {
        var backdrop = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            Background = new LinearGradientBrush(Color.FromRgb(0x1E, 0x4F, 0x8C), Color.FromRgb(0xE0, 0x7A, 0x3A), 35),
        };
        var shapes = new System.Windows.Controls.Canvas();
        for (int i = 0; i < 9; i++)
            shapes.Children.Add(new System.Windows.Shapes.Ellipse { Width = 260, Height = 260, Fill = new SolidColorBrush(i % 2 == 0 ? Color.FromRgb(0xF4, 0xF1, 0xEA) : Color.FromRgb(0x14, 0x16, 0x1B)), Margin = new Thickness(180 + i * 230, 260 + (i % 3) * 170, 0, 0) });
        backdrop.Content = shapes;
        var w = host.Window;
        try
        {
            w.Place(display);
            var mons = Launcher.Monitors();
            var info = new Native.Win32.MONITORINFO { cbSize = Marshal.SizeOf<Native.Win32.MONITORINFO>() };
            Native.Win32.GetMonitorInfo(mons[Math.Clamp(display - 1, 0, mons.Count - 1)], ref info);
            var mr = info.rcMonitor;
            backdrop.Show();
            Native.Win32.SetWindowPos(new WindowInteropHelper(backdrop).Handle, Native.Win32.HWND_TOPMOST, mr.Left, mr.Top, mr.Right - mr.Left, mr.Bottom - mr.Top, Native.Win32.SWP_NOACTIVATE);
            for (int i = 0; i < 40 && !host.Apps.Ready; i++) await Task.Delay(100);
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                w.Go(Mode.Root, "메모");
                w.Place(display);
                w.Show();
                w.Activate();
                Native.Win32.ForceForeground(w.Handle);
                await Task.Delay(900);
                _ = DwmGetWindowAttribute(w.Handle, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var rc, Marshal.SizeOf<Native.Win32.RECT>());
                int width = rc.Right - rc.Left + 40, height = rc.Bottom - rc.Top + 40;
                IntPtr screen = Native.Win32.GetDC(IntPtr.Zero);
                IntPtr mem = CreateCompatibleDC(screen);
                IntPtr bmp = CreateCompatibleBitmap(screen, width, height);
                IntPtr old = SelectObject(mem, bmp);
                BitBlt(mem, 0, 0, width, height, screen, rc.Left - 20, rc.Top - 20, 0x00CC0020 | 0x40000000);
                SelectObject(mem, old);
                var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Native.Win32.DeleteObject(bmp);
                DeleteDC(mem);
                _ = Native.Win32.ReleaseDC(IntPtr.Zero, screen);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using (var fs = File.Create(file.Replace(".png", "-" + theme + ".png"))) enc.Save(fs);
                w.Hide();
            }
            var me = System.Diagnostics.Process.GetCurrentProcess();
            File.WriteAllText(file.Replace(".png", ".txt"), $"working set {me.WorkingSet64 / 1048576.0:0.0} MB, private {me.PrivateMemorySize64 / 1048576.0:0.0} MB, threads {me.Threads.Count}, foreground ok {Native.Win32.GetForegroundWindow() == w.Handle}");
        }
        catch (Exception e)
        {
            File.WriteAllText(file.Replace(".png", "-error.txt"), e.ToString());
        }
        backdrop.Close();
        host.Quit();
    }

    /// `--qa-chat <file> [--qa-model m1,m2…] [--qa-effort e] [--qa-ask "q1|q2"]`: short turns through
    /// the real CLIs (no window; Haiku and two sums by default; one model per turn, the last one
    /// repeating, so a list also checks the switch between Claude and ChatGPT), replies to the file.
    public static async Task Chat(Host host, string file, string model, string? effort, string[]? asks)
    {
        var log = new System.Text.StringBuilder();
        var t = host.Chats.New();
        string[] models = model.Split(',');
        if (effort is not null) t.Effort = effort;
        var started = DateTime.UtcNow;
        int turn = 0;
        // BLOOM_QA_RESTART=<seconds>: between asks the CLI is closed and that long waited (an idle close), so the next ask
        // resumes the session in a new CLI — its log line shows whether the prompt cache was read or written again
        int restart = int.TryParse(Environment.GetEnvironmentVariable("BLOOM_QA_RESTART"), out int wait) ? wait : -1;
        foreach (string q in asks ?? new[] { "1+1은? 숫자만", "방금 답에 10을 곱하면? 숫자만" })
        {
            if (turn > 0 && restart >= 0)
            {
                host.Chats.EndSession(t);
                await Task.Delay(TimeSpan.FromSeconds(restart));
            }
            t.Model = models[Math.Min(turn++, models.Length - 1)];
            var sent = DateTime.UtcNow;
            host.Chats.Send(t, q);
            int deltas = 0;
            void OnUpdate(ChatThread x) => deltas++;
            host.Chats.Updated += OnUpdate;
            // a tool call that asks: answered as BLOOM_QA_ASK says (allow; anything else declines)
            bool allow = Environment.GetEnvironmentVariable("BLOOM_QA_ASK") == "allow";
            while (t.Busy && DateTime.UtcNow - sent < TimeSpan.FromSeconds(120))
            {
                if (t.Messages[^1].Asks.FirstOrDefault() is { } ask)
                {
                    log.AppendLine($"ASK ({ask.Tool}): {ask.Says} → {(allow ? "allow" : "decline")}");
                    host.Chats.Answer(t, ask, allow);
                }
                await Task.Delay(100);
            }
            host.Chats.Updated -= OnUpdate;
            var a = t.Messages[^1];
            log.AppendLine($"Q ({t.Model} · {Chats.EffortOf(t)}): {q}");
            log.AppendLine($"A: {a.Text.Trim()} (error={a.Error}, updates={deltas}, {(DateTime.UtcNow - sent).TotalSeconds:0.0}s)");
            foreach (string d in a.Done ?? new()) log.AppendLine("DONE: " + d);
        }
        log.AppendLine($"model {t.Model} effort {Chats.EffortOf(t)} session {t.SessionId} ({t.SessionProvider}) · total {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
        File.WriteAllText(file, log.ToString());
        host.Quit();
    }

    private static async Task Shot(Launcher w, string dir, string name, Mode mode, string text, int wait = 450)
    {
        w.Go(mode, text);
        await Task.Delay(wait);
        await Snap(w, dir, name);
    }

    private static async Task Snap(Launcher w, string dir, string name)
    {
        await Task.Delay(250);
        await w.Dispatcher.InvokeAsync(() => w.Capture(Path.Combine(dir, name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
