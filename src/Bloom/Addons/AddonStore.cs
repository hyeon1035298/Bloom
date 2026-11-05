using System;
using System.IO;
using System.Security.Cryptography;
using Bloom.Core;

namespace Bloom.Addons;

/// Where add-ons are: the shipped folder (read only, inside Bloom's install folder) and the add-on's own folder under
/// Paths.Local, which holds its data files and one run copy per version. The host runs from the run copy, never from the
/// install folder: explorer keeps the add-on's DLL loaded, and a locked file in the install folder would keep Velopack
/// from swapping it at the next update.
internal sealed class AddonStore
{
    private readonly string shippedRoot, localRoot;

    public AddonStore(string shippedRoot, string localRoot)
    {
        this.shippedRoot = shippedRoot;
        this.localRoot = localRoot;
    }

    /// Bloom's own: <install folder>\addons and <Paths.Local>\addons.
    public static AddonStore Default => new(Path.Combine(Paths.AppDir, "addons"), Path.Combine(Paths.Local, "addons"));

    public string ShippedDir(string id) => Path.Combine(shippedRoot, id);

    /// The add-on's data folder (values.json, the host's and runtime's state files, its logs), with the run copies in it.
    public string DataDir(string id) => Path.Combine(localRoot, id);

    /// The shipped add-on's manifest; null when Bloom came without it. InvalidDataException when it is there but wrong.
    public AddonManifest? Shipped(string id)
    {
        string file = Path.Combine(ShippedDir(id), AddonManifest.FileName);
        if (!File.Exists(file)) return null;
        var m = AddonManifest.Parse(File.ReadAllText(file));
        return m.Id == id ? m : throw new InvalidDataException($"addon.json: id «{m.Id}» in the folder «{id}»");
    }

    /// The run copy of this version, made when missing or not matching its hashes: copied into a staging folder beside
    /// it, each file checked against the manifest, then renamed in, so a half copy is never started. Returns its folder.
    public string RunCopy(AddonManifest m)
    {
        string target = Path.Combine(DataDir(m.Id), m.Version);
        if (Matches(target, m)) return target;
        string staging = Path.Combine(DataDir(m.Id), $".{m.Version}.{Guid.NewGuid():N}.tmp");
        try
        {
            foreach (var (name, sha) in m.Files)
            {
                string to = Path.Combine(staging, name);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(Path.Combine(ShippedDir(m.Id), name), to);
                if (!string.Equals(Sha256(to), sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{name}: SHA-256 does not match addon.json");
            }
            File.Copy(Path.Combine(ShippedDir(m.Id), AddonManifest.FileName), Path.Combine(staging, AddonManifest.FileName));
            if (Directory.Exists(target)) Directory.Delete(target, true); // a broken copy (throws while a file of it is still loaded)
            Directory.Move(staging, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(staging)) TryDelete(staging);
        }
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
