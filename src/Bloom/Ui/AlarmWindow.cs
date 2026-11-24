using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// A timer or reminder that came due: a small card at the bottom right of the main screen (it doesn't take the
/// focus from what the user is doing), a sound every few seconds for half a minute, 끄기 / 5분 뒤 다시.
internal sealed class AlarmWindow : Window
{
    private static int open; // stacked cards move up
    private readonly DispatcherTimer ring = new() { Interval = TimeSpan.FromSeconds(4) };
    private int rings;

    public AlarmWindow(TimerEntry e, Action<TimerEntry> snooze, bool sound)
    {
        Title = Tr("Bloom 알림", "Bloom Notification");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        UseLayoutRounding = true;

        var title = Theme.T(e.Kind == "timer" ? Tr("타이머 끝", "Timer Finished") : Tr("알림", "Reminder"), 12, Theme.P.AccentText, Theme.Semi, false);
        var label = Theme.T(Hangul.KeepAll(e.Label), 15, Theme.P.Text, Theme.Medium);
        label.MaxWidth = 300;
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(0, 4, 0, 2);
        var when = Theme.T(Timers.WhenText(e.DueAt), 11.5, Theme.P.TextFaint, Theme.Regular, false);
        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(label);
        text.Children.Add(when);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Button(Tr("5분 뒤 다시", "Snooze 5 min"), false, () =>
        {
            snooze(e);
            Close();
        }));
        buttons.Children.Add(Button(Tr("끄기", "Turn off"), true, Close));

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Icons.Tile(e.Kind == "timer" ? G.Stopwatch : G.Calendar, true, 34);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 12, 0);
        head.Children.Add(icon);
        Grid.SetColumn(text, 1);
        head.Children.Add(text);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(buttons);
        var card = new SmoothBorder
        {
            Background = Theme.P.Solid,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 14, 12),
            MinWidth = 300,
            Margin = new Thickness(10),
            Child = body,
        };
        card.Lift(18, 3, 0.35);
        Content = card;

        int slot = open++;
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 8;
            Top = wa.Bottom - ActualHeight - 8 - slot * (ActualHeight - 6);
        };
        SourceInitialized += (_, _) => Glass.ToolWindow(this);
        KeyDown += (_, k) =>
        {
            if (k.Key is Key.Escape or Key.Enter) Close();
        };
        Closed += (_, _) =>
        {
            ring.Stop();
            open = Math.Max(0, open - 1);
        };
        if (sound)
        {
            ring.Tick += (_, _) =>
            {
                if (++rings > 7) ring.Stop();
                else Chime();
            };
            Loaded += (_, _) =>
            {
                Chime();
                ring.Start();
            };
        }
    }

    private static void Chime()
    {
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Alarm01.wav");
            if (File.Exists(wav)) new SoundPlayer(wav).Play();
            else SystemSounds.Asterisk.Play();
        }
        catch
        {
            // no sound device: the card still shows
        }
    }

    private static Border Button(string label, bool primary, Action click)
    {
        var b = new SmoothBorder
        {
            Child = Theme.T(label, 12.5, primary ? Theme.P.AccentInk : Theme.P.Text, Theme.Medium, false),
            Background = primary ? Theme.P.Accent : Theme.P.Key,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 5, 12, 6),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }
}
