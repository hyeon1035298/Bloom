using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Bloom.Core;

/// What a document's bytes say: its text (every line ending made \n, as the pane shows it), how to write it back
/// (Encoding with its BOM or none, Crlf: CRLF line endings), why it can only be read (null: it can be edited), and
/// the hash of the bytes it came from.
internal sealed record DocText(string Text, Encoding Encoding, bool Crlf, string? ReadOnly, bool Large, byte[] Hash);

/// What the disk holds now against the bytes last read or written here.
internal enum DocState { Same, Changed, Gone, Busy }

/// A save's outcome: Saved; Conflict (another program changed the file since: nothing written); Gone (deleted or
/// moved: nothing written); Busy (another program holds the file: try again); Failed (Reason says why).
internal enum DocSaved { Saved, Conflict, Gone, Busy, Failed }

/// A Markdown file on this PC for its document window (Ui\DocWindow.cs): read and decoded, written back the way it was
/// written (a UTF-8 or UTF-16 BOM kept, its line ending), and watched for other programs' changes. A file without a BOM
/// must be valid UTF-8 to be edited; anything else shows read-only. Writes go through a temporary file in the same
/// folder (File.Replace), and never over a change another program made since the last read or write here (the bytes'
/// hash, checked before each write: a watcher may miss events). No UI: its owner shows what it says.
internal sealed class DocFile : IDisposable
{
    /// Larger than this (bytes) a file opens read-only as plain text, its first MaxEdit bytes only.
    public const int MaxEdit = 10 * 1024 * 1024;

    public string Path { get; }

    private readonly object gate = new(); // the hash and what writing keeps: a save against a watcher's look
    private byte[] hash = [];
    private Encoding encoding = Utf8;
    private bool crlf;
    private FileSystemWatcher? watcher;
    private Timer? settle;

    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// Another program changed, replaced, moved or deleted the file (on a pool thread, 200 ms after the last of a burst of
    /// events, a save's own included): Peek says whether its bytes differ from the ones known here.
    public event Action? Touched;

    public DocFile(string path) => Path = path;

