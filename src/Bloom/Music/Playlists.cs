using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bloom.Core;

namespace Bloom.Music;

/// 재생목록: Bloom's own in the data folder's playlists\ (one .m3u8 each, named as the file), and any .m3u / .m3u8 another
/// player wrote. Only the direct way plays them (the other players would take one command per song).
internal static class Playlists
{
    /// One of Bloom's lists: its name, its file, how many songs it names.
    public sealed record Saved(string Name, string Path, int Count);

    public static string Folder => Path.Combine(Paths.Data, "playlists");

    /// Larger than this is no playlist (a list of 100 000 songs is ~15 MB).
    private const long MaxBytes = 16 * 1024 * 1024;

    public static bool IsPlaylist(string path) =>
        path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);

    /// Bloom's lists, by name.
    public static List<Saved> All()
    {
        var list = new List<Saved>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (string f in Directory.EnumerateFiles(Folder, "*.m3u8"))
                if (Text(f) is { } text) list.Add(new Saved(Path.GetFileNameWithoutExtension(f), f, M3u.Count(text)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("playlists: " + e.Message);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    /// A name that can be a file's ("a/b" → "a_b", no trailing dots, not a device name), or null when nothing is left.
    public static string? FileName(string name)
    {
        var sb = new StringBuilder(name.Trim());
        var bad = Path.GetInvalidFileNameChars();
        for (int i = 0; i < sb.Length; i++)
            if (Array.IndexOf(bad, sb[i]) >= 0) sb[i] = '_';
        string s = sb.ToString().TrimEnd('.', ' ');
        if (s.Length > 100) s = s[..100].TrimEnd('.', ' ');
        if (s.Length == 0) return null;
        string stem = s.Split('.')[0].ToUpperInvariant();
        bool device = stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]));
        return device ? s + "_" : s;
    }

    /// Writes the songs as that list (a list of that name is replaced): its file, or null when it could not be written.
    public static string? Save(string name, IEnumerable<Track> tracks)
    {
        if (FileName(name) is not { } file) return null;
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, file + ".m3u8");
            File.WriteAllText(path, M3u.Write(tracks), new UTF8Encoding(false));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("playlists: not saved — " + e.Message);
            return null;
        }
    }

    /// The songs of a list file (what it names that is an audio file), or null when it cannot be read.
    public static List<Track>? Read(string path) => Text(path) is { } text ? M3u.Parse(text, Path.GetDirectoryName(path)) : null;

    /// To the Recycle Bin.
    public static bool Delete(Saved list) => Native.ShellFiles.Recycle(list.Path, IntPtr.Zero);

    /// The file's text (its encoding as the document window reads it: a BOM, UTF-8, else the PC's legacy code page — an
    /// old .m3u), or null.
    private static string? Text(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return null;
            return DocFile.Decode(File.ReadAllBytes(path), false, null).Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
