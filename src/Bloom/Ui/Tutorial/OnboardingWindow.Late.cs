using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The tutorial's pages an update can bring back on their own (the add-ons, the task notices, the keys) and the last
/// page. The add-ons and the notices only fill Choices here: the last button applies them.
internal sealed partial class OnboardingWindow
{
    // ── the add-ons ─────────────────────────────────────────────────────────────────────────────
    private void AddonsPick(StackPanel page)
    {
        var c = Choices;
        page.Children.Add(TutorialParts.Header(Tr("애드온", "Add-ons"), Tr("Windows에 기능을 더합니다. 켠 것은 마칠 때 받습니다.", "They add features to Windows. What you turn on downloads when you finish."), NewTag));
        var grid = Under(new TileGrid(3, 9, 9), 18);
        foreach (string id in c.AddonsListed)
        {
            if (AddonHub.All.FirstOrDefault(a => a.Id == id) is not { } addon) continue;
            // the catalog's own names are as long as they are: two lines rather than cut
            var tile = new PictureTile(AddonArt.Tile(id, frozen: env.Still) ?? Plain(G.Plug), 84, AddonName(addon), null, TileMark.Switch, c.Addons.Contains(id), nameLines: 2);
            void Say()
            {
                (tile.About, tile.AboutAccent) = AddonAbout(id, addon.On, c.Addons.Contains(id), addon.Catalog?.About(Code));
            }
            tile.Toggled += v =>
            {
                // nothing is switched here: the last button asks first, then turns them on
                if (v) c.Addons.Add(id);
                else c.Addons.Remove(id);
                Say();
            };
            Say();
            grid.Add(tile);
        }
        page.Children.Add(grid);
        if (!UpdateOnly) page.Children.Add(Under(TutorialParts.Note(Tr("설정에서 언제든 끌 수 있습니다.", "You can turn them off in Settings at any time.")), 14));
    }

    private static string AddonName(Addon a) =>
        a.Id == AddonProtocol.Id ? Tr("작업표시줄", "Taskbar") : a.Catalog?.Title(Code) ?? AddonConsent.Describe(a).Name;

    /// An add-on's icon as Settings' add-on list shows it: the taskbar's screen, the plug for every other.
    internal static char AddonGlyph(Addon a) => a.Id == AddonProtocol.Id ? G.Screen : G.Plug;

    /// The line under an add-on's name, and whether it is said in the accent: that it is on already while it stays
    /// picked, else what it does (the tutorial's short wording for the add-ons it knows, the catalog's for the rest).
    internal static (string Text, bool Accent) AddonAbout(string id, bool onNow, bool picked, string? catalog)
    {
        if (onNow && picked) return (Tr("켜져 있음", "On now"), true);
        return (id switch
        {
            AddonProtocol.Id => Tr("가운데 알약 모양", "A pill in the middle"),
            TutorialChoices.RunCard => Tr("Claude Code 진행", "Claude Code progress"),
            "memory-clean" => Tr("시계 옆에서 비우기", "Free memory by the clock"),
            "brightness" => Tr("모니터마다 밝기", "Brightness per monitor"),
            "keep-awake" => Tr("PC가 잠들지 않게", "Keeps the PC awake"),
            "alt-tab-per-monitor" => Tr("마우스 있는 화면만", "Only the screen with the mouse"),
            _ => catalog ?? "",
        }, false);
    }

    /// A plain icon in the middle of a tile, where there is no picture for it.
    private static FrameworkElement Plain(char glyph)
    {
        var icon = Icons.Tile(glyph, false, 34);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid();
        g.Children.Add(icon);
        return g;
    }

