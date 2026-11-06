using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set mine`: 내 명령 and the file actions. A script in a folder with a space and a Korean name is run for real
/// (hidden; its result card off screen), and a chain that calls it; what came back goes to mine.txt. Nothing is pasted
/// and no app is started.
internal static partial class Captures
{
    private static async Task MineScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        Notices.Quiet = true; // result cards stay off screen
        string folder = Path.Combine(dir, "my scripts");
        Directory.CreateDirectory(folder);
        string ps1 = Path.Combine(folder, Tr("백업.ps1", "backup.ps1"));
        File.WriteAllText(ps1, "param($p)\n\"받은 입력: $p\"\n\"완료 3개\"\n", new System.Text.UTF8Encoding(true));
        var script = UserCommands.New("script", Tr("사진 백업", "Back up photos"), $"{ps1} {{input}}", "bk");
        var line = UserCommands.New("script", Tr("오늘 날짜", "Today's date"), "Get-Date -Format \"yyyy-MM-dd (ddd)\"", "td");
        var snippet = UserCommands.New("snippet", Tr("메일 인사", "Mail greeting"), Tr("안녕하세요, {date} 회의 자료 보내드립니다.\\n감사합니다.", "Hello, here are the notes for {date}.\\nThanks."), "hi");
        var chain = UserCommands.New("chain", Tr("백업 후 확인", "Back up, then check"), Tr("bk D:\\사진 ; 기다리기 1초 ; td", "bk D:\\photos ; wait 1 ; td"), "bc");
        var notes = UserCommands.New("ai", Tr("회의록 정리", "Tidy meeting notes"), Tr("결정한 것과 할 일만 목록으로 >> 영어로 번역", "Only the decisions and to-dos as a list >> Translate into Korean"), "mm");
        var daily = UserCommands.New("ai", Tr("오늘의 영어 표현", "Phrase of the day"), Tr("오늘 써먹을 영어 표현 하나와 예문 두 개", "One useful Korean phrase for today with two examples"), null);
        daily.Input = AiInput.None;
        daily.Every = "daily 09:00";
        daily.LastRun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var c in new[] { script, line, snippet, chain, notes, daily }) UserCommands.Upsert(c);

        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{l}-{theme}-m1-commands", Mode.Commands, "", 700);
        }
        Theme.Apply("dark");
        await Shot(w, dir, $"{l}-dark-m2-typed", Mode.Root, Tr("bk D:\\사진", "bk D:\\photos"), 700);
        w.OpenActions();
        await Snap(w, dir, $"{l}-dark-m3-script-actions");
        w.CloseActionsForQa();
        w.StartForm(host.Engine.CommandForm("chain", null, null));
        w.SetTextForQa(Tr("메모장 ; 볼륨 30 ; 없는단계xyz", "notepad ; volume 30 ; nosuchstepxyz"));
        await Snap(w, dir, $"{l}-dark-m4-chain-form");

        // 내 AI 명령: its form, its actions, the AI 빠른 동작 list with it, 알림 센터 (nothing is asked of an AI)
        w.StartForm(host.Engine.CommandForm("ai", null, null));
        w.SetTextForQa(Tr("핵심 3줄로 요약 >> 영어로 번역", "Summarize in 3 lines >> Translate into Korean"));
        await Snap(w, dir, $"{l}-dark-m6-ai-form");
        await Shot(w, dir, $"{l}-dark-m7-ai-search", Mode.Root, Tr("오늘의 영어", "phrase of the"), 700);
        w.OpenActions();
        await Snap(w, dir, $"{l}-dark-m8-ai-actions");
        w.CloseActionsForQa();
        host.Quick.Source = Tr("내일 3시 회의는 4시로 미룹니다. 민수는 자료를, 지은은 예약을 맡기로 했습니다.", "Tomorrow's 3 pm meeting moves to 4. Min brings the slides, Ji books the room.");
        host.Quick.FromSelection = true;
        host.Quick.Action = host.Quick.Result = host.Quick.Error = null;
        await Shot(w, dir, $"{l}-dark-m9-quick-mine", Mode.Quick, "", 700);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        NoticeLog.Add(false, Tr("AI 명령 · 실패", "AI command · Failed"), daily.Name, Tr("Claude 답변이 없습니다. 다시 시도하세요.", "No answer from Claude. Try again."), null, now - 26 * 3600 * 1000L);
        NoticeLog.Add(true, Tr("AI 명령 · 완료", "AI command · Done"), daily.Name, Tr("**놓치다(miss)** — 버스를 놓쳤어요.\n1. 기차를 놓칠 뻔했어요.\n2. 그 기회는 놓치지 마세요.", "**I'm swamped** — I have too much to do.\n1. I'm swamped this week.\n2. Sorry, I was swamped yesterday."), "qa-none", now - 3600 * 1000L);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{l}-{theme}-m10-inbox", Mode.Inbox, "", 700);
        }
        Theme.Apply("dark");
        NoticeLog.Clear();

        var sw = new SettingsWindow(host) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Filter(Tr("AI 채팅 창", "AI chat window"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-m5-chat-shortcut.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();

        // the file actions on a file result
        w.Go(Mode.Root, "");
        var file = host.Engine.FromPath(ps1, false, null);
        await File.WriteAllTextAsync(Path.Combine(dir, "file-actions.txt"), string.Join("\n", file.MoreActions!().Select(a => $"{a.Title} {a.Keys}")));

        // run them for real: the script hidden, then the chain that calls it
        var log = new System.Text.StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        host.RunMine(script, Tr("D:\\내 사진", "D:\\my photos"));
        while (host.IsRunning(script) && clock.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
        log.AppendLine($"script {clock.Elapsed.TotalSeconds:0.0} s: {host.LastOutput(script)}");
        host.RunMine(line, "");
        while (host.IsRunning(line) && clock.Elapsed < TimeSpan.FromSeconds(40)) await Task.Delay(100);
        log.AppendLine($"line: {host.LastOutput(line)}");
        foreach (string step in UserCommands.Steps(chain.Body))
            log.AppendLine($"chain step '{step}' → {(UserCommands.Wait(step) is { } wt ? $"wait {wt.TotalSeconds} s" : host.Engine.StepAction(step, 0) is null ? "nothing" : "runs")}");
        log.AppendLine($"chain step 'notepad'-like '{Tr("메모장", "notepad")}' → {(host.Engine.StepAction(Tr("메모장", "notepad"), 0) is null ? "nothing" : "runs (not run here)")}");
        log.AppendLine("snippet text: " + UserCommands.Expand(UserCommands.SnippetText(snippet.Body), "", null).Replace("\n", "⏎"));
        await File.WriteAllTextAsync(Path.Combine(dir, "mine.txt"), log.ToString());
        foreach (var c in new[] { script, line, snippet, chain, notes, daily }) UserCommands.Remove(c);
        Notices.Quiet = false;
    }

    /// `--qa-set mcp`: a made-up chat where the AI did one thing on the PC and waits for a yes on the next (the
    /// approval card), in the quick chat with its 새 대화 · 세션 종료 chips, dark and light; the settings row.
    private static async Task McpScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var t = new ChatThread { Id = "qa-mcp", Title = Tr("다운로드 정리", "Tidy downloads"), Model = "sonnet" };
        t.Messages.Add(new ChatMessage { Role = "user", Text = Tr("어제 받은 견적서 PDF 찾아서 문서 폴더로 옮기고, 10분 타이머 맞춰줘", "Find yesterday's quote PDF, move it to Documents, and set a 10 min timer") });
        var a = new ChatMessage { Role = "assistant", Text = Tr("`견적서_1007.pdf`를 찾았습니다. 문서 폴더로 옮깁니다.", "Found `quote_1007.pdf`. Moving it to Documents."), Streaming = true, Status = Tr("승인 대기", "Approval pending") };
        string timer = Tr("10분 타이머", "10 min timer");
        a.Done = new() { "✓ " + Tr($"Bloom에서 실행: {timer}", $"Run in Bloom: {timer}") };
        a.Asks.Add(new ToolAsk("qa", "mcp__bloom__pc", "{}", Tr(@"옮기기: 견적서_1007.pdf → C:\Users\me\Documents", @"Move: quote_1007.pdf → C:\Users\me\Documents")));
        t.Messages.Add(a);
        host.Chats.SetLiveForQa(t, true);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.ShowChat(t);
            await Snap(w, dir, $"{l}-{theme}-p1-approval");
        }
        Theme.Apply("dark");
        w.OpenActions();
        await Snap(w, dir, $"{l}-dark-p2-actions");
        w.CloseActionsForQa();
        host.Chats.SetLiveForQa(t, false);
        // 설정 > PC 작업 with the clipboard switched off, 설정 > 기능 with two features off (then all back on)
        Settings.Current.AiToolsOff.Add("clip");
        Features.Set("processes", false);
        Features.Set("space", false);
        var sw = new SettingsWindow(host) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (var (page, name, row) in new[] { ("mcp", "p3-settings-mcp", 0), ("mcp", "p3b-settings-mcp-servers", -3), ("features", "p4-settings-features", 0) })
        {
            sw.Page(page);
            if (row != 0) sw.SelectForQa(row); // the other MCP servers found on this PC, lower down the page
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-dark-{name}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
        // a search for a feature switched off finds nothing of it; on again, it is back (feature-off.txt)
        w.Go(Mode.Root);
        w.SetTextForQa(Tr("프로세스 종료", "end process"));
        await Snap(w, dir, $"{l}-dark-p5-feature-off");
        string offIds = string.Join(" ", w.ResultIdsForQa());
        Settings.Current.AiToolsOff.Clear();
        Settings.Current.FeaturesOff.Clear();
        w.SetTextForQa(Tr("프로세스 종료 ", "end process "));
        await Snap(w, dir, $"{l}-dark-p6-feature-on");
        File.WriteAllText(Path.Combine(dir, "feature-off.txt"), $"off: {offIds}\non: {string.Join(" ", w.ResultIdsForQa())}\n");
    }
}
