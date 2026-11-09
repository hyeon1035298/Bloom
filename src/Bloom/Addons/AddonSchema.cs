using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Addons;

/// A text an add-on ships in Korean and English, and in Japanese and Chinese when it has them; a language it lacks takes
/// Bloom's own translation of the English one when its table has it ("Taskbar folders"), else shows the English.
internal sealed record AddonText(string Ko, string En, string? Ja = null, string? Zh = null)
{
    public string Shown => Lang.Code switch
    {
        "ko" => Ko,
        "en" => En,
        "ja" when Ja is not null => Ja,
        "zh" when Zh is not null => Zh,
        _ => Lang.Find(En) ?? En,
    };

    public static AddonText? Of(JsonNode? node) =>
        node is JsonObject o && Text(o, "en") is { } e
            ? new AddonText(Text(o, "ko") ?? e, e, Text(o, "ja"), Text(o, "zh"))
            : null;

    private static string? Text(JsonObject o, string code) => o[code] is JsonValue v && v.TryGetValue(out string? s) && s.Length > 0 ? s : null;
}

internal sealed record AddonOption(string Value, AddonText Name)
{
    /// The option's plainer name on Bloom's page, and its picture (the schema's "easy": {"name", "picture"}).
    public AddonText? EasyName { get; init; }
    public AddonPicture? Picture { get; init; }
}

/// One setting: its full key ("group.key", the name in values.json), its kind (bool, int, choice, text), default,
/// range for a number and options for a choice.
internal sealed record AddonSetting(string Key, string Type, JsonNode Default, int? Min, int? Max, IReadOnlyList<AddonOption> Options, AddonText Name, AddonText? About)
{
    /// Where the value lives when not in values.json: a JSON file in the add-on's data folder that the add-on writes too
    /// (its window's own settings), and the top-level field in it.
    public string? File { get; init; }
    public string? Field { get; init; }

    /// A choice whose options are numbers (written as numbers).
    public bool Numeric { get; init; }

    /// What a list's entries end with (".exe"; kept lower-case), and the record field that names a record on its line.
    public string? Suffix { get; init; }
    public string? Label { get; init; }

    /// A record's fields (records only).
    public IReadOnlyList<AddonSetting> Fields { get; init; } = [];

    /// How Bloom's page words and shows it (AddonEasyItem; null: as the schema names it).
    public AddonEasyItem? Easy { get; init; }

    private static readonly string[] Units = ["px", "%", "ms", "s", "min", "초", "분"];

    /// The number's unit when the name ends with it ("Icon size (px)", "간격(분)"): the slider shows it with the value instead.
    public (string name, string unit) Split()
    {
        string n = Name.Shown;
        foreach (string u in Units)
        {
            if (n.EndsWith($" ({u})", StringComparison.Ordinal)) return (n[..^(u.Length + 3)], u);
            if (n.EndsWith($"({u})", StringComparison.Ordinal)) return (n[..^(u.Length + 2)].TrimEnd(), u);
        }
        return (n, "");
    }
}

internal sealed record AddonGroup(string Id, AddonText Name, AddonText? About, IReadOnlyList<AddonSetting> Items)
{
    public AddonEasyGroup? Easy { get; init; }
}

/// An add-on's settings.schema.json: its settings in groups, with names, defaults and ranges. Bloom draws its settings
/// page from this data and writes the values the user changes; nothing of the add-on's code is in Bloom.
internal sealed class AddonSchema
{
    public IReadOnlyList<AddonGroup> Groups { get; }
    private readonly Dictionary<string, AddonSetting> byKey;

    /// The groups' order on Bloom's page where it isn't theirs here (the rest after these), and the pictures of the
    /// page's own choices by a name of the page's and the option ("size" → "s"); the schema's "easy": {"order", "pictures"}.
    public IReadOnlyList<string> Order { get; private init; } = [];
    private IReadOnlyDictionary<string, AddonPicture> pictures = new Dictionary<string, AddonPicture>();

    private AddonSchema(List<AddonGroup> groups)
    {
        Groups = groups;
        byKey = groups.SelectMany(g => g.Items).ToDictionary(i => i.Key, StringComparer.Ordinal);
    }

    public AddonSetting? this[string key] => byKey.GetValueOrDefault(key);

