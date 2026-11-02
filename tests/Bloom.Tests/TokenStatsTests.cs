using System;
using System.Collections.Generic;
using Bloom.Ai;

namespace Bloom.Tests;

/// The token count's flow on the UI thread (Ai\TokenStats), with no Dispatcher and no counting thread (Attach): when a
/// home drawn starts a pass, and what a pass sends back after a Reset or SetForQa.
internal static class TokenStatsTests
{
    private static TokenBucket Now(long output) =>
        new(new DateTime(DateTime.UtcNow.Ticks - DateTime.UtcNow.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc), "claude", "claude-opus-5-5", TokenVariant.None, new TokenCounts(1, 1, output, 0, 0, 0));

    public static void APassThatEndsShortIsNotStartedAgainAtOnce()
    {
        int started = 0;
        try
        {
            TokenStats.Attach(a => a(), () => true, () => started++);
            TokenStats.Look();
            Check.Eq(started, 1, "a home drawn starts a pass");
            TokenStats.Look();
            Check.Eq(started, 1, "none more while it runs");
            var job = Check.NotNull(TokenStats.Next(), "its job");
            // it ends with the first count not over (it broke off): Updated rebuilds the home, which draws the block again
            TokenStats.Publish(job, [Now(5)], null, false, changed: true, done: true);
            Check.True(TokenStats.Next() is null, "nothing more asked: the thread ends");
            TokenStats.Look();
            Check.Eq(started, 1, "a home drawn right after starts none");
            TokenStats.Refresh();
            Check.Eq(started, 2, "↻ still does");

            job = Check.NotNull(TokenStats.Next(), "the ↻ pass");
            TokenStats.Publish(job, [Now(5)], 0.4, false, changed: true, done: true, stopped: true);
            Check.True(TokenStats.Next() is null, "it stopped as no block wanted it");
            TokenStats.Look();
            Check.Eq(started, 3, "a block wanting the numbers again goes on at once");
        }
        finally
        {
            TokenStats.Attach(null);
        }
    }

    public static void WhatAnOlderPassSendsIsDropped()
    {
        var posted = new List<Action>();
        int started = 0;
        void Deliver()
        {
            foreach (var a in posted.ToArray()) a();
            posted.Clear();
        }
        try
        {
            TokenStats.Attach(posted.Add, () => true, () => started++);
            TokenStats.Look();
            var job = Check.NotNull(TokenStats.Next(), "the first pass's job");
            TokenStats.Publish(job, [Now(5)], 0.3, null, changed: true, done: false);
            Check.True(TokenStats.Indexing is null, "nothing until the UI thread takes it");
            Deliver();
            Check.Eq(TokenStats.Indexing, 0.3, "the first count's share");
            Check.Eq(TokenStats.Query(TokenPeriod.Day).Sum.Calls, 1L, "and its sums");

            TokenStats.Reset();
            Check.Eq(started, 1, "a Reset while a pass runs starts no second one");
            Check.True(TokenStats.Indexing is null && TokenStats.Query(TokenPeriod.Day).Sum.Calls == 0, "the numbers forgotten at once");
            TokenStats.Publish(job, [Now(5), Now(7)], 0.6, null, changed: true, done: true);
            Deliver();
            Check.True(TokenStats.Indexing is null && TokenStats.Counted is null && TokenStats.Query(TokenPeriod.Day).Sum.Calls == 0, "the older pass's end is dropped");

            var next = Check.NotNull(TokenStats.Next(), "one more pass follows");
            Check.True(next.Forget && next.Generation != job.Generation, "forgetting the index, for the new generation");
            TokenStats.Publish(next, [Now(9)], null, false, changed: true, done: true);
            Deliver();
            Check.True(TokenStats.Counted is not null && TokenStats.Query(TokenPeriod.Day).Sum.Output == 9, "its numbers are taken");
            Check.True(TokenStats.Next() is null, "and the thread ends");

            TokenStats.SetForQa(TokenView.Empty, 0.42);
            TokenStats.Publish(next, [Now(11)], null, false, changed: true, done: true);
            Deliver();
            Check.True(TokenStats.Indexing == 0.42 && TokenStats.Query(TokenPeriod.Day).Sum.Calls == 0, "nothing sent after SetForQa is taken");
            TokenStats.Look();
            Check.Eq(started, 1, "and no pass starts");
        }
        finally
        {
            TokenStats.Attach(null);
        }
    }
}
