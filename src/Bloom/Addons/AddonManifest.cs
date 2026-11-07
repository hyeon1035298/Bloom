using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bloom.Addons;

/// An add-on's addon.json, read and checked. Its id names folders and events and every path in it is joined to a folder
/// of Bloom's, so nothing that could leave that folder (a drive, a rooted path, "..") gets through; each file Bloom
/// copies carries the SHA-256 it must have.
internal sealed record AddonManifest(string Id, string Version, string License, string Source, System.Version MinBloom,
    string HostExe, int HostProtocol, IReadOnlyDictionary<string, string> Files, string? Settings = null,
    IReadOnlyDictionary<string, string>? Names = null)
{
    public const string FileName = "addon.json";

    /// Parsed and checked; InvalidDataException says what is wrong.
    public static AddonManifest Parse(string json)
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
        string id = Text(root, "id");
        if (!IsId(id)) throw Bad($"id «{id}»: lowercase letters, digits and '-' only");
        string version = Text(root, "version");
        if (!IsVersion(version)) throw Bad($"version «{version}»");
        string license = Text(root, "license");
        // the source address may still be empty in a build not yet published; the page then offers no link
        string source = root["source"] is JsonValue sv && sv.TryGetValue(out string? src) ? src : "";
        if (!System.Version.TryParse(Text(root, "minBloom"), out var minBloom)) throw Bad("minBloom is not a version");
        if (root["host"] is not JsonObject host) throw Bad("no host");
        string exe = SafePath(Text(host, "exe")) ?? throw Bad("host.exe is not a path inside the add-on's folder");
        int protocol = host["protocol"] is JsonValue p && p.TryGetValue(out int n) ? n : throw Bad("host.protocol is not a number");
        if (protocol != AddonProtocol.Version) throw Bad($"host.protocol {protocol}, Bloom speaks {AddonProtocol.Version}");
        if (root["files"] is not JsonObject list || list.Count == 0) throw Bad("no files");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hash) in list)
        {
            string path = SafePath(name) ?? throw Bad($"file «{name}» is not a path inside the add-on's folder");
            string sha = hash is JsonValue h && h.TryGetValue(out string? s) ? s : "";
            if (!IsSha256(sha)) throw Bad($"file «{name}»: no SHA-256");
            if (!files.TryAdd(path, sha.ToLowerInvariant())) throw Bad($"file «{name}» listed twice");
        }
        if (!files.ContainsKey(exe)) throw Bad("host.exe is not among the files"); // the exe Bloom starts is hash-checked too
        // the settings schema the page is drawn from: optional, and hash-checked like the rest when it is there
        string? settings = null;
        if (root["settings"] is JsonValue st && st.TryGetValue(out string? sp) && sp.Length > 0)
        {
            settings = SafePath(sp) ?? throw Bad("settings is not a path inside the add-on's folder");
            if (!files.ContainsKey(settings)) throw Bad("settings is not among the files");
        }
        var names = LangTexts(root["name"], MaxName);
        return new AddonManifest(id, version, license, source, minBloom, exe, protocol, files, settings, names);
    }

    /// The longest name shown (addon.json's and the catalog's).
    public const int MaxName = 80;

    /// The name to show in a language: that language's, else the English one, else the id.
    public string Title(string lang) => Pick(Names, lang) ?? Id;

    /// Texts by language ({"ko": …, "en": …}) as addon.json and Bloom's catalog give a name or a line about the add-on:
    /// optional and only shown, so a wrong or over-long one is left out, not refused.
    internal static Dictionary<string, string> LangTexts(JsonNode? node, int max)
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is JsonObject list)
            foreach (var (lang, value) in list)
                if (value is JsonValue v && v.TryGetValue(out string? text) && text.Trim() is { Length: > 0 } shown && shown.Length <= max) texts[lang] = shown;
        return texts;
    }

    /// The text for a language: that language's, else the English one; null when there is neither.
    internal static string? Pick(IReadOnlyDictionary<string, string>? texts, string lang) =>
        texts?.GetValueOrDefault(lang) ?? texts?.GetValueOrDefault("en");

    /// This Bloom is new enough for it.
    public bool Fits(System.Version bloom) => bloom >= MinBloom;

    /// lowercase letters, digits and '-' (a folder and an event name are made of it).
    public static bool IsId(string id) => id.Length is > 0 and <= 64 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') && id[0] != '-';

    /// A version names the run copy's folder (and a downloaded one's): letters, digits and . - + only, and never a "."
    /// or ".." alone.
    public static bool IsVersion(string v) => v.Length is > 0 and <= 40 && v.Trim('.').Length > 0
        && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');

    internal static bool IsSha256(string s) => s.Length == 64 && s.All(char.IsAsciiHexDigit);

    /// A relative path that stays inside the folder it is joined to, with Windows' separators; null for anything else
    /// (rooted, a drive or stream colon, "..", ".", empty parts, characters a file name can't have).
    public static string? SafePath(string path)
    {
        if (path.Length is 0 or > 200 || path.Contains(':') || Path.IsPathRooted(path)) return null;
        var parts = path.Split('/', '\\');
        var bad = Path.GetInvalidFileNameChars();
        foreach (string part in parts)
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(bad) >= 0) return null;
        return string.Join('\\', parts);
    }

    private static string Text(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue(out string? s) && s.Length > 0 ? s : throw Bad($"no {name}");

    private static InvalidDataException Bad(string why) => new("addon.json: " + why);
}
