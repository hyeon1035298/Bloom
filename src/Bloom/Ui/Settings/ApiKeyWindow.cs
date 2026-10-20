using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > AI 계정's key entry for one API provider, on the same surface as ConfirmWindow: a masked field (the key is never
/// shown, not even while typed), a link to the provider's key page, 저장 · 취소. Enter saves, Esc cancels.
internal sealed class ApiKeyWindow : Window
{
    private readonly PasswordBox field;
    private string? result;

    private ApiKeyWindow(Window? owner, ApiProvider provider)
    {
        Title = Tr($"{provider.Name} API 키", $"{provider.Name} API key");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner is null;
        if (owner is { IsVisible: true }) Owner = owner;
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        stack.Children.Add(Theme.T(Title, 17, Theme.P.Text, Theme.Semi));
        var body = Theme.T(Tr($"키는 이 PC에만 암호화하여 저장하며 {provider.Name}에 보내는 요청에만 사용합니다. 사용 요금은 {provider.Name} 계정에 청구됩니다.",
            $"The key is stored encrypted on this PC only and used only for requests to {provider.Name}. Usage is billed to your {provider.Name} account."), 13.5, Theme.P.TextDim, Theme.Regular, false);
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(body);
        field = new PasswordBox
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 13.5,
            Background = Theme.P.Card,
            Foreground = Theme.P.Text,
            BorderBrush = Theme.P.CardLine,
            CaretBrush = Theme.P.Text,
        };
        stack.Children.Add(field);
        var page = ToolUi.TextButton(Tr("키 발급 페이지 열기", "Open the key page"), () => Launch.Open(provider.KeyPage));
        page.HorizontalAlignment = HorizontalAlignment.Left;
        page.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(page);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var save = ActionButton.Make(Tr("저장", "Save"), true, Save);
        save.Margin = new Thickness(8, 6, 0, 0);
        buttons.Children.Add(save);
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
        Loaded += (_, _) => field.Focus();
    }

    private void Save()
    {
        if (field.Password.Trim().Length == 0) return;
        result = field.Password.Trim();
        Close();
    }

    /// Asks for the provider's key; null when cancelled.
    public static string? Ask(Window? owner, ApiProvider provider)
    {
        var w = new ApiKeyWindow(owner, provider);
        w.ShowDialog();
        return w.result;
    }
}
