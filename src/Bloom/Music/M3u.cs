using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bloom.Music;

/// The M3U playlist text: `#EXTM3U`, then per song `#EXTINF:<seconds>,<artist> - <title>` and the file's path.
internal static class M3u
{
    public static string Write(IEnumerable<Track> tracks)
    {
        var sb = new StringBuilder("#EXTM3U\n");
        foreach (var t in tracks)
        {
            string seconds = t.Duration > 0 ? Math.Round(t.Duration).ToString(CultureInfo.InvariantCulture) : "-1";
            string name = OneLine(t.Artist.Length > 0 ? t.Artist + " - " + t.Title : t.Title);
            sb.Append("#EXTINF:").Append(seconds).Append(',').Append(name).Append('\n');
            sb.Append(t.Path).Append('\n');
        }
        return sb.ToString();
    }

    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    /// How many songs the text names (its lines that are not comments).
    public static int Count(string text)
    {
        int n = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0 && line[0] != '#' && line[0] != '﻿') n++;
        }
        return n;
    }

    /// The songs the text names: a full path, one relative to `baseDir` (the list's folder), or a file:// address. A
    /// line is a song only through AudioFiles.Resolve — a web address, a program or a script in a list plays nothing.
    public static List<Track> Parse(string text, string? baseDir)
    {
        var list = new List<Track>();
        string title = "", artist = "";
        double seconds = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                (seconds, artist, title) = Info(line[8..]);
                continue;
            }
            if (line[0] == '#') continue;
            if (Where(line, baseDir) is { } path && AudioFiles.Resolve(path) is { } full)
                list.Add(new Track(full, title.Length > 0 ? title : Path.GetFileNameWithoutExtension(full), artist, seconds));
            title = artist = "";
            seconds = 0;
        }
        return list;
    }

    /// `#EXTINF:` 's rest: "<seconds>[ attributes],<artist> - <title>" (or just the title).
    private static (double seconds, string artist, string title) Info(string s)
    {
        int comma = s.IndexOf(',');
        string head = comma >= 0 ? s[..comma] : s, name = comma >= 0 ? s[(comma + 1)..].Trim() : "";
        int space = head.IndexOf(' ');
        double seconds = double.TryParse(space >= 0 ? head[..space] : head, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 ? d : 0;
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? (seconds, name[..dash].Trim(), name[(dash + 3)..].Trim()) : (seconds, "", name);
    }

    /// The file a line names, or null (another kind of address).
    private static string? Where(string line, string? baseDir)
    {
        if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(line, UriKind.Absolute, out var u) && u.IsFile ? u.LocalPath : null;
        if (line.Contains("://", StringComparison.Ordinal)) return null;
        try
        {
            if (Path.IsPathFullyQualified(line)) return line;
            if (string.IsNullOrEmpty(baseDir)) return null;
            if (Path.IsPathRooted(line)) return Path.Combine(Path.GetPathRoot(baseDir) ?? "", line.TrimStart('\\', '/')); // \Music\a.mp3: the list's drive
            return Path.Combine(baseDir, line);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