    public static bool IsMarkdown(string path) => System.IO.Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase);

    /// How much of a file LooksBinary reads.
    private const int SniffBytes = 8192;

    /// Not text: a NUL byte in its first 8 KB without a UTF-16 BOM (a song, a picture, a program). Such a file read as
    /// text is megabytes of garbage that hold the UI thread for minutes. A file that can't be read now: false (the
    /// document window says why).
    public static bool LooksBinary(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[(int)Math.Min(SniffBytes, fs.Length)];
            fs.ReadExactly(head);
            return head is not ([0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) && Array.IndexOf(head, (byte)0) >= 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// The file as it is now, taken as the one known here (its hash, encoding and line ending): at the start and for
    /// 다시 읽기. A file that can't be read comes back empty and read-only with the reason.
    public DocText Read()
    {
        lock (gate)
        {
            DocText t;
            try
            {
                t = Decode(ReadBytes(out bool large), large, Writable());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                t = new DocText("", Utf8, false, Tr("파일을 열지 못함", "Couldn't open the file") + " · " + e.Message, false, []);
            }
            Adopt(t);
            return t;
        }
    }

    /// The disk against the bytes known here; Changed brings what it holds now (not yet taken: Adopt).
    public (DocState state, DocText? now) Peek()
    {
        lock (gate)
        {
            byte[] bytes;
            bool large;
            try
            {
                bytes = ReadBytes(out large);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return (DocState.Gone, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (DocState.Busy, null); // held by another program, or being deleted
            }
            if (SHA256.HashData(bytes).AsSpan().SequenceEqual(hash)) return (DocState.Same, null);
            return (DocState.Changed, Decode(bytes, large, Writable()));
        }
    }

    /// What Peek or Read brought becomes the version known here: the next save writes over it, the way it is written.
    public void Adopt(DocText t)
    {
        lock (gate)
        {
            hash = t.Hash;
            encoding = t.Encoding;
            crlf = t.Crlf;
        }
    }

    /// The text written to the file (any line endings: they become the file's own), unless the disk changed since the
    /// last read or write here or the file is gone — `force` (고친 내용 저장) writes anyway, making it again if it went.
    public (DocSaved result, string? reason) Save(string text, bool force)
    {
        byte[] bytes;
        lock (gate)
        {
            try
            {
                bytes = Encode(text, encoding, crlf);
            }
            catch (EncoderFallbackException)
            {
                return (DocSaved.Failed, Tr("이 파일에 저장할 수 없는 글자가 있음", "Has characters this file can't hold"));
            }
            byte[] next = SHA256.HashData(bytes);
            try
            {
                if (!force)
                {
                    if (!File.Exists(Path)) return (DocSaved.Gone, null);
                    byte[] disk = SHA256.HashData(ReadBytes(out _));
                    if (!disk.AsSpan().SequenceEqual(hash)) return (DocSaved.Conflict, null);
                    if (disk.AsSpan().SequenceEqual(next)) return (DocSaved.Saved, null); // the same bytes: nothing to write
                }
                Write(bytes);
                hash = next;
                return (DocSaved.Saved, null);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return force ? (DocSaved.Failed, e.Message) : (DocSaved.Gone, null);
            }
            catch (IOException e) when (Sharing(e))
            {
                return (DocSaved.Busy, null);
            }
            catch (UnauthorizedAccessException)
            {
                return (DocSaved.Failed, Tr("이 파일을 고칠 권한 없음", "No permission to change this file"));
            }
            catch (IOException e)
            {
                return (DocSaved.Failed, e.Message);
            }
        }
    }

    /// From now on, Touched follows the file. A folder that can't be watched (some network drives) just isn't: the check
    /// before each save still keeps another program's change.
    public void Watch()
    {
        if (watcher is not null || System.IO.Path.GetDirectoryName(Path) is not { } dir) return;
        settle = new Timer(_ => Touched?.Invoke());
        try
        {
            watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(Path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            watcher.Changed += Poke;
            watcher.Created += Poke;
            watcher.Deleted += Poke;
            watcher.Renamed += Poke;
            watcher.Error += (_, _) => settle?.Change(200, Timeout.Infinite); // events lost: a look all the same
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            Log.Error("doc watch", e);
            watcher?.Dispose();
            watcher = null;
        }
    }

    private void Poke(object? sender, FileSystemEventArgs e) => settle?.Change(200, Timeout.Infinite);

    public void Dispose()
    {
        watcher?.Dispose();
        settle?.Dispose();
        watcher = null;
        settle = null;
    }

    // ── reading and writing ─────────────────────────────────────────────────────────────────────

    /// The file's bytes (its first MaxEdit when it is larger: `large`), shared with anyone writing or deleting it.
    private byte[] ReadBytes(out bool large)
    {
        using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        large = fs.Length > MaxEdit;
        var bytes = new byte[large ? MaxEdit : fs.Length];
        fs.ReadExactly(bytes);
        return bytes;
    }

    /// Why this file can't be written (null: it can): the read-only mark, or no permission to open it for writing.
    private string? Writable()
    {
        try
        {
            if ((File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0) return Tr("읽기 전용 파일", "The file is read-only");
            using var _ = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); // opened, nothing written
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Tr("이 파일을 고칠 권한 없음", "No permission to change this file");
        }
        catch (IOException)
        {
            return null; // held by another program now: a save tries again
        }
    }

    /// The new bytes in place of the file's: a temporary file beside it, then File.Replace (File.Move over it where the
    /// drive can't replace; the file itself rewritten where the folder takes no new files). Gone: made again.
    private void Write(byte[] bytes)
    {
        string dir = System.IO.Path.GetDirectoryName(Path)!;
        string temp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            try
            {
                using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Write(bytes);
                fs.Flush(true);
            }
            catch (UnauthorizedAccessException) when (File.Exists(Path))
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.Read);
                fs.SetLength(0);
                fs.Write(bytes);
                fs.Flush(true);
                return;
            }
            if (!File.Exists(Path)) File.Move(temp, Path);
            else
                try
                {
                    File.Replace(temp, Path, null, true);
                }
                catch (Exception e) when (e is PlatformNotSupportedException || (e is IOException io && !Sharing(io) && e is not FileNotFoundException))
                {
                    File.Move(temp, Path, true);
                }
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("doc temp", e);
            }
        }
    }

    /// Another program holds the file (sharing or lock violation; File.Replace: the file in use can't be replaced).
    private static bool Sharing(IOException e) => (e.HResult & 0xFFFF) is 32 or 33 or 1175;

    /// Bytes → text: a UTF-8 or UTF-16 BOM says the encoding (kept for writing back), no BOM must be strict UTF-8. Not
    /// decodable: shown anyway (the PC's legacy code page for a file without a BOM) and read-only, as is a large one
    /// and one `notWritable` names a reason for.
    internal static DocText Decode(byte[] bytes, bool large, string? notWritable)
    {
        Encoding enc;
        int skip = 0;
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            enc = new UTF8Encoding(true, true);
            skip = 3;
        }
        else if (bytes is [0xFF, 0xFE, ..])
        {
            enc = new UnicodeEncoding(false, true, true);
            skip = 2;
        }
        else if (bytes is [0xFE, 0xFF, ..])
        {
            enc = new UnicodeEncoding(true, true, true);
            skip = 2;
        }
        else enc = Utf8;
        string raw;
        string? reason = null;
        try
        {
            raw = enc.GetString(bytes, skip, bytes.Length - skip);
        }
        catch (DecoderFallbackException)
        {
            // a large file's cut may split a character: its start is still read as it is
            if (large) raw = Lenient(enc).GetString(bytes, skip, bytes.Length - skip);
            else
            {
                raw = (skip == 0 ? Legacy() : Lenient(enc)).GetString(bytes, skip, bytes.Length - skip);
                reason = skip == 0 ? Tr("UTF-8이 아니라 고칠 수 없음", "Not UTF-8, so it can't be edited") : Tr("읽을 수 없는 글자가 있어 고칠 수 없음", "Has unreadable characters, so it can't be edited");
            }
        }
        if (large) reason = Tr("10 MB 초과 · 앞부분만 텍스트로 표시", "Over 10 MB · only its start, as plain text");
        reason ??= notWritable;
        return new DocText(Lf(raw), enc, MostlyCrlf(raw), reason, large, SHA256.HashData(bytes));
    }

    /// The line ending most of the text's lines have: CRLF when its CRLF lines are at least as many as its bare-LF ones (a
    /// text without any line break: LF). Lone CRs don't count.
    private static bool MostlyCrlf(string raw)
    {
        int pairs = 0, bare = 0;
        for (int k = raw.IndexOf('\n', StringComparison.Ordinal); k >= 0; k = raw.IndexOf('\n', k + 1))
            if (k > 0 && raw[k - 1] == '\r') pairs++;
            else bare++;
        return pairs > 0 && pairs >= bare;
    }

    /// What couldn't go to the file, kept in a new file beside it — `<name> (Bloom 복구 yyyy-MM-dd HHmm).md`, written the
    /// way the file is — or in `fallbackDir` (made if needed) when its folder takes none: Bloom ending with no one to ask.
    /// The path written, or null when neither took it.
    public string? Rescue(string text, string fallbackDir, DateTime now)
    {
        byte[] bytes;
        lock (gate)
        {
            try
            {
                bytes = Encode(text, encoding, crlf);
            }
            catch (EncoderFallbackException)
            {
                bytes = Encode(text, new UTF8Encoding(true), crlf); // a character the file's encoding can't hold: UTF-8 with its BOM
            }
        }
        string name = System.IO.Path.GetFileNameWithoutExtension(Path) + " (" + Tr("Bloom 복구", "Bloom recovery") + " " + now.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture) + ")";
        string ext = System.IO.Path.GetExtension(Path);
        foreach (string? dir in new[] { System.IO.Path.GetDirectoryName(Path), fallbackDir })
        {
            if (dir is null) continue;
            try
            {
                if (ReferenceEquals(dir, fallbackDir)) Directory.CreateDirectory(dir); // the document's own folder is never made again
                for (int n = 1; n < 100; n++)
                {
                    string copy = System.IO.Path.Combine(dir, name + (n > 1 ? " " + n : "") + ext);
                    if (File.Exists(copy)) continue;
                    using var fs = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    fs.Write(bytes);
                    fs.Flush(true);
                    return copy;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Log.Error("doc rescue " + dir, e);
            }
        }
        return null;
    }

    /// Text → the file's bytes: its BOM (if it had one), its line ending on every line.
    internal static byte[] Encode(string text, Encoding enc, bool crlf)
    {
        string t = Lf(text);
        if (crlf) t = t.Replace("\n", "\r\n", StringComparison.Ordinal);
        byte[] bom = enc.GetPreamble(), body = enc.GetBytes(t);
        if (bom.Length == 0) return body;
        var all = new byte[bom.Length + body.Length];
        bom.CopyTo(all, 0);
        body.CopyTo(all, bom.Length);
        return all;
    }

    /// Every line ending (CRLF, a lone CR) as \n.
    internal static string Lf(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static Encoding Lenient(Encoding enc) => enc is UnicodeEncoding u ? new UnicodeEncoding(u.GetPreamble() is [0xFE, ..], true, false) : new UTF8Encoding(enc.GetPreamble().Length > 0, false);

    /// The PC's legacy code page (CP949 on a Korean Windows), to show a file written in it.
    private static Encoding Legacy()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int acp = GetACP();
        try
        {
            return acp == 65001 ? Lenient(Utf8) : Encoding.GetEncoding(acp);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Lenient(Utf8);
        }
    }

    [DllImport("kernel32.dll")] private static extern int GetACP();
}

