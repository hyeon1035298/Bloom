using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Addons;

/// Why a download was not installed, for the page's words.
internal enum AddonFailure
{
    Network, // no answer, an error status, or the connection broke off
    Hash, // the zip is not the one the catalog pins
    Content, // the zip's entries or its addon.json are not what the catalog names
    Disk, // the files could not be written
}

internal sealed class AddonDownloadException(AddonFailure reason, string message, Exception? inner = null) : Exception(message, inner)
{
    public AddonFailure Reason { get; } = reason;
}

/// One add-on's download as the page shows it: written by the download's worker, read on the UI thread.
internal sealed class AddonFetch
{
    private readonly object gate = new();

    public bool Busy { get; private set; }

    public long Done { get; private set; }

    /// The zip's length, -1 while unknown.
    public long Total { get; private set; } = -1;

    /// Why the last download failed; null after one that worked or while none ran.
    public AddonFailure? Failed { get; private set; }

    /// 0-100, or -1 while the length is unknown.
    public int Percent
    {
        get
        {
            lock (gate) return Total > 0 ? (int)Math.Clamp(Done * 100 / Total, 0, 100) : -1;
        }
    }

    /// False while one runs already.
    public bool Begin()
    {
        lock (gate)
        {
            if (Busy) return false;
            (Busy, Done, Total, Failed) = (true, 0, -1, null);
            return true;
        }
    }

    /// True when what the page shows changed: a whole percent, or each MB while the length is unknown.
    public bool Report(long done, long total)
    {
        lock (gate)
        {
            bool changed = total != Total || (total > 0 ? done * 100 / total != Done * 100 / total : done >> 20 != Done >> 20);
            (Done, Total) = (done, total);
            return changed;
        }
    }

    public void End(AddonFailure? failed)
    {
        lock (gate) (Busy, Failed) = (false, failed);
    }
}

/// Downloads a catalog add-on into <download root>\<id>\<version>: the zip to a temp file, its SHA-256 checked against
/// the catalog, unpacked into a staging folder (an entry whose path would leave it refuses the whole zip), its addon.json
/// checked for the catalog's id and version and every file for the hash addon.json gives, then renamed in. Whatever
/// fails, the temp file and the staging folder go, so a half download is never used or left behind.
internal static class AddonDownload
{
    /// Larger than any add-on should be: a server sending more is stopped there (the catalog's size, when given, instead).
    private const long MaxBytes = 512L * 1024 * 1024;

    /// No byte from the server for this long ends the download.
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    /// Download, check and put in, in one go (the tests; Addon.Download puts in under its own lock).
    public static async Task Install(HttpClient http, AddonCatalog.Entry e, string root, Action<long, long>? progress = null, CancellationToken ct = default)
    {
        string staging = await Prepare(http, e, root, progress, ct).ConfigureAwait(false);
        try
        {
            Commit(staging, e, root);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// The download, checked and unpacked: returns its staging folder (under <root>\<id>, named with a leading dot so
    /// it is never taken for a downloaded version). `progress`: bytes so far and the total (-1 while unknown).
    public static async Task<string> Prepare(HttpClient http, AddonCatalog.Entry e, string root, Action<long, long>? progress, CancellationToken ct)
    {
        if (!e.Offered) throw new AddonDownloadException(AddonFailure.Hash, $"{e.Id} {e.Version}: the catalog gives no SHA-256");
        string dir = Path.Combine(root, e.Id);
        string tag = $".{e.Version}.{Guid.NewGuid():N}";
        string zip = Path.Combine(dir, tag + ".zip.tmp"), staging = Path.Combine(dir, tag + ".tmp");
        try
        {
            Directory.CreateDirectory(dir);
            string sha = await Fetch(http, e, zip, progress, ct).ConfigureAwait(false);
            if (!string.Equals(sha, e.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new AddonDownloadException(AddonFailure.Hash, $"{e.Id} {e.Version}: SHA-256 {sha}, the catalog gives {e.Sha256}");
            Unpack(zip, staging);
            Verify(staging, e);
            return staging;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            TryDelete(staging);
            throw new AddonDownloadException(AddonFailure.Disk, x.Message, x);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException)
            {
                // a scanner holds it: the next download's Commit clears it
            }
        }
    }

    /// The checked staging folder becomes <root>\<id>\<version> (a broken one there before is replaced), and every other
    /// version and leftover beside it goes. Fast: the caller may hold its add-on's lock around this.
    public static void Commit(string staging, AddonCatalog.Entry e, string root)
    {
        string dir = Path.Combine(root, e.Id), target = Path.Combine(dir, e.Version);
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            throw new AddonDownloadException(AddonFailure.Disk, x.Message, x);
        }
        foreach (string sub in Directory.GetDirectories(dir))
            if (!string.Equals(sub, target, StringComparison.OrdinalIgnoreCase)) TryDelete(sub);
        foreach (string file in Directory.GetFiles(dir))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException)
            {
                // still open somewhere: the next download tries again
            }
        }
    }

