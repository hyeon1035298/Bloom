using System;
using System.Collections.Generic;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

internal sealed partial class Host
{
    private DemoBackdrop? backdrop;

    /// The apps the recording mode and the README pictures search: the browsers and the tools Windows comes with.
    public static bool WindowsOwn(Search.AppEntry a) => a.Name is "Google Chrome" or "Microsoft Edge" or "메모장" or "Notepad" or "계산기" or "Calculator" or "그림판" or "Paint" or "캡처 도구" or "Snipping Tool" or "파일 탐색기" or "File Explorer";

    /// `--demo` (Core.Demo): the recording mode, set up once the services exist. The settings that would show this PC
    /// go off, a first start's favourites and a few sample clipboard entries come in (only while the data folder has
    /// none), the usage numbers are made up, and the apps are the browsers and the tools Windows comes with.
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
        static UsageAccount Plan(string name, string plan, double? fiveHour, double week)
        {
            var limits = new List<UsageLimit>();
            if (fiveHour is { } h) limits.Add(new UsageLimit(Tr("5시간", "5h"), h, DateTime.Now.AddHours(3).AddMinutes(12)));
            limits.Add(new UsageLimit(Tr("주간", "Weekly"), week, DateTime.Now.AddDays(4)));
            return new(name, plan, limits, DateTime.Now);
        }
        AiUsage.SetForQa(Plan("Claude", "Ultra", 32, 47), Plan("ChatGPT", "Pro 20x", null, 21)); // ChatGPT Pro has a weekly limit only
        if (Opt.CaptureDir is null) // QA pictures are taken off-screen: no backdrop over the user's screen
        {
            backdrop = new DemoBackdrop(Opt.Display);
            backdrop.Show();
        }
        Log.Write("demo: recording mode on");
    }
}
