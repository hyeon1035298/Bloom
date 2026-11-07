using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Clip;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set snippets`: the clipboard view's 기록 | 상용구 tabs, the history's 상용구로 저장, a snippet's actions and
/// form, the 상용구 shortcut row; what the saves did goes to snippets.txt. Nothing is pasted.
internal static partial class Captures
{
    private static async Task SnippetScreens(Host host, Launcher w, string dir)
    {
        var log = new StringBuilder();
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        string address = Tr("서울시 중구 세종대로 110\n3층 디자인팀", "110 Main Street\n3rd floor, design team");
        host.Clipboard.Add(new ClipEntry { Id = "clip:qa-snip-url", Kind = "text", Text = "https://example.com/meeting", Copied = now - 5 * 60_000, Source = "Chrome" });
        host.Clipboard.Add(new ClipEntry { Id = "clip:qa-snip-addr", Kind = "text", Text = address, Copied = now - 60_000, Source = Tr("메모장", "Notepad") });
        foreach (var c in new[]
        {
            UserCommands.New("snippet", Tr("메일 인사", "Mail greeting"), Tr("안녕하세요, {date} 회의 자료 보내드립니다.\\n감사합니다.", "Hello, here are the notes for {date}.\\nThanks."), "hi"),
            UserCommands.New("snippet", Tr("계좌 안내", "Payment details"), Tr("입금 확인 후 바로 발송됩니다.", "It ships as soon as the payment arrives."), null),
            UserCommands.New("script", Tr("오늘 날짜", "Today's date"), "Get-Date", "td"), // not a snippet: stays out of the tab
        }) UserCommands.Upsert(c);
        int before = Engine.SnippetCount;

        // 기록 (the default tab) and its Ctrl+K on the copied address
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, $"{theme}-s1-history", Mode.Clipboard, "", 700);
        }
        Theme.Apply("dark");
        w.Go(Mode.Clipboard, "");
        await Task.Delay(300);
        log.AppendLine($"history first row: {w.Selected?.Title}");
        w.OpenActions();
        await Snap(w, dir, "dark-s2-history-actions");
        w.CloseActionsForQa();
        var save = w.Selected?.MoreActions?.Invoke().FirstOrDefault(a => a.Title == Tr("상용구로 저장", "Save as a snippet"));
        log.AppendLine($"save action: {(save is null ? "missing" : "there")}");
        save?.Run();
        save?.Run(); // the same text again: not saved twice
        log.AppendLine($"snippets {before} → {Engine.SnippetCount} (one added, the second save refused)");
        var saved = UserCommands.Current.Items.LastOrDefault(c => c.Kind == "snippet");
        log.AppendLine($"saved name ‘{saved?.Name}’, lines kept: {UserCommands.SnippetText(saved?.Body ?? "") == address.Replace("\r\n", "\n")}");
        await Snap(w, dir, "dark-s3-saved-toast");

        // 상용구: the hotkey's way in (it picks the tab before the view opens), then Ctrl+Tab's
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            w.Go(Mode.Root, "");
            w.PickClipTab(Launcher.ClipSnippetsTab);
            await Shot(w, dir, $"{theme}-s4-snippets", Mode.Clipboard, "", 700);
        }
        Theme.Apply("dark");
        log.AppendLine($"tab after the pick: {w.ClipTab}");
        w.Go(Mode.Root, "");
        w.Go(Mode.Clipboard, "");
        log.AppendLine($"tab when entered from home: {w.ClipTab}");
        w.SetClipTab(Launcher.ClipSnippetsTab);
        await Task.Delay(300);
        w.MoveForQa(1); // the second snippet: its preview
        await Snap(w, dir, "dark-s5-snippet-preview");
        w.OpenActions();
        await Snap(w, dir, "dark-s6-snippet-actions");
        w.CloseActionsForQa();

        // the last snippet dragged above the first: its line on the way, the new order after the drop
        string Order() => string.Join(" · ", UserCommands.Current.Items.Where(c => c.Kind == "snippet").Select(c => c.Name));
        log.AppendLine($"order before the drag: {Order()}");
        w.DragRowForQa(2, -100, false);
        await Snap(w, dir, "dark-s6b-snippet-drag");
        w.DragRowForQa(2, -100, true);
        await Task.Delay(300);
        log.AppendLine($"order after the drag: {Order()} · selected {w.Selected?.Title}");
        await Snap(w, dir, "dark-s6c-snippet-dropped");
        await Shot(w, dir, "dark-s7-snippets-search", Mode.Clipboard, Tr("입금", "payment"), 500);
        log.AppendLine($"search keeps the tab: {w.ClipTab}");
        await Shot(w, dir, "dark-s8-snippets-new", Mode.Clipboard, Tr("오늘은 휴무입니다", "Closed today"), 500);
        log.AppendLine($"new row: {w.Selected?.Title}");

        // the new-snippet form from the tab: text → name → keyword, then back on the tab
        w.Selected?.Run?.Invoke();
        await Snap(w, dir, "dark-s9-form-text");
        w.FormNextForQa();
        await Snap(w, dir, "dark-s10-form-name");
        w.FormNextForQa();
        w.FormNextForQa();
        await Task.Delay(300);
        log.AppendLine($"after the form: {w.Mode} · {w.ClipTab} · snippets {Engine.SnippetCount}");
        await Snap(w, dir, "dark-s11-after-form");

        // kept: written to usercommands.json and read back
        UserCommands.Flush();
        UserCommands.Load();
        log.AppendLine($"read back: {Engine.SnippetCount} snippets, file {(File.Exists(Paths.In("usercommands.json")) ? "there" : "missing")}");

        var sw = new SettingsWindow(host) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        sw.Filter(Tr("상용구", "Snippets"));
        await Task.Delay(350);
        await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, "dark-s12-shortcut.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        sw.Close();
        await File.WriteAllTextAsync(Path.Combine(dir, "snippets.txt"), log.ToString());
    }
}
