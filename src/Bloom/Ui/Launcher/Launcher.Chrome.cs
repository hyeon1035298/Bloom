using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// The search line's lead, chip and hints, and the footer, for each view.
internal sealed partial class Launcher
{
    private void UpdateChrome()
    {
        string ph;
        string? chipLabel = null;
        FrameworkElement leadEl;
        hint.Children.Clear();
        footRight.Children.Clear();
        switch (Mode)
        {
            case Mode.Chat:
                leadEl = Theme.Sparkle(17, Theme.P.Accent);
                chipLabel = attachedImage is not null ? Tr("화면 · Bloom Lens", "Screen · Bloom Lens") : attachedFiles.Count > 0 ? Tr($"첨부 {attachedFiles.Count}개", $"{attachedFiles.Count} attached") : attachment is { } a ? (a.url.Length > 0 ? Tr($"페이지 · {ClipText(a.title, 16)}", $"Page · {ClipText(a.title, 16)}") : ClipText(a.title, 16)) : null;
                bool busy = thread?.Busy == true;
                ph = attachedImage is not null ? Tr("이 화면에 대해 물어보세요 (비우고 Enter = 설명해 줘)", "Ask about this screen (empty + Enter = explain it)")
                    : attachedFiles.Count > 0 ? Tr("첨부한 파일에 대해 물어보세요", "Ask about the attached files")
                    : thread is { Messages.Count: > 0 } ? Tr("이어서 묻기…", "Ask a follow-up…") : Tr("무엇이든 물어보세요…", "Ask anything…");
                // few chips, so what is typed keeps the room: [■ 정지] while it answers, else [model · level ▾] (PickModel),
                // then [세션 종료] while its CLI runs; 새 대화 is in the footer, 📎 at the bar's end
                if (busy)
                {
                    var stop = HeadChip("■  " + Tr("중지", "Stop"), StopChat, true);
                    stop.ToolTip = Tr("답변 중지 (Ctrl+.) · Esc는 홈으로 이동하며 답변은 계속됩니다.", "Stops the answer (Ctrl+.) · Esc returns home while the answer continues.");
                    hint.Children.Add(stop);
                }
                else
                {
                    string curModel = CurrentModel;
                    string level = Ai.AiModels.HasEffort(curModel) ? " · " + Ai.AiModels.EffortName(thread is null ? Ai.AiModels.DefaultEffort(curModel) : Chats.EffortOf(thread)) : "";
                    var modelChip = HeadChip(Engine.ModelName(curModel) + level + "  ▾", PickModel);
                    modelChip.ToolTip = Tr("모델 · 생각 수준 (Ctrl+M · Ctrl+E · Ctrl+Shift+M AI 전환)", "Model and thinking level (Ctrl+M · Ctrl+E · Ctrl+Shift+M switches AI)");
                    hint.Children.Add(modelChip);
                }
                if (thread is not null && host.Chats.IsLive(thread))
                {
                    var t = thread;
                    var endChip = HeadChip(Tr("세션 종료", "End session"), () =>
                    {
                        host.Chats.EndSession(t);
                        ShowChat(host.Chats.New());
                        ShowToast(Tr("세션 종료 · 새 대화 열림 · 지난 대화 기록 유지", "Session closed, a new chat is open — the last one stays in the list"));
                    });
                    endChip.Margin = new Thickness(6, 0, 0, 0);
                    endChip.ToolTip = Tr("대화 메모리를 해제하고 새 대화를 엽니다. 대화 기록은 유지됩니다.", "Frees chat memory and opens a new chat. History stays.");
                    hint.Children.Add(endChip);
                }
                quickUnseen = false;
                if (busy) Foot(Tr("중지", "Stop"), "Ctrl+.", StopChat);
                else Foot(Tr("보내기", "Send"), "Enter");
                Divider();
                Foot(Tr("홈", "Home"), "Esc");
                Divider();
                Foot(Tr("새 대화", "New chat"), "Ctrl+N", () => ShowChat(host.Chats.New()));
                Divider();
                Foot(Tr("더보기", "More"), "Ctrl+K", OpenActions);
                footContext.Text = thread is { Messages.Count: > 0 } ? thread.Title : Tr("AI 채팅", "AI chat"); // trimmed to the footer's room
                work.Working = busy;
                break;
            case Mode.Form:
                leadEl = Back();
                chipLabel = form?.Title;
                ph = form?.Current.Hint ?? "";
                bool last = form is { } ff && ff.Step == ff.Fields.Count - 1;
                Foot(last ? Tr("저장", "Save") : Tr("다음", "Next"), "Enter");
                Divider();
                Foot(form is { Step: > 0 } ? Tr("이전", "Back") : Tr("취소", "Cancel"), form is { Step: > 0 } ? "Backspace" : "Esc");
                footContext.Text = form is { } fs ? $"{fs.Title} · {fs.Current.Label}" : "";
                break;
            default:
                leadEl = Mode == Mode.Root ? Theme.Glyph(G.Search, 17, Theme.P.TextFaint) : Back();
                var spec = View(Mode);
                chipLabel = spec.Chip();
                ph = spec.Placeholder();
                if (Mode == Mode.Files)
                    hint.Children.Add(HeadChip(Engine.FileKind(fileKind).name + "  ▾", PickFileKind));
                if (Mode == Mode.Root && QuickPending)
                {
                    var t = thread!;
                    var back = HeadChip("✦  " + (t.Busy ? Tr("빠른 AI 답 쓰는 중 · Tab", "Quick AI answering · Tab") : Tr("빠른 AI 답변 완료 · Tab", "Quick AI answer ready · Tab")), () => ShowChat(t), true);
                    back.ToolTip = ClipText(t.Title, 60);
                    hint.Children.Add(back);
                }
                var item = Selected;
                if (item?.Run is not null && item.PrimaryLabel.Length > 0)
                {
                    Foot(item.PrimaryLabel, "Enter");
                    Divider();
                }
                Foot(Tr("더보기", "More"), "Ctrl+K");
                if (Mode == Mode.Root && box.Text.Length == 0)
                {
                    Divider();
                    footRight.Children.Add(HomeEditButton());
                }
                footContext.Text = spec.Context();
                break;
        }
        CornerButtons();
        lead.Content = leadEl;
        placeholder.Text = ph;
        placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        chip.Visibility = chipLabel is null ? Visibility.Collapsed : Visibility.Visible;
        chipText.Text = chipLabel ?? "";
    }

