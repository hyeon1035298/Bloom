using System;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.Addons;

/// 설정 > 작업 알림 > 카드를 누르면 > 작업표시줄 AI 패널: a Claude Code or Codex turn's card opens the session's chat in the
/// taskbar add-on's usage details (protocol §5, cmd "chat"); while the add-on can't show it, the card goes to the app.
internal static class TaskbarChat
{
    internal enum Need
    {
        None,
        Addon, // not installed, off, or not running
        Chat, // its usage meter, session list or chat panel is off
    }

    /// What the add-on still needs before it can show a chat.
    public static Need Missing()
    {
        var a = AddonHub.Taskbar;
        if (!a.On || a.Status().State != AddonState.Running) return Need.Addon;
        return a.Values is { } v && v.Bool("usage.enabled") && v.Bool("usage.sessions") && v.Bool("usage.chat") ? Need.None : Need.Chat;
    }

    /// A turn's key ("claude:<id>", "codex:<id>", as ClaudeWatch and CodexWatch make them) as the command's source and
    /// session; null for any other.
    internal static (string source, string session)? Session(string key)
    {
        int colon = key.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == key.Length - 1) return null;
        string source = key[..colon];
        return source is "claude" or "codex" ? (source, key[(colon + 1)..]) : null;
    }

    /// The click of a turn's card: its chat in the taskbar under that setting, else `app` (the session's window).
    public static Action? Click(string key, Action? app) =>
        Settings.Current.NoticeClick == "taskbar" && Session(key) is { } s ? () => _ = Open(s.source, s.session, app) : app;

    private static async Task Open(string source, string session, Action? app)
    {
        bool ok = false;
        try
        {
            if (Missing() == Need.None)
            {
                Win32.AllowSetForegroundWindow(-1); // explorer brings its popup forward
                ok = (await AddonHub.Taskbar.Commands.Send(TaskbarCommands.Chat(source, session)).ConfigureAwait(true)).Ok;
            }
        }
        catch (Exception e)
        {
            Log.Error("taskbar chat", e);
        }
        if (!ok) app?.Invoke();
    }
}
