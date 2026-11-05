using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Addons;

/// The add-ons this Bloom can download (addons\catalog.json, built into Bloom.dll): for each its id, version, the
/// address of its release zip and that zip's SHA-256. Never fetched at run time, so the only code that can reach
/// explorer through a download is the zip whose hash this build pins.
internal static class AddonCatalog
{
    /// One add-on's release. `Size`: the zip's length in bytes, 0 while not known. `Names`, `Abouts`: its name and a line
    /// about it by language, which the page shows before and after its addon.json is here (in Bloom's languages).
    internal sealed record Entry(string Id, string Version, string Url, string Sha256, long Size,
        IReadOnlyDictionary<string, string>? Names = null, IReadOnlyDictionary<string, string>? Abouts = null)
    {
        /// Downloadable: its hash is filled in (an entry without one waits for its release).
        public bool Offered => Sha256.Length == 64;

        /// Its name in a language: that language's, else the English one; null when the catalog names it in neither.
        public string? Title(string lang) => AddonManifest.Pick(Names, lang);

        /// What it does, in a language; empty when the catalog says nothing.
        public string About(string lang) => AddonManifest.Pick(Abouts, lang) ?? "";
    }

    /// The longest line about an add-on.
    private const int MaxAbout = 200;

    private static IReadOnlyList<Entry>? builtIn;

    /// The catalog built into this Bloom (read once; empty when it can't be read).
    public static IReadOnlyList<Entry> BuiltIn => builtIn ??= Load();

    /// The entry for an add-on when it can be downloaded; null otherwise.
    public static Entry? Find(string id) => BuiltIn.FirstOrDefault(e => e.Id == id && e.Offered);

    private static IReadOnlyList<Entry> Load()
    {
        try
        {
            using var stream = typeof(AddonCatalog).Assembly.GetManifestResourceStream("Bloom.addons.catalog.json");
            if (stream is null) return Array.Empty<Entry>();
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Log.Error("add-on catalog", e);
            return Array.Empty<Entry>();
        }
    }

    /// {"addons": [{id, version, url, sha256, size?, name?, about?}]}, name and about as {"ko": …, "en": …}. An entry with
    /// a wrong field is left out (logged); an empty sha256 is kept, as an entry that is not offered. InvalidDataException
    /// when the file itself isn't such a list.
    public static IReadOnlyList<Entry> Parse(string json)
    {
        JsonArray list;
        try
        {
            list = (JsonNode.Parse(json) as JsonObject)?["addons"] as JsonArray ?? throw new InvalidDataException("catalog.json: no addons list");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("catalog.json: " + e.Message);
        }
        var entries = new List<Entry>();
        foreach (var node in list)
        {
            if (node is not JsonObject o) continue;
            string id = Text(o, "id"), version = Text(o, "version"), url = Text(o, "url"), sha = Text(o, "sha256").ToLowerInvariant();
            long size = o["size"] is JsonValue sv && sv.TryGetValue(out long n) && n > 0 ? n : 0;
            bool ok = AddonManifest.IsId(id) && AddonManifest.IsVersion(version)
                && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
                && (sha.Length == 0 || sha.Length == 64 && sha.All(char.IsAsciiHexDigit));
            if (!ok || entries.Any(e => e.Id == id))
            {
                Log.Write($"add-on catalog: entry «{id}» left out");
                continue;
            }
            entries.Add(new Entry(id, version, url, sha, size,
                AddonManifest.LangTexts(o["name"], AddonManifest.MaxName), AddonManifest.LangTexts(o["about"], MaxAbout)));
        }
        return entries;
    }

    private static string Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    /// `a` is a later version than `b`: by the numbers before any -suffix or +suffix, then a release before its
    /// pre-release ("1.0.0" after "1.0.0-beta"), then by text. Versions that aren't numbers compare by text.
    public static bool Newer(string a, string b) => Compare(a, b) > 0;

    public static int Compare(string a, string b)
    {
        static (Version? number, string rest) Split(string v)
        {
            int cut = v.AsSpan().IndexOfAny('-', '+');
            return (System.Version.TryParse(cut < 0 ? v : v[..cut], out var n) ? n : null, cut < 0 ? "" : v[cut..]);
        }
        var (na, ra) = Split(a);
        var (nb, rb) = Split(b);
        if (na is not null && nb is not null)
        {
            int c = na.CompareTo(nb);
            if (c != 0) return c;
            if (ra.Length == 0 != (rb.Length == 0)) return ra.Length == 0 ? 1 : -1;
        }
        return string.CompareOrdinal(a, b);
    }
}
