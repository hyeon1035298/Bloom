using System;
using System.Collections.Generic;

namespace Bloom.Music;

/// 반복: none, the whole queue (after the last song the first), or the song that is on (again when it ends).
internal enum Repeat { Off, All, One }

/// Bloom's own play queue (the direct way): the songs, which one is on, and the order they play in — the queue's own, or
/// a shuffled one (무작위: every song once a round, a new round shuffled again).
internal sealed class PlayQueue
{
    public readonly List<Track> Items = new();

    private readonly Random random;

    /// 무작위: the places of Items in the order they play; null: the queue's order.
    private List<int>? order;

    public PlayQueue(Random? random = null) => this.random = random ?? Random.Shared;

    /// The song that is on (or was last on); -1 before the first.
    public int Index { get; set; } = -1;

    public Track? Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    public Repeat Repeat { get; set; }

    /// Switched on: the songs after the one on are shuffled (it stays first); off: the queue's order from the song on.
    public bool Shuffle
    {
        get => order is not null;
        set
        {
            if (value == Shuffle) return;
            order = value ? Shuffled(Index) : null;
        }
    }

    /// Every place in a random order, `first` (when there is one) at the front.
    private List<int> Shuffled(int first)
    {
        var list = new List<int>(Items.Count);
        for (int i = 0; i < Items.Count; i++)
            if (i != first) list.Add(i);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        if (first >= 0 && first < Items.Count) list.Insert(0, first);
        return list;
    }

    /// A new round of 무작위 that begins with `first`.
    public void Reshuffle(int first)
    {
        if (order is not null) order = Shuffled(first);
    }

    /// Where a place of the queue is in the playing order (-1 for none).
    private int Turn(int i) => order is null ? i : order.IndexOf(i);

    private int At(int turn) => order is null ? turn : order[turn];

    /// The song played last when the playing order runs out.
    public int Last => Items.Count == 0 ? -1 : At(Items.Count - 1);

    /// At the end of the queue (무작위: somewhere among the songs still to come).
    public int Add(Track t) => Insert(Items.Count, t);

    /// Right after the song that is on (a "play now" keeps the queue around it); it also plays next with 무작위.
    public int InsertNext(Track t) => Insert(Index + 1, t, true);

    /// At that place (kept within the queue); the song that is on stays the one on. `soon`: with 무작위 it plays right
    /// after the song before it in the queue when that one is still to come (a run of songs handed over together stays
    /// in its order), else right after the song on; without `soon` somewhere among the songs still to come.
    public int Insert(int at, Track t, bool soon = false)
    {
        at = Math.Clamp(at, 0, Items.Count);
        Items.Insert(at, t);
        if (at <= Index) Index++;
        if (order is null) return at;
        for (int k = 0; k < order.Count; k++)
            if (order[k] >= at) order[k]++;
        int now = Index >= 0 ? order.IndexOf(Index) : -1;
        int turn;
        if (soon)
        {
            int before = at > 0 ? order.IndexOf(at - 1) : -1;
            turn = (before > now ? before : now) + 1;
        }
        else turn = random.Next(now + 1, order.Count + 1);
        order.Insert(turn, at);
        return at;
    }

    /// The song with its tags read in place of the one in the queue (that same track, not an equal one); false: it was
    /// taken out meanwhile.
    public bool Replace(Track old, Track now)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (!ReferenceEquals(Items[i], old)) continue;
            Items[i] = now;
            return true;
        }
        return false;
    }

    /// Takes a song out; the one that is on stays (false).
    public bool RemoveAt(int i)
    {
        if (i < 0 || i >= Items.Count || i == Index) return false;
        Items.RemoveAt(i);
        if (i < Index) Index--;
        if (order is not null)
        {
            order.Remove(i);
            for (int k = 0; k < order.Count; k++)
                if (order[k] > i) order[k]--;
        }
        return true;
    }

    public void Clear()
    {
        Items.Clear();
        Index = -1;
        if (order is not null) order = new List<int>();
    }

    /// The nearest song in `step` direction (+1 next, -1 before) in the playing order that `reject` lets through; every
    /// one passed over goes to `skipped` with its reason. 반복 (all or one) goes round the end to the start (`wrapped`),
    /// the song on itself last of all. -1: none left that way.
    public int Find(int step, Func<Track, string?> reject, List<(Track track, string reason)>? skipped, out bool wrapped)
    {
        wrapped = false;
        int n = Items.Count, now = Turn(Index);
        for (int k = 1; k <= n; k++)
        {
            int turn = now + step * k;
            if (turn < 0 || turn >= n)
            {
                if (Repeat == Repeat.Off) break;
                turn = ((turn % n) + n) % n;
                wrapped = true;
            }
            int i = At(turn);
            if (reject(Items[i]) is not { } why) return i;
            skipped?.Add((Items[i], why));
        }
        return -1;
    }

    public int Find(int step, Func<Track, string?> reject, List<(Track track, string reason)>? skipped = null) => Find(step, reject, skipped, out _);
}
