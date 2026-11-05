using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.App;
using Bloom.Native;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Ocr;

/// Bloom Lens › 화면에 고정: the picked area stays on top of everything, right where it was taken, as a picture to look at
/// while working (a reference, a number to copy, a bit of a chat). Drag moves it, the wheel sizes it (Ctrl+wheel: see
/// through), 0 takes it back to its size, a double click or Esc closes it; the right button has copy, save and read text.
internal sealed class PinWindow : Window
{
    private readonly Host host;
    private readonly ScreenRect area; // where it was taken (physical pixels)
    private readonly LensShot shot; // its Lens copy on disk (Local\lens) is what 복사 and 저장 hand on
    private readonly BitmapSource picture; // the only copy of the pixels this window keeps
    private readonly PopupMenu menu = new();
    private double scale = 1;

    public PinWindow(Host host, LensShot shot)
    {
        this.host = host;
        area = shot.Rect;
        this.shot = shot with { Bgra = [] }; // the pixels live in `picture` only
        picture = BitmapSource.Create(shot.Rect.Width, shot.Rect.Height, 96, 96, PixelFormats.Bgra32, null, shot.Pixels(), shot.Rect.Width * 4);
        picture.Freeze();
        Title = Tr("Bloom 화면 고정", "Bloom pinned picture");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; // Ctrl+wheel fades it
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        var image = new Image { Source = picture, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Content = new Border
        {
            BorderBrush = Theme.P.Accent,
            BorderThickness = new Thickness(1),
            Child = image,
            ToolTip = Tr("끌어서 옮기기 · 휠 크기 · Ctrl+휠 투명도 · 0 원래 크기 · 더블클릭 · Esc 닫기 · 오른쪽 클릭 메뉴",
                "Drag to move · wheel to size · Ctrl+wheel to fade · 0 back to size · double click · Esc closes · right click for more"),
        };
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            Place(area.X, area.Y);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseRightButtonUp += (_, e) =>
        {
            menu.Open(Actions());
            e.Handled = true;
        };
        MouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
            else Resize(scale * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        };
        KeyDown += (_, e) =>
        {
            if (menu.HandleKey(e.Key))
            {
                e.Handled = true;
                return;
            }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.D0 or Key.NumPad0:
                    Resize(1);
                    Opacity = 1;
                    break;
                case Key.C when ctrl:
                    Copy();
                    break;
                case Key.S when ctrl:
                    Save();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
    }

    private List<ActionItem> Actions() => new()
    {
        new() { Title = Tr("그림 복사", "Copy the picture"), Keys = "Ctrl+C", Glyph = G.Copy, Run = Copy },
        new() { Title = Tr("그림 저장…", "Save the picture…"), Keys = "Ctrl+S", Glyph = G.Document, Run = Save },
        new() { Title = Tr("글자 읽어 복사", "Read and copy the text"), Glyph = G.Page, Run = ReadText },
        new() { Title = Tr("원래 크기로", "Back to its size"), Keys = "0", Glyph = G.Refresh, Run = () => { Resize(1); Opacity = 1; } },
        new() { Title = "" },
        new() { Title = Tr("닫기", "Close"), Keys = "Esc", Glyph = G.Close, Run = Close },
    };

    /// The size at `s` × the picked area (physical pixels), the top-left corner staying put.
    private void Resize(double s)
    {
        scale = Math.Clamp(s, 0.2, 4);
        if (Win32.GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) Place(r.Left, r.Top);
    }

    private void Place(int x, int y)
    {
        IntPtr h = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(h, Win32.HWND_TOPMOST, x, y, Math.Max(8, (int)Math.Round(area.Width * scale)), Math.Max(8, (int)Math.Round(area.Height * scale)), Win32.SWP_NOACTIVATE);
    }

    private void Copy() => Launch.CopyImage(picture, shot.Png);

    private void Save() => Host.SavePicture(shot, picture, this);

    private async void ReadText()
    {
        var bgra = new byte[area.Width * area.Height * 4];
        picture.CopyPixels(bgra, area.Width * 4, 0);
        var (px, w, h, _) = ScreenGrab.ForOcr(bgra, area.Width, area.Height);
        var r = await WinOcr.Recognize(px, w, h, host.OcrLanguage());
        if (r is null || r.Text.Trim().Length == 0)
        {
            Notices.Result(false, "Bloom Lens", Tr("인식된 글자 없음", "No text found"), null);
            return;
        }
        Launch.Copy(r.Text);
        Notices.Result(true, "Bloom Lens", Tr($"{r.Lines.Count}줄 인식·복사 완료", $"Read and copied {r.Lines.Count} lines"), null);
    }
}
