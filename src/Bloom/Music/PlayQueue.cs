using System;
using System.Collections.Generic;

namespace Bloom.Music;

/// Bloom's own play queue (the direct way): the songs, and which one is on.
internal sealed class PlayQueue
{
    public readonly List<Track> Items = new();

    /// The song that is on (or was last on); -1 before the first.
    public int Index { get; set; } = -1;

    public Track? Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    /// At the end of the queue.
    public int Add(Track t)
    {
        Items.Add(t);
        return Items.Count - 1;
    }

    /// Right after the song that is on (a "play now" keeps the queue around it).
    public int InsertNext(Track t)
    {
        int at = Math.Min(Items.Count, Index + 1);
        Items.Insert(at, t);
        return at;
    }

    /// Takes a song out; the one that is on stays (false).
    public bool RemoveAt(int i)
    {
        if (i < 0 || i >= Items.Count || i == Index) return false;
        Items.RemoveAt(i);
        if (i < Index) Index--;
        return true;
    }

    public void Clear()
    {
        Items.Clear();
        Index = -1;
    }

    /// The nearest song in `step` direction (+1 next, -1 before) that `reject` lets through; every one passed over goes
    /// to `skipped` with its reason. -1: none left that way.
    public int Find(int step, Func<Track, string?> reject, List<(Track track, string reason)>? skipped = null)
    {
        for (int i = Index + step; i >= 0 && i < Items.Count; i += step)
        {
            if (reject(Items[i]) is not { } why) return i;
            skipped?.Add((Items[i], why));
        }
        return -1;
    }
}
