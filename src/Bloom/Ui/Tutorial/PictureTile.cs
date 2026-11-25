using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Core;

namespace Bloom.Ui;

internal enum TileMark { None, Pick, Badge, Switch }

/// A picture with a name under it. The mark says how it is chosen.
internal sealed class PictureTile : Grid
{
    private const double Radius = 12;
    /// A line under the name that starts with this shows a drawn check in its place: the font's own is a thin,
    /// lopsided stroke beside the marks drawn on the tiles.
    private const string CheckLead = "✓ ";

    private readonly TileMark mark;
    private readonly bool small;
    private readonly int nameLines;
    private readonly string nameText;
    private readonly Grid sceneBox = new();
    private readonly SmoothBorder line = new() { CornerRadius = new CornerRadius(Radius), IsHitTestVisible = false };
    private readonly Grid meta = new();
    private readonly TextBlock name, about;
    private readonly TutorialParts.EvenLines nameSlot, aboutSlot;
    private readonly FrameworkElement aboutCheck;
    private readonly DockPanel aboutLine = new() { LastChildFill = true };
    private readonly Border linkBox = new() { Visibility = Visibility.Collapsed };
    private readonly Switch? toggle;
    private readonly Grid? disc;
    private bool on, aboutAccent, dim, off, hover, nameWraps;
    private string? aboutText;
    private Action? click;
    private (string label, Action open)? linkTo;
    private double fittedFor = double.NaN;

    public PictureTile(FrameworkElement scene, double sceneHeight, string name, string? about = null,
        TileMark mark = TileMark.None, bool on = false, bool small = false, int nameLines = 1)
    {
        (this.mark, this.on, this.small, this.nameLines, nameText) = (mark, on, small, Math.Max(1, nameLines), name);
        Scene = scene;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(sceneHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // the card takes the pointer for the whole tile; the scene lies on it, cut to the card's top corners
        var card = new SmoothBorder { CornerRadius = new CornerRadius(Radius), Background = Theme.P.Card };
        SetRowSpan(card, 2);
        Children.Add(card);
        scene.Height = sceneHeight;
        sceneBox.Children.Add(scene);
        sceneBox.SizeChanged += (_, e) => sceneBox.Clip = SmoothBorder.Shape(new Rect(e.NewSize), new CornerRadius(Radius, Radius, 0, 0));
        Children.Add(sceneBox);

        this.name = Theme.T(name, small ? 11.5 : 13, Theme.P.Text, Theme.Semi);
        if (!small && this.nameLines > 1) this.name.Text = Hangul.KeepAll(name);
        nameSlot = new TutorialParts.EvenLines(this.name, small ? 15 : NameLine, small ? 1 : this.nameLines);
        this.about = Theme.T("", small ? 10.5 : 11.5, Theme.P.TextFaint);
        aboutSlot = new TutorialParts.EvenLines(this.about, small ? 13 : AboutLine, small ? 1 : 2);
        aboutCheck = TutorialParts.Tick(11, Theme.P.AccentText, 1.6);
        aboutCheck.VerticalAlignment = VerticalAlignment.Top;
        aboutCheck.Margin = new Thickness(0, small ? 1 : 2, 4, 0);
        DockPanel.SetDock(aboutCheck, Dock.Left);
        aboutLine.Children.Add(aboutCheck);
        aboutLine.Children.Add(aboutSlot);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        text.Children.Add(nameSlot);
        text.Children.Add(aboutLine);
        text.Children.Add(linkBox);
        meta.Margin = small ? new Thickness(9, 6, 8, 7) : new Thickness(11, 8, 10, 9);
        if (small)
        {
            meta.Height = 28; // two lines whatever the name does, so every small tile is as tall
            meta.VerticalAlignment = VerticalAlignment.Top;
        }
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.Children.Add(text);
        if (mark == TileMark.Switch)
        {
            toggle = new Switch(on);
            SetColumn(toggle, 1);
            meta.Children.Add(toggle);
        }
        SetRow(meta, 1);
        Children.Add(meta);

        if (mark is TileMark.Pick or TileMark.Badge)
        {
            double size = mark == TileMark.Pick ? 16 : 18, inset = mark == TileMark.Pick ? 8 : 7;
            disc = new Grid
            {
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, inset, inset, 0),
                IsHitTestVisible = false,
            };
            Children.Add(disc);
        }
        SetRowSpan(line, 2);
        Children.Add(line);

        About = about;
        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => Hover(false);
        MouseLeftButtonUp += (_, e) =>
        {
            if (!Takes) return;
            Press();
            e.Handled = true;
        };
        TutorialParts.TabStop(this, new CornerRadius(Radius + 2));
        TutorialParts.Keys(this, Press);
        Loaded += (_, _) =>
        {
            Theme.Changed -= Draw;
            Theme.Changed += Draw;
            Draw();
        };
        Unloaded += (_, _) => Theme.Changed -= Draw;
        Draw();
    }

