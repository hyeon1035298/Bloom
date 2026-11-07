using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Bloom.Native;
using Bloom.Ui;

namespace Bloom.Ocr;

/// 3 · 2 · 1 in the middle of the area before 화면 녹화 starts; kept out of screen captures and clicks.
internal sealed class RecordCountdown : Window
{
    private const double Side = 72;
    private readonly TextBlock number;
    private readonly ScreenRect area;
    private readonly double scale;

    public RecordCountdown(ScreenRect area)
    {
        this.area = area;
        var center = new Win32.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 };
        IntPtr mon = Win32.MonitorFromPoint(center, Win32.MONITOR_DEFAULTTONEAREST);
        scale = Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Width = Height = Side + 32;
        Left = Top = -32000;
        number = Theme.T("3", 26, Theme.P.Text, Theme.Semi, false);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        Content = new SmoothBorder
        {
            Width = Side,
            Height = Side,
            CornerRadius = new CornerRadius(Side / 2),
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            Child = number,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Color = Colors.Black, Opacity = Theme.P.Dark ? 0.4 : 0.14 },
        };
        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            Glass.ToolWindow(this);
            const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
            long ex = Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE).ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE;
            Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, new IntPtr(ex));
            SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
            Place();
        };
        DpiChanged += (_, _) => Place();
    }

    /// 3, 2, 1 a second each, then gone; false when cancelled on the way.
    public async Task<bool> Run(CancellationToken cancel)
    {
        Show();
        try
        {
            for (int n = 3; n >= 1; n--)
            {
                number.Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Task.Delay(1000, cancel);
            }
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        finally
        {
            Close();
        }
    }

    private void Place()
    {
        int side = (int)Math.Round(Width * scale);
        Win32.SetWindowPos(new WindowInteropHelper(this).Handle, Win32.HWND_TOPMOST,
            area.X + area.Width / 2 - side / 2, area.Y + area.Height / 2 - side / 2, side, side, Win32.SWP_NOACTIVATE);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
