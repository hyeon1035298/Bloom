using System.Collections.Generic;
using System.Linq;
using Bloom.Search;

namespace Bloom.Core;

/// The home's 명령 block (Home's "commands"): which of Bloom's commands (Search\SystemItems.Commands) it lists, in
/// Bloom's order. Untouched it lists every command but those that refresh, lock, restart, shut down or quit (Off);
/// 홈 편집 takes one off (✕ on its row) or puts one on (“+ 명령 추가”), kept as the user's picks over those defaults
/// (Settings.HomeCommands), so a command new to Bloom shows as its default says. A command of a feature switched off in
/// 설정 > 기능 never shows and is never offered, whatever was picked.
internal static class HomeCommands
{
    /// The commands left off until put on.
    public static readonly HashSet<string> Off = ["cmd:reindex", "cmd:lock", "cmd:restart", "cmd:shutdown", "cmd:quit"];

    /// “+ 명령 추가” as a row (Ui\Controls\Row.AddTile): whether its cards show.
    public sealed record Tile(bool Open);

    /// A card under “+ 명령 추가” (Ui\Controls\Row.Card): the command it puts on.
    public sealed record Card(string Id);

    /// Whether the block lists command `id`, its feature aside: as picked, else its default.
    public static bool Picked(string id) => Picks() is { } p && p.TryGetValue(id, out bool on) ? on : !Off.Contains(id);

    /// The commands the block lists, in Bloom's order.
    public static List<SysEntry> Shown() => SystemItems.Commands.Where(e => Picked(e.Id) && Features.Shows(e.Id)).ToList();

    /// What “+ 명령 추가” offers, in Bloom's order: the commands left off whose feature is on.
    public static List<SysEntry> Missing() => SystemItems.Commands.Where(e => !Picked(e.Id) && Features.Shows(e.Id)).ToList();

    /// Command `id` on the block (`on`) or off it; a pick that is its default is not kept.
    public static void Set(string id, bool on)
    {
        if (!SystemItems.Commands.Exists(e => e.Id == id)) return;
        var picks = Settings.Current.HomeCommands ??= [];
        if (on == !Off.Contains(id)) picks.Remove(id);
        else picks[id] = on;
        Picks();
        Settings.Save();
    }

    /// The picks kept, those of commands Bloom no longer has and those that are their default dropped (none: null, the
    /// settings file as if never edited).
    private static Dictionary<string, bool>? Picks()
    {
        var s = Settings.Current;
        if (s.HomeCommands is not { } picks) return null;
        foreach (var (id, on) in picks.ToList())
            if (!SystemItems.Commands.Exists(e => e.Id == id) || on == !Off.Contains(id)) picks.Remove(id);
        if (picks.Count == 0) s.HomeCommands = null;
        return s.HomeCommands;
    }
}
