using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// A Markdown file (.md) in a window of its own (Explorer's 연결 프로그램 through `--open`, the search's Bloom으로 보기,
/// a link in another document): an ordinary window — the taskbar, Alt+Tab, Windows' snapping, maximize (with Windows
/// 11's snap layouts on its button) — with the tool windows' glass and head. One window per file. It opens drawn (보기);
/// what is written in 편집 or ticked in 보기 goes to the file 0.8 s after the last change, on closing and when Windows
/// signs out (DocFile: its encoding and line endings kept). Another program's change comes in by itself while nothing
/// here waits to be saved; otherwise a bar asks which to keep. A file that can't be written shows read-only and why.
internal sealed class DocWindow : Window
{
    private static readonly Dictionary<string, DocWindow> open = new(StringComparer.OrdinalIgnoreCase);

    /// A document window closed (the launcher's memory trim follows).
    public static event Action? Gone;

    /// QA: new windows open off-screen and unactivated (pictures with no window on the user's screens).
    internal static bool OffScreenForQa;

    public string FilePath { get; }
    private readonly DocFile file;
    private readonly SmoothBorder frame, bar, maxButton;
    private readonly Grid body;
    private readonly Border modeSlot = new();
    private readonly TextBlock status, barText;
    private readonly SmoothBorder reloadButton;
    private readonly Saver saver;
    private readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromSeconds(1) };
    private MdPane pane;
    private DocText shown;
    private int edits, savedEdits; // changes made here, and how many of them the file has
    private int letGo = -1, rescued = -1; // the edits 저장 안 함 let go of, and the ones last kept in a recovery copy
    private bool paused, force, quitting, maxHover, maxPressed;
    private string barFor = ""; // "" | "changed" (another program's change) | "gone" (deleted or moved)

    // the caption buttons' glyphs (Segoe Fluent Icons)
    private const char Minimize = '\uE921', Maximize = '\uE922', Restore = '\uE923';

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// QA: what a picture draws of it.
    public SmoothBorder FrameForQa => frame;

    /// QA: its pane (보기 / 편집).
    public MdPane PaneForQa => pane;

    private DocWindow(string path, int cascade)
    {
        FilePath = path;
        string name = Path.GetFileName(path);
        Title = name + " - Bloom";
        Shots.TrackIcon(this);
        int[] size = Settings.Current.DocSize is { Length: 2 } kept ? kept : new Settings().DocSize; // Settings owns the default
        Width = Math.Clamp(size[0], 360, 4000);
        Height = Math.Clamp(size[1], 240, 4000);
        MinWidth = 360;
        MinHeight = 240;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (OffScreenForQa)
        {
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
        }
        GlassFrame.Prepare(this);

        file = new DocFile(path);
        shown = file.Read();
        pane = NewPane(shown);

        // head: the document's glyph (the full path on it) and name, 보기 / 편집, its folder, minimize, maximize, close
        var buttons = new StackPanel();
        buttons.Children.Add(modeSlot);
        WindowChrome.SetIsHitTestVisibleInChrome(modeSlot, true);
        buttons.Children.Add(GlassFrame.HeadButton(G.Folder, Tr("폴더에서 보기", "Show in folder"), () => Launch.Reveal(FilePath)));
        buttons.Children.Add(GlassFrame.HeadButton(Minimize, Tr("최소화", "Minimize"), () => WindowState = WindowState.Minimized));
        maxButton = GlassFrame.HeadButton(Maximize, Tr("최대화", "Maximize"), ToggleMaximize);
        buttons.Children.Add(maxButton);
        buttons.Children.Add(GlassFrame.HeadButton(G.Close, Tr("닫기", "Close"), Close));
        var head = GlassFrame.Head(G.Page, name, buttons, out var title);
        // the name stays the caption (dragged, snapped, double-clicked like any title bar); the glyph tells the path
        var mark = ((Panel)title).Children[0];
        ((FrameworkElement)mark).ToolTip = path;
        WindowChrome.SetIsHitTestVisibleInChrome(mark, true);

        // the body: the bar for another program's change, the text, the line saying how it is kept
        body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        barText = Theme.T("", 12.5, Theme.P.Text, Theme.Medium);
        barText.VerticalAlignment = VerticalAlignment.Center;
        reloadButton = ToolUi.TextButton(Tr("다시 읽기", "Reload"), Reload);
        reloadButton.Margin = new Thickness(10, 0, 0, 0);
        var mine = ToolUi.TextButton(Tr("고친 내용 저장", "Save mine"), SaveMine);
        mine.Margin = new Thickness(6, 0, 0, 0);
        var barRow = new DockPanel();
        var warn = Theme.Glyph(G.Warning, 13, Theme.P.Accent);
        warn.Margin = new Thickness(0, 0, 8, 0);
        warn.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(warn, Dock.Left);
        barRow.Children.Add(warn);
        var barButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        barButtons.Children.Add(reloadButton);
        barButtons.Children.Add(mine);
        DockPanel.SetDock(barButtons, Dock.Right);
        barRow.Children.Add(barButtons);
        barRow.Children.Add(barText);
        bar = new SmoothBorder
        {
            Child = barRow,
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 7, 8, 7),
            Margin = new Thickness(10, 8, 10, 2),
            Visibility = Visibility.Collapsed,
        };
        body.Children.Add(bar);
        status = Theme.T("", 11.5, Theme.P.TextFaint);
        status.Margin = new Thickness(14, 2, 14, 8);
        Grid.SetRow(status, 2);
        body.Children.Add(status);
        Put(pane);

        frame = GlassFrame.Frame(head, body);
        Content = frame;
        Theme.Scaled(this, frame); // 설정 > 일반 > 크기: the content and the window's size

        saver = Saver.ForUi(Snapshot, 800);
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            Outside();
        };
        file.Touched += () => Dispatcher.BeginInvoke(Outside);
        file.Watch();
        ShowKept(null);

        SourceInitialized += (_, _) =>
        {
            // no system menu: over the glass DWM would paint its own caption buttons beside ours (Glass.ToolWindow);
            // the minimize and maximize boxes and the sizing frame stay, which Windows' snapping goes by
            const int GWL_STYLE = -16;
            const long WS_SYSMENU = 0x80000;
            Win32.SetWindowLongPtr(Handle, GWL_STYLE, new IntPtr(Win32.GetWindowLongPtr(Handle, GWL_STYLE).ToInt64() & ~WS_SYSMENU));
            Win32.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
            Paint();
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc); // after WindowChrome's: asked first
            if (!OffScreenForQa) Place(cascade);
            Glass.CloakFirstFrame(this);
        };
        StateChanged += (_, _) => Fit();
        SizeChanged += (_, _) => Fit();
        LocationChanged += (_, _) => Fit();
        Activated += (_, _) =>
        {
            if (!IsKeyboardFocusWithin) Dispatcher.BeginInvoke(() => pane.KeyTarget.Focus(), DispatcherPriority.Input);
        };
        Closing += (_, e) => e.Cancel = !LetGo();
        Theme.Changed += Paint;
        Closed += (_, _) =>
        {
            Theme.Changed -= Paint;
            retry.Stop();
            saver.Dispose();
            file.Dispose();
            open.Remove(FilePath);
            Gone?.Invoke();
        };
    }

    /// The file's window, made or brought forward (out of the taskbar if it was down there).
    public static DocWindow Show(string path)
    {
        string full = Path.GetFullPath(path);
        if (!open.TryGetValue(full, out var w))
        {
            w = new DocWindow(full, open.Count);
            open[full] = w;
            w.Show();
        }
        else if (w.WindowState == WindowState.Minimized) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE); // maximized again if it was
        if (OffScreenForQa) return w;
        w.Activate();
        Win32.ForceForeground(w.Handle);
        return w;
    }

    /// Windows signing out, Bloom quitting: what waits goes to the files now.
    public static void FlushAll()
    {
        foreach (var w in open.Values.ToList()) w.saver.Flush();
    }

    /// Bloom about to quit, restart, update or reset on the user's word: each window whose edits still aren't in its file
    /// (the bar asks, the file is held or gone, a save failed) comes forward and asks — 고친 내용 저장 / 저장 안 함 / 취소.
    /// False: 취소, or 고친 내용 저장 still couldn't write (its line says why) — nothing quits.
    public static bool SettleAll()
    {
        foreach (var w in open.Values.ToList())
        {
            w.saver.Flush();
            if (!w.Unwritten) continue;
            if (w.WindowState == WindowState.Minimized) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE);
            w.Activate();
            if (!w.Settle()) return false;
        }
        return true;
    }

    /// Bloom ending with no one to ask (Windows signing out, `--quit`, after the asking above): what still isn't in a
    /// file goes to a copy beside it (or the data folder's recovered), and the log says where — never dropped silently.
    public static void RescueAll()
    {
        foreach (var w in open.Values.ToList())
        {
            w.saver.Flush();
            if (!w.Unwritten || w.rescued == w.edits) continue;
            string? copy = w.file.Rescue(w.pane.Text, Path.Combine(Paths.Data, "recovered"), DateTime.Now);
            w.rescued = w.edits;
            Log.Write(copy is null ? "doc: NOT kept anywhere " + w.FilePath : "doc: kept in " + copy + " (" + w.FilePath + ")");
        }
    }

    /// Edits made here that the file doesn't have, and that 저장 안 함 didn't let go of.
    private bool Unwritten => edits != savedEdits && edits != letGo && shown.ReadOnly is null;

    /// Bloom quits: the windows close without asking (FlushAll has saved what could be).
    public static void Quitting()
    {
        foreach (var w in open.Values.ToList()) w.quitting = true;
    }

    // ── the text and the file ───────────────────────────────────────────────────────────────────

    private MdPane NewPane(DocText t)
    {
        var p = new MdPane(t.Text, !t.Large, Path.GetDirectoryName(FilePath), t.ReadOnly is not null, true) { OpenLink = Follow };
        p.Edited += _ => Typed();
        return p;
    }

    /// The pane in the body, its 보기 / 편집 in the head.
    private void Put(MdPane p)
    {
        body.Children.Remove(pane);
        pane = p;
        Grid.SetRow(p, 1);
        body.Children.Insert(0, p);
        modeSlot.Child = p.ToggleButton;
    }

    private void Typed()
    {
        edits++;
        if (!paused) saver.Touch();
    }

    /// The text to write now and its write (the worker's, or this thread's for a flush); nothing while nothing waits, while
    /// the bar asks (unless 고친 내용 저장 answered it) or for a read-only file.
    private Action Snapshot()
    {
        bool mine = force;
        force = false;
        if (shown.ReadOnly is not null || (paused && !mine) || (edits == savedEdits && !mine)) return () => { };
        string text = pane.Text;
        int at = edits;
        return () =>
        {
            var (result, reason) = file.Save(text, mine);
            if (Dispatcher.CheckAccess()) Saved(result, reason, at);
            else Dispatcher.BeginInvoke(() => Saved(result, reason, at));
        };
    }

    private void Saved(DocSaved result, string? reason, int at)
    {
        switch (result)
        {
            case DocSaved.Saved:
                savedEdits = Math.Max(savedEdits, at);
                if (paused) Ask("");
                ShowKept(DateTime.Now);
                break;
            case DocSaved.Conflict:
                Ask("changed");
                break;
            case DocSaved.Gone:
                Ask("gone");
                break;
            case DocSaved.Busy:
                status.Text = Tr("저장 못 함 · 다른 프로그램이 파일을 쓰는 중 · 곧 다시 저장", "Not saved · another program is using the file, trying again");
                saver.Touch();
                break;
            default:
                status.Text = Tr("저장 못 함", "Not saved") + " · " + reason;
                break;
        }
    }

    /// Another program changed, moved or deleted the file (or a save of ours came back): what the disk holds now.
    private void Outside()
    {
        var (state, now) = file.Peek();
        switch (state)
        {
            case DocState.Same:
                if (barFor.Length > 0)
                {
                    Ask(""); // back as it was known here
                    if (edits != savedEdits) saver.Touch();
                }
                break;
            case DocState.Changed when edits == savedEdits && barFor != "changed":
                Take(now!); // nothing here waits: it comes in, scrolled where it was
                if (barFor.Length > 0) Ask("");
                break;
            case DocState.Changed:
                Ask("changed");
                break;
            case DocState.Gone:
                Ask("gone");
                break;
            default:
                retry.Start(); // held by another program: looked at again in a second
                break;
        }
    }

    /// The text the disk holds becomes the one shown and known (a read-only file now writable, or the other way round, gets
    /// a new pane).
    private void Take(DocText t)
    {
        file.Adopt(t);
        bool same = (t.ReadOnly is null) == (shown.ReadOnly is null) && t.Large == shown.Large;
        shown = t;
        savedEdits = edits;
        if (same) pane.Text = t.Text;
        else
        {
            bool view = pane.InView;
            var p = NewPane(t);
            p.InView = view && !t.Large;
            Put(p);
        }
        ShowKept(null);
    }

    /// 다시 읽기: the file as it is now in place of what was written here.
    private void Reload()
    {
        var t = file.Read();
        if (t.Hash.Length == 0) return; // couldn't be read: the bar stays
        Ask("");
        Take(t);
    }

    /// 고친 내용 저장: what is written here goes to the file, over another program's change or where the file was.
    private void SaveMine()
    {
        force = true;
        saver.Flush();
    }

    /// The bar for `why` ("changed", "gone"; "" hides it): autosave waits while it shows.
    private void Ask(string why)
    {
        barFor = why;
        paused = why.Length > 0;
        bar.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        reloadButton.Visibility = why == "changed" ? Visibility.Visible : Visibility.Collapsed;
        barText.Text = why == "gone"
            ? Tr("파일이 삭제되었거나 이동됨 · 글은 여기에 그대로 있음", "The file was deleted or moved · the text is still here")
            : Tr("다른 프로그램이 이 파일을 바꿈", "Another program changed this file");
        barText.ToolTip = barText.Text;
    }

    /// The line under the text: read-only and why, or kept (`at`: just saved; null: as the file was last written).
    private void ShowKept(DateTime? at)
    {
        if (shown.ReadOnly is { } why)
        {
            status.Text = Tr("보기 전용", "View only") + " · " + why;
            return;
        }
        DateTime when = at ?? (File.Exists(FilePath) ? File.GetLastWriteTime(FilePath) : DateTime.Now);
        status.Text = Tr($"자동 저장 · {ToolUi.Ago(when)}", $"Saved as you type · {ToolUi.Ago(when)}");
    }

    /// Closing: what waits is written; what can't be (the bar asks, the file is held, no permission) is asked about.
    private bool LetGo()
    {
        if (!quitting && !Settle()) return false;
        // the size the next document opens at
        var r = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;
        if (r.Width > 0 && r.Height > 0)
        {
            Settings.Current.DocSize = new[] { (int)Math.Round(r.Width / Theme.Scale), (int)Math.Round(r.Height / Theme.Scale) };
            Settings.Save();
        }
        return true;
    }

    /// What waits is written; edits that still can't be are asked about: 고친 내용 저장 (true once written; false while it
    /// still can't be, the line under the text says why), 저장 안 함 (let go), 취소 (false).
    private bool Settle()
    {
        saver.Flush();
        if (!Unwritten) return true;
        var answer = ConfirmWindow.Ask(this, Tr("저장하지 않은 내용", "Unsaved changes"),
            Tr("고친 내용이 아직 파일에 저장되지 않았습니다. 고친 내용을 저장하면 파일이 이 글로 바뀝니다.", "What you changed isn't in the file yet. Saving yours puts this text in the file."),
            (Tr("고친 내용 저장", "Save mine"), MessageBoxResult.OK), (Tr("저장 안 함", "Don't save"), MessageBoxResult.No), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.OK)
        {
            SaveMine();
            return !Unwritten;
        }
        letGo = edits;
        return true;
    }

    private void Follow(string url) => FollowLink(url, Path.GetDirectoryName(FilePath), pane);

    /// A link clicked in a document's or a note's 보기 (`baseDir`: where relative links start; null for a note): a web
    /// address in the browser, a Markdown file in its document window, another file or folder shown in its folder (a
    /// written link never runs a program); one that isn't there says so on `toast`. A network target is looked for off
    /// the UI thread (a share that doesn't answer would hold it).
    public static async void FollowLink(string url, string? baseDir, IComposerHost toast)
    {
        var (kind, target) = DocLink.Resolve(url, baseDir);
        if (kind == DocLink.Kind.None) return;
        if (kind == DocLink.Kind.Web)
        {
            Browser.Web.Open(target);
            return;
        }
        static (bool file, bool folder) Look(string p) => File.Exists(p) ? (true, false) : (false, Directory.Exists(p));
        var (file, folder) = DocLink.IsNetwork(target) ? await System.Threading.Tasks.Task.Run(() => Look(target)) : Look(target);
        if (kind == DocLink.Kind.Doc && file) Show(target);
        else if (file || folder) Launch.Reveal(target);
        else toast.ShowToast(Tr($"‘{Path.GetFileName(target)}’ 파일 없음", $"‘{Path.GetFileName(target)}’ isn't there"));
    }

    // ── the window ──────────────────────────────────────────────────────────────────────────────

    private void Paint() => GlassFrame.Paint(this, frame);

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// Maximized, Windows puts the window's edges (its resize border) past the screen's: the frame keeps inside the work
    /// area. The maximize button shows maximize or restore.
    private void Fit()
    {
        bool max = WindowState == WindowState.Maximized;
        ((TextBlock)maxButton.Child).Text = (max ? Restore : Maximize).ToString();
        maxButton.ToolTip = max ? Tr("이전 크기로", "Restore") : Tr("최대화", "Maximize");
        var spill = new Thickness();
        IntPtr h = Handle;
        if (max && h != IntPtr.Zero && Win32.GetWindowRect(h, out var r))
        {
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(Win32.MonitorFromWindow(h, Win32.MONITOR_DEFAULTTONEAREST), ref info))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var work = info.rcWork;
                spill = new Thickness(Math.Max(0, work.Left - r.Left) / dpi.DpiScaleX, Math.Max(0, work.Top - r.Top) / dpi.DpiScaleY,
                    Math.Max(0, r.Right - work.Right) / dpi.DpiScaleX, Math.Max(0, r.Bottom - work.Bottom) / dpi.DpiScaleY);
            }
        }
        if (frame.Margin != spill) frame.Margin = spill;
    }

    /// Centred on the screen under the mouse; each window opened while others are open a step further down and right.
    private void Place(int cascade)
    {
        IntPtr h = Handle;
        Win32.GetCursorPos(out var c);
        IntPtr mon = Win32.MonitorFromPoint(c, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(mon, ref info)) return;
        var work = info.rcWork;
        double k = Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
        int step = (int)Math.Round(28 * k) * (cascade % 8);
        // twice: the first move may cross into another DPI, and the window's size follows it
        for (int pass = 0; pass < 2; pass++)
        {
            Win32.GetWindowRect(h, out var r);
            int w = Math.Min(r.Right - r.Left, work.Right - work.Left), ht = Math.Min(r.Bottom - r.Top, work.Bottom - work.Top);
            int x = Math.Min(work.Left + (work.Right - work.Left - w) / 2 + step, work.Right - w);
            int y = Math.Min(work.Top + (work.Bottom - work.Top - ht) / 2 + step, work.Bottom - ht);
            Win32.SetWindowPos(h, IntPtr.Zero, x, y, w, ht, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
    }

    /// The maximize button answers Windows' hit test as one (HTMAXBUTTON: Windows 11 shows its snap layouts over it), so
    /// its hover and click come as non-client mouse messages. A click is a press and a release both on it: a press that
    /// ends or moves elsewhere is no click.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        bool onMax = wParam.ToInt64() == Win32.HTMAXBUTTON; // not ToInt32: a 64-bit wParam (a wheel turned down) overflows it
        switch (msg)
        {
            case Win32.WM_NCHITTEST when OverMax(lParam):
                handled = true;
                return new IntPtr(Win32.HTMAXBUTTON);
            case Win32.WM_NCMOUSEMOVE:
                MaxHover(onMax);
                if (!onMax) maxPressed = false;
                break;
            case Win32.WM_NCMOUSELEAVE:
                MaxHover(false);
                maxPressed = false;
                break;
            case Win32.WM_NCLBUTTONDOWN:
                maxPressed = onMax;
                if (!onMax) break;
                handled = true; // not Windows' own button tracking
                return IntPtr.Zero;
            case Win32.WM_NCLBUTTONUP:
                bool click = onMax && maxPressed;
                maxPressed = false;
                if (!onMax) break;
                handled = true;
                MaxHover(false);
                if (click) ToggleMaximize();
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    /// The point of a WM_NCHITTEST (screen pixels) is on the maximize button.
    private bool OverMax(IntPtr lParam)
    {
        if (!maxButton.IsVisible) return false;
        long v = lParam.ToInt64();
        var p = new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        var a = maxButton.PointToScreen(new Point(0, 0));
        var b = maxButton.PointToScreen(new Point(maxButton.ActualWidth, maxButton.ActualHeight));
        return p.X >= Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
    }

    private void MaxHover(bool on)
    {
        if (on && !maxHover)
        {
            var track = new Win32.TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE | Win32.TME_NONCLIENT, hwndTrack = Handle };
            Win32.TrackMouseEvent(ref track); // WM_NCMOUSELEAVE once the mouse goes
        }
        maxHover = on;
        maxButton.Background = on ? Theme.P.Hover : Brushes.Transparent;
    }

    /// QA: what Windows' hit test answers at the maximize button's middle (HTMAXBUTTON expected).
    internal int MaxHitForQa()
    {
        var m = maxButton.PointToScreen(new Point(maxButton.ActualWidth / 2, maxButton.ActualHeight / 2));
        IntPtr lp = new(((int)m.Y << 16) | ((int)m.X & 0xFFFF));
        return Win32.SendMessage(Handle, (uint)Win32.WM_NCHITTEST, IntPtr.Zero, lp).ToInt32();
    }

    /// QA: as if another program had changed the file (the watcher's look, now).
    internal void OutsideForQa() => Outside();

    /// QA: 다시 읽기.
    internal void ReloadForQa() => Reload();

    /// QA: the window's margin while maximized (what keeps it inside the screen).
    internal Thickness SpillForQa => frame.Margin;

    /// QA: as if typed.
    internal void TypeForQa(string text)
    {
        pane.Text = text;
        Typed();
    }
}