    public FrameworkElement Scene { get; }

    public bool On
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            Draw();
        }
    }

    public event Action<bool>? Toggled;

    public Action? Click
    {
        get => click;
        set
        {
            click = value;
            Draw();
        }
    }

    public string? About
    {
        get => aboutText;
        set
        {
            aboutText = value;
            bool check = value?.StartsWith(CheckLead, StringComparison.Ordinal) == true;
            string shown = check ? value![CheckLead.Length..] : value ?? "";
            about.Text = small ? shown : Hangul.KeepAll(shown);
            aboutCheck.Visibility = check ? Visibility.Visible : Visibility.Collapsed;
            Refit();
            ShowLines();
        }
    }

    public bool AboutAccent
    {
        get => aboutAccent;
        set
        {
            aboutAccent = value;
            Refit();
            Draw();
        }
    }

    public (string label, Action open)? Link
    {
        get => linkTo;
        set
        {
            linkTo = value;
            linkBox.Child = null;
            if (value is { } l)
            {
                var link = TutorialParts.Link(l.label, l.open, small ? 10.5 : 11.5);
                if (link is TextBlock t) TutorialParts.Lines(t, small ? 13 : AboutLine);
                linkBox.Child = link;
            }
            ShowLines();
        }
    }

    public bool Dim
    {
        get => dim;
        set
        {
            dim = value;
            Scene.Opacity = value ? 0.4 : 1;
        }
    }

    public bool Off
    {
        get => off;
        set
        {
            off = value;
            Opacity = value ? 0.45 : 1;
            IsHitTestVisible = !value;
            IsEnabled = !value; // nothing inside takes the keyboard either
            Draw();
        }
    }

    /// The tile does something when pressed: it has a mark, or a click of its own.
    private bool Takes => !off && (mark != TileMark.None || click is not null);

    /// What a click does, by mark: Switch and Badge flip, Pick turns on once, None runs Click.
    internal void Press()
    {
        if (off) return;
        switch (mark)
        {
            case TileMark.None:
                click?.Invoke();
                break;
            case TileMark.Pick:
                if (on) return;
                On = true;
                Toggled?.Invoke(true);
                break;
            default:
                On = !on;
                Toggled?.Invoke(on);
                break;
        }
    }

    private void Hover(bool over)
    {
        hover = over;
        Draw();
    }

    /// Under the name: the link when there is one, else the line; neither where a small tile's name takes both lines.
    private void ShowLines()
    {
        bool room = !nameWraps;
        linkBox.Visibility = room && linkTo is not null ? Visibility.Visible : Visibility.Collapsed;
        aboutLine.Visibility = room && linkTo is null && aboutText is not null ? Visibility.Visible : Visibility.Collapsed;
        if (toggle is null) return;
        // beside the name and the first line under it, wherever the text ends: tiles of a grid are equally tall and
        // their texts are not, and this way their switches still stand in one line
        bool under = linkTo is not null || aboutText is not null;
        toggle.VerticalAlignment = under ? VerticalAlignment.Top : VerticalAlignment.Center;
        toggle.Margin = new Thickness(SwitchGap, under ? (NameLine + AboutLine - toggle.Height) / 2 : 0, 0, 0);
    }

    private const double SwitchGap = 8, NameLine = 17, AboutLine = 15;

    private void Refit()
    {
        fittedFor = double.NaN;
        InvalidateMeasure();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Refit(); // a text is not as wide at another scale
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width) && !(Math.Abs(fittedFor - constraint.Width) < 0.01))
        {
            fittedFor = constraint.Width;
            double room = constraint.Width - meta.Margin.Left - meta.Margin.Right - (toggle is null ? 0 : toggle.Width + SwitchGap);
            if (small) FitSmallName(room);
            FitAbout(room - (aboutCheck.Visibility == Visibility.Visible ? aboutCheck.Width + aboutCheck.Margin.Right : 0));
            ShowLines();
        }
        return base.MeasureOverride(constraint);
    }

    /// The text's width on one line at `size`.
    private static double Natural(TextBlock t, double size)
    {
        t.FontSize = size;
        t.TextWrapping = TextWrapping.NoWrap;
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return t.DesiredSize.Width;
    }

    /// A small tile's name: on one line as it is, else a little smaller on one line, else on two lines in place of
    /// the line under it. The tile stays as tall either way.
    private void FitSmallName(double width)
    {
        name.Text = nameText;
        nameWraps = Natural(name, 11.5) > width - 0.5 && Natural(name, 11) > width - 0.5;
        if (nameWraps) name.Text = Hangul.KeepAll(nameText);
        nameSlot.Set(nameWraps ? 14 : 15, nameWraps ? 2 : 1);
    }

    /// The line under the name: on one line as it is, else half a point smaller on one line, which saves a line left
    /// with one short word. What is still too long wraps to a second line at its own size (a small tile's ends in an
    /// ellipsis: it has one line).
    private void FitAbout(double width)
    {
        double size = small ? 10.5 : 11.5;
        bool fits = Natural(about, size) <= width - 0.5 || Natural(about, size - 0.5) <= width - 0.5;
        if (!fits) about.FontSize = size;
        aboutSlot.Set(small ? 13 : AboutLine, small || fits ? 1 : 2);
    }

    private void Draw()
    {
        bool lit = on && mark != TileMark.None, pick = mark == TileMark.Pick;
        // the outline lies over the scene, so the scene keeps the tile's whole width
        line.BorderThickness = new Thickness(lit && pick ? 1.5 : 1);
        line.BorderBrush = lit ? Theme.P.Accent : hover && Takes ? Theme.P.EdgeStrong : Theme.P.CardLine;
        line.Opacity = lit && !pick ? hover ? 0.75 : 0.5 : 1;
        about.Foreground = aboutAccent ? Theme.P.AccentText : Theme.P.TextFaint;
        about.FontWeight = aboutAccent ? Theme.Semi : Theme.Regular;
        Cursor = Takes ? Cursors.Hand : null;
        Focusable = Takes;
        if (toggle is not null && toggle.On != on) toggle.On = on;
        if (disc is not null) DrawDisc();
    }

    private bool? discOn, discDark;

    private void DrawDisc()
    {
        if (discOn == on && discDark == Theme.P.Dark) return;
        (discOn, discDark) = (on, Theme.P.Dark);
        disc!.Children.Clear();
        if (on)
        {
            disc.Children.Add(new Ellipse { Fill = Theme.P.Accent });
            disc.Children.Add(TutorialParts.Tick(mark == TileMark.Pick ? 10 : 11, Theme.P.AccentInk, 1.5));
            return;
        }
        if (mark != TileMark.Badge) return;
        // off: a ring on a little of the ground's colour, so it reads over whatever the scene has under it
        bool dark = Theme.P.Dark;
        disc.Children.Add(new Ellipse
        {
            Fill = Frozen(dark ? Color.FromArgb(0x99, 0x1C, 0x1C, 0x1F) : Color.FromArgb(0xB3, 0xFA, 0xFA, 0xFA)),
            Stroke = Frozen(dark ? Color.FromRgb(0x6B, 0x6B, 0x72) : Color.FromRgb(0xA1, 0xA1, 0xAA)),
            StrokeThickness = 1.5,
        });
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// Equal columns of tiles; every row is as tall as the tallest tile of the grid.
internal sealed class TileGrid : Panel
{
    private readonly int columns;
    private readonly double columnGap, rowGap;
    private double rowHeight;

    public TileGrid(int columns, double columnGap, double rowGap)
    {
        this.columns = Math.Max(1, columns);
        this.columnGap = columnGap;
        this.rowGap = rowGap;
    }

    private IEnumerable<PictureTile> Tiles => Children.OfType<PictureTile>();

    public void Add(PictureTile tile) => Children.Add(tile);

    /// Single choice over Pick tiles: one stays on, `picked` gets its index.
    public void PickOne(Action<int> picked)
    {
        var tiles = Tiles.ToList();
        for (int i = 0; i < tiles.Count; i++)
        {
            int index = i;
            tiles[i].Toggled += v =>
            {
                if (!v) return;
                for (int k = 0; k < tiles.Count; k++)
                    if (k != index) tiles[k].On = false;
                picked(index);
            };
        }
    }

    /// A column's left edge and width on whole pixels: a width the columns do not divide (three in 552 with a gap
    /// of 8) gives columns a pixel apart in width and gaps that stay the same.
    private (double X, double Width) Column(int col, double width)
    {
        double w = Math.Max(0, (width - (columns - 1) * columnGap) / columns);
        double x = Math.Round(col * (w + columnGap)), right = Math.Round(col * (w + columnGap) + w);
        return (x, Math.Max(0, right - x));
    }

    protected override Size MeasureOverride(Size available)
    {
        bool open = double.IsInfinity(available.Width);
        rowHeight = 0;
        double widest = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(open ? double.PositiveInfinity : Column(i % columns, available.Width).Width, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, c.DesiredSize.Height);
            widest = Math.Max(widest, c.DesiredSize.Width);
        }
        int rows = (InternalChildren.Count + columns - 1) / columns;
        double width = open ? widest * columns + (columns - 1) * columnGap : available.Width;
        return new Size(width, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * rowGap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var (x, w) = Column(i % columns, final.Width);
            InternalChildren[i].Arrange(new Rect(x, i / columns * (rowHeight + rowGap), w, rowHeight));
        }
        return final;
    }
}
