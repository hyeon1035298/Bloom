using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Ui;

/// The glass Bloom draws itself: the host backdrop (the screen behind, blurred) under a luminosity layer (GlassEffect),
/// in a Windows.UI.Composition visual, called through its vtables as Native.WinRt does (no projection). A window's composition visuals always lie over its own drawing, so the glass is
/// drawn in a window of its own just under the glass window: a plain popup the same size, shown, hidden, moved and
/// cloaked with it, never active and never hit (the glass window covers it). The glass window shows it through its Tint
/// as it showed DWM's acrylic. One compositor on the UI thread serves every window; when composition fails, the
/// windows keep DWM's acrylic (Glass.Apply).
internal static unsafe class OwnGlass
{
    private sealed class Layer(IntPtr glass, IntPtr under, IntPtr target, IntPtr visual)
    {
        public readonly IntPtr Glass = glass, Under = under;
        public readonly IntPtr Target = target; // ICompositionTarget
        public readonly IntPtr Visual = visual; // ISpriteVisual
        public IntPtr Brush; // ICompositionEffectBrush
        public (bool Dark, float Amount) PaintedFor = (false, -1);
        public bool On, Topmost, Zoomed;
        public HwndSourceHook? Hook;
    }

    private static IntPtr compositor; // ICompositor
    private static IntPtr queue; // the UI thread's DispatcherQueueController, which the compositor needs; kept for good
    private static int thread;
    private static bool broken; // composition failed: DWM's acrylic from then on
    private static readonly Dictionary<IntPtr, Layer> layers = [];
    private static readonly Dictionary<bool, (float Amount, IntPtr Factory)> factories = []; // ICompositionEffectFactory

    private static float Amount => GlassEffect.Amount(Settings.Current.GlassTransparency);

