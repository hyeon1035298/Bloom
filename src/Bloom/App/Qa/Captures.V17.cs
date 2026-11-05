using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Ui;

namespace Bloom.App;

/// v17: the quick AI's search line (one model chip, ■ 정지 + 세션 종료 while it answers, 📎), the home while it answers
/// in the background, Bloom Lens with 6 캡처, an app's 제거… (asked, never run), a shortcut pressed in (Ctrl+K › 단축키 지정, 설정's rows) — in the
/// language of the run (`--lang ko|en`). The conversation is a local example, never sent.
internal static partial class Captures
{
    private static async Task V17Screens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var t = new ChatThread { Id = "qa-quick", Title = Tr("로그 분석", "Log analysis"), Model = "sonnet" };
        t.Messages.Add(new ChatMessage { Role = "user", Text = Tr("어제 서버 로그에서 에러가 몰린 시간대 찾아줘", "Find when the errors piled up in yesterday's server log") });
        t.Messages.Add(new ChatMessage { Role = "assistant", Text = Tr("로그를 시간대별로 나눠 보는 중이에요…", "Splitting the log by the hour…"), Streaming = true });
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            string d = $"{l}-{theme}-";
            // answering: ■ 정지 and 세션 종료 side by side, the footer's 정지 · 홈
            host.Chats.SetLiveForQa(t, true);
            w.ShowChat(t);
            await Task.Delay(300);
            await Snap(w, dir, d + "q01-answering");
            // Esc: the home, the answer going on (the chip goes back to it)
            await Shot(w, dir, d + "q02-home-running", Mode.Root, "");
            // idle with something typed: one model chip, 세션 종료, 📎
            t.Messages[^1].Streaming = false;
            t.Messages[^1].Text = Tr("오전 3시~4시에 에러의 62%가 몰려 있어요.", "62% of the errors fall between 3 and 4 am.");
            w.ShowChat(t, Tr("그 시간대에 무슨 작업이 돌았는지도 봐줘", "Also check what jobs ran in that hour"));
            await Task.Delay(300);
            await Snap(w, dir, d + "q03-idle-typing");
            host.Chats.SetLiveForQa(t, false);
            t.Messages[^1].Streaming = true;
            t.Messages[^1].Text = Tr("로그를 시간대별로 나눠 보는 중이에요…", "Splitting the log by the hour…");
        }
        t.Messages[^1].Streaming = false;
        Theme.Apply("dark");
        string dk = $"{l}-dark-";

        // Bloom Lens: 6 캡처 in the menu
        var shot = Message(dir);
        host.SetLensForQa(new Ocr.ScreenRect(-32000, -32000, shot.w, shot.h), shot.px);
        await Shot(w, dir, dk + "l01-lens-menu", Mode.Lens, "", 800);

        // Ctrl+K › 단축키 지정: the keys pressed show as the combo
        w.Go(Mode.Root, Tr("창 배치", "window layout"));
        await Task.Delay(400);
        if (w.Selected is { } item && host.Engine.ItemKeyActions(item).FirstOrDefault() is { } set)
        {
            set.Run();
            await Task.Delay(200);
            await Shot(w, dir, dk + "k01-key-empty", Mode.Form, "", 300);
            await Shot(w, dir, dk + "k02-key-pressed", Mode.Form, "Ctrl+Alt+M", 300);
        }

        // an app's 제거…: in its menu (Ctrl+K / right-click), then asked once more; nothing is run
        bool Removable(Search.AppEntry a) => a.Source == "lnk" && host.Engine.FromApp(a).MoreActions?.Invoke().Any(x => x.Danger) == true;
        if (host.Apps.Apps.FirstOrDefault(Removable) is { } app)
        {
            w.Go(Mode.Root, app.Name);
            await Task.Delay(400);
            w.OpenActions();
            await Snap(w, dir, dk + "u01-app-menu");
            if (w.Selected?.MoreActions?.Invoke().FirstOrDefault(x => x.Danger) is { } remove)
            {
                remove.Run();
                await Snap(w, dir, dk + "u02-app-confirm");
            }
            w.CloseActionsForQa();
        }

        // 설정 › 단축키: shortcuts as pressed-in wells, one listening
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Page("shortcuts");
        await Task.Delay(400);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, dk + "s01-lens-keys.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (Find<KeyRecorder>(sw) is { } rec)
        {
            rec.Start();
            await Task.Delay(200);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, dk + "s02-lens-listening.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T hit) return hit;
            if (Find<T>(c) is { } deeper) return deeper;
        }
        return null;
    }
}
