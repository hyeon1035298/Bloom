using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// The tool windows (메모 · 타이머 · 계산기 · 클립보드): which are open, which are attached to the launcher and where,
/// and the group they make with it. A tool opened while nothing was put by hand joins a column on the launcher's
/// right (the launcher's height shared); once a tool is dragged or sized, every attached one keeps the place it has
/// (free: an offset from the launcher, in dip at 100 %). Attached tools show and hide with the launcher and keep it
/// open while you work in them (it isn't pinned for that: a click outside the group lets both go); floating ones live
/// on their own, not on top of other windows until their 항상 위 is switched on. Settings.Tools remembers them across
/// starts.
internal sealed class ToolDock
{
    private readonly Host host;
    private readonly List<ToolWindow> windows = new(); // in order along each edge

    /// The tools, as the launcher's 도구 menu (Ctrl+J) lists them.
    public static readonly (string key, string name, char glyph)[] Kinds =
    {
        ("notes", Tr("메모", "Notes"), G.Document),
        ("timers", Tr("타이머 · 알림", "Timers"), G.Stopwatch),
        ("calc", Tr("계산기", "Calculator"), G.Calculator),
        ("clipboard", Tr("클립보드", "Clipboard"), G.Paste),
        ("keys", Tr("단축키", "Shortcuts"), G.Keyboard),
        ("player", Tr("미니 플레이어", "Mini player"), Music.PlayerGlyph.Music),
    };

    public ToolDock(Host host)
    {
        this.host = host;
        Theme.ScaleChanged += (from, to) =>
        {
            // floating ones grow or shrink from their top-left corner; attached ones take the launcher's new size
            foreach (var w in windows.Where(w => w.Edge is null && w.Handle != IntPtr.Zero))
            {
                var r = w.Rect;
                w.PlaceAt(r.Left, r.Top, (int)((r.Right - r.Left) * to / from), (int)((r.Bottom - r.Top) * to / from));
            }
            Layout();
            Save();
        };
    }

    private Launcher L => host.Window;

    /// QA: the open tool windows, in order.
    public IReadOnlyList<ToolWindow> WindowsForQa => windows;

    public bool IsOpen(string key) => windows.Exists(w => w.Tool.Key == key);

    public ToolWindow? Find(string key) => windows.Find(w => w.Tool.Key == key);

    private ITool Make(string key, string? state) => key switch
    {
        "notes" => new NotesTool(state),
        "timers" => new TimerTool(),
        "calc" => new CalcTool(),
        "keys" => new KeysTool(host),
        "player" => new PlayerTool(),
        _ => new ClipTool(host),
    };

    /// The 도구 menu: open one beside the launcher, or close it.
    public void Toggle(string key)
    {
        if (Find(key) is { } w) Close(w);
        else Open(key);
    }

    /// Opens a tool (attached beside the launcher when it shows, else floating near the middle) and puts the keyboard
    /// in it; an open one comes forward. 메모 with a note shows that note.
    public void Open(string key, string? note = null)
    {
        if (Find(key) is { } open)
        {
            if (note is not null && open.Tool is NotesTool notes) notes.Show(note);
            Front(open);
            return;
        }
        var w = new ToolWindow(this, Make(key, note), null, false);
        windows.Add(w);
        if (L.IsVisible) AttachNew(w);
        else Float(w, null);
        Front(w);
        Save();
    }

    /// For the AI: a tool brought up beside where the user chats — attached to the launcher when it shows, else against
    /// the AI 채팅 window's right edge (`chat`), else near the middle — without taking the keyboard from the chat.
    /// 메모 with a note shows that note.
    public void ShowBeside(string key, string? note, IntPtr chat)
    {
        if (Find(key) is { } open)
        {
            if (note is not null && open.Tool is NotesTool notes) notes.Show(note);
            if (!open.IsVisible && (open.Edge is null || L.IsVisible)) open.Show();
            return;
        }
        var w = new ToolWindow(this, Make(key, note), null, false);
        windows.Add(w);
        if (L.IsVisible) AttachNew(w);
        else if (chat != IntPtr.Zero && Win32.GetWindowRect(chat, out var cr))
        {
            new WindowInteropHelper(w).EnsureHandle();
            double dpi = Dpi(chat), k = dpi * Theme.Scale;
            int gap = (int)Math.Round(8 * dpi), width = (int)(300 * k), height = Math.Min(cr.Bottom - cr.Top, (int)(520 * k));
            var wa = WorkArea((cr.Left + cr.Right) / 2, (cr.Top + cr.Bottom) / 2);
            int x = cr.Right + gap + width <= wa.Right ? cr.Right + gap : Math.Max(wa.Left, cr.Left - gap - width);
            Float(w, new ToolPlace { X = x, Y = cr.Top, W = width, H = height });
        }
        else Float(w, null);
        if (!w.IsVisible) w.Show();
        Save();
    }

