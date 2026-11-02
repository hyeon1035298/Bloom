using System;
using Bloom.Ui;

namespace Bloom.App;

/// The home's 플레이어 block: what it shows, kept up while the launcher shows (Music\MusicPlayer.Watch) and never asked
/// for while it is hidden.
internal static class HomePlayer
{
    /// Host, once, outside QA, after the launcher was built: `window` (watched only while it shows), `later` (the home
    /// drawn again soon), `demo` (the recording mode: a made-up song, nothing asked).
    public static void Start(Launcher window, Action later, bool demo) { }
}
