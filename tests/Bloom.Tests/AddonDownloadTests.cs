using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Addons;

namespace Bloom.Tests;

/// Settings > Add-ons downloads an add-on from its repository's latest release: bloom-addon.json there names the zip and
/// its SHA-256; only that zip, unpacked inside its folder, holding that id, version and minBloom and files matching
/// addon.json, is put in, and only when it is later than the version here; anything else leaves nothing behind. No
/// network: a fake handler answers.
internal static class AddonDownloadTests
{
    private const string Id = "pill-taskbar";

    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("host exe"), Dll = Encoding.ASCII.GetBytes("runtime dll");

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static JsonObject Manifest(string id = Id, string version = "0.2.2", string? dllSha = null) => new()
    {
        ["id"] = id,
        ["version"] = version,
        ["license"] = "GPL-3.0-only",
        ["source"] = "https://example.invalid/pill-taskbar",
        ["minBloom"] = "0.1.0",
        ["host"] = new JsonObject { ["exe"] = "PillTaskbarHost.exe", ["protocol"] = AddonProtocol.Version },
        ["files"] = new JsonObject { ["PillTaskbarHost.exe"] = Sha(Exe), ["bin/PillTaskbarRuntime.dll"] = dllSha ?? Sha(Dll) },
    };

    /// A release zip: addon.json and its files at the root, a LICENSE beside them, and `extra` entries.
    private static byte[] Zip(JsonObject manifest, params (string name, byte[] data)[] extra)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entries = new List<(string, byte[])>
            {
                (AddonManifest.FileName, Encoding.UTF8.GetBytes(manifest.ToJsonString())),
                ("PillTaskbarHost.exe", Exe),
                ("bin/PillTaskbarRuntime.dll", Dll),
                ("LICENSE", Encoding.ASCII.GetBytes("GPL")),
            };
            entries.AddRange(extra);
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    private static AddonRelease Release(byte[] zip, string version = "0.2.2") =>
        new(Id, version, "https://example.invalid/pill.zip", Sha(zip), zip.Length, new Version(0, 1, 0));

    /// The catalog's line for the test add-on: its repository and a floor below every version the tests use.
    private static readonly AddonCatalog.Entry Catalog = new(Id, "owner/pill", "0.0.1");

    /// bloom-addon.json for a zip, as an add-on's release workflow writes it.
    private static string Latest(byte[] zip, string version, string id = Id, string minBloom = "0.1.0") => new JsonObject
    {
        ["id"] = id,
        ["version"] = version,
        ["zip"] = $"Pill-v{version}.zip",
        ["sha256"] = Sha(zip),
        ["size"] = zip.Length,
        ["minBloom"] = minBloom,
    }.ToJsonString();

