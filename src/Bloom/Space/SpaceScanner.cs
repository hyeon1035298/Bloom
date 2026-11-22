using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;

namespace Bloom.Space;

/// A file in the map: a name, its folder and its size — nothing else, there can be millions.
internal class SpaceNode
{
    public readonly string Name;
    public readonly SpaceDir? Parent;
    public long Size; // bytes; for a folder, everything under it (grows while the scan runs: read with Volatile)

    public SpaceNode(string name, SpaceDir? parent, long size)
    {
        Name = name;
        Parent = parent;
        Size = size;
    }

    public virtual bool IsDir => false;

    public virtual long FileCount => 1;

    public string Path => Parent is null ? Name : System.IO.Path.Combine(Parent.Path, Name);
}

/// A folder: its children (folders, and its largest files), and the small files folded into one block.
internal sealed class SpaceDir : SpaceNode
{
    public long Files; // files counted under it
    public int Hidden; // small files folded into RestSize
    public long RestSize;
    public bool Done; // its own entries are all known
    public bool Denied; // could not be read
    private SpaceNode[] kids = Array.Empty<SpaceNode>();
    private int count;

    public SpaceDir(string name, SpaceDir? parent) : base(name, parent, 0)
    {
    }

    public override bool IsDir => true;

    public override long FileCount => Volatile.Read(ref Files);

    /// A copy, largest first (the scan may be adding to it).
    public List<SpaceNode> Children()
    {
        var copy = Copy();
        Array.Sort(copy, (a, b) => Volatile.Read(ref b.Size).CompareTo(Volatile.Read(ref a.Size)));
        return new List<SpaceNode>(copy);
    }

    /// A copy in no particular order, safe to read on any thread while the scan adds to the folder.
    private SpaceNode[] Copy()
    {
        lock (this)
        {
            var copy = new SpaceNode[count];
            Array.Copy(kids, copy, count);
            return copy;
        }
    }

    /// The `take` largest files under this folder, largest first, looking at no more than `budget` entries (a whole
    /// drive has millions). Runs off the UI thread: each folder is read through its own locked copy.
    public List<SpaceNode> Largest(int take, int budget)
    {
        var top = new PriorityQueue<SpaceNode, long>(take + 1); // smallest of the kept ones on top
        var stack = new Stack<SpaceDir>();
        stack.Push(this);
        int seen = 0;
        while (stack.Count > 0 && seen < budget)
            foreach (var c in stack.Pop().Copy())
            {
                seen++;
                if (c is SpaceDir cd) stack.Push(cd);
                else
                {
                    long size = Volatile.Read(ref c.Size);
                    if (top.Count < take) top.Enqueue(c, size);
                    else if (size > top.Peek().Size) top.EnqueueDequeue(c, size);
                }
            }
        var list = new List<SpaceNode>(top.Count);
        while (top.Count > 0) list.Add(top.Dequeue());
        list.Reverse();
        return list;
    }

    public int ChildCount
    {
        get
        {
            lock (this) return count;
        }
    }

    public void Add(SpaceNode n)
    {
        lock (this)
        {
            if (count == kids.Length) Array.Resize(ref kids, Math.Max(4, count * 2));
            kids[count++] = n;
        }
    }

    public void Remove(SpaceNode n)
    {
        lock (this)
        {
            int i = Array.IndexOf(kids, n, 0, count);
            if (i < 0) return;
            Array.Copy(kids, i + 1, kids, i, count - i - 1);
            kids[--count] = null!;
        }
    }

    /// Exact size once listing is done (the arrays grew in doubling steps).
    public void Trim()
    {
        lock (this)
            if (kids.Length != count) Array.Resize(ref kids, count);
    }

    /// Adds to this folder and every folder above it.
    public void Grow(long bytes, long files)
    {
        for (var n = this; n is not null; n = n.Parent)
        {
            Interlocked.Add(ref n.Size, bytes);
            Interlocked.Add(ref n.Files, files);
        }
    }
}