    /// An option's picture: a setting's option's, else one of the page's own choices (null: none).
    public AddonPicture? Picture(string key, string value) =>
        this[key]?.Options.FirstOrDefault(o => o.Value == value)?.Picture ?? pictures.GetValueOrDefault(key + "/" + value);

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
            list.Add(new AddonGroup(id, AddonText.Of(g["name"]) ?? throw Bad($"group {id}: no name"), AddonText.Of(g["about"]), items) { Easy = AddonEasyGroup.Of(g["easy"]) });
        }
        var easy = root["easy"] as JsonObject;
        var order = (easy?["order"] as JsonArray)?.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null).OfType<string>().ToArray() ?? [];
        var pictures = new Dictionary<string, AddonPicture>(StringComparer.Ordinal);
        if (easy?["pictures"] is JsonObject choices)
            foreach (var (choice, options) in choices)
                if (options is JsonObject byValue)
                    foreach (var (value, picture) in byValue)
                        if (AddonPicture.Of(picture) is { } p) pictures[choice + "/" + value] = p;
        return new AddonSchema(list) { Order = order, pictures = pictures };
    }

    /// Files a setting can't live in: the runtime's values and the host's and runtime's own.
    private static readonly string[] Reserved = [AddonProtocol.ValuesFile, AddonProtocol.HostStateFile, AddonProtocol.RuntimeStateFile, AddonProtocol.StateFile];

    private static AddonSetting Setting(JsonObject i, bool inRecord = false)
    {
        string key = Str(i, "key") ?? throw Bad("a setting without a key");
        string type = Str(i, "type") ?? "";
        var name = AddonText.Of(i["name"]) ?? throw Bad($"{key}: no name");
        var def = i["default"]?.DeepClone() ?? throw Bad($"{key}: no default");
        int? min = Int(i["min"]), max = Int(i["max"]);
        var options = new List<AddonOption>();
        string? file = Str(i, "file"), field = Str(i, "field");
        if (file is not null || field is not null)
        {
            if (inRecord) throw Bad($"{key}: a record's field with a file");
            if (file is null || field is not { Length: > 0 }) throw Bad($"{key}: a file needs a field, a field a file");
            if (file != Path.GetFileName(file) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.StartsWith('.')
                || Reserved.Contains(file, StringComparer.OrdinalIgnoreCase))
                throw Bad($"{key}: file «{file}»");
        }
        if (inRecord && type is not ("int" or "bool")) throw Bad($"{key}: a record's field is a number or a switch");
        if (type is "flags" or "list" or "hotkey" or "records" && file is null) throw Bad($"{key}: kind «{type}» needs a file");
        bool numeric = false;
        string? suffix = null, label = null;
        var fields = new List<AddonSetting>();
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
                numeric = Int(def) is not null;
                if (numeric && file is null) throw Bad($"{key}: numbered options need a file");
                foreach (var o in (i["options"] as JsonArray ?? throw Bad($"{key}: no options")).OfType<JsonObject>())
                    options.Add(new AddonOption(OptionValue(o, numeric) ?? throw Bad($"{key}: an option without a value"), AddonText.Of(o["name"]) ?? throw Bad($"{key}: an option without a name"))
                    {
                        EasyName = AddonText.Of((o["easy"] as JsonObject)?["name"]),
                        Picture = AddonPicture.Of((o["easy"] as JsonObject)?["picture"]),
                    });
                string? shown = numeric ? Int(def)?.ToString(CultureInfo.InvariantCulture) : Str(i, "default");
                if (options.Count == 0 || !options.Any(o => o.Value == shown)) throw Bad($"{key}: default is not one of its options");
                break;
            case "flags":
                if (Int(def) is null) throw Bad($"{key}: default is not a number");
                foreach (var o in (i["options"] as JsonArray ?? throw Bad($"{key}: no options")).OfType<JsonObject>())
                {
                    if (Int(o["value"]) is not (> 0 and var bit)) throw Bad($"{key}: an option's bit");
                    options.Add(new AddonOption(bit.ToString(CultureInfo.InvariantCulture), AddonText.Of(o["name"]) ?? throw Bad($"{key}: an option without a name")));
                }
                if (options.Count == 0) throw Bad($"{key}: no options");
                break;
            case "list":
                if (def is not JsonArray a || a.Any(e => e is not JsonValue v || !v.TryGetValue(out string? _))) throw Bad($"{key}: default is not a list of text");
                suffix = Str(i, "suffix");
                break;
            case "hotkey":
                if (def is not JsonObject h || Int(h["mods"]) is null || Int(h["vk"]) is null) throw Bad($"{key}: default is not a key");
                break;
            case "records":
                if (def is not JsonObject) throw Bad($"{key}: default is not an object");
                label = Str(i, "label");
                foreach (var f in (i["fields"] as JsonArray ?? throw Bad($"{key}: no fields")).OfType<JsonObject>())
                    fields.Add(Setting(f, true));
                if (fields.Count == 0) throw Bad($"{key}: no fields");
                break;
            case "text":
                if (Str(i, "default") is null && def.GetValueKind() != JsonValueKind.String) throw Bad($"{key}: default is not text");
                break;
            default:
                throw Bad($"{key}: kind «{type}»");
        }
        return new AddonSetting(key, type, def, min, max, options, name, AddonText.Of(i["about"]))
        {
            File = file,
            Field = field,
            Numeric = numeric,
            Suffix = suffix,
            Label = label,
            Fields = fields,
            Easy = inRecord ? null : AddonEasyItem.Of(i["easy"]),
        };
    }

    /// An option's value as text: a numbered choice's in invariant digits.
    private static string? OptionValue(JsonObject o, bool numeric) =>
        numeric ? Int(o["value"])?.ToString(CultureInfo.InvariantCulture) : Str(o, "value");

    internal static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int x) ? x : n is JsonValue d && d.TryGetValue(out double y) && y == Math.Floor(y) ? (int)y : null;

    private static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static InvalidDataException Bad(string why) => new("settings.schema.json: " + why);
}
