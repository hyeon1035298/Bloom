using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// One value typed in on the same surface as ApiKeyWindow (설정 > 미니 플레이어: an address, a user, a program's path, a
/// password): a field — masked when it is a secret, which is never shown, not even while typed — 저장 · 취소 and, when
/// there is a value to take back, 지우기. Enter saves, Esc cancels.
internal sealed class ValueWindow : Window
{
    private readonly TextBox? text;
    private readonly PasswordBox? secret;
    private readonly TextBlock error;
    private readonly Func<string, string?>? check;
    private string? result;

    private ValueWindow(Window? owner, string title, string about, string value, bool isSecret, bool canClear, Func<string, string?>? check)
    {
        this.check = check;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner is null;
        if (owner is { IsVisible: true }) Owner = owner;
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Icon = Shots.AppIcon();

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        stack.Children.Add(Theme.T(title, 17, Theme.P.Text, Theme.Semi));
        var body = Theme.T(about, 13.5, Theme.P.TextDim, Theme.Regular, false);
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(body);
        if (isSecret)
        {
            secret = new PasswordBox();
            Skin(secret);
            stack.Children.Add(secret);
        }
        else
        {
            text = new TextBox { Text = value, FontFamily = Theme.Sans, SelectionBrush = Theme.P.Selection };
            Skin(text);
            stack.Children.Add(text);
        }
        error = Theme.T("", 12, Theme.P.Danger, Theme.Regular, false);
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 8, 0, 0);
        error.Visibility = Visibility.Collapsed;
        stack.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var save = ActionButton.Make(Tr("저장", "Save"), true, Save);
        save.Margin = new Thickness(8, 6, 0, 0);
        buttons.Children.Add(save);
        if (canClear)
        {
            var clear = ActionButton.Make(Tr("지우기", "Clear"), false, () =>
            {
                result = "";
                Close();
            });
            clear.Margin = new Thickness(8, 6, 0, 0);
            buttons.Children.Add(clear);
        }
        var cancel = ActionButton.Make(Tr("취소", "Cancel"), false, Close);
        cancel.Margin = new Thickness(8, 6, 0, 0);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = new Border { Background = Theme.P.Solid, Child = stack };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Save();
            else return;
            e.Handled = true;
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
        Loaded += (_, _) =>
        {
            if (text is not null)
            {
                text.Focus();
                text.SelectAll();
            }
            else secret!.Focus();
        };
    }

    private static void Skin(Control c)
    {
        c.Margin = new Thickness(0, 16, 0, 0);
        c.Padding = new Thickness(10, 7, 10, 7);
        c.FontSize = 13.5;
        c.Background = Theme.P.Card;
        c.Foreground = Theme.P.Text;
        c.BorderBrush = Theme.P.CardLine;
        if (c is TextBox t) t.CaretBrush = Theme.P.Text;
        if (c is PasswordBox p) p.CaretBrush = Theme.P.Text;
    }

    private void Save()
    {
        string value = (text?.Text ?? secret!.Password).Trim();
        if (check?.Invoke(value) is { } problem)
        {
            error.Text = problem;
            error.Visibility = Visibility.Visible;
            return;
        }
        result = value;
        Close();
    }

    /// The typed value (empty: taken back); null when cancelled. `check` returns what is wrong with a value, or null.
    public static string? Ask(Window? owner, string title, string about, string value, bool isSecret, bool canClear, Func<string, string?>? check = null)
    {
        var w = new ValueWindow(owner, title, about, value, isSecret, canClear, check);
        w.ShowDialog();
        return w.result;
    }
}
