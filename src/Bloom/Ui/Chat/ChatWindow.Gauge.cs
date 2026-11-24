using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// How full the model's memory is in an omp chat ("14.2k / 200k") and the 정리 button next to it. omp tidies the chat itself
/// when it gets long; 정리 asks it to do so now. Hidden on Claude and ChatGPT chats and until omp has told its numbers.
internal sealed partial class ChatWindow
{
    private readonly StackPanel gaugeBox = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private string gaugeKey = "";

    private void OnUsage(ChatThread t)
    {
        if (ReferenceEquals(t, thread)) Gauge();
    }

    /// The numbers' colour: faint, a caution colour from 70 %, red from 90 %.
    private static Brush GaugeTint(double percent) => percent >= 90 ? Theme.P.Danger : percent >= 70 ? CautionBrush() : Theme.P.TextDim;

    private static Brush CautionBrush()
    {
        var b = new SolidColorBrush(Theme.P.Dark ? Color.FromRgb(0xE8, 0xB0, 0x4A) : Color.FromRgb(0xB0, 0x6A, 0x00));
        b.Freeze();
        return b;
    }

    /// Rebuilds only when what it shows changed (it is called on every streamed word). On every omp chat — before omp has
    /// told its numbers in this run (a chat opened after a restart, a new one) the numbers read "—" until the next answer.
    private void Gauge()
    {
        bool omp = AiModels.Provider(thread.Model) == "omp";
        var usage = host.Chats.UsageOf(thread);
        bool busy = thread.Busy, tidying = host.Chats.IsCompacting(thread);
        string key = $"{omp}|{usage}|{busy}|{tidying}|{thread.Id}|{thread.Messages.Count > 0}|{Theme.P.Dark}";
        if (key == gaugeKey) return;
        gaugeKey = key;
        gaugeBox.Children.Clear();
        if (!omp) return;
        var numbers = usage is { } u
            ? Theme.T(u.Gauge, 12, GaugeTint(u.Percent), Theme.Medium, false)
            : Theme.T("— / —", 12, Theme.P.TextFaint, Theme.Medium, false);
        numbers.VerticalAlignment = VerticalAlignment.Center;
        numbers.ToolTip = usage is { } v
            ? Tr($"모델이 기억하는 대화 {v.Percent:0.#}% · 대화가 길어지면 omp가 스스로 요약합니다", $"Context used: {v.Percent:0.#}% · omp automatically compacts the chat when it gets long")
            : Tr("모델이 기억하는 대화의 양 — 다음 답이 끝나면 표시됩니다 · 대화가 길어지면 omp가 스스로 요약합니다", "Context usage — shown after the next reply finishes · omp automatically compacts long chats");
        gaugeBox.Children.Add(numbers);
        if (thread.Messages.Count == 0) return; // nothing to tidy in a new chat
        var tidy = TextButton(G.Refresh, tidying ? Tr("정리 중…", "Compacting…") : Tr("정리", "Compact"), () =>
        {
            if (thread.Busy || host.Chats.IsCompacting(thread)) return;
            if (!host.Chats.Compact(thread)) ShowToast(Tr("지금은 정리할 수 없습니다", "Cannot compact right now"));
        });
        tidy.Margin = new Thickness(8, 0, 0, 0);
        tidy.ToolTip = Tr("대화 앞부분을 요약해 모델이 기억하는 분량을 줄입니다 — 화면의 대화 기록은 그대로입니다 (답하는 중에는 사용할 수 없습니다)", "Summarizes earlier messages to free up model context — on-screen chat history stays intact (unavailable while replying)");
        if (busy || tidying) tidy.Opacity = 0.45;
        gaugeBox.Children.Add(tidy);
    }
}
