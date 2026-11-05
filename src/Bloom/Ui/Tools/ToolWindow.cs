using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// A Bloom tool in a small window of its own, moved by its head and sized by its edges anywhere; near the launcher,
/// another tool or the screen's edge it pulls in (ToolDock). Dropped beside the launcher (or a tool attached to it)
/// it stays attached there: it moves, shows and hides with the launcher. Dropped away, it floats on its own and stays
/// when something else takes the focus, on top of other windows or not (its 항상 위 button).
internal sealed class ToolWindow : Window
{
    public readonly ITool Tool;
    private readonly ToolDock dock;
    private readonly SmoothBorder frame;
    private readonly SmoothBorder attachButton, topButton;

    /// right | left | bottom while laid out along that edge of the launcher, free while attached where it was put
    /// (Spot); null floating.
    public string? Edge { get; private set; }

    /// Attached where it was put: its place and size in dip at 100 % from the launcher's top-left.
    public ToolPlace? Spot { get; set; }

    /// Floating: above other windows.
    public bool OnTop { get; private set; } = true;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// QA: what a group picture draws of it.
    public FrameworkElement FrameForQa => frame;

    public ToolWindow(ToolDock dock, ITool tool, string? edge, bool onTop)
    {
        this.dock = dock;
        Tool = tool;
        OnTop = onTop;
        Title = "Bloom " + tool.Name;
        Width = 300;
        Height = 360;
        MinWidth = 160;
        MinHeight = 120;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        GlassFrame.Prepare(this);

        // head: the tool's glyph and name (the handle to drag it by), its own buttons, attach, on top, close
        var buttons = new StackPanel();
        if (tool.Head is { } own)
        {
            WindowChrome.SetIsHitTestVisibleInChrome(own, true);
            buttons.Children.Add(own);
        }
        attachButton = GlassFrame.HeadButton(Undock, Tr("런처에서 떼기", "Detach from the launcher"), () => dock.ToggleAttach(this));
        topButton = GlassFrame.HeadButton('', Tr("항상 위", "Always on top"), () => SetOnTop(!OnTop));
        buttons.Children.Add(attachButton);
        buttons.Children.Add(topButton);
        buttons.Children.Add(GlassFrame.HeadButton(G.Close, Tr("닫기", "Close"), () => dock.Close(this)));
        var head = GlassFrame.Head(tool.Glyph, tool.Name, buttons, out _);

        frame = GlassFrame.Frame(head, tool.View);
        Content = frame;
        frame.LayoutTransform = Theme.Zoom; // its size follows the scale through ToolDock (attached ones from the launcher)
        SetEdge(edge);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = dock.Escape(this);
        };
        Activated += (_, _) => dock.Activated(this);
        Deactivated += (_, _) => dock.Deactivated(this);
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            Paint();
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
            Glass.CloakFirstFrame(this);
        };
        Theme.Changed += Paint;
        Closed += (_, _) =>
        {
            Theme.Changed -= Paint;
            Tool.Closed();
        };
    }

    private Win32.POINT grab; // where the mouse holds the window, from its top-left corner, while it is dragged

    // the attach button: attached, 떼기 (a window coming out); floating, 붙이기 (docked on the right) — not a pin, so it
    // never looks like 항상 위
    private const char Undock = '\uE8A7', DockOn = '\uE90D';

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (dock.KeepLook(hwnd, msg, wParam) is { } kept)
        {
            handled = true;
            return kept;
        }
        // a drag or a resize starts: the attached tools laid out along an edge stay where they are from now on
        if (msg == Win32.WM_ENTERSIZEMOVE)
        {
            if (Edge is not null) dock.Freeze();
            Win32.GetCursorPos(out var c);
            var start = Rect;
            grab = new Win32.POINT { X = c.X - start.Left, Y = c.Y - start.Top };
        }
        if (msg is Win32.WM_MOVING or Win32.WM_SIZING)
        {
            // while dragged or sized: the edges that move pull to the edges nearby (other tools, the launcher, the screen).
            // A drag works from where the mouse is, not from the rectangle Windows offers: that one starts from the last
            // snapped place, so a slow drag kept snapping back to a screen's edge and never reached the next monitor.
            var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.RECT>(lParam);
            if (msg == Win32.WM_MOVING)
            {
                Win32.GetCursorPos(out var c);
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                r = new Win32.RECT { Left = c.X - grab.X, Top = c.Y - grab.Y, Right = c.X - grab.X + w, Bottom = c.Y - grab.Y + h };
                dock.Magnet(this, ref r);
            }
            if (msg == Win32.WM_MOVING || dock.MagnetEdges(this, (int)wParam.ToInt64(), ref r))
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
                handled = true;
                return new IntPtr(1);
            }
        }
        if (msg == Win32.WM_EXITSIZEMOVE) Dispatcher.BeginInvoke(() => dock.Moved(this)); // dropped: attached there or floating
        return IntPtr.Zero;
    }

    /// Attached (an edge, or free) or floating (null): the buttons and the frame follow.
    public void SetEdge(string? edge)
    {
        Edge = edge;
        if (edge != "free") Spot = null;
        ((TextBlock)attachButton.Child).Text = (edge is null ? DockOn : Undock).ToString();
        attachButton.ToolTip = edge is null ? Tr("런처에 붙이기", "Attach to the launcher") : Tr("런처에서 떼기", "Detach from the launcher");
        topButton.Visibility = edge is null ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    public void SetOnTop(bool on)
    {
        OnTop = on;
        if (Edge is null) Topmost = on;
        Paint();
        dock.Save();
    }

    private void Paint()
    {
        GlassFrame.Paint(this, frame);
        ((TextBlock)topButton.Child).Foreground = OnTop ? Theme.P.Accent : Theme.P.TextDim;
    }

    /// The window's place on the screen, in physical pixels.
    public Win32.RECT Rect
    {
        get
        {
            Win32.GetWindowRect(Handle, out var r);
            return r;
        }
    }

    /// Puts the window at a place in physical pixels (attached: sized by the launcher).
    public void PlaceAt(int x, int y, int w, int h) =>
        Win32.SetWindowPos(Handle, Topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
}
