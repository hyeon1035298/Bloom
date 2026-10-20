using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// Settings in a window of their own (taskbar, Alt+Tab, resizable): the filter field and the categories on the
/// left, the open category's page on the right, the keys under it.
/// The title bar takes the window's colour so it reads as one surface.
internal sealed class SettingsWindow : Window
{
    private readonly SettingsView view;
    private readonly TextBox box;
    private readonly TextBlock placeholder;
    private readonly Grid root;

    public SettingsWindow(Host host)
    {
        Title = Tr("Bloom 설정", "Bloom settings");
        Width = 880;
        Height = 660;
        MinWidth = 700;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; // the screen with the mouse
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        root = new Grid { Background = Theme.P.Solid };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // left: the filter field over the categories
        var side = new Grid();
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = Theme.Glyph(G.Search, 13, Theme.P.TextFaint);
        glyph.Margin = new Thickness(0, 0, 9, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(glyph);
        box = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = 13.5,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Template = Launcher.BareTextBox(),
        };
        placeholder = Theme.T(Tr("설정 찾기", "Find a setting"), 13.5, Theme.P.TextFaint);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var boxHost = new Grid();
        boxHost.Children.Add(placeholder);
        boxHost.Children.Add(box);
        Grid.SetColumn(boxHost, 1);
        bar.Children.Add(boxHost);
        side.Children.Add(new SmoothBorder
        {
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10, 14, 10, 12),
            Child = bar,
            Cursor = Cursors.IBeam,
        });

        view = new SettingsView(host) { ClearFilter = () => box.Clear() };
        view.FocusFilter = () =>
        {
            box.Focus();
            Keyboard.Focus(box);
        };
        var navScroll = new ScrollViewer { Content = view.Nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Template = Launcher.ThinScroll() };
        Grid.SetRow(navScroll, 1);
        side.Children.Add(navScroll);
        root.Children.Add(side);

        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetColumn(line, 1);
        root.Children.Add(line);

        // right: the page, the keys under it
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        main.Children.Add(view);
        var footLine = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(footLine, 1);
        main.Children.Add(footLine);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        void Hint(string label, string keys, bool divider)
        {
            var t = Theme.T(label, 12, Theme.P.TextDim, Theme.Medium, false);
            t.Margin = new Thickness(0, 0, 3, 0);
            foot.Children.Add(t);
            foot.Children.Add(Keys.Caps(keys));
            if (divider) foot.Children.Add(new SmoothBorder { Width = 1, Height = 14, Background = Theme.P.Line, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        Hint(Tr("분류", "Section"), "Ctrl+Tab", true);
        Hint(Tr("바꾸기", "Change"), "Enter", true);
        Hint(Tr("고르기", "Pick"), "←→", true);
        Hint(Tr("닫기", "Close"), "Esc", false);
        Grid.SetRow(foot, 2);
        main.Children.Add(foot);
        Grid.SetColumn(main, 2);
        root.Children.Add(main);
        Content = root;
        Theme.Scaled(this, root); // 설정 > 일반 > 크기

        box.TextChanged += (_, _) =>
        {
            placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            view.Build(box.Text);
        };
        PreviewKeyDown += (_, e) =>
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                if (!view.HandleKey(key)) // an open list closes first
                {
                    if (box.Text.Length > 0) box.Clear();
                    else Close();
                }
                e.Handled = true;
                return;
            }
            if (view.HandleKey(key)) e.Handled = true;
        };
        SourceInitialized += (_, _) => Frame();
        Loaded += (_, _) =>
        {
            box.Focus();
            Keyboard.Focus(box);
        };
        Theme.Changed += OnTheme;
        Closed += (_, _) => Theme.Changed -= OnTheme;
        view.Build("");
    }

    private void OnTheme()
    {
        Frame();
        view.Build(box.Text);
    }

    private void Frame() => Shots.SolidCaption(this);


    /// The list again (a connection or the Tool changed state).
    public void Refresh() => view.Build(box.Text);

    public void Filter(string text) => box.Text = text;

    public void Page(string key) => view.GoForQa(key);

    public void SelectForQa(int i) => view.SelectForQa(i);

    public void DragHomeForQa(string key, int before) => view.DragHomeForQa(key, before);

    public void Capture(string file) => Shots.Save(root, file);
}
