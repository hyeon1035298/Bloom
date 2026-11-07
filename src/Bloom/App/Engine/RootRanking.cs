using System;
using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// A matched source and its final score, before expensive row/actions construction.
internal readonly record struct RootCandidate(object Source, int Score);

internal static class RootRanking
{
    public static void AppendTop(List<RootCandidate> candidates, List<ResultItem> output, Func<object, ResultItem> materialize)
    {
        // Keep the same traversal, comparator and List.Sort tie behavior as the root list.
        candidates.Sort((x, y) => y.Score.CompareTo(x.Score));
        // a result hidden from the search (Core.HiddenResults) is passed over, so the list still fills up to nine
        for (int i = 0, added = 0; i < candidates.Count && added < 9; i++)
        {
            var candidate = candidates[i];
            var item = materialize(candidate.Source);
            if (HiddenResults.Has(item.Id)) continue;
            item.Score = candidate.Score;
            output.Add(item);
            added++;
        }
    }
}