    /// A small clickable chip at the right of the search line (the chat's model and level); `accent` for one that
    /// stands for something going on (■ 정지, the quick AI still answering).
    private static Border HeadChip(string label, Action click, bool accent = false)
    {
        var chip = new SmoothBorder
        {
            Child = Theme.T(label, 12, accent ? Theme.P.AccentText : Theme.P.TextDim, Theme.Medium, false),
            Background = accent ? Theme.P.AccentSoft : Theme.P.Tile,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return chip;
    }

    private FrameworkElement Back()
    {
        var g = Theme.Glyph('', 14, Theme.P.TextDim);
        var b = new SmoothBorder { Child = g, Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            Go(Mode.Root);
            e.Handled = true;
        };
        return b;
    }

    private static string ClipText(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    /// A footer hint: its key caps and what they do; with `click` the pair is a button too (■ 정지).
    private void Foot(string label, string keys, Action? click = null)
    {
        var caps = Keys.Caps(keys);
        var t = Theme.T(label, 12, Theme.P.TextDim, Theme.Medium, false);
        t.Margin = new Thickness(6, 0, 0, 0);
        if (click is null)
        {
            footRight.Children.Add(caps);
            footRight.Children.Add(t);
            return;
        }
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(caps);
        sp.Children.Add(t);
        var b = new SmoothBorder { Child = sp, CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 2, 6, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        footRight.Children.Add(b);
    }

    /// The home's layout button (footer, right end): 홈 편집 ↔ 편집 끝 (Ctrl+E).
    private FrameworkElement HomeEditButton()
    {
        bool on = HomeEditing;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(on ? G.Check : '\uE70F', 11, on ? Theme.P.AccentInk : Theme.P.TextDim);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 5, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.T(on ? Tr("편집 끝", "Done") : Tr("홈 편집", "Edit home"), 12, on ? Theme.P.AccentInk : Theme.P.TextDim, Theme.Medium, false));
        var b = new SmoothBorder
        {
            Child = sp,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 9, 4),
            Background = on ? Theme.P.Accent : Theme.P.Key,
            Cursor = Cursors.Hand,
            ToolTip = on ? Tr("편집 끝 (Esc · Ctrl+E)", "Done (Esc · Ctrl+E)") : Tr("홈 화면 블록 순서 · 숨기기 (Ctrl+E)", "Order or hide the home blocks (Ctrl+E)"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!on)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.Selected;
            b.MouseLeave += (_, _) => b.Background = Theme.P.Key;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            ToggleHomeEdit();
            e.Handled = true;
        };
        return b;
    }

    private void Divider() => footRight.Children.Add(new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = Theme.P.TextFaint, Opacity = 0.7, Margin = new Thickness(11, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center });
}
