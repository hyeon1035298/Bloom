using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ui;

internal static class TutorialClip
{
    public const string Version = "v1";

    /// "ko" in Korean, "en" in every other language.
    public static string Variant => Lang.Code == "ko" ? "ko" : "en";

    // the what's-new route with its last folder swapped: the same host and repository
    public static string Url(string variant) =>
        ReleaseNotes.Base[..^"docs/whatsnew/".Length] + "docs/media/tutorial/" + Version + "/" + variant + ".mp4";

    public static string CacheFile(string variant) => Paths.InLocal(Path.Combine("tutorial", Version, variant + ".mp4"));

    /// Whether this run may play video at all (motion, system effects, Media Foundation, QA).
    public static bool Allowed(TutorialEnv env) => false;

    /// The cached file, downloaded first when missing (one download per variant and run, shared by every caller).
    /// Null when it cannot be had; never throws. `stop` ends this caller's wait, never the download.
    public static Task<string?> Get(string variant, CancellationToken stop) => Task.FromResult<string?>(null);

    /// Tests replace this; the app's reads the URL with Ai.ApiClient.Shared.
    internal static Func<string, CancellationToken, Task<byte[]>> Fetch = (url, token) => Ai.ApiClient.Shared.GetByteArrayAsync(new Uri(url), token);

    /// Tests: drops what this run remembers (the downloads under way and the ones that failed).
    internal static void Forget()
    {
    }
}
