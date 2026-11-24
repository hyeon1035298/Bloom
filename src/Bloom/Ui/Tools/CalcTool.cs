using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bloom.Search;

namespace Bloom.Ui;

/// 계산기: the answer while you type (units and currency too, as in the search line); Enter copies it and keeps the
/// line below, where a click copies it again; the head's 기록 지우기 clears those lines.
internal sealed class CalcTool : ITool
{
    private readonly Grid view = new();
    private readonly StackPanel history = new() { Margin = new Thickness(8, 2, 8, 8) };
    private readonly TextBox input;
    private readonly TextBlock answer, detail;
    private readonly List<(string expr, string value, string copy)> lines = new();
    private readonly SmoothBorder clearButton;

    public string Key => "calc";
    public string Name => Tr("계산기", "Calculator");
    public char Glyph => G.Calculator;
    public FrameworkElement View => view;
    public FrameworkElement? Head => clearButton;
    public string? State => null;

    public CalcTool()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var (field, box) = ToolUi.Field(Tr("12*3 · 5km to mi · 100 usd", "12*3 · 5km to mi · 100 usd"), 15);
        input = box;
        field.Margin = new Thickness(12, 10, 12, 4);
        view.Children.Add(field);
        var result = new StackPanel { Margin = new Thickness(16, 6, 16, 8) };
        answer = Theme.T("", 22, Theme.P.Text, Theme.Semi);
        detail = Theme.T("", 11.5, Theme.P.TextFaint);
        result.Children.Add(answer);
        result.Children.Add(detail);
        Grid.SetRow(result, 1);
        view.Children.Add(result);
        var scroll = ToolUi.Scroll(history);
        Grid.SetRow(scroll, 2);
        view.Children.Add(scroll);
        clearButton = ToolUi.Button(G.Delete, Tr("기록 지우기", "Clear history"), () =>
        {
            lines.Clear();
            detail.Text = Tr("기록 삭제 완료", "History cleared");
            Fill();
        });
        input.TextChanged += (_, _) => Show();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            Keep();
        };
        Fill();
    }

    private void Show()
    {
        var a = Calculator.Evaluate(input.Text);
        answer.Text = a?.Value ?? "";
        detail.Text = a?.Detail ?? (input.Text.Trim().Length > 0 && a is null ? Tr("식을 계속 입력하세요", "Continue typing expression") : "");
    }

    private void Keep()
    {
        if (Calculator.Evaluate(input.Text) is not { } a) return;
        string copy = a.Copy.Length > 0 ? a.Copy : a.Value;
        Launch.Copy(copy);
        lines.Insert(0, (a.Expr, a.Value, copy));
        if (lines.Count > 30) lines.RemoveAt(lines.Count - 1);
        detail.Text = Tr("복사 완료", "Copied");
        input.SelectAll();
        Fill();
    }

    private void Fill()
    {
        history.Children.Clear();
        clearButton.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (lines.Count == 0)
        {
            history.Children.Add(ToolUi.Empty(Tr("Enter로 결과 복사 및 기록 저장", "Press Enter to copy result and save to history")));
            return;
        }
        foreach (var (expr, value, copy) in lines)
        {
            var g = new StackPanel();
            g.Children.Add(Theme.T(value, 14, Theme.P.Text, Theme.Semi));
            var sub = Theme.T(expr, 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 1, 0, 0);
            g.Children.Add(sub);
            var row = ToolUi.Row(g, () =>
            {
                Launch.Copy(copy);
                detail.Text = Tr($"복사 완료 · {value}", $"Copied · {value}");
            });
            row.ToolTip = Tr("눌러서 복사", "Click to copy");
            history.Children.Add(row);
        }
    }

    /// QA: the line as typed so far.
    public void TypeForQa(string text) => input.Text = text;

    public void FocusFirst() => input.Dispatcher.BeginInvoke(() =>
    {
        input.Focus();
        Keyboard.Focus(input);
    }, DispatcherPriority.Input);

    public void Closed()
    {
    }
}
