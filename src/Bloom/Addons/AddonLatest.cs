using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Addons;

/// One release of an add-on, as its bloom-addon.json names it: the zip's address (in the same release), SHA-256 and
/// length, and the oldest Bloom it runs in.
internal sealed record AddonRelease(string Id, string Version, string Url, string Sha256, long Size, Version MinBloom)
{
    /// This Bloom can run it.
    public bool Fits(Version bloom) => bloom >= MinBloom;
}

/// An add-on's latest release: bloom-addon.json from https://github.com/<repo>/releases/latest/download/, which GitHub
/// sends on to the asset of the newest release that is neither a draft nor a pre-release (no API call, so no rate
/// limit). The file only says which zip to take and its hash; the zip's address is made here, always in that same
/// repository's release of that version.
internal static class AddonLatest
{
    public const string FileName = "bloom-addon.json";

    /// A bloom-addon.json longer than this is refused unread.
    private const int MaxBytes = 64 * 1024;

    public static Uri Address(AddonCatalog.Entry e) => new($"https://github.com/{e.Repo}/releases/latest/download/{FileName}");

    /// The latest release, read and checked. AddonDownloadException: Missing when the latest release has no
    /// bloom-addon.json (or there is no release), Network when it couldn't be read, Content when it isn't a valid one.
    public static async Task<AddonRelease> Fetch(HttpClient http, AddonCatalog.Entry e, CancellationToken ct = default)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var response = await AddonDownload.Get(http, Address(e), idle).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new AddonDownloadException(AddonFailure.Missing, $"{e.Repo}: no {FileName} in its latest release");
        if (response.StatusCode != HttpStatusCode.OK) throw new AddonDownloadException(AddonFailure.Network, $"{e.Repo}: HTTP {(int)response.StatusCode}");
        if (response.Content.Headers.ContentLength > MaxBytes) throw new AddonDownloadException(AddonFailure.Content, $"{FileName}: longer than {MaxBytes} bytes");
        var body = new MemoryStream();
        await AddonDownload.Net(async () =>
        {
            await using var s = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            var buffer = new byte[8192];
            int n;
            while ((n = await s.ReadAsync(buffer, idle.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + n > MaxBytes) throw new AddonDownloadException(AddonFailure.Content, $"{FileName}: longer than {MaxBytes} bytes");
                body.Write(buffer, 0, n);
            }
            return 0;
        }).ConfigureAwait(false);
        return Parse(Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length), e);
    }

    /// {"id", "version", "zip", "sha256", "size", "minBloom"}: the id the catalog gives, a version that can name a folder,
    /// a zip named by a plain file name, its SHA-256 in hex, its length and a Bloom version. AddonDownloadException
    /// (Content) for anything else.
    public static AddonRelease Parse(string json, AddonCatalog.Entry e)
    {
        JsonObject o;
        try
        {
            o = JsonNode.Parse(json) as JsonObject ?? throw Bad("not a JSON object");
        }
        catch (JsonException x)
        {
            throw Bad(x.Message);
        }
        string id = Text(o, "id"), version = Text(o, "version"), zip = Text(o, "zip"), sha = Text(o, "sha256").ToLowerInvariant();
        if (id != e.Id) throw Bad($"id «{id}», the catalog's is «{e.Id}»");
        if (!AddonManifest.IsVersion(version)) throw Bad($"version «{version}»");
        if (!IsZipName(zip)) throw Bad($"zip «{zip}» is not a plain .zip file name");
        if (!AddonManifest.IsSha256(sha)) throw Bad("sha256 is not 64 hex digits");
        if (o["size"] is not JsonValue sv || !sv.TryGetValue(out long size) || size <= 0) throw Bad("no size");
        if (!Version.TryParse(Text(o, "minBloom"), out var minBloom)) throw Bad("minBloom is not a version");
        string url = $"https://github.com/{e.Repo}/releases/download/v{Uri.EscapeDataString(version)}/{zip}";
        return new AddonRelease(id, version, url, sha, size, minBloom);
    }

    /// Letters, digits, '.', '-' and '_', ending in .zip: nothing that could be a path or need escaping in the address.
    internal static bool IsZipName(string name) => name.Length is > 4 and <= 200 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        && char.IsAsciiLetterOrDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static string Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    private static AddonDownloadException Bad(string why) => new(AddonFailure.Content, FileName + ": " + why);
}
