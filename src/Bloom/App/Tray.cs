using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Native;
using Microsoft.Win32;

namespace Bloom.App;

/// The notification-area icon: left click toggles the launcher, right click shows a small menu.
/// The icon is the app mark drawn at the tray's pixel size (crisp at any DPI).
internal sealed class Tray : IDisposable
{
    private const uint CallbackMsg = Win32.WM_APP + 7;
    private readonly HwndSource window;
    private Win32.NOTIFYICONDATA data;
    private IntPtr icon;
    private readonly uint taskbarCreated;

    public event Action? Toggle, OpenSettings, Quit;

    public Tray()
    {
        window = new HwndSource(new HwndSourceParameters("BloomTray") { Width = 0, Height = 0, WindowStyle = 0 });
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        window.AddHook(Hook);
        icon = MakeIcon();
        data = new Win32.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = window.Handle,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
            uCallbackMessage = CallbackMsg,
            hIcon = icon,
            szTip = Tr("Bloom — Win 키로 열기", "Bloom — press Win to open"),
            szInfo = "",
            szInfoTitle = "",
        };
        Add();
        Ui.Theme.Changed += Repaint;
    }

    /// The mark wears the accent: a new icon when it changes.
    private void Repaint()
    {
        IntPtr fresh = MakeIcon(), old = icon;
        icon = fresh;
        data.hIcon = fresh;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
        if (old != IntPtr.Zero) Win32.DestroyIcon(old);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    private void Add()
    {
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
        data.uVersion = 4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == (int)taskbarCreated)
        {
            Add(); // Explorer restarted
            return IntPtr.Zero;
        }
        if (msg != (int)CallbackMsg) return IntPtr.Zero;
        uint ev = (uint)(l.ToInt64() & 0xFFFF);
        if (ev == Win32.WM_LBUTTONUP) Toggle?.Invoke();
        else if (ev is Win32.WM_RBUTTONUP or Win32.WM_CONTEXTMENU) Menu();
        handled = true;
        return IntPtr.Zero;
    }

    private void Menu()
    {
        IntPtr m = Win32.CreatePopupMenu();
        Win32.AppendMenu(m, Win32.MF_STRING, (UIntPtr)1, Tr("열기\tWin", "Open\tWin"));
        Win32.AppendMenu(m, Win32.MF_STRING, (UIntPtr)2, Tr("설정", "Settings"));
        Win32.AppendMenu(m, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
        Win32.AppendMenu(m, Win32.MF_STRING, (UIntPtr)3, Tr("Bloom 종료", "Quit Bloom"));
        Win32.GetCursorPos(out var p);
        Win32.SetForegroundWindow(window.Handle);
        int cmd = Win32.TrackPopupMenuEx(m, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN, p.X, p.Y, window.Handle, IntPtr.Zero);
        Win32.DestroyMenu(m);
        switch (cmd)
        {
            case 1:
                Toggle?.Invoke();
                break;
            case 2:
                OpenSettings?.Invoke();
                break;
            case 3:
                Quit?.Invoke();
                break;
        }
    }

    /// The mark rendered into a 32-bit icon at the small-icon size.
    private static IntPtr MakeIcon()
    {
        int size = Math.Max(16, Win32.GetSystemMetrics(49 /* SM_CXSMICON */));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Ui.Theme.MarkVisual(size));
        var px = new byte[size * size * 4];
        new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0).CopyPixels(px, size * 4, 0); // icons want straight alpha
        var bi = new Win32.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        IntPtr color = Win32.CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        Marshal.Copy(px, 0, bits, px.Length);
        IntPtr mask = Win32.CreateBitmap(size, size, 1, 1, null);
        var ii = new Win32.ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        IntPtr h = Win32.CreateIconIndirect(ref ii);
        Win32.DeleteObject(color);
        Win32.DeleteObject(mask);
        return h;
    }

    public void Dispose()
    {
        Ui.Theme.Changed -= Repaint;
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        if (icon != IntPtr.Zero) Win32.DestroyIcon(icon);
        icon = IntPtr.Zero;
        window.Dispose();
    }
}

/// "윈도우 시작 시 실행": the HKCU Run entry (no admin rights). Dev copies never touch it.
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Bloom";

    public static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    /// On, and pointing at this exe.
    public static bool IsCurrent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string s && s == Command;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOn()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool on)
    {
        if (Core.Paths.IsDev) return;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Name, Command);
            else k.DeleteValue(Name, false);
        }
        catch (Exception e)
        {
            Core.Log.Error("autostart", e);
        }
    }
}
