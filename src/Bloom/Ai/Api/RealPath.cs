using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// Where a path really leads, for the API chats' file tools (ApiToolbox). A path is first classed from its text alone
/// (Kind): a network path or a device is asked about before any file system call touches it. A file is then opened
/// once (OpenExisting, OpenForWrite) and judged by what the handle says — its final path, its link count (IsInside) —
/// and read or written through that handle, so what was checked is what is used. A junction, symbolic link, mount point
/// or hard link inside the chat's folder could lead anywhere while the path still reads as inside it: a link on the way
/// is found first without following it (Trace) and asked about before it is, wherever it leads.
internal static class RealPath
{
    private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"];

    /// The class of a full path from its text (see PathKind). A mapped network drive is told by the drive type, which
    /// Windows keeps and does not ask the network for.
    public static PathKind Kind(string path)
    {
        string p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) return PathKind.Device;
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // a share is a network path only when every part of it is a plain name: the server, the share and what is below
            string[] parts = p[2..].Split('\\');
            return parts[0].Length == 0 || parts.Any(IsDeviceName) ? PathKind.Device : PathKind.Network;
        }
        if (p.Length < 3 || !char.IsAsciiLetter(p[0]) || p[1] != ':' || p[2] != '\\') return PathKind.Device;
        if (p[3..].Split('\\').Any(IsDeviceName)) return PathKind.Device;
        return GetDriveTypeW(p[..3]) == DriveRemote ? PathKind.Network : PathKind.Local;
    }

    /// A path segment Windows takes for a device (NUL, COM1, NUL.txt…), a stream (name:stream) or another name than it
    /// reads (trailing dots and spaces are dropped by Windows).
    private static bool IsDeviceName(string segment)
    {
        if (segment.Contains(':', StringComparison.Ordinal) || segment.EndsWith('.') || segment.EndsWith(' ')) return true;
        string name = segment.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return DeviceNames.Contains(name) || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
            && (char.IsAsciiDigit(name[3]) || name[3] is '\u00B9' or '\u00B2' or '\u00B3'));
    }

    /// `path` is lexically inside `root` (both full paths), no reparse point lies on the way from `root` down to it (root
    /// itself included; a cloud placeholder counts too), and the final path Windows resolves for it — or for its nearest
    /// existing ancestor, for a path not made yet — is inside root's own final path. Anything unknown (a step that can't
    /// be read) counts as outside; so does a path that is not on a local drive, with no file system call made. Above root
    /// lies Bloom's own data folder, which the model has no hand in, so it is only compared through the final paths.
    public static bool Within(string root, string path)
    {
        if (Kind(root) != PathKind.Local || Kind(path) != PathKind.Local || !Lexically(root, path)) return false;
        string existing = root;
        if (Step(root) is not { } top || top.Reparse) return false;
        string relative = Path.GetRelativePath(root, path);
        if (relative != ".")
        {
            string at = root;
            foreach (string part in relative.Split(Path.DirectorySeparatorChar))
            {
                at = Path.Combine(at, part);
                if (Step(at) is not { } step) return false;
                if (!step.Exists) break;
                if (step.Reparse) return false;
                existing = at;
            }
        }
        return Final(root) is { } realRoot && Final(existing) is { } real && Lexically(realRoot, real);
    }

    /// Looks down `full` (a local path) for a link — a symbolic link, junction, mount point or other reparse point — step
    /// by step from `above` (a folder the model has no hand in, when `full` is inside it; else the drive's root), each
    /// step opened as itself and never followed (FILE_FLAG_OPEN_REPARSE_POINT), and stops at the first one: whatever
    /// lies beyond it is not touched. A step that can't be read counts as a link whose target is not told. A path that
    /// is not local (Kind) is not looked into at all: it is asked about on its own.
    public static LinkTrace Trace(string? above, string full, TraceHold? hold = null) => Trace(above, full, hold, 0);

    private static LinkTrace Trace(string? above, string full, TraceHold? hold, int depth)
    {
        if (Kind(full) != PathKind.Local) return LinkTrace.None;
        bool inside = above is not null && Lexically(above, full);
        string start = inside ? above! : Path.GetPathRoot(full) ?? full;
        string relative = Path.GetRelativePath(start, full);
        string[] parts = relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar);
        string at = start;
        // the folder `above` is looked at itself first (index -1): a chat folder that is a junction leads anywhere
        for (int i = inside ? -1 : 0; i < parts.Length; i++)
        {
            if (i >= 0) at = Path.Combine(at, parts[i]);
            if (Probe(at, hold) is not { } step) return new LinkTrace(at, null, false);
            if (!step.Exists) break; // nothing there: nothing below it can be a link
            if (step.Reparse) return Leading(at, step.IsDirectory, step.Identity, parts[(i + 1)..], hold, depth);
        }
        return LinkTrace.None;
    }

    /// The most links one path may go through before it is not followed (a loop, or a path nobody can show whole).
    private const int MaxLinkChain = 8;

    /// The link `link` (found as itself, with the identity its handle told) and where it leads, as the link itself says
    /// (its reparse data; the target is not opened): where the whole path `rest` below it then lies (Expected), and the
    /// links on that path in turn (Then). A target that can't be told, or can't be named by a path (a volume), has no
    /// Expected; one past MaxLinkChain hops is remote and has none either.
    private static LinkTrace Leading(string link, bool isDirectory, string identity, string[] rest, TraceHold? hold, int depth)
    {
        LinkTrace trace;
        try
        {
            FileSystemInfo info = isDirectory ? new DirectoryInfo(link) : new FileInfo(link);
            trace = Linking(link, info.LinkTarget);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            trace = new LinkTrace(link, null, false); // can't be read: a link whose target is not told
        }
        trace = trace with { Identity = identity };
        if (trace.Target is null || trace.Target.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)) return trace;
        trace = trace with { Expected = rest.Length == 0 ? trace.Target : Path.Combine(trace.Target, Path.Combine(rest)) };
        if (trace.Remote) return trace;
        if (depth >= MaxLinkChain) return trace with { Expected = null, Remote = true };
        var next = Trace(null, trace.Expected!, hold, depth + 1);
        return next.Any ? trace with { Then = next } : trace;
    }

    /// The trace for `link` that leads to `target` as written in it (null: not a symbolic link or junction): a target
    /// that is a UNC or device path, or not on a local drive, is remote; a relative one is taken from the link's folder.
    internal static LinkTrace Linking(string link, string? target)
    {
        if (target is null) return new LinkTrace(link, null, false);
        string t = target.Replace('/', '\\');
        if (t.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase)) t = @"\\" + t[8..];
        else if (t.StartsWith(@"\??\", StringComparison.Ordinal)) t = t[4..];
        if (t.StartsWith(@"\\", StringComparison.Ordinal) || t.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)) return new LinkTrace(link, t, true); // UNC, device or volume: not looked at
        try
        {
            if (!(t.Length >= 3 && char.IsAsciiLetter(t[0]) && t[1] == ':' && t[2] == '\\'))
                t = Path.GetFullPath(t, Path.GetDirectoryName(link) ?? "");
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LinkTrace(link, t, true);
        }
        return new LinkTrace(link, t, Kind(t) != PathKind.Local);
    }

    /// `file`, opened, is really inside `root`: root itself is no link, the file's final path is inside root's final path,
    /// and a file has one name only (another name of it may lie outside).
    public static bool IsInside(string root, OpenedFile file) =>
        Kind(root) == PathKind.Local && Step(root) is { Reparse: false } && Final(root) is { } realRoot && Lexically(realRoot, file.Final) && (file.IsDirectory || file.Links <= 1);

    /// The final path of what `path` names, its own or its nearest existing ancestor's plus the rest; null when it can't
    /// be told. Shown on the approval card when it differs from the path asked for. A local path only: nothing else is
    /// looked at before the user has said yes.
    public static string? Of(string path)
    {
        if (Kind(path) != PathKind.Local) return null;
        string at = path, rest = "";
        while (Step(at) is { Exists: false })
        {
            if (Path.GetDirectoryName(at) is not { } parent) return null;
            rest = Path.Combine(Path.GetFileName(at), rest);
            at = parent;
        }
        return Final(at) is { } real ? Path.TrimEndingDirectorySeparator(Path.Combine(real, rest)) : null;
    }

    /// The two full paths are the same place.
    public static bool Same(string a, string b) => Path.TrimEndingDirectorySeparator(a).Equals(Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    public static bool Lexically(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + "\\", StringComparison.OrdinalIgnoreCase);

    /// Opens an existing file or folder (links followed; a device or pipe is refused) → it with its handle's facts, or
    /// null with the Windows error (2/3: not there). `write`: for reading and writing, the file only.
    public static OpenedFile? OpenExisting(string path, bool write, out int error)
    {
        var handle = CreateFileW(path, write ? GenericRead | GenericWrite : GenericRead, ShareAll, IntPtr.Zero, OpenExistingFile, write ? 0 : BackupSemantics, IntPtr.Zero);
        return Describe(handle, created: false, out error);
    }

    /// Opens a file for reading and writing, making it when it is not there, without emptying it. The file is first made
    /// exclusively and without following a link (CREATE_NEW + FILE_FLAG_OPEN_REPARSE_POINT fails on any name that exists,
    /// a link to nowhere included, so nothing is ever made at a link's target). A file that is there is looked at without
    /// following first: a link is refused (Diverged), anything else opened. Nothing is truncated here: the caller checks
    /// what was opened (OpenedFile.Final, Links) and only then writes. `path` leads through folders already held (CreateIn).
    public static OpenedFile? OpenForWrite(string path, out int error)
    {
        var handle = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, CreateNewFile, OpenReparsePoint, IntPtr.Zero);
        if (!handle.IsInvalid) return Describe(handle, created: true, out error);
        int why = Marshal.GetLastWin32Error();
        handle.Dispose();
        var there = Probe(path, null); // what the name is, as itself
        if (there is { Exists: true, Reparse: true } || (why is ErrorFileExists or ErrorAlreadyExists && there is not { Exists: true }))
        {
            error = Diverged; // a link, or gone since: not what was judged
            return null;
        }
        if (why is ErrorFileExists or ErrorAlreadyExists) return OpenExisting(path, true, out error);
        error = why;
        return null;
    }

    private static OpenedFile? Describe(SafeFileHandle handle, bool created, out int error)
    {
        error = Marshal.GetLastWin32Error();
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        if (GetFileType(handle) != FileTypeDisk || !GetFileInformationByHandle(handle, out var info) || FinalOf(handle) is not { } final)
        {
            handle.Dispose();
            error = ErrorInvalidName;
            return null;
        }
        error = 0;
        return new OpenedFile(handle, final, (info.Attributes & DirectoryAttribute) != 0, info.Links, created);
    }

    /// Told in place of a Windows error: what was opened is not where it was said to be.
    public const int Diverged = -1;

    /// Makes (or opens, without emptying) the file `full` for writing, and the folders above it that are missing, only
    /// through folders already opened and checked: the nearest folder that exists is opened and held (shared for reading and
    /// writing, not for deleting or renaming, so neither it nor anything above it can be swapped while it is held), must
    /// be where `really` (the card's account of where the path leads; null: not told) says, and each folder made below it
    /// is opened and held the same way before the next thing is made in it. Nothing is made before the folder it goes into
    /// has been checked, and what is made is judged by its handle again. The file's `Parent` is the folder held.
    /// Null with the Windows error, or Diverged.
    public static OpenedFile? CreateIn(string full, string? really, out int error)
    {
        error = 0;
        var missing = new List<string>(); // deepest first
        string at = Path.GetDirectoryName(full) ?? "";
        OpenedFile? parent;
        while ((parent = OpenHeld(at, out error)) is null)
        {
            if (error is not (ErrorFileNotFound or ErrorPathNotFound) || Path.GetDirectoryName(at) is not { } up) return null;
            missing.Add(Path.GetFileName(at));
            at = up;
        }
        try
        {
            if (!parent.IsDirectory)
            {
                error = ErrorInvalidName;
                return null;
            }
            if (really is not null)
            {
                string expected = really;
                for (int i = 0; i <= missing.Count; i++) expected = Path.GetDirectoryName(expected) ?? "";
                if (!Same(parent.Final, expected))
                {
                    error = Diverged;
                    return null;
                }
            }
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                string made = Path.Combine(parent.Final, missing[i]);
                Directory.CreateDirectory(made);
                var child = OpenHeld(made, out error);
                if (child is null) return null;
                parent.Dispose();
                parent = child;
                if (!Same(child.Final, made) || !child.IsDirectory)
                {
                    error = Diverged;
                    return null;
                }
            }
            string path = Path.Combine(parent.Final, Path.GetFileName(full));
            var file = OpenForWrite(path, out error);
            if (file is null) return null;
            if (!Same(file.Final, path))
            {
                file.Dispose();
                error = Diverged;
                return null;
            }
            file.Parent = parent;
            parent = null; // the file holds it now
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = ErrorAccessDenied;
            return null;
        }
        finally
        {
            parent?.Dispose();
        }
    }

    /// A folder opened to be held: others may read and write in it, not delete or rename it.
    private static OpenedFile? OpenHeld(string path, out int error) =>
        Describe(CreateFileW(path, GenericRead, ShareReadWrite, IntPtr.Zero, OpenExistingFile, BackupSemantics, IntPtr.Zero), created: false, out error);

    /// Writes `bytes` as the whole of `path`, a file that is really inside `root`: checked before it is made (no link on
    /// the way) and again by the handle (its final path, one name), emptied only after both. False when anything fails.
    public static bool WriteInside(string root, string path, byte[] bytes)
    {
        if (!Within(root, path)) return false;
        try
        {
            if (Path.GetDirectoryName(path) is { } dir) Directory.CreateDirectory(dir);
            using var file = OpenForWrite(path, out _);
            if (file is null || file.IsDirectory || !IsInside(root, file)) return false;
            using var stream = new FileStream(file.Handle, FileAccess.Write, 1);
            stream.SetLength(0);
            stream.Write(bytes);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// One step of a path: whether it exists and is a reparse point (its own attributes, the link not followed); null
    /// when that can't be read.
    private static (bool Exists, bool Reparse)? Step(string path)
    {
        try
        {
            return (true, File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (false, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// The entries of the opened folder, read through its handle (never by opening its final path again, which a swap could
    /// send elsewhere), at most `max`: (name, is a folder, size). Throws IOException when the handle can't be read.
    public static List<(string Name, bool IsDirectory, long Size)> Entries(OpenedFile directory, int max)
    {
        var list = new List<(string, bool, long)>();
        var buffer = Marshal.AllocHGlobal(EntryBuffer);
        try
        {
            int cls = FileIdBothDirectoryRestartInfo;
            while (list.Count < max && GetFileInformationByHandleEx(directory.Handle, cls, buffer, EntryBuffer))
            {
                cls = FileIdBothDirectoryInfo;
                int at = 0;
                while (true)
                {
                    uint attributes = (uint)Marshal.ReadInt32(buffer, at + 56);
                    string name = Marshal.PtrToStringUni(buffer + at + 104, Marshal.ReadInt32(buffer, at + 60) / 2);
                    if (name is not ("." or ".."))
                    {
                        bool folder = (attributes & DirectoryAttribute) != 0;
                        list.Add((name, folder, folder ? 0 : Marshal.ReadInt64(buffer, at + 40)));
                        if (list.Count >= max) break;
                    }
                    int next = Marshal.ReadInt32(buffer, at);
                    if (next == 0) break;
                    at += next;
                }
            }
            if (list.Count < max && Marshal.GetLastWin32Error() != ErrorNoMoreFiles) throw new IOException("The folder could not be read.");
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int EntryBuffer = 64 * 1024, FileIdBothDirectoryInfo = 10, FileIdBothDirectoryRestartInfo = 11, ErrorNoMoreFiles = 18;

    /// One step of a path as itself: whether it is there, a reparse point, a folder (the link not followed); null when it
    /// can't be read (not for being absent).
    private static (bool Exists, bool Reparse, bool IsDirectory, string Identity)? Probe(string path, TraceHold? hold)
    {
        // with a hold, opened so that the link can't be deleted or renamed while it is kept: for reading (a handle for
        // attributes alone takes no part in sharing) and not shared for deleting; where that is refused, as itself only
        var handle = hold is null ? CreateFileW(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExistingFile, BackupSemantics | OpenReparsePoint, IntPtr.Zero)
            : CreateFileW(path, GenericRead, ShareReadWrite, IntPtr.Zero, OpenExistingFile, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid && hold is not null && Marshal.GetLastWin32Error() is not (ErrorFileNotFound or ErrorPathNotFound))
            handle = CreateFileW(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExistingFile, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        bool kept = false;
        try
        {
            if (handle.IsInvalid) return Marshal.GetLastWin32Error() is ErrorFileNotFound or ErrorPathNotFound ? (false, false, false, "") : null;
            if (!GetFileInformationByHandle(handle, out var info)) return null;
            bool reparse = (info.Attributes & ReparseAttribute) != 0;
            if (reparse && hold is not null)
            {
                hold.Add(handle);
                kept = true;
            }
            return (true, reparse, (info.Attributes & DirectoryAttribute) != 0, $"{info.VolumeSerial:x8}:{info.IndexHigh:x8}{info.IndexLow:x8}");
        }
        finally
        {
            if (!kept) handle.Dispose();
        }
    }

    private const uint ReadAttributes = 0x80, ShareAll = 0x7, ShareReadWrite = 0x3, OpenExistingFile = 3, CreateNewFile = 1, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000; // BackupSemantics opens folders too; the last opens a link itself
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, DirectoryAttribute = 0x10, ReparseAttribute = 0x400;
    private const uint FileTypeDisk = 1, DriveRemote = 4;
    private const int ErrorAlreadyExists = 183, ErrorFileExists = 80, ErrorInvalidName = 123, ErrorFileNotFound = 2, ErrorPathNotFound = 3, ErrorAccessDenied = 5;
    private const int MaxPath = 32_768;

    /// GetFinalPathNameByHandle on the opened file or folder (links followed), without its \\?\ prefix.
    internal static string? Final(string path)
    {
        using var handle = CreateFileW(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExistingFile, BackupSemantics, IntPtr.Zero);
        return handle.IsInvalid ? null : FinalOf(handle);
    }

    private static string? FinalOf(SafeFileHandle handle)
    {
        var buffer = new char[MaxPath];
        uint n = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (n == 0 || n >= buffer.Length) return null;
        string real = new(buffer, 0, (int)n);
        return real.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + real[8..] : real.StartsWith(@"\\?\", StringComparison.Ordinal) ? real[4..] : real;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public uint Attributes;
        public uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh; // three FILETIMEs
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint size, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int information, IntPtr buffer, int size);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);
}