    private static void Front(ToolWindow w)
    {
        if (!w.IsVisible) w.Show();
        w.Activate();
        w.Tool.FocusFirst();
    }

    public void Close(ToolWindow w)
    {
        windows.Remove(w);
        w.Close();
        Layout();
        Save();
        if (L.IsVisible) L.Activate();
    }

    /// The attach button on a tool's head: attached → floating beside where it was; floating → beside the launcher.
    public void ToggleAttach(ToolWindow w)
    {
        if (w.Edge is not null)
        {
            var r = w.Rect;
            Detach(w);
            int step = (int)(24 * Dpi(w.Handle));
            Float(w, new ToolPlace { X = r.Left + step, Y = r.Top + step, W = r.Right - r.Left, H = r.Bottom - r.Top });
        }
        else
        {
            if (!L.IsVisible) host.Summon(); // the launcher comes, then the tool joins it
            AttachNew(w);
        }
        Save();
    }

    private void Attach(ToolWindow w, string edge)
    {
        w.SetEdge(edge);
        w.Topmost = true;
        Layout();
    }

    private void Detach(ToolWindow w)
    {
        w.SetEdge(null);
        w.Topmost = w.OnTop;
        Layout();
    }

    /// A tool joining the launcher: in the column on its right while every attached tool is laid out there; once they
    /// were put by hand, in the first open spot beside the launcher (right, then left, top down) at 300 × 360, or
    /// floating when there is none.
    private void AttachNew(ToolWindow w)
    {
        if (!windows.Exists(o => o != w && o.Edge == "free"))
        {
            Attach(w, "right");
            return;
        }
        new WindowInteropHelper(w).EnsureHandle();
        Win32.GetWindowRect(L.Handle, out var lr);
        double dpi = Dpi(L.Handle), k = dpi * Theme.Scale;
        int gap = (int)Math.Round(8 * dpi), width = (int)(300 * k), height = Math.Min((int)(360 * k), lr.Bottom - lr.Top);
        var wa = WorkArea((lr.Left + lr.Right) / 2, (lr.Top + lr.Bottom) / 2);
        var taken = windows.Where(o => o != w && o.Edge is not null && o.IsVisible).Select(o => o.Rect).Append(lr).ToList();
        bool Open(Win32.RECT c) => c.Left >= wa.Left && c.Right <= wa.Right && c.Top >= wa.Top && c.Bottom <= wa.Bottom
            && !taken.Exists(o => c.Left < o.Right && c.Right > o.Left && c.Top < o.Bottom && c.Bottom > o.Top);
        foreach (int x in new[] { lr.Right + gap, lr.Left - gap - width })
            foreach (int y in taken.Select(o => o.Bottom + gap).Prepend(lr.Top).OrderBy(v => v))
            {
                var c = new Win32.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
                if (!Open(c)) continue;
                w.SetEdge("free");
                w.Topmost = true;
                Pin(w, c);
                Layout();
                return;
            }
        Float(w, null);
    }

    /// Attached where it is: its place and size kept as an offset from the launcher (dip at 100 %).
    private void Pin(ToolWindow w, Win32.RECT r)
    {
        Win32.GetWindowRect(L.Handle, out var lr);
        double k = Dpi(L.Handle) * Theme.Scale;
        w.SetEdge("free");
        w.Spot = new ToolPlace
        {
            X = (int)Math.Round((r.Left - lr.Left) / k),
            Y = (int)Math.Round((r.Top - lr.Top) / k),
            W = (int)Math.Round((r.Right - r.Left) / k),
            H = (int)Math.Round((r.Bottom - r.Top) / k),
        };
    }

    /// A drag or a resize begins: the tools laid out along an edge keep the place they have now as their own, so
    /// nothing else moves while one is put somewhere.
    public void Freeze()
    {
        if (!L.IsVisible) return;
        foreach (var w in windows.Where(w => w.Edge is "right" or "left" or "bottom" && w.IsVisible).ToList()) Pin(w, w.Rect);
    }

