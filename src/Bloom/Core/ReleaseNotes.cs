using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Core;

/// What changed in each version, from GitHub (Bloom carries no copy): docs/whatsnew/versions.txt lists the versions,
/// newest first, and docs/whatsnew/<version>.md holds one version's notes, its English part or the part under
/// "### 한국어" in Korean. tools/whatsnew/split.py makes both from CHANGELOG.md, the text each release's page is made of.
/// The list comes first and a version's notes only when it is shown, each once a run. Also whether this start is the
/// first one after an update (Settings.SeenVersion), so 바뀐 점 shows once.
internal static class ReleaseNotes
{
    /// Where the files are: raw.githubusercontent.com, not api.github.com (that one allows an address 60 unsigned
    /// requests an hour, and the update check needs them). `--notes-from` puts a dev run's elsewhere (QA before a push).
    public static string Base { get; set; } = "https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/whatsnew/";

    // how long a file or a picture may take to come
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static List<string>? versions; // this run's list
    private static readonly Dictionary<string, string> notes = new(); // this run's notes by version (both languages)

    /// This build's version: "0.9.2".
    public static string Current => typeof(ReleaseNotes).Assembly.GetName().Version?.ToString(3) ?? "";

    /// The versions with notes, newest first; null when the list can't be had (offline, a refusal).
    public static async Task<List<string>?> Versions()
    {
        if (versions is null && await Get("versions.txt") is { } text) versions = List(text);
        return versions;
    }

    /// `version`'s notes in the UI language (Markdown); null when they can't be had or say nothing.
    public static async Task<string?> For(string version)
    {
        if (!notes.ContainsKey(version) && await Get(version + ".md") is { } text) notes[version] = text;
        return notes.TryGetValue(version, out var both) ? Part(both) : null;
    }

    /// versions.txt's lines: one version each.
    public static List<string> List(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// A version's notes in the UI language: the part before "### 한국어" in English, the part after it in Korean (all of
    /// it when there is no Korean part); null when that part is empty.
    public static string? Part(string both)
    {
        var lines = both.Replace("\r\n", "\n").Split('\n');
        int ko = Array.FindIndex(lines, l => l.Trim() == "### 한국어");
        var part = ko < 0 ? lines : En ? lines[..ko] : lines[(ko + 1)..];
        string text = string.Join("\n", part).Trim();
        return text.Length == 0 ? null : text;
    }

    private static async Task<string?> Get(string file)
    {
        try
        {
            using var cut = new CancellationTokenSource(Wait);
            return await Ai.ApiClient.Shared.GetStringAsync(new Uri(Base + Uri.EscapeDataString(file)), cut.Token).ConfigureAwait(true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Log.Write($"release notes {file}: {e.Message}");
            return null;
        }
    }

    /// True once per new version: an onboarded Bloom starting on a version it hasn't started on before. The version is
    /// remembered either way (a fresh install gets the first-run screens instead).
    public static bool JustUpdated()
    {
        var s = Settings.Current;
        string now = Current;
        if (s.SeenVersion == now) return false;
        bool updated = s.Onboarded == true; // the settings came from an earlier version (before SeenVersion: 0.9.1 and older)
        s.SeenVersion = now;
        Settings.Save();
        return updated;
    }
}
