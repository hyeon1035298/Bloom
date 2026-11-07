using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Bloom.Core;

namespace Bloom.Addons;

/// A JSON file in an add-on's data folder where the add-on keeps its window's settings, which Bloom changes too
/// (settings.schema.json's "file"). Read again whenever it changed on disk. A change touches only its own field: on
/// writing, the file is read fresh, the waiting changes are put in and the whole is written by rename, so the add-on's
/// other fields (and what it wrote in the meantime) stay. A run of changes is written at most ten times a second.
internal sealed class AddonPrefs
{
    private readonly string file;
    private readonly object gate = new();
    private readonly List<(string[] path, JsonNode value)> pending = new();
    private JsonObject now = new();
    private (DateTime time, long length) seen;
    private Timer? timer;

    public AddonPrefs(string file) => this.file = file;

    public string File => file;

    /// The value at `path` (a top-level field, then names inside it), with the waiting changes; null when not there.
    public JsonNode? Get(params string[] path)
    {
        lock (gate)
        {
            Fresh(false);
            JsonNode? node = now;
            foreach (string name in path) node = node is JsonObject o ? o[name] : null;
            return node?.DeepClone();
        }
    }

    public void Set(string[] path, JsonNode value)
    {
        lock (gate)
        {
            Fresh(false);
            pending.Add((path, value.DeepClone()));
            Put(now, path, value.DeepClone());
            timer ??= new Timer(_ => Flush(), null, AddonValues.WriteEvery, Timeout.InfiniteTimeSpan);
        }
    }

    /// What is waiting goes to the file now.
    public void Flush()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
            if (pending.Count == 0) return;
            Fresh(true);
            pending.Clear();
            string text = now.ToJsonString();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                AtomicFile.Write(file, stream => stream.Write(Encoding.UTF8.GetBytes(text)));
                seen = Stamp();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("add-on prefs " + Path.GetFileName(file), e);
            }
        }
    }

    /// Reads the file again when it changed (`force`: always), then puts the waiting changes back on top.
    private void Fresh(bool force)
    {
        var stamp = Stamp();
        if (!force && stamp == seen) return;
        seen = stamp;
        JsonObject read = new();
        try
        {
            if (stamp.length > 0 && JsonNode.Parse(System.IO.File.ReadAllText(file)) is JsonObject o) read = o;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("add-on prefs " + Path.GetFileName(file), e);
        }
        foreach (var (path, value) in pending) Put(read, path, value.DeepClone());
        now = read;
    }

    private (DateTime time, long length) Stamp()
    {
        var info = new FileInfo(file);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (default, -1);
    }

    /// `value` at `path`, making the objects on the way.
    private static void Put(JsonObject root, string[] path, JsonNode value)
    {
        var o = root;
        for (int i = 0; i < path.Length - 1; i++)
        {
            if (o[path[i]] is not JsonObject inner)
            {
                inner = new JsonObject();
                o[path[i]] = inner;
            }
            o = inner;
        }
        o[path[^1]] = value;
    }
}
