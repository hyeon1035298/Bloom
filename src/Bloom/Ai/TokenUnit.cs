namespace Bloom.Ai;

/// What one bar of a TokenView covers: Day → 24 hours, Week → 7 days, Month → 30 days, Quarter → weeks from Monday,
/// All → days while the count spans 60 days or less, months after.
internal enum TokenUnit { Hour, Day, Week, Month }
