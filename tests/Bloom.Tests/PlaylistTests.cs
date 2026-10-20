using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Music;

namespace Bloom.Tests;

/// 재생목록 files (M3U): written and read back, the lines other players write (relative paths, file:// addresses,
/// comments, CRLF), what is never a song (a web address, a program), the names a list may have, and the folder's cover.
internal static class PlaylistTests
{
    public static void WrittenListsReadBack()
    {
        var songs = new List<Track>
        {
            new(@"C:\Music\Dive.flac", "Dive", "Artist", 201.4, "Album"),
            new(@"D:\x y\(01) 곡 #2 100%.mp3", "곡", "", 0),
        };
        string text = M3u.Write(songs);
        Check.True(text.StartsWith("#EXTM3U\n", StringComparison.Ordinal), "the header");
        Check.True(text.Contains("#EXTINF:201,Artist - Dive\n", StringComparison.Ordinal), "seconds, artist - title");
        Check.True(text.Contains("#EXTINF:-1,곡\n", StringComparison.Ordinal), "an unknown length");
        var back = M3u.Parse(text, null);
        Check.Eq(back.Count, 2, "both");
        Check.Eq(back[0].Path, @"C:\Music\Dive.flac", "the path");
        Check.Eq(back[0].Line, "Artist — Dive", "the artist and title");
        Check.Eq(back[0].Duration, 201.0, "the length");
        Check.Eq(back[1].Path, @"D:\x y\(01) 곡 #2 100%.mp3", "a name with spaces, # and % stays as it is");
        Check.Eq(back[1].Title, "곡", "its title");
        Check.Eq(M3u.Count(text), 2, "two songs counted");
    }

    public static void OtherPlayersLinesAreRead()
    {
        string text = "\uFEFF#EXTM3U\r\n#PLAYLIST:mix\r\n#EXTINF:12 tvg-id=\"x\",Only Title\r\nsub\\a.flac\r\n..\\b.mp3\r\n\\Root\\c.m4a\r\nfile:///E:/Songs/d%20e.wav\r\nhttp://radio.example/stream.mp3\r\nC:\\Tools\\setup.exe\r\nC:\\Music\\cover.jpg\r\n\r\n#EXTINF:-1,Next - Song\r\n";
        var songs = M3u.Parse(text, @"C:\Lists\Mine");
        Check.Eq(string.Join(" | ", songs.Select(s => s.Path)), @"C:\Lists\Mine\sub\a.flac | C:\Lists\b.mp3 | C:\Root\c.m4a | E:\Songs\d e.wav", "relative, .., drive-rooted and file:// lines");
        Check.Eq(songs[0].Title, "Only Title", "a title without an artist");
        Check.Eq(songs[0].Duration, 12.0, "seconds before the attributes");
        Check.Eq(songs[1].Title, "b", "no #EXTINF: the file's name");
        Check.Eq(M3u.Parse("a.mp3", null).Count, 0, "a relative line with no folder to start from");
    }

    public static void ListNamesAreFileNames()
    {
        Check.Eq(Playlists.FileName("  드라이브 / 밤  "), "드라이브 _ 밤", "a slash becomes _");
        Check.Eq(Playlists.FileName("mix..."), "mix", "no trailing dots");
        Check.Eq(Playlists.FileName("CON"), "CON_", "not a device name");
        Check.Eq(Playlists.FileName("com1.list"), "com1.list_", "not a device name with a dot");
        Check.Eq(Playlists.FileName("Comet"), "Comet", "a name that starts like one");
        Check.True(Playlists.FileName("   ") is null && Playlists.FileName("..") is null, "nothing left");
        Check.Eq(Playlists.FileName(new string('a', 150))!.Length, 100, "kept short");
        Check.True(Playlists.IsPlaylist(@"C:\a.M3U8") && Playlists.IsPlaylist(@"C:\a.m3u") && !Playlists.IsPlaylist(@"C:\a.pls"), "by extension");
    }

    public static void TheFoldersCoverIsFoundByName()
    {
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Music\Album\folder.jpg", @"C:\Music\Album\Cover.PNG" };
        Check.Eq(AlbumArt.FolderImage(@"C:\Music\Album\01.flac", there.Contains), @"C:\Music\Album\cover.png", "cover before folder");
        there.Remove(@"C:\Music\Album\Cover.PNG");
        Check.Eq(AlbumArt.FolderImage(@"C:\Music\Album\01.flac", there.Contains), @"C:\Music\Album\folder.jpg", "then folder");
        Check.Eq(AlbumArt.FolderImage(@"C:\Music\Other\01.flac", there.Contains), null, "none");
    }
}
