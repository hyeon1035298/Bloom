using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bloom.Music;

/// How the mini player plays (설정 > 도구 > 미니 플레이어): Bloom itself, or a player program that is already on the PC.
internal static class PlayerWays
{
    public const string Direct = "direct", Jriver = "jriver", Foobar = "foobar", MusicBee = "musicbee";

    public static readonly (string key, string name)[] All = { (Direct, Tr("직접 재생", "Direct")), (Jriver, "JRiver"), (Foobar, "foobar2000"), (MusicBee, "MusicBee") };

    /// The setting's value, or the direct way when it names none of these.
    public static string Of(string? value) => All.Any(w => w.key == value) ? value! : Direct;

    public static string NameOf(string way) => All.First(w => w.key == Of(way)).name;

    /// Another program plays (the queue, the volume and the progress are its own).
    public static bool IsExternal(string way) => Of(way) != Direct;
}

/// What the mini player asks of another player.
internal enum PlayerOp { Play, Add, Toggle, Next, Previous, Stop }

/// The command lines of the programs that take them: foobar2000 and MusicBee. Each is what the program's own command line
/// does when it is already running (the running one receives it) and what starts it when it is not.
internal static class PlayerCommands
{
    /// The arguments for `op` (the file for Play and Add); null when the player has no command line for it or the file is missing.
    public static string[]? Args(string way, PlayerOp op, string? file = null)
    {
        bool needsFile = op is PlayerOp.Play or PlayerOp.Add;
        if (needsFile && string.IsNullOrEmpty(file)) return null;
        return way switch
        {
            // foobar2000: a bare file is its "open" — plays it (with the open-files setting of the user's foobar, which
            // may replace the active playlist); /add appends to the active playlist without playing
            PlayerWays.Foobar => op switch
            {
                PlayerOp.Play => new[] { file! },
                PlayerOp.Add => new[] { "/add", file! },
                PlayerOp.Toggle => new[] { "/playpause" },
                PlayerOp.Next => new[] { "/next" },
                PlayerOp.Previous => new[] { "/prev" },
                _ => new[] { "/stop" },
            },
            // MusicBee: /Play <file> plays it now, /QueueLast adds it after the Now Playing list
            PlayerWays.MusicBee => op switch
            {
                PlayerOp.Play => new[] { "/Play", file! },
                PlayerOp.Add => new[] { "/QueueLast", file! },
                PlayerOp.Toggle => new[] { "/PlayPause" },
                PlayerOp.Next => new[] { "/Next" },
                PlayerOp.Previous => new[] { "/Previous" },
                _ => new[] { "/Stop" },
            },
            _ => null,
        };
    }
}

/// Where a player program is: the path set in the settings, else what is running, else the usual install folders.
internal static class PlayerExe
{
    public static string ProcessName(string way) => way == PlayerWays.MusicBee ? "MusicBee" : "foobar2000";

    /// The places to look, in order, for the setting's path and the folders the programs install to.
    public static IEnumerable<string> Candidates(string way, string? setting, string? running, Func<string, string?> env)
    {
        string exe = ProcessName(way) + ".exe";
        if (!string.IsNullOrWhiteSpace(setting)) yield return setting.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(running)) yield return running;
        foreach (string variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" })
        {
            string? root = env(variable);
            if (string.IsNullOrWhiteSpace(root)) continue;
            string sub = variable == "LOCALAPPDATA" ? Path.Combine("Programs", ProcessName(way)) : ProcessName(way);
            yield return Path.Combine(root, sub, exe);
        }
    }

    /// The first candidate that exists, or null.
    public static string? Find(string way, string? setting, string? running, Func<string, string?> env, Func<string, bool> exists) =>
        Candidates(way, setting, running, env).FirstOrDefault(exists);
}

/// What JRiver Media Center's web service (MCWS) is asked, and its answer read: `Playback/Info` for the song and the
/// state, `PlayPause` · `Next` · `Previous` · `Stop`, and `PlayByFilename` for a file. The service answers on the PC
/// where JRiver runs (port 52199) and asks for a user and a password when JRiver was set up with them.
internal static class Mcws
{
    public const string DefaultAddress = "http://localhost:52199";

