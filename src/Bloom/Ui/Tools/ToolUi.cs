using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// The pieces the tools are made of: a field with a placeholder, a row that lights up under the mouse, a small glyph
/// button, a list that scrolls, and how long ago something was.
internal static class ToolUi
{
    /// A one-line field in a soft well, its placeholder showing while it is empty.
    /// `enter`: a ↵ key cap at the field's end that does what Enter does.
    public static (FrameworkElement view, TextBox box) Field(string placeholder, double size = 13.5, Action? enter = null)
    {
        var box = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = size,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Template = Launcher.BareTextBox(),
        };
        var hint = Theme.T(placeholder, size, Theme.P.TextFaint);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(2, 0, 0, 0);
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(hint);
        g.Children.Add(box);
        if (enter is not null)
        {
            var cap = Keys.Cap("↵");
            cap.Margin = new Thickness(6, 0, 0, 0);
            cap.Cursor = Cursors.Hand;
            cap.ToolTip = "Enter";
            cap.MouseLeftButtonUp += (_, e) =>
            {
                enter();
                e.Handled = true;
            };
            Grid.SetColumn(cap, 1);
            g.Children.Add(cap);
        }
        var well = new SmoothBorder { Background = Theme.P.Well, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 8), Child = g, Cursor = Cursors.IBeam };
        well.MouseLeftButtonDown += (_, _) => box.Focus();
        return (well, box);
    }

    /// A list row: lights up under the mouse, `click` on a left click.
    public static SmoothBorder Row(UIElement content, Action? click)
    {
        var row = new SmoothBorder { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 8, 8), Background = Brushes.Transparent, Child = content, Cursor = click is null ? null : Cursors.Hand };
        row.MouseEnter += (_, _) => row.Background = Theme.P.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (click is not null)
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return;
                click();
                e.Handled = true;
            };
        return row;
    }

    /// A small glyph button (a row's pin or delete, a head's new or back).
    public static SmoothBorder Button(char glyph, string tip, Action click, Brush? color = null)
    {
        var b = new SmoothBorder
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Theme.Glyph(glyph, 11, color ?? Theme.P.TextDim),
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// A text button for a tool's foot ("켜기", "모두 지우기").
    public static SmoothBorder TextButton(string text, Action click)
    {
        var b = ButtonLook.Apply(new SmoothBorder { CornerRadius = new CornerRadius(8), Height = 28, Padding = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand, Child = Theme.Label(text, 12, Theme.P.Text, Theme.Medium, inBorder: true), HorizontalAlignment = HorizontalAlignment.Left });
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// A list that scrolls with Bloom's thin thumb.
    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Template = Launcher.ThinScroll(),
        Focusable = false,
    };

    /// A faint line in the middle of an empty list.
    public static TextBlock Empty(string text)
    {
        var t = Theme.T(Core.Hangul.KeepAll(text), 12.5, Theme.P.TextFaint, Theme.Regular, false);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(16, 24, 16, 0);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return t;
    }

    /// "방금", "5분 전", "오후 3:12", "10월 7일".
    public static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        if (d.TotalMinutes < 1) return Tr("방금", "just now");
        if (d.TotalHours < 1) return Tr($"{(int)d.TotalMinutes}분 전", $"{(int)d.TotalMinutes} min ago");
        if (at.Date == DateTime.Today) return at.ToString(Tr("tt h:mm", "h:mm tt"), Culture);
        return at.ToString(Tr("M월 d일", "MMM d"), Culture);
    }
}