    /// A floating tool at its kept place (on a screen that is still there), else near the middle of the screen.
    private static void Float(ToolWindow w, ToolPlace? at)
    {
        new WindowInteropHelper(w).EnsureHandle();
        w.Topmost = w.OnTop;
        double k = Dpi(w.Handle) * Theme.Scale;
        int width = at is { W: > 0 } ? at.W : (int)(300 * k), height = at is { H: > 0 } ? at.H : (int)(360 * k);
        int x, y;
        if (at is not null && OnScreen(at.X + width / 2, at.Y + 20))
        {
            x = at.X;
            y = at.Y;
        }
        else
        {
            Win32.GetCursorPos(out var p);
            var wa = WorkArea(p.X, p.Y);
            x = wa.Left + (wa.Right - wa.Left - width) * 3 / 4;
            y = wa.Top + (wa.Bottom - wa.Top - height) / 3;
        }
        w.PlaceAt(x, y, width, height);
        if (!w.IsVisible) w.Show();
    }

    /// The attached tools around the launcher: the ones put by hand at their offsets; the ones laid out along an edge
    /// in a column on the right or left (the launcher's height shared) or a row underneath (its width shared), a side
    /// with no room on the screen going to the other one.
    public void Layout()
    {
        if (!L.IsVisible || L.Handle == IntPtr.Zero)
        {
            foreach (var w in windows.Where(w => w.Edge is not null && w.IsVisible)) w.Hide();
            return;
        }
        Win32.GetWindowRect(L.Handle, out var r);
        double dpi = Dpi(L.Handle), k = dpi * Theme.Scale;
        int gap = (int)Math.Round(8 * dpi);
        var wa = WorkArea((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        foreach (var w in windows.Where(w => w.Edge == "free" && w.Spot is not null))
        {
            new WindowInteropHelper(w).EnsureHandle();
            var s = w.Spot!;
            w.PlaceAt(r.Left + (int)Math.Round(s.X * k), r.Top + (int)Math.Round(s.Y * k), (int)Math.Round(s.W * k), (int)Math.Round(s.H * k));
            if (!w.IsVisible) w.Show();
        }
        foreach (string edge in new[] { "right", "left", "bottom" })
        {
            var on = windows.Where(w => w.Edge == edge).ToList();
            for (int i = 0; i < on.Count; i++)
            {
                var w = on[i];
                new WindowInteropHelper(w).EnsureHandle();
                int x, y, width, height;
                if (edge == "bottom")
                {
                    width = (r.Right - r.Left - gap * (on.Count - 1)) / on.Count;
                    height = (int)(260 * k);
                    x = r.Left + i * (width + gap);
                    y = r.Bottom + gap + height <= wa.Bottom ? r.Bottom + gap : r.Top - gap - height;
                }
                else
                {
                    width = (int)(300 * k);
                    height = (r.Bottom - r.Top - gap * (on.Count - 1)) / on.Count;
                    y = r.Top + i * (height + gap);
                    bool right = edge == "right" ? r.Right + gap + width <= wa.Right || r.Left - gap - width < wa.Left : r.Left - gap - width < wa.Left;
                    x = right ? r.Right + gap : r.Left - gap - width;
                }
                w.PlaceAt(x, y, width, height);
                if (!w.IsVisible) w.Show();
            }
        }
    }

    /// The launcher was sized from `from` to `to` (dip at 100 %; by hand, or back to its default): a tool put beyond its
    /// right or bottom edge moves with that edge, the others keep their place from its top-left; then all are laid out.
    public void LauncherSized((int w, int h) from, (int w, int h) to)
    {
        foreach (var s in windows.Where(w => w.Edge == "free").Select(w => w.Spot).OfType<ToolPlace>())
        {
            if (s.X >= from.w) s.X += to.w - from.w;
            if (s.Y >= from.h) s.Y += to.h - from.h;
        }
        Layout();
    }

    /// A tool was dropped after a drag or a resize: touching the open launcher, or a tool attached to it, it stays
    /// attached right there; anywhere else it floats there.
    public void Moved(ToolWindow w)
    {
        bool attach = false;
        if (L.IsVisible)
        {
            var t = w.Rect;
            Win32.GetWindowRect(L.Handle, out var lr);
            int near = (int)(24 * Dpi(L.Handle));
            bool Near(Win32.RECT o) => t.Left < o.Right + near && t.Right > o.Left - near && t.Top < o.Bottom + near && t.Bottom > o.Top - near;
            attach = Near(lr) || windows.Exists(o => o != w && o.Edge is not null && o.IsVisible && Near(o.Rect));
        }
        if (attach)
        {
            Freeze();
            w.Topmost = true;
            Pin(w, w.Rect);
            Layout();
        }
        else if (w.Edge is not null)
        {
            Detach(w);
        }
        Save();
    }

    /// The launcher and the other tools showing, the edges a tool pulls to.
    private List<Win32.RECT> Others(ToolWindow w)
    {
        var others = windows.Where(o => o != w && o.IsVisible).Select(o => o.Rect).ToList();
        if (L.IsVisible)
        {
            Win32.GetWindowRect(L.Handle, out var lr);
            others.Add(lr);
        }
        return others;
    }

    /// While a tool is dragged: its edges pull (within 14 px) to the edges near it — beside another tool or the
    /// launcher with Bloom's 8 px gap, lined up with them, or against the screen's work area. True: `r` moved.
    public bool Magnet(ToolWindow w, ref Win32.RECT r)
    {
        double dpi = Dpi(w.Handle);
        int pull = (int)(14 * dpi), gap = (int)Math.Round(8 * dpi), near = pull * 3;
        var others = Others(w);
        int bestX = pull + 1, bestY = pull + 1;
        void X(int edge, int to)
        {
            if (Math.Abs(to - edge) < Math.Abs(bestX)) bestX = to - edge;
        }
        void Y(int edge, int to)
        {
            if (Math.Abs(to - edge) < Math.Abs(bestY)) bestY = to - edge;
        }
        foreach (var o in others)
        {
            if (r.Top < o.Bottom + near && r.Bottom > o.Top - near)
            {
                X(r.Left, o.Right + gap);
                X(r.Right, o.Left - gap);
                X(r.Left, o.Left);
                X(r.Right, o.Right);
            }
            if (r.Left < o.Right + near && r.Right > o.Left - near)
            {
                Y(r.Top, o.Bottom + gap);
                Y(r.Bottom, o.Top - gap);
                Y(r.Top, o.Top);
                Y(r.Bottom, o.Bottom);
            }
        }
        var wa = WorkArea((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        X(r.Left, wa.Left);
        X(r.Right, wa.Right);
        Y(r.Top, wa.Top);
        Y(r.Bottom, wa.Bottom);
        bool moved = false;
        if (Math.Abs(bestX) <= pull)
        {
            r.Left += bestX;
            r.Right += bestX;
            moved = true;
        }
        if (Math.Abs(bestY) <= pull)
        {
            r.Top += bestY;
            r.Bottom += bestY;
            moved = true;
        }
        return moved;
    }

    /// While a tool is sized: the edges being pulled (WM_SIZING's WMSZ_* side) snap the same way, within 14 px, never
    /// below the window's smallest size. True: `r` changed.
    public bool MagnetEdges(ToolWindow w, int side, ref Win32.RECT r)
    {
        double dpi = Dpi(w.Handle);
        int pull = (int)(14 * dpi), gap = (int)Math.Round(8 * dpi), near = pull * 3;
        int minW = (int)(w.MinWidth * dpi), minH = (int)(w.MinHeight * dpi);
        var rr = r;
        var others = Others(w);
        var wa = WorkArea((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        var rows = others.Where(o => rr.Top < o.Bottom + near && rr.Bottom > o.Top - near).ToList();
        var cols = others.Where(o => rr.Left < o.Right + near && rr.Right > o.Left - near).ToList();
        int Pull(int edge, IEnumerable<int> targets)
        {
            int best = 0, d = pull + 1;
            foreach (int to in targets)
                if (Math.Abs(to - edge) < d)
                {
                    d = Math.Abs(to - edge);
                    best = to - edge;
                }
            return d <= pull ? best : 0;
        }
        bool changed = false;
        if (side is 1 or 4 or 7 && Pull(rr.Left, rows.SelectMany(o => new[] { o.Right + gap, o.Left }).Append(wa.Left)) is var dl and not 0 && rr.Right - (rr.Left + dl) >= minW)
        {
            rr.Left += dl;
            changed = true;
        }
        if (side is 2 or 5 or 8 && Pull(rr.Right, rows.SelectMany(o => new[] { o.Left - gap, o.Right }).Append(wa.Right)) is var dr and not 0 && rr.Right + dr - rr.Left >= minW)
        {
            rr.Right += dr;
            changed = true;
        }
        if (side is 3 or 4 or 5 && Pull(rr.Top, cols.SelectMany(o => new[] { o.Bottom + gap, o.Top }).Append(wa.Top)) is var dt and not 0 && rr.Bottom - (rr.Top + dt) >= minH)
        {
            rr.Top += dt;
            changed = true;
        }
        if (side is 6 or 7 or 8 && Pull(rr.Bottom, cols.SelectMany(o => new[] { o.Top - gap, o.Bottom }).Append(wa.Bottom)) is var db and not 0 && rr.Bottom + db - rr.Top >= minH)
        {
            rr.Bottom += db;
            changed = true;
        }
        r = rr;
        return changed;
    }

    // ── the group with the launcher ─────────────────────────────────────────────────────────────

    /// The window is one of the attached tools.
    public bool Owns(IntPtr hwnd) => hwnd != IntPtr.Zero && windows.Exists(w => w.Edge is not null && w.Handle == hwnd);

    /// The attached tools come back with the launcher, cloaked like it: the launcher uncloaks the returned windows with
    /// its own, once their new frame is drawn.
    public List<IntPtr> LauncherShown()
    {
        var cloaked = windows.Where(w => w.Edge is not null && !w.IsVisible).Select(w => new WindowInteropHelper(w).EnsureHandle()).ToList();
        foreach (var h in cloaked) Glass.Cloak(h, true);
        Layout();
        return cloaked;
    }

    public void LauncherHidden()
    {
        foreach (var w in windows.Where(w => w.Edge is not null)) w.Hide();
    }

    /// The group in front (one of it has the focus).
    public void SetTopmost(bool on)
    {
        foreach (var w in windows.Where(w => w.Edge is not null)) w.Topmost = on;
    }

    /// Working in an attached tool keeps the group in front (the launcher stays open while the focus is in the group;
    /// its pin is left as the user set it).
    public void Activated(ToolWindow w)
    {
        if (w.Edge is null) return;
        L.Topmost = true;
        SetTopmost(true);
    }

    /// An attached tool lost the focus: to the launcher or another attached tool, nothing; elsewhere, the group lets go.
    public void Deactivated(ToolWindow w)
    {
        if (w.Edge is null) return;
        L.Dispatcher.BeginInvoke(() =>
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == L.Handle || Owns(fg)) return;
            L.GroupLeft();
        }, DispatcherPriority.Input);
    }

    /// Esc in a tool: an attached one hides with the launcher; a floating one keeps it (true: handled).
    public bool Escape(ToolWindow w)
    {
        if (w.Edge is null) return false;
        L.Dismiss();
        return true;
    }

    // ── kept across starts ──────────────────────────────────────────────────────────────────────

    public void Save()
    {
        Settings.Current.Tools = windows.Select(w =>
        {
            var r = w.Handle != IntPtr.Zero ? w.Rect : default;
            var s = w.Edge == "free" ? w.Spot : null; // attached where it was put: its offset from the launcher
            return new ToolPlace { Key = w.Tool.Key, Edge = w.Edge, X = s?.X ?? r.Left, Y = s?.Y ?? r.Top, W = s?.W ?? r.Right - r.Left, H = s?.H ?? r.Bottom - r.Top, OnTop = w.OnTop, Note = w.Tool.State };
        }).ToList();
        Settings.Save();
    }

    /// At start: the tools left open come back (floating ones now, attached ones with the launcher).
    public void Restore()
    {
        foreach (var p in Settings.Current.Tools.Where(p => Kinds.Any(k => k.key == p.Key)).GroupBy(p => p.Key).Select(g => g.First()).ToList())
        {
            bool spot = p.Edge == "free" && p.W > 0 && p.H > 0;
            var w = new ToolWindow(this, Make(p.Key, p.Note), p.Edge == "free" && !spot ? "right" : p.Edge, p.OnTop);
            if (spot) w.Spot = new ToolPlace { X = p.X, Y = p.Y, W = p.W, H = p.H };
            windows.Add(w);
            if (p.Edge is null) Float(w, p);
        }
    }

    // ── screens ─────────────────────────────────────────────────────────────────────────────────

    /// A window's screen scale (1 = 96 dpi).
    public static double Dpi(IntPtr hwnd) => hwnd != IntPtr.Zero && Win32.GetDpiForWindow(hwnd) is > 0 and var d ? d / 96.0 : 1;

    private static Win32.RECT WorkArea(int x, int y)
    {
        var mon = Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        return Win32.GetMonitorInfo(mon, ref info) ? info.rcWork : new Win32.RECT { Right = 1920, Bottom = 1080 };
    }

    private static bool OnScreen(int x, int y) => Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero;
}
