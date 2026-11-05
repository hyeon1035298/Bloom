using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.App;
using Bloom.Core;
using Line = Bloom.Ui.NoticePlacement.Line;

namespace Bloom.Ui;

/// 직접 옮기기 (설정 > 알림): the primary monitor's work area dimmed, with a notice card at its real size and place to
/// drag. It sticks to the centre lines and the edges (NoticePlacement.Snap; Ctrl held: it doesn't), a line in the
/// accent shows what it is stuck to, and the island window (one a taskbar add-on keeps at the top middle of the screen)
/// shows as a dashed box the card stays under. Enter or 완료 keeps the place (a named spot when it is one, else `free` with its fractions); Esc or 취소 leaves it as it was.
internal sealed class NoticePlacer : Window
{
    private static NoticePlacer? open;

    private readonly Canvas canvas = new() { ClipToBounds = true };
    private readonly Grid desk = new();
    private readonly Rect room; // the work area, in the canvas's own coordinates
    private readonly Rect? island; // the same coordinates
    private readonly FrameworkElement sheet;
    private readonly Rectangle stuckX = Guide(), stuckY = Guide();
    private readonly FrameworkElement help;
    private readonly Action<bool>? done;
    private Size size;
    private Point at; // the card window's corner as kept: stuck, before the island moves it down
    private Vector? grab;
    private bool finished;

