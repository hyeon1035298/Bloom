using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Addons;

/// A text an add-on ships in Korean and English; the other languages Bloom speaks take Bloom's own translation of the
/// English one when its table has it ("Taskbar folders"), else show the English.
internal sealed record AddonText(string Ko, string En)
{
    public string Shown => Lang.Code switch
    {
        "ko" => Ko,
        "en" => En,
        _ => Lang.Find(En) ?? En,
    };

    public static AddonText? Of(JsonNode? node) =>
        node is JsonObject o && o["en"] is JsonValue en && en.TryGetValue(out string? e) && e.Length > 0
            ? new AddonText(o["ko"] is JsonValue ko && ko.TryGetValue(out string? k) && k.Length > 0 ? k : e, e)
            : null;
}

internal sealed record AddonOption(string Value, AddonText Name);

/// One setting: its full key ("group.key", the name in values.json), its kind (bool, int, choice, text), default,
/// range for a number and options for a choice.
internal sealed record AddonSetting(string Key, string Type, JsonNode Default, int? Min, int? Max, IReadOnlyList<AddonOption> Options, AddonText Name, AddonText? About)
{
    /// The number's unit when the name ends with it ("Icon size (px)"): the slider shows it with the value instead.
    public (string name, string unit) Split()
    {
        string n = Name.Shown;
        foreach (string u in new[] { "px", "%", "ms" })
            if (n.EndsWith($" ({u})", StringComparison.Ordinal)) return (n[..^(u.Length + 3)], u);
        return (n, "");
    }
}

internal sealed record AddonGroup(string Id, AddonText Name, AddonText? About, IReadOnlyList<AddonSetting> Items);

/// An add-on's settings.schema.json: its settings in groups, with names, defaults and ranges. Bloom draws its settings
/// page from this data and writes the values the user changes; nothing of the add-on's code is in Bloom.
internal sealed class AddonSchema
{
    public IReadOnlyList<AddonGroup> Groups { get; }
    private readonly Dictionary<string, AddonSetting> byKey;

    private AddonSchema(List<AddonGroup> groups)
    {
        Groups = groups;
        byKey = groups.SelectMany(g => g.Items).ToDictionary(i => i.Key, StringComparer.Ordinal);
    }

    public AddonSetting? this[string key] => byKey.GetValueOrDefault(key);

    public IEnumerable<string> Keys => byKey.Keys;

    /// The schema the shipped manifest names, when its SHA-256 matches (null when there is none or it doesn't).
    public static AddonSchema? Load(string dir, AddonManifest m)
    {
        if (m.Settings is not { } name) return null;
        string file = Path.Combine(dir, name);
        try
        {
            if (!File.Exists(file) || !string.Equals(AddonStore.Sha256(file), m.Files[name], StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"add-on {m.Id}: settings schema missing or changed");
                return null;
            }
            return Parse(File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error("add-on schema", e);
            return null;
        }
    }

    /// Parsed and checked: every setting has a key, a known kind, a default of that kind, a name in English, options for a
    /// choice; a key is never there twice. InvalidDataException says what is wrong.
    public static AddonSchema Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw Bad("not a JSON object");
        }
        catch (JsonException e)
        {
            throw Bad(e.Message);
        }
        if (root["groups"] is not JsonArray groups) throw Bad("no groups");
        var list = new List<AddonGroup>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups.OfType<JsonObject>())
        {
            string id = Str(g, "id") ?? throw Bad("a group without an id");
            var items = new List<AddonSetting>();
            foreach (var i in (g["items"] as JsonArray ?? throw Bad($"group {id}: no items")).OfType<JsonObject>())
            {
                var item = Setting(i);
                if (!seen.Add(item.Key)) throw Bad($"{item.Key} twice");
                items.Add(item);
            }
            list.Add(new AddonGroup(id, AddonText.Of(g["name"]) ?? throw Bad($"group {id}: no name"), AddonText.Of(g["about"]), items));
        }
        return new AddonSchema(list);
    }

    private static AddonSetting Setting(JsonObject i)
    {
        string key = Str(i, "key") ?? throw Bad("a setting without a key");
        string type = Str(i, "type") ?? "";
        var name = AddonText.Of(i["name"]) ?? throw Bad($"{key}: no name");
        var def = i["default"]?.DeepClone() ?? throw Bad($"{key}: no default");
        int? min = Int(i["min"]), max = Int(i["max"]);
        var options = new List<AddonOption>();
        switch (type)
        {
            case "bool":
                if (def.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) throw Bad($"{key}: default is not true or false");
                break;
            case "int":
                if (Int(def) is not { } d) throw Bad($"{key}: default is not a number");
                if (min > max || d < min || d > max) throw Bad($"{key}: default outside its range");
                break;
            case "choice":
                foreach (var o in (i["options"] as JsonArray ?? throw Bad($"{key}: no options")).OfType<JsonObject>())
                    options.Add(new AddonOption(Str(o, "value") ?? throw Bad($"{key}: an option without a value"), AddonText.Of(o["name"]) ?? throw Bad($"{key}: an option without a name")));
                if (options.Count == 0 || !options.Any(o => o.Value == Str(i, "default"))) throw Bad($"{key}: default is not one of its options");
                break;
            case "text":
                if (Str(i, "default") is null && def.GetValueKind() != JsonValueKind.String) throw Bad($"{key}: default is not text");
                break;
            default:
                throw Bad($"{key}: kind «{type}»");
        }
        return new AddonSetting(key, type, def, min, max, options, name, AddonText.Of(i["about"]));
    }

    internal static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int x) ? x : n is JsonValue d && d.TryGetValue(out double y) && y == Math.Floor(y) ? (int)y : null;

    private static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static InvalidDataException Bad(string why) => new("settings.schema.json: " + why);
}
