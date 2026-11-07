using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Core;

namespace Bloom.Addons;

/// The values the user changed for an add-on, as one flat map ("group.key" → number, true/false, text): kept with
/// Bloom's settings (the user's file) and written for the runtime (values.json), which reads it again on each change.
/// A value back at its default is dropped, so the runtime falls back to its own default for it. A run of changes (a
/// slider being dragged) is written at most ten times a second. A setting the schema puts in a file of the add-on's own
/// (its window's settings) is read from and written to that file instead (AddonPrefs), default or not.
internal sealed class AddonValues
{
    private readonly string userFile, valuesFile;
    private readonly AddonSchema schema;
    private readonly object gate = new();
    private readonly Dictionary<string, AddonPrefs> files = new(StringComparer.OrdinalIgnoreCase);
    private JsonObject map = new();
    private Timer? pending;

    public static readonly TimeSpan WriteEvery = TimeSpan.FromMilliseconds(100);

    public AddonValues(string userFile, string valuesFile, AddonSchema schema)
    {
        this.userFile = userFile;
        this.valuesFile = valuesFile;
        this.schema = schema;
        Reload();
    }

    /// Read again from the user's file.
    public void Reload()
    {
        JsonObject read = new();
        try
        {
            if (File.Exists(userFile) && JsonNode.Parse(File.ReadAllText(userFile)) is JsonObject o) read = o;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("add-on values", e);
        }
        lock (gate) map = read;
    }

    /// The value set, else the schema's default.
    public JsonNode? Get(string key)
    {
        if (InFile(key) is { } item) return Prefs(item).Get(item.Field!) ?? item.Default.DeepClone();
        lock (gate)
            if (map[key] is { } v) return v.DeepClone();
        return schema[key]?.Default.DeepClone();
    }

    public bool Changed(string key)
    {
        if (InFile(key) is { } item) return Prefs(item).Get(item.Field!) is not null;
        lock (gate) return map.ContainsKey(key);
    }

    /// The setting when the schema puts it in a file of the add-on's own.
    private AddonSetting? InFile(string key) => schema[key] is { File: not null } item ? item : null;

    private AddonPrefs Prefs(AddonSetting item)
    {
        lock (gate)
        {
            if (!files.TryGetValue(item.File!, out var p))
                files[item.File!] = p = new AddonPrefs(Path.Combine(Path.GetDirectoryName(valuesFile)!, item.File!));
            return p;
        }
    }

    /// A list's entries (text only).
    public IReadOnlyList<string> List(string key) =>
        Get(key) is JsonArray a ? a.OfType<JsonValue>().Select(v => v.TryGetValue(out string? s) ? s : null).OfType<string>().ToList() : [];

    public void SetList(string key, IEnumerable<string> entries) => Set(key, new JsonArray(entries.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()));

    /// A hotkey as Windows' modifier bits and virtual key (vk 0: none).
    public (uint mods, uint vk) Key(string key) =>
        Get(key) is JsonObject o ? ((uint)Math.Max(0, AddonSchema.Int(o["mods"]) ?? 0), (uint)Math.Max(0, AddonSchema.Int(o["vk"]) ?? 0)) : (0, 0);

    public void SetKey(string key, uint mods, uint vk) => Set(key, new JsonObject { ["mods"] = mods, ["vk"] = vk });

    /// A records setting's records: each id and its name (its label field; the id without one), in the file's order.
    public IReadOnlyList<(string id, string name)> Records(string key)
    {
        if (InFile(key) is not { Type: "records" } item || Get(key) is not JsonObject o) return [];
        return o.Where(p => p.Value is JsonObject).Select(p => (p.Key,
            item.Label is { } l && p.Value![l] is JsonValue v && v.TryGetValue(out string? n) && n.Length > 0 ? n : p.Key)).ToList();
    }

    /// One field of a record, else that field's default.
    public JsonNode? Record(string key, string id, string field)
    {
        if (InFile(key) is not { } item) return null;
        return Prefs(item).Get(item.Field!, id, field) ?? item.Fields.FirstOrDefault(f => f.Key == field)?.Default.DeepClone();
    }

    public void SetRecord(string key, string id, string field, JsonNode value)
    {
        if (InFile(key) is { } item) Prefs(item).Set([item.Field!, id, field], value);
    }

    /// A switch: true/false, or a number (0 off, anything else on).
    public bool Bool(string key) => Get(key) switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.True => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
        var n => AddonSchema.Int(n) is { } i && i != 0,
    };

    public int Int(string key) => AddonSchema.Int(Get(key)) ?? 0;

    /// Text, or a numbered choice's number as text.
    public string Text(string key) => Get(key) switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        var n when schema[key] is { Numeric: true } && AddonSchema.Int(n) is { } i => i.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    /// A text setting's default (what an emptied field goes back to).
    public string Default(string key) => schema[key]?.Default is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    public void Set(string key, bool value) => Set(key, JsonValue.Create(value));

    public void Set(string key, int value) => Set(key, JsonValue.Create(value));

    public void Set(string key, string value) => Set(key, JsonValue.Create(value));

    private void Set(string key, JsonNode value)
    {
        if (InFile(key) is { } own)
        {
            // a numbered choice is picked by its text
            if (own.Numeric && value is JsonValue t && t.TryGetValue(out string? digits) && int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n))
                value = JsonValue.Create(n);
            Prefs(own).Set([own.Field!], value);
            return;
        }
        lock (gate)
        {
            if (schema[key] is { } item && Same(item, value)) map.Remove(key);
            else map[key] = value;
            pending ??= new Timer(_ => Flush(), null, WriteEvery, Timeout.InfiniteTimeSpan);
        }
    }

    /// A value equal to the default (a switch's 0/1 against true/false too).
    private static bool Same(AddonSetting item, JsonNode value) => item.Type switch
    {
        "bool" => (item.Default.GetValueKind() == JsonValueKind.True) == (value.GetValueKind() == JsonValueKind.True || AddonSchema.Int(value) is { } i && i != 0),
        "int" => AddonSchema.Int(item.Default) == AddonSchema.Int(value),
        _ => JsonNode.DeepEquals(item.Default, value),
    };

    /// What is waiting goes to both files now.
    public void Flush()
    {
        AddonPrefs[] own;
        lock (gate) own = files.Values.ToArray();
        foreach (var p in own) p.Flush();
        string text;
        lock (gate)
        {
            pending?.Dispose();
            pending = null;
            text = AddonHub.ValuesText(map);
        }
        foreach (string file in new[] { userFile, valuesFile })
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (File.Exists(file) && File.ReadAllText(file) == text) continue;
                AtomicFile.Write(file, stream =>
                {
                    using var w = new StreamWriter(stream);
                    w.Write(text);
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("add-on values " + Path.GetFileName(file), e);
            }
    }
}
