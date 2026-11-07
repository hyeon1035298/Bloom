using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bloom.Core;

namespace Bloom.Addons;

/// The add-ons this Bloom can download (addons\catalog.json, built into Bloom.dll): for each its id, the GitHub repository
/// whose latest release it is downloaded from (AddonLatest), and the oldest version it may be. The release itself is not
/// pinned here, so a new release of an add-on reaches Bloom without a new Bloom.
internal static class AddonCatalog
{
    /// One add-on. `Repo`: owner/name on GitHub. `MinVersion`: a release before it is never taken (an older release served
    /// as the latest can't take the add-on back below it). `Names`, `Abouts`: its name and a line about it by language,
    /// which the page shows before and after its addon.json is here (in Bloom's languages).
    internal sealed record Entry(string Id, string Repo, string MinVersion,
        IReadOnlyDictionary<string, string>? Names = null, IReadOnlyDictionary<string, string>? Abouts = null)
    {
        /// Its name in a language: that language's, else the English one; null when the catalog names it in neither.
        public string? Title(string lang) => AddonManifest.Pick(Names, lang);

        /// What it does, in a language; empty when the catalog says nothing.
        public string About(string lang) => AddonManifest.Pick(Abouts, lang) ?? "";

        /// Its repository's page.
        public string Page => "https://github.com/" + Repo;
    }

    /// The longest line about an add-on.
    private const int MaxAbout = 200;

    private static IReadOnlyList<Entry>? builtIn;

    /// The catalog built into this Bloom (read once; empty when it can't be read).
    public static IReadOnlyList<Entry> BuiltIn => builtIn ??= Load();

    /// The entry for an add-on; null when the catalog doesn't have it.
    public static Entry? Find(string id) => BuiltIn.FirstOrDefault(e => e.Id == id);

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

    /// owner/name as GitHub allows them: letters, digits, '-' (the owner), also '.' and '_' (the name).
    public static bool IsRepo(string repo)
    {
        int slash = repo.IndexOf('/');
        if (slash <= 0 || slash != repo.LastIndexOf('/') || repo.Length > 140) return false;
        string owner = repo[..slash], name = repo[(slash + 1)..];
        return owner.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && name.Length > 0 && name.Trim('.').Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_');
    }

    /// {"addons": [{id, repo, minVersion, name?, about?}]}, name and about as {"ko": …, "en": …}. An entry with a wrong field
    /// is left out (logged). InvalidDataException when the file itself isn't such a list.
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
            string id = Text(o, "id"), repo = Text(o, "repo"), min = Text(o, "minVersion");
            bool ok = AddonManifest.IsId(id) && IsRepo(repo) && AddonManifest.IsVersion(min);
            if (!ok || entries.Any(e => e.Id == id))
            {
                Log.Write($"add-on catalog: entry «{id}» left out");
                continue;
            }
            entries.Add(new Entry(id, repo, min, AddonManifest.LangTexts(o["name"], AddonManifest.MaxName), AddonManifest.LangTexts(o["about"], MaxAbout)));
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
