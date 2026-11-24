using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bloom.Native;

/// Windows' own SQLite (winsqlite3.dll, no package), for reading another program's database: Windows' notification
/// store (WindowsToasts) and T3 Code's list of threads (Ai.T3Threads). Read only, never written.
internal static partial class SystemSqlite
{
    /// `read` with the database at `file` open read only, waiting up to half a second while it is busy; false when it
    /// can't be opened.
    public static bool ReadOnly(string file, Action<IntPtr> read)
    {
        if (sqlite3_open_v2(file, out IntPtr db, 1 /* READONLY */, IntPtr.Zero) != 0)
        {
            _ = sqlite3_close(db);
            return false;
        }
        try
        {
            _ = sqlite3_busy_timeout(db, 500);
            read(db);
            return true;
        }
        finally
        {
            _ = sqlite3_close(db);
        }
    }

    /// Each row of `sql`; InvalidDataException with SQLite's words when it doesn't prepare.
    public static void Query(IntPtr db, string sql, Action<IntPtr> row)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out IntPtr s, IntPtr.Zero) != 0) throw new InvalidDataException("sqlite: " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
        try
        {
            while (sqlite3_step(s) == 100 /* ROW */) row(s);
        }
        finally
        {
            _ = sqlite3_finalize(s);
        }
    }

    public static long Int64(IntPtr row, int col) => sqlite3_column_int64(row, col);

    public static string? Text(IntPtr row, int col) => sqlite3_column_text(row, col) is var p && p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : null;

    /// A blob column read as UTF-8 text ("" when empty).
    public static string BlobText(IntPtr row, int col)
    {
        int size = sqlite3_column_bytes(row, col);
        IntPtr blob = sqlite3_column_blob(row, col);
        return size > 0 && blob != IntPtr.Zero ? Marshal.PtrToStringUTF8(blob, size) : "";
    }

    [LibraryImport("winsqlite3.dll", StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_open_v2(string file, out IntPtr db, int flags, IntPtr vfs);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_close(IntPtr db);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_busy_timeout(IntPtr db, int ms);
    [LibraryImport("winsqlite3.dll", StringMarshalling = StringMarshalling.Utf8)] private static partial int sqlite3_prepare_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_step(IntPtr stmt);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_finalize(IntPtr stmt);
    [LibraryImport("winsqlite3.dll")] private static partial long sqlite3_column_int64(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_column_blob(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_column_bytes(IntPtr stmt, int col);
    [LibraryImport("winsqlite3.dll")] private static partial IntPtr sqlite3_errmsg(IntPtr db);
}
