using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Music;
using Bloom.Ui;

namespace Bloom.App;

internal static partial class Captures
{
    /// `--qa-set readme-home` (run with --demo, 2×): the README's home as the data folder's settings.json lays it out —
    /// the blocks and their columns, the launcher's size, the AI accounts and their names all come from there, so the
    /// picture follows whatever home the recording seeds. A timer, a reminder for this week and one made-up song in
    /// another app fill the blocks that would be empty. Dark and light, then the dark one in 홈 편집.
    private static async Task ReadmeHomeSetScreens(Host host, Launcher w, string dir)
    {
        Shots.Scale = 2;
        Timers.Add("timer", Tr("집중", "Focus"), DateTime.Now.AddMinutes(25));
        Timers.Add("reminder", Tr("디자인 리뷰", "Design review"), DateTime.Today.AddDays(2).AddHours(15));
        for (int i = 0; i < 60 && !host.Apps.Ready; i++) await Task.Delay(100);
        double width = w.Width, height = w.Height;
        w.Width = Settings.Current.Width;
        w.Height = Settings.Current.Height;
        var song = new MediaNow("Spotify.exe", Tr("새벽 산책", "Morning Walk"), Tr("데모 테이프", "Demo Tape"), MediaStatus.Playing,
            TimeSpan.FromSeconds(74), TimeSpan.FromSeconds(212), true, true, true, false);
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            OtherApps.SetForQa(song);
            w.Go(Mode.Root, "");
            await Task.Delay(900);
            await Snap(w, dir, $"RH-{theme}-home");
        }
        Theme.Apply("dark");
        OtherApps.SetForQa(song);
        w.Go(Mode.Root, "");
        await Task.Delay(600);
        w.ToggleHomeEdit();
        await Task.Delay(500);
        await Snap(w, dir, "RH-dark-home-edit");
        w.ToggleHomeEdit();
        w.Width = width;
        w.Height = height;

        // the settings board: 일반 (the accent row with its presets), AI 계정 (the same accounts) and 데이터, dark
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        foreach (string page in new[] { "general", "accounts", "data" })
        {
            sw.Page(page);
            await Task.Delay(400);
            await sw.Dispatcher.InvokeAsync(() => sw.Capture(Path.Combine(dir, $"RH-dark-s-{page}.png")), DispatcherPriority.ApplicationIdle);
        }
        sw.Close();
    }
}
