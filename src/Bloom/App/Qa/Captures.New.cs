using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// QA for the newer features: `--capture <dir> --qa-set new` (clipboard, preview, calendar,
/// quicklinks, folders, processes, OCR view, translation panel), `--qa-paste`, `--qa-ocr`, `--qa-space`.
internal static partial class Captures
{
    /// `--qa-set v14`: files in the AI chat — a CSV and a picture waiting under a new conversation; with BLOOM_QA_AI=1
    /// one real message each to Claude Haiku and ChatGPT asking them to read the CSV and make a file (2 calls), the
    /// answers and the files they made in chatfiles.txt.
    private static async Task V14Screens(Host host, Launcher w, string dir)
    {
        string csv = Path.Combine(dir, "sales.csv");
        File.WriteAllText(csv, "month,sales\n1월,120\n2월,95\n3월,143\n");
        string png = Path.Combine(dir, "dot.png");
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 120, 80));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE0, 0x4E, 0x78)), null, new Point(60, 40), 26, 26);
        }
        var rtb = new RenderTargetBitmap(120, 80, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        using (var fs = File.Create(png))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(fs);
        }
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.ShowChat(host.Chats.New());
            w.Attach(new[] { csv, png });
            await Task.Delay(500);
            await Snap(w, dir, theme + "-f01-pending");
        }
        Theme.Apply("dark");
        if (Environment.GetEnvironmentVariable("BLOOM_QA_AI") != "1") return;
        var log = new System.Text.StringBuilder();
        foreach (var model in new[] { "haiku", "chatgpt:gpt-6-luna" })
        {
            var t = host.Chats.New();
            t.Model = model;
            t.Effort = Ai.AiModels.Fit(model, "low");
            w.ShowChat(t);
            w.Attach(new[] { csv, png });
            w.SetTextForQa("sales.csv의 합계와 평균을 구하고, 결과를 표로 정리한 summary.md 파일을 만들어줘. 그림에 뭐가 있는지도 한 줄로 말해줘.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            w.SendChat();
            await Task.Delay(500);
            while (t.Busy && sw.Elapsed < TimeSpan.FromSeconds(150)) await Task.Delay(400);
            var last = t.Messages[^1];
            log.AppendLine($"== {model}: {sw.ElapsedMilliseconds} ms, error={last.Error}");
            log.AppendLine("sent files: " + string.Join(", ", t.Messages[^2].Files?.Select(Path.GetFileName) ?? Array.Empty<string>()));
            log.AppendLine("made files: " + string.Join(", ", last.Files?.Select(f => $"{Path.GetFileName(f)} ({new FileInfo(f).Length} B)") ?? Array.Empty<string>()));
            if (last.Files?.FirstOrDefault() is { } made) log.AppendLine("--- " + Path.GetFileName(made) + "\n" + File.ReadAllText(made));
            log.AppendLine("--- answer\n" + last.Text);
            w.ShowChat(t);
            await Task.Delay(700);
            await Snap(w, dir, "dark-f02-answer-" + (model == "haiku" ? "claude" : "chatgpt"));
        }
        File.WriteAllText(Path.Combine(dir, "chatfiles.txt"), log.ToString());
    }

    /// `--qa-set v13` (fresh data): the browser and home-tools screens (switches; the second look starts from the home and
    /// takes out what is switched off), the home with them, the renamed 브라우저 view. The favorites after closing go to tools.txt.
    private static async Task V13Screens(Host host, Launcher w, string dir)
    {
        async Task Cap(OnboardingWindow ob, string name)
        {
            await Task.Delay(250);
            await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var ob = new OnboardingWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            ob.Show();
            ob.Go(2);
            await Cap(ob, theme + "-n1-browser");
            ob.Go(4);
            await Cap(ob, theme + "-n2-tools");
            if (theme == "dark")
            {
                ob.ToggleToolForQa("cmd:color");
                ob.ToggleToolForQa("cmd:space");
                await Cap(ob, theme + "-n3-tools-switched");
            }
            else
            {
                ob.ToggleToolForQa("cmd:timer"); // a second look: on the home already, so switching it off takes it out
                await Cap(ob, theme + "-n4-tools-second-look");
            }
            ob.Close();
        }
        var log = new System.Text.StringBuilder();
        foreach (var e in Favorites.Current.Items)
            log.AppendLine($"{e.Kind} {e.Title}" + (e.Items is { } inside ? " [" + string.Join(", ", inside.Select(x => x.Title)) + "]" : ""));
        File.WriteAllText(Path.Combine(dir, "tools.txt"), log.ToString());
        Theme.Apply("dark");
        await Shot(w, dir, "dark-n5-home", Mode.Root, "", 900);
        await Shot(w, dir, "dark-n6-browser-view", Mode.Quicklinks, "", 700);
        var sideLog = new System.Text.StringBuilder();
        foreach (string q in new[] { "작업 관리자", "everything", "blender", "chrome", "powershell", "terminal", "cmd" })
            foreach (var it in host.Engine.Root(q).SelectMany(x => x.Items).Take(3))
                sideLog.AppendLine($"{q} | {it.Title} | {it.Kind} | {(it.Data as AppEntry)?.Source} | {(it.Data as AppEntry)?.Path} | side={(it.Side is not null)}");
        File.WriteAllText(Path.Combine(dir, "side.txt"), sideLog.ToString());
        await Shot(w, dir, "dark-n7-admin-button", Mode.Root, "everything", 700);
        await Shot(w, dir, "dark-n8-admin-tool", Mode.Root, "작업 관리자", 700);
    }

    /// `--qa-set v12`: settings out to a file and back (no dialogs): a round trip that must bring back the theme,
    /// accent, a favorite and a quick search while this PC's CLI path stays, a non-Bloom zip refused, the backup made;
    /// then the welcome screen with its 불러오기 link and the 정보 page. Results in transfer.txt.
    private static async Task V12Screens(Host host, Launcher w, string dir)
    {
        var log = new System.Text.StringBuilder();
        void Check(string what, bool ok) => log.AppendLine($"{(ok ? "OK  " : "FAIL")} {what}");
        var s = Settings.Current;
        s.Theme = "light";
        s.Accent = "mint";
        s.ClaudePath = @"X:\exported\claude.exe";
        Settings.Save();
        Quicklinks.Upsert(new Quicklink { Id = "qa-ql", Name = "QA 검색", Keyword = "qa", Url = "https://example.com/?q={query}" });
        host.SaveAll();
        string zip = Path.Combine(dir, "export.zip");
        int n = SettingsTransfer.Export(zip, "qa");
        Check($"export wrote {n} files", n >= 3 && File.Exists(zip));

        // change everything, then bring the file back
        s.Theme = "dark";
        s.Accent = "rose";
        s.ClaudePath = @"Y:\this-pc\claude.exe";
        Settings.Save();
        Quicklinks.Remove(Quicklinks.Current.Items.Find(q => q.Id == "qa-ql")!);
        host.SaveAll();
        var files = SettingsTransfer.Import(zip);
        host.ReloadForQa();
        Check($"import replaced {string.Join(", ", files)}", files.Contains("settings.json"));
        Check($"theme back: {Settings.Current.Theme}", Settings.Current.Theme == "light");
        Check($"accent back: {Settings.Current.Accent}", Settings.Current.Accent == "mint");
        Check($"this PC's CLI path kept: {Settings.Current.ClaudePath}", Settings.Current.ClaudePath == @"Y:\this-pc\claude.exe");
        Check("quick search back", Quicklinks.Current.Items.Exists(q => q.Id == "qa-ql"));
        Check("onboarded stays true", Settings.Current.Onboarded == true);
        Check("backup made", Directory.Exists(Paths.In("backups")) && Directory.GetDirectories(Paths.In("backups")).Length > 0);

        // a zip that is not Bloom's
        string bad = Path.Combine(dir, "not-bloom.zip");
        using (var z = System.IO.Compression.ZipFile.Open(bad, System.IO.Compression.ZipArchiveMode.Create))
        using (var sw = new StreamWriter(z.CreateEntry("settings.json").Open()))
            sw.Write("{\"theme\":\"dark\"}");
        try
        {
            SettingsTransfer.Import(bad);
            Check("non-Bloom zip refused", false);
        }
        catch (InvalidDataException e)
        {
            Check($"non-Bloom zip refused ({e.Message})", Settings.Current.Theme == "light");
        }
        File.WriteAllText(Path.Combine(dir, "transfer.txt"), log.ToString());

        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var ob = new OnboardingWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            ob.Show();
            await Task.Delay(300);
            await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, theme + "-e01-welcome-import.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            ob.Close();
        }
        Theme.Apply("dark");
        var set = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        set.Show();
        set.Page("about");
        await Task.Delay(350);
        await set.Dispatcher.InvokeAsync(() => set.Capture(Path.Combine(dir, "dark-e02-settings-about.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        set.Close();
        await Shot(w, dir, "dark-e03-search-export", Mode.Root, "설정 내보내기", 600);
    }

    /// `--qa-set v11`: timers and reminders (typed on the home line, their view, the home block, the alarm card drawn
    /// off-screen — nothing rings), the colour view with its gradients (a typed colour; the picker covers every screen, so
    /// it is skipped), the new settings page. Also writes the parser's answers for sample lines to parse.txt.
    private static async Task V11Screens(Host host, Launcher w, string dir)
    {
        var log = new System.Text.StringBuilder();
        foreach (string line in new[] { "10분 타이머", "타이머 1시간 30분", "timer 5m", "30분 뒤 빨래 확인", "15:30 회의 알림", "오후 3시 반에 전화 알려줘", "내일 9시 업로드 리마인더", "10분", "3시", "remind me in 20 min to stretch", "25분 집중 타이머", "블렌더 10분 튜토리얼" })
        {
            var a = TimeParse.Read(line);
            log.AppendLine(a is null ? $"{line} → (none)" : $"{line} → {a.Kind} · {a.Label} · {(a.Length is { } l ? Timers.DurationText(l) : "-")} · {Timers.WhenText(a.Due)}");
        }
        foreach (string c in new[] { "#ff5a8a", "ff5a8a", "#f5a", "rgb(47, 114, 214)", "hsl(342, 100%, 68%)", "123456", "#12345" })
            log.AppendLine($"{c} → {(ColorBook.Parse(c) is { } col ? ColorBook.Hex(col) + " " + ColorBook.Name(col) : "(none)")}");
        foreach (string c in new[] { "12*3", "3 x 4", "2x5", "10 더하기 5 곱하기 2", "100 나누기 4", "10 km to mi", "2026-10-07", "010-1234-5678", "5!", "1,234 + 1" })
            log.AppendLine($"calc {c} → {(Calculator.Evaluate(c) is { } r ? r.Expr + " = " + r.Value : "(none)")}");
        File.WriteAllText(Path.Combine(dir, "parse.txt"), log.ToString());

        Timers.Add("timer", "25분 타이머", DateTime.Now.AddMinutes(24).AddSeconds(31));
        Timers.Add("reminder", "빨래 확인", DateTime.Now.AddMinutes(48));
        Timers.Add("reminder", "영상 업로드", DateTime.Today.AddDays(1).AddHours(9));
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-t01-timers", Mode.Timers, "", 700);
            await Shot(w, dir, theme + "-c01-color", Mode.Color, "#e05a8a", 800);
        }
        Theme.Apply("dark");
        await Shot(w, dir, "dark-t02-typed-timer", Mode.Root, "30분 뒤 빨래 확인", 600);
        await Shot(w, dir, "dark-t03-typed-clock", Mode.Root, "오후 3시 반에 전화 알려줘", 600);
        await Shot(w, dir, "dark-t04-timers-typed", Mode.Timers, "1시간 30분", 600);
        await Shot(w, dir, "dark-t05-home", Mode.Root, "", 900);
        await Shot(w, dir, "dark-t08-calendar", Mode.Calendar, "", 900);
        await Shot(w, dir, "dark-c02-typed-color", Mode.Root, "#2f72d6", 600);
        ColorBook.Add(Color.FromRgb(0x2F, 0x72, 0xD6));
        ColorBook.Add(Color.FromRgb(0x88, 0x88, 0x90));
        ColorBook.Add(Color.FromRgb(0xE0, 0x5A, 0x8A));
        await Shot(w, dir, "dark-c03-color-recent", Mode.Color, "", 800);
        await Shot(w, dir, "dark-c04-grey", Mode.Color, "#888890", 800);
        w.ScrollForQa(330);
        await Task.Delay(300);
        await Snap(w, dir, "dark-c05-color-scrolled");

        // the alarm card, drawn without showing it (it would sit on the user's screen and ring)
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var aw = new AlarmWindow(new TimerEntry { Kind = "reminder", Label = "빨래 확인", Due = DateTimeOffset.Now.ToUnixTimeMilliseconds() }, _ => { }, false);
            var card = (FrameworkElement)aw.Content;
            aw.Content = null;
            var holder = new System.Windows.Controls.Border { Background = theme == "dark" ? new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x38)) : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)), Child = card, Padding = new Thickness(10) };
            holder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            holder.Arrange(new Rect(holder.DesiredSize));
            holder.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(holder.ActualWidth * 1.5), (int)Math.Ceiling(holder.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(holder);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(dir, theme + "-t06-alarm.png"));
            enc.Save(fs);
        }
        Theme.Apply("dark");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("tools");
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-t07-settings-tools.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();

        // the first-run screens
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            var ob = new OnboardingWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            ob.Show();
            for (int i = 0; i < ob.StepCount; i++)
            {
                ob.Go(i);
                await Task.Delay(250);
                await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, $"{theme}-o{i + 1}-onboarding.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            ob.Close();
        }

        // the footer (keycap first, 더보기) and the 더보기 menu opened on the selected row
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-d01-search", Mode.Root, "설정", 700);
            w.OpenActions();
            await Task.Delay(400);
            await Snap(w, dir, theme + "-d02-more");
            w.CloseActionsForQa();
        }
        Theme.Apply("dark");
        await Shot(w, dir, "dark-d03-timers", Mode.Timers, "", 600);
        w.OpenActions();
        await Task.Delay(400);
        await Snap(w, dir, "dark-d04-timer-more");
        w.CloseActionsForQa();
    }

    /// `--qa-set v10`: Bloom Lens on a drawn sample (the picker covers every screen, so it is skipped): the menu, the chat
    /// with the picture, and one real question each to Claude Haiku and ChatGPT (2 calls; answers in lens.txt).
    private static async Task V10Screens(Host host, Launcher w, string dir)
    {
        const int W = 360, H = 160;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2F, 0x72, 0xD6)), null, new Rect(16, 16, 60, 60));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE0, 0x4E, 0x78)), null, new Point(120, 46), 30, 30);
            var ft = new FormattedText("BLOOM 7", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 40, Brushes.Black, 1.0);
            dc.DrawText(ft, new Point(170, 20));
            var ft2 = new FormattedText("Error: shader not found", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, Brushes.DarkRed, 1.0);
            dc.DrawText(ft2, new Point(16, 110));
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[W * H * 4];
        rtb.CopyPixels(px, W * 4, 0);
        host.SetLensForQa(new Ocr.ScreenRect(100, 100, W, H), px);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-l01-lens", Mode.Lens, "", 800);
        }
        Theme.Apply("dark");
        await host.LensDo("ask");
        await Task.Delay(500);
        await Snap(w, dir, "dark-l02-ask-chat");
        if (Environment.GetEnvironmentVariable("BLOOM_QA_AI") != "1") return; // the two real calls only when asked for
        var log = new System.Text.StringBuilder();
        foreach (var model in new[] { "haiku", "chatgpt:gpt-6-luna" })
        {
            var t = host.Chats.New();
            t.Model = model;
            t.Effort = Ai.AiModels.Fit(model, "low");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            host.Chats.Send(t, "이 그림에 보이는 글자와 도형을 한 줄로 말해줘", null, null, host.LastLens!.Png);
            while (t.Busy && sw.Elapsed < TimeSpan.FromSeconds(90)) await Task.Delay(300);
            log.AppendLine($"{model}: {sw.ElapsedMilliseconds} ms · {t.Messages[^1].Text.Replace('\n', ' ')}");
            if (model == "haiku")
            {
                w.ShowChat(t);
                await Task.Delay(600);
                await Snap(w, dir, "dark-l03-answer");
            }
        }
        File.WriteAllText(Path.Combine(dir, "lens.txt"), log.ToString());
    }

    /// `--qa-set v9`: no browser link any more — a search handed to the browser, an address, the 검색 / 연결 settings.
    private static async Task V9Screens(Host host, Launcher w, string dir)
    {
        Theme.Apply("dark");
        await Shot(w, dir, "dark-b01-search-words", Mode.Root, "토마토 파스타 레시피", 700);
        await Shot(w, dir, "dark-b02-address", Mode.Root, "github.com", 700);
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (var page in new[] { "general", "launcher", "shortcuts", "ai", "mcp", "tools", "about" })
        {
            sw.Page(page);
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-b03-settings-" + page + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
    }

    /// `--qa-set v8`: the footer's 홈 편집 button, edit mode (tools + "+ 블록 추가"), then back.
    private static async Task V8Screens(Host host, Launcher w, string dir)
    {
        Settings.Current.Home = null;
        host.Engine.MakePictureIcons();
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-e01-home", Mode.Root, "", 800);
            w.ToggleHomeEdit();
            await Task.Delay(500);
            await Snap(w, dir, theme + "-e02-editing");
            w.ScrollForQa(2000);
            await Snap(w, dir, theme + "-e03-editing-bottom");
            w.ToggleHomeEdit();
            await Task.Delay(300);
        }
        Theme.Apply("dark");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("launcher");
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-e04-settings-home-grips.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.DragHomeForQa("week", 0); // "이번 주" dropped before the first block
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-e05-settings-home-dropped.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        File.WriteAllText(Path.Combine(dir, "order.txt"), string.Join(", ", Home.Order().Select(x => x.Key + (x.On ? "" : "(off)"))));
        Settings.Current.Home = null;
        Favorites.ClearMono();
    }

    /// `--qa-set v7`: the picture icons in each tile style, dark and light (for a comparison sheet).
    private static async Task V7Screens(Host host, Launcher w, string dir)
    {
        host.Engine.MakePictureIcons();
        foreach (string style in new[] { "soft", "tile", "card", "black" })
        {
            Settings.Current.IconStyle = style;
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                await Shot(w, dir, $"{theme}-z-{style}", Mode.Root, "", 700);
            }
        }
        Settings.Current.IconStyle = "tile";
        Favorites.ClearMono();
    }

    /// `--qa-set v6`: two AI-drawn icons (2 Claude calls) on the first two favorites, the answers in draw.txt.
    private static async Task V6Screens(Host host, Launcher w, string dir)
    {
        var favs = Favorites.Current.Items.Take(2).ToList();
        var log = new System.Text.StringBuilder();
        var asks = new[] { ("Blender", (string?)null), ("Bloom", "고양이 발바닥") };
        for (int i = 0; i < favs.Count && i < asks.Length; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (icon, error) = await Ai.IconMaker.Draw(asks[i].Item1, asks[i].Item2, System.Threading.CancellationToken.None);
            log.AppendLine($"{asks[i].Item1} / {asks[i].Item2}: {sw.ElapsedMilliseconds} ms · {(icon is null ? "ERROR " + error : icon.Length + " chars")}");
            if (icon is not null) { log.AppendLine(icon); Favorites.SetMono(favs[i].Id, icon); }
        }
        File.WriteAllText(Path.Combine(dir, "draw.txt"), log.ToString());
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-y01-ai-icons", Mode.Root, "", 900);
        }
        Favorites.ClearMono();
    }

    /// `--qa-set v5`: the home blocks (default order, then everything on and reordered), the 홈 화면 settings page.
    private static async Task V5Screens(Host host, Launcher w, string dir)
    {
        Settings.Current.Home = null; // defaults
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-x01-home", Mode.Root, "", 900);
        }
        Theme.Apply("dark");
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("launcher");
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-x02-settings-home.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Home.Set("clipboard", true);
        Home.Set("threads", true);
        Home.Set("commands", false);
        Home.Move("week", -1);
        Home.Move("week", -1); // week first
        Home.Move("usage", 1); // usage under the favorites
        sw.Page("launcher");
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-x03-settings-home-changed.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        await Shot(w, dir, "dark-x04-home-changed", Mode.Root, "", 900);
        w.ScrollForQa(400);
        await Snap(w, dir, "dark-x05-home-changed-scrolled");
        Settings.Current.Home = null;
        int n = host.Engine.MakePictureIcons();
        Launcher.ShowHomeToolsForQa = true;
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{theme}-x06-home-pictures-{n}-tools", Mode.Root, "", 900);
        }
        Launcher.ShowHomeToolsForQa = false;
        Favorites.ClearMono();
    }

    /// `--qa-set v4`: name icons on the favorites, the colour presets, file search by kind.
    private static async Task V4Screens(Host host, Launcher w, string dir)
    {
        Theme.Apply("dark");
        await Shot(w, dir, "dark-w01-root-before", Mode.Root, "", 600);
        int n = host.Engine.MakePictureIcons();
        await Shot(w, dir, $"dark-w02-root-name-icons-{n}", Mode.Root, "", 600);
        foreach (var key in new[] { "lavender", "mint", "gray" })
        {
            Settings.Current.Accent = key;
            Theme.Apply("dark");
            await Shot(w, dir, "dark-w03-root-" + key, Mode.Root, "", 500);
        }
        Settings.Current.Accent = "sky";
        Theme.Apply("light");
        await Shot(w, dir, "light-w04-root-sky", Mode.Root, "", 500);
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("general");
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "light-w05-settings-general-sky.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        Settings.Current.Accent = "rose";
        Theme.Apply("dark");
        foreach (var kind in new[] { "pic", "folder", "exe", "unity", "blender", "adobe" })
        {
            w.Go(Mode.Files, "");
            w.SetFileKind(kind);
            await Task.Delay(1500);
            await Snap(w, dir, "dark-w06-files-" + kind);
        }
        w.SetFileKind("doc");
        w.Go(Mode.Files, "TASK");
        await Task.Delay(1500);
        await Snap(w, dir, "dark-w07-files-doc-TASK");
        Favorites.ClearMono();
    }

    /// `--qa-set v3`: settings by category, the month calendar, the Bloom-only app name form, a folder without 모두 열기.
    private static async Task V3Screens(Host host, Launcher w, string dir)
    {
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-v01-calendar", Mode.Calendar, "", 900);
            if (theme == "dark")
            {
                w.CalendarGoForQa(DateTime.Today.AddDays(2));
                await Snap(w, dir, theme + "-v02-calendar-day");
                await Shot(w, dir, theme + "-v03-calendar-search", Mode.Calendar, "회의", 600);
                if (host.Apps.Apps.FirstOrDefault(a => a.Name == "메모장") is { } app)
                {
                    w.Go(Mode.Root);
                    w.StartForm(host.Engine.AppNameForm(app));
                    await Snap(w, dir, theme + "-v04-rename-form");
                    AppNames.Set(app.Id, "메모");
                    host.AppsChanged();
                    await Task.Delay(300);
                    await Shot(w, dir, theme + "-v05-renamed-search", Mode.Root, "notepad", 600);
                    AppNames.Set(app.Id, null);
                    host.AppsChanged();
                }
                if (Favorites.Current.Items.FirstOrDefault(e => e.Kind == Favorites.GroupKind) is { } g)
                {
                    w.OpenGroup(g.Id);
                    await Task.Delay(400);
                    await Snap(w, dir, theme + "-v06-folder");
                }
            }
            var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            sw.Show();
            foreach (var page in theme == "dark" ? new[] { "general", "ai", "tools" } : new[] { "general", "tools" })
            {
                sw.Page(page);
                await Task.Delay(350);
                await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + "-v1-settings-" + page + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            if (theme == "dark")
            {
                sw.Filter("생각");
                await Task.Delay(350);
                await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + "-v1-settings-filter.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            sw.Close();
        }
    }

    /// Clipboard history (sample entries), the file preview, the calendar, quicklinks and their form.
    private static async Task NewScreens(Host host, Launcher w, string dir)
    {
        var clip = host.Clipboard;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string img = Path.Combine(Path.GetDirectoryName(Clip.ClipboardHistory.ImagePath(new Clip.ClipEntry { Image = "x" }))!, "qa-sample.png");
        var rtb = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2B, 0x24, 0x27), Color.FromRgb(0xE0, 0x4E, 0x78), 30), null, new Rect(0, 0, 640, 400));
            dc.DrawImage(RenderMark(), new Rect(240, 120, 160, 160));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(img)) enc.Save(fs);
        var samples = new[]
        {
            new Clip.ClipEntry { Id = "clip:qa1", Kind = "text", Text = "이번 주 할 일 정리\n- 발표 자료 마무리\n- 마감 10월 9일", Source = "메모장", Copied = now - 3600_000L * 30, Pinned = true },
            new Clip.ClipEntry { Id = "clip:qa2", Kind = "files", Files = new() { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini") }, Source = "Windows 탐색기", Copied = now - 3600_000L * 26 },
            new Clip.ClipEntry { Id = "clip:qa3", Kind = "image", Image = "qa-sample.png", Width = 640, Height = 400, Source = "캡처 도구", Copied = now - 3600_000L * 2 },
            new Clip.ClipEntry { Id = "clip:qa4", Kind = "text", Text = "https://example.com/docs/getting-started", Source = "Chrome", Copied = now - 60_000L * 12 },
            new Clip.ClipEntry { Id = "clip:qa5", Kind = "text", Text = "public void Apply()\n{\n    if (window is null) return;\n    bool want = Settings.Current.ClipHistory;\n}", Source = "Visual Studio Code", Copied = now - 60_000L },
        };
        foreach (var e in samples) clip.Add(e);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            // a favorites folder with a few apps (dev data only)
            if (!Favorites.Current.Items.Exists(e => e.Kind == Favorites.GroupKind))
            {
                var g = Favorites.AddGroup("영상 편집");
                foreach (var name in new[] { "메모장", "Everything", "그림판", "계산기" })
                    if (host.Apps.Apps.FirstOrDefault(a => a.Name == name) is { } app)
                        Favorites.AddToGroup(g.Id, new Favorites.Entry { Id = app.Id, Kind = app.Source == "uwp" ? "uwp" : "app", Title = app.Name, Target = app.Path });
            }
            if (theme == "dark" && Favorites.Current.Items.FirstOrDefault(e => e.Kind == Favorites.GroupKind) is { } ig)
            {
                // a folder with its own icon, the icon menu, then a drag in the favorites row (first tile → after the third)
                Favorites.SetGroupIcon(ig.Id, "glyph:E714");
                w.Go(Mode.Root, "");
                await Task.Delay(500);
                w.PickGroupIcon(ig.Id);
                await Snap(w, dir, theme + "-n22-icon-menu");
                w.Go(Mode.Root, "");
                await Task.Delay(500);
                var order = string.Join(" | ", Favorites.Current.Items.Select(e => e.Title));
                w.StripForQa?.DragForQa(0, 250, false);
                await Snap(w, dir, theme + "-n23-dragging");
                w.StripForQa?.DragForQa(0, 250, true);
                await Task.Delay(400);
                await Snap(w, dir, theme + "-n24-dropped");
                File.WriteAllText(Path.Combine(dir, "drag.txt"), order + "\n→\n" + string.Join(" | ", Favorites.Current.Items.Select(e => e.Title)));
            }
            await Shot(w, dir, theme + "-n00-root", Mode.Root, "", 900);
            if (Favorites.Current.Items.FirstOrDefault(e => e.Kind == Favorites.GroupKind) is { } fg)
            {
                w.OpenGroup(fg.Id);
                await Task.Delay(500);
                await Snap(w, dir, theme + "-n20-group");
                if (theme == "dark")
                {
                    w.MoveForQa(1);
                    w.OpenActions();
                    await Snap(w, dir, theme + "-n21-group-item-actions");
                }
                w.Go(Mode.Root);
            }
            if (theme == "dark")
            {
                w.MoveForQa(1); // out of the favorites row into the list
                await Snap(w, dir, theme + "-n00b-root-down");
                host.RunCommand("cmd:processes");
                await Task.Delay(2200);
                await Snap(w, dir, theme + "-n16-processes");
            }
            host.LastOcr = new Ocr.OcrOutput("마감은 10월 9일 금요일입니다.\nThe quick brown fox jumps over 13 lazy dogs.", new() { new("a", 0, 0, 1, 1), new("b", 0, 0, 1, 1) }, "ko");
            await Shot(w, dir, theme + "-n17-ocr", Mode.Ocr, "", 600);
            foreach (bool live in new[] { false, true })
            {
                var tw = new Ocr.TranslateWindow(host, new Ocr.ScreenRect(0, 0, 0, 0), null, false) { Left = -32000, Top = -32000, ShowActivated = false };
                tw.Show();
                tw.FillForQa("締め切りは10月9日の金曜日です。\nよろしくお願いします。", "마감은 10월 9일 금요일이에요.\n잘 부탁드려요.", live ? 7 : 1);
                await Task.Delay(300);
                tw.Capture(Path.Combine(dir, theme + (live ? "-n19-translate-b" : "-n18-translate") + ".png"));
                tw.Close();
                if (theme == "light") break;
            }
            await Shot(w, dir, theme + "-n01-clipboard", Mode.Clipboard, "", 900);
            if (theme == "dark")
            {
                w.Go(Mode.Clipboard, "");
                await Task.Delay(300);
                w.MoveForQa(2); // the picture
                await Task.Delay(900);
                await Snap(w, dir, theme + "-n02-clipboard-image");
                w.OpenActions();
                await Snap(w, dir, theme + "-n03-clipboard-actions");
                await Shot(w, dir, theme + "-n04-files-text", Mode.Files, "TASK.md", 1500);
                await Shot(w, dir, theme + "-n05-files-image", Mode.Files, "ext:png icon", 2000);
                await Shot(w, dir, theme + "-n06-files-video", Mode.Files, "Podcast-loop.mp4", 3500);
                await Shot(w, dir, theme + "-n07-files-folder", Mode.Files, "folder:Bloom", 1500);
            }
            await Shot(w, dir, theme + "-n08-calendar", Mode.Calendar, "", 900);
            await Shot(w, dir, theme + "-n09-quicklinks", Mode.Quicklinks, "", 600);
            await Shot(w, dir, theme + "-n10-typed", Mode.Root, "yt 고양이", 600);
            if (theme == "dark")
            {
                await Shot(w, dir, theme + "-n11-search-ql", Mode.Root, "유튜브", 600);
                w.StartForm(host.Engine.QuicklinkForm(null, "https://www.melon.com/search/total/index.htm?q={query}", null));
                await Snap(w, dir, theme + "-n12-form-1");
                w.FormNextForQa();
                await Snap(w, dir, theme + "-n13-form-2");
                w.FormNextForQa();
                w.SetTextForQa("y t");
                w.FormNextForQa(); // a space: refused
                await Snap(w, dir, theme + "-n14-form-error");
                w.Go(Mode.Root);
            }
            var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            sw.Show();
            sw.Filter("클립보드");
            await Task.Delay(400);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + "-n15-settings-clipboard.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            sw.Filter("번역");
            await Task.Delay(400);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, theme + "-n15b-settings-translate.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            sw.Close();
        }
    }

    /// `--qa-paste <txt>`: a text box of our own on the given monitor, an entry pasted into it the way
    /// Enter does (clipboard + Ctrl+V), the box's text written out. The user's clipboard text comes back after.
    public static async Task Paste(Host host, string file, int display)
    {
        string? saved = null;
        try
        {
            saved = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            /* busy: nothing to give back */
        }
        var box = new System.Windows.Controls.TextBox { FontSize = 16, AcceptsReturn = true };
        var win = new Window { Title = "Bloom paste test", Width = 420, Height = 160, Content = box, WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true };
        var log = new System.Text.StringBuilder();
        try
        {
            var mons = Launcher.Monitors();
            var info = new Native.Win32.MONITORINFO { cbSize = Marshal.SizeOf<Native.Win32.MONITORINFO>() };
            Native.Win32.GetMonitorInfo(mons[Math.Clamp(display - 1, 0, mons.Count - 1)], ref info);
            win.Left = info.rcWork.Left / 1.0 + 80;
            win.Top = info.rcWork.Top / 1.0 + 80;
            win.Show();
            win.Activate();
            Native.Win32.ForceForeground(new WindowInteropHelper(win).Handle);
            box.Focus();
            await Task.Delay(400);
            var e = new Clip.ClipEntry { Id = "clip:qa-paste", Kind = "text", Text = "붙여넣기 확인 " + DateTime.Now.ToString("HHmmss") };
            bool ok = await host.Clipboard.Paste(e, new WindowInteropHelper(win).Handle);
            await Task.Delay(500);
            log.AppendLine($"paste returned {ok}; box = \"{box.Text}\"; match {box.Text == e.Text}");
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
        }
        win.Close();
        try
        {
            if (saved is not null) Clipboard.SetDataObject(saved, true);
        }
        catch
        {
            /* leave it */
        }
        File.WriteAllText(file, log.ToString());
        host.Quit();
    }

    /// `--qa-ocr <txt> [--qa-translate]`: known lines drawn into a picture, read back by Windows OCR
    /// (no screen involved); with --qa-translate, the Korean line translated once by each engine.
    public static async Task OcrTest(Host host, string file, bool translate)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            string[] lines = { "Bloom 화면 텍스트 추출 테스트 2026", "The quick brown fox jumps over 13 lazy dogs.", "마감은 10월 9일 금요일입니다." };
            int w = 900, h = 60 + lines.Length * 46;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                for (int i = 0; i < lines.Length; i++)
                    dc.DrawText(new FormattedText(lines[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Theme.Sans, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 22, Brushes.Black, 1.0), new Point(30, 30 + i * 46));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);
            log.AppendLine("languages: " + string.Join(", ", Ocr.WinOcr.Languages()));
            var (big, bw, bh, _) = Ocr.ScreenGrab.ForOcr(px, w, h);
            foreach (var (lang, data, dw, dh) in new[] { ((string?)null, px, w, h), ("ko", px, w, h), ("x2", big, bw, bh) })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await Ocr.WinOcr.Recognize(data, dw, dh, lang == "x2" ? null : lang);
                log.AppendLine($"[{lang ?? "auto"}] {sw.ElapsedMilliseconds} ms, {r?.Lines.Count} lines, language {r?.Language}");
                log.AppendLine(r?.Text ?? "(null)");
                if (r is not null)
                    foreach (var l in r.Lines) log.AppendLine($"  @{l.X:0},{l.Y:0} {l.Width:0}x{l.Height:0}");
            }
            if (translate)
                foreach (string provider in new[] { "claude", "chatgpt" })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (t, err) = await Ai.Translator.Run(lines[2], provider, "en", default);
                    log.AppendLine($"translate {provider} ({Ai.Translator.Model(provider)}) {sw.ElapsedMilliseconds} ms: {t ?? "ERROR " + err}");
                }
        }
        catch (Exception e)
        {
            log.AppendLine(e.ToString());
        }
        File.WriteAllText(file, log.ToString());
        host.Quit();
    }

    /// `--qa-space <dir> --qa-space-path <folder>`: Space Cleaner off-screen on that folder (read only):
    /// the map when the scan is done, then one level in; timing and memory in space.txt.
    public static async Task Space(Host host, string dir, string path)
    {
        Directory.CreateDirectory(dir);
        if (host.Opt.QaScale is { } scale) Shots.Scale = scale;
        var log = new System.Text.StringBuilder();
        var roots = new System.Collections.Generic.List<WeakReference>();
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            log.AppendLine($"before: managed {GC.GetTotalMemory(false) / 1048576.0:0} MB");
            // the scan alone, nothing drawn: what stays after it is not the map's doing
            {
                var only = new Space.SpaceScanner(path);
                only.Start();
                while (only.Running) await Task.Delay(200);
                var wr = new WeakReference(only.Root);
                only = null;
                await Task.Delay(300);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                log.AppendLine($"scan only, released: alive {wr.IsAlive} · managed {GC.GetTotalMemory(false) / 1048576.0:0} MB");
            }
            foreach (string theme in new[] { "dark", "light", "dark" })
            {
                Theme.Apply(theme);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Space.SpaceWindow? win = new Space.SpaceWindow(path) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
                win.Show();
                if (theme == "dark")
                {
                    await Task.Delay(700);
                    win.Capture(Path.Combine(dir, "space-dark-0-scanning.png"));
                }
                while (win.Scanning && sw.Elapsed < TimeSpan.FromMinutes(4)) await Task.Delay(250);
                await Task.Delay(600);
                var me = System.Diagnostics.Process.GetCurrentProcess();
                me.Refresh();
                log.AppendLine($"{theme}: {win.StatusForQa} · wall {sw.Elapsed.TotalSeconds:0.0}s · private {me.PrivateMemorySize64 / 1048576.0:0} MB · managed {GC.GetTotalMemory(false) / 1048576.0:0} MB");
                win.Capture(Path.Combine(dir, $"space-{theme}-1-done.png"));
                if (theme == "dark")
                {
                    win.EnterLargestForQa();
                    await Task.Delay(500);
                    win.Capture(Path.Combine(dir, "space-dark-2-inside.png"));
                }
                if (win.RootForQa is { } wr) roots.Add(wr);
                win.Close();
                win = null; // the map goes with the window
                await Task.Delay(500);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                log.AppendLine($"  after close: managed {GC.GetTotalMemory(false) / 1048576.0:0} MB");
            }
            await Task.Delay(2000);
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            Native.Win32.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            var after = System.Diagnostics.Process.GetCurrentProcess();
            log.AppendLine($"maps still alive after close: {roots.Count(r => r.IsAlive)} of {roots.Count}");
            log.AppendLine($"closed: private {after.PrivateMemorySize64 / 1048576.0:0} MB · working set {after.WorkingSet64 / 1048576.0:0} MB · managed {GC.GetTotalMemory(false) / 1048576.0:0} MB");
        }
        catch (Exception e)
        {
            log.AppendLine(e.ToString());
        }
        File.WriteAllText(Path.Combine(dir, "space.txt"), log.ToString());
        host.Quit();
    }

    private static ImageSource RenderMark()
    {
        var rtb = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Theme.MarkVisual(160));
        return rtb;
    }
}