/// Where a link in a document or note goes: the web (http, https, mailto only), another Markdown file (its own document
/// window), another file or folder on this PC (shown in its folder, never run), or nowhere (a #heading, a relative link
/// with no folder to start from, any other address — ms-msdt:, search-ms: and the like hand a click to whatever
/// program registered them, so a written link never starts one).
internal static class DocLink
{
    public enum Kind { None, Web, Doc, File }

    /// `href` as the document wrote it; relative paths start at `baseDir` (the document's folder), %20 and the like
    /// decoded, a #part left off.
    public static (Kind kind, string target) Resolve(string href, string? baseDir)
    {
        string h = href.Trim();
        if (h.Length == 0 || h[0] == '#') return (Kind.None, "");
        string path;
        if (Uri.TryCreate(h, UriKind.Absolute, out var u))
        {
            if (!u.IsFile) return u.Scheme is "http" or "https" or "mailto" ? (Kind.Web, h) : (Kind.None, "");
            path = u.LocalPath;
        }
        else
        {
            int cut = h.IndexOf('#', StringComparison.Ordinal);
            if (cut >= 0) h = h[..cut];
            if (baseDir is null || h.Length == 0) return (Kind.None, "");
            try
            {
                path = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, Uri.UnescapeDataString(h).Replace('/', '\\')));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return (Kind.None, "");
            }
        }
        return (DocFile.IsMarkdown(path) ? Kind.Doc : Kind.File, path);
    }

    /// A path on another computer (\\host\share\…, //host/…, and the \\?\ and \\.\ forms): opening it, even only to see
    /// whether it is there, makes Windows sign in to that computer with the user's account.
    public static bool IsNetwork(string path) => path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/';

    /// The file on this PC a picture's `src` (![alt](src)) means, made full; null for none: the web, a relative path with
    /// no folder to start from, and any network path — drawing a document or note never reaches another computer (Windows
    /// would hand it the user's sign-in hash). Only a document that itself lives on a share (`baseDir` \\host\share\…)
    /// shows pictures there, from its own folder and below. Nothing here touches the disk.
    public static string? Picture(string src, string? baseDir)
    {
        string s = src.Trim();
        string path;
        try
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            {
                if (!u.IsFile) return null;
                path = u.LocalPath;
            }
            else
            {
                if (baseDir is null) return null;
                path = System.IO.Path.Combine(baseDir, Uri.UnescapeDataString(s));
            }
            path = System.IO.Path.GetFullPath(path);
            if (!IsNetwork(path)) return path;
            if (baseDir is null || !IsNetwork(baseDir)) return null;
            string folder = System.IO.Path.GetFullPath(baseDir).TrimEnd('\\') + "\\";
            return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }
}
