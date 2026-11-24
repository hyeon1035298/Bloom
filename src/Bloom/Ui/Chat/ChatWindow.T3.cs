using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The chat window's T3 side: a Bloom | T3 switch over the list; on T3 the threads
/// T3 Code runs, by project (folder), each with its state (a rose ring: working, orange: it waits on the user), five a
/// project until 더 보기; one opened reads as a chat (T3Chats follows it), its bar naming its project, its model and
/// T3에서 열기; 새 대화 there is a new T3 thread whose empty page offers T3's projects as cards, and 폴더 없이.
internal sealed partial class ChatWindow
{
    private Segmented? sides;
    private SmoothBorder? sidesRow;
    private TextBlock? findHint;
    private FrameworkElement? pickKey;
    private DispatcherTimer? findDelay;

    private T3Chats T3 => host.Chats.T3;

    /// The list shows T3's threads (the switch is on T3 and T3 is there to talk to).
    private bool OnT3 => Settings.Current.ChatSide == "t3" && T3.Present;

    /// 새 대화 / Ctrl+N: a chat on the side the list shows.
    private ChatThread NewChat() => OnT3 ? T3.Draft() : host.Chats.New();

    /// The Bloom | T3 switch, under 새 대화; hidden while T3 isn't there (설정 > AI 'T3 Code 연결' off, or no T3 Code).
    private FrameworkElement SideSwitch()
    {
        sides = new Segmented(new[] { ("bloom", "Bloom"), ("t3", "T3") }, OnT3 ? "t3" : "bloom") { HorizontalAlignment = HorizontalAlignment.Stretch };
        sides.Picked += side =>
        {
            Settings.Current.ChatSide = side;
            Settings.Save();
            if (selecting) EndSelect();
            find.Clear();
            Hints();
            if (side == "t3") _ = T3.Refresh(query: "", projects: true);
            BuildList();
        };
        sidesRow = new SmoothBorder { Margin = new Thickness(10, 0, 10, 8), Child = sides };
        PaintSides();
        return sidesRow;
    }

