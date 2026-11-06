using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// Windows 11 acrylic behind a WPF window: the DWM "transient window" backdrop over a client area
/// extended into the frame, rounded corners, our own hairline border, no DWM show/hide animation
/// (the launcher opens 100+ times a day — it must appear instantly).
internal static class Glass
{
    public static void Apply(Window w, bool dark, bool acrylic, Color border)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int v = dark ? 1 : 0;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        int corner = Win32.DWMWCP_ROUND;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
        int noAnim = 1;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_TRANSITIONS_FORCEDISABLED, ref noAnim, 4);
        int colorRef = border.R | border.G << 8 | border.B << 16;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref colorRef, 4);
        // no caption fill: the title-bar strip at the top shows the acrylic like the rest
        int noCaption = Win32.DWMWA_COLOR_NONE;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CAPTION_COLOR, ref noCaption, 4);
        int backdrop = acrylic ? Win32.DWMSBT_TRANSIENTWINDOW : Win32.DWMSBT_NONE;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
        var m = acrylic ? new Win32.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 } : new Win32.MARGINS();
        _ = Win32.DwmExtendFrameIntoClientArea(hwnd, ref m);
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = acrylic ? Colors.Transparent : Colors.Black;
    }

    /// Hides a window from the screen while it keeps drawing (DWM cloak). Opening under a cloak, the first frame on
    /// screen is the new one. Uncloaked, it was the one from before the last hide (or nothing yet, for a new window)
    /// over the bare backdrop: a light flash, measured on 0.12.2 on the first open after the idle trim. The acrylic only
    /// draws while the window is active, so whoever opens a glass window activates it before RevealAfterFrame.
    public static void Cloak(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        int v = on ? 1 : 0;
        int hr = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CLOAK, ref v, 4);
        if (hr != 0 && !cloakLogged)
        {
            cloakLogged = true;
            Log.Write($"cloak {(on ? "on" : "off")} failed 0x{hr:X8}");
        }
    }

    /// A glass window being created (from its SourceInitialized, before it is shown): it appears with its first frame.
    public static void CloakFirstFrame(Window w)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        Cloak(hwnd, true);
        RevealAfterFrame([hwnd]);
    }

    private static bool cloakLogged;
    private static int openings;
    private static readonly Dictionary<IntPtr, int> claims = []; // a cloaked window → the newest opening that reveals it
    private static readonly Dictionary<IntPtr, (int, int)> drawnAt = []; // a window's size when it last came on screen

    /// The longest an opening window stays cloaked if no frame comes; the first frame came 6–85 ms after opening (measured).
    private static readonly TimeSpan RevealGuard = TimeSpan.FromMilliseconds(500);

    /// Uncloaks the windows together once WPF has drawn their next frame and DWM has composed it. The Rendering event
    /// comes before the drawing (and can come twice within a couple of ms), so after it: the frame goes out (Loaded, behind
    /// the drawing and the Loaded handlers' own relayout), then DWM passes for at least DrawnWait, waited for off the UI
    /// thread, and the uncloak goes ahead of WPF's next frame (Send). The guard never leaves them hidden. A window opened
    /// again before that belongs to the newer opening, which uncloaks it; this one only lets go of it if it stayed hidden.
    /// `step` hears "fresh", "frame", "flush" (the wait for DWM starts), "flushed" (on the pool), "guard" and "revealed"
    /// (SummonTrace).
    /// WPF tells nobody when its render thread has put a frame on screen (its present interlock is off here), so the
    /// waits are measured ones (the launcher at 240 Hz, a screen grab at the uncloak against one later): a drawn
    /// window's frame was there after one DWM pass, a fresh one's (Fresh) after 12–26 ms, blank grey before. Waiting at
    /// Background instead had given 25–60 ms by chance: WPF runs Background on the system's 15.6 ms timer ticks while
    /// messages are posted, as drawing posts them, and late rows queued there ran first.
    private static readonly TimeSpan DrawnWait = TimeSpan.FromMilliseconds(10), FreshWait = TimeSpan.FromMilliseconds(50);

    public static void RevealAfterFrame(IReadOnlyList<IntPtr> hwnds, Action<string>? step = null)
    {
        var ui = Dispatcher.CurrentDispatcher;
        bool fresh = Fresh(hwnds);
        if (fresh) step?.Invoke("fresh");
        int opening = ++openings;
        foreach (var h in hwnds) claims[h] = opening;
        bool done = false;
        var guard = new DispatcherTimer { Interval = RevealGuard };
        EventHandler? onFrame = null;
        // `framed`: after a frame (not the guard); a window it shows is then drawn at its size (Fresh)
        void Reveal(bool framed)
        {
            if (done) return;
            done = true;
            CompositionTarget.Rendering -= onFrame;
            guard.Stop();
            foreach (var h in hwnds)
            {
                bool mine = claims.TryGetValue(h, out int c) && c == opening;
                if (mine) claims.Remove(h);
                if (mine || !Win32.IsWindowVisible(h)) Cloak(h, false);
                if (framed && mine && Win32.IsWindowVisible(h) && Size(h) is { } size) drawnAt[h] = size;
            }
            step?.Invoke("revealed");
        }
        onFrame = (_, _) =>
        {
            CompositionTarget.Rendering -= onFrame;
            step?.Invoke("frame");
            ui.BeginInvoke(() =>
            {
                step?.Invoke("flush");
                Task.Run(() =>
                {
                    var wait = fresh ? FreshWait : DrawnWait;
                    var since = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        var pass = System.Diagnostics.Stopwatch.StartNew();
                        if (Win32.DwmFlush() != 0 || pass.Elapsed < TimeSpan.FromMilliseconds(1)) Thread.Sleep(2); // DWM had no pass to wait for
                    }
                    while (since.Elapsed < wait);
                    step?.Invoke("flushed");
                }).ContinueWith(_ => ui.InvokeAsync(() => Reveal(true), DispatcherPriority.Send), TaskContinuationOptions.ExecuteSynchronously);
            }, DispatcherPriority.Loaded);
        };
        guard.Tick += (_, _) =>
        {
            if (!done) step?.Invoke("guard");
            Reveal(false);
        };
        CompositionTarget.Rendering += onFrame;
        guard.Start();
    }

    /// A window not yet on screen at its size after a frame (Reveal): new, hidden again before that, moved to another
    /// monitor's DPI, resized. WPF's render thread makes its surfaces anew and its first frame comes later (the first
    /// opening after start, every new glass window).
    private static bool Fresh(IReadOnlyList<IntPtr> hwnds)
    {
        if (drawnAt.Count > 64)
            foreach (var gone in drawnAt.Keys.Where(h => !Win32.IsWindow(h)).ToList()) drawnAt.Remove(gone);
        return hwnds.Any(h => Size(h) is { } size && (!drawnAt.TryGetValue(h, out var was) || was != size));
    }

    private static (int, int)? Size(IntPtr h) => Win32.GetWindowRect(h, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : null;

    /// Keep the launcher out of Alt+Tab and the taskbar, and drop the system menu: with the frame
    /// extended over the whole window, DWM would otherwise paint its close button in the corner.
    /// (The caption itself stays: without WS_CAPTION, DWM draws no acrylic at all.)
    public static void ToolWindow(Window w)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex = (ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
        const int GWL_STYLE = -16;
        const long WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
        long st = Win32.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(st & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)));
        Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }
}
