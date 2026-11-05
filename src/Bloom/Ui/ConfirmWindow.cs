using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A question on Bloom's own surface whose buttons say what they do (초기화 · 취소, 데이터 삭제 · 데이터 유지 …) instead of
/// 예 / 아니요: the title names the action, the text gives its consequence. The first choice is the accent one; Esc or
/// the window's ✕ picks the last (취소 / 나중에). One choice makes it a plain notice (확인).
/// The toned form gives each button its own look (a step that can't be undone in red) and names a default choice: it
/// carries a ring without keyboard focus and Enter picks it.
internal sealed class ConfirmWindow : Window
{
    private MessageBoxResult result;

    private ConfirmWindow(Window? owner, string title, string text, (string label, MessageBoxResult value, ButtonTone tone)[] choices, int byDefault)
    {
        Title = title;
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
        result = choices[^1].value;

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        stack.Children.Add(Theme.T(title, 17, Theme.P.Text, Theme.Semi));
        var body = Theme.T(text, 13.5, Theme.P.TextDim, Theme.Regular, false);
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(body);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        for (int i = 0; i < choices.Length; i++)
        {
            var value = choices[i].value;
            var button = ActionButton.Make(choices[i].label, choices[i].tone, () =>
            {
                result = value;
                Close();
            });
            button.Margin = new Thickness(8, 6, 0, 0);
            if (i == byDefault)
            {
                // the default's mark shows without keyboard focus (the buttons never take it)
                button.BorderBrush = Theme.P.Text;
                button.BorderThickness = new Thickness(1.5);
            }
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons);
        Content = new Border { Background = Theme.P.Solid, Child = stack }; // one surface with the title bar (SolidCaption)

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(); // the last choice stands
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && byDefault >= 0)
            {
                result = choices[byDefault].value;
                Close();
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
    }

    private static (string, MessageBoxResult, ButtonTone)[] Plain((string label, MessageBoxResult value)[] choices) =>
        choices.Select((c, i) => (c.label, c.value, i == 0 ? ButtonTone.Accent : ButtonTone.Plain)).ToArray();

    /// Shows the question and waits; returns the value of the button pressed (the last one on Esc / ✕).
    public static MessageBoxResult Ask(Window? owner, string title, string text, params (string label, MessageBoxResult value)[] choices)
    {
        var w = new ConfirmWindow(owner, title, text, Plain(choices), -1);
        w.ShowDialog();
        return w.result;
    }

    /// As Ask, each button in its own tone; `byDefault` is the value Enter picks, its button ringed.
    public static MessageBoxResult Ask(Window? owner, string title, string text, MessageBoxResult byDefault, params (string label, MessageBoxResult value, ButtonTone tone)[] choices)
    {
        var w = new ConfirmWindow(owner, title, text, choices, System.Array.FindIndex(choices, c => c.value == byDefault));
        w.ShowDialog();
        return w.result;
    }

    /// The window unshown, for a QA capture (`--qa-set lang`).
    public static Window ForQa(string title, string text, params (string label, MessageBoxResult value)[] choices) => new ConfirmWindow(null, title, text, Plain(choices), -1);

    /// A result to read and close (확인).
    public static void Tell(Window? owner, string title, string text) => Ask(owner, title, text, (Tr("확인", "OK"), MessageBoxResult.OK));
}
