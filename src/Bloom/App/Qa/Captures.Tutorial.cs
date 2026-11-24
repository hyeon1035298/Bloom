using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set tutorial` (with `--demo --lang <code>`): every page of the tutorial in both themes under made-up PCs, the
/// update-only flow and the add-on question, then {code}-tut-report.txt (pages that had to scroll, scene text that was
/// trimmed, checks that failed) and {code}-tut-missed.txt (texts the language's table lacked).
internal static partial class Captures
{
    private static async Task TutorialScreens(Host host, Launcher w, string dir)
    {
        string l = Code;
        var report = new List<string>();
        var s = Settings.Current;
        // --demo turns the notices off and seeds a home: the pages are drawn as a first run sees them
        bool notices = s.Notices;
        string theme0 = s.Theme;
        var favourites = Favorites.Current.Items.ToList();
        s.Notices = true;
        TutorialArt.TrackTrims = true;
        TutorialArt.Trims.Clear();

        // made-up PCs: the browsers are named here and borrow one program's icon, so no picture shows this PC's list
        string sample = Browser.Web.Installed is [var first, ..] ? first.Exe : Environment.ProcessPath ?? "";
        var everything = new TutorialEnv
        {
            Claude = true,
            Codex = true,
            Omp = true,
            T3 = true,
            Everything = true,
            AdminOffered = true,
            AddonsOffered = true,
            DefaultBrowserExe = sample,
            Still = true,
        };
        TutorialEnv B(int k) => everything with
        {
            Browsers = [.. Enumerable.Range(1, k).Select(i => new Browser.InstalledBrowser($"b{i}", $"Browser {i}", sample))],
        };
        var full = B(3);
        var one = new TutorialEnv { Claude = true, Everything = true, AddonsOffered = true, Browsers = full.Browsers, DefaultBrowserExe = sample, Still = true };
        var none = new TutorialEnv { AddonsOffered = true, Browsers = full.Browsers, DefaultBrowserExe = sample, Still = true };
        var noAdd = full with { AddonsOffered = false };

        string theme = "light";
        void Fail(string what) => report.Add($"{l}-{theme}\tFAIL {what}");

        // each window starts from the same first run: an empty home and nothing seen (its Close writes both)
        OnboardingWindow Open(TutorialEnv env, IReadOnlyList<string>? only = null)
        {
            foreach (var e in Favorites.Current.Items.ToList()) Favorites.Remove(e.Id);
            Settings.Current.Onboarded = false;
            Settings.Current.TutorialSeen = null;
            var ob = new OnboardingWindow(host, only, env) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            ob.Show();
            return ob;
        }

        void Trims(string file)
        {
            foreach (string t in TutorialArt.Trims) report.Add($"{file}\tTRIM-ART {t}");
            TutorialArt.Trims.Clear();
        }

        async Task Pic(OnboardingWindow ob, string state)
        {
            string file = $"{l}-{theme}-tut-{state}.png";
            await Task.Delay(250);
            await ob.Dispatcher.InvokeAsync(() => ob.Capture(Path.Combine(dir, file)), DispatcherPriority.ApplicationIdle);
            if (ob.ScrolledForQa) report.Add($"{file}\tSCROLL {ob.StepId}");
            Trims(file);
        }

        // one window per picture: what a picture changes (choices, the home) never reaches the next one
        // `setup` changes the choices before the page is built again; `check` reads the page once it is drawn
        async Task Page(string state, TutorialEnv env, string id, Action<OnboardingWindow>? setup = null, Action<OnboardingWindow>? check = null)
        {
            var ob = Open(env);
            try
            {
                if (!ob.Go(id))
                {
                    Fail($"{state}: no {id} page");
                    return;
                }
                if (setup is not null)
                {
                    setup(ob);
                    ob.Rebuild();
                }
                await Pic(ob, state);
                check?.Invoke(ob);
            }
            finally
            {
                ob.Close();
            }
        }

        void PickTwo(OnboardingWindow ob)
        {
            ob.Choices.Addons.Add(AddonProtocol.Id);
            ob.Choices.Addons.Add(TutorialChoices.RunCard);
        }

        try
        {
            foreach (string t in new[] { "light", "dark" })
            {
                theme = t;
                Settings.Current.Theme = theme;
                Theme.Apply(theme);

                await Page("01-start", full, TutorialSteps.Start);
                await Page("02-open", full, TutorialSteps.Open);
                await Page("02-open-noadmin", one, TutorialSteps.Open);
                await Page("03-looks", full, TutorialSteps.Looks);
                await Page("04-browser", full, TutorialSteps.Browser);
                foreach (int k in new[] { 0, 1, 5, 9 }) await Page($"04-browser-{k + 1}", B(k), TutorialSteps.Browser);
                await Page("05-home", full, TutorialSteps.Home);
                await Page("05-home-none", none, TutorialSteps.Home);
                await Page("06-ai-many", full, TutorialSteps.Ai);
                await Page("06-ai-one", one, TutorialSteps.Ai);
                await Page("06-ai-none", none, TutorialSteps.Ai);
                await Page("07-addons", full, TutorialSteps.Addons);
                await Page("07-addons-picked", full, TutorialSteps.Addons, PickTwo);
                await Page("07-addons-noclaude", none, TutorialSteps.Addons);
                await Page("08-notify", full, TutorialSteps.Notify, check: ob =>
                {
                    if (!ob.Choices.Notices) Fail("08-notify: the notices start off");
                });
                await Page("08-notify-taskbar", full, TutorialSteps.Notify, PickTwo);
                await Page("08-notify-off", full, TutorialSteps.Notify, ob => ob.Choices.Notices = false);
                await Page("08-notify-one", one, TutorialSteps.Notify);
                await Page("09-keys", full, TutorialSteps.Keys);
                await Page("09-keys-noai", none, TutorialSteps.Keys);
                bool winKey = Settings.Current.WinKey;
                Settings.Current.WinKey = false;
                try
                {
                    await Page("09-keys-winoff", full, TutorialSteps.Keys);
                }
                finally
                {
                    Settings.Current.WinKey = winKey;
                }
                await Page("10-done", full, TutorialSteps.Done, PickTwo, ob =>
                {
                    string on = Tr("작업 알림 켬", "Task notifications on");
                    if (!Texts(ob).Contains(on)) Fail($"10-done: no \"{on}\" tile");
                });
                await Page("10-done-min", none, TutorialSteps.Done);

                // the pages each environment leaves out
                var check = Open(none);
                if (check.Go(TutorialSteps.Notify)) Fail("NONE has a notify page");
                check.Close();
                check = Open(noAdd);
                if (check.Go(TutorialSteps.Addons)) Fail("NOADD has an addons page");
                check.Close();

                // the update-only flow: what an updated Bloom shows
                var up = Open(full, [TutorialSteps.Addons, TutorialSteps.Notify, TutorialSteps.Keys]);
                try
                {
                    if (!up.UpdateOnly) Fail("11-update: not the update-only flow");
                    foreach (var (state, id) in new[] { ("11-update-addons", TutorialSteps.Addons), ("11-update-notify", TutorialSteps.Notify), ("11-update-keys", TutorialSteps.Keys) })
                    {
                        if (up.Go(id)) await Pic(up, state);
                        else Fail($"{state}: no {id} page");
                    }
                }
                finally
                {
                    up.Close();
                }
                up = Open(full, [TutorialSteps.Keys]);
                try
                {
                    if (up.StepCount != 1) Fail($"11-update-one: {up.StepCount} pages");
                    await Pic(up, "11-update-one");
                }
                finally
                {
                    up.Close();
                }

                // the add-on question the finish button asks
                var picked = TutorialChoices.AddonOrder.Select(id => AddonHub.All.FirstOrDefault(a => a.Id == id)).OfType<Addon>().ToList();
                await Consent("12-consent-2", [.. picked.Where(a => a.Id is AddonProtocol.Id or TutorialChoices.RunCard)], true);
                await Consent("12-consent-6", picked, false);
            }
        }
        finally
        {
            TutorialArt.TrackTrims = false;
            s = Settings.Current;
            s.Notices = notices;
            s.Theme = theme0;
            foreach (var e in Favorites.Current.Items.ToList()) Favorites.Remove(e.Id);
            foreach (var e in favourites) Favorites.Add(e);
            s.MarkTutorialSeen();
            Settings.Save();
        }
        File.WriteAllLines(Path.Combine(dir, $"{l}-tut-report.txt"), report);
        File.WriteAllLines(Path.Combine(dir, $"{l}-tut-missed.txt"), Misses().Order(StringComparer.Ordinal));

        async Task Consent(string state, IReadOnlyList<Addon> addons, bool taskbarChat)
        {
            string file = $"{l}-{theme}-tut-{state}.png";
            var cw = AddonConsent.ForQa(addons, taskbarChat);
            cw.WindowStartupLocation = WindowStartupLocation.Manual;
            cw.Left = cw.Top = -32000;
            cw.ShowActivated = false;
            cw.Show();
            try
            {
                await Task.Delay(300);
                await cw.Dispatcher.InvokeAsync(() => Shots.Save((FrameworkElement)cw.Content, Path.Combine(dir, file)), DispatcherPriority.ApplicationIdle);
                Trims(file);
            }
            finally
            {
                cw.Close();
            }
        }
    }

    /// The texts a window shows, for the checks that read a page's words.
    private static HashSet<string> Texts(DependencyObject root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<DependencyObject>([root]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is TextBlock { Text: { Length: > 0 } text }) found.Add(text);
            int n = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
        return found;
    }
}
