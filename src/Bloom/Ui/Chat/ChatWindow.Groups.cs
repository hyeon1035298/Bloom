using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Search;

namespace Bloom.Ui;

/// Groups in the chat window's list: a line per group (click folds it; hover shows + 새 대화 and ⋯), its conversations
/// under it. Made with the + next to 그룹, named in place; conversations go in by dragging or from their ⋯ / right-click
/// menu. Deleting a group keeps its conversations.
internal sealed partial class ChatWindow
{
    private const string ThreadFormat = "Bloom.ChatThread";
    private const char More = '', Rename = '', Out = '';
    private readonly PopupMenu menu = new();
    private readonly Dictionary<string, SmoothBorder> headers = new();
    private TextBox? renaming;
    private string? renamingId;
    private SmoothBorder? dropMark;

    /// "그룹" over the groups, with + 새 그룹.
    private Grid GroupsLabel()
    {
        var g = new Grid { Margin = new Thickness(22, 10, 12, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = Theme.T(Tr("그룹", "Groups"), 11.5, Theme.P.TextFaint, Theme.Semi, false);
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        var add = SmallButton(G.Add, Tr("새 그룹", "New group"), _ => NewGroup());
        add.Width = add.Height = 24;
        Grid.SetColumn(add, 1);
        g.Children.Add(add);
        return g;
    }

    private SmoothBorder GroupHeader(ChatGroup group, int count, bool filtering)
    {
        bool open = !group.Folded || filtering;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = selecting ? GridLength.Auto : new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = Theme.Glyph(open ? '' : '', 9, Theme.P.TextFaint);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(selecting ? Box(picked.Contains(GroupKey(group))) : chevron);
        FrameworkElement name;
        if (renamingId == group.Id)
        {
            var box = new TextBox
            {
                Text = group.Name,
                FontFamily = Theme.Sans,
                FontSize = 13.5,
                FontWeight = Theme.Semi,
                Foreground = Theme.P.Text,
                CaretBrush = Theme.P.Accent,
                SelectionBrush = Theme.P.Selection,
                SelectionOpacity = 1,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                MaxLength = 40,
                Template = Launcher.BareTextBox(),
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Escape)) return;
                EndRename(group, e.Key == Key.Enter);
                e.Handled = true;
            };
            box.LostKeyboardFocus += (_, _) => EndRename(group, true);
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            renaming = box;
            name = new SmoothBorder
            {
                Child = box,
                Background = Theme.P.Card,
                BorderBrush = Theme.P.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 2, 6, 3),
                Margin = new Thickness(-5, 0, 0, 0), // the name stays about where it was
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else name = Theme.T(group.Name, 13.5, Theme.P.Text, Theme.Semi);
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        var n = Theme.T(count.ToString(), 11.5, Theme.P.TextFaint, Theme.Medium, false);
        n.VerticalAlignment = VerticalAlignment.Center;
        n.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(n, 2);
        g.Children.Add(n);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        buttons.Children.Add(SmallButton(G.Add, Tr("이 그룹에서 새 대화", "New chat in this group"), _ => Open(NewIn(group))));
        buttons.Children.Add(SmallButton(More, Tr("그룹 메뉴", "Group menu"), b => menu.Open(GroupActions(group), b)));
        Grid.SetColumn(buttons, 3);
        g.Children.Add(buttons);

        var header = new SmoothBorder
        {
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(10, 1, 10, 1),
            Padding = new Thickness(10, 0, 6, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = g,
            Tag = group,
            ToolTip = open ? null : Tr("눌러서 펼치기", "Click to expand"),
        };
        header.MouseEnter += (_, _) =>
        {
            if (!ReferenceEquals(header, dropMark)) header.Background = Theme.P.Hover;
            buttons.Visibility = renaming is null && !selecting ? Visibility.Visible : Visibility.Collapsed;
        };
        header.MouseLeave += (_, _) =>
        {
            if (!ReferenceEquals(header, dropMark)) header.Background = Brushes.Transparent;
            buttons.Visibility = Visibility.Collapsed;
        };
        header.MouseLeftButtonUp += (_, e) =>
        {
            if (renaming is not null) return;
            if (selecting) Toggle(group);
            else if (PickClick) StartSelect(g: group);
            else if (!filtering) host.Chats.Fold(group, !group.Folded);
            e.Handled = true;
        };
        header.MouseRightButtonUp += (_, e) =>
        {
            menu.Open(GroupActions(group));
            e.Handled = true;
        };
        headers[group.Id] = header;
        return header;
    }

    /// Under a group without conversations: how one gets in.
    private static Border EmptyGroup(ChatGroup group)
    {
        var t = Theme.T(Core.Hangul.KeepAll(Tr("대화를 끌어다 놓거나 ⋯에서 추가합니다", "Drag chats here or add them via ⋯")), 11.5, Theme.P.TextFaint, null, false);
        t.TextWrapping = TextWrapping.Wrap;
        return new Border { Child = t, Padding = new Thickness(46, 4, 14, 8), Background = Brushes.Transparent, Tag = group };
    }

    private ChatThread NewIn(ChatGroup group)
    {
        var t = host.Chats.New();
        t.Group = group.Id; // listed (and saved) in the group once it has a message
        if (group.Folded) host.Chats.Fold(group, false);
        return t;
    }

    private void NewGroup() => StartRename(host.Chats.NewGroup());

    /// The group's name becomes a text field (Enter keeps, Esc leaves it as it was).
    private void StartRename(ChatGroup group)
    {
        if (renaming is not null) return;
        find.Clear();
        renamingId = group.Id;
        BuildList();
        headers.GetValueOrDefault(group.Id)?.BringIntoView();
    }

    private void EndRename(ChatGroup group, bool keep)
    {
        if (renaming is not { } box) return;
        bool typing = box.IsKeyboardFocused; // Enter or Esc; a click elsewhere keeps its own focus
        renaming = null;
        renamingId = null;
        if (keep) host.Chats.Rename(group, box.Text); // GroupsChanged rebuilds the list when the name changed
        BuildList();
        Header();
        if (typing) input.Focus();
    }

    // ── menus ───────────────────────────────────────────────────────────────────────────────────
    private List<ActionItem> GroupActions(ChatGroup group) => new()
    {
        new() { Title = Tr("이 그룹에서 새 대화", "New chat in this group"), Glyph = G.Add, Run = () => Open(NewIn(group)) },
        new() { Title = Tr("이름 바꾸기", "Rename"), Glyph = Rename, Run = () => StartRename(group) },
        new() { Title = Tr("선택 — 여러 개 골라 지우기", "Select — bulk delete"), Glyph = G.Check, Run = () => StartSelect(g: group) },
        new() { Title = "" },
        new()
        {
            Title = Tr("그룹 삭제 — 대화는 유지됩니다", "Delete group — keeps chats"),
            Glyph = G.Delete,
            Danger = true,
            Run = () =>
            {
                host.Chats.DeleteGroup(group);
                ShowToast(Tr($"‘{group.Name}’ 그룹 삭제 완료 · 대화 기록 유지", $"Deleted group ‘{group.Name}’ · Chat history kept"));
            },
        },
    };

    /// A conversation's ⋯ (or right-click): into a group or out of it, 세션 종료, delete (asked once more).
    private List<ActionItem> ThreadActions(ChatThread t, FrameworkElement? anchor)
    {
        var list = new List<ActionItem>();
        foreach (var g in host.Chats.Groups.Where(g => g.Id != t.Group))
        {
            var to = g;
            list.Add(new ActionItem { Title = Tr($"{g.Name}에 넣기", $"Move to {g.Name}"), Glyph = G.Folder, Run = () => host.Chats.MoveTo(t, to) });
        }
        list.Add(new ActionItem
        {
            Title = Tr("새 그룹을 만들어 넣기", "Move to new group"),
            Glyph = G.Add,
            Run = () =>
            {
                var g = host.Chats.NewGroup();
                host.Chats.MoveTo(t, g);
                StartRename(g);
            },
        });
        if (t.Group is not null) list.Add(new ActionItem { Title = Tr("그룹에서 빼기", "Remove from group"), Glyph = Out, Run = () => host.Chats.MoveTo(t, null) });
        if (host.Chats.Threads.Contains(t)) list.Add(new ActionItem { Title = Tr("선택 — 여러 개 골라 지우기", "Select — bulk delete"), Glyph = G.Check, Run = () => StartSelect(t) });
        if (host.Chats.IsLive(t) || host.Chats.Threads.Contains(t)) list.Add(new ActionItem { Title = "" });
        if (host.Chats.IsLive(t)) list.Add(new ActionItem { Title = Tr("세션 종료 — 대화는 유지됩니다", "End session — keeps chat history"), Glyph = G.Stop, Run = () => EndSession(t) });
        if (host.Chats.Threads.Contains(t))
            list.Add(new ActionItem
            {
                Title = Tr("대화 삭제", "Delete chat"),
                Glyph = G.Delete,
                Danger = true,
                Run = () => Dispatcher.BeginInvoke(() => menu.Open(new List<ActionItem>
                {
                    new() { Title = Tr($"‘{NameOf(t)}’ 지우기", $"Delete ‘{NameOf(t)}’"), Glyph = G.Delete, Danger = true, Run = () => host.DeleteThread(t) },
                    new() { Title = Tr("취소", "Cancel"), Glyph = G.Close },
                }, anchor)),
            });
        return list;
    }

    // ── dragging a conversation onto a group ────────────────────────────────────────────────────
    /// Over a group (its line, its conversations) → into it; anywhere else in the list → out of its group.
    private DragDropEffects DragThread(DragEventArgs e, bool drop)
    {
        ChatGroup? into = null;
        bool inList = false;
        for (var d = e.OriginalSource as DependencyObject; d is not null && !inList; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { Tag: ChatGroup g })
            {
                into = g;
                inList = true;
            }
            else if (ReferenceEquals(d, listScroll)) inList = true;
        }
        Mark(into);
        if (!inList) return DragDropEffects.None;
        if (drop)
        {
            Mark(null);
            if (e.Data.GetData(ThreadFormat) is string id && host.Chats.Threads.Find(t => t.Id == id) is { } moved) host.Chats.MoveTo(moved, into);
        }
        return DragDropEffects.Move;
    }

    /// The group a dragged conversation would go into, in rose.
    private void Mark(ChatGroup? group)
    {
        var header = group is null ? null : headers.GetValueOrDefault(group.Id);
        if (ReferenceEquals(header, dropMark)) return;
        if (dropMark is not null) dropMark.Background = Brushes.Transparent;
        dropMark = header;
        if (header is not null) header.Background = Theme.P.AccentSoft;
    }

    // ── QA ──────────────────────────────────────────────────────────────────────────────────────
    public void CaptureMenuForQa(ChatThread t, string file)
    {
        menu.Open(ThreadActions(t, title), title);
        menu.Capture(file);
        menu.Close();
    }

    public void RenameForQa(ChatGroup group, bool start)
    {
        if (start) StartRename(group);
        else EndRename(group, false);
    }
}
