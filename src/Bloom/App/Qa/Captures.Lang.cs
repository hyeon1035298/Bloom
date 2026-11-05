using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set lang` (with `--lang ja|zh --demo`): the main screens in the run's language — the home, results of each
/// kind, the views, the chat, every settings page and the first-run screens — then {code}-missed.txt: the texts the
/// table lacked on them (shown in English).
internal static partial class Captures
{
    private static async Task LangScreens(Host host, Launcher w, string dir)
    {
        string l = Code;
        Theme.Apply("dark");
        foreach (var (name, mode, text) in new (string, Mode, string)[]
        {
            ("01-home", Mode.Root, ""), ("02-apps", Mode.Root, "notepad"), ("03-calc", Mode.Root, "12*3"),
            ("04-date", Mode.Root, "in 100 days"), ("05-world", Mode.Root, "tokyo 3pm"), ("06-timer", Mode.Root, "10 min timer"),
            ("07-system", Mode.Root, "dark mode"), ("08-layout", Mode.Root, "left half"), ("09-ask", Mode.Root, "what is a launcher"),
            ("10-clipboard", Mode.Clipboard, ""), ("11-calendar", Mode.Calendar, ""), ("12-timers", Mode.Timers, ""),
            ("13-color", Mode.Color, "#ff7a9e"), ("14-emoji", Mode.Emoji, ""), ("15-windows", Mode.Windows, ""),
            ("16-commands", Mode.Commands, ""), ("17-processes", Mode.Processes, ""), ("18-quicklinks", Mode.Quicklinks, ""),
        })
            await Shot(w, dir, $"{l}-{name}", mode, text, 700);

        var t = new ChatThread { Id = "qa-lang", Title = Tr("로그 분석", "Log analysis"), Model = "sonnet" };
        t.Messages.Add(new ChatMessage { Role = "user", Text = Tr("어제 서버 로그에서 에러가 몰린 시간대 찾아줘", "Find when the errors piled up in yesterday's server log") });
        t.Messages.Add(new ChatMessage { Role = "assistant", Text = Tr("오전 3시~4시에 에러의 62%가 몰려 있어요.", "62% of the errors fall between 3 and 4 am.") });
        w.ShowChat(t);
        await Snap(w, dir, $"{l}-19-chat");

        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (var page in new[] { "general", "launcher", "features", "shortcuts", "ai", "accounts", "mcp", "notices", "tools", "addons", "data", "about" })
        {
            sw.Page(page);
            await Task.Delay(350);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"{l}-s-{page}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();

        var ob = new OnboardingWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        ob.Show();
        for (int i = 0; i < ob.StepCount; i++)
        {
            ob.Go(i);
            await Task.Delay(250);
            await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, $"{l}-o{i}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        ob.Close();

        // the question window (초기화 · 삭제 · 다시 시작) and a corner card with its button
        var cw = ConfirmWindow.ForQa(Tr("Bloom 삭제", "Uninstall Bloom"), Tr("이 PC에서 Bloom과 시작 메뉴 바로 가기를 삭제하고 Windows 시작 시 자동 실행을 해제합니다.", "Removes Bloom and its Start menu shortcut from this PC and stops it starting with Windows."),
            (Tr("데이터 유지", "Keep data"), MessageBoxResult.No), (Tr("데이터 삭제", "Delete data"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
        cw.WindowStartupLocation = WindowStartupLocation.Manual;
        cw.Left = cw.Top = -32000;
        cw.ShowActivated = false;
        cw.Show();
        await Task.Delay(300);
        await cw.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)cw.Content, Path.Combine(dir, $"{l}-x1-confirm.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        cw.Close();
        var nw = new NoticeWindow { Offscreen = true };
        nw.Add(new Notice("limit", "Bloom Lens", Tr("글자 인식 실패", "Text recognition failed"), Tr("설치된 인식 언어를 확인하세요", "Check installed recognition languages"), ActionText: Tr("인식 언어 설정", "Recognition language settings"), Action: () => { }));
        await Task.Delay(400);
        await nw.Dispatcher.InvokeAsync(() => nw.Capture(Path.Combine(dir, $"{l}-x2-notice-action.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        nw.Close();
        File.WriteAllLines(Path.Combine(dir, $"{l}-missed.txt"), Misses().Order(StringComparer.Ordinal));
    }
}
