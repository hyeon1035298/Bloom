using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Bloom.Core;

namespace Bloom.Addons;

/// Where add-ons are: the source folder (read only: inside Bloom's install folder, or a downloaded version's folder under
/// <Paths.Local>\addon-downloads, AddonDownload) and the add-on's own folder under Paths.Local, which holds its data
/// files and one run copy per version. The host runs from the run copy, never from the install folder: explorer keeps
/// the add-on's DLL loaded, and a locked file in the install folder would keep Velopack from swapping it at the next
/// update.
internal sealed class AddonStore
{
    private readonly string shippedRoot, localRoot;

    public AddonStore(string shippedRoot, string localRoot, string? downloadRoot = null)
    {
        this.shippedRoot = shippedRoot;
        this.localRoot = localRoot;
        DownloadRoot = downloadRoot;
    }

    /// Bloom's own: <install folder>\addons, downloaded ones in <Paths.Local>\addon-downloads, and <Paths.Local>\addons.
    public static AddonStore Default => new(Path.Combine(Paths.AppDir, "addons"), Path.Combine(Paths.Local, "addons"), Path.Combine(Paths.Local, "addon-downloads"));

    /// The user's own: add-ons they put in <Paths.Local>\user-addons\<id> themselves, none of them Bloom's. Read from
    /// there the way shipped ones are read from the install folder; their run copies and data sit beside the shipped
    /// ones in <Paths.Local>\addons.
    public static AddonStore User => new(Path.Combine(Paths.Local, "user-addons"), Path.Combine(Paths.Local, "addons"));

    /// Where downloaded add-ons go (<root>\<id>\<version>); null for a store that takes no downloads (the user's).
    public string? DownloadRoot { get; }

    /// The install folder holds this add-on (a local install or a dev build).
    public bool Bundled(string id) => File.Exists(Path.Combine(shippedRoot, id, AddonManifest.FileName));

    /// The folder the add-on is read from: the downloaded version (the latest when a clean-up that failed left two) when
    /// the install folder has none or an earlier version (or one it can't read), else the install folder's; with neither,
    /// the newest run copy (a Bloom that came with the add-on was replaced by one that doesn't, and nothing is downloaded
    /// yet: the copy that ran keeps running), else the install folder's path, which has none.
    public string SourceDir(string id)
    {
        string shipped = Path.Combine(shippedRoot, id);
        if (DownloadRoot is null) return shipped;
        string? downloaded = Newest(Path.Combine(DownloadRoot, id));
        if (Bundled(id))
            return downloaded is not null && (VersionIn(shipped) is not { } bundled || AddonCatalog.Newer(Path.GetFileName(downloaded), bundled)) ? downloaded : shipped;
        return downloaded ?? Newest(DataDir(id)) ?? shipped;
    }

