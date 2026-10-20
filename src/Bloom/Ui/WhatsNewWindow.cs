using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// 바뀐 점: the first start after an update (ReleaseNotes.JustUpdated) and 설정 › 정보 show the version's notes from the
/// changelog, selectable like a chat answer (Markdown → ReadText). 확인 (Enter / Esc) closes it; GitHub에서 보기 opens the
/// release page. It doesn't take the focus when it comes up on its own.
internal sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow(string version, string notes)
    {
        Title = Tr($"Bloom {version} · 바뀐 점", $"Bloom {version} · What's new");
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        var stack = new StackPanel { Margin = new Thickness(32, 26, 32, 20) };
        stack.Children.Add(Theme.T(Tr($"Bloom {version} 업데이트 완료", $"Bloom {version} update complete"), 20, Theme.P.Text, Theme.Semi));
        var sub = Theme.T(Tr("이번 버전의 변경 사항", "Changes in this version"), 13, Theme.P.TextFaint);
        sub.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(sub);
        var body = Markdown.Render(notes, link => Launch.Open(link));
        stack.Children.Add(new ScrollViewer
        {
            Content = body,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Template = Launcher.ThinScroll(),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(ActionButton.Make(Tr("GitHub에서 보기", "See it on GitHub"), false, () => Launch.Open(Updates.Page)));
        buttons.Children.Add(ActionButton.Make(Tr("확인", "OK"), true, Close));
        stack.Children.Add(buttons);
        Content = new Border { Background = Theme.P.Solid, Child = stack }; // one surface with the title bar (SolidCaption)

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            Close();
            e.Handled = true;
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
    }
}
