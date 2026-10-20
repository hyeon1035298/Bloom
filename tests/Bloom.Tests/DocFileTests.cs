using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Bloom.Core;

namespace Bloom.Tests;

/// The document window's file (Core\DocFile.cs): what it reads and writes back (BOMs, strict UTF-8, line endings), its
/// own saves against another program's changes, and where a document's links go.
internal static class DocFileTests
{
    private static string Folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bloom-docfile-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Done(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch (IOException) { /* a watcher still letting go: the temp folder keeps it */ }
    }

    public static void BomsAndEncodingsAreKept()
    {
        string dir = Folder();
        try
        {
            foreach (var (name, enc) in new (string, Encoding)[] { ("utf8bom", new UTF8Encoding(true)), ("utf16le", new UnicodeEncoding(false, true)), ("utf16be", new UnicodeEncoding(true, true)), ("utf8", new UTF8Encoding(false)) })
            {
                string path = Path.Combine(dir, name + ".md");
                byte[] before = enc.GetPreamble().Concat(enc.GetBytes("# 제목\n- [ ] 할 일\n")).ToArray();
                File.WriteAllBytes(path, before);
                using var f = new DocFile(path);
                var t = f.Read();
                Check.Eq(t.Text, "# 제목\n- [ ] 할 일\n", name + ": read");
                Check.Eq(t.ReadOnly, null, name + ": editable");
                Check.Eq(f.Save("# 제목\n- [x] 할 일\n", false).result, DocSaved.Saved, name + ": saved");
                byte[] after = File.ReadAllBytes(path);
                byte[] expected = enc.GetPreamble().Concat(enc.GetBytes("# 제목\n- [x] 할 일\n")).ToArray();
                Check.True(after.SequenceEqual(expected), name + ": the same BOM and encoding written back");
            }
        }
        finally
        {
            Done(dir);
        }
    }