/// Lists a folder tree on several threads (the OS's own fast enumeration, no FileInfo objects).
/// Junctions and symbolic links are not followed (no double counting); unreadable folders are
/// marked and skipped. Per folder only its 12 largest files and any file of 32 MB or more stay as
/// blocks — the rest become one "작은 파일" block, so a whole drive stays small in memory.
internal sealed class SpaceScanner
{
    private const int KeepFiles = 12;
    private const long KeepAbove = 32L * 1024 * 1024; // always its own block
    private const long KeepAtLeast = 64 * 1024; // smaller files are only ever part of "작은 파일"

    public readonly SpaceDir Root;
    private readonly CancellationTokenSource cancel = new();
    private int pending;
    public int Folders;
    public int DeniedFolders;
    public DateTime Started = DateTime.UtcNow;
    public DateTime? Finished;

    public SpaceScanner(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        Root = new SpaceDir(path, null);
    }

    public bool Running => Finished is null;

    public void Start()
    {
        var queue = new BlockingCollection<SpaceDir>();
        Interlocked.Increment(ref pending);
        queue.Add(Root);
        int workers = Math.Clamp(Environment.ProcessorCount, 2, 8);
        for (int i = 0; i < workers; i++)
            new Thread(() => Work(queue)) { IsBackground = true, Name = "space", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public void Stop() => cancel.Cancel();

    private void Work(BlockingCollection<SpaceDir> queue)
    {
        var files = new List<Entry>(256); // one per thread, reused
        try
        {
            foreach (var dir in queue.GetConsumingEnumerable(cancel.Token))
            {
                try
                {
                    List(dir, queue, files);
                }
                catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    dir.Denied = true;
                    Interlocked.Increment(ref DeniedFolders);
                }
                catch (Exception e)
                {
                    Core.Log.Error("space " + dir.Name, e);
                }
                dir.Trim();
                dir.Done = true;
                Interlocked.Increment(ref Folders);
                if (Interlocked.Decrement(ref pending) == 0)
                {
                    Finished = DateTime.UtcNow;
                    queue.CompleteAdding();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Finished ??= DateTime.UtcNow;
        }
    }

    private readonly record struct Entry(string Name, long Size);

    private void List(SpaceDir dir, BlockingCollection<SpaceDir> queue, List<Entry> files)
    {
        files.Clear();
        var opts = new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        // folders are made right here; files come back as (name, size)
        var entries = new FileSystemEnumerable<Entry>(dir.Path, (ref FileSystemEntry e) =>
        {
            if (!e.IsDirectory) return new Entry(e.FileName.ToString(), e.Length);
            if ((e.Attributes & FileAttributes.ReparsePoint) == 0) return new Entry(e.FileName.ToString(), -1);
            return new Entry("", -2); // junction / symlink: its target is counted where it really is
        }, opts);
        foreach (var e in entries)
        {
            if (cancel.IsCancellationRequested) return;
            if (e.Size == -1)
            {
                var child = new SpaceDir(e.Name, dir);
                dir.Add(child);
                Interlocked.Increment(ref pending);
                queue.Add(child);
            }
            else if (e.Size >= 0) files.Add(e);
        }
        long total = 0;
        foreach (var f in files) total += f.Size;
        files.Sort((a, b) => b.Size.CompareTo(a.Size));
        int keep = 0;
        while (keep < files.Count && (files[keep].Size >= KeepAbove || (keep < KeepFiles && files[keep].Size >= KeepAtLeast))) keep++;
        if (keep < files.Count && files.Count > 1)
        {
            long rest = 0;
            for (int i = keep; i < files.Count; i++) rest += files[i].Size;
            dir.Hidden = files.Count - keep;
            dir.RestSize = rest;
            for (int i = 0; i < keep; i++) dir.Add(new SpaceNode(files[i].Name, dir, files[i].Size));
        }
        else
            foreach (var f in files) dir.Add(new SpaceNode(f.Name, dir, f.Size)); // one small file alone stays itself
        dir.Grow(total, files.Count);
    }

    /// The drives worth showing: fixed and removable ones that are ready.
    public static List<DriveInfo> Drives() =>
        DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).ToList();
}