    private NoticePlacer(Rect work, Rect? islandBox, Action<bool>? done)
    {
        this.done = done;
        Title = Tr("알림 위치", "Notice position");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
        room = new Rect(0, 0, work.Width, work.Height);
        if (islandBox is { } i) island = new Rect(i.X - work.Left, i.Y - work.Top, i.Width, i.Height);

        canvas.Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        canvas.Children.Add(Faint(room.Width / 2, 0, 1, room.Height));
        canvas.Children.Add(Faint(0, room.Height / 2, room.Width, 1));
        canvas.Children.Add(stuckX);
        canvas.Children.Add(stuckY);
        if (island is { } box) IslandMark(box);
        help = Help();
        canvas.Children.Add(help);
        sheet = NoticeWindow.Sheet(new Notice("done", Tr("Bloom · 알림 위치", "Bloom · Notice position"), Tr("여기에 알림이 표시됩니다", "Notices show here"), Tr("끌어서 옮기세요", "Drag it to move"), Sample: true));
        sheet.Cursor = Cursors.SizeAll;
        sheet.ToolTip = null;
        canvas.Children.Add(sheet);
        desk.Children.Add(canvas);
        Content = desk;

        sheet.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        size = sheet.DesiredSize;
        var (x, y) = NoticeWindow.Spot;
        at = NoticePlacement.At(room, size, x, y);
        Draw(Line.None, Line.None);

        sheet.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(canvas) - Shown();
            sheet.CaptureMouse();
            e.Handled = true;
        };
        sheet.MouseMove += (_, e) =>
        {
            if (grab is not { } g || e.LeftButton != MouseButtonState.Pressed) return;
            DragTo(e.GetPosition(canvas) - g, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        };
        sheet.MouseLeftButtonUp += (_, e) =>
        {
            grab = null;
            sheet.ReleaseMouseCapture();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case Key.Enter: Finish(true); break;
                case Key.Escape: Finish(false); break;
                case Key.Left: DragTo(Shown() + new Vector(-step, 0), true); break;
                case Key.Right: DragTo(Shown() + new Vector(step, 0), true); break;
                case Key.Up: DragTo(Shown() + new Vector(0, -step), true); break;
                case Key.Down: DragTo(Shown() + new Vector(0, step), true); break;
                default: return;
            }
            e.Handled = true;
        };
        Closed += (_, _) =>
        {
            if (open == this) open = null;
            if (finished) return;
            finished = true;
            this.done?.Invoke(false); // closed some other way (Alt+F4): nothing changes
        };
    }

    /// Over the primary monitor's work area; `done` hears whether a place was kept. One at a time.
    public static void Open(Action<bool> done)
    {
        if (open is { } already)
        {
            already.Activate();
            return;
        }
        open = new NoticePlacer(SystemParameters.WorkArea, NoticeIsland.Find(), done);
        open.Show();
        open.Activate();
    }

    /// QA: unshown, over a made-up work area and island, on a plain desk colour (the picture has no screen behind it).
    public static NoticePlacer ForQa(Rect work, Rect? islandBox, Brush deskColour)
    {
        var w = new NoticePlacer(work, islandBox, null) { Topmost = false, ShowActivated = false };
        w.desk.Background = deskColour;
        return w;
    }

    public void Capture(string file) => Shots.Save(desk, file);

    /// The card's corner where it is dragged to (canvas coordinates), kept in, stuck unless `free`.
    public void DragTo(Point corner, bool free)
    {
        var (stuck, lineX, lineY) = NoticePlacement.Snap(room, size, corner, island, free);
        at = stuck;
        Draw(lineX, lineY);
    }

    private Point Shown() => NoticePlacement.Avoid(room, size, at, island);

    private void Draw(Line lineX, Line lineY)
    {
        var shown = Shown();
        Canvas.SetLeft(sheet, shown.X);
        Canvas.SetTop(sheet, shown.Y);
        Stick(stuckX, lineX, true);
        Stick(stuckY, lineY, false, shown.Y != at.Y ? shown.Y : 0);
        PlaceHelp(new Rect(shown, size));
    }

    /// The accent line of what the card is stuck to: through the middle, or along the edge (the card's shown top when the
    /// island keeps it below the edge).
    private void Stick(Rectangle guide, Line line, bool across, double shownTop = 0)
    {
        guide.Visibility = line == Line.None ? Visibility.Collapsed : Visibility.Visible;
        double thick = line == Line.Center ? 2 : 4;
        double pos = line switch
        {
            Line.Center => ((across ? room.Width : room.Height) - thick) / 2,
            Line.End => (across ? room.Width : room.Height) - thick,
            _ => across ? 0 : Math.Max(0, shownTop),
        };
        guide.Width = across ? thick : room.Width;
        guide.Height = across ? room.Height : thick;
        Canvas.SetLeft(guide, across ? pos : 0);
        Canvas.SetTop(guide, across ? 0 : pos);
    }

    /// The help card in the middle, or a quarter up or down when the notice card is over the middle.
    private void PlaceHelp(Rect card)
    {
        help.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hs = help.DesiredSize;
        double x = (room.Width - hs.Width) / 2;
        double y = (room.Height - hs.Height) / 2;
        if (new Rect(x, y, hs.Width, hs.Height).IntersectsWith(card))
            y = card.Top + (card.Height / 2) > room.Height / 2 ? (room.Height * 0.25) - (hs.Height / 2) : (room.Height * 0.75) - (hs.Height / 2);
        Canvas.SetLeft(help, Math.Round(x));
        Canvas.SetTop(help, Math.Round(y));
    }

    private void Finish(bool keep)
    {
        if (finished) return;
        finished = true;
        if (keep)
        {
            var (x, y) = NoticePlacement.Fractions(room, size, at);
            var s = Settings.Current;
            s.NoticeSpot = NoticePlacement.Name(x, y);
            if (s.NoticeSpot == "free") (s.NoticeX, s.NoticeY) = (x, y);
            Settings.Save();
        }
        Close();
        done?.Invoke(keep);
    }

    private FrameworkElement Help()
    {
        var text = new StackPanel();
        text.Children.Add(Theme.T(Tr("알림 위치", "Notice position"), 15, Theme.P.Text, Theme.Semi));
        // one sentence a line: Korean wraps between any two letters
        foreach (var (line, size, colour, gap) in new[]
        {
            (Tr("알림 카드를 끌어 원하는 곳에 놓으세요.", "Drag the notice card where you want it."), 13.0, Theme.P.TextDim, 8.0),
            (Tr("가운데와 가장자리에 가까이 가면 붙습니다.", "It sticks to the middle and the edges nearby."), 13.0, Theme.P.TextDim, 2.0),
            (Tr("Ctrl을 누른 채 끌면 붙지 않습니다.", "Hold Ctrl while dragging to place it freely."), 12.0, Theme.P.TextFaint, 6.0),
        })
        {
            var t = Theme.T(line, size, colour, Theme.Regular, false);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, gap, 0, 0);
            text.Children.Add(t);
        }
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ActionButton.Make(Tr("완료", "Done"), true, () => Finish(true)));
        buttons.Children.Add(ActionButton.Make(Tr("취소", "Cancel"), false, () => Finish(false)));
        text.Children.Add(buttons);
        return new SmoothBorder
        {
            Width = 380,
            Background = Theme.P.Solid,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 18, 22, 18),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
            Child = text,
        };
    }

    /// The island as a dashed pill with its name beside it.
    private void IslandMark(Rect box)
    {
        var mark = new Rectangle
        {
            Width = box.Width,
            Height = box.Height,
            RadiusX = box.Height / 2,
            RadiusY = box.Height / 2,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(mark, box.Left);
        Canvas.SetTop(mark, box.Top);
        canvas.Children.Add(mark);
        var name = new SmoothBorder
        {
            Background = Theme.P.Solid,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 4),
            Child = Theme.T(Tr("아일랜드", "Island"), 12, Theme.P.TextDim, Theme.Medium),
        };
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(name, box.Right + 8);
        Canvas.SetTop(name, Math.Round(box.Top + ((box.Height - name.DesiredSize.Height) / 2)));
        canvas.Children.Add(name);
    }

    private static Rectangle Guide() => new() { Fill = Theme.P.Accent, Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    private static Rectangle Faint(double x, double y, double w, double h)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
        Canvas.SetLeft(r, Math.Round(x));
        Canvas.SetTop(r, Math.Round(y));
        return r;
    }
}
