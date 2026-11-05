using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The chat window's left column: 새 대화, a filter, the groups (ChatWindow.Groups), then the other conversations by day
/// (오늘 · 어제 · 지난 7일 · 이전), the open one in rose. A dot marks a conversation whose CLI session still runs; hovering
/// shows 세션 종료, ⋯ (groups, delete) and 삭제 (two clicks); a conversation can be dragged onto a group. Under the list,
/// how many sessions run and 모두 종료.
internal sealed partial class ChatWindow
{
    private TextBox find = null!;
    private readonly StackPanel list = new();
    private readonly ScrollViewer listScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Background = Brushes.Transparent };
    private readonly ContentControl running = new();
    private int listKey;

    private FrameworkElement BuildSide()
    {
        var side = new Grid();
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 새 대화
        var plus = Theme.Glyph(G.Add, 13, Theme.P.Accent);
        plus.VerticalAlignment = VerticalAlignment.Center;
        plus.Margin = new Thickness(0, 0, 10, 0);
        var label = Theme.T(Tr("새 대화", "New chat"), 13.5, Theme.P.AccentText, Theme.Semi);
        var caps = Keys.Caps("Ctrl+N");
        caps.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(plus);
        Grid.SetColumn(label, 1);
        g.Children.Add(label);
        Grid.SetColumn(caps, 2);
        g.Children.Add(caps);
        var fresh = new SmoothBorder
        {
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = Theme.P.AccentSoft,
            Padding = new Thickness(13, 0, 8, 0),
            Margin = new Thickness(10, 14, 10, 8),
            Cursor = Cursors.Hand,
            Child = g,
        };
        fresh.MouseLeftButtonUp += (_, e) =>
        {
            Open(host.Chats.New());
            e.Handled = true;
        };
        side.Children.Add(fresh);

        // 대화 찾기, built like the settings window's filter
        var bar = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = Theme.Glyph(G.Search, 13, Theme.P.TextFaint);
        glyph.Margin = new Thickness(0, 0, 9, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(glyph);
        find = new TextBox
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
        var placeholder = Theme.T(Tr("대화 찾기", "Find a chat"), 13.5, Theme.P.TextFaint);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var boxHost = new Grid();
        boxHost.Children.Add(placeholder);
        boxHost.Children.Add(find);
        Grid.SetColumn(boxHost, 1);
        bar.Children.Add(boxHost);
        var pick = SmallButton(G.Check, Tr("여러 개 골라서 지우기 (Ctrl+클릭)", "Pick several to delete (Ctrl+click)"), _ =>
        {
            if (selecting) EndSelect();
            else StartSelect();
        });
        pick.Margin = new Thickness(4, 0, 4, 0);
        Grid.SetColumn(pick, 2);
        bar.Children.Add(pick);
        var findCard = new SmoothBorder
        {
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10, 0, 10, 6),
            Child = bar,
            Cursor = Cursors.IBeam,
        };
        findCard.MouseLeftButtonDown += (_, _) => find.Focus();
        Grid.SetRow(findCard, 1);
        side.Children.Add(findCard);
        find.TextChanged += (_, _) =>
        {
            placeholder.Visibility = find.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildList();
        };
        find.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (Matches().FirstOrDefault() is { } first) Open(first);
            e.Handled = true;
        };

        listScroll.Content = list;
        listScroll.Template = Launcher.ThinScroll();
        list.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(listScroll, 2);
        side.Children.Add(listScroll);
        Grid.SetRow(running, 3);
        side.Children.Add(running);
        return side;
    }

    private IEnumerable<ChatThread> Matches()
    {
        var q = new Query(find.Text);
        return host.Chats.Threads.Where(t => q.IsEmpty || Matcher.Score(q, new MatchTarget(t.Title)) > 0);
    }

    private static string NameOf(ChatThread t) => t.Messages.Count == 0 ? Tr("새 대화", "New chat") : t.Title;

    /// Did anything the list shows change (a message, an answer starting or ending, a new title)? Streaming text doesn't.
    private bool ListChanged()
    {
        var h = new HashCode();
        h.Add(host.Chats.Threads.Count);
        foreach (var t in host.Chats.Threads)
        {
            h.Add(t.Id);
            h.Add(t.Messages.Count);
            h.Add(t.Busy);
            h.Add(t.Title);
            h.Add(t.Group);
        }
        int key = h.ToHashCode();
        if (key == listKey) return false;
        listKey = key;
        return true;
    }

    private void BuildList()
    {
        if (renaming is not null) return; // a group's name is being typed; EndRename builds the list
        ListChanged(); // the key of what is drawn now
        list.Children.Clear();
        headers.Clear();
        var q = new Query(find.Text);
        bool filtering = !q.IsEmpty;
        bool Hit(ChatThread t) => !filtering || Matcher.Score(q, new MatchTarget(t.Title)) > 0;
        // the conversation being written that isn't saved yet shows where it will be
        var all = host.Chats.Threads.Contains(thread) ? host.Chats.Threads : host.Chats.Threads.Prepend(thread).ToList();
        int shown = 0;

        // the groups first, in the order they were made; while filtering, those with a match (or a matching name)
        var groups = new List<UIElement>();
        foreach (var g in host.Chats.Groups)
        {
            bool named = filtering && Matcher.Score(q, new MatchTarget(g.Name)) > 0;
            var inside = all.Where(t => t.Group == g.Id && (named || Hit(t))).ToList();
            if (filtering && !named && inside.Count == 0) continue;
            groups.Add(GroupHeader(g, all.Count(t => t.Group == g.Id), filtering));
            if (g.Folded && !filtering) continue;
            foreach (var t in inside) groups.Add(Item(t, g));
            if (inside.Count == 0) groups.Add(EmptyGroup(g));
            shown += inside.Count;
        }
        if (!filtering || groups.Count > 0)
        {
            list.Children.Add(GroupsLabel());
            foreach (var el in groups) list.Children.Add(el);
        }

        // then the others by day
        string? day = null;
        foreach (var t in all.Where(t => t.Group is null && Hit(t)))
        {
            string d = Day(DateTimeOffset.FromUnixTimeMilliseconds(t.Updated).LocalDateTime);
            if (d != day) list.Children.Add(Label(day = d));
            list.Children.Add(Item(t, null));
            shown++;
        }
        if (filtering ? shown == 0 : host.Chats.Threads.Count == 0)
        {
            var none = Theme.T(Core.Hangul.KeepAll(filtering ? Tr("검색된 대화 없음", "No chat matches") : Tr("Tab으로 물어본 대화도 여기에 저장됩니다", "Chats you ask with Tab gather here too")), 12.5, Theme.P.TextFaint, null, false);
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(22, 14, 18, 0);
            list.Children.Add(none);
        }
        Running();
    }

    private static string Day(DateTime t) =>
        t.Date == DateTime.Today ? Tr("오늘", "Today")
        : t.Date == DateTime.Today.AddDays(-1) ? Tr("어제", "Yesterday")
        : t.Date > DateTime.Today.AddDays(-7) ? Tr("지난 7일", "Previous 7 days")
        : Tr("이전", "Older");

    private static TextBlock Label(string title)
    {
        var t = Theme.T(title, 11.5, Theme.P.TextFaint, Theme.Semi, false);
        t.Margin = new Thickness(22, 12, 0, 5);
        return t;
    }

    /// One conversation: its name, who answers and when; the dot while its session runs. In a group it sits indented
    /// under the group's line (and carries the group for a drop).
    private SmoothBorder Item(ChatThread t, ChatGroup? group)
    {
        bool on = ReferenceEquals(t, thread), live = host.Chats.IsLive(t);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Theme.T(NameOf(t), 13.5, Theme.P.Text, on ? Theme.Semi : Theme.Medium));
        if (live)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Theme.P.Accent, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Tr("세션 실행 중 · 바로 답변 가능", "Session running · ready to answer") };
            Grid.SetColumn(dot, 1);
            head.Children.Add(dot);
        }
        words.Children.Add(head);
        string sub = t.Busy ? Tr("답하는 중…", "Answering…")
            : AiModels.Who(AiModels.Normalize(t.Model)) + "  ·  " + Engine.Ago(DateTimeOffset.FromUnixTimeMilliseconds(t.Updated).LocalDateTime);
        var s = Theme.T(sub, 11.5, t.Busy ? Theme.P.AccentText : Theme.P.TextFaint);
        s.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(s);
        g.Children.Add(words);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
        if (live) buttons.Children.Add(SmallButton(G.Stop, Tr("세션 종료 — 대화는 유지됩니다", "End session — the chat stays"), b => EndSession(t)));
        buttons.Children.Add(SmallButton(More, Tr("그룹으로 옮기기 · 더보기", "Move to a group · more"), b => menu.Open(ThreadActions(t, b), b)));
        if (host.Chats.Threads.Contains(t))
        {
            bool armed = false;
            buttons.Children.Add(SmallButton(G.Delete, Tr("대화 삭제", "Delete chat"), b =>
            {
                if (!armed)
                {
                    armed = true;
                    ((TextBlock)b.Child).Foreground = Theme.P.Danger;
                    b.ToolTip = Tr("한 번 더 누르면 삭제됩니다.", "Click again to delete");
                    ShowToast(Tr("한 번 더 누르면 대화가 삭제됩니다.", "Click again to delete this chat"));
                    return;
                }
                host.DeleteThread(t);
            }));
        }
        Grid.SetColumn(buttons, 1);
        g.Children.Add(buttons);
        FrameworkElement content = g;
        if (selecting && host.Chats.Threads.Contains(t))
        {
            var row = new DockPanel();
            var box = Box(picked.Contains(t.Id));
            DockPanel.SetDock(box, Dock.Left);
            row.Children.Add(box);
            row.Children.Add(g);
            content = row;
        }

        var item = new SmoothBorder
        {
            Height = 52,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(group is null ? 10 : 24, 1, 10, 1),
            Padding = new Thickness(12, 0, 6, 0),
            Background = on ? Theme.P.Selected : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content,
            Tag = group,
        };
        item.MouseEnter += (_, _) =>
        {
            if (!on) item.Background = Theme.P.Hover;
            if (!selecting) buttons.Visibility = Visibility.Visible;
        };
        item.MouseLeave += (_, _) =>
        {
            if (!on) item.Background = Brushes.Transparent;
            buttons.Visibility = Visibility.Collapsed;
        };
        // a click opens it; pressed and moved, it is dragged (onto a group, or out of one)
        Point? press = null;
        item.PreviewMouseLeftButtonDown += (_, e) => press = e.GetPosition(item);
        item.PreviewMouseMove += (_, e) =>
        {
            if (press is not { } p || selecting || e.LeftButton != MouseButtonState.Pressed || !host.Chats.Threads.Contains(t)) return;
            var at = e.GetPosition(item);
            if (Math.Abs(at.X - p.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - p.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            press = null;
            item.Opacity = 0.5;
            DragDrop.DoDragDrop(item, new DataObject(ThreadFormat, t.Id), DragDropEffects.Move);
            item.Opacity = 1;
            Mark(null);
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            if (press is null) return;
            press = null;
            if (selecting) Toggle(t);
            else if (PickClick) StartSelect(t);
            else Open(t);
            e.Handled = true;
        };
        item.MouseRightButtonUp += (_, e) =>
        {
            menu.Open(ThreadActions(t, null));
            e.Handled = true;
        };
        return item;
    }

    private static SmoothBorder SmallButton(char glyph, string tip, Action<SmoothBorder> click)
    {
        var icon = Theme.Glyph(glyph, 12, Theme.P.TextDim);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new SmoothBorder { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Child = icon, ToolTip = tip, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Key;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click(b);
            e.Handled = true;
        };
        return b;
    }

    /// Under the list: how many CLI sessions are open, and 모두 종료.
    private void Running()
    {
        if (selecting)
        {
            SelectBar();
            return;
        }
        var live = host.Chats.Threads.Where(host.Chats.IsLive).ToList();
        if (live.Count == 0)
        {
            running.Content = null;
            return;
        }
        var g = new Grid { Margin = new Thickness(22, 10, 12, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Theme.P.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        var t = Theme.T(Tr($"세션 {live.Count}개 열림", $"{live.Count} session{(live.Count == 1 ? "" : "s")} open"), 12, Theme.P.TextDim, Theme.Medium);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var all = TextButton(G.Stop, Tr("모두 종료", "End all"), () =>
        {
            foreach (var x in live) host.Chats.EndSession(x);
        });
        all.ToolTip = Tr("모든 대화의 메모리를 해제합니다. 대화 기록은 유지됩니다.", "Frees memory for all chats. Chat history stays.");
        Grid.SetColumn(all, 2);
        g.Children.Add(all);
        running.Content = new StackPanel { Children = { new SmoothBorder { Height = 1, Background = Theme.P.Line }, g } };
    }
}
