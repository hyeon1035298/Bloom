using System.Linq;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Addons;

/// Everything "카드를 누르면 → 작업표시줄 AI 패널" needs from the taskbar add-on and from Bloom.
internal static class TaskbarChatSetup
{
    /// The add-on answers a chat command only with all three on (its CanChat), which is also what TaskbarChat.Missing
    /// asks before it sends one.
    public static readonly string[] Keys = ["usage.enabled", "usage.sessions", "usage.chat"];

    /// The add-on is here with a schema that has the three keys.
    public static bool Possible(Addon taskbar) => taskbar.Values is not null && taskbar.Schema is { } s && Keys.All(k => s[k] is not null);

    /// The three values are on already and Bloom exports usage.
    public static bool Ready(Addon taskbar) => Possible(taskbar) && Keys.All(k => taskbar.Values!.Bool(k)) && UsageExport.On;

    /// Turns the three values on, writes them, and turns Bloom's usage export on. False when not Possible.
    public static bool Enable(Addon taskbar)
    {
        if (!Possible(taskbar)) return false;
        var v = taskbar.Values!;
        foreach (string k in Keys) v.Set(k, true);
        v.Flush(); // as AddonHub.FollowTheme writes pill.theme
        // the add-on draws its usage widget, which the panel opens from, only with the model and account Bloom's
        // export gives it; without them a click is taken and nothing opens
        if (!UsageExport.On)
        {
            UsageExport.Set(true);
            Settings.Save();
        }
        return true;
    }
}
