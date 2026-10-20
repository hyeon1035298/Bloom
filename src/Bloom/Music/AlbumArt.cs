using System;
using System.IO;

namespace Bloom.Music;

/// A song's cover when its file holds none (Ui\Icons.Art reads the one in the file first): the picture its folder keeps
/// for the album, by the names players and Windows Media Player write.
internal static class AlbumArt
{
    private static readonly string[] Names = { "cover", "folder", "front", "album", "albumartsmall", "albumart" };
    private static readonly string[] Kinds = { ".jpg", ".jpeg", ".png" };

    /// The first of cover, folder, front, album, AlbumArtSmall, AlbumArt (.jpg, .jpeg, .png) beside the song, or null.
    public static string? FolderImage(string song, Func<string, bool> exists)
    {
        string? dir = Path.GetDirectoryName(song);
        if (string.IsNullOrEmpty(dir)) return null;
        foreach (string name in Names)
            foreach (string kind in Kinds)
            {
                string path = Path.Combine(dir, name + kind);
                if (exists(path)) return path;
            }
        return null;
    }
}