    /// GitHub as the add-on's repository: latest/download/bloom-addon.json sent on (302) to the asset, and the release's zip.
    private static Fake GitHub(byte[] zip, string version) => new(r => r.RequestUri!.AbsolutePath switch
    {
        "/owner/pill/releases/latest/download/bloom-addon.json" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://assets.example.invalid/latest") } },
        "/latest" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Latest(zip, version)) },
        var path when path == $"/owner/pill/releases/download/v{version}/Pill-v{version}.zip" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) },
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    });

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpClient Serving(byte[] body) => new(new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));

    private static void Temp(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-addon-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static AddonFailure Fails(HttpClient http, AddonRelease e, string root)
    {
        try
        {
            AddonDownload.Install(http, e, root).GetAwaiter().GetResult();
        }
        catch (AddonDownloadException x)
        {
            return x.Reason;
        }
        throw new InvalidOperationException("installed, though it should have been refused");
    }

    /// Nothing at all in <root>\pill-taskbar: no temp zip, no staging folder, no version.
    private static void Empty(string root, string what) =>
        Check.Eq(Directory.Exists(Path.Combine(root, Id)) ? Directory.GetFileSystemEntries(Path.Combine(root, Id)).Length : 0, 0, what + ": nothing left behind");

    public static void GoodInstall() => Temp(root =>
    {
        string downloads = Path.Combine(root, "downloads");
        byte[] zip = Zip(Manifest());
        // GitHub answers a release download with a redirect; the shared client follows none, so it is followed by hand
        var fake = new Fake(r => r.RequestUri!.Host == "example.invalid"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://assets.example.invalid/zip") } }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        var progress = new List<(long, long)>();
        AddonDownload.Install(new HttpClient(fake), Release(zip), downloads, (d, t) => progress.Add((d, t))).GetAwaiter().GetResult();
        Check.Eq(fake.Asked.Count, 2, "the redirect followed");
        string dir = Path.Combine(downloads, Id, "0.2.2");
        Check.True(File.Exists(Path.Combine(dir, AddonManifest.FileName)) && File.Exists(Path.Combine(dir, "bin", "PillTaskbarRuntime.dll")) && File.Exists(Path.Combine(dir, "LICENSE")), "unpacked into its version's folder");
        Check.Eq(Directory.GetFileSystemEntries(Path.Combine(downloads, Id)).Length, 1, "only the version: no temp zip or staging folder");
        Check.Eq(progress.Last(), ((long)zip.Length, (long)zip.Length), "progress ends at the whole zip");

        // the store reads it as the add-on's source, and the run copy is made from it
        var store = new AddonStore(Path.Combine(root, "shipped"), Path.Combine(root, "local"), downloads);
        Check.Eq(store.SourceDir(Id), dir, "the downloaded version is the source");
        var m = Check.NotNull(store.Shipped(Id), "its manifest");
        Check.Eq(m.Version, "0.2.2", "version");
        Check.True(File.Exists(Path.Combine(store.RunCopy(m), "PillTaskbarHost.exe")), "a run copy from the downloaded files");

        // the page's offer: none at the same version, Update for a newer one
        var addon = new Addon(Id, store) { Catalog = Catalog };
        Check.True(addon.Manifest is not null && !addon.Broken, "the add-on reads it");
        Check.Eq(addon.Offer(), null, "no latest release read yet: nothing offered");
        addon.Latest = Release(zip);
        Check.Eq(addon.Offer(), null, "the latest version is there: nothing offered");
        addon.Latest = Release(zip, "0.2.3");
        Check.Eq(addon.Offer()?.Version, "0.2.3", "a newer release is offered");

        // an update through the add-on: its latest release read, the new version put in, the old one gone, the
        // manifest read again
        byte[] newer = Zip(Manifest(version: "0.2.3"));
        addon.Latest = null;
        var github = GitHub(newer, "0.2.3");
        addon.Update(new HttpClient(github)).GetAwaiter().GetResult();
        Check.Eq(addon.Fetch.Busy, false, "done");
        Check.Eq(addon.Fetch.Failed, null, "no failure");
        Check.Eq(addon.Latest?.Version, "0.2.3", "the latest release read");
        Check.Eq(addon.Manifest?.Version, "0.2.3", "the manifest follows the download");
        Check.True(!Directory.Exists(dir) && Directory.Exists(Path.Combine(downloads, Id, "0.2.3")), "the old version's source goes");
        Check.Eq(github.Asked.Count, 3, "bloom-addon.json through its redirect, then the zip");
        Check.Eq(github.Asked[0].AbsoluteUri, "https://github.com/owner/pill/releases/latest/download/bloom-addon.json", "the latest release's asset, no API");
        Check.Eq(github.Asked[2].AbsoluteUri, "https://github.com/owner/pill/releases/download/v0.2.3/Pill-v0.2.3.zip", "the zip from the same repository's release");
        // nothing newer: a second update downloads nothing
        github.Asked.Clear();
        addon.Update(new HttpClient(github)).GetAwaiter().GetResult();
        Check.True(github.Asked.Count == 2 && addon.Fetch.Failed is null, "only bloom-addon.json read");

        // a copy in the install folder: used unless a later release was downloaded
        string shipped = Path.Combine(root, "shipped", Id);
        Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(shipped, AddonManifest.FileName), Manifest(version: "0.1.0").ToJsonString());
        Check.Eq(store.SourceDir(Id), Path.Combine(downloads, Id, "0.2.3"), "a later download wins over the install folder's copy");
        File.WriteAllText(Path.Combine(shipped, AddonManifest.FileName), Manifest(version: "0.2.3").ToJsonString());
        Check.Eq(store.SourceDir(Id), shipped, "the same version: the install folder's copy");
        File.WriteAllText(Path.Combine(shipped, AddonManifest.FileName), Manifest(version: "0.9.0").ToJsonString());
        Check.Eq(store.SourceDir(Id), shipped, "a later install folder's copy wins");
        var bundled = new Addon(Id, store) { Catalog = Catalog, Latest = Release(zip, "0.2.3") };
        Check.Eq(bundled.Offer(), null, "nothing offered below the install folder's copy");
        bundled.Latest = Release(zip, "0.9.1");
        Check.Eq(bundled.Offer()?.Version, "0.9.1", "a release later than the install folder's copy is offered");
    });

    /// A Bloom that came with the add-on was replaced by one that doesn't, and nothing is downloaded: the run copy the
    /// old one made is the source, so the add-on still starts, and a newer catalog release is still offered over it.
    public static void RunCopyStandsIn() => Temp(root =>
    {
        string downloads = Path.Combine(root, "downloads");
        byte[] zip = Zip(Manifest());
        AddonDownload.Install(Serving(zip), Release(zip), downloads).GetAwaiter().GetResult();
        var store = new AddonStore(Path.Combine(root, "shipped"), Path.Combine(root, "local"), downloads);
        string copy = store.RunCopy(Check.NotNull(store.Shipped(Id), "its manifest"));
        Directory.Delete(downloads, true);

        Check.Eq(store.SourceDir(Id), copy, "the run copy is the source");
        var addon = new Addon(Id, store) { Catalog = Catalog, Latest = Release(zip) };
        Check.True(addon.Manifest is { Version: "0.2.2" } && !addon.Broken, "the add-on reads it");
        Check.Eq(store.RunCopy(addon.Manifest!), copy, "it runs from that copy as it is");
        Check.Eq(addon.Offer(), null, "the latest version is there: nothing offered");
        addon.Latest = Release(zip, "0.2.3");
        Check.Eq(addon.Offer()?.Version, "0.2.3", "a newer release is offered");

        // a user store takes no downloads and has no such fallback
        var user = new AddonStore(Path.Combine(root, "user"), Path.Combine(root, "local"));
        Check.Eq(user.Shipped(Id), null, "nothing for the user's store");
    });

    public static void HashMismatchRefused() => Temp(root =>
    {
        byte[] zip = Zip(Manifest());
        Check.Eq(Fails(Serving(zip), Release(zip) with { Sha256 = Sha(Exe) }, root), AddonFailure.Hash, "a zip that isn't the one bloom-addon.json names");
        Empty(root, "hash");
        // a file inside not matching addon.json
        byte[] bad = Zip(Manifest(dllSha: Sha(Exe)));
        Check.Eq(Fails(Serving(bad), Release(bad), root), AddonFailure.Content, "a file not matching addon.json");
        Empty(root, "file hash");
    });

    public static void PathEscapeRefused() => Temp(root =>
    {
        string downloads = Path.Combine(root, "downloads");
        foreach (string evil in new[] { "../evil.txt", "bin/../../evil.txt", "C:/evil.txt", "/evil.txt", "a/..\\..\\evil.txt" })
        {
            byte[] zip = Zip(Manifest(), (evil, Encoding.ASCII.GetBytes("x")));
            Check.Eq(Fails(Serving(zip), Release(zip), downloads), AddonFailure.Content, evil);
            Empty(downloads, evil);
            Check.True(!File.Exists(Path.Combine(downloads, "evil.txt")) && !File.Exists(Path.Combine(root, "evil.txt")), evil + ": not written outside");
        }
    });

    public static void WrongIdOrVersionRefused() => Temp(root =>
    {
        byte[] version = Zip(Manifest(version: "0.2.1"));
        Check.Eq(Fails(Serving(version), Release(version), root), AddonFailure.Content, "another version");
        Empty(root, "version");
        byte[] id = Zip(Manifest(id: "other-addon"));
        Check.Eq(Fails(Serving(id), Release(id), root), AddonFailure.Content, "another id");
        Empty(root, "id");
        byte[] good = Zip(Manifest());
        Check.Eq(Fails(Serving(good), Release(good) with { MinBloom = new Version(0, 0, 1) }, root), AddonFailure.Content,
            "addon.json needs another Bloom than bloom-addon.json says");
        Empty(root, "minBloom");
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using var s = z.CreateEntry("readme.txt").Open();
            s.Write(Exe);
        }
        byte[] none = ms.ToArray();
        Check.Eq(Fails(Serving(none), Release(none), root), AddonFailure.Content, "no addon.json");
        Empty(root, "no addon.json");
    });

    /// A stream that gives `good` bytes and then breaks off, the way a dropped connection does.
    private sealed class Broken(byte[] data, int good) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => Position >= good ? throw new IOException("connection reset") : base.Read(buffer, offset, Math.Min(count, good - (int)Position));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
        {
            var b = new byte[buffer.Length];
            int n = Read(b, 0, b.Length);
            b.AsSpan(0, n).CopyTo(buffer.Span);
            return ValueTask.FromResult(n);
        }
    }

    public static void InterruptedDownloadLeavesNothing() => Temp(root =>
    {
        byte[] zip = Zip(Manifest());
        var http = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Broken(zip, zip.Length / 2)) }));
        Check.Eq(Fails(http, Release(zip), root), AddonFailure.Network, "a connection that breaks off");
        Empty(root, "interrupted");
        var missing = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Check.Eq(Fails(missing, Release(zip), root), AddonFailure.Network, "404");
        Empty(root, "404");
        var plain = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://example.invalid/zip") } }));
        Check.Eq(Fails(plain, Release(zip), root), AddonFailure.Network, "a redirect to plain http");
        Check.Eq(Fails(Serving(zip), Release(zip) with { Size = zip.Length - 1 }, root), AddonFailure.Hash, "longer than bloom-addon.json's size");
        Empty(root, "too long");
    });

    public static void CatalogParsed()
    {
        var list = AddonCatalog.Parse(new JsonObject
        {
            ["addons"] = new JsonArray
            {
                new JsonObject { ["id"] = "pill-taskbar", ["repo"] = "owner/Pill_Addon", ["minVersion"] = "0.2.2" },
                new JsonObject { ["id"] = "Bad_Id", ["repo"] = "owner/a", ["minVersion"] = "1.0.0" },
                new JsonObject { ["id"] = "no-repo", ["minVersion"] = "1.0.0" },
                new JsonObject { ["id"] = "path", ["repo"] = "owner/a/../b", ["minVersion"] = "1.0.0" },
                new JsonObject { ["id"] = "dots", ["repo"] = "owner/..", ["minVersion"] = "1.0.0" },
                new JsonObject { ["id"] = "url", ["repo"] = "https://github.com/owner/a", ["minVersion"] = "1.0.0" },
                new JsonObject { ["id"] = "no-floor", ["repo"] = "owner/a" },
                new JsonObject { ["id"] = "pill-taskbar", ["repo"] = "owner/again", ["minVersion"] = "1.0.0" },
            },
        }.ToJsonString());
        Check.Eq(string.Join(",", list.Select(e => e.Id)), "pill-taskbar", "the wrong entries left out, and a second line for an id");
        Check.True(list[0].Repo == "owner/Pill_Addon" && list[0].MinVersion == "0.2.2" && list[0].Page == "https://github.com/owner/Pill_Addon", "its repository and floor");
        Check.Eq(AddonLatest.Address(list[0]).AbsoluteUri, "https://github.com/owner/Pill_Addon/releases/latest/download/bloom-addon.json", "its latest release's bloom-addon.json");
        Check.True(AddonCatalog.BuiltIn.Any(e => e.Id == AddonProtocol.Id), "the catalog is built into Bloom");
        Check.True(AddonCatalog.BuiltIn.All(e => e.Repo.StartsWith("hyeon1035298/", StringComparison.Ordinal)), "every built-in entry is one of Bloom's add-on repositories");
        Check.True(AddonCatalog.BuiltIn.Where(e => e.Id != AddonProtocol.Id).All(e => e.Title("en") is not null && e.About("en").Length > 0),
            "every built-in entry besides the taskbar one has a name and a line about it");
        Check.True(AddonCatalog.Newer("0.2.3", "0.2.2") && AddonCatalog.Newer("0.10.0", "0.9.9") && AddonCatalog.Newer("1.0.0", "1.0.0-beta"), "newer");
        Check.True(!AddonCatalog.Newer("0.2.2", "0.2.2") && !AddonCatalog.Newer("0.2.1", "0.2.2"), "not newer");
        Check.True(AddonCatalog.Newer("1.2.1-2", "1.2.1-1") && AddonCatalog.Newer("1.2.2-1", "1.2.1-2"), "an add-on build after the mod's version");
    }

    public static void CatalogNamesByLanguage()
    {
        var list = AddonCatalog.Parse(new JsonObject
        {
            ["addons"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "named", ["repo"] = "owner/a", ["minVersion"] = "1.0.0",
                    ["name"] = new JsonObject { ["ko"] = "이름", ["en"] = "Name" },
                    ["about"] = new JsonObject { ["en"] = "What it does", ["ko"] = 5 },
                },
                new JsonObject { ["id"] = "bare", ["repo"] = "owner/b", ["minVersion"] = "1.0.0", ["name"] = "Plain" },
            },
        }.ToJsonString());
        Check.Eq(list.Count, 2, "a name that isn't an object leaves the entry in");
        Check.Eq(list[0].Title("ko") ?? "", "이름", "the language's name");
        Check.Eq(list[0].Title("ja") ?? "", "Name", "a language it lacks: English");
        Check.Eq(list[0].About("ko"), "What it does", "a line that isn't text is left out: English");
        Check.True(list[1].Title("en") is null && list[1].About("en").Length == 0, "no name or line: none");
    }

    private static AddonFailure Refused(string json)
    {
        try
        {
            AddonLatest.Parse(json, Catalog);
        }
        catch (AddonDownloadException x)
        {
            return x.Reason;
        }
        throw new InvalidOperationException("read, though it should have been refused");
    }

    /// bloom-addon.json: the release it names, its zip always in the same repository's release of that version; anything
    /// not plainly one is refused.
    public static void LatestParsed()
    {
        byte[] zip = Zip(Manifest());
        var r = AddonLatest.Parse(Latest(zip, "0.2.3"), Catalog);
        Check.True(r.Id == Id && r.Version == "0.2.3" && r.Sha256 == Sha(zip) && r.Size == zip.Length && r.MinBloom == new Version(0, 1, 0), "read");
        Check.Eq(r.Url, "https://github.com/owner/pill/releases/download/v0.2.3/Pill-v0.2.3.zip", "the zip in the same repository's release");
        JsonObject Edit(Action<JsonObject> change)
        {
            var o = (JsonObject)JsonNode.Parse(Latest(zip, "0.2.3"))!;
            change(o);
            return o;
        }
        Check.Eq(Refused(Latest(zip, "0.2.3", id: "other")), AddonFailure.Content, "another add-on's");
        Check.Eq(Refused(Latest(zip, "..")), AddonFailure.Content, "a version that isn't one");
        foreach (string name in new[] { "../evil.zip", "a/b.zip", "https://evil.invalid/a.zip", "a.exe", ".zip", "a b.zip", "a%2F.zip" })
            Check.Eq(Refused(Edit(o => o["zip"] = name).ToJsonString()), AddonFailure.Content, "zip " + name);
        Check.Eq(Refused(Edit(o => o["sha256"] = "abc").ToJsonString()), AddonFailure.Content, "a short hash");
        Check.Eq(Refused(Edit(o => o.Remove("size")).ToJsonString()), AddonFailure.Content, "no size");
        Check.Eq(Refused(Edit(o => o["size"] = 0).ToJsonString()), AddonFailure.Content, "size 0");
        Check.Eq(Refused(Edit(o => o["minBloom"] = "soon").ToJsonString()), AddonFailure.Content, "minBloom not a version");
        Check.Eq(Refused("[1, 2]"), AddonFailure.Content, "not an object");
        Check.Eq(Refused("{"), AddonFailure.Content, "not JSON");
    }

    /// A release is offered only when later than what is here, not below the catalog's floor and not needing a newer
    /// Bloom: an older release served as the latest never replaces a newer version (a downgrade).
    public static void DowngradeRefused()
    {
        var floor = Catalog with { MinVersion = "0.2.0" };
        var bloom = new Version(1, 0, 0);
        AddonRelease At(string v, Version? min = null) => new(Id, v, "https://example.invalid/a.zip", new string('a', 64), 1, min ?? new Version(0, 1, 0));
        Check.Eq(Addon.Pick(At("0.2.3"), floor, "0.2.2", bloom)?.Version, "0.2.3", "later: offered");
        Check.Eq(Addon.Pick(At("0.2.2"), floor, "0.2.2", bloom), null, "the same version: not");
        Check.Eq(Addon.Pick(At("0.2.1"), floor, "0.2.2", bloom), null, "earlier: never");
        Check.Eq(Addon.Pick(At("0.1.9"), floor, null, bloom), null, "nothing here, but below the catalog's floor: never");
        Check.Eq(Addon.Pick(At("0.2.0"), floor, null, bloom)?.Version, "0.2.0", "nothing here, at the floor: offered");
        Check.Eq(Addon.Pick(At("0.3.0", new Version(2, 0, 0)), floor, "0.2.2", bloom), null, "needs a newer Bloom: not");
        Check.Eq(Addon.Pick(At("0.3.0") with { Id = "other" }, floor, "0.2.2", bloom), null, "another add-on's: not");
        Check.Eq(Addon.Pick(null, floor, "0.2.2", bloom), null, "none read: nothing");
    }

    /// The latest release has no bloom-addon.json (one made before this), or GitHub can't be reached: Missing or
    /// Network, nothing downloaded, the version here left as it is.
    public static void LatestMissing() => Temp(root =>
    {
        string downloads = Path.Combine(root, "downloads");
        var store = new AddonStore(Path.Combine(root, "shipped"), Path.Combine(root, "local"), downloads);
        var addon = new Addon(Id, store) { Catalog = Catalog };
        addon.Update(new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))).GetAwaiter().GetResult();
        Check.Eq(addon.Fetch.Failed, AddonFailure.Missing, "no bloom-addon.json");
        addon.Update(new HttpClient(new Fake(_ => throw new HttpRequestException("offline")))).GetAwaiter().GetResult();
        Check.Eq(addon.Fetch.Failed, AddonFailure.Network, "offline");
        var huge = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[65 * 1024]) }));
        addon.Update(huge).GetAwaiter().GetResult();
        Check.Eq(addon.Fetch.Failed, AddonFailure.Content, "a bloom-addon.json too long to be one");
        Check.True(addon.Manifest is null && addon.Latest is null && !Directory.Exists(Path.Combine(downloads, Id)), "nothing downloaded");
    });

    /// 이슈 열기: a new issue in the add-on's repository with the id, version and status, and nothing about the user.
    public static void IssueLink()
    {
        string url = Bloom.Ui.SettingsView.IssueUrl(Catalog, "0.2.3", AddonState.SafeMode);
        Check.True(url.StartsWith("https://github.com/owner/pill/issues/new?title=", StringComparison.Ordinal), "the repository's new issue");
        string text = Uri.UnescapeDataString(url);
        Check.True(text.Contains("pill-taskbar 0.2.3: SafeMode") && text.Contains("Bloom: ") && text.Contains("Windows: "), "id, version, status, Bloom and Windows");
        Check.True(!text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) && !text.Contains(@":\"), "no user name or path");
    }
}
