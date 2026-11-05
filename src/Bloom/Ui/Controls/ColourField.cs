using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A colour typed as #RRGGBB (설정 > 색 > 직접): a box and 적용; a code that is not one is not taken and the reason shows beside it.
/// Enter applies, Esc puts the old code back; the field's Tag answers the keys while it is typed in (like DigitBox's).
internal sealed class ColourField : StackPanel
{
    private readonly TextBox box;
    private readonly TextBlock error = Theme.T("", 12, Theme.P.Danger, Theme.Regular, false);
    private readonly string? kept;

    /// The code was good: its normal form ("#3A7BD5").
    public event Action<string>? Applied;
    public Action? Done;

    public ColourField(string? value)
    {
        kept = value;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        box = new TextBox
        {
            Text = value ?? "",
            MaxLength = 7,
            MinWidth = 76,
            FontFamily = Theme.Sans,
            FontSize = 14,
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
        InputMethod.SetIsInputMethodEnabled(box, false);
        var hint = Theme.T("#3A7BD5", 14, Theme.P.TextFaint, Theme.Medium, false);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        var inner = new Grid();
        inner.Children.Add(hint);
        inner.Children.Add(box);
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            error.Text = "";
        };
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                    box.Text = kept ?? "";
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
        if (!Accents.TryParse(box.Text, out var c, out string why))
        {
            error.Text = why;
            return;
        }
        string code = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        box.Text = code;
        Applied?.Invoke(code);
    }
}
