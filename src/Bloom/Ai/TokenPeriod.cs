namespace Bloom.Ai;

/// The span a token block sums: the last 24 hours, the last 7, 30 or 90 days counting today, or all Bloom has counted.
internal enum TokenPeriod { Day, Week, Month, Quarter, All }
