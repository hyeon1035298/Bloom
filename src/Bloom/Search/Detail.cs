using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace Bloom.Search;

/// What the right-hand pane shows for a row (file search, clipboard history): one big preview
/// and a few lines of facts under it.
internal sealed class Detail
{
    public string? Image; // a picture, fitted
    public string? Video; // played muted, looping; click pauses
    public string? Text; // text (clipboard text, the start of a text file)
    public string? Shell; // anything else: the shell's large thumbnail or icon
    public bool ShellIcon; // the icon, never a thumbnail (folders)
    public IconSpec? Icon; // when there is nothing to show (a glyph tile)
    public Brush? Fill; // a colour or gradient filling the whole preview
    public string? Symbol; // an emoji or symbol, drawn big
    public bool SymbolIsEmoji;
    public readonly List<(string label, string value)> Meta = new();

    public Detail Add(string label, string? value)
    {
        if (!string.IsNullOrEmpty(value)) Meta.Add((label, value));
        return this;
    }

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".avif", ".jxr" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp" };
    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".yaml", ".yml", ".toml", ".cs", ".js", ".ts", ".tsx", ".jsx",
        ".py", ".rs", ".go", ".java", ".c", ".h", ".cpp", ".hpp", ".css", ".scss", ".html", ".htm", ".ps1", ".bat", ".cmd", ".sh", ".sql", ".shader", ".hlsl",
        ".cginc", ".asmdef", ".meta", ".unity", ".prefab", ".mat", ".anim", ".controller", ".gitignore", ".editorconfig", ".srt", ".vtt",
    };

    public static bool IsPicture(string path) => Pictures.Contains(Path.GetExtension(path));

    public static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));

    /// A file or folder: what it is, where, how big, when it changed.
    public static Detail ForPath(string path, bool folder, long size = -1, DateTime? modified = null)
    {
        var d = new Detail();
        string ext = Path.GetExtension(path);
        if (!folder && Pictures.Contains(ext) && ext is not ".heic" and not ".avif") d.Image = path;
        else if (!folder && Videos.Contains(ext)) d.Video = path;
        else if (!folder && Texts.Contains(ext)) d.Text = Head(path);
        d.Shell = path;
        d.ShellIcon = folder;
        string name = Path.GetFileName(path.TrimEnd('\\'));
        d.Add(Tr("이름", "Name"), name.Length > 0 ? name : path);
        d.Add(Tr("위치", "Position"), Path.GetDirectoryName(path.TrimEnd('\\')) is { } dir ? Bloom.App.Engine.Short(dir) : null);
        d.Add(Tr("종류", "Type"), folder ? Tr("폴더", "Folder") : ext.Length > 1 ? Tr($"{ext[1..].ToUpperInvariant()} 파일", $"{ext[1..].ToUpperInvariant()} file") : Tr("파일", "File"));
        try
        {
            if (folder)
            {
                var di = new DirectoryInfo(path);
                modified ??= di.Exists ? di.LastWriteTime : null;
            }
            else
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    if (size < 0) size = fi.Length;
                    modified ??= fi.LastWriteTime;
                }
            }
        }
        catch
        {
            /* no access: what Everything said stays */
        }
        if (!folder && size >= 0) d.Add(Tr("크기", "Size"), Bloom.App.Engine.Size(size));
        if (modified is { } m) d.Add(Tr("수정한 날짜", "Date modified"), m.ToString("yyyy. M. d. HH:mm"));
        return d;
    }

    /// The first 6 KB of a text file (UTF-8, or the system code page when that fails).
    private static string? Head(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[6 * 1024];
            int n = fs.Read(buf, 0, buf.Length);
            if (Array.IndexOf(buf, (byte)0, 0, n) >= 0) return null; // binary after all
            return new System.Text.UTF8Encoding(false, false).GetString(buf, 0, n);
        }
        catch
        {
            return null;
        }
    }
}
