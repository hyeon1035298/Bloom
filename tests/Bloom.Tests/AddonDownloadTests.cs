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

/// Settings > Add-ons downloads the taskbar add-on from the release the built-in catalog pins: only a zip with the pinned
/// SHA-256, unpacked inside its folder, holding the catalog's id and version and files matching addon.json, is put in;
/// anything else leaves nothing behind. No network: a fake handler answers.
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

    private static AddonCatalog.Entry Entry(byte[] zip, string version = "0.2.2") =>
        new(Id, version, "https://example.invalid/pill.zip", Sha(zip), 0);

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

    private static AddonFailure Fails(HttpClient http, AddonCatalog.Entry e, string root)
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
        AddonDownload.Install(new HttpClient(fake), Entry(zip), downloads, (d, t) => progress.Add((d, t))).GetAwaiter().GetResult();
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

        // the page's offer: none at the same version, Update for a newer one, none without a hash
        var addon = new Addon(Id, store);
        Check.True(addon.Manifest is not null && !addon.Broken, "the add-on reads it");
        Check.Eq(addon.Offer(_ => Entry(zip)), null, "the catalog's version is there: nothing offered");
        Check.Eq(addon.Offer(_ => Entry(zip, "0.2.3"))?.Version, "0.2.3", "a newer catalog version is offered");
        Check.Eq(addon.Offer(_ => Entry(zip, "0.2.3") with { Sha256 = "" }), null, "an entry without a hash is not offered");

        // an update through the add-on: the new version is put in, the old one goes, the manifest is read again
        byte[] newer = Zip(Manifest(version: "0.2.3"));
        addon.Download(Serving(newer), Entry(newer, "0.2.3")).GetAwaiter().GetResult();
        Check.Eq(addon.Fetch.Busy, false, "done");
        Check.Eq(addon.Fetch.Failed, null, "no failure");
        Check.Eq(addon.Manifest?.Version, "0.2.3", "the manifest follows the download");
        Check.True(!Directory.Exists(dir) && Directory.Exists(Path.Combine(downloads, Id, "0.2.3")), "the old version's source goes");

        // a copy in the install folder comes first: nothing is offered for it
        string shipped = Path.Combine(root, "shipped", Id);
        Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(shipped, AddonManifest.FileName), Manifest(version: "0.1.0").ToJsonString());
        Check.Eq(store.SourceDir(Id), shipped, "the install folder's copy wins");
        Check.Eq(new Addon(Id, store).Offer(_ => Entry(zip, "0.9.0")), null, "nothing offered over the install folder's copy");
    });

    public static void HashMismatchRefused() => Temp(root =>
    {
        byte[] zip = Zip(Manifest());
        Check.Eq(Fails(Serving(zip), Entry(zip) with { Sha256 = Sha(Exe) }, root), AddonFailure.Hash, "a zip that isn't the pinned one");
        Empty(root, "hash");
        // a file inside not matching addon.json
        byte[] bad = Zip(Manifest(dllSha: Sha(Exe)));
        Check.Eq(Fails(Serving(bad), Entry(bad), root), AddonFailure.Content, "a file not matching addon.json");
        Empty(root, "file hash");
    });

    public static void PathEscapeRefused() => Temp(root =>
    {
        string downloads = Path.Combine(root, "downloads");
        foreach (string evil in new[] { "../evil.txt", "bin/../../evil.txt", "C:/evil.txt", "/evil.txt", "a/..\\..\\evil.txt" })
        {
            byte[] zip = Zip(Manifest(), (evil, Encoding.ASCII.GetBytes("x")));
            Check.Eq(Fails(Serving(zip), Entry(zip), downloads), AddonFailure.Content, evil);
            Empty(downloads, evil);
            Check.True(!File.Exists(Path.Combine(downloads, "evil.txt")) && !File.Exists(Path.Combine(root, "evil.txt")), evil + ": not written outside");
        }
    });

    public static void WrongIdOrVersionRefused() => Temp(root =>
    {
        byte[] version = Zip(Manifest(version: "0.2.1"));
        Check.Eq(Fails(Serving(version), Entry(version), root), AddonFailure.Content, "another version");
        Empty(root, "version");
        byte[] id = Zip(Manifest(id: "other-addon"));
        Check.Eq(Fails(Serving(id), Entry(id), root), AddonFailure.Content, "another id");
        Empty(root, "id");
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using var s = z.CreateEntry("readme.txt").Open();
            s.Write(Exe);
        }
        byte[] none = ms.ToArray();
        Check.Eq(Fails(Serving(none), Entry(none), root), AddonFailure.Content, "no addon.json");
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
        Check.Eq(Fails(http, Entry(zip), root), AddonFailure.Network, "a connection that breaks off");
        Empty(root, "interrupted");
        var missing = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Check.Eq(Fails(missing, Entry(zip), root), AddonFailure.Network, "404");
        Empty(root, "404");
        var plain = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://example.invalid/zip") } }));
        Check.Eq(Fails(plain, Entry(zip), root), AddonFailure.Network, "a redirect to plain http");
        Check.Eq(Fails(Serving(zip), Entry(zip) with { Size = zip.Length - 1 }, root), AddonFailure.Hash, "longer than the catalog's size");
        Empty(root, "too long");
    });

    public static void CatalogParsed()
    {
        string sha = new('a', 64);
        var list = AddonCatalog.Parse(new JsonObject
        {
            ["addons"] = new JsonArray
            {
                new JsonObject { ["id"] = "pill-taskbar", ["version"] = "0.2.2", ["url"] = "https://example.invalid/a.zip", ["sha256"] = sha.ToUpperInvariant(), ["size"] = 1234 },
                new JsonObject { ["id"] = "waiting", ["version"] = "1.0.0", ["url"] = "https://example.invalid/b.zip", ["sha256"] = "" },
                new JsonObject { ["id"] = "Bad_Id", ["version"] = "1.0.0", ["url"] = "https://example.invalid/c.zip", ["sha256"] = sha },
                new JsonObject { ["id"] = "plain", ["version"] = "1.0.0", ["url"] = "http://example.invalid/d.zip", ["sha256"] = sha },
                new JsonObject { ["id"] = "short", ["version"] = "1.0.0", ["url"] = "https://example.invalid/e.zip", ["sha256"] = "abc" },
                new JsonObject { ["id"] = "dots", ["version"] = "..", ["url"] = "https://example.invalid/f.zip", ["sha256"] = sha },
            },
        }.ToJsonString());
        Check.Eq(string.Join(",", list.Select(e => e.Id)), "pill-taskbar,waiting", "the wrong entries left out");
        Check.True(list[0].Offered && list[0].Sha256 == sha && list[0].Size == 1234, "an entry with its hash is offered");
        Check.True(!list[1].Offered, "an empty hash: not offered");
        Check.True(AddonCatalog.BuiltIn.Any(e => e.Id == AddonProtocol.Id), "the catalog is built into Bloom");
        Check.True(AddonCatalog.BuiltIn.All(e => e.Offered), "every built-in entry carries its zip's hash");
        Check.True(AddonCatalog.BuiltIn.Where(e => e.Id != AddonProtocol.Id).All(e => e.Title("en") is not null && e.About("en").Length > 0),
            "every built-in entry besides the taskbar one has a name and a line about it");
        Check.True(AddonCatalog.Newer("0.2.3", "0.2.2") && AddonCatalog.Newer("0.10.0", "0.9.9") && AddonCatalog.Newer("1.0.0", "1.0.0-beta"), "newer");
        Check.True(!AddonCatalog.Newer("0.2.2", "0.2.2") && !AddonCatalog.Newer("0.2.1", "0.2.2"), "not newer");
        Check.True(AddonCatalog.Newer("1.2.1-2", "1.2.1-1") && AddonCatalog.Newer("1.2.2-1", "1.2.1-2"), "an add-on build after the mod's version");
    }

    public static void CatalogNamesByLanguage()
    {
        string sha = new('a', 64);
        var list = AddonCatalog.Parse(new JsonObject
        {
            ["addons"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "named", ["version"] = "1.0.0", ["url"] = "https://example.invalid/a.zip", ["sha256"] = sha,
                    ["name"] = new JsonObject { ["ko"] = "이름", ["en"] = "Name" },
                    ["about"] = new JsonObject { ["en"] = "What it does", ["ko"] = 5 },
                },
                new JsonObject { ["id"] = "bare", ["version"] = "1.0.0", ["url"] = "https://example.invalid/b.zip", ["sha256"] = sha, ["name"] = "Plain" },
            },
        }.ToJsonString());
        Check.Eq(list.Count, 2, "a name that isn't an object leaves the entry in");
        Check.Eq(list[0].Title("ko") ?? "", "이름", "the language's name");
        Check.Eq(list[0].Title("ja") ?? "", "Name", "a language it lacks: English");
        Check.Eq(list[0].About("ko"), "What it does", "a line that isn't text is left out: English");
        Check.True(list[1].Title("en") is null && list[1].About("en").Length == 0, "no name or line: none");
    }
}
