using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// AskUserQuestion on a notice card (NoticeWindow): each question under its header, its choices (one to pick, or
/// several when it allows), and a line to type an answer of one's own (기타); 답하기 sends them once every question has
/// one. A click in here stays here: elsewhere on the card it still goes to the terminal.
internal static class NoticeQuestionsView
{
    public static FrameworkElement Build(CardQuestions asked, IReadOnlyList<(string Text, Action Run)>? buttons)
    {
        var answers = new Func<string>[asked.Items.Count];
        SmoothBorder? send = null;
        bool sent = false;
        bool Complete() => answers.All(a => a().Length > 0);
        void Changed()
        {
            if (send is not null) send.Opacity = Complete() ? 1 : 0.45;
        }
        void Send()
        {
            if (sent || !Complete()) return;
            sent = true;
            var given = new Dictionary<string, string>();
            for (int i = 0; i < answers.Length; i++) given[asked.Items[i].Text] = answers[i]();
            asked.Answer(given);
        }
        var list = new StackPanel();
        for (int i = 0; i < asked.Items.Count; i++)
        {
            var block = Question(asked.Items[i], out answers[i], Changed, Send);
            if (i > 0) block.Margin = new Thickness(0, 12, 0, 0);
            list.Children.Add(block);
        }
        var scroll = ToolUi.Scroll(list);
        scroll.MaxHeight = 420;
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        send = ToolUi.TextButton(Tr("답하기", "Answer"), Send);
        send.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(send);
        foreach (var (label, run) in buttons ?? [])
        {
            var b = ToolUi.TextButton(label, run);
            b.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(b);
        }
        Changed();
        var all = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        all.Children.Add(scroll);
        all.Children.Add(row);
        var area = new Border { Background = Brushes.Transparent, Cursor = Cursors.Arrow, Child = all };
        area.MouseLeftButtonUp += (_, e) => e.Handled = true; // not the card's own click (the terminal)
        return area;
    }

    /// One question: its header and text, a row per choice, then 기타. `answer` reads what is picked now ("" none).
    private static FrameworkElement Question(CardQuestion q, out Func<string> answer, Action changed, Action send)
    {
        var picked = new HashSet<int>();
        bool typedOn = false;
        var stack = new StackPanel();
        string top = q.Header.Length > 0 && q.Multi ? $"{q.Header} · {Tr("여러 개 선택", "Pick any")}" : q.Header.Length > 0 ? q.Header : q.Multi ? Tr("여러 개 선택", "Pick any") : "";
        if (top.Length > 0) stack.Children.Add(Theme.T(top, 11, Theme.P.TextFaint, Theme.Semi));
        var text = Theme.T(Hangul.KeepAll(q.Text), 13, Theme.P.Text, Theme.Medium, false);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 2, 0, 4);
        stack.Children.Add(text);

        var marks = new List<SmoothBorder>();
        var typedMark = Mark(q.Multi);
        var box = new TextBox { FontFamily = Theme.Sans, FontSize = 12.5, Foreground = Theme.P.Text, CaretBrush = Theme.P.Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 1, 0, 2), Template = Launcher.BareTextBox(), VerticalAlignment = VerticalAlignment.Center };
        void Paint()
        {
            for (int i = 0; i < marks.Count; i++) Show(marks[i], picked.Contains(i));
            Show(typedMark, typedOn);
            changed();
        }
        for (int i = 0; i < q.Choices.Count; i++)
        {
            int at = i;
            var c = q.Choices[i];
            var words = new StackPanel();
            var label = Theme.T(Hangul.KeepAll(c.Label), 12.5, Theme.P.Text, Theme.Medium, false);
            label.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(label);
            if (c.Description.Length > 0 && c.Description != c.Label)
            {
                var about = Theme.T(Hangul.KeepAll(c.Description), 11.5, Theme.P.TextFaint, Theme.Regular, false);
                about.TextWrapping = TextWrapping.Wrap;
                about.Margin = new Thickness(0, 1, 0, 0);
                words.Children.Add(about);
            }
            var mark = Mark(q.Multi);
            marks.Add(mark);
            var row = Row(mark, words);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (q.Multi)
                {
                    if (!picked.Remove(at)) picked.Add(at);
                }
                else
                {
                    picked.Clear();
                    picked.Add(at);
                    typedOn = false;
                }
                Paint();
            };
            stack.Children.Add(row);
        }

        // 기타: an answer of one's own; typing picks it (in place of a choice, when only one is picked)
        var hint = Theme.T(Tr("기타 (직접 입력)", "Other (type your own)"), 12.5, Theme.P.TextFaint, Theme.Regular);
        hint.IsHitTestVisible = false;
        var field = new Grid { VerticalAlignment = VerticalAlignment.Center };
        field.Children.Add(hint);
        field.Children.Add(box);
        var well = new SmoothBorder { Background = Theme.P.Well, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 3), Child = field, Cursor = Cursors.IBeam };
        typedMark.VerticalAlignment = VerticalAlignment.Center; // beside the line, not its top
        typedMark.Margin = new Thickness(0, 0, 9, 0);
        var typedRow = Row(typedMark, well);
        box.TextChanged += (_, _) =>
        {
            bool has = !string.IsNullOrWhiteSpace(box.Text);
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            if (has && !q.Multi) picked.Clear();
            typedOn = has;
            Paint();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            send();
        };
        // the cards never take the focus: the window does while this line is typed in, and gives it back after
        void Typing(bool on)
        {
            if (Window.GetWindow(box) is NoticeWindow w) w.Typing(on);
        }
        well.PreviewMouseLeftButtonDown += (_, _) =>
        {
            Typing(true);
            box.Focus();
        };
        typedMark.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Typing(true);
            box.Focus();
        };
        box.LostKeyboardFocus += (_, _) => Typing(false);
        box.Unloaded += (_, _) => Typing(false);
        stack.Children.Add(typedRow);

        answer = () => CardQuestions.Joined(q, picked, typedOn ? box.Text : null);
        Paint();
        return stack;
    }

    /// A choice's row: its mark, then what it says.
    private static SmoothBorder Row(SmoothBorder mark, FrameworkElement words)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(mark);
        Grid.SetColumn(words, 1);
        g.Children.Add(words);
        var row = new SmoothBorder { CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(-6, 0, 0, 0), Background = Brushes.Transparent, Child = g };
        row.MouseEnter += (_, _) => row.Background = Theme.P.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// A round mark for a question with one answer, a square one for several; filled with a check when picked.
    private static SmoothBorder Mark(bool multi) => new()
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(multi ? 4 : 8),
        BorderThickness = new Thickness(1.5),
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 9, 0),
        Child = Theme.Glyph(G.Check, 9, Theme.P.AccentInk),
    };

    private static void Show(SmoothBorder mark, bool on)
    {
        mark.Background = on ? Theme.P.Accent : Brushes.Transparent;
        mark.BorderBrush = on ? Theme.P.Accent : Theme.P.TextFaint;
        mark.Child.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }
}
