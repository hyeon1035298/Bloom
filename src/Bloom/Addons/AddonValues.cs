using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Core;

namespace Bloom.Addons;

/// The values the user changed for an add-on, as one flat map ("group.key" → number, true/false, text): kept with
/// Bloom's settings (the user's file) and written for the runtime (values.json), which reads it again on each change.
/// A value back at its default is dropped, so the runtime falls back to its own default for it. A run of changes (a
/// slider being dragged) is written at most ten times a second.
internal sealed class AddonValues
{
    private readonly string userFile, valuesFile;
    private readonly AddonSchema schema;
    private readonly object gate = new();
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
        lock (gate)
            if (map[key] is { } v) return v.DeepClone();
        return schema[key]?.Default.DeepClone();
    }

    public bool Changed(string key)
    {
        lock (gate) return map.ContainsKey(key);
    }

    /// A switch: true/false, or a number (0 off, anything else on).
    public bool Bool(string key) => Get(key) switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.True => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
        var n => AddonSchema.Int(n) is { } i && i != 0,
    };

    public int Int(string key) => AddonSchema.Int(Get(key)) ?? 0;

    public string Text(string key) => Get(key) is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    /// A text setting's default (what an emptied field goes back to).
    public string Default(string key) => schema[key]?.Default is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    public void Set(string key, bool value) => Set(key, JsonValue.Create(value));

    public void Set(string key, int value) => Set(key, JsonValue.Create(value));

    public void Set(string key, string value) => Set(key, JsonValue.Create(value));

    private void Set(string key, JsonNode value)
    {
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