    /// "localhost:52199", "http://host:52199/" … → "http://host:52199/MCWS/v1/"; null when it is no http(s) address.
    public static string? Base(string? address)
    {
        string a = (address ?? "").Trim();
        if (a.Length == 0) a = DefaultAddress;
        if (!a.Contains("://", StringComparison.Ordinal)) a = "http://" + a;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0) return null;
        string path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/MCWS/v1", StringComparison.OrdinalIgnoreCase)) path = path[..^"/MCWS/v1".Length];
        return $"{uri.Scheme}://{uri.Authority}{path}/MCWS/v1/";
    }

    /// The request's address: `endpoint` after the base, the parameters escaped, the zone always the active one.
    public static string Url(string baseUrl, string endpoint, params (string name, string value)[] parameters)
    {
        var all = new List<(string, string)>(parameters) { ("Zone", "-1") };
        return baseUrl + endpoint + "?" + string.Join("&", all.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
    }

    public static string Endpoint(PlayerOp op) => op switch
    {
        PlayerOp.Toggle => "Playback/PlayPause",
        PlayerOp.Next => "Playback/Next",
        PlayerOp.Previous => "Playback/Previous",
        PlayerOp.Stop => "Playback/Stop",
        _ => "Playback/PlayByFilename",
    };

    /// The request for `op`. A file goes into Playing Now (`Filenames`, `Location`): Add at the end, Play right after the
    /// song that is on (`Next`; the caller then starts it with PlayByIndex). Playing Now is never replaced.
    public static string Request(string baseUrl, PlayerOp op, string? file = null) => op switch
    {
        PlayerOp.Play => Url(baseUrl, Endpoint(op), ("Filenames", file ?? ""), ("Location", "Next")),
        PlayerOp.Add => Url(baseUrl, Endpoint(op), ("Filenames", file ?? ""), ("Location", "End")),
        _ => Url(baseUrl, Endpoint(op)),
    };

    /// Starts the song at that place of Playing Now (0-based).
    public static string PlayByIndex(string baseUrl, int index) => Url(baseUrl, "Playback/PlayByIndex", ("Index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// "user:password" as the Authorization header's Basic value; null when there is no user.
    public static string? Basic(string? user, string? password) =>
        string.IsNullOrEmpty(user) ? null : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user + ":" + password));

    /// JRiver's answer to `Playback/Info`.
    public sealed record Info(bool Ok, bool Has, bool Playing, bool Paused, string Title, string Artist, TimeSpan Position, TimeSpan Length, int Index = 0, int Tracks = 0);

    /// Reads the XML (`<Response Status="OK"><Item Name="State">2</Item>…`): State 2 plays, 1 is paused, 0 is stopped.
    public static Info ParseInfo(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null || !string.Equals((string?)root.Attribute("Status"), "OK", StringComparison.OrdinalIgnoreCase)) return new Info(false, false, false, false, "", "", TimeSpan.Zero, TimeSpan.Zero);
            var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in root.Elements("Item"))
                if ((string?)item.Attribute("Name") is { } name) items[name] = item.Value;
            string Get(string k) => items.TryGetValue(k, out var v) ? v : "";
            _ = int.TryParse(Get("State"), out int state);
            string title = Get("Name");
            if (title.Length == 0) title = Path.GetFileNameWithoutExtension(Get("Filename"));
            bool has = state is 1 or 2 && title.Length > 0;
            return new Info(true, has, state == 2, state == 1, title, Get("Artist"), Millis(Get("PositionMS")), Millis(Get("DurationMS")), int.TryParse(Get("PlayingNowPosition"), out int at) ? at : 0, int.TryParse(Get("PlayingNowTracks"), out int n) ? n : 0);
        }
        catch (System.Xml.XmlException)
        {
            return new Info(false, false, false, false, "", "", TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    private static TimeSpan Millis(string s) => long.TryParse(s, out long ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

    /// Whether a play / control request was accepted: `<Response Status="OK">`.
    public static bool Accepted(string xml)
    {
        try
        {
            return string.Equals((string?)XDocument.Parse(xml).Root?.Attribute("Status"), "OK", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}