    /// Streams the zip to `file`, hashing as it goes; returns the hex SHA-256. Redirects are followed by hand (the shared
    /// client follows none), https only and at most five.
    private static async Task<string> Fetch(HttpClient http, AddonCatalog.Entry e, string file, Action<long, long>? progress, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var url = new Uri(e.Url);
        HttpResponseMessage? response = null;
        try
        {
            for (int hops = 0; ; hops++)
            {
                if (url.Scheme != Uri.UriSchemeHttps) throw new AddonDownloadException(AddonFailure.Network, $"{url}: not https");
                idle.CancelAfter(Idle);
                response = await Net(() => http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, idle.Token)).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next && hops < 5)
                {
                    url = next.IsAbsoluteUri ? next : new Uri(url, next);
                    response.Dispose();
                    response = null;
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new AddonDownloadException(AddonFailure.Network, $"{url.Host}: HTTP {(int)response.StatusCode}");
                break;
            }
            var content = response.Content;
            long total = e.Size > 0 ? e.Size : content.Headers.ContentLength ?? -1;
            long limit = e.Size > 0 ? e.Size : MaxBytes;
            if (content.Headers.ContentLength is { } sent && sent > limit) throw new AddonDownloadException(AddonFailure.Hash, $"{e.Id}: {sent} bytes, more than {limit}");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var net = await Net(() => content.ReadAsStreamAsync(idle.Token)).ConfigureAwait(false);
            await using var to = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long done = 0;
            progress?.Invoke(0, total);
            while (true)
            {
                idle.CancelAfter(Idle);
                int n = await Net(() => net.ReadAsync(buffer, idle.Token).AsTask()).ConfigureAwait(false);
                if (n == 0) break;
                done += n;
                if (done > limit) throw new AddonDownloadException(AddonFailure.Hash, $"{e.Id}: more than {limit} bytes");
                hash.AppendData(buffer, 0, n);
                await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                progress?.Invoke(done, total);
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// A step that talks to the server: what goes wrong there is a network failure (a cancel by the caller stays one).
    private static async Task<T> Net<T>(Func<Task<T>> step)
    {
        try
        {
            return await step().ConfigureAwait(false);
        }
        catch (Exception x) when (x is HttpRequestException or IOException or OperationCanceledException)
        {
            throw new AddonDownloadException(AddonFailure.Network, x.Message, x);
        }
    }

    /// Every entry into `staging`, each by a path AddonManifest.SafePath accepts (no drive, root, "..", or name Windows
    /// can't keep), checked again against the folder's full path.
    private static void Unpack(string zip, string staging)
    {
        string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries)
            {
                bool folder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                string? path = AddonManifest.SafePath(folder ? entry.FullName.TrimEnd('/', '\\') : entry.FullName);
                string to = path is null ? "" : Path.GetFullPath(Path.Combine(staging, path));
                if (path is null || !to.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new AddonDownloadException(AddonFailure.Content, $"zip entry «{entry.FullName}» is not a path inside the add-on's folder");
                if (folder)
                {
                    Directory.CreateDirectory(to);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                entry.ExtractToFile(to, false);
            }
        }
        catch (InvalidDataException x)
        {
            throw new AddonDownloadException(AddonFailure.Content, x.Message, x);
        }
    }

    /// The unpacked add-on is the one the catalog names, and every file it lists has its hash.
    private static void Verify(string staging, AddonCatalog.Entry e)
    {
        string file = Path.Combine(staging, AddonManifest.FileName);
        if (!File.Exists(file)) throw new AddonDownloadException(AddonFailure.Content, "the zip has no addon.json");
        AddonManifest m;
        try
        {
            m = AddonManifest.Parse(File.ReadAllText(file));
        }
        catch (InvalidDataException x)
        {
            throw new AddonDownloadException(AddonFailure.Content, x.Message, x);
        }
        if (m.Id != e.Id || m.Version != e.Version)
            throw new AddonDownloadException(AddonFailure.Content, $"the zip holds {m.Id} {m.Version}, not {e.Id} {e.Version}");
        foreach (var (name, sha) in m.Files)
        {
            string f = Path.Combine(staging, name);
            if (!File.Exists(f) || !string.Equals(AddonStore.Sha256(f), sha, StringComparison.OrdinalIgnoreCase))
                throw new AddonDownloadException(AddonFailure.Content, $"{name}: missing or not matching addon.json");
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            // something in it is still open: the next download's Commit clears it
        }
    }
}
