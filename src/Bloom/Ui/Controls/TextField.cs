using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A line of free text on a settings row (a model name, a program's path): a box and 적용. Enter applies, Esc puts the kept
/// text back; the field's Tag answers the keys while it is typed in (like DigitBox's). `Check` returns why a text is not
/// taken (shown beside the field), null when it is; an empty text is always taken (it means "the default").
internal sealed class TextField : StackPanel
{
    private readonly TextBox box;
    private readonly TextBlock error = Theme.T("", 12, Theme.P.Danger, Theme.Regular, false);
    private string kept;

    public event Action<string>? Applied;
    public Action? Done;
    public Func<string, string?>? Check;

    public TextField(string? value, string hintText, double width = 230)
    {
        kept = value ?? "";
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        box = new TextBox
        {
            Text = kept,
            Width = width,
            FontFamily = Theme.Sans,
            FontSize = 13,
            FontWeight = Theme.Medium,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Template = Launcher.BareTextBox(),
        };
        var hint = Theme.T(hintText, 13, Theme.P.TextFaint, Theme.Medium, false);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var inner = new Grid();
        inner.Children.Add(hint);
        inner.Children.Add(box);
        void ShowHint() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            ShowHint();
            error.Text = "";
        };
        ShowHint();
        var well = new SmoothBorder
        {
            Background = Theme.P.Well,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.IBeam,
            Child = inner,
        };
        well.MouseLeftButtonDown += (_, e) =>
        {
            FocusBox();
            e.Handled = true;
        };
        var apply = ActionButton.Make(Tr("적용", "Apply"), true, Apply);
        apply.Margin = new Thickness(8, 0, 0, 0);
        apply.Padding = new Thickness(12, 5, 12, 6);
        error.Margin = new Thickness(10, 0, 0, 0);
        Children.Add(well);
        Children.Add(apply);
        Children.Add(error);
        box.Tag = (Func<Key, bool>)(key =>
        {
            switch (key)
            {
                case Key.Enter:
                    Apply();
                    return true;
                case Key.Escape:
                    box.Text = kept;
                    Done?.Invoke();
                    return true;
                default:
                    return false;
            }
        });
    }

    public void FocusBox()
    {
        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
    }

    private void Apply()
    {
        string text = box.Text.Trim();
        if (text.Length > 0 && Check?.Invoke(text) is { } why)
        {
            error.Text = why;
            return;
        }
        box.Text = kept = text;
        Applied?.Invoke(text);
    }
}