    public static void NotUtf8OpensReadOnly()
    {
        string dir = Folder();
        try
        {
            string path = Path.Combine(dir, "old.md");
            File.WriteAllBytes(path, new byte[] { 0x23, 0x20, 0xC7, 0xD1, 0xB1, 0xDB, 0x0D, 0x0A }); // "# 한글" in CP949
            using var f = new DocFile(path);
            var t = f.Read();
            Check.True(t.ReadOnly is { Length: > 0 }, "a file without a BOM that isn't UTF-8 is read-only, with a reason");
            Check.True(t.ReadOnly!.Contains("UTF-8", StringComparison.Ordinal), "the reason names UTF-8: " + t.ReadOnly);
            Check.True(t.Text.StartsWith("# ", StringComparison.Ordinal), "it still shows");
            var bom = DocFile.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0xFF }, false, null);
            Check.True(bom.ReadOnly is not null, "broken bytes after a UTF-8 BOM: read-only too");
            var big = DocFile.Decode(Encoding.UTF8.GetBytes("abc"), true, null);
            Check.True(big.Large && big.ReadOnly is not null, "a large file is read-only");
            var marked = DocFile.Decode(Encoding.UTF8.GetBytes("abc"), false, "읽기 전용 파일이에요");
            Check.Eq(marked.ReadOnly, "읽기 전용 파일이에요", "the reason the file can't be written");
        }
        finally
        {
            Done(dir);
        }
    }

    public static void LineEndingsFollowTheFile()
    {
        var crlf = DocFile.Decode(Encoding.UTF8.GetBytes("a\r\nb\r\n"), false, null);
        Check.Eq(crlf.Text, "a\nb\n", "the pane gets \\n");
        Check.True(crlf.Crlf, "CRLF seen");
        Check.Eq(Encoding.UTF8.GetString(DocFile.Encode("a\nb\r\nc\n", crlf.Encoding, true)), "a\r\nb\r\nc\r\n", "a CRLF file: every line CRLF, whatever the editor gave");
        var lf = DocFile.Decode(Encoding.UTF8.GetBytes("a\nb\n"), false, null);
        Check.True(!lf.Crlf, "LF seen");
        Check.Eq(Encoding.UTF8.GetString(DocFile.Encode("a\r\nb\nc", lf.Encoding, false)), "a\nb\nc", "an LF file: every line LF");
        var mixed = DocFile.Decode(Encoding.UTF8.GetBytes("a\nb\r\nc\rd"), false, null);
        Check.Eq(mixed.Text, "a\nb\nc\nd", "mixed and lone CR: \\n");
        Check.True(mixed.Crlf, "as many CRLF lines as LF ones: CRLF");
        Check.Eq(Encoding.UTF8.GetString(DocFile.Encode(mixed.Text, mixed.Encoding, mixed.Crlf)), "a\r\nb\r\nc\r\nd", "one ending throughout");
        var mostlyLf = DocFile.Decode(Encoding.UTF8.GetBytes("a\nb\nc\r\nd\n"), false, null);
        Check.True(!mostlyLf.Crlf, "one CRLF line among LF ones: LF (the majority)");
        Check.Eq(Encoding.UTF8.GetString(DocFile.Encode(mostlyLf.Text, mostlyLf.Encoding, mostlyLf.Crlf)), "a\nb\nc\nd\n", "written LF throughout");
        var mostlyCrlf = DocFile.Decode(Encoding.UTF8.GetBytes("a\r\nb\r\nc\nd"), false, null);
        Check.True(mostlyCrlf.Crlf, "mostly CRLF: CRLF");
        Check.True(!DocFile.Decode(Encoding.UTF8.GetBytes("one line"), false, null).Crlf, "no line break at all: LF");
        Check.True(!DocFile.Decode(Encoding.UTF8.GetBytes("a\rb\rc\n"), false, null).Crlf, "lone CRs don't count for CRLF");
    }

    public static void RescueKeepsWhatCouldNotBeWritten()
    {
        string dir = Folder(), data = Folder();
        try
        {
            string path = Path.Combine(dir, "plan.md");
            File.WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("a\r\nb\r\n")).ToArray());
            using var f = new DocFile(path);
            f.Read();
            var at = new DateTime(2026, 10, 19, 9, 5, 0);
            string? copy = f.Rescue("a\nb\nmine\n", Path.Combine(data, "recovered"), at);
            Check.Eq(copy, Path.Combine(dir, "plan (" + Lang.Tr("Bloom 복구", "Bloom recovery") + " 2026-10-19 0905).md"), "beside the file, named for when");
            byte[] expected = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("a\r\nb\r\nmine\r\n")).ToArray();
            Check.True(File.ReadAllBytes(copy!).SequenceEqual(expected), "written the way the file is (its BOM, CRLF)");
            Check.Eq(File.ReadAllText(path), "a\r\nb\r\n", "the file itself untouched");
            string? second = f.Rescue("again\n", Path.Combine(data, "recovered"), at);
            Check.True(second is not null && second != copy && second.EndsWith(" 2.md", StringComparison.Ordinal), "the same minute again: a second copy, the first kept: " + second);
            // the document's folder gone: the data folder's recovered (made), the document's folder never made again
            Directory.Delete(dir, true);
            string? fallback = f.Rescue("still mine\n", Path.Combine(data, "recovered"), at);
            Check.True(fallback is not null && Path.GetDirectoryName(fallback) == Path.Combine(data, "recovered"), "in the data folder's recovered: " + fallback);
            Check.Eq(File.ReadAllText(fallback!), "still mine\r\n", "with the text");
            Check.True(!Directory.Exists(dir), "the document's folder isn't made again");
        }
        finally
        {
            Done(dir);
            Done(data);
        }
    }

    public static void SongsAndProgramsAreNotText()
    {
        string dir = Folder();
        try
        {
            string F(string name, byte[] bytes)
            {
                string p = Path.Combine(dir, name);
                File.WriteAllBytes(p, bytes);
                return p;
            }
            Check.True(DocFile.LooksBinary(F("a.flac", [0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22, 0x12])), "a song (NULs in its header)");
            Check.True(!DocFile.LooksBinary(F("a.txt", Encoding.UTF8.GetBytes("# 제목\n본문\n"))), "UTF-8 text");
            Check.True(!DocFile.LooksBinary(F("u16.txt", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("abc")])), "UTF-16 with its BOM (NULs between the letters)");
            Check.True(!DocFile.LooksBinary(F("empty.txt", [])), "an empty file");
            byte[] late = new byte[9000];
            Array.Fill(late, (byte)'a');
            late[8999] = 0;
            Check.True(!DocFile.LooksBinary(F("late.txt", late)), "only the first 8 KB is looked at");
            Check.True(!DocFile.LooksBinary(Path.Combine(dir, "gone.bin")), "a file that isn't there: the document window says why");
        }
        finally
        {
            Done(dir);
        }
    }

    public static void PicturesNeverReachAnotherComputer()
    {
        string local = @"C:\docs\notes";
        Check.Eq(DocLink.Picture("pics/a.png", local), @"C:\docs\notes\pics\a.png", "a relative picture beside the document");
        Check.Eq(DocLink.Picture("../a%20b.png", local), @"C:\docs\a b.png", "up a folder, %20 decoded");
        Check.Eq(DocLink.Picture(@"D:\shots\a.png", null), @"D:\shots\a.png", "an absolute path, even in a note");
        Check.Eq(DocLink.Picture("file:///D:/shots/a.png", null), @"D:\shots\a.png", "a file: address on this PC");
        Check.True(DocLink.Picture("https://example.com/a.png", local) is null, "the web: never fetched");
        Check.True(DocLink.Picture("a.png", null) is null, "relative with no folder: nothing");
        foreach (string unc in new[] { @"\\host\share\a.png", "//host/share/a.png", "file://host/share/a.png", "%5C%5Chost%5Cshare%5Ca.png", "%2F%2Fhost/share/a.png", @"\\?\UNC\host\share\a.png", @"\\.\UNC\host\share\a.png" })
        {
            Check.True(DocLink.Picture(unc, local) is null, "a network picture from a local document: refused: " + unc);
            Check.True(DocLink.Picture(unc, null) is null, "and from a note: " + unc);
        }
        string share = @"\\host\share\docs";
        Check.Eq(DocLink.Picture("pics/a.png", share), @"\\host\share\docs\pics\a.png", "a document on a share: pictures in its folder and below");
        Check.True(string.Equals(DocLink.Picture(@"\\HOST\share\docs\a.png", share), @"\\host\share\docs\a.png", StringComparison.OrdinalIgnoreCase), "written in full, any case");
        Check.True(DocLink.Picture("../other/a.png", share) is null, "above its folder: refused");
        Check.True(DocLink.Picture(@"\\host\share\docs2\a.png", share) is null, "a folder that only starts the same: refused");
        Check.True(DocLink.Picture(@"\\evil\share\a.png", share) is null, "another computer: refused");
        Check.True(DocLink.Picture("%5C%5Cevil%5Cs%5Ca.png", share) is null, "another computer, escaped: refused");
        Check.True(DocLink.IsNetwork(@"\\host\s") && DocLink.IsNetwork("//host/s") && !DocLink.IsNetwork(@"C:\a") && !DocLink.IsNetwork(@"\a"), "network paths");
    }

    public static void OwnSaveIsNotAnOutsideChange()
    {
        string dir = Folder();
        try
        {
            string path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "one\r\n", new UTF8Encoding(false));
            using var f = new DocFile(path);
            f.Read();
            int touched = 0;
            f.Touched += () => Interlocked.Increment(ref touched);
            f.Watch();
            Check.Eq(f.Save("one\ntwo\n", false).result, DocSaved.Saved, "saved");
            Check.Eq(File.ReadAllText(path), "one\r\ntwo\r\n", "CRLF kept on disk");
            Check.Eq(Directory.GetFiles(dir).Length, 1, "no temporary file left");
            for (int i = 0; i < 30 && Volatile.Read(ref touched) == 0; i++) Thread.Sleep(50);
            Check.Eq(f.Peek().state, DocState.Same, "after its own save the disk is as known (the watcher's event changes nothing)");
            Check.Eq(f.Save("one\ntwo\n", false).result, DocSaved.Saved, "the same text again: nothing to write");
        }
        finally
        {
            Done(dir);
        }
    }

    public static void OutsideChangeIsSeenAndNeverWrittenOver()
    {
        string dir = Folder();
        try
        {
            string path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "mine\n", new UTF8Encoding(false));
            using var f = new DocFile(path);
            f.Read();
            using var seen = new ManualResetEventSlim();
            f.Touched += seen.Set;
            f.Watch();
            File.WriteAllText(path, "theirs\n", new UTF8Encoding(false));
            Check.True(seen.Wait(3000), "the watcher tells");
            var (state, now) = f.Peek();
            Check.Eq(state, DocState.Changed, "another program's change");
            Check.Eq(now!.Text, "theirs\n", "with what it wrote");
            // edits waiting here meanwhile: an autosave writes nothing over it
            Check.Eq(f.Save("mine, edited\n", false).result, DocSaved.Conflict, "the save stops");
            Check.Eq(File.ReadAllText(path), "theirs\n", "their text stays on disk");
            Check.Eq(f.Save("mine, edited\n", true).result, DocSaved.Saved, "고친 내용 저장 writes anyway");
            Check.Eq(File.ReadAllText(path), "mine, edited\n", "and it is on disk");
            // 다시 읽기 instead: taken as known, then saves go on
            File.WriteAllText(path, "theirs again\n", new UTF8Encoding(false));
            var (_, again) = f.Peek();
            f.Adopt(again!);
            Check.Eq(f.Save("edited on theirs\n", false).result, DocSaved.Saved, "after taking their text in, the next save goes");
            File.Delete(path);
            Check.Eq(f.Peek().state, DocState.Gone, "deleted: gone");
            Check.Eq(f.Save("still here\n", false).result, DocSaved.Gone, "an autosave doesn't make it again");
            Check.Eq(f.Save("still here\n", true).result, DocSaved.Saved, "고친 내용 저장 does");
            Check.Eq(File.ReadAllText(path), "still here\n", "made again");
        }
        finally
        {
            Done(dir);
        }
    }

    public static void HeldFileWaitsAndReadOnlyFileSays()
    {
        string dir = Folder();
        try
        {
            string path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "a\n", new UTF8Encoding(false));
            using var f = new DocFile(path);
            f.Read();
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check.Eq(f.Save("b\n", false).result, DocSaved.Busy, "held by another program: tried again later");
            Check.Eq(f.Save("b\n", false).result, DocSaved.Saved, "let go: saved");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                Check.True(f.Read().ReadOnly is { Length: > 0 }, "the read-only mark: view only");
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
        finally
        {
            Done(dir);
        }
    }

    public static void LinksGoWhereTheDocumentMeans()
    {
        string folder = @"C:\docs\notes";
        Check.Eq(DocLink.Resolve("other.md#part", folder), (DocLink.Kind.Doc, @"C:\docs\notes\other.md"), "a Markdown file beside it, its #part left off");
        Check.Eq(DocLink.Resolve("../shared/my%20plan.MD", folder), (DocLink.Kind.Doc, @"C:\docs\shared\my plan.MD"), "up a folder, %20 decoded, any case of .md");
        Check.Eq(DocLink.Resolve("pics/map.png", folder), (DocLink.Kind.File, @"C:\docs\notes\pics\map.png"), "another file: shown in its folder");
        Check.Eq(DocLink.Resolve(@"D:\elsewhere\read me.md", folder), (DocLink.Kind.Doc, @"D:\elsewhere\read me.md"), "an absolute path");
        Check.Eq(DocLink.Resolve("file:///C:/abs/n%20a.md", folder), (DocLink.Kind.Doc, @"C:\abs\n a.md"), "a file: address");
        Check.Eq(DocLink.Resolve("https://example.com/a.md", folder), (DocLink.Kind.Web, "https://example.com/a.md"), "the web, even ending in .md");
        Check.Eq(DocLink.Resolve("mailto:me@example.com", folder), (DocLink.Kind.Web, "mailto:me@example.com"), "mail: the app Windows has for it");
        Check.Eq(DocLink.Resolve("ms-msdt:/id PCWDiagnostic", folder).kind, DocLink.Kind.None, "any other address: nowhere (it would start a program)");
        Check.Eq(DocLink.Resolve("search-ms:query=x&crumb=location:\\\\host\\share", folder).kind, DocLink.Kind.None, "search-ms: nowhere");
        Check.Eq(DocLink.Resolve("#heading", folder).kind, DocLink.Kind.None, "a heading in the same document: nowhere");
        Check.Eq(DocLink.Resolve("other.md", null).kind, DocLink.Kind.None, "relative with no folder: nowhere");
    }
}