    private void PaintSides()
    {
        if (sidesRow is not null) sidesRow.Visibility = T3.Present ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The filter's words (and the key hint's under the chat): 대화 찾기 on Bloom's side, 스레드 찾기 on T3's, where ✓
    /// (picking chats to delete) is gone.
    private void Hints()
    {
        if (findHint is not null) findHint.Text = OnT3 ? Tr("스레드 찾기", "Search threads") : Tr("대화 찾기", "Search chats");
        if (findFoot is not null) findFoot.Text = OnT3 ? Tr("스레드 찾기", "Search threads") : Tr("대화 찾기", "Search chats");
        if (pickKey is not null) pickKey.Visibility = OnT3 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// Typed in the filter on T3's side: the list narrows at once, and T3 is asked (titleContains) a moment later.
    private void FindOnT3()
    {
        findDelay ??= NewDelay();
        findDelay.Stop();
        findDelay.Start();
    }

    private DispatcherTimer NewDelay()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (OnT3) _ = T3.Refresh(query: find.Text.Trim());
        };
        return t;
    }

    private System.Collections.Generic.IEnumerable<T3Row> T3Matches()
    {
        var q = new Query(find.Text);
        return T3.Rows.Where(r => q.IsEmpty || Matcher.Score(q, new MatchTarget(r.Title)) > 0);
    }

    /// The T3 side's list: by project, five threads each until 더 보기, then 더 불러오기 when T3 has more.
    private void BuildT3List()
    {
        bool filtering = find.Text.Trim().Length > 0;
        var groups = T3Chats.Groups(T3Matches(), T3.Projects);
        foreach (var (projectId, name, rows) in groups)
        {
            list.Children.Add(ProjectLabel(name, projectId is null));
            string key = projectId ?? T3Chats.ScratchKey;
            bool all = filtering || T3.Unfolded.Contains(key);
            foreach (var row in all ? rows : rows.Take(T3Chats.PerProject)) list.Children.Add(T3Item(row));
            if (!all && rows.Count > T3Chats.PerProject)
                list.Children.Add(MoreLink(Tr($"더 보기 ({rows.Count - T3Chats.PerProject})", $"Show more ({rows.Count - T3Chats.PerProject})"), () =>
                {
                    T3.Unfolded.Add(key);
                    BuildList();
                }));
        }
        if (T3.MoreCursor is not null && T3.ListState == "")
            list.Children.Add(MoreLink(Tr("스레드 더 불러오기", "Load more threads"), () => _ = T3.Refresh(more: true)));
        string? note = T3.ListState switch
        {
            "loading" => Tr("T3의 스레드를 읽는 중…", "Loading T3 threads…"),
            "t3-off" => Tr("T3에 닿지 못했어요. T3 Code가 켜져 있는지 보세요.", "Could not connect to T3. Make sure T3 Code is running."),
            "failed" => Tr("T3의 스레드를 읽지 못했어요.", "Could not load threads from T3."),
            _ when groups.Count == 0 => filtering ? Tr("검색된 스레드 없음", "No matching threads") : Tr("T3에 스레드가 없어요. 새 대화로 하나 만드세요.", "No threads in T3. Start a new chat to create one."),
            _ => null,
        };
        if (note is not null)
        {
            var none = Theme.T(Hangul.KeepAll(note), 12.5, Theme.P.TextFaint, null, false);
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(22, 14, 18, 0);
            list.Children.Add(none);
        }
        running.Content = null;
    }

    /// A project's line over its threads: the folder glyph and its name.
    private static FrameworkElement ProjectLabel(string name, bool scratch)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 12, 12, 5) };
        var g = Theme.Glyph(scratch ? G.Add : G.Folder, 11, Theme.P.TextFaint);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 7, 0);
        sp.Children.Add(g);
        var t = Theme.T(name, 11.5, Theme.P.TextFaint, Theme.Semi, false);
        t.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(t);
        return sp;
    }

    private static FrameworkElement MoreLink(string text, Action click)
    {
        var t = Theme.T(text, 12, Theme.P.AccentText, Theme.Medium, false);
        var b = new SmoothBorder { Child = t, CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(10, 1, 10, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// One T3 thread: its state's dot, its title, its model and when it last changed; a settled one (marked done in T3)
    /// faint.
    private SmoothBorder T3Item(T3Row row)
    {
        bool on = thread.T3?.ThreadId == row.Id;
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = StateDot(row);
        dot.Margin = new Thickness(0, 1, 8, 0);
        head.Children.Add(dot);
        var name = Theme.T(row.Title.Length > 0 ? row.Title : Tr("제목 없는 스레드", "Untitled thread"), 13.5, Theme.P.Text, on ? Theme.Semi : Theme.Medium);
        head.Children.Add(name);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(head);
        string model = row.Instance.Length > 0 && row.Model.Length > 0 ? AiModels.Name(AiModels.T3(row.Instance, row.Model)) : "T3";
        bool asking = row.Pending > 0 || row.Status == "waiting";
        string sub = asking ? Tr("답을 기다리는 중", "Waiting for reply") : row.Active ? Tr("일하는 중…", "Working…") : model + "  ·  " + Engine.Ago(row.Updated.ToLocalTime());
        var s = Theme.T(sub, 11.5, asking ? Theme.P.Warn : row.Active ? Theme.P.AccentText : Theme.P.TextFaint);
        s.Margin = new Thickness(15, 3, 0, 0);
        words.Children.Add(s);
        var item = new SmoothBorder
        {
            Height = 52,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(10, 1, 10, 1),
            Padding = new Thickness(12, 0, 6, 0),
            Background = on ? Theme.P.Selected : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = words,
            Opacity = row.Settled && !on ? 0.55 : 1,
            ToolTip = row.Settled ? Tr("T3에서 끝난 스레드", "Settled thread in T3") : null,
        };
        item.MouseEnter += (_, _) =>
        {
            if (!on) item.Background = Theme.P.Hover;
        };
        item.MouseLeave += (_, _) =>
        {
            if (!on) item.Background = Brushes.Transparent;
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            Open(T3.Chat(row));
            e.Handled = true;
        };
        return item;
    }

    /// A rose ring while T3 works on the thread, orange while it waits on the user, a faint dot otherwise.
    private static Ellipse StateDot(T3Row row)
    {
        bool asking = row.Pending > 0 || row.Status == "waiting";
        var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        if (asking) dot.Fill = Theme.P.Warn;
        else if (row.Active)
        {
            dot.Stroke = Theme.P.Accent;
            dot.StrokeThickness = 1.6;
        }
        else dot.Fill = Theme.P.Line;
        return dot;
    }

    // ── a T3 chat's bar ────────────────────────────────────────────────────────────────────────

    /// The bar of a T3 chat: its project faint in front of its name; the model (a list to pick from for a new one) under
    /// a T3 tag; T3에서 열기 once the thread is there.
    private void T3Header()
    {
        var r = thread.T3!;
        System.Collections.Generic.List<string>? ids = null;
        if (r.ThreadId is null && thread.Messages.Count == 0)
        {
            ids = T3.Models();
            if (AiModels.T3Parts(thread.Model) is not null && !ids.Contains(thread.Model) && !T3Threads.AntigravityModel(thread.Model)) ids.Insert(0, thread.Model);
            // a new chat shows a model it can start on; nothing is saved until one is picked
            if (ids.Count > 0 && !ids.Contains(thread.Model)) thread.Model = ids[0];
        }
        var (project, name) = T3Title(thread);
        shownTitle = T3TitleKey(thread);
        title.Inlines.Clear();
        if (project.Length > 0 && (r.ThreadId is not null || thread.Messages.Count > 0))
        {
            title.Inlines.Add(new Run(project) { Foreground = Theme.P.TextFaint, FontWeight = Theme.Medium });
            title.Inlines.Add(new Run("  /  ") { Foreground = Theme.P.TextFaint, FontWeight = Theme.Regular });
        }
        title.Inlines.Add(new Run(name));
        tools.Children.Clear();
        tools.Children.Add(Badge());
        if (ids is not null)
        {
            if (ids.Count == 0)
            {
                tools.Children.Add(Chip(Tr("모델 없음", "No model")));
                return;
            }
            var models = new Dropdown(ids.Select(m => (m, AiModels.Who(m), (string?)T3Instance(m))).ToArray(), thread.Model) { ToolTip = Tr("모델 (Ctrl+M)", "Model (Ctrl+M)"), Margin = new Thickness(6, 0, 0, 0) };
            models.Picked += m =>
            {
                T3.SetModel(thread, m);
                Header();
            };
            tools.Children.Add(models);
            return;
        }
        var who = Chip(AiModels.Who(thread.Model));
        who.ToolTip = T3Instance(thread.Model);
        who.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(who);
        if (r.ThreadId is not null)
        {
            var open = TextButton(G.NewWindow, Tr("T3에서 열기", "Open in T3"), () =>
            {
                if (!T3Chats.OpenInT3(thread)) ShowToast(Tr("T3를 열지 못했어요", "Could not open T3"));
            });
            open.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(open);
        }
    }

    /// A T3 chat's bar: its project (폴더 없이 for a new scratch one) and its name.
    private (string Project, string Name) T3Title(ChatThread t)
    {
        var r = t.T3!;
        string project = r.ThreadId is null && r.Scratch ? Tr("폴더 없이", "No folder") : T3.Projects.FirstOrDefault(p => p.Id == r.ProjectId)?.Title ?? "";
        return (project, r.ThreadId is null && t.Messages.Count == 0 ? Tr("새 T3 스레드", "New T3 Thread") : t.Title);
    }

    /// What the bar of a T3 chat shows, to tell when it is to be drawn again.
    private string T3TitleKey(ChatThread t)
    {
        var (project, name) = T3Title(t);
        return project + "\n" + name + "\n" + t.Model + "\n" + t.T3!.ThreadId;
    }

    /// The T3 provider instance a model runs on, for its tooltip ("claudeAgent", an account's name).
    private static string? T3Instance(string model) => AiModels.T3Parts(model) is { } p ? Tr($"T3 계정 · {p.Instance}", $"T3 account · {p.Instance}") : null;

    /// The small T3 tag in front of the model.
    private static SmoothBorder Badge() => new()
    {
        Background = Theme.P.Key,
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(5, 1, 5, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Theme.T("T3", 10.5, Theme.P.Text, Theme.Semi, false),
    };

    private static SmoothBorder Chip(string text) => new()
    {
        Background = Theme.P.Well,
        BorderBrush = Theme.P.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(11, 3, 11, 4),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Theme.Label(text, 12, Theme.P.Text, Theme.Medium, inBorder: true),
    };

    // ── a new T3 chat: its folder cards ────────────────────────────────────────────────────────

    /// A new T3 chat's empty page: 어느 폴더에서 일할까요? over a card per project T3 keeps and 폴더 없이; the picked one in
    /// rose. Null for any other chat.
    private FrameworkElement? T3Empty(ChatThread t)
    {
        if (t.T3 is not { ThreadId: null } r) return null;
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0), Width = 460 };
        var big = Theme.T(Tr("어느 폴더에서 일할까요?", "Which folder would you like to work in?"), 17, Theme.P.Text, Theme.Medium, false);
        big.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(big);
        var sub = Theme.T(Tr("T3에 등록된 프로젝트입니다 · 보내면 T3에 새 스레드가 생깁니다", "Projects registered in T3 · Sending a message creates a new thread in T3"), 12.5, Theme.P.TextFaint, Theme.Regular, false);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 6, 0, 16);
        sp.Children.Add(sub);
        if (T3.Projects.Count == 0 && T3.ListState is "loading" or "t3-off" or "failed")
        {
            sp.Children.Add(ToolUi.Empty(T3.ListState == "loading" ? Tr("T3의 폴더를 읽는 중…", "Loading folders from T3…") : Tr("T3에 닿지 못했어요. T3 Code가 켜져 있는지 보세요.", "Could not connect to T3. Make sure T3 Code is running.")));
            return sp;
        }
        var cards = new UniformGrid { Columns = 2 };
        foreach (var p in T3.Projects)
        {
            int count = T3.Rows.Count(x => x.ProjectId == p.Id);
            string about = (p.Root ?? "") + (count > 0 ? (count == 1 ? Tr(" · 스레드 1", " · 1 thread") : Tr($" · 스레드 {count}", $" · {count} threads")) : "");
            cards.Children.Add(FolderCard(G.Folder, p.Title, about, !r.Scratch && r.ProjectId == p.Id, () => PickFolder(t, p.Id)));
        }
        cards.Children.Add(FolderCard(G.Add, Tr("폴더 없이", "No folder"), Tr("빈 작업 공간(scratch)", "Empty scratch workspace"), r.Scratch, () => PickFolder(t, null)));
        sp.Children.Add(cards);
        return sp;
    }

    private void PickFolder(ChatThread t, string? project)
    {
        T3.Pick(t, project);
        chat.Show(t);
        Header();
        PaintComposer();
        input.Focus();
    }

    private static SmoothBorder FolderCard(char glyph, string name, string about, bool on, Action click)
    {
        var col = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(glyph, 11, on ? Theme.P.AccentText : Theme.P.TextDim);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 7, 0);
        head.Children.Add(g);
        head.Children.Add(Theme.T(name, 13, on ? Theme.P.AccentText : Theme.P.Text, Theme.Semi));
        col.Children.Add(head);
        var b = Theme.T(about, 11.5, Theme.P.TextFaint, Theme.Regular);
        b.Margin = new Thickness(0, 3, 0, 0);
        col.Children.Add(b);
        var card = new SmoothBorder
        {
            Background = on ? Theme.P.AccentSoft : Theme.P.Card,
            BorderBrush = on ? Theme.P.Accent : Theme.P.CardLine,
            BorderThickness = new Thickness(on ? 1.5 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 10),
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            Child = col,
            ToolTip = about,
        };
        if (!on)
        {
            card.MouseEnter += (_, _) => card.Background = Theme.P.Hover;
            card.MouseLeave += (_, _) => card.Background = Theme.P.Card;
        }
        card.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return card;
    }

    // ── the composer of a T3 chat ──────────────────────────────────────────────────────────────

    /// The send key is ■ (a click stops the turn): T3 works on the thread, no question waits and nothing is typed
    /// (typed words go into the turn).
    /// Never on an Antigravity thread (view-only).
    private bool T3Stops => thread.T3 is { ThreadId: not null, ViewOnly: false } && thread.Busy && thread.Messages[^1].Question is null && input.Text.Trim().Length == 0;

    /// The send key and the line's hint as the chat is: ■ (T3Stops), and words that say where what is typed goes. An
    /// Antigravity thread has no line to type in nor send key: one line says it is view-only (what was typed stays for
    /// the next chat).
    private void PaintComposer()
    {
        bool viewOnly = thread.T3 is { ViewOnly: true };
        input.Visibility = sendKey.Visibility = viewOnly ? Visibility.Collapsed : Visibility.Visible;
        inputHint.Visibility = viewOnly || input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (viewOnly)
        {
            inputHint.Text = T3Threads.ViewOnlyNote;
            attachKey.Visibility = Visibility.Collapsed;
            if (attachFoot is not null) attachFoot.Visibility = Visibility.Collapsed;
            return;
        }
        bool t3 = thread.T3 is not null, stop = T3Stops;
        sendIcon.Text = (stop ? G.Stop : G.Send).ToString();
        sendKey.ToolTip = stop ? Tr("정지 (Esc)", "Stop (Esc)") : Tr("보내기 (Enter)", "Send (Enter)");
        attachKey.Visibility = t3 ? Visibility.Collapsed : Visibility.Visible;
        if (attachFoot is not null) attachFoot.Visibility = attachKey.Visibility;
        inputHint.Text = !t3 ? Tr("메시지 입력 — Enter로 보내고 Shift+Enter로 줄을 변경합니다", "Type a message — Enter to send, Shift+Enter for new line")
            : thread.Messages.LastOrDefault()?.Question is not null ? Tr("질문에 답하거나 이어서 보내기", "Answer the question or send a follow-up")
            : thread.T3!.ThreadId is null ? NewHint(thread.T3)
            : thread.Busy ? Tr("이어서 보낼 말 · 지금 턴에 넣어요", "Steer or add to the current turn")
            : Tr("T3 스레드에 이어서 보내기", "Follow up in T3 thread");
    }

    private string NewHint(T3Ref r) => r.Scratch ? Tr("새 작업 공간에서 할 일을 적으세요", "Describe what to do in the scratch workspace")
        : T3.Projects.FirstOrDefault(p => p.Id == r.ProjectId)?.Title is { } name ? Tr($"{name}에서 할 일을 적으세요", $"Describe what to do in {name}")
        : Tr("T3에 맡길 일을 적으세요", "Describe a task for T3");

    /// The window's place in front tells T3Chats how often to read the open T3 thread.
    private void WatchT3() => T3.Watch(thread.T3 is not null ? thread : null, IsVisible && WindowState != WindowState.Minimized, IsActive);

    private void OnT3List()
    {
        if (Dispatcher.HasShutdownStarted) return;
        PaintSides();
        if (OnT3) BuildList();
        if (thread.T3 is { ThreadId: null } && thread.Messages.Count == 0)
        {
            chat.Show(thread); // its folder cards
            Header();
        }
    }

    private void OnT3Note(ChatThread t, string note)
    {
        if (ReferenceEquals(t, thread)) ShowToast(note);
    }

    private void OnTurns() => Dispatcher.BeginInvoke(() => T3.Wake());
}
