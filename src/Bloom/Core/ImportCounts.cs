using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Bloom.Core;

/// What a settings import brought in from the lists that can run things (내 명령, 즐겨찾기, 퀵 검색), for the message after
/// it. Paused = AI commands with a schedule that came in switched off.
internal sealed record ImportCounts(int Commands, int Favorites, int Quicklinks, int Paused)
{
    public static readonly ImportCounts None = new(0, 0, 0, 0);

    /// Marks every scheduled AI command in usercommands.json paused (the schedule stays) and returns how many there were.
    /// A file from elsewhere must not start runs of the AI by itself; the user switches them on again.
    public static int PauseScheduled(JsonObject commands)
    {
        int n = 0;
        foreach (var item in Items(commands).OfType<JsonObject>())
        {
            if (!string.Equals(Text(item, "kind"), "ai", StringComparison.OrdinalIgnoreCase) || Text(item, "every") is not { Length: > 0 }) continue;
            foreach (string key in item.Select(p => p.Key).Where(k => string.Equals(k, "paused", StringComparison.OrdinalIgnoreCase)).ToList()) item.Remove(key);
            item["paused"] = true;
            n++;
        }
        return n;
    }

    public static ImportCounts Of(JsonObject? commands, JsonObject? favorites, JsonObject? quicklinks, int paused) =>
        new(commands is null ? 0 : Items(commands).Length, favorites is null ? 0 : Leaves(Items(favorites)), quicklinks is null ? 0 : Items(quicklinks).Length, paused);

    private static JsonNode[] Items(JsonObject o) =>
        o.FirstOrDefault(p => string.Equals(p.Key, "items", StringComparison.OrdinalIgnoreCase)).Value is JsonArray a ? a.Where(n => n is not null).Select(n => n!).ToArray() : Array.Empty<JsonNode>();

    /// Favorites without the folders: the entries inside them count, not the folder.
    private static int Leaves(JsonNode[] items) =>
        items.Sum(n => n is JsonObject o && Items(o) is { Length: > 0 } inner ? Leaves(inner) : 1);

    private static string? Text(JsonObject o, string key) =>
        o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
