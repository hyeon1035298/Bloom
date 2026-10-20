using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Space;

/// Bloom Space Cleaner: where the disk space went, as a map (SpaceSniffer's idea, Bloom's look).
/// Drives on top, the map in the middle (click = go in, Backspace = go out, right click = actions),
/// the largest files on the right, the scan's progress at the bottom. Deleting always goes to the
/// Recycle Bin after a question, and never touches drive roots or Windows' own folders.
internal sealed class SpaceWindow : Window
{
    private readonly Grid root;
    private readonly StackPanel crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel drives = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TreemapView map = new();
    private readonly StackPanel bigList = new();
    private readonly TextBlock bigTitle;
    private readonly TextBlock hoverText, scanText;
    private readonly Popup menu = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.MousePoint };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private SpaceScanner? scan;
    private SpaceDir? current;
    private DateTime bigAt = DateTime.MinValue;
    private bool asking; // a question is over the window: its keys go to it

    public SpaceWindow(string? path)
    {
        Title = "Bloom Space Cleaner";
        Width = 1180;
        Height = 760;
        MinWidth = 720;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        root = new Grid { Background = Theme.P.Solid };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        // top: where we are, the drives, the folder picker
        var top = new Grid { Margin = new Thickness(16, 0, 12, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = Button("\uE72B", Tr("위로 (Backspace)", "Up (Backspace)"), Up);
        back.Margin = new Thickness(0, 0, 10, 0);
        top.Children.Add(back);
        var crumbScroll = new ScrollViewer { Content = crumbs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        Grid.SetColumn(crumbScroll, 1);
        top.Children.Add(crumbScroll);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(drives);
        var pick = TextButton(Tr("폴더 고르기…", "Pick a folder…"), PickFolder);
        pick.Margin = new Thickness(10, 0, 0, 0);
        right.Children.Add(pick);
        var again = Button(G.Refresh, Tr("다시 읽기 (F5)", "Scan again (F5)"), Rescan);
        again.Margin = new Thickness(6, 0, 0, 0);
        right.Children.Add(again);
        Grid.SetColumn(right, 2);
        top.Children.Add(right);
        root.Children.Add(top);
        Line(1);

        // middle: the map and the largest files
        var mid = new Grid();
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        map.Margin = new Thickness(8);
        mid.Children.Add(map);
        var side = new Grid();
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bigTitle = Theme.T(Tr("큰 파일", "Big files"), 11.5, Theme.P.TextFaint, Theme.Semi);
        bigTitle.Margin = new Thickness(16, 0, 12, 0);
        side.Children.Add(bigTitle);
        var bigScroll = new ScrollViewer { Content = bigList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Template = Launcher.ThinScroll(), Focusable = false };
        Grid.SetRow(bigScroll, 1);
        side.Children.Add(bigScroll);
        var sideFrame = new Border { BorderBrush = Theme.P.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = side };
        Grid.SetColumn(sideFrame, 1);
        mid.Children.Add(sideFrame);
        Grid.SetRow(mid, 2);
        root.Children.Add(mid);
        Line(3);

        // bottom: what is under the mouse, how the scan goes
        var foot = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        hoverText = Theme.T("", 12, Theme.P.TextDim);
        foot.Children.Add(hoverText);
        scanText = Theme.T("", 12, Theme.P.TextFaint, Theme.Regular, false);
        scanText.HorizontalAlignment = HorizontalAlignment.Right;
        foot.Children.Add(scanText);
        Grid.SetRow(foot, 4);
        root.Children.Add(foot);
        Content = root;

        map.Hovered += n => hoverText.Text = n is null ? "" : $"{n.Path}  ·  {TreemapView.Size(Volatile.Read(ref n.Size))}" + (n.IsDir ? Tr($"  ·  파일 {n.FileCount:#,0}개", $"  ·  {n.FileCount:#,0} files") : "");
        map.Opened += Show;
        map.Menu += (n, _) => OpenMenu(n);
        tick.Tick += (_, _) => Tick();
        PreviewKeyDown += OnKey;
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) Up();
        };
        SourceInitialized += (_, _) => Frame();
        Theme.Changed += OnTheme;
        Closed += (_, _) =>
        {
            Theme.Changed -= OnTheme;
            tick.Stop();
            scan?.Stop();
        };
        Drives();
        Scan(path ?? Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
    }

    private void Line(int row)
    {
        var l = new Border { Background = Theme.P.Line };
        Grid.SetRow(l, row);
        root.Children.Add(l);
    }

    // ── scanning and moving around ──────────────────────────────────────────────────────────────
    public void Scan(string path)
    {
        if (!Directory.Exists(path)) return;
        scan?.Stop();
        scan = new SpaceScanner(path);
        scan.Start();
        Show(scan.Root);
        tick.Start();
        Drives();
    }

    private void Rescan()
    {
        if (current is not null) Scan(current.Path);
    }

    private void Show(SpaceDir node)
    {
        current = node;
        map.Root = node;
        bigAt = DateTime.MinValue;
        Crumbs();
        Tick();
    }

    /// One level out; above where the scan started, a new scan of the parent folder.
    private void Up()
    {
        if (current?.Parent is { } p) Show(p);
        else if (current is not null && Directory.GetParent(current.Path.TrimEnd('\\'))?.FullName is { } parent && current.Path.TrimEnd('\\').Length > 3) Scan(parent);
    }

    private void Tick()
    {
        if (scan is null) return;
        bool running = scan.Running;
        double secs = ((scan.Finished ?? DateTime.UtcNow) - scan.Started).TotalSeconds;
        long size = Volatile.Read(ref scan.Root.Size);
        string status = running ? Tr("읽는 중", "Scanning") : Tr("읽기 완료", "Scan complete");
        scanText.Text = scan.DeniedFolders > 0
            ? Tr($"{status} · {TreemapView.Size(size)} · 폴더 {scan.Folders:#,0}개 · {secs:0.0}초 · 읽기 실패 폴더 {scan.DeniedFolders:#,0}개", $"{status} · {TreemapView.Size(size)} · {scan.Folders:#,0} folders · {secs:0.0} s · {scan.DeniedFolders:#,0} folders unreadable")
            : Tr($"{status} · {TreemapView.Size(size)} · 폴더 {scan.Folders:#,0}개 · {secs:0.0}초", $"{status} · {TreemapView.Size(size)} · {scan.Folders:#,0} folders · {secs:0.0} s");
        map.Redraw();
        if (DateTime.UtcNow - bigAt > TimeSpan.FromSeconds(running ? 2 : 0) || !running) Big();
        if (!running) tick.Stop();
    }

    private void Crumbs()
    {
        crumbs.Children.Clear();
        if (current is null) return;
        string full = current.Path.TrimEnd('\\');
        var parts = full.Split('\\');
        string acc = "";
        for (int i = 0; i < parts.Length; i++)
        {
            acc = i == 0 ? parts[0] + "\\" : Path.Combine(acc, parts[i]);
            string target = acc;
            bool last = i == parts.Length - 1;
            var t = Theme.T(i == 0 ? parts[0] : parts[i], 14, last ? Theme.P.Text : Theme.P.TextDim, last ? Theme.Semi : Theme.Medium, false);
            var b = new Border { Child = t, Padding = new Thickness(6, 3, 6, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = last ? Cursors.Arrow : Cursors.Hand };
            if (!last)
            {
                b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
                b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
                b.MouseLeftButtonUp += (_, _) => GoTo(target);
            }
            crumbs.Children.Add(b);
            if (!last) crumbs.Children.Add(Theme.Glyph('\uE76C', 9, Theme.P.TextFaint));
        }
    }

    /// A folder on the path: inside the scan it is a step out, above it a new scan.
    private void GoTo(string path)
    {
        for (var n = current; n is not null; n = n.Parent)
            if (string.Equals(n.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Show(n);
                return;
            }
        Scan(path);
    }

    private void Drives()
    {
        drives.Children.Clear();
        foreach (var d in SpaceScanner.Drives())
        {
            double used = d.TotalSize > 0 ? 1 - (double)d.AvailableFreeSpace / d.TotalSize : 0;
            var bar = new Grid { Width = 46, Height = 4, Margin = new Thickness(0, 3, 0, 0) };
            bar.Children.Add(new Border { Background = Theme.P.Track, CornerRadius = new CornerRadius(2) });
            bar.Children.Add(new Border { Background = used > 0.9 ? Theme.P.Danger : Theme.P.Accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 46 * used });
            var sp = new StackPanel();
            sp.Children.Add(Theme.T(d.Name.TrimEnd('\\'), 12, Theme.P.Text, Theme.Semi, false));
            sp.Children.Add(bar);
            bool on = scan is not null && string.Equals(Path.GetPathRoot(scan.Root.Path), d.Name, StringComparison.OrdinalIgnoreCase);
            var chip = new Border
            {
                Child = sp,
                Padding = new Thickness(9, 4, 9, 5),
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(7),
                Background = on ? Theme.P.Selected : Theme.P.Tile,
                Cursor = Cursors.Hand,
                ToolTip = Tr($"{d.Name}  {(string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel + "  ")}남은 공간 {TreemapView.Size(d.AvailableFreeSpace)} / {TreemapView.Size(d.TotalSize)}", $"{d.Name}  {(string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel + "  ")}free {TreemapView.Size(d.AvailableFreeSpace)} / {TreemapView.Size(d.TotalSize)}"),
            };
            string name = d.Name;
            chip.MouseLeftButtonUp += (_, _) => Scan(name);
            drives.Children.Add(chip);
        }
    }

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Tr("크기를 볼 폴더", "A folder to see the size of"), InitialDirectory = current?.Path };
        if (dlg.ShowDialog(this) == true) Scan(dlg.FolderName);
    }

    // ── the largest files under what is shown ───────────────────────────────────────────────────
    private void Big()
    {
        bigAt = DateTime.UtcNow;
        bigList.Children.Clear();
        if (current is null) return;
        var top = new List<SpaceNode>();
        var stack = new Stack<SpaceDir>();
        stack.Push(current);
        int seen = 0;
        while (stack.Count > 0 && seen < 400_000)
        {
            var n = stack.Pop();
            foreach (var c in n.Children())
            {
                seen++;
                if (c is SpaceDir cd) stack.Push(cd);
                else top.Add(c);
            }
            if (top.Count > 600)
            {
                top = top.OrderByDescending(x => x.Size).Take(40).ToList();
            }
        }
        top = top.OrderByDescending(x => x.Size).Take(40).ToList();
        bigTitle.Text = Tr($"큰 파일 · {current.Name.TrimEnd('\\')} 안에서", $"Big files · in {current.Name.TrimEnd('\\')}");
        foreach (var f in top) bigList.Children.Add(BigRow(f));
    }

    private FrameworkElement BigRow(SpaceNode f)
    {
        var g = new Grid { Margin = new Thickness(8, 0, 8, 0), Height = 44, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = TreemapView.Kind(f.Name), Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(dot);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(f.Name, 12.5, Theme.P.Text, Theme.Medium));
        var where = Theme.T(Path.GetDirectoryName(f.Path) ?? "", 11, Theme.P.TextFaint);
        text.Children.Add(where);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var size = Theme.T(TreemapView.Size(f.Size), 12, Theme.P.TextDim, Theme.Medium, false);
        size.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(size, 2);
        g.Children.Add(size);
        var row = new Border { Child = g, CornerRadius = new CornerRadius(8) };
        row.MouseEnter += (_, _) => row.Background = Theme.P.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (f.Parent is { } p) Show(p);
        };
        row.MouseRightButtonUp += (_, _) => OpenMenu(f);
        row.ToolTip = f.Path;
        return row;
    }

    // ── actions ─────────────────────────────────────────────────────────────────────────────────
    private void OpenMenu(SpaceNode n)
    {
        var items = new List<(string label, char glyph, bool danger, Action run)>();
        if (n is SpaceDir nd) items.Add((Tr("이 폴더로 들어가기", "Go into this folder"), '\uE8A7', false, () => Show(nd)));
        items.Add((n.IsDir ? Tr("폴더 열기", "Open folder") : Tr("열기", "Open"), G.NewWindow, false, () => Launch.Open(n.Path)));
        items.Add((Tr("탐색기에서 보기", "Show in Explorer"), G.Folder, false, () => Launch.Reveal(n.Path)));
        items.Add((Tr("경로 복사", "Copy path"), G.Copy, false, () => Launch.Copy(n.Path)));
        if (Deletable(n.Path)) items.Add((Tr("휴지통으로 보내기…", "Move to the recycle bin…"), G.Delete, true, () => AskDelete(n)));
        var panel = new StackPanel { Margin = new Thickness(4) };
        var head = Theme.T(n.Name + "  ·  " + TreemapView.Size(Volatile.Read(ref n.Size)), 11.5, Theme.P.TextFaint, Theme.Semi);
        head.Margin = new Thickness(10, 6, 10, 6);
        head.MaxWidth = 300;
        panel.Children.Add(head);
        foreach (var (label, glyph, danger, run) in items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var gl = Theme.Glyph(glyph, 13, danger ? Theme.P.Danger : Theme.P.TextDim);
            gl.Margin = new Thickness(0, 0, 10, 0);
            sp.Children.Add(gl);
            sp.Children.Add(Theme.T(label, 13, danger ? Theme.P.Danger : Theme.P.Text, Theme.Medium, false));
            var b = new Border { Child = sp, Padding = new Thickness(10, 7, 14, 7), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, _) =>
            {
                menu.IsOpen = false;
                run();
            };
            panel.Children.Add(b);
        }
        menu.Child = new Border { Child = panel, Background = Theme.P.Popup, BorderBrush = Theme.P.PopupLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), MinWidth = 220 };
        menu.IsOpen = true;
    }

    /// Never a drive root, Windows' own folders, or a profile / AppData folder itself.
    private static bool Deletable(string path)
    {
        string p = path.TrimEnd('\\');
        if (p.Length <= 3) return false;
        string[] keep =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Path.GetPathRoot(p) ?? "", "Users"),
        };
        foreach (string k in keep)
            if (k.Length > 0 && string.Equals(p, k.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
        // inside Windows or Program Files: never (apps and Windows break); inside the profile: the user's call
        foreach (string k in keep.Take(3))
            if (k.Length > 0 && p.StartsWith(k.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void AskDelete(SpaceNode n)
    {
        long size = Volatile.Read(ref n.Size);
        string what = n.IsDir ? Tr($"‘{n.Name}’ 폴더 (파일 {n.FileCount:#,0}개, {TreemapView.Size(size)})", $"The folder ‘{n.Name}’ ({n.FileCount:#,0} files, {TreemapView.Size(size)})") : $"‘{n.Name}’ ({TreemapView.Size(size)})";
        Ask(Tr($"{what} — 휴지통 이동", $"{what} — move to the Recycle Bin"), Tr("휴지통에서 복원할 수 있습니다. 크기가 너무 커서 휴지통에 넣을 수 없으면 Windows 확인 창이 표시됩니다.", "Items can be restored from the Recycle Bin. Windows shows another confirmation if they are too large for it."), Tr("휴지통으로", "Move to bin"), () => Delete(n, size));
    }

    /// A question over the window (Enter = yes, Esc = no), in the window's own look.
    private void Ask(string question, string note, string yes, Action run)
    {
        var dim = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) };
        Grid.SetRowSpan(dim, 5);
        var card = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };
        var q = Theme.T(Core.Hangul.KeepAll(question), 14, Theme.P.Text, Theme.Semi, false);
        q.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(q);
        var n2 = Theme.T(Core.Hangul.KeepAll(note), 12, Theme.P.TextFaint, Theme.Regular, false);
        n2.TextWrapping = TextWrapping.Wrap;
        n2.Margin = new Thickness(0, 8, 0, 16);
        card.Children.Add(n2);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Done(bool ok)
        {
            root.Children.Remove(dim);
            PreviewKeyDown -= Keys;
            asking = false;
            if (ok) run();
        }
        void Keys(object s, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Done(e.Key == Key.Enter);
                e.Handled = true;
            }
        }
        var no = TextButton(Tr("취소", "Cancel"), () => Done(false));
        var go = TextButton(yes, () => Done(true));
        go.Background = Theme.P.Danger;
        ((TextBlock)go.Child).Foreground = Brushes.White;
        go.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(no);
        buttons.Children.Add(go);
        card.Children.Add(buttons);
        dim.Children.Add(new Border { Child = card, Background = Theme.P.Popup, BorderBrush = Theme.P.PopupLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(dim);
        asking = true;
        PreviewKeyDown += Keys;
    }

    private void Delete(SpaceNode n, long size)
    {
        if (!Recycle(n.Path))
        {
            hoverText.Text = Tr("휴지통 이동 실패 · 파일을 사용하는 앱을 닫고 권한을 확인한 뒤 다시 시도하세요", "Could not move to the Recycle Bin. Close apps using the file, check permissions and retry.");
            return;
        }
        // the map forgets it at once
        n.Parent?.Remove(n);
        n.Parent?.Grow(-size, -n.FileCount);
        if (current == n || IsUnder(current, n)) Show(n.Parent ?? scan!.Root);
        else
        {
            map.Redraw();
            Big();
        }
        hoverText.Text = Tr($"휴지통 이동 완료: {n.Path}", $"Moved to the recycle bin: {n.Path}");
        Drives();
    }

    private static bool IsUnder(SpaceNode? node, SpaceNode ancestor)
    {
        for (var x = node; x is not null; x = x.Parent)
            if (x == ancestor) return true;
        return false;
    }

    private bool Recycle(string path) => Bloom.Native.ShellFiles.Recycle(path, new WindowInteropHelper(this).Handle);

    // ── chrome ──────────────────────────────────────────────────────────────────────────────────
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (asking) return;
        switch (e.Key)
        {
            case Key.Back:
                Up();
                break;
            case Key.F5:
                Rescan();
                break;
            case Key.Escape:
                if (menu.IsOpen) menu.IsOpen = false;
                else Close();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private static Border Button(object glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = Theme.P.Tile,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Theme.Glyph(glyph is char c ? c : ((string)glyph)[0], 13, Theme.P.TextDim),
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Selected;
        b.MouseLeave += (_, _) => b.Background = Theme.P.Tile;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    private static Border TextButton(string label, Action click)
    {
        var b = new Border
        {
            Padding = new Thickness(11, 6, 11, 7),
            CornerRadius = new CornerRadius(7),
            Background = Theme.P.Tile,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Theme.T(label, 12.5, Theme.P.Text, Theme.Medium, false),
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Selected;
        b.MouseLeave += (_, _) => b.Background = Theme.P.Tile;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    private void OnTheme()
    {
        Frame();
        root.Background = Background = Theme.P.Solid;
        Drives();
        Crumbs();
        map.Redraw();
        Big();
    }

    private void Frame() => Shots.SolidCaption(this);


    public void Capture(string file) => Shots.Save(root, file);

    public bool Scanning => scan?.Running == true;

    /// QA: into the largest folder shown, as a click would.
    public void EnterLargestForQa()
    {
        if (current?.Children().OfType<SpaceDir>().FirstOrDefault() is { } d) Show(d);
    }

    public string StatusForQa => scanText.Text;

    public WeakReference? RootForQa => scan is null ? null : new WeakReference(scan.Root);
}
