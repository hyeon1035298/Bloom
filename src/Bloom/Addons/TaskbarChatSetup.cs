namespace Bloom.Addons;

/// Everything "카드를 누르면 → 작업표시줄 AI 패널" needs from the taskbar add-on and from Bloom.
internal static class TaskbarChatSetup
{
    public static readonly string[] Keys = ["usage.enabled", "usage.sessions", "usage.chat"];

    /// The add-on is here with a schema that has the three keys.
    public static bool Possible(Addon taskbar) => false;

    /// The three values are on already and Bloom exports usage.
    public static bool Ready(Addon taskbar) => false;

    /// Turns the three values on, writes them, and turns Bloom's usage export on. False when not Possible.
    public static bool Enable(Addon taskbar) => false;
}
