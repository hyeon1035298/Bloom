using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온 > 작업표시줄 폴더: the taskbar as the mod sees it (a preview strip of the pins), the folders in a list beside
/// the one being edited — its name, icon, apps (dragged into order, taken out, added from the Start menu) and 폴더 풀기 —
/// a new folder, and the running folder. Bloom never changes the folders itself: each change is a command the mod
/// carries out in explorer, and the state it writes back redraws this. Read only while the add-on doesn't run or the mod
/// doesn't answer.
internal sealed class TaskbarEditor : Border
{
    /// What the editor was showing, kept across the page's rebuilds (each state.json change rebuilds it).
    internal sealed class Edit
    {
        public string? Selected; // a folder's name, Running, or null for the first folder
        public bool New;
        public string NewName = "";
        public List<(string path, string name)> Picks = new();
        public string? Confirm; // "dissolve" or "delete" while its question shows
        public string IconTab = "auto";
        public bool DarkTone = true;
        public string? Error;
        public bool Busy;
    }

    public const string Running = "\u0001running";

    private const string DragFormat = "Bloom.TaskbarApp";

    private static readonly char[] Glyphs =
    {
        '\uE8B7', '\uE7FC', '\uE8D6', '\uE91B', '\uE715', '\uE774', '\uE943', '\uE821', '\uE80F', '\uEB51',
        '\uE734', '\uE8BD', '\uE714', '\uE8A5', '\uE8EF', '\uE713', '\uE7BF', '\uE70F', '\uE753', '\uE716',
    };

    private readonly Host host;
    private readonly Addon addon;
    private readonly AddonValues values;
    private readonly TaskbarState? state;
    private readonly Edit edit;
    private readonly bool readOnly;
    private readonly Action rebuild;

    public TaskbarEditor(Host host, Addon addon, AddonValues values, TaskbarState? state, Edit edit, string? readOnlyWhy, Action rebuild)
    {
        this.host = host;
        this.addon = addon;
        this.values = values;
        this.state = state;
        this.edit = edit;
        this.rebuild = rebuild;
        readOnly = readOnlyWhy is not null;
        Padding = new Thickness(14, 12, 14, 14);
        var root = new StackPanel();
        if (readOnlyWhy is not null) root.Children.Add(Note(readOnlyWhy, G.Lock, Theme.P.TextDim));
        if (edit.Busy) root.Children.Add(Note(Tr("작업표시줄에 보내는 중…", "Sending to the taskbar…"), G.Sync, Theme.P.TextDim));
        else if (edit.Error is { } error) root.Children.Add(Note(error, G.Warning, Theme.P.Danger));
        if (state is not null) root.Children.Add(Preview());
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(List());
        var detail = Detail();
        detail.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(detail, 1);
        grid.Children.Add(detail);
        root.Children.Add(grid);
        Child = root;
    }

    private string[] Folders => state?.FolderNames.ToArray() ?? Array.Empty<string>();

    /// The folder (or Running) shown on the right; null for the empty state.
    private string? Current => edit.Selected == Running ? Running
        : edit.Selected is { } s && Folders.Contains(s) ? s : Folders.FirstOrDefault();

