namespace Bloom.Addons;

/// What a running add-on's line adds: it runs on symbols ported from an older Windows build until Microsoft publishes
/// the real ones, or some of its modules stayed unhooked (a required symbol of theirs is missing).
internal enum RunningNote
{
    None,
    Ported,
    Degraded,
}
