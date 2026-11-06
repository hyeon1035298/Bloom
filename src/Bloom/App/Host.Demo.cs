using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal sealed partial class Host
{
    private DemoBackdrop? backdrop;

    /// The recording mode's Claude cards, one per Claude account in turn: percent used of 5 hours, the week and Fable's week.
    private static readonly (double Hour, double Week, double Fable)[] DemoClaude = { (32, 47, 18), (64, 71, 40), (8, 22, 5) };

    /// The apps the recording mode and the README pictures search: the browsers and the tools Windows comes with.
    public static bool WindowsOwn(Search.AppEntry a) => a.Name is "Google Chrome" or "Microsoft Edge" or "Microsoft Store" or "메모장" or "Notepad" or "계산기" or "Calculator" or "그림판" or "Paint" or "캡처 도구" or "Snipping Tool" or "파일 탐색기" or "File Explorer";

    /// `--demo` (Core.Demo): the recording mode, set up once the services exist. The settings that would show this PC
    /// go off, a first start's favourites and a few sample clipboard entries come in (only while the data folder has
    /// none), the usage and token numbers are made up, and the apps are the browsers and the tools Windows comes with.
    private void BeginDemo()
    {
        var s = Settings.Current;
        s.Onboarded = true;
        s.FilesInRoot = false; // Everything and the recent files are off anyway; the switch shows it
        s.WindowsInRoot = false;
        s.Notices = false; // the limit and task cards watch the real Claude Code and Codex
        Settings.Save();
        Apps.KeepForQa(WindowsOwn);
        if (Favorites.Current.Items.Count == 0)
            foreach (string id in new[] { "cmd:quicklinks", "cmd:files", "cmd:clipboard", "cmd:timer", "cmd:calendar", "cmd:lens", "cmd:space" }) // a first start's own row
                if (Search.SystemItems.Find(id) is { } e) Favorites.Add(new Favorites.Entry { Id = e.Id, Kind = e.Kind, Title = e.Title, Subtitle = e.Subtitle, Target = e.Target });
        if (Clipboard.Items.Count == 0)
        {
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Clipboard.Add(new Clip.ClipEntry { Id = "clip:demo1", Kind = "text", Text = Tr("회의 자료: 다음 주 화요일 오후 2시, 3층 회의실", "Meeting notes: next Tuesday at 2 PM, room 3"), Source = Tr("메모장", "Notepad"), Copied = now - (3600_000L * 26) });
            Clipboard.Add(new Clip.ClipEntry { Id = "clip:demo2", Kind = "text", Text = "https://github.com", Source = "Google Chrome", Copied = now - (3600_000L * 3) });
            Clipboard.Add(new Clip.ClipEntry { Id = "clip:demo3", Kind = "text", Text = Tr("오늘 저녁 7시에 만나요. 늦으면 연락 주세요.", "See you at 7 tonight. Text me if you're running late."), Source = Tr("메모장", "Notepad"), Copied = now - (60_000L * 12) });
        }
        // one made-up card per account the data folder's settings list (the two defaults on a fresh one), under the
        // account's own id, so each wears its company's logo and its alias as a signed-in PC's cards do
        static UsageAccount Plan(AiAccount a, int nth)
        {
            if (a.Kind == "chatgpt") // ChatGPT Pro has a weekly limit only
                return new("ChatGPT", "Pro 100", new() { new(Tr("주간", "Weekly"), 21 + (nth * 17), DateTime.Now.AddDays(4), UsageLimit.WeekMins) }, DateTime.Now) { Id = a.Id };
            var (hour, week, fable) = DemoClaude[nth % DemoClaude.Length];
            string model = "Fable"; // a hole, as ClaudeUsage writes it, so the tables' "{0} weekly" translates it
            return new("Claude", "Ultra", new()
            {
                new(Tr("5시간", "5h"), hour, DateTime.Now.AddHours(3).AddMinutes(12 + (nth * 41)), UsageLimit.SessionMins),
                new(Tr("주간", "Weekly"), week, DateTime.Now.AddDays(4 - nth), UsageLimit.WeekMins),
                new(Tr($"{model} 주간", $"{model} weekly"), fable, DateTime.Now.AddDays(4 - nth), UsageLimit.WeekMins, model),
            }, DateTime.Now)
            { Id = a.Id };
        }
        var shown = AiAccounts.All.Where(a => !a.Hidden).ToList();
        AiUsage.SetForQa(shown.Select(a => Plan(a, shown.Where(o => o.Kind == a.Kind).ToList().IndexOf(a))).ToArray());
        TokenStats.SetForQa(TokenDemo.View);
        if (Opt.CaptureDir is null) // QA pictures are taken off-screen: no backdrop over the user's screen
        {
            backdrop = new DemoBackdrop(Opt.Display);
            backdrop.Show();
        }
        Log.Write("demo: recording mode on");
    }
}
