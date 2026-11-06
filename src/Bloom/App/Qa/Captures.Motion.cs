using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set motion` (fresh data, dark, 2×; run it with --demo): the frames the promo animation is cut from, in the
    /// language of the run — the home as a first start shows it; an app search, a sum, a unit, a reminder, an emoji and
    /// Space Cleaner typed key by key; 클립보드 기록; a quick chat typed and answered, and one where a file move waits
    /// on its approval card and is done; the 도구 menu, 메모 floating and attached with a note being written, 계산기
    /// under the launcher, a colour pasted and its 컬러 피커 view, a Bloom Lens pick (the picked message drawn here too)
    /// with its menu, a translation card and a task-done card; motion.json gives the sizes (dip) to lay them out as Bloom
    /// does. Files, open windows and recent apps stay out of the pictures; the usage numbers are made up.
    private static async Task MotionScreens(Host host, Launcher w, string dir)
    {
        Shots.Scale = 2;
        Theme.Apply("dark");
        Settings.Current.FilesInRoot = false; // no Everything results, no recent files
        Settings.Current.WindowsInRoot = false;
        host.Apps.KeepForQa(a => a.Name is "Microsoft Store"); // an app every PC has (browsers can be removed), nothing of this one's
        foreach (string id in new[] { "cmd:quicklinks", "cmd:files", "cmd:clipboard", "cmd:timer", "cmd:calendar", "cmd:lens", "cmd:space" }) // a first start's own row
            if (Search.SystemItems.Find(id) is { } e) Favorites.Add(new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target });
        Usage.Current.Items.Clear();
        static UsageAccount Plan(string name, string plan, double? fiveHour, double week)
        {
            var limits = new System.Collections.Generic.List<UsageLimit>();
            if (fiveHour is { } h) limits.Add(new UsageLimit(Tr("5시간", "5h"), h, DateTime.Now.AddHours(3).AddMinutes(12)));
            limits.Add(new UsageLimit(Tr("주간", "Weekly"), week, DateTime.Now.AddDays(4)));
            return new(name, plan, limits, DateTime.Now);
        }
        if (!Demo.On) AiUsage.SetForQa(Plan("Claude", "Ultra", 32, 47), Plan("ChatGPT", "Pro 20x", null, 21)); // --demo made its own per account

        // the launcher: home, then an app found from a name typed with the keyboard left in Korean (in English runs, from
        // its initials), then a sum key by key
        await Shot(w, dir, "L-home", Mode.Root, "", 900);
        string[] app = En ? new[] { "m", "ms" } : new[] { "ㄴ", "ㄴㅅ", "ㄴ새", "ㄴ색", "ㄴ색ㄷ" }; // "store" with the keyboard left in Korean
        for (int i = 0; i < app.Length; i++) await Shot(w, dir, $"L-app-{i + 1:00}", Mode.Root, app[i], 150);
        string sum = "45000*1.1"; // a price with 10 % tax: short, no brackets
        for (int i = 1; i <= sum.Length; i++) await Shot(w, dir, $"L-calc-{i:00}", Mode.Root, sum[..i], 150);

        // a unit, a reminder, an emoji and Space Cleaner, each typed key by key (no frame for a space), and 클립보드 기록
        foreach (var (key, line) in new[] { ("units", Tr("84m2 to 평", "5.9 ft to cm")), ("remind", Tr("30분 뒤 빨래", "laundry in 30 min")), ("emoji", Tr(":하트", ":heart")), ("space", "space") })
            for (int i = 1; i <= line.Length; i++)
                if (line[i - 1] != ' ') await Shot(w, dir, $"L-{key}-{i:00}", Mode.Root, line[..i], 150);
        await Shot(w, dir, "L-clip", Mode.Clipboard, "", 800);

        // the quick chat (Tab): the question typed, then the answer coming in
        var t = new ChatThread { Id = "motion", Title = "", Model = "sonnet" };
        w.ShowChat(t);
        await Snap(w, dir, "L-chat-00");
        string ask = Tr("다음 주 발표 준비할 일 정리해 줘", "List what to prep for next week's talk");
        for (int i = 1; i <= ask.Length; i++)
        {
            if (ask[i - 1] == ' ') continue;
            w.ShowChat(t, ask[..i]);
            await Snap(w, dir, $"L-chat-t{i:00}");
        }
        string answer = Tr(
            "발표 전까지 할 일이에요.\n\n1. **슬라이드 초안** 목요일까지\n2. **리허설** 팀과 한 번\n3. **예상 질문** 다섯 개 정리\n\n하나씩 같이 볼까요?",
            "Here's what to do before the talk.\n\n1. **Draft the slides** by Thursday\n2. **Rehearse** once with the team\n3. **List five questions** people may ask\n\nWant to go through them one by one?");
        t.Messages.Add(new ChatMessage { Role = "user", Text = ask });
        var reply = new ChatMessage { Role = "assistant", Text = "", Streaming = true, Status = Tr("생각하는 중…", "Thinking…") };
        t.Messages.Add(reply);
        w.ShowChat(t);
        await Snap(w, dir, "L-chat-a00"); // before the first words
        reply.Status = null;
        // then two words at a time, as the answer streams in
        // (never inside a **bold** run: an open ** would show as it is for a whole frame)
        var words = Enumerable.Range(1, answer.Length)
            .Where(i => i == answer.Length || (answer[i] is ' ' or '\n' && answer[i - 1] is not (' ' or '\n')))
            .Where(i => (answer[..i].Split("**").Length - 1) % 2 == 0)
            .ToList();
        int step = 0;
        for (int k = 1; k < words.Count + 1; k += 2)
        {
            reply.Text = answer[..words[Math.Min(k, words.Count - 1)]];
            w.ShowChat(t);
            await Snap(w, dir, $"L-chat-a{++step:00}");
        }
        reply.Streaming = false;
        reply.Text = answer;
        w.ShowChat(t);
        await Snap(w, dir, $"L-chat-a{++step:00}");

        // PC 작업: a new quick chat asks for a file to be moved; the AI's step waits on its card, then it is done
        var job = new ChatThread { Id = "motion-pc", Title = "", Model = "sonnet" };
        string jobAsk = Tr("어제 받은 견적서 PDF 문서 폴더로 옮겨 줘", "Move yesterday's quote PDF to Documents");
        for (int i = 1; i <= jobAsk.Length; i++)
        {
            if (jobAsk[i - 1] == ' ') continue;
            w.ShowChat(job, jobAsk[..i]);
            await Snap(w, dir, $"L-pc-t{i:00}");
        }
        string move = Tr(@"옮기기: 견적서_1007.pdf → C:\Users\me\Documents", @"Move: quote_1007.pdf → C:\Users\me\Documents");
        job.Messages.Add(new ChatMessage { Role = "user", Text = jobAsk });
        var did = new ChatMessage { Role = "assistant", Text = Tr("`견적서_1007.pdf`를 찾았습니다. 문서 폴더로 옮깁니다.", "Found `quote_1007.pdf`. Moving it to Documents."), Streaming = true, Status = Tr("승인 대기", "Approval pending") };
        did.Asks.Add(new ToolAsk("motion", "mcp__bloom__pc", "{}", move));
        job.Messages.Add(did);
        host.Chats.SetLiveForQa(job, true);
        w.ShowChat(job);
        await Snap(w, dir, "L-pc-ask");
        did.Asks.Clear();
        did.Done = new() { "✓ " + move };
        did.Text += Tr("\n\n문서 폴더로 옮겼습니다.", "\n\nMoved to Documents.");
        did.Streaming = false;
        did.Status = null;
        host.Chats.SetLiveForQa(job, false);
        w.ShowChat(job);
        await Snap(w, dir, "L-pc-done");

        // the 도구 menu (Ctrl+J), and the launcher pinned as it is while attached tools are in use
        w.Go(Mode.Root);
        w.OpenTools();
        await Snap(w, dir, "L-tools-menu");
        w.CloseActionsForQa();
        w.SetPinned(true);
        await Shot(w, dir, "L-home-pinned", Mode.Root, "", 600);
        double lw = w.FrameForQa.ActualWidth, lh = w.FrameForQa.ActualHeight;

        // 메모: floating at its first size, then on the launcher's right with a note being written
        host.Tools.Open("notes");
        var notes = host.Tools.Find("notes")!;
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(notes).DpiScaleX;
        int Px(double dip) => (int)Math.Round(dip * dpi);
        notes.SetEdge(null);
        notes.PlaceAt(-32000, -32000, Px(300), Px(360));
        await Tool(notes, dir, "T-notes-float");
        notes.SetEdge("right");
        notes.PlaceAt(-32000, -32000, Px(300), Px(lh));
        await Tool(notes, dir, "T-notes-col-00");
        string note = Tr("장보기\n- 우유\n- 계란\n- 식빵", "Groceries\n- milk\n- eggs\n- bread");
        // a frame per Hangul syllable, per Latin word
        var cuts = Enumerable.Range(1, note.Length).Where(i => note[i - 1] is >= '가' and <= '힣' || (note[i - 1] is not (' ' or '\n' or '-') && (i == note.Length || note[i] is ' ' or '\n'))).ToList();
        for (int i = 0; i < cuts.Count; i++)
        {
            ((NotesTool)notes.Tool).TypeForQa(note[..cuts[i]]);
            await Tool(notes, dir, $"T-notes-col-{i + 1:00}");
        }

        // 계산기: dropped under the launcher, a sum typed into it
        host.Tools.Open("calc");
        var calc = host.Tools.Find("calc")!;
        calc.SetEdge("bottom");
        calc.PlaceAt(-32000, -32000, Px(lw), Px(260));
        await Tool(calc, dir, "T-calc-row-00");
        string calcSum = "1200*1.1";
        for (int i = 1; i <= calcSum.Length; i++)
        {
            ((CalcTool)calc.Tool).TypeForQa(calcSum[..i]);
            await Tool(calc, dir, $"T-calc-row-{i:00}");
        }
        host.Tools.Close(calc);
        host.Tools.Close(notes);
        w.SetPinned(false);

        // 컬러 피커: a colour pasted into the search line (the caret's own rose), then its view
        await Shot(w, dir, "L-color-root", Mode.Root, "#ff7a9e", 600);
        await Shot(w, dir, "L-color-view", Mode.Color, "#ff7a9e", 800);

        // Bloom Lens: a message on the screen (M-lens-card.png, drawn here so the video shows the same pixels), its two
        // lines picked, and the launcher with what to do with them
        var lens = LensCard(Path.Combine(dir, "M-lens-card.png"));
        host.SetLensForQa(new Ocr.ScreenRect(0, 0, lens.PickPx.w, lens.PickPx.h), lens.Bgra);
        await Shot(w, dir, "L-lens-menu", Mode.Lens, "", 800);

        // Bloom Lens → 번역: the card with what was read and its translation
        var tw = new Ocr.TranslateWindow(host, new Ocr.ScreenRect(0, 0, 0, 0), null, false) { Left = -32000, Top = -32000, ShowActivated = false };
        tw.Show();
        tw.FillForQa(string.Join("\n", LensSource), LensTranslation, 1);
        await Task.Delay(300);
        tw.Capture(Path.Combine(dir, "TW-translate.png"));
        double tww = tw.ActualWidth, twh = tw.ActualHeight;
        tw.Close();

        // 작업 알림: a Claude Code task done, its card as it shows in the corner
        var nw = new NoticeWindow { Offscreen = true };
        Settings.Current.NoticeSpot = "bottom-right";
        Settings.Current.NoticeSize = "m";
        nw.Add(SampleDone("Claude Code", Tr("결제 모듈 테스트 추가", "Add payment module tests"), 12));
        await Task.Delay(400);
        await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, "N-done.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        nw.Close();

        Shots.Scale = 1;
        File.WriteAllText(Path.Combine(dir, "motion.json"), JsonSerializer.Serialize(new
        {
            scale = 2,
            gap = 8,
            launcher = new { w = lw, h = lh },
            notesFloat = new { w = 300, h = 360 },
            column = new { w = 300, h = lh },
            row = new { w = lw, h = 260 },
            translate = new { w = tww, h = twh },
            lensCard = new { w = lens.W, h = lens.H, pick = new { x = lens.Pick.X, y = lens.Pick.Y, w = lens.Pick.Width, h = lens.Pick.Height } },
        }, Indented));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // The Lens sample: a message in another language, translated into the interface language — English into Korean,
    // Japanese or Chinese, and in English runs Japanese into English. Message text (a person's words), not interface
    // text, so it is per language here rather than in the tables.
    private static bool LensFromJapanese => Lang.Code == "en";

    private static string[] LensSource => LensFromJapanese
        ? new[] { "会議は金曜日の午後3時に変更になりました。", "修正したスライドを持ってきてください。" }
        : new[] { "The meeting has moved to Friday at 3 PM.", "Please bring the updated slides." };

    private static string LensTranslation => Lang.Code switch
    {
        "en" => "The meeting has moved to Friday at 3 PM.\nPlease bring the updated slides.",
        "ja" => "会議は金曜日の午後3時に変更になりました。\n修正したスライドを持ってきてください。",
        "zh" => "会议改到周五下午 3 点了。\n请带上修改后的幻灯片。",
        _ => "회의가 금요일 오후 3시로 변경되었습니다.\n수정한 슬라이드를 가져와 주세요.",
    };

    /// The message the Lens cut picks from: a light card as another app would show it (fictional sender), saved at 2×.
    /// Returns its size and the picked lines' area (dip), and those lines' pixels at 2× as a 200 % screen would give them.
    private static (double W, double H, Rect Pick, (int w, int h) PickPx, byte[] Bgra) LensCard(string file)
    {
        const double W = 560, H = 176, Line = 32;
        var face = new Typeface(new FontFamily("Segoe UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        string[] lines = LensSource;
        FormattedText Text(string s, double size, Brush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, b, 1.0);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xFB, 0xF8, 0xF9)), null, new Rect(0, 0, W, H), 16, 16);
            dc.DrawText(Text(LensFromJapanese ? "Haruka · 2:14 PM" : "Alex Kim · 2:14 PM", 13, new SolidColorBrush(Color.FromRgb(0x7A, 0x72, 0x76))), new Point(24, 20));
            for (int i = 0; i < lines.Length; i++) dc.DrawText(Text(lines[i], 20, new SolidColorBrush(Color.FromRgb(0x1D, 0x1A, 0x1B))), new Point(24, 58 + (i * Line)));
        }
        double textW = lines.Max(l => Text(l, 20, Brushes.Black).WidthIncludingTrailingWhitespace);
        var pick = new Rect(14, 50, Math.Ceiling(textW) + 22, (2 * Line) + 16);
        var bmp = new RenderTargetBitmap((int)(W * 2), (int)(H * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) png.Save(fs);
        int pw = (int)(pick.Width * 2), ph = (int)(pick.Height * 2);
        var px = new byte[pw * ph * 4];
        bmp.CopyPixels(new Int32Rect((int)(pick.X * 2), (int)(pick.Y * 2), pw, ph), px, pw * 4, 0);
        return (W, H, pick, (pw, ph), px);
    }

    /// One tool window's content as it is now, on its solid colour (no acrylic in a picture).
    private static async Task Tool(ToolWindow tw, string dir, string name)
    {
        if (tw.FrameForQa is SmoothBorder frame) frame.Background = Theme.P.Solid;
        await Task.Delay(250);
        await tw.Dispatcher.InvokeAsync(() => Shots.Save(tw.FrameForQa, Path.Combine(dir, name + ".png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