    /// Puts the glass under `hwnd` (or brings it to the theme); false when composition can't.
    public static bool Show(Window w, IntPtr hwnd, bool dark)
    {
        if (broken) return false;
        try
        {
            if (!layers.TryGetValue(hwnd, out var layer))
            {
                IntPtr c = Compositor();
                if (c == IntPtr.Zero || HwndSource.FromHwnd(hwnd) is not { } source) return false;
                layer = Make(c, hwnd);
                layers[hwnd] = layer;
                layer.Hook = (IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == WM_WINDOWPOSCHANGED) Follow(layer);
                    return IntPtr.Zero;
                };
                source.AddHook(layer.Hook);
                w.Closed += (_, _) => Remove(hwnd);
            }
            Paint(layer, dark);
            if (!layer.On) SeeThrough(hwnd, true);
            layer.On = true;
            Follow(layer);
            return true;
        }
        catch (Exception e)
        {
            Log.Error("own glass", e);
            broken = true;
            foreach (var h in new List<IntPtr>(layers.Keys)) Remove(h);
            return false;
        }
    }

    /// No glass under the window (transparency effects off, or DWM's acrylic instead).
    public static void Hide(IntPtr hwnd)
    {
        if (!layers.TryGetValue(hwnd, out var layer) || !layer.On) return;
        layer.On = false;
        SeeThrough(hwnd, false);
        Follow(layer);
    }

    /// A captioned window with no system backdrop fills its extended frame with DWM's frame colour where its drawing is
    /// clear; blur-behind with an empty region (no blur is drawn since Windows 8) makes the clear pixels show what is
    /// under the window instead: the glass.
    private static void SeeThrough(IntPtr hwnd, bool on)
    {
        IntPtr none = on ? CreateRectRgn(0, 0, -1, -1) : IntPtr.Zero;
        var bb = new DWM_BLURBEHIND { dwFlags = 1 | 2 /* DWM_BB_ENABLE | DWM_BB_BLURREGION */, fEnable = on, hRgnBlur = none };
        _ = DwmEnableBlurBehindWindow(hwnd, ref bb);
        if (none != IntPtr.Zero) Win32.DeleteObject(none);
    }

    /// The glass window cloaked or uncloaked (Glass.Cloak): the glass goes with it. Uncloaking, the glass is there
    /// before the window; cloaking, it goes after.
    public static void Cloak(IntPtr hwnd, bool on)
    {
        if (!layers.TryGetValue(hwnd, out var layer)) return;
        int v = on ? 1 : 0;
        _ = Win32.DwmSetWindowAttribute(layer.Under, Win32.DWMWA_CLOAK, ref v, 4);
    }

    /// The glass transparency changed (설정 > 성능 > 유리 투명도): the luminosity layer follows it.
    public static void Follow()
    {
        if (broken) return;
        try
        {
            foreach (var layer in layers.Values)
                if (layer.On) Paint(layer, layer.PaintedFor.Dark);
        }
        catch (Exception e)
        {
            Log.Error("own glass", e);
        }
    }

    /// The glass window moved, was resized, shown, hidden or restacked: the glass goes just under it at its size, or
    /// hides with it.
    private static void Follow(Layer layer)
    {
        bool shown = layer.On && Win32.IsWindowVisible(layer.Glass) && !IsIconic(layer.Glass);
        if (!shown || !Win32.GetWindowRect(layer.Glass, out var r))
        {
            Win32.SetWindowPos(layer.Under, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | SWP_HIDEWINDOW);
            return;
        }
        bool topmost = (Win32.GetWindowLongPtr(layer.Glass, Win32.GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
        if (topmost != layer.Topmost)
        {
            Win32.SetWindowPos(layer.Under, topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            layer.Topmost = topmost;
        }
        // maximized, the window reaches past its monitor by its resize border and its corners are square
        bool zoomed = IsZoomed(layer.Glass);
        if (zoomed != layer.Zoomed)
        {
            int corner = zoomed ? DWMWCP_DONOTROUND : Win32.DWMWCP_ROUND;
            _ = Win32.DwmSetWindowAttribute(layer.Under, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            layer.Zoomed = zoomed;
        }
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (zoomed && Win32.GetMonitorInfo(Win32.MonitorFromWindow(layer.Glass, 2 /* nearest */), ref info))
        {
            var w = info.rcWork;
            r = new Win32.RECT { Left = Math.Max(r.Left, w.Left), Top = Math.Max(r.Top, w.Top), Right = Math.Min(r.Right, w.Right), Bottom = Math.Min(r.Bottom, w.Bottom) };
        }
        Win32.SetWindowPos(layer.Under, layer.Glass, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOACTIVATE | SWP_NOOWNERZORDER | Win32.SWP_SHOWWINDOW);
    }

    private static Layer Make(IntPtr c, IntPtr glass)
    {
        IntPtr under = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP, UnderClass(), null, WS_POPUP | WS_DISABLED,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (under == IntPtr.Zero) throw new COMException("glass window", Marshal.GetHRForLastWin32Error());
        try
        {
            int on = 1, corner = Win32.DWMWCP_ROUND, none = Win32.DWMWA_COLOR_NONE;
            _ = Win32.DwmSetWindowAttribute(under, Win32.DWMWA_TRANSITIONS_FORCEDISABLED, ref on, 4);
            _ = Win32.DwmSetWindowAttribute(under, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            _ = Win32.DwmSetWindowAttribute(under, Win32.DWMWA_BORDER_COLOR, ref none, 4);
            // the host backdrop draws what is behind the window only once the window allows it
            _ = Win32.DwmSetWindowAttribute(under, Win32.DWMWA_USE_HOSTBACKDROPBRUSH, ref on, 4);
            IntPtr target = IntPtr.Zero, visual = IntPtr.Zero;
            try
            {
                target = Query(Target(c, under), IID_ICompositionTarget);
                visual = WinRt.Call(c, 22); // ICompositor.CreateSpriteVisual
                IntPtr v2 = Query(visual, IID_IVisual2, release: false);
                try
                {
                    WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, Vector2, int>)WinRt.Slot(v2, 11))(v2, Vector2.One)); // put_RelativeSizeAdjustment
                }
                finally
                {
                    Marshal.Release(v2);
                }
                SetRoot(target, visual);
                return new Layer(glass, under, target, visual);
            }
            catch
            {
                Close(ref visual);
                Close(ref target);
                throw;
            }
        }
        catch
        {
            DestroyWindow(under);
            throw;
        }
    }

    private static void Paint(Layer layer, bool dark)
    {
        float amount = Amount;
        if (layer.PaintedFor == (dark, amount)) return;
        IntPtr brush = WinRt.Call(Factory(dark, amount), WinRt.First); // ICompositionEffectFactory.CreateBrush
        try
        {
            IntPtr c3 = Query(compositor, IID_ICompositor3, release: false), backdrop = IntPtr.Zero;
            try
            {
                backdrop = Query(WinRt.Call(c3, WinRt.First), IID_ICompositionBrush); // ICompositor3.CreateHostBackdropBrush
                IntPtr name = WinRt.HString(GlassEffect.Backdrop);
                try
                {
                    WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)WinRt.Slot(brush, 7))(brush, name, backdrop)); // SetSourceParameter
                }
                finally
                {
                    WinRt.FreeHString(name);
                }
            }
            finally
            {
                Marshal.Release(c3);
                if (backdrop != IntPtr.Zero) Marshal.Release(backdrop);
            }
            IntPtr asBrush = Query(brush, IID_ICompositionBrush, release: false);
            try
            {
                WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRt.Slot(layer.Visual, 7))(layer.Visual, asBrush)); // ISpriteVisual.put_Brush
            }
            finally
            {
                Marshal.Release(asBrush);
            }
        }
        catch
        {
            Close(ref brush);
            throw;
        }
        Close(ref layer.Brush);
        layer.Brush = brush;
        layer.PaintedFor = (dark, amount);
    }

    private static IntPtr Factory(bool dark, float amount)
    {
        if (factories.TryGetValue(dark, out var made) && made.Amount == amount) return made.Factory;
        IntPtr effect = GlassEffect.Make(GlassEffect.Grey(dark), amount), factory;
        try
        {
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)WinRt.Slot(compositor, 11))(compositor, effect, &factory)); // CreateEffectFactory
        }
        finally
        {
            Marshal.Release(effect);
        }
        int status = WinRt.GetInt(factory, WinRt.First + 2); // LoadStatus: 0 Success, 2 Pending
        if (status is not (0 or 2))
        {
            int why = WinRt.GetInt(factory, WinRt.First + 1); // ExtendedError
            Close(ref factory);
            throw new COMException("luminosity layer " + status, why < 0 ? why : unchecked((int)0x80004005));
        }
        IntPtr old = made.Factory;
        Close(ref old);
        factories[dark] = (amount, factory);
        return factory;
    }

    private static void Remove(IntPtr hwnd)
    {
        if (!layers.Remove(hwnd, out var layer)) return;
        try
        {
            if (layer.Hook is not null) HwndSource.FromHwnd(hwnd)?.RemoveHook(layer.Hook);
            SetRoot(layer.Target, IntPtr.Zero);
            Close(ref layer.Brush);
            IntPtr visual = layer.Visual, target = layer.Target;
            Close(ref visual);
            Close(ref target);
        }
        catch (Exception e)
        {
            Log.Error("own glass", e);
        }
        DestroyWindow(layer.Under);
    }

    /// The UI thread's compositor, made on the first glass window; null on another thread.
    private static IntPtr Compositor()
    {
        if (compositor != IntPtr.Zero) return thread == Environment.CurrentManagedThreadId ? compositor : IntPtr.Zero;
        var options = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = 2 /* current */, Apartment = 2 /* STA */ };
        int hr = CreateDispatcherQueueController(options, out queue);
        if (hr < 0) Log.Write($"own glass: dispatcher queue 0x{hr:X8}"); // a queue already on the thread serves as well
        IntPtr factory = WinRt.Factory("Windows.UI.Composition.Compositor", IID_IActivationFactory);
        try
        {
            compositor = Query(WinRt.Call(factory, WinRt.First), IID_ICompositor); // IActivationFactory.ActivateInstance
        }
        finally
        {
            Marshal.Release(factory);
        }
        thread = Environment.CurrentManagedThreadId;
        return compositor;
    }

    private static readonly Guid IID_IActivationFactory = new("00000035-0000-0000-C000-000000000046");
    private static readonly Guid IID_IClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
    private static readonly Guid IID_ICompositor = new("B403CA50-7F8C-4E83-985F-CC45060036D8");
    private static readonly Guid IID_ICompositor3 = new("C9DD8EF0-6EB1-4E3C-A658-675D9C64D4AB");
    private static readonly Guid IID_ICompositionBrush = new("AB0D7608-30C0-40E9-B568-B60A6BD1FB46");
    private static readonly Guid IID_ICompositionTarget = new("A1BEA8BA-D726-4663-8129-6B5E7927FFA6");
    private static readonly Guid IID_IVisual = new("117E202D-A859-4C89-873B-C2AA566788E3");
    private static readonly Guid IID_IVisual2 = new("3052B611-56C3-4C3E-8BF3-F6E1AD473F06");

    /// `p` as `iid`; `p` is released unless `release` is false.
    private static IntPtr Query(IntPtr p, Guid iid, bool release = true)
    {
        try
        {
            WinRt.Check(Marshal.QueryInterface(p, in iid, out IntPtr q));
            return q;
        }
        finally
        {
            if (release) Marshal.Release(p);
        }
    }

    /// ICompositionTarget.put_Root (`visual` an ISpriteVisual, or none).
    private static void SetRoot(IntPtr target, IntPtr visual)
    {
        IntPtr v = visual == IntPtr.Zero ? IntPtr.Zero : Query(visual, IID_IVisual, release: false);
        try
        {
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)WinRt.Slot(target, 7))(target, v));
        }
        finally
        {
            if (v != IntPtr.Zero) Marshal.Release(v);
        }
    }

    /// A composition object closed (IClosable.Close: its resources go now, not with the last reference) and released.
    private static void Close(ref IntPtr p)
    {
        if (p == IntPtr.Zero) return;
        if (Marshal.QueryInterface(p, in IID_IClosable, out IntPtr closable) >= 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)WinRt.Slot(closable, WinRt.First))(closable);
            Marshal.Release(closable);
        }
        Marshal.Release(p);
        p = IntPtr.Zero;
    }

    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");

    /// ICompositorDesktopInterop.CreateDesktopWindowTarget (an IDesktopWindowTarget).
    private static IntPtr Target(IntPtr c, IntPtr hwnd)
    {
        IntPtr interop = Query(c, IID_ICompositorDesktopInterop, release: false);
        try
        {
            IntPtr target;
            WinRt.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)WinRt.Slot(interop, 3))(interop, hwnd, 0, &target));
            return target;
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    // ── the glass's own window ──────────────────────────────────────────────────────────────────

    private const string ClassName = "Bloom.GlassUnder";
    private static bool registered;

    /// A window class with no procedure of our own and no background: the window holds only the composition visual.
    private static string UnderClass()
    {
        if (registered) return ClassName;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref wc) == 0) throw new COMException("glass window class", Marshal.GetHRForLastWin32Error());
        registered = true;
        return ClassName;
    }

    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint WS_POPUP = 0x80000000, WS_DISABLED = 0x08000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint SWP_HIDEWINDOW = 0x0080, SWP_NOOWNERZORDER = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size, ThreadType, Apartment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public int dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND bb);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
