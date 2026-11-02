using System;
using System.IO;
using Bloom.Core;

namespace Bloom.Ai;

/// Reads the whole lines of a log file from an offset as bytes, no string made of any: a line ends at '\n', and the
/// unfinished line at the end is left for the next read (the offset returned stops before it). One buffer serves a
/// whole pass and goes with it; it grows to the longest line up to Max (or the one given), and a longer line is
/// skipped (and logged).
/// The file is opened so that its CLI may go on writing, renaming or deleting it.
internal sealed class LogLines
{
    public const int First = 1 << 20, Max = 64 << 20;

    private readonly int max;
    private byte[] buffer;

    /// `max`: the longest line kept (the tests give a small one).
    public LogLines(int max = Max)
    {
        this.max = max;
        buffer = new byte[Math.Min(First, max)];
    }

    /// What to do with one line (without its '\n') that starts `at` bytes into the file.
    public delegate void Line(ReadOnlySpan<byte> line, long at);

    /// Bytes read from disk by this reader so far.
    public long Bytes { get; private set; }

    /// Reads `path` from `offset` to its last '\n' → the offset just past that '\n' (`offset` itself when there is none).
    /// Throws IOException or UnauthorizedAccessException when the file can't be read.
    public long Read(string path, long offset, Line each)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (offset > 0) file.Position = offset;
        long at = offset; // where buffer[0] lies in the file
        long skipped = -1; // where a line too long to keep began, while it is being passed over
        int filled = 0;
        while (true)
        {
            if (filled == buffer.Length)
            {
                if (buffer.Length < max) Array.Resize(ref buffer, Math.Min(buffer.Length * 2, max));
                else
                {
                    if (skipped < 0)
                    {
                        skipped = at;
                        Log.Write($"token count: a line over {(max >= 1 << 20 ? $"{max >> 20} MB" : $"{max} bytes")} skipped in {Path.GetFileName(path)} at {at}");
                    }
                    at += filled;
                    filled = 0;
                }
            }
            int n = file.Read(buffer, filled, buffer.Length - filled);
            if (n == 0) break;
            Bytes += n;
            int start = 0, from = filled;
            filled += n;
            while (true)
            {
                int end = buffer.AsSpan(from, filled - from).IndexOf((byte)'\n');
                if (end < 0) break;
                end += from;
                if (skipped >= 0) skipped = -1;
                else each(buffer.AsSpan(start, end - start), at + start);
                start = from = end + 1;
            }
            if (start > 0)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
                at += start;
                filled -= start;
            }
        }
        return skipped >= 0 ? skipped : at;
    }
}
