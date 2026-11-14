using System;
using System.Threading.Tasks;
using Bloom.Addons;
using Bloom.Core;

namespace Bloom.App;

/// `Bloom.exe --settings --settings-page <key>`: 설정 opened on one page from outside Bloom — an add-on's own button
/// (`addon:<id>`, its page under 애드온) or a page's key (`about`, `addons` …). A Bloom that starts opens that page; a
/// running one gets the key over a pipe (LinePipe) and opens it. Only the page shown changes, never a value. An older
/// Bloom ignores the argument it doesn't know and opens the first screen, as `--settings` alone does.
internal static class SettingsHandoff
{
    public const string Arg = "--settings-page";

    /// The running Bloom's pipe for pages asked for.
    public static string Pipe => Paths.Pipe("settings");

    /// A key the argument and the pipe take: a page's (lowercase letters, digits and '-', as an add-on's id) or an
    /// add-on's page, `addon:` and its id. Whether this Bloom has that page is the settings window's to say (Has).
    public static bool IsKey(string? key) => key is not null
        && (key.StartsWith(AddonPrefix, StringComparison.Ordinal) ? AddonManifest.IsId(key[AddonPrefix.Length..]) : AddonManifest.IsId(key));

    private const string AddonPrefix = "addon:";

    /// The key the argument names; null for one IsKey doesn't take (the first screen opens).
    public static string? Parse(string value) => IsKey(value) ? value : null;

    /// The running Bloom, from its start: every key handed over that IsKey goes to `open` (true: settings opened).
    public static void Listen(string name, Func<string, Task<bool>> open) => LinePipe.Listen(name, IsKey, open, "settings");

    /// The handing start (LinePipe.Send). The running Bloom answers once its settings window is open.
    public static LinePipe.Answer? Send(string name, string key, int answerMs = 5000) => LinePipe.Send(name, key, answerMs);
}
