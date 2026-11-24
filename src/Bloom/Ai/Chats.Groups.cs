using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// Groups (folders) of conversations, made in the AI chat window: a conversation is in one group or none
/// (ChatThread.Group). Deleting a group keeps its conversations; they go back to the dated list.
internal sealed partial class Chats
{
    public readonly List<ChatGroup> Groups = new();

    /// UI thread; a group was made, renamed, folded or deleted, or a conversation moved in or out of one.
    public event Action? GroupsChanged;

    private string GroupsFile => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dir) ?? "", "chat-groups.json");

    private void LoadGroups()
    {
        Groups.AddRange(Json.Load<List<ChatGroup>>(GroupsFile).Where(g => g.Id.Length > 0));
        foreach (var t in Threads)
            if (t.Group is { } id && !Groups.Exists(g => g.Id == id)) t.Group = null; // its group is gone
    }

    private void SaveGroups()
    {
        store.Save(GroupsFile, Groups);
        GroupsChanged?.Invoke();
    }

    public ChatGroup? GroupOf(ChatThread t) => t.Group is { } id ? Groups.Find(g => g.Id == id) : null;

    public ChatGroup NewGroup()
    {
        string name = Tr("새 그룹", "New group");
        for (int n = 2; Groups.Exists(g => g.Name == name); n++) name = Tr($"새 그룹 {n}", $"New Group {n}");
        var group = new ChatGroup { Id = Guid.NewGuid().ToString("N")[..8], Name = name };
        Groups.Add(group);
        SaveGroups();
        return group;
    }

    public void Rename(ChatGroup g, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == g.Name) return;
        g.Name = name.Length > 40 ? name[..40] : name;
        SaveGroups();
    }

    public void Fold(ChatGroup g, bool folded)
    {
        g.Folded = folded;
        SaveGroups();
    }

    /// The group goes; its conversations stay and go back to the dated list.
    public void DeleteGroup(ChatGroup g)
    {
        foreach (var t in Threads.Where(t => t.Group == g.Id))
        {
            t.Group = null;
            Save(t);
        }
        Groups.Remove(g);
        SaveGroups();
    }

    /// Into a group, or out of every group (null).
    public void MoveTo(ChatThread t, ChatGroup? g)
    {
        if (t.Group == g?.Id) return;
        t.Group = g?.Id;
        Save(t);
        GroupsChanged?.Invoke();
    }
}
