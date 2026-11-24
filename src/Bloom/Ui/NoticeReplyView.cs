using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// A T3 session's 진행 중 card (NoticeWindow): a line to type more words for its turn, a send key at its end, and the
/// card's buttons (정지, T3에서 열기) under it. Folded until the card is pointed at, it stays open while it is typed in
/// or holds words; a send that went folds it and says how it went. A click in here stays here: elsewhere on the card it
/// still goes to the session.
internal sealed class NoticeReplyView : Border
{
    private readonly Func<string, Task<(bool Ok, string Note)>> reply;
    private readonly TextBox box;
    private readonly TextBlock hint, note;
    private readonly SmoothBorder well, send;
    private readonly StackPanel open;
    private UIElement? card;
    private bool sending;

    public NoticeReplyView(Func<string, Task<(bool Ok, string Note)>> reply, IReadOnlyList<(string Text, Action Run)>? buttons)
    {
        this.reply = reply;
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        box = new TextBox { FontFamily = Theme.Sans, FontSize = 12.5, Foreground = Theme.P.Text, CaretBrush = Theme.P.Accent, SelectionBrush = Theme.P.Selection, SelectionOpacity = 1, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 0, 2), Template = Launcher.BareTextBox(), VerticalAlignment = VerticalAlignment.Center, MaxLength = RunCardProtocol.MaxSay };
        hint = Theme.T(Tr("이어서 보낼 말", "Type a follow-up message"), 12.5, Theme.P.TextFaint, Theme.Regular);
        hint.IsHitTestVisible = false;
        var field = new Grid { VerticalAlignment = VerticalAlignment.Center };
        field.Children.Add(hint);
        field.Children.Add(box);
        well = new SmoothBorder { Background = Theme.P.Well, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Padding = new Thickness(8, 4, 8, 4), MinHeight = 30, Child = field, Cursor = Cursors.IBeam };
        send = new SmoothBorder { Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Background = Theme.P.Accent, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0), ToolTip = Tr("보내기 (Enter)", "Send (Enter)"), Child = Theme.Glyph(G.Send, 12, Theme.P.AccentInk) };
        send.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = Send();
        };
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(well);
        Grid.SetColumn(send, 1);
        line.Children.Add(send);

        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, run) in buttons ?? [])
        {
            var b = ToolUi.TextButton(label, run);
            b.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(b);
        }
        open = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        open.Children.Add(line);
        if (row.Children.Count > 0) open.Children.Add(row);
        note = Theme.T("", 11.5, Theme.P.Accent, Theme.Medium);
        note.Margin = new Thickness(0, 8, 0, 0);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.Visibility = Visibility.Collapsed;
        Child = new StackPanel { Children = { open, note } };

        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            Paint();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = Send();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Leave();
            }
        };
        // the cards never take the focus: the window does while this line is typed in, and gives it back after
        well.PreviewMouseLeftButtonDown += (_, _) =>
        {
            Typing(true);
            box.Focus();
        };
        box.GotKeyboardFocus += (_, _) => Paint();
        box.LostKeyboardFocus += (_, _) =>
        {
            Typing(false);
            Paint();
        };
        box.Unloaded += (_, _) => Typing(false);
        MouseLeftButtonUp += (_, e) => e.Handled = true; // not the card's own click (the session)
        Paint();
    }

    /// The card this sits on: pointing at it opens the line.
    public void Follow(UIElement on)
    {
        card = on;
        on.MouseEnter += (_, _) => Paint();
        on.MouseLeave += (_, _) => Paint();
    }

    private void Typing(bool on)
    {
        if (Window.GetWindow(this) is NoticeWindow w) w.Typing(on);
    }

    /// Open while the card is pointed at, typed in, holding words or sending them.
    private void Paint()
    {
        bool shown = card?.IsMouseOver == true || box.IsKeyboardFocusWithin || box.Text.Length > 0 || sending;
        open.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        well.BorderBrush = box.IsKeyboardFocusWithin ? Theme.P.Accent : Brushes.Transparent;
        send.Opacity = !sending && !string.IsNullOrWhiteSpace(box.Text) ? 1 : 0.45;
    }

    /// Out of the line: the focus goes back to where it was, and the line folds when the card isn't pointed at.
    private void Leave()
    {
        Keyboard.ClearFocus();
        Typing(false);
        Paint();
    }

    private async Task Send()
    {
        string words = box.Text.Trim();
        if (sending || words.Length == 0) return;
        sending = true;
        box.IsReadOnly = true;
        Paint();
        (bool Ok, string Note) went;
        try
        {
            went = await reply(words);
        }
        catch (Exception e)
        {
            Core.Log.Error("notice reply", e);
            went = (false, Tr("보내지 못했어요", "Failed to send"));
        }
        sending = false;
        box.IsReadOnly = false;
        note.Text = went.Note;
        note.Foreground = went.Ok ? Theme.P.Accent : Theme.P.Danger;
        note.Visibility = Visibility.Visible;
        if (went.Ok)
        {
            box.Clear();
            Leave();
        }
        else Paint(); // the words stay, to send again
    }
}
