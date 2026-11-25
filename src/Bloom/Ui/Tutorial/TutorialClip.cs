using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

/// The clip behind the tutorial's first page. Bloom does not carry it: it comes from the repository beside the
/// what's-new notes, once, into the local cache, and the page shows its first frame (embedded) until then.
internal static class TutorialClip
{
    public const string Version = "v1";

    // a real clip is about 0.7 MB; anything far off that is an error page or a cut-off download
    private const long Least = 50 * 1024, Most = 3 * 1024 * 1024;

    // this run's download per variant, kept when it ended (a failure stays a failure until the next start)
    private static readonly Dictionary<string, Task<string?>> downloads = new(StringComparer.Ordinal);

    /// "ko" in Korean, "en" in every other language.
    public static string Variant => Lang.Code == "ko" ? "ko" : "en";

    // beside the what's-new folder: the same host and repository, or the same dev address with --notes-from
    public static string Url(string variant) =>
        new Uri(new Uri(ReleaseNotes.Base), "../media/tutorial/" + Version + "/" + variant + ".mp4").AbsoluteUri;

    public static string CacheFile(string variant) => Paths.InLocal(Path.Combine("tutorial", Version, variant + ".mp4"));

    /// Whether this run may play video at all (motion, system effects, Media Foundation, QA).
    public static bool Allowed(TutorialEnv env) => !env.Still && Motion.Moves && !Native.SystemEffects.Off && MediaAvailable;

    /// Windows N without the Media Feature Pack has neither, and a MediaElement there fails only after a while.
    /// Settable so the poster-only case can be tried on a PC that has them.
    internal static bool MediaAvailable { get; set; } =
        File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll")) && File.Exists(Path.Combine(Environment.SystemDirectory, "wmp.dll"));

    /// The cached file, downloaded first when missing (one download per variant and run, shared by every caller).
    /// Null when it cannot be had; never throws. `stop` ends this caller's wait, never the download.
    public static async Task<string?> Get(string variant, CancellationToken stop)
    {
        string file;
        try
        {
            file = CacheFile(variant);
            if (Usable(file)) return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        // the clip comes from the same place as the update check and what's new: off means nothing is fetched
        if (!Settings.Current.UpdateCheck) return null;
        Task<string?> download;
        lock (downloads)
        {
            // one under way or one that failed is this run's answer; one that ended with the file holds only while the
            // file is there (emptying the cache in Settings takes it)
            if (!downloads.TryGetValue(variant, out download!) || download is { IsCompletedSuccessfully: true, Result: { } done } && !File.Exists(done))
                downloads[variant] = download = Task.Run(() => Download(variant, file));
        }
        try
        {
            return await download.WaitAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null; // this caller stopped waiting; the download goes on for the next one
        }
    }

    /// Tests replace this; the app's reads the URL with Ai.ApiClient.Shared.
    internal static Func<string, CancellationToken, Task<byte[]>> Fetch = (url, token) => Ai.ApiClient.Shared.GetByteArrayAsync(new Uri(url), token);

    /// Tests: drops what this run remembers (the downloads under way and the ones that failed).
    internal static void Forget()
    {
        lock (downloads) downloads.Clear();
    }

    private static async Task<string?> Download(string variant, string file)
    {
        // a name of this process's own: two Blooms fetching at once never write the same file
        string part = file + ".part-" + Environment.ProcessId;
        try
        {
            using var wait = new CancellationTokenSource(ReleaseNotes.Wait);
            byte[] bytes = await Fetch(Url(variant), wait.Token).ConfigureAwait(false);
            if (!Looks(bytes.Length, bytes))
            {
                Log.Write($"tutorial clip: {variant} is not a clip ({bytes.Length} bytes)");
                return null;
            }
            await File.WriteAllBytesAsync(part, bytes, CancellationToken.None).ConfigureAwait(false);
            File.Move(part, file, overwrite: true);
            return file;
        }
        catch (Exception e)
        {
            Log.Write($"tutorial clip: {variant}: {e.Message}");
            try
            {
                File.Delete(part);
            }
            catch (Exception d) when (d is IOException or UnauthorizedAccessException)
            {
                /* not there, or held: the cache wipe takes it */
            }
            return null;
        }
    }

    // a cached file is checked as a download is: a clip of a sensible size that starts as an MP4 does
    private static bool Usable(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < Least || info.Length > Most) return false;
        var head = new byte[8];
        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && Looks(info.Length, head);
    }

    // length within bounds, and bytes 4..8 are the MP4 file-type box
    private static bool Looks(long length, ReadOnlySpan<byte> head) =>
        length >= Least && length <= Most && head.Length >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p';
}
