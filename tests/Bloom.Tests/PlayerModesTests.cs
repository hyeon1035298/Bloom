using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Music;

namespace Bloom.Tests;

/// The direct way's 반복 (off, all, one) and 무작위 in the queue: going round, a shuffled round played once each, songs
/// put in or taken out while shuffled, a new round, and the tags' track put in place of the very one read.
internal static class PlayerModesTests
{
    private static Track T(string name) => new(@"C:\Music\" + name, name, "a", 10);

    private static PlayQueue Q(int songs, int seed = 7)
    {
        var q = new PlayQueue(new Random(seed));
        for (int i = 0; i < songs; i++) q.Add(T(i + ".flac"));
        return q;
    }

    private static string? None(Track t) => null;

    private static string? Skip(Track t) => t.Path.EndsWith(".ogg", StringComparison.Ordinal) ? "ogg" : null;

    /// The songs played one after another from the one on until the queue says none (or `max`).
    private static List<int> Walk(PlayQueue q, int max = 100)
    {
        var played = new List<int>();
        for (int i = q.Find(1, None); i >= 0 && played.Count < max; i = q.Find(1, None))
        {
            played.Add(i);
            q.Index = i;
        }
        return played;
    }

    public static void RepeatAllGoesRoundTheEnd()
    {
        var q = Q(3);
        q.Index = 2;
        Check.Eq(q.Find(1, None), -1, "repeat off: nothing after the last");
        q.Repeat = Repeat.All;
        Check.Eq(q.Find(1, None, null, out bool wrapped), 0, "repeat all: the first after the last");
        Check.True(wrapped, "and it says it went round");
        q.Index = 0;
        Check.Eq(q.Find(-1, None), 2, "before the first: the last");
        q.Repeat = Repeat.One;
        q.Index = 2;
        Check.Eq(q.Find(1, None), 0, "repeat one: the buttons still move, round the end too");
    }

    public static void RepeatAllWithOneSongPlaysItAgain()
    {
        var q = Q(1);
        q.Index = 0;
        q.Repeat = Repeat.All;
        Check.Eq(q.Find(1, None), 0, "the same song again");
    }

    public static void RepeatAllWithNothingPlayableEnds()
    {
        var q = new PlayQueue();
        q.Add(T("a.ogg"));
        q.Add(T("b.ogg"));
        q.Repeat = Repeat.All;
        q.Index = 0;
        var skipped = new List<(Track, string)>();
        Check.Eq(q.Find(1, Skip, skipped, out _), -1, "none: it does not go round forever");
        Check.Eq(skipped.Count, 2, "each passed over once");
    }

    public static void ShufflePlaysEverySongOnceTheOneOnFirst()
    {
        var q = Q(10);
        q.Index = 3;
        q.Shuffle = true;
        var played = Walk(q);
        Check.Eq(played.Count, 9, "the nine others");
        Check.Eq(played.Distinct().Count(), 9, "each once");
        Check.True(!played.Contains(3), "not the one that was on");
        Check.True(!played.SequenceEqual(new[] { 4, 5, 6, 7, 8, 9, 0, 1, 2 }) && !played.SequenceEqual(new[] { 0, 1, 2, 4, 5, 6, 7, 8, 9 }), "not the queue's order");
    }

    public static void ShuffleOffGoesOnInTheQueuesOrder()
    {
        var q = Q(6);
        q.Index = 2;
        q.Shuffle = true;
        q.Shuffle = false;
        Check.Eq(q.Find(1, None), 3, "the song after the one on");
        Check.True(!q.Shuffle, "off");
    }

    public static void ShuffleGoesBackTheWayItCame()
    {
        var q = Q(8);
        q.Index = 0;
        q.Shuffle = true;
        int next = q.Find(1, None);
        q.Index = next;
        Check.Eq(q.Find(-1, None), 0, "previous: the song played before");
    }

    public static void ShufflePlayNowAndAHandedRunComeNext()
    {
        var q = Q(8);
        q.Index = 0;
        q.Shuffle = true;
        int now = q.InsertNext(T("now.flac"));
        Check.Eq(q.Find(1, None), now, "play now: next even shuffled");
        q.Index = now;
        int a = q.Insert(now + 1, T("x.flac"), true);
        int b = q.Insert(a + 1, T("y.flac"), true);
        Check.Eq(q.Find(1, None), a, "a run handed over: its first");
        q.Index = a;
        Check.Eq(q.Find(1, None), b, "then its second");
    }

    public static void ShuffleTakesInAndLetsGo()
    {
        var q = Q(6);
        q.Index = 0;
        q.Shuffle = true;
        q.Add(T("late.flac"));
        Check.True(q.RemoveAt(2), "one taken out");
        Check.Eq(q.Items.Count, 6, "six left");
        var played = Walk(q);
        Check.Eq(played.Count, 5, "every other song of the six once");
        Check.Eq(played.Distinct().Count(), 5, "no song twice");
        Check.True(played.Any(i => q.Items[i].Title == "late.flac"), "the added one plays in the round");
        Check.True(played.All(i => i >= 0 && i < q.Items.Count), "every place still in the queue");
    }

    public static void ShuffleWithNothingOnStartsAnywhere()
    {
        var q = Q(5, 3);
        q.Shuffle = true;
        Check.Eq(Walk(q).Count, 5, "all five");
        q.Clear();
        Check.True(q.Shuffle, "still shuffled after clearing");
        q.Add(T("one.flac"));
        Check.Eq(q.Find(1, None), 0, "the only song");
    }

    public static void ANewRoundIsShuffledAgainFromItsFirst()
    {
        var q = Q(6);
        q.Index = 0;
        q.Shuffle = true;
        q.Repeat = Repeat.All;
        var first = Walk(q, 5);
        int round = q.Find(1, None, null, out bool wrapped);
        Check.True(wrapped, "the round ended");
        Check.Eq(round, 0, "the first round began with song 0");
        q.Reshuffle(round);
        q.Index = round;
        var second = Walk(q, 5);
        Check.Eq(first.Count, 5, "the first round: the five others");
        Check.Eq(second.Distinct().Count(), 5, "the new round, from its first: every other song once");
        Check.True(!second.Contains(round), "its first is not played twice");
        Check.Eq(q.Last, second[^1], "the last of the order");
    }

    public static void TagsReplaceTheVeryTrackRead()
    {
        var q = new PlayQueue();
        var one = T("same.flac");
        var two = T("same.flac");
        q.Add(one);
        q.Add(two);
        Check.True(q.Replace(two, two with { Title = "Tagged" }), "found");
        Check.Eq(q.Items[0].Title, "same.flac", "an equal track elsewhere stays");
        Check.Eq(q.Items[1].Title, "Tagged", "the one read is replaced");
        Check.True(!q.Replace(T("gone.flac"), T("x")), "one taken out meanwhile");
    }

    public static void RepeatIsReadFromTheSettings()
    {
        Check.Eq(MusicPlayer.RepeatOf("all"), Repeat.All, "all");
        Check.Eq(MusicPlayer.RepeatOf("ONE"), Repeat.One, "any case");
        Check.Eq(MusicPlayer.RepeatOf("off"), Repeat.Off, "off");
        Check.Eq(MusicPlayer.RepeatOf("5"), Repeat.Off, "a number that is no mode");
        Check.Eq(MusicPlayer.RepeatOf(null), Repeat.Off, "none");
    }
}
