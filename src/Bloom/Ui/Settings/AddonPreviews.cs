using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Addons;
using Bloom.App;

namespace Bloom.Ui;

/// What an add-on's settings come to on screen, drawn from its values at the top of their card (설정 > 애드온): the
/// taskbar on a desktop, the auto-hide island, the brightness widget, dimming while away, how the coffee icon is used.
/// Still pictures, made again when the page is (a value changed); nothing moves and nothing stays when the page closes.
internal static class AddonPreviews
{
    private static readonly Color WallTop = Color.FromRgb(0xCD, 0xD7, 0xE6), WallBottom = Color.FromRgb(0x9F, 0xB0, 0xC7);

    /// A preview's frame inside a card: a faint inset panel; `indent` sets it under the switch it belongs to.
    public static SmoothBorder Stage(FrameworkElement content, bool indent = false) => new()
    {
        CornerRadius = new CornerRadius(10),
        Background = Theme.P.Card,
        Padding = new Thickness(14, 12, 14, 12),
        Margin = new Thickness(indent ? 20 : 0, 0, 0, 0),
        Child = content,
    };

    /// A small faint caption ("미리 보기"), its ink centred on its line.
    private static TextBlock Caption(string text, Brush? brush = null)
    {
        var t = Theme.T(text, 12, brush ?? Theme.P.TextFaint);
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    /// A drawing made on a canvas of its own size, shrunk (never grown) to the width the card gives it.
    private static Viewbox Fit(Pic p) => new() { Child = p.Done(), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };

    // ── 작업표시줄 ──────────────────────────────────────────────────────────────────────────────
    /// The taskbar as its values draw it: its size, background, start button, edge and alignment, the tray icons on,
    /// now playing, AI usage and the clock, on a plain desktop. The pill follows Bloom's light or dark like the add-on.
    public static FrameworkElement Taskbar(AddonValues v)
    {
        bool dark = Theme.P.Dark;
        int height = 48, icon = 24, spacing = 20;
        if (v.Bool("size.enabled"))
        {
            height = Math.Clamp(v.Int("size.taskbarHeight"), 24, 96);
            icon = Math.Clamp(v.Int("size.iconSize"), 12, 64);
            spacing = Math.Clamp(v.Int("size.iconSpacing"), 0, 40);
        }
        const double k = 0.66; // the desktop is drawn at two thirds
        double h = Math.Round(height * k), s = Math.Round(icon * k), gap = Math.Round(spacing * k * 0.6);
        var ink = new SolidColorBrush(dark ? Color.FromRgb(0xEC, 0xED, 0xEE) : Color.FromRgb(0x27, 0x27, 0x2A));
        var tiles = new[] { Color.FromRgb(0x7D, 0x8A, 0xA0), Color.FromRgb(0x8B, 0x9A, 0xB0), Color.FromRgb(0x6E, 0x7C, 0x93), Color.FromRgb(0x7D, 0x8A, 0xA0) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(FrameworkElement e, double left)
        {
            e.Margin = new Thickness(row.Children.Count == 0 ? 0 : left, 0, 0, 0);
            e.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(e);
        }
        switch (v.Text("start.mode"))
        {
            case "windows":
                Add(new Viewbox { Width = s, Height = s, Child = new Pic(true, 12, 12).Windows(6, 6, 5, 1).Done() }, 0);
                break;
            case "bloom":
                Add(new Viewbox { Width = s, Height = s, Child = new Pic(true, 12, 12).Flower(6, 6, 1.9).Done() }, 0);
                break;
        }
        foreach (var c in tiles) Add(new SmoothBorder { Width = s, Height = s, CornerRadius = new CornerRadius(s / 4), Background = new SolidColorBrush(c) }, gap + 4);
        double trayGap = 8;
        if (v.Bool("media.enabled"))
        {
            var media = new StackPanel { Orientation = Orientation.Horizontal };
            media.Children.Add(new SmoothBorder { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center });
            var title = Theme.Label("Lo-fi", 10, ink, Theme.Regular, false, true);
            title.Margin = new Thickness(5, 0, 0, 0);
            media.Children.Add(title);
            Add(new SmoothBorder { Height = 20, CornerRadius = new CornerRadius(6), Padding = new Thickness(3, 0, 6, 0), Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x1A : (byte)0x10, (byte)(dark ? 255 : 0), (byte)(dark ? 255 : 0), (byte)(dark ? 255 : 0))), Child = media }, 14);
        }
        if (v.Bool("usage.enabled")) Add(UsageBars(26, 3, Color.FromArgb(0x24, ink.Color.R, ink.Color.G, ink.Color.B)), trayGap);
        foreach (var (key, glyph) in new[] { ("tray.hideMicrophone", '\uE720'), ("tray.hideVolume", '\uE767'), ("tray.hideNetwork", '\uE701'), ("tray.hideBattery", '\uE83F') })
            if (!v.Bool(key)) Add(Theme.Glyph(glyph, 12, ink), trayGap);
        if (v.Text("tray.bell") != "hidden") Add(Theme.Glyph('\uEA8F', 12, ink), trayGap);
        string clock = v.Text("tray.clock");
        if (clock != "hidden")
        {
            string time = Clock(v.Text("tray.clockFormat") == "h12");
            if (clock == "timeDate")
            {
                var two = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var t1 = Theme.T(time, 10, ink, Theme.Medium, false);
                var t2 = Theme.T("2026-11-09", 9, ink, Theme.Regular, false);
                t1.HorizontalAlignment = t2.HorizontalAlignment = HorizontalAlignment.Right;
                two.Children.Add(t1);
                two.Children.Add(t2);
                Add(two, trayGap);
            }
            else Add(Theme.Label(time, 11, ink, Theme.Medium, false, true), trayGap);
        }

        string style = v.Text("pill.style");
        var pill = new SmoothBorder
        {
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Padding = new Thickness(Math.Round(h * 0.38), 0, Math.Round(h * 0.38), 0),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0xC8, 0x20, 0x20, 0x24) : Color.FromArgb(0xBD, 0xFA, 0xFA, 0xFA)),
            BorderBrush = style == "windowGlass" ? null : new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(style == "windowGlass" ? 0 : 1),
            TopLine = style == "glass" ? new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)) : null,
            Child = row,
            HorizontalAlignment = v.Text("pill.align") switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
            VerticalAlignment = v.Text("pill.edge") == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = new Thickness(12, 9, 12, 9),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.14, Direction = 270 },
        };
        var tag = Caption(Tr("미리 보기", "Preview"), new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)));
        tag.Margin = new Thickness(12, 9, 0, 9);
        tag.HorizontalAlignment = HorizontalAlignment.Left;
        tag.VerticalAlignment = pill.VerticalAlignment == VerticalAlignment.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        var desk = new Grid();
        desk.Children.Add(tag);
        desk.Children.Add(pill);
        return new SmoothBorder
        {
            Height = 136,
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush(WallTop, WallBottom, 70),
            ClipToBounds = true,
            Child = desk,
        };
    }

    /// "21:41", or "오후 9:41" / "9:41 PM" on a 12-hour clock.
    private static string Clock(bool twelve) => twelve ? Tr("오후 9:41", "9:41 PM") : "21:41";

    /// Two usage bars, the first longer.
    private static FrameworkElement UsageBars(double width, double height, Color track)
    {
        var bars = new StackPanel { Width = width, VerticalAlignment = VerticalAlignment.Center };
        foreach (double share in new[] { 0.62, 0.3 })
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, bars.Children.Count == 0 ? 0 : 2, 0, 0) };
            g.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(track) });
            g.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x57)), Width = Math.Round(width * share), HorizontalAlignment = HorizontalAlignment.Left });
            bars.Children.Add(g);
        }
        return bars;
    }

    // ── 숨김 아일랜드 ───────────────────────────────────────────────────────────────────────────
    /// The island with what it is set to show, in its colour.
    public static FrameworkElement Island(AddonValues v)
    {
        bool white = v.Text("island.color") == "white";
        var ink = new SolidColorBrush(white ? Color.FromRgb(0x18, 0x18, 0x1B) : Color.FromRgb(0xF4, 0xF4, 0xF5));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(FrameworkElement e)
        {
            e.Margin = new Thickness(row.Children.Count == 0 ? 0 : 8, 0, 0, 0);
            e.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(e);
        }
        if (v.Bool("island.showClock")) Add(Theme.Label(Clock(v.Text("tray.clockFormat") == "h12"), 10, ink, Theme.Semi, false, true));
        if (v.Bool("island.showMedia")) Add(new SmoothBorder { Width = 10, Height = 10, CornerRadius = new CornerRadius(2.5), Background = Theme.P.Accent });
        if (v.Bool("island.showUsage")) Add(UsageBars(22, 2.5, Color.FromArgb(0x33, ink.Color.R, ink.Color.G, ink.Color.B)));
        if (v.Bool("island.showMemory")) Add(Theme.Label("RAM 62%", 10, ink, Theme.Semi, false, true));
        if (v.Bool("island.showCpu")) Add(Theme.Label("CPU 12%", 10, ink, Theme.Semi, false, true));
        var island = new SmoothBorder
        {
            Height = 20,
            MinWidth = 40,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(white ? Color.FromRgb(0xFA, 0xFA, 0xFA) : Color.FromRgb(0x11, 0x11, 0x11)),
            BorderBrush = white ? Theme.P.Edge : null,
            BorderThickness = new Thickness(white ? 1 : 0),
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var line = new DockPanel { LastChildFill = false };
        var note = Caption(row.Children.Count == 0 ? Tr("보일 것을 아래에서 고르세요", "Pick what it shows below") : Tr("고른 것만 보입니다", "Only what is picked shows"));
        DockPanel.SetDock(note, Dock.Right);
        line.Children.Add(note);
        line.Children.Add(Caption(Tr("아일랜드", "Island")));
        line.Children.Add(island);
        return Stage(line, indent: true);
    }

    // ── 모니터 밝기 조정 ────────────────────────────────────────────────────────────────────────
    /// The brightness widget on the taskbar (the design system's monitor, at twice its size) and what the wheel and the
    /// shortcut each change it by.
    public static FrameworkElement Brightness(AddonValues v)
    {
        var widget = new Pic(true, 25, 18).BoxLeft(1.15, 0.95, 15.9, 12.6, 3.2, false, 0.25).Outline(1.15, 0.95, 22.7, 12.6, 3.2, 0.65, 1)
            .Box(11.1, 13.55, 2.8, 2.4, 0, 1).Box(7.5, 15.9, 10, 1.3, 0.65, 1).Text("70", 12.5, 7.25, 7.5, width: 20);
        var art = new Viewbox { Width = 50, Height = 36, Child = widget.Done(), Margin = new Thickness(14, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var wheel = new StackPanel { Orientation = Orientation.Horizontal };
        var mouse = Theme.Glyph('\uE962', 12, Theme.P.TextDim);
        mouse.Margin = new Thickness(0, 0, 6, 0);
        wheel.Children.Add(mouse);
        wheel.Children.Add(Caption(Tr($"휠을 돌리면 {v.Int("taskbar.wheelStep")}%씩", $"The wheel: {v.Int("taskbar.wheelStep")}% a notch"), Theme.P.TextDim));
        lines.Children.Add(wheel);
        var (mods, vk) = v.Key("keys.up");
        string combo = Hotkeys.Combo(mods, vk);
        if (combo != "off")
        {
            var keys = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (string part in Hotkeys.Parts(combo))
            {
                var cap = Keys.Cap(part);
                if (keys.Children.Count == 0) cap.Margin = new Thickness(0);
                keys.Children.Add(cap);
            }
            var by = Caption(Tr($"누르면 {v.Int("keys.step")}%씩", $"{v.Int("keys.step")}% a press"), Theme.P.TextDim);
            by.Margin = new Thickness(8, 0, 0, 0);
            keys.Children.Add(by);
            lines.Children.Add(keys);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Caption(Tr("작업표시줄에서", "On the taskbar")));
        row.Children.Add(art);
        row.Children.Add(lines);
        return Stage(row);
    }

    /// Dimming while away: in use at 100, the wait, dimmed to the level, back on the mouse or keyboard.
    public static FrameworkElement Idle(AddonValues v)
    {
        int minutes = v.Int("idle.minutes"), level = v.Int("idle.level");
        var p = new Pic(true, 520, 66);
        void Monitor(double x, double fill, string number, bool accent, string caption)
        {
            p.Box(x, 8, 64, 40, 5, fill).Outline(x, 8, 64, 40, 5, 1, 0.5).Box(x + 26, 50, 12, 3, 1.5, 0.4)
                .Text(number, x + 32, 28, 12, paint: accent ? "accent" : "popup", width: 50)
                .Text(caption, x + 32, 60, 11, false, 0.7, width: 100);
        }
        void Arrow(double from, double to) => p.Stroke(Invariant($"M {from},28 H {to} M {to - 6},23 L {to},28 {to - 6},33"), 1.3, 0.35);
        Monitor(10, 0.55, "100", false, Tr("쓰는 동안", "In use"));
        Arrow(88, 232);
        p.Acc(118, 17, 84, 22, 6).Text(Tr($"{minutes}분 그대로", $"{minutes} min idle"), 160, 28, 11, paint: "accentInk", width: 84);
        Monitor(246, 0.1, level.ToString(CultureInfo.CurrentCulture), true, Tr("어둡게", "Dimmed"));
        Arrow(324, 430);
        p.Text(Tr("마우스·키보드", "Mouse or keys"), 377, 18, 11, false, 0.7, width: 110);
        Monitor(444, 0.55, "100", false, Tr("원래대로", "Back"));
        return Stage(Fit(p), indent: true);
    }

    // ── 커피 ────────────────────────────────────────────────────────────────────────────────────
    /// How the coffee icon is used: a left click picks a time then and there, a right click turns it on for the time
    /// set below.
    public static FrameworkElement Coffee(AddonValues v)
    {
        var p = new Pic(true, 444, 58);
        // the cup
        p.Shape("M 7,18 H 27 V 30 A 6,6 0 0 1 21,36 H 13 A 6,6 0 0 1 7,30 Z", accent: true).Stroke("M 27,21 H 30 A 4,4 0 0 1 30,29 H 27", 2, 1, true)
            .Stroke("M 13,11 Q 15,14 13,16 M 19,11 Q 21,14 19,16", 1.3, 0.5);
        void Mouse(double x, bool left, string caption)
        {
            p.Outline(x, 4, 22, 34, 11, 1.3, 0.55).Stroke(Invariant($"M {x + 11},4 V 17 M {x},17 H {x + 22}"), 1.3, 0.55)
                .Shape(left ? Invariant($"M {x + 11},4.7 A 10.3,10.3 0 0 0 {x + 0.7},15 V 17 H {x + 11} Z") : Invariant($"M {x + 11},4.7 A 10.3,10.3 0 0 1 {x + 21.3},15 V 17 H {x + 11} Z"), accent: true)
                .Text(caption, x + 11, 50, 11, false, 0.75, width: 80);
        }
        void Arrow(double x) => p.Stroke(Invariant($"M {x},21 H {x + 18} M {x + 13},16 L {x + 18},21 {x + 13},26"), 1.3, 0.4);
        Mouse(60, true, Tr("왼쪽 클릭", "Left click"));
        Arrow(98);
        p.Box(124, 6, 108, 30, 7, 0.06).Outline(124, 6, 108, 30, 7, 1, 0.18).Text(Tr("30분 · 1시간 …", "30 min · 1 h …"), 178, 21, 11, false, width: 104)
            .Text(Tr("그때그때 고르기", "Pick each time"), 178, 50, 11, false, 0.75, width: 110);
        Mouse(256, false, Tr("오른쪽 클릭", "Right click"));
        Arrow(294);
        string set = v.Text("awake.duration") switch
        {
            "m30" => Tr("바로 30분 켜기", "On for 30 min"),
            "h1" => Tr("바로 1시간 켜기", "On for 1 hour"),
            "h2" => Tr("바로 2시간 켜기", "On for 2 hours"),
            "custom" => Tr($"바로 {Minutes(v.Int("awake.minutes"))} 켜기", $"On for {Minutes(v.Int("awake.minutes"))}"),
            _ => Tr("끌 때까지 켜기", "On until turned off"),
        };
        p.Acc(320, 6, 120, 30, 7).Text(set, 380, 21, 11, paint: "accentInk", width: 116)
            .Text(Tr("아래에서 정한 시간", "The time set below"), 380, 50, 11, false, 0.75, width: 120);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var how = Caption(Tr("쓰는 법", "How to use"));
        how.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(how);
        row.Children.Add(Fit(p));
        return Stage(row);
    }

    /// 45 → "45분", 120 → "2시간", 90 → "1시간 30분".
    internal static string Minutes(int m) => m % 60 == 0 && m >= 60 ? Tr($"{m / 60}시간", $"{m / 60} h")
        : m > 60 ? Tr($"{m / 60}시간 {m % 60}분", $"{m / 60} h {m % 60} min") : Tr($"{m}분", $"{m} min");

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// The memory in use now, for the threshold's mark (-1: unknown).
    public static int MemoryNow() => Native.Win32.MemoryLoad();

    /// The tray icons' glyphs, by the chip's key (Segoe Fluent Icons): the icon each chip stands for.
    public static char ChipGlyph(string key) => key switch
    {
        "tray.hideVolume" => '\uE767',
        "tray.hideNetwork" => '\uE701',
        "tray.hideBattery" => '\uE83F',
        "tray.hideMicrophone" => '\uE720',
        "tray.hideCopilot" => '\uE8BD',
        "tray.bell" => '\uEA8F',
        "tray.hideLocation" => '\uE81D',
        "tray.hideStudioEffects" => '\uE714',
        "tray.hideRecall" => '\uE81C',
        "tray.hideLanguageBar" => '\uE8D3',
        "tray.hideLanguageExtras" => '\uE765',
        "island.showClock" => '\uE823',
        "island.showMedia" => '\uE8D6',
        "island.showUsage" => '\uEC4A',
        "island.showMemory" => '\uE950',
        "island.showCpu" => '\uE9D9',
        _ => '\0',
    };
}
