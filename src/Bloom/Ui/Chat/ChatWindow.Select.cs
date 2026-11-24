using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Ai;
using Bloom.Search;

namespace Bloom.Ui;

/// Deleting several conversations and groups at once. 선택 (the button by the filter, ⋯ › 선택, or Ctrl/Shift+click)
/// puts a box before each line and a click ticks it; a group ticks its conversations with it, and one unticked again
/// stays in the list, out of the group. The bar under the list counts what is ticked and deletes on a second click;
/// Esc or 취소 ends it.
internal sealed partial class ChatWindow
{
    private bool selecting, deleteArmed;
    private readonly HashSet<string> picked = new(); // conversation ids and "g:" + group ids

    private static string GroupKey(ChatGroup g) => "g:" + g.Id;

    private void StartSelect(ChatThread? t = null, ChatGroup? g = null)
    {
        if (renaming is not null || OnT3) return; // T3's side lists threads Bloom doesn't delete
        selecting = true;
        deleteArmed = false;
        if (t is not null && host.Chats.Threads.Contains(t)) picked.Add(t.Id);
        if (g is not null) Pick(g, true);
        BuildList();
    }

    private void EndSelect()
    {
        selecting = deleteArmed = false;
        picked.Clear();
        BuildList();
    }

    private void Toggle(ChatThread t)
    {
        if (!host.Chats.Threads.Contains(t)) return; // the new one isn't saved yet
        if (!picked.Remove(t.Id)) picked.Add(t.Id);
        deleteArmed = false;
        BuildList();
    }

    private void Toggle(ChatGroup g)
    {
        Pick(g, !picked.Contains(GroupKey(g)));
        deleteArmed = false;
        BuildList();
    }

    private void Pick(ChatGroup g, bool on)
    {
        foreach (string id in host.Chats.Threads.Where(t => t.Group == g.Id).Select(t => t.Id).Append(GroupKey(g)).ToList())
            if (on) picked.Add(id);
            else picked.Remove(id);
    }

    /// Every conversation the filter shows (and every group when nothing is typed); again: none.
    private void PickAll()
    {
        var keys = Matches().Where(host.Chats.Threads.Contains).Select(t => t.Id).ToList();
        if (find.Text.Length == 0) keys.AddRange(host.Chats.Groups.Select(GroupKey));
        if (keys.All(picked.Contains)) picked.Clear();
        else picked.UnionWith(keys);
        deleteArmed = false;
        BuildList();
    }

    private void DeletePicked()
    {
        var threads = host.Chats.Threads.Where(t => picked.Contains(t.Id)).ToList();
        var groups = host.Chats.Groups.Where(g => picked.Contains(GroupKey(g))).ToList();
        if (threads.Count + groups.Count == 0) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            Running();
            ShowToast(Tr("한 번 더 누르면 영구 삭제됩니다.", "Click again to delete — this can't be undone"));
            return;
        }
        selecting = deleteArmed = false;
        picked.Clear();
        foreach (var g in groups) host.Chats.DeleteGroup(g);
        host.DeleteThreads(threads);
        BuildList();
        ShowToast(Counted(threads.Count, groups.Count, deleted: true));
    }

    /// "대화 3개 · 그룹 1개 선택" while picking, "… 삭제 완료" after the delete (whole sentences: the word order differs by language).
    private static string Counted(int threads, int groups, bool deleted)
    {
        if (threads > 0 && groups > 0)
            return deleted ? Tr($"대화 {threads}개 · 그룹 {groups}개 삭제 완료", $"Deleted chats: {threads} · groups: {groups}") : Tr($"대화 {threads}개 · 그룹 {groups}개 선택", $"Selected chats: {threads} · groups: {groups}");
        if (threads > 0)
            return deleted ? Tr($"대화 {threads}개 삭제 완료", $"Deleted chats: {threads}") : Tr($"대화 {threads}개 선택", $"Selected chats: {threads}");
        if (groups > 0)
            return deleted ? Tr($"그룹 {groups}개 삭제 완료", $"Deleted groups: {groups}") : Tr($"그룹 {groups}개 선택", $"Selected groups: {groups}");
        return Tr("선택된 항목 없음", "Nothing picked");
    }

    /// The tick box before a line while picking.
    private static SmoothBorder Box(bool on)
    {
        var tick = Theme.Glyph(G.Check, 10, Theme.P.Solid);
        tick.HorizontalAlignment = HorizontalAlignment.Center;
        tick.VerticalAlignment = VerticalAlignment.Center;
        tick.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        return new SmoothBorder
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            Background = on ? Theme.P.Accent : Brushes.Transparent,
            BorderBrush = on ? Theme.P.Accent : Theme.P.TextFaint,
            BorderThickness = new Thickness(on ? 0 : 1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Child = tick,
        };
    }

    /// Under the list while picking: how many, 모두 선택, 취소, 삭제 (asks once more).
    private void SelectBar()
    {
        int threads = host.Chats.Threads.Count(t => picked.Contains(t.Id)), groups = host.Chats.Groups.Count(g => picked.Contains(GroupKey(g)));
        var g = new StackPanel { Margin = new Thickness(22, 10, 12, 12) };
        var count = Theme.T(Counted(threads, groups, deleted: false), 12, Theme.P.TextDim, Theme.Medium);
        count.TextTrimming = TextTrimming.CharacterEllipsis;
        count.Margin = new Thickness(0, 0, 0, 8);
        g.Children.Add(count);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var all = TextButton(null, Tr("모두", "All"), PickAll);
        all.ToolTip = Tr("보이는 대화를 모두 고르기 · 다시 누르면 모두 풀기 (Ctrl+A)", "Pick every chat shown · again to pick none (Ctrl+A)");
        var cancel = TextButton(null, Tr("취소", "Cancel"), EndSelect);
        cancel.ToolTip = "Esc";
        cancel.Margin = new Thickness(6, 0, 0, 0);
        var delete = TextButton(G.Delete, deleteArmed ? Tr("정말 삭제", "Really delete") : Tr("삭제", "Delete"), DeletePicked, threads + groups > 0 ? Theme.P.Danger : null);
        delete.Margin = new Thickness(6, 0, 0, 0);
        if (threads + groups == 0) delete.Opacity = 0.45;
        buttons.Children.Add(all);
        buttons.Children.Add(cancel);
        buttons.Children.Add(delete);
        g.Children.Add(buttons);
        running.Content = new StackPanel { Children = { new SmoothBorder { Height = 1, Background = Theme.P.Line }, g } };
    }

    /// The keys while picking: Esc ends it, Ctrl+A ticks everything shown, Delete deletes (asked once more).
    private bool SelectKey(Key key, bool ctrl)
    {
        if (!selecting) return false;
        if (key != Key.Escape && (find.IsKeyboardFocused || input.IsKeyboardFocused && input.Text.Length > 0)) return false; // typing
        switch (key)
        {
            case Key.Escape: EndSelect(); return true;
            case Key.A when ctrl: PickAll(); return true;
            case Key.Delete: DeletePicked(); return true;
            default: return false;
        }
    }

    /// QA pictures: picking with these ticked, then 삭제 pressed once (armed, nothing deleted); null ends it.
    public void SelectForQa(ChatThread? t, ChatGroup? g, bool arm = false)
    {
        if (t is null && g is null)
        {
            EndSelect();
            return;
        }
        StartSelect(t, g);
        if (arm)
        {
            deleteArmed = true;
            Running();
        }
    }

    /// Ctrl or Shift held with a click: the click picks instead of opening.
    private static bool PickClick => (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
}