    // ── the task notices ────────────────────────────────────────────────────────────────────────
    private void Notify(StackPanel page)
    {
        var c = Choices;
        c.Settle(env);
        // the click's two places need the taskbar add-on and an AI its panel can open
        bool showClick = c.Taskbar && (env.Claude || env.Codex);
        var body = new StackPanel();
        var master = new Switch(c.Notices);
        void Dim()
        {
            // with the notices off nothing under the header applies, and nothing there takes the keyboard
            body.Opacity = c.Notices ? 1 : 0.45;
            body.IsHitTestVisible = body.IsEnabled = c.Notices;
        }
        TutorialParts.Focusable(master, () =>
        {
            c.Notices = master.On = !master.On;
            Dim();
        }, new CornerRadius(13));
        page.Children.Add(TutorialParts.Header(Tr("작업 알림", "Task Notifications"),
            Tr("Claude Code와 Codex가 일을 마치거나 승인을 기다리면 카드로 알립니다.", "A card tells you when Claude Code or Codex finishes or waits for approval."), NewTag, master));
        page.Children.Add(body);
        Dim();

        double rowGap = 12; // the first row under the picture; the ones after it sit closer
        void Row(TutorialScene picture, string title, string sub, bool on, Action<bool> set)
        {
            body.Children.Add(Under(ThumbRow.Switched(TutorialArt.For(picture, 64, 38), title, sub, on, set), rowGap));
            rowGap = 6;
        }
        void Approve() => Row(TutorialScene.ThumbApprove, Tr("카드에서 승인하고 답하기", "Approve and answer on the card"),
            Tr("켜면 Claude Code 설정 파일에 Bloom을 넣습니다. 끄면 뺍니다", "Adds Bloom to Claude Code's settings file. Off removes it"), c.Approve, c.SetApprove);
        void Windows() => Row(TutorialScene.ThumbWindows, Tr("Windows 알림도 카드로", "Windows notifications as cards"),
            Tr("다른 앱 알림도 같은 카드로 받습니다", "Other apps' notifications use the same card"), c.Windows, v => c.Windows = v);
        void T3() => Row(TutorialScene.ThumbT3, Tr("T3 Code 연결", "T3 Code Connection"),
            Tr("카드와 AI 채팅에서 T3 Code 작업에 답하고 멈춥니다", "Answer and stop T3 Code tasks from cards and AI chat"), c.T3Link, v => c.T3Link = v);

        if (showClick)
        {
            var label = TutorialParts.Label(Tr("카드를 누르면", "When card is clicked"));
            body.Children.Add(Under(label, 14));
            double scene = UpdateOnly ? 64 : 80; // the tag over the title takes the room
            var grid = new TileGrid(2, 10, 0);
            grid.Add(new PictureTile(TutorialArt.For(TutorialScene.NotifyToApp, 271, scene), scene, Tr("앱으로 이동", "Go to app"),
                env.T3 ? Tr("작업하던 창이나 T3 Code로", "To your window or T3 Code") : Tr("작업하던 창으로", "To the window you were in"), TileMark.Pick, c.Click != "taskbar"));
            grid.Add(new PictureTile(TutorialArt.For(TutorialScene.NotifyToTaskbar, 271, scene), scene, Tr("작업표시줄 AI 패널", "Taskbar AI panel"),
                Tr("작업표시줄에서 대화를 엽니다", "Opens the chat on the taskbar"), TileMark.Pick, c.Click == "taskbar"));
            grid.PickOne(i => c.Click = i == 1 ? "taskbar" : "app");
            body.Children.Add(grid);
            rowGap = 10;
            if (env.Claude) Approve();
            Windows();
            if (env.T3) T3();
            return;
        }
        // no choice of where a click goes: the cards themselves are the picture
        var cards = new Grid { Width = PageWidth, Height = 156 };
        var art = TutorialArt.For(TutorialScene.NotifyCards, PageWidth, 156);
        art.Clip = SmoothBorder.Shape(new Rect(0, 0, PageWidth, 156), new CornerRadius(12));
        cards.Children.Add(art);
        cards.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(12), BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), IsHitTestVisible = false });
        body.Children.Add(Under(cards, 18));
        if (env.Claude) Approve();
        if (c.Taskbar) Windows(); // Windows' notices are read through the taskbar add-on
        if (env.T3) T3();
        body.Children.Add(Under(TutorialParts.Note(env.T3
            ? Tr("카드를 누르면 작업하던 창이나 T3 Code로 갑니다.", "Clicking a card takes you to your window or T3 Code.")
            : Tr("카드를 누르면 작업하던 창으로 갑니다.", "Clicking a card takes you to the window you were in.")), 14));
    }

    // ── the keys ────────────────────────────────────────────────────────────────────────────────
    private void KeysToKnow(StackPanel page)
    {
        var s = Settings.Current;
        page.Children.Add(TutorialParts.Header(Tr("알아 두면 편한 키", "Keys worth knowing"), Tr("이것만 알면 거의 다 됩니다.", "These cover almost everything."), NewTag));
        var grid = Under(new TileGrid(3, 9, 9), 18);
        void Tile(TutorialScene picture, string name, TutorialArtData? data = null) =>
            grid.Add(new PictureTile(TutorialArt.For(picture, 178, 112, data), 112, name, nameLines: 2));
        // the caps are the user's own combinations; one that is switched off has no tile
        void Combo(TutorialScene picture, string name, string combo)
        {
            if (combo != "off") Tile(picture, name, new TutorialArtData(Names: Hotkeys.Parts(combo)));
        }
        Tile(TutorialScene.KeyOpen, Tr("열고 적기", "Open and type"), new TutorialArtData(WinKey: s.WinKey, AltSpace: s.AltSpace));
        if (env.AnyProvider) Tile(TutorialScene.KeyTab, Tr("적은 말을 AI에게 묻기", "Ask AI what you typed"));
        Tile(TutorialScene.KeyMore, Tr("더보기", "More"));
        Combo(TutorialScene.KeyLens, "Bloom Lens", s.LensHotkey);
        Combo(TutorialScene.KeyClipboard, Tr("클립보드와 상용구", "Clipboard and snippets"), s.ClipHotkey);
        Tile(TutorialScene.KeyHelp, Tr("적을 수 있는 말", "What you can type"));
        page.Children.Add(grid);
    }

    // ── the last page ───────────────────────────────────────────────────────────────────────────
    private void Done(StackPanel page)
    {
        var s = Settings.Current;
        var c = Choices;
        var mark = Launcher.MarkImage(52);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(mark);
        page.Children.Add(Under(TutorialParts.Lines(Theme.T(Tr("준비됐습니다", "You are all set"), 24, Theme.P.Text, Theme.Semi), 30), 16));
        string sub = s.WinKey ? Tr("Win 키를 눌러 바로 써 보세요.", "Press the Win key and try it.")
            : s.AltSpace ? Tr("Alt + Space를 눌러 바로 써 보세요.", "Press Alt + Space and try it.")
            : Tr("트레이 아이콘을 눌러 열 수 있습니다.", "Open it from the tray icon.");
        page.Children.Add(Under(new TutorialParts.EvenLines(Theme.T(Hangul.KeepAll(sub), 13.5, Theme.P.TextDim), 20, 2), 6));

        // what was chosen, one picture each
        var grid = Under(new TileGrid(2, 10, 10), 18);
        void Tile(FrameworkElement scene, string name) => grid.Add(new PictureTile(scene, 64, name, nameLines: 2));
        if (s.WinKey || s.AltSpace)
            Tile(TutorialArt.For(TutorialScene.DoneOpen, 271, 64, new TutorialArtData(WinKey: s.WinKey, AltSpace: s.AltSpace)),
                s.WinKey && s.AltSpace ? Tr("Win 키, Alt + Space로 열기", "Opens with the Win key or Alt + Space")
                : s.WinKey ? Tr("Win 키로 열기", "Opens with the Win key") : Tr("Alt + Space로 열기", "Opens with Alt + Space"));
        var home = ids.Contains(TutorialSteps.Home) ? HomeRow() : [];
        int n = home.Count;
        if (n > 0) Tile(HomeScene(home), n == 1 ? Tr("홈 기능 1개", "1 feature on Home") : Tr($"홈 기능 {n}개", $"{n} features on Home"));
        var plan = PlanForQa();
        if (plan.TurnOn.Count > 0)
        {
            var addons = plan.TurnOn.Select(id => AddonHub.All.FirstOrDefault(a => a.Id == id)).OfType<Addon>().ToList();
            var data = new TutorialArtData(Names: [.. addons.Select(AddonName)], Glyphs: [.. addons.Select(AddonGlyph)]);
            int k = plan.TurnOn.Count;
            // the finish asks only about an add-on not confirmed before (or the AI panel): ones confirmed earlier just go on
            string line = plan.Consent
                ? k == 1 ? Tr("애드온 1개는 확인하고 받습니다", "1 add-on downloads after you confirm") : Tr($"애드온 {k}개는 확인하고 받습니다", $"{k} add-ons download after you confirm")
                : k == 1 ? Tr("애드온 1개를 켭니다", "Turns on 1 add-on") : Tr($"애드온 {k}개를 켭니다", $"Turns on {k} add-ons");
            Tile(TutorialArt.For(TutorialScene.DoneAddons, 271, 64, data), line);
        }
        if (c.NotifyShown)
            Tile(TutorialArt.For(TutorialScene.DoneNotify, 271, 64, new TutorialArtData(On: c.Notices)),
                c.Notices ? Tr("작업 알림 켬", "Task notifications on") : Tr("작업 알림 끔", "Task notifications off"));
        page.Children.Add(grid);

        var link = TutorialParts.Link(Tr("Bloom 설정 열기", "Open Bloom Settings"), () => host.OpenSettings("general"));
        var note = TutorialParts.Note(Tr("이 안내는 Bloom에서 ‘둘러보기’를 적으면 다시 볼 수 있습니다.", "Type \"tour\" in Bloom to see this guide again."));
        page.Children.Add(Under(new Beside(link, note), 16));
    }

    /// The home's row as the last page shows it: every feature's icon up to eight, else seven and how many more.
    private static FrameworkElement HomeScene(List<SysEntry> on)
    {
        const int most = 8;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        int shown = on.Count > most ? most - 1 : on.Count;
        for (int i = 0; i < shown; i++) row.Children.Add(HomeIcon(on[i].Glyph, first: i == 0, gap: i == 0 ? 0 : 6));
        if (shown < on.Count)
            row.Children.Add(new SmoothBorder
            {
                Height = 26,
                MinWidth = 26,
                CornerRadius = new CornerRadius(7),
                Background = Theme.P.Well,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Child = Theme.Label("+" + (on.Count - shown), 11.5, Theme.P.TextDim, Theme.Semi, inBorder: true),
            });
        var scene = new Grid();
        scene.Children.Add(row);
        return scene;
    }

    /// A link with a note beside it on one line; a note too long for the rest of the line goes under the link, whole.
    private sealed class Beside : Panel
    {
        private const double Gap = 16, Under = 8;
        private readonly FrameworkElement lead, rest;
        private bool stacked;

        public Beside(FrameworkElement lead, FrameworkElement rest)
        {
            (this.lead, this.rest) = (lead, rest);
            Children.Add(lead);
            Children.Add(rest);
        }

        protected override Size MeasureOverride(Size available)
        {
            var open = new Size(double.PositiveInfinity, double.PositiveInfinity);
            lead.Measure(open);
            rest.Measure(open);
            double line = lead.DesiredSize.Width + Gap + rest.DesiredSize.Width;
            stacked = !double.IsInfinity(available.Width) && line > available.Width + 0.5;
            if (!stacked) return new Size(line, Math.Max(lead.DesiredSize.Height, rest.DesiredSize.Height));
            rest.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(available.Width, lead.DesiredSize.Height + Under + rest.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double lh = lead.DesiredSize.Height, rh = rest.DesiredSize.Height;
            if (stacked)
            {
                lead.Arrange(new Rect(0, 0, lead.DesiredSize.Width, lh));
                rest.Arrange(new Rect(0, lh + Under, final.Width, rh));
                return final;
            }
            double h = Math.Max(lh, rh);
            lead.Arrange(new Rect(0, Math.Round((h - lh) / 2), lead.DesiredSize.Width, lh));
            rest.Arrange(new Rect(lead.DesiredSize.Width + Gap, Math.Round((h - rh) / 2), rest.DesiredSize.Width, rh));
            return final;
        }
    }
}