    /// The version the addon.json in dir gives; null when it can't be read.
    private static string? VersionIn(string dir)
    {
        try
        {
            return AddonManifest.Parse(File.ReadAllText(Path.Combine(dir, AddonManifest.FileName))).Version;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// The highest version folder in dir with an addon.json in it; null when there is none.
    private static string? Newest(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            string? latest = Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
                .Where(v => !v.StartsWith('.') && AddonManifest.IsVersion(v) && File.Exists(Path.Combine(dir, v, AddonManifest.FileName)))
                .OrderByDescending(v => v, Comparer<string>.Create(AddonCatalog.Compare)).FirstOrDefault();
            return latest is null ? null : Path.Combine(dir, latest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// The add-ons in the read-only root: each folder named like an add-on id with an addon.json in it, by name.
    public IReadOnlyList<string> Ids()
    {
        try
        {
            if (!Directory.Exists(shippedRoot)) return Array.Empty<string>();
            return Directory.GetDirectories(shippedRoot).Select(Path.GetFileName).OfType<string>()
                .Where(n => AddonManifest.IsId(n) && File.Exists(Path.Combine(shippedRoot, n, AddonManifest.FileName)))
                .Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// The add-on's data folder (values.json, the host's and runtime's state files, its logs), with the run copies in it.
    public string DataDir(string id) => Path.Combine(localRoot, id);

    /// The add-on's manifest, shipped or downloaded (SourceDir); null when it is neither. InvalidDataException when it is
    /// there but wrong.
    public AddonManifest? Shipped(string id)
    {
        string file = Path.Combine(SourceDir(id), AddonManifest.FileName);
        if (!File.Exists(file)) return null;
        var m = AddonManifest.Parse(File.ReadAllText(file));
        return m.Id == id ? m : throw new InvalidDataException($"addon.json: id «{m.Id}» in the folder «{id}»");
    }

    /// The run copy of this version, made when missing or not matching its hashes: copied into a staging folder beside
    /// it, each file checked against the manifest, then renamed in, so a half copy is never started. Returns its folder.
    /// `release`: lets go of a copy that can't be replaced because files of it are in use (Replace).
    public string RunCopy(AddonManifest m, Action? release = null)
    {
        string target = Path.Combine(DataDir(m.Id), m.Version);
        if (Matches(target, m)) return target;
        string staging = Path.Combine(DataDir(m.Id), $".{m.Version}.{Guid.NewGuid():N}.tmp");
        string source = SourceDir(m.Id);
        try
        {
            foreach (var (name, sha) in m.Files)
            {
                string to = Path.Combine(staging, name);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(Path.Combine(source, name), to);
                if (!string.Equals(Sha256(to), sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{name}: SHA-256 does not match addon.json");
            }
            File.Copy(Path.Combine(source, AddonManifest.FileName), Path.Combine(staging, AddonManifest.FileName));
            if (Directory.Exists(target)) Replace(target, release);
            Directory.Move(staging, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(staging)) TryDelete(staging);
        }
    }

    /// Release waits for the host, but explorer drops the runtime DLL a moment after the host has seen it go.
    private static readonly TimeSpan[] ReleasedTries = { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800) };

    /// Deletes a copy that doesn't match: a broken one, or another build of the same version (a local install over a
    /// downloaded release). Nothing of it goes while a host runs from it or explorer holds its DLLs: a recursive delete
    /// would take every file not in use (addon.json, the schema) from under them before it fails. `release` then ends
    /// what holds it and the copy is deleted once it is free (IOException when there is no `release` or it stays in use).
    private static void Replace(string target, Action? release)
    {
        if (InUse(target))
        {
            if (release is null) throw new IOException($"{target}: in use");
            release();
            for (int i = 0; InUse(target); i++)
            {
                if (i == ReleasedTries.Length) throw new IOException($"{target}: still in use");
                Thread.Sleep(ReleasedTries[i]);
            }
        }
        Directory.Delete(target, true);
    }

    /// A file of the folder is open or mapped (a running exe, a loaded DLL): opening it for writing, shared with no one,
    /// fails then. Nothing is written.
    private static bool InUse(string dir)
    {
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                new FileStream(f, FileMode.Open, FileAccess.Write, FileShare.None).Dispose();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
        return false;
    }

    /// Run copies of other versions, and staging folders left by a crash, go; one whose DLL explorer still holds can't
    /// and stays for a later start. The data files and folders beside them are never touched.
    public void Prune(AddonManifest m)
    {
        string dir = DataDir(m.Id);
        if (!Directory.Exists(dir)) return;
        foreach (string sub in Directory.GetDirectories(dir))
        {
            string name = Path.GetFileName(sub);
            bool staging = name.StartsWith('.') && name.EndsWith(".tmp", StringComparison.Ordinal);
            bool oldCopy = name != m.Version && File.Exists(Path.Combine(sub, AddonManifest.FileName));
            if (staging) TryDelete(sub);
            else if (oldCopy) TryDeleteCopy(sub);
        }
    }

    /// Its files first and its addon.json last: a DLL still loaded stops it there, and the addon.json left behind keeps
    /// the folder known as a run copy for the next try.
    private static void TryDeleteCopy(string dir)
    {
        string manifest = Path.Combine(dir, AddonManifest.FileName);
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (!string.Equals(f, manifest, StringComparison.OrdinalIgnoreCase)) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        TryDelete(dir);
    }

    private static bool Matches(string dir, AddonManifest m)
    {
        if (!Directory.Exists(dir)) return false;
        foreach (var (name, sha) in m.Files)
        {
            string f = Path.Combine(dir, name);
            if (!File.Exists(f) || !string.Equals(Sha256(f), sha, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static string Sha256(string file)
    {
        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // something in it is still open: a later start tries again
        }
    }
}
