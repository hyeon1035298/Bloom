using System;
using System.IO;

namespace Bloom.Music;

/// One song: the file, and what to show for it (the file name without its extension when nothing better is known).
internal sealed record Track(string Path, string Title, string Artist = "", double Duration = 0)
{
    /// "Artist — Title", or just the title.
    public string Line => Artist.Length > 0 ? Artist + " — " + Title : Title;

    public static Track Of(string path) => new(path, System.IO.Path.GetFileNameWithoutExtension(path));
}

/// Which files the mini player takes: audio files only, by their extension. Whatever asks to play a file (a search
/// result, a later provider) goes through here, so a program or a script is never handed to a player.
internal static class AudioFiles
{
    /// What the player programs open.
    private static readonly string[] Audio = { ".flac", ".mp3", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".oga", ".opus", ".aif", ".aiff", ".ape", ".wv", ".mka", ".mpc", ".mp2" };

    /// What Windows' Media Foundation opens (the direct way; checked on real files: flac, mp3, wav, m4a open, ogg fails).
    private static readonly string[] Direct = { ".flac", ".mp3", ".wav", ".m4a", ".aac", ".wma" };

    public static bool IsAudio(string path) => Has(Audio, path);

    public static bool CanPlayDirect(string path) => Has(Direct, path);

    private static bool Has(string[] list, string path) => Array.Exists(list, e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// The file's normalized full path when it is an audio file on a drive or a share; else null. Relative paths, device
    /// paths (\\?\, \\.\), alternate data streams and every other extension (a program, a script) are refused.
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
            if (full.IndexOf(':', full.StartsWith(@"\\", StringComparison.Ordinal) ? 0 : 2) >= 0) return null; // C:\x.mp3:stream
            return IsAudio(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// Why the file cannot be played now (a line for the notice), or null when it can. `direct`: Bloom opens it itself.
    public static string? Reject(string path, bool direct, Func<string, bool> exists)
    {
        if (Resolve(path) is not { } full) return Tr("오디오 파일이 아니에요", "Not an audio file");
        if (direct && !CanPlayDirect(full)) return Tr($"{Path.GetExtension(full)} 형식은 Bloom이 직접 재생하지 못해요 — 설정 > 미니 플레이어에서 다른 플레이어를 고르세요", $"Bloom cannot play {Path.GetExtension(full)} files itself — pick another player in Settings > Mini player");
        if (!exists(full)) return Tr("파일이 없어요", "The file is gone");
        return null;
    }
}