    private static FrameworkElement Note(string text, char glyph, Brush brush)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var g = Theme.Glyph(glyph, 12, brush);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(g);
        var t = Theme.T(Hangul.KeepAll(text), 12.5, brush, Theme.Regular, false);
        t.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(t);
        return sp;
    }

    // ── the pieces drawn ────────────────────────────────────────────────────────────────────────
    private static FrameworkElement AppIcon(string? path, double size) =>
        path is { Length: > 0 } ? Icons.Build(IconSpec.Shell(path), size) : Icons.Tile(G.App, false, size);

    private FrameworkElement FolderIcon(string? name, double size)
    {
        if (name == Running) return Icons.Tile(G.Clock, false, size);
        var f = name is null ? null : state?.Items.GetValueOrDefault(name);
        if (f?.Icon is { Kind: "file", Path: { Length: > 0 } p }) return Icons.Build(IconSpec.Shell(p), size);
        // 앱 모음: the first four apps together on one tile, as the mod draws it
        var grid = new UniformGrid { Rows = 2, Columns = 2, Margin = new Thickness(size * 0.12) };
        foreach (var a in (f?.Apps ?? new()).Take(4))
        {
            var i = AppIcon(a.IconPath, size * 0.34);
            i.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(i);
        }
        return new SmoothBorder { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Background = Theme.P.Tile, Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    /// The pinned strip as the taskbar shows it; a folder in it is picked with a click.
    private FrameworkElement Preview()
    {
        var pill = new StackPanel { Orientation = Orientation.Horizontal };
        // the Start button as Bloom's flower (시작 버튼 › Bloom 버튼), first as on the taskbar; the gap under it
        // stands for a pin's mark so the row lines up
        if (values.Text("start.mode") == "bloom")
        {
            var start = new StackPanel { Margin = new Thickness(5, 0, 5, 0), ToolTip = "Bloom" };
            start.Children.Add(Launcher.MarkImage(26));
            start.Children.Add(new FrameworkElement { Height = 7 });
            pill.Children.Add(start);
        }
        foreach (var pin in state!.Pinned)
        {
            string? folder = pin.Kind switch { "folder" => pin.Name, "running" => Running, _ => null };
            if (pin.Kind == "running" && !values.Bool("folders.autoRunning")) continue;
            var icon = folder is null ? AppIcon(pin.Path is { Length: > 0 } ? pin.Path : pin.AppId is { Length: > 0 } id ? @"shell:AppsFolder\" + id : null, 26) : FolderIcon(folder, 26);
            bool on = folder is not null && folder == Current && !edit.New;
            var mark = new SmoothBorder { Height = on ? 2 : 3, Width = on ? 18 : 6, CornerRadius = new CornerRadius(1.5), Background = on ? Theme.P.Accent : Theme.P.TextFaint, Margin = new Thickness(0, 4, 0, on ? 1 : 0), HorizontalAlignment = HorizontalAlignment.Center, Opacity = folder is null && pin.Kind != "app" ? 0 : on ? 1 : 0.6 };
            var item = new StackPanel { Margin = new Thickness(5, 0, 5, 0), ToolTip = folder == Running ? RunningName : pin.Name };
            item.Children.Add(icon);
            item.Children.Add(mark);
            if (folder is not null)
            {
                item.Cursor = Cursors.Hand;
                string pick = folder;
                item.MouseLeftButtonUp += (_, e) =>
                {
                    Select(pick);
                    e.Handled = true;
                };
            }
            pill.Children.Add(item);
        }
        var clock = Theme.T(ClockText(values.Text("tray.clockFormat") == "h12"), 12, Theme.P.TextDim, Theme.Medium);
        clock.VerticalAlignment = VerticalAlignment.Center;
        clock.Margin = new Thickness(14, 0, 4, 4);
        pill.Children.Add(clock);
        // the taskbar as it looks on a desktop: a see-through pill over a made-up wallpaper
        bool dark = Theme.P.Dark;
        var bar = new SmoothBorder
        {
            Background = new SolidColorBrush(dark ? Color.FromArgb(0x8C, 0x1C, 0x1C, 0x20) : Color.FromArgb(0x9E, 0xFA, 0xFA, 0xFC)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(100), // a pill
            Padding = new Thickness(14, 9, 14, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = pill,
        };
        var desk = new StackPanel();
        desk.Children.Add(bar);
        var hint = Theme.T(Tr("폴더를 누르면 아래에서 고칩니다", "Click a folder to edit it below"), 11.5, Theme.P.TextDim, Theme.Regular);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, 10, 0, 0);
        desk.Children.Add(hint);
        return new SmoothBorder { Background = Wallpaper(dark), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 20, 12, 12), Child = desk };
    }

    /// A soft made-up wallpaper for the preview: two blurred glows (rose top left, blue bottom right) on a slanted wash.
    private static Brush Wallpaper(bool dark)
    {
        static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        static Brush Glow(Color c, Point at, double rx, double ry, double reach) => new RadialGradientBrush
        {
            Center = at,
            GradientOrigin = at,
            RadiusX = rx,
            RadiusY = ry,
            GradientStops = { new GradientStop(c, 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), reach) },
        };
        var box = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var wash = new LinearGradientBrush(C(dark ? 0x1D1D24u : 0xECEEF3u), C(dark ? 0x131318u : 0xDDE1E8u), new Point(0.33, 0.03), new Point(0.67, 0.97));
        var art = new DrawingGroup();
        art.Children.Add(new GeometryDrawing(wash, null, box));
        art.Children.Add(new GeometryDrawing(Glow(C(dark ? 0x3B3247u : 0xF2DBE4u), new Point(0.15, 0), 1.2, 1.4, 0.55), null, box));
        art.Children.Add(new GeometryDrawing(Glow(C(dark ? 0x23384Au : 0xD5E5F2u), new Point(0.9, 1), 0.9, 1.2, 0.6), null, box));
        var brush = new DrawingBrush(art) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private string RunningName => values.Text("folders.runningName") is { Length: > 0 } n ? n : Tr("실행 중", "Running");

    /// The time as the taskbar writes it (the add-on's ClockText): this PC's short time pattern, in 12 hours with its
    /// AM/PM mark when tray.clockFormat says so, else in 24 hours without one.
    internal static string ClockText(bool twelve)
    {
        var culture = CultureInfo.CurrentCulture;
        string format = culture.DateTimeFormat.ShortTimePattern;
        if (twelve)
        {
            format = format.Replace('H', 'h');
            if (!format.Contains('t')) format = "tt " + format;
        }
        else
        {
            format = string.Join(' ', format.Replace("t", "").Replace('h', 'H').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return DateTime.Now.ToString(format, culture);
    }

    private FrameworkElement ListRow(FrameworkElement icon, string name, string end, bool on, Action? click)
    {
        var g = new Grid { Height = 40 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(icon);
        var t = Theme.Label(name, 13, Theme.P.Text, Theme.Medium, true);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var c = Theme.Label(end, 12, Theme.P.TextFaint, Theme.Regular);
        Grid.SetColumn(c, 2);
        g.Children.Add(c);
        // the folder open on the right: a gray fill, as the settings' own list marks its page
        var b = new SmoothBorder { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 10, 0), Margin = new Thickness(0, 1, 0, 1), Background = on ? Theme.P.Selected : Brushes.Transparent, Child = g };
        if (click is not null)
        {
            b.Cursor = Cursors.Hand;
            if (!on)
            {
                b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
                b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            }
            b.MouseLeftButtonUp += (_, e) =>
            {
                click();
                e.Handled = true;
            };
        }
        return b;
    }

    private FrameworkElement List()
    {
        var sp = new StackPanel();
        string? current = edit.New ? null : Current;
        foreach (string f in Folders)
            sp.Children.Add(ListRow(FolderIcon(f, 24), f, (state!.Items[f].Apps.Count).ToString(System.Globalization.CultureInfo.CurrentCulture), f == current, () => Select(f)));
        sp.Children.Add(ListRow(Icons.Tile(G.Add, false, 24), Tr("새 폴더", "New folder"), "", edit.New, readOnly ? null : StartNew));
        sp.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Line, Margin = new Thickness(8, 6, 8, 6) });
        var label = Theme.T(Tr("자동 폴더", "Automatic folder"), 11.5, Theme.P.TextFaint, Theme.Semi);
        label.Margin = new Thickness(8, 0, 0, 4);
        sp.Children.Add(label);
        bool on = values.Bool("folders.autoRunning");
        sp.Children.Add(ListRow(FolderIcon(Running, 24), RunningName, on ? (state?.Running?.Apps.Count ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture) : Tr("꺼짐", "Off"), current == Running, () => Select(Running)));
        return sp;
    }

    private void Select(string folder)
    {
        edit.Selected = folder;
        edit.New = false;
        edit.Confirm = null;
        edit.Picks.Clear();
        rebuild();
    }

    private void StartNew()
    {
        edit.New = true;
        edit.Confirm = null;
        edit.Picks.Clear();
        rebuild();
    }

    // ── the right side ──────────────────────────────────────────────────────────────────────────
    private FrameworkElement Detail()
    {
        if (edit.New) return NewFolder();
        string? current = Current;
        if (current == Running) return RunningDetail();
        if (current is null) return state is null ? Faint(Tr("작업표시줄 꾸미기가 작동하면 고정한 폴더가 여기에 보입니다.", "The folders pinned to the taskbar show here while taskbar customization runs.")) : Empty();
        var d = FolderDetail(current);
        d.IsEnabled = !readOnly;
        return d;
    }

    private static TextBlock Faint(string text, double size = 12)
    {
        var t = Theme.T(Hangul.KeepAll(text), size, Theme.P.TextFaint, Theme.Regular, false);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    private static Grid Head(FrameworkElement icon, FrameworkElement field, FrameworkElement? end)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, 14, 0);
        g.Children.Add(icon);
        Grid.SetColumn(field, 1);
        field.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(field);
        if (end is not null)
        {
            end.VerticalAlignment = VerticalAlignment.Center;
            end.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(end, 2);
            g.Children.Add(end);
        }
        return g;
    }

    private static FrameworkElement SubLine(string left, string right)
    {
        var g = new Grid { Margin = new Thickness(0, 16, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Theme.T(left, 12.5, Theme.P.TextDim, Theme.Semi));
        var r = Faint(right);
        Grid.SetColumn(r, 1);
        g.Children.Add(r);
        return g;
    }

    /// An app on its tile: the icon, the name under it, and (when given) the ✕ that takes it out.
    private static FrameworkElement AppTile(string? iconPath, string name, Action? takeOut, bool picked = false)
    {
        var sp = new StackPanel { Width = 78, Margin = new Thickness(0, 0, 6, 8) };
        var icon = AppIcon(iconPath, 40);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(icon);
        var n = Theme.T(name, 11.5, Theme.P.Text, Theme.Regular);
        n.TextTrimming = TextTrimming.CharacterEllipsis;
        n.TextAlignment = TextAlignment.Center;
        n.Margin = new Thickness(2, 6, 2, 0);
        sp.Children.Add(n);
        var cell = new Grid { ToolTip = name };
        cell.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 8, 0, 6), Background = picked ? Theme.P.AccentSoft : Brushes.Transparent, Child = sp });
        if (takeOut is not null)
        {
            var x = new SmoothBorder { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = Theme.P.Btn, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0), ToolTip = Tr("빼기", "Take out"), Child = Theme.Glyph(G.Close, 9, Theme.P.TextDim) };
            ((FrameworkElement)x.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)x.Child).VerticalAlignment = VerticalAlignment.Center;
            x.MouseLeftButtonUp += (_, e) =>
            {
                takeOut();
                e.Handled = true;
            };
            cell.Children.Add(x);
        }
        return cell;
    }

    private static FrameworkElement AddTile(string label, Action<FrameworkElement> open)
    {
        var sp = new StackPanel { Width = 78, Margin = new Thickness(0, 0, 6, 8) };
        var plus = new SmoothBorder { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), BorderBrush = Theme.P.EdgeStrong, BorderThickness = new Thickness(1), Child = Theme.Glyph(G.Add, 14, Theme.P.TextDim), HorizontalAlignment = HorizontalAlignment.Center };
        ((FrameworkElement)plus.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)plus.Child).VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(plus);
        var n = Theme.T(label, 11.5, Theme.P.TextDim, Theme.Regular);
        n.TextAlignment = TextAlignment.Center;
        n.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(n);
        var b = new SmoothBorder { CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 8, 0, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = sp };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            open(b);
            e.Handled = true;
        };
        return b;
    }

    private FrameworkElement FolderDetail(string folder)
    {
        var f = state!.Items[folder];
        var sp = new StackPanel();
        var name = new TextField(folder, Tr("폴더 이름", "Folder name"), 140);
        name.Applied += v =>
        {
            if (v.Length > 0 && v != folder) Send(TaskbarCommands.Rename(folder, v), Tr("이름 바꾸기", "Rename"), v);
        };
        // the button's own click marks the press handled, so the popup opens from it (a second handler never hears it)
        SmoothBorder? iconButton = null;
        iconButton = ToolUi.TextButton(Tr("아이콘 바꾸기", "Change icon"), () => IconPopup(folder, iconButton!));
        sp.Children.Add(Head(FolderIcon(folder, 48), name, iconButton));
        sp.Children.Add(SubLine(Tr($"안의 앱 {f.Apps.Count}개", $"{f.Apps.Count} apps inside"), Tr("끌어서 순서를 바꿉니다", "Drag to reorder")));
        var apps = new WrapPanel();
        var files = f.Apps.Select(a => a.File ?? a.Name).ToList();
        foreach (var a in f.Apps)
        {
            string file = a.File ?? a.Name;
            var tile = AppTile(a.IconPath, a.Name, () => Send(TaskbarCommands.TakeOut(folder, file), Tr("앱 빼기", "Take out"), folder));
            Draggable(tile, folder, file, files);
            apps.Children.Add(tile);
        }
        apps.Children.Add(AddTile(Tr("앱 추가", "Add app"), at => AppPopup(at, folder, path => Send(TaskbarCommands.Add(folder, path), Tr("앱 추가", "Add app"), folder))));
        sp.Children.Add(apps);
        sp.Children.Add(new SmoothBorder { Height = 1, Background = Theme.P.Line, Margin = new Thickness(0, 6, 0, 10) });
        var foot = new WrapPanel();
        if (edit.Confirm is { } act)
        {
            bool delete = act == "delete";
            string question = delete
                ? f.Apps.Count > 0
                    ? Tr($"‘{folder}’ 폴더를 지웁니다. 안의 앱 {f.Apps.Count}개도 작업표시줄에서 빠지고, 폴더는 휴지통으로 갑니다.", $"Deletes ‘{folder}’. Its {f.Apps.Count} apps leave the taskbar too, and the folder goes to the Recycle Bin.")
                    : Tr($"‘{folder}’ 폴더를 지웁니다. 폴더는 휴지통으로 갑니다.", $"Deletes ‘{folder}’. The folder goes to the Recycle Bin.")
                : Tr($"‘{folder}’ 폴더를 풉니다. 안의 앱 {f.Apps.Count}개는 작업표시줄로 돌아갑니다.", $"Ungroups ‘{folder}’. Its {f.Apps.Count} apps go back to the taskbar.");
            var q = Theme.T(question, 12.5, Theme.P.Text, Theme.Medium);
            q.VerticalAlignment = VerticalAlignment.Center;
            q.Margin = new Thickness(0, 0, 8, 0);
            foot.Children.Add(q);
            foot.Children.Add(ActionButton.Make(Tr("취소", "Cancel"), false, () =>
            {
                edit.Confirm = null;
                rebuild();
            }));
            foot.Children.Add(ActionButton.Make(delete ? Tr("지우기", "Delete") : Tr("풀기", "Ungroup"), true, () =>
            {
                edit.Confirm = null;
                edit.Selected = null;
                if (delete) Send(TaskbarCommands.Delete(folder), Tr("폴더 지우기", "Delete folder"), null);
                else Send(TaskbarCommands.Dissolve(folder), Tr("폴더 풀기", "Ungroup"), null);
            }));
        }
        else
        {
            void Ask(string label, string act)
            {
                var b = ToolUi.TextButton(label, () =>
                {
                    edit.Confirm = act;
                    rebuild();
                });
                b.Margin = new Thickness(0, 0, 10, 0);
                foot.Children.Add(b);
            }
            if (f.Apps.Count > 0) Ask(Tr("폴더 풀기", "Ungroup folder"), "dissolve");
            Ask(Tr("폴더 지우기", "Delete folder"), "delete");
            var hint = Faint(f.Apps.Count > 0
                ? Tr("풀면 앱이 작업표시줄로 돌아가고, 지우면 앱도 빠집니다. 폴더는 휴지통으로 갑니다", "Ungrouping puts the apps back on the taskbar; deleting takes them off too. The folder goes to the Recycle Bin")
                : Tr("빈 폴더는 지우면 작업표시줄에서 빠지고 휴지통으로 갑니다", "An empty folder, deleted, leaves the taskbar for the Recycle Bin"));
            hint.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(hint);
        }
        sp.Children.Add(foot);
        return sp;
    }

    /// Drag an app onto another to put it before that one: the folder's new order goes to the mod.
    private void Draggable(FrameworkElement tile, string folder, string file, List<string> order)
    {
        Point from = default;
        tile.PreviewMouseLeftButtonDown += (_, e) => from = e.GetPosition(tile);
        tile.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(tile) - from).Length < 6) return;
            DragDrop.DoDragDrop(tile, new DataObject(DragFormat, file), DragDropEffects.Move);
        };
        tile.AllowDrop = true;
        tile.Drop += (_, e) =>
        {
            if (e.Data.GetData(DragFormat) is not string moved || moved == file) return;
            var next = order.Where(n => n != moved).ToList();
            next.Insert(Math.Max(0, next.IndexOf(file)), moved);
            Send(TaskbarCommands.Order(folder, next), Tr("순서 바꾸기", "Reorder"), folder);
            e.Handled = true;
        };
    }

    private FrameworkElement NewFolder()
    {
        var sp = new StackPanel();
        var name = new TextField(edit.NewName, Tr("폴더 이름", "Folder name"), 140);
        sp.Children.Add(Head(Icons.Tile(G.Add, false, 48), name, null));
        void Again()
        {
            edit.NewName = name.Text; // the name typed so far outlives the redraw
            rebuild();
        }
        int n = edit.Picks.Count;
        sp.Children.Add(SubLine(Tr("넣을 앱을 고르세요", "Pick the apps to put in it"), n > 0 ? Tr($"{n}개 고름", $"{n} picked") : Tr("하나 이상", "One or more")));
        var apps = new WrapPanel();
        // the pinned apps first (a click picks or unpicks), then what was picked from the Start menu
        var pool = (state?.Pinned ?? new()).Where(p => p.Kind == "app" && p.Path is { Length: > 0 }).Select(p => (path: p.Path!, name: p.Name ?? Path.GetFileNameWithoutExtension(p.Path!))).ToList();
        foreach (var pick in edit.Picks.Where(p => !pool.Any(q => q.path == p.path))) pool.Add(pick);
        foreach (var (path, label) in pool)
        {
            bool on = edit.Picks.Any(p => p.path == path);
            var tile = AppTile(path, label, null, on);
            tile.Cursor = Cursors.Hand;
            tile.MouseLeftButtonUp += (_, e) =>
            {
                if (on) edit.Picks.RemoveAll(p => p.path == path);
                else edit.Picks.Add((path, label));
                Again();
                e.Handled = true;
            };
            apps.Children.Add(tile);
        }
        apps.Children.Add(AddTile(Tr("앱 추가", "Add app"), at => AppPopup(at, null, path =>
        {
            if (!edit.Picks.Any(p => p.path == path)) edit.Picks.Add((path, host.Apps.Apps.FirstOrDefault(a => a.Path == path)?.Name ?? Path.GetFileNameWithoutExtension(path)));
            Again();
        })));
        sp.Children.Add(apps);
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = Faint(Tr("고정한 앱과 시작 메뉴의 앱을 넣을 수 있습니다", "Pinned apps and Start menu apps can go in"));
        hint.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ActionButton.Make(Tr("취소", "Cancel"), false, () =>
        {
            edit.New = false;
            edit.NewName = "";
            edit.Picks.Clear();
            rebuild();
        }));
        var make = ActionButton.Make(Tr("만들기", "Create"), true, () =>
        {
            if (name.Text.Length == 0 || edit.Picks.Count == 0) return;
            var paths = edit.Picks.Select(p => p.path).ToList();
            edit.New = false;
            edit.NewName = "";
            edit.Picks.Clear();
            edit.Selected = name.Text;
            Send(TaskbarCommands.Create(name.Text, paths), Tr("새 폴더", "New folder"), name.Text);
        });
        make.Opacity = n > 0 ? 1 : 0.45;
        buttons.Children.Add(make);
        Grid.SetColumn(buttons, 1);
        foot.Children.Add(buttons);
        sp.Children.Add(foot);
        sp.IsEnabled = !readOnly;
        return sp;
    }

    /// The running folder: its switch and name are settings of the add-on (values), not commands, and work offline too.
    private FrameworkElement RunningDetail()
    {
        var sp = new StackPanel();
        bool on = values.Bool("folders.autoRunning");
        var name = new TextField(RunningName, Tr("폴더 이름", "Folder name"), 140);
        name.Applied += v =>
        {
            values.Set("folders.runningName", v.Length > 0 ? v : values.Default("folders.runningName")); // empty: the default name
            Settings.Save();
            rebuild();
        };
        var sw = new Switch(on) { Cursor = Cursors.Hand, ToolTip = Tr("실행 중 폴더 쓰기", "Use the running folder") };
        sw.MouseLeftButtonUp += (_, e) =>
        {
            values.Set("folders.autoRunning", !on);
            Settings.Save();
            rebuild();
            e.Handled = true;
        };
        sp.Children.Add(Head(FolderIcon(Running, 48), name, sw));
        var about = Faint(Tr("고정하지 않고 켠 앱 가운데 어느 폴더에도 없는 앱을 이 폴더에 모읍니다. 앱을 끄면 빠지고, 그런 앱이 없으면 작업표시줄에서 폴더도 사라집니다.", "Gathers the apps you run without pinning that aren't in any folder. A closed app leaves it, and with none left the folder leaves the taskbar."));
        about.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(about);
        var apps = state?.Running?.Apps ?? new();
        sp.Children.Add(SubLine(Tr("지금 들어 있는 앱", "Apps in it now"), on ? Tr($"{apps.Count}개", $"{apps.Count}") : Tr("꺼짐", "Off")));
        if (on)
        {
            var wrap = new WrapPanel();
            foreach (var a in apps) wrap.Children.Add(AppTile(a.IconPath, a.Name, null));
            sp.Children.Add(wrap);
        }
        else sp.Children.Add(Faint(Tr("꺼 두면 고정하지 않은 앱이 Windows처럼 따로 버튼을 만듭니다.", "While off, unpinned apps get their own buttons, as in Windows.")));
        return sp;
    }

    private FrameworkElement Empty()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18), MaxWidth = 340 };
        var icon = Icons.Tile(G.Folder, false, 40);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(icon);
        var t = Theme.T(Tr("아직 폴더가 없습니다", "No folders yet"), 14, Theme.P.Text, Theme.Semi);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 4);
        sp.Children.Add(t);
        var s = Faint(Tr("앱 몇 개를 골라 폴더를 만드세요. 작업표시줄에서 앱을 다른 앱 위에 끌어 잠깐 두어도 폴더가 됩니다.", "Pick a few apps to make a folder. Dragging an app onto another on the taskbar and holding it there briefly makes one too."), 12.5);
        s.TextAlignment = TextAlignment.Center;
        sp.Children.Add(s);
        if (!readOnly)
        {
            var b = ActionButton.Make(Tr("새 폴더", "New folder"), true, StartNew);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(b);
        }
        return sp;
    }

    // ── popups ──────────────────────────────────────────────────────────────────────────────────
    private static (Popup popup, StackPanel body) Pop(FrameworkElement under, double width)
    {
        var body = new StackPanel();
        var card = new SmoothBorder
        {
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Width = width,
            Margin = new Thickness(14, 4, 14, 20),
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.3, Color = Colors.Black },
            LayoutTransform = Theme.Zoom,
        };
        var popup = new Popup { Child = card, AllowsTransparency = true, StaysOpen = false, PlacementTarget = under, Placement = PlacementMode.Bottom, HorizontalOffset = -14 };
        return (popup, body);
    }

    /// The Start menu's apps to add: only those with a shortcut file can go into a folder (the mod copies it); the
    /// others, and apps already in a folder, are shown dimmed with why.
    private void AppPopup(FrameworkElement under, string? folder, Action<string> picked)
    {
        var (popup, body) = Pop(under, 320);
        var search = new TextBox { FontFamily = Theme.Sans, FontSize = 13, Foreground = Theme.P.Text, CaretBrush = Theme.P.Accent, Background = Theme.P.Well, BorderThickness = new Thickness(0), Padding = new Thickness(8, 5, 8, 6), Template = Launcher.BareTextBox(), Tag = (Func<Key, bool>)(_ => false) };
        body.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(8), Background = Theme.P.Well, Child = search });
        var label = Theme.T(Tr("시작 메뉴의 앱", "Start menu apps"), 11.5, Theme.P.TextFaint, Theme.Semi);
        label.Margin = new Thickness(4, 10, 0, 4);
        body.Children.Add(label);
        var list = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Template = Launcher.ThinScroll() });
        string Where(AppEntry a)
        {
            string file = Path.GetFileName(a.Path);
            foreach (var (n, f) in state?.Items ?? new())
                if (f.Apps.Any(x => string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Path, a.Path, StringComparison.OrdinalIgnoreCase)))
                    return n == folder ? Tr("이 폴더에 있음", "In this folder") : Tr($"{n}에 있음", $"In {n}");
            return a.Source == "lnk" ? "" : Tr("바로 가기 없음", "No shortcut");
        }
        void Fill()
        {
            list.Children.Clear();
            var q = new Query(search.Text.Trim());
            foreach (var a in host.Apps.Apps.Where(a => q.Raw.Length == 0 || Matcher.Score(q, a.Match) > 0).OrderBy(a => a.Name, StringComparer.CurrentCulture).Take(200))
            {
                string why = Where(a);
                bool can = why.Length == 0;
                var g = new Grid { Height = 32 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icon = AppIcon(a.Path, 20);
                icon.Margin = new Thickness(0, 0, 10, 0);
                g.Children.Add(icon);
                var t = Theme.Label(a.Name, 13, Theme.P.Text, Theme.Medium, true);
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                var e = Theme.Label(why, 11.5, Theme.P.TextFaint, Theme.Regular);
                Grid.SetColumn(e, 2);
                g.Children.Add(e);
                var row = new SmoothBorder { CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 0, 6, 0), Child = g, Opacity = can ? 1 : 0.45, Background = Brushes.Transparent };
                if (can)
                {
                    string path = a.Path;
                    row.Cursor = Cursors.Hand;
                    row.MouseEnter += (_, _) => row.Background = Theme.P.Hover;
                    row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
                    row.MouseLeftButtonUp += (_, ev) =>
                    {
                        popup.IsOpen = false;
                        picked(path);
                        ev.Handled = true;
                    };
                }
                list.Children.Add(row);
            }
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        popup.IsOpen = true;
        search.Focus();
    }

    /// 아이콘 바꾸기: 앱 모음 (the apps drawn together), 아이콘 (the set, in the look of Bloom's theme here and in both
    /// looks on the taskbar), 기호 (a symbol on a light or dark tile, made into an .ico here), 파일 (an .ico, .png, .exe or
    /// .dll of the user's).
    private void IconPopup(string folder, FrameworkElement under)
    {
        var (popup, body) = Pop(under, 330);
        var tabs = new Segmented(new[] { ("auto", Tr("앱 모음", "Apps")), ("set", Tr("아이콘", "Icons")), ("glyph", Tr("기호", "Symbol")), ("file", Tr("파일", "File")) }, edit.IconTab) { HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(tabs);
        var inner = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(inner);
        void Show()
        {
            inner.Children.Clear();
            switch (edit.IconTab)
            {
                case "auto":
                    var a = Faint(Tr("안의 앱 넷을 모아 보입니다. 앱 순서를 바꾸면 아이콘도 따라 바뀝니다.", "Shows the first four apps together. Reordering the apps changes it too."));
                    inner.Children.Add(a);
                    var use = ActionButton.Make(Tr("앱 모음으로", "Use the apps"), true, () =>
                    {
                        popup.IsOpen = false;
                        Send(TaskbarCommands.IconAuto(folder), Tr("아이콘 바꾸기", "Change icon"), folder);
                    });
                    use.HorizontalAlignment = HorizontalAlignment.Left;
                    use.Margin = new Thickness(0, 10, 0, 0);
                    inner.Children.Add(use);
                    break;
                case "set":
                    var set = new WrapPanel();
                    foreach (string name in FolderIconSet.Names)
                    {
                        // the look this theme's taskbar gets: silver on a dark one
                        var b = new Border { Width = 40, Height = 40, Margin = new Thickness(0, 0, 4, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = new Image { Source = FolderIconSet.Image(name, Theme.P.Dark), Width = 38, Height = 38 } };
                        string picked = name;
                        b.MouseLeftButtonUp += (_, e) =>
                        {
                            popup.IsOpen = false;
                            UseSet(folder, picked);
                            e.Handled = true;
                        };
                        set.Children.Add(b);
                    }
                    inner.Children.Add(set);
                    var s = Faint(Tr("작업표시줄 테마에 따라 밝은 판과 어두운 판이 바뀝니다.", "Switches between its light and dark look with the taskbar's theme."));
                    s.Margin = new Thickness(0, 6, 0, 0);
                    inner.Children.Add(s);
                    break;
                case "glyph":
                    var grid = new WrapPanel();
                    foreach (char g in Glyphs)
                    {
                        var b = new SmoothBorder { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), Background = Theme.P.Tile, Cursor = Cursors.Hand, Child = Theme.Glyph(g, 15, Theme.P.Text) };
                        ((FrameworkElement)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
                        ((FrameworkElement)b.Child).VerticalAlignment = VerticalAlignment.Center;
                        char glyph = g;
                        b.MouseLeftButtonUp += (_, e) =>
                        {
                            popup.IsOpen = false;
                            UseGlyph(folder, glyph);
                            e.Handled = true;
                        };
                        grid.Children.Add(b);
                    }
                    inner.Children.Add(grid);
                    var tone = new Segmented(new[] { ("light", Tr("밝은 바탕", "Light")), ("dark", Tr("어두운 바탕", "Dark")) }, edit.DarkTone ? "dark" : "light") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                    tone.Picked += v => edit.DarkTone = v == "dark";
                    inner.Children.Add(tone);
                    break;
                default:
                    var pick = ActionButton.Make(Tr("파일 고르기", "Choose a file"), true, () =>
                    {
                        popup.IsOpen = false;
                        UseFile(folder);
                    });
                    pick.HorizontalAlignment = HorizontalAlignment.Left;
                    inner.Children.Add(pick);
                    var f = Faint(Tr(".ico, .png, .exe, .dll 파일의 아이콘을 씁니다.", "Uses the icon of an .ico, .png, .exe or .dll file."));
                    f.Margin = new Thickness(0, 10, 0, 0);
                    inner.Children.Add(f);
                    break;
            }
        }
        tabs.Picked += v =>
        {
            edit.IconTab = v;
            Show();
        };
        Show();
        popup.IsOpen = true;
    }

    private string IconDir => Path.Combine(addon.DataDir, "icons");

    /// Both looks of the set's icon `name`: silver for a dark taskbar, graphite for a light one.
    private void UseSet(string folder, string name)
    {
        try
        {
            string dark = IconFile.Save(IconDir, "set", IconFile.SetIcon(name, true));
            string light = IconFile.Save(IconDir, "set", IconFile.SetIcon(name, false));
            Send(TaskbarCommands.IconThemed(folder, dark, light), Tr("아이콘 바꾸기", "Change icon"), folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(Tr("아이콘 바꾸기", "Change icon"), "failed", e);
        }
    }

    private void UseGlyph(string folder, char glyph)
    {
        try
        {
            string file = IconFile.Save(IconDir, "glyph", IconFile.Glyph(glyph, edit.DarkTone));
            Send(TaskbarCommands.IconFile(folder, file, 0), Tr("아이콘 바꾸기", "Change icon"), folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(Tr("아이콘 바꾸기", "Change icon"), "failed", e);
        }
    }

    private void UseFile(string folder)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Tr("폴더 아이콘", "Folder icon"),
            Filter = Tr("아이콘 (*.ico;*.png;*.exe;*.dll)|*.ico;*.png;*.exe;*.dll", "Icons (*.ico;*.png;*.exe;*.dll)|*.ico;*.png;*.exe;*.dll"),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string file = dlg.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? IconFile.Save(IconDir, "picture", IconFile.FromPicture(dlg.FileName)) : dlg.FileName;
            Send(TaskbarCommands.IconFile(folder, file, 0), Tr("아이콘 바꾸기", "Change icon"), folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            Fail(Tr("아이콘 바꾸기", "Change icon"), "badArgs", e);
        }
    }

    private void Fail(string what, string error, Exception e)
    {
        Log.Error("taskbar icon", e);
        edit.Error = ErrorText(what, error);
        rebuild();
    }

    // ── commands ────────────────────────────────────────────────────────────────────────────────
    /// Sends a command and shows its outcome; `select`: the folder to show once it is done (a new or renamed one).
    private async void Send((string, JsonObject) command, string what, string? select)
    {
        edit.Busy = true;
        edit.Error = null;
        rebuild();
        var r = await addon.Commands.Send(command);
        edit.Busy = false;
        edit.Error = r.Ok ? null : ErrorText(what, r.Error);
        if (r.Ok && select is not null) edit.Selected = select;
        rebuild();
    }

    /// "<what> 실패 · <why>", the why from the mod's error.
    public static string ErrorText(string what, string? error)
    {
        string why = error switch
        {
            "foldersOff" => Tr("작업표시줄 폴더가 꺼져 있습니다", "Taskbar folders are off"),
            "noFolder" => Tr("폴더를 찾지 못했습니다", "The folder wasn't found"),
            "exists" => Tr("같은 이름의 폴더가 이미 있습니다", "A folder with that name already exists"),
            "badName" => Tr("폴더 이름에 쓸 수 없는 글자가 있습니다", "The name has characters a folder can't have"),
            "notFound" => Tr("앱이나 항목을 찾지 못했습니다", "The app or item wasn't found"),
            CommandResult.Timeout => Tr("작업표시줄이 5초 안에 답하지 않았습니다", "The taskbar didn't answer within 5 seconds"),
            _ => Tr("작업표시줄이 명령을 처리하지 못했습니다", "The taskbar couldn't carry it out"),
        };
        return Tr($"{what} 실패 · {why}", $"{what} failed · {why}");
    }
}
