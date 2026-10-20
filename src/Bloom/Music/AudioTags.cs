using System;
using System.IO;
using System.Runtime.InteropServices;
using Bloom.Native;

namespace Bloom.Music;

/// A song's tags as Windows reads them for Explorer's Details pane (its property handlers: mp3, m4a, flac, wma, wav):
/// the title, the artist, the album and the length. No tag library of Bloom's own: what Explorer cannot read keeps the
/// file's name. Blocks on the file: off the UI thread.
internal static class AudioTags
{
    private static readonly PropertyKey Title = new("f29f85e0-4ff9-1068-ab91-08002b27b3d9", 2); // System.Title
    private static readonly PropertyKey Artist = new("56a3372e-ce9c-11d2-9f0e-006097c686f6", 2); // System.Music.Artist
    private static readonly PropertyKey AlbumArtist = new("56a3372e-ce9c-11d2-9f0e-006097c686f6", 13); // System.Music.AlbumArtist
    private static readonly PropertyKey Album = new("56a3372e-ce9c-11d2-9f0e-006097c686f6", 4); // System.Music.AlbumTitle
    private static readonly PropertyKey Duration = new("64440490-4c8b-11d1-8b70-080036b11a03", 3); // System.Media.Duration, 100 ns

    /// The track with what its file's tags say (what they leave out stays as it was); the same track when nothing was read.
    public static Track Read(Track t)
    {
        if (AudioFiles.Resolve(t.Path) is not { } full || !File.Exists(full)) return t;
        IPropertyStore? store = null;
        try
        {
            store = PropertyStore.OfFile(full);
            if (store is null) return t;
            string title = PropertyStore.Text(store, Title), artist = PropertyStore.Text(store, Artist), album = PropertyStore.Text(store, Album);
            if (artist.Length == 0) artist = PropertyStore.Text(store, AlbumArtist);
            ulong ticks = PropertyStore.Number(store, Duration) ?? 0;
            if (title.Length == 0 && artist.Length == 0 && album.Length == 0 && ticks == 0) return t;
            return t with
            {
                Title = title.Length > 0 ? title : t.Title,
                Artist = artist.Length > 0 ? artist : t.Artist,
                Album = album.Length > 0 ? album : t.Album,
                Duration = ticks > 0 ? ticks / 1e7 : t.Duration,
            };
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException or IOException)
        {
            return t;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }
}
