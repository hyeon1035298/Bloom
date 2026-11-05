using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// The CODEX_HOME a ChatGPT chat's `codex app-server` runs in. app-server has no --ignore-user-config, and `-c` overrides
/// only merge into the tables config.toml sets, so it never gets the account's own folder: each account has a folder of
/// Bloom's (bloom-app-server in an added account's folder, Local\codex-app-server for the default one) whose config.toml
/// Bloom writes. The account's config.toml — base URLs, model providers, profiles, sandbox, shell environment, MCP
/// servers, hooks, features — and the AGENTS.md, skills and rules beside it are not read, decided before the process
/// starts. Two things stay the account's own, shared and not copied:
/// - auth.json is a hard link to the account's: one file, which Codex rewrites in place when it refreshes the sign-in
///   (codex-cli 0.159.3), so the user's own Codex and Bloom's never hold different tokens. It is there only while an
///   app-server runs in the folder (Claim makes it, the last one's Leave takes it away, Sweep one a crash left), so a
///   sign-out isn't kept alive by it. A running Codex keeps the sign-in it read, and writes a refresh back after its
///   request without looking again, so the link never moves under one: when the account's file is replaced, holds
///   another sign-in or is gone — seen by a watch on it, by CodexAuth putting tokens in, by a turn (Ticket.Holds), or
///   told by UsageLogin before a sign-in or sign-out (Hold) — the processes on the old one end and are waited for until they're gone,
///   and only then is the file linked again, for the next one. A sign-in that can't be told or linked starts none.
/// - sessions is a junction to the account's sessions folder: the rollouts stay where `codex exec` keeps them, so a saved
///   thread id resumes on either.
/// What the machine sets (%ProgramData%\OpenAI\Codex: config.toml, requirements.toml) is outside CODEX_HOME and applies
/// as before. Codex's own databases (state_5.sqlite and the like) are the folder's: the first app-server in it indexes the
/// account's sessions once before it answers initialize (17 s for 637 rollouts here), so the first start is given longer
/// (Ready). Sharing the account's databases instead (CODEX_SQLITE_HOME) would save that, but they would then name this
/// folder's junction as where Bloom's threads are, and a thread whose row names a path that's gone doesn't resume.
internal static class CodexAppHome
{
    /// The whole of the folder's config.toml (anything else in it is written over at the next start).
    internal const string Config = """
        # Bloom's ChatGPT chats run `codex app-server` with this folder as CODEX_HOME, so the account's own config.toml
        # is not read (as `codex exec --ignore-user-config` doesn't read it). Bloom writes this file before each start.
        cli_auth_credentials_store = "file"
        sandbox_mode = "read-only"
        approval_policy = "never"

        """;

    /// How long the processes on a sign-in that changed get to be gone before a start or a sign-in gives up on them.
    private static readonly TimeSpan EndWait = TimeSpan.FromSeconds(30);

    /// The folder for an account's folder (null: Codex's default one).
    public static string Folder(string? accountHome) =>
        accountHome is null ? Path.Combine(Paths.Local, "codex-app-server") : Path.Combine(accountHome, "bloom-app-server");

    /// The sign-in a process started on: the account's auth.json (its volume and index, the same for every link to it) and
    /// whose sign-in it holds.
    internal readonly record struct Stamp(string File, string Who);

    /// One folder of Bloom's: the account it is for, the app-servers in it, and the watch on the account's auth.json while
    /// any runs. The link and Live change under Gate only.
    internal sealed class Place(string home, string account)
    {
        public readonly string Home = home, Account = account;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly List<Ticket> Live = new();
        public FileSystemWatcher? Watch;
        public int Held; // sign-ins running into the account's folder (Hold): nothing starts
        public string Link => Path.Combine(Home, "auth.json");
        public string Source => Path.Combine(Account, "auth.json");
    }

    private static readonly Dictionary<string, Place> places = new(StringComparer.OrdinalIgnoreCase); // by Bloom's folder; locked

    private static Place PlaceOf(string? accountHome)
    {
        string home = Full(Folder(accountHome));
        lock (places)
        {
            if (!places.TryGetValue(home, out var place)) places[home] = place = new Place(home, Full(accountHome ?? CodexAuth.DefaultHome()));
            return place;
        }
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// One app-server's place in its folder, from before its launch until it and all it started are gone (Leave).
    internal sealed class Ticket
    {
        internal readonly Place Place;
        internal readonly Stamp Stamp;
        internal readonly TaskCompletionSource Gone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<Ticket> end;

        internal Ticket(Place place, Stamp stamp, Action<Ticket> end)
        {
            Place = place;
            Stamp = stamp;
            this.end = end;
        }

        /// Bloom's folder: the process's CODEX_HOME.
        public string Home => Place.Home;

        /// The account's auth.json is still the file and the sign-in the process started on, and Bloom's still a link to it
        /// (asked before each turn; a file that can't be read counts as changed).
        public bool Holds() => Read(Place.Account, out var now) && now == Stamp && Identity(Place.Link) == Stamp.File;

        private int ended;

        /// It was asked to end, perhaps before its session had it (Open looks once it has).
        public bool Ended => Volatile.Read(ref ended) == 1;

        /// Its sign-in changed: the session ends the process (Leave follows once it is gone).
        internal void End()
        {
            Interlocked.Exchange(ref ended, 1);
            try
            {
                end(this);
            }
            catch (Exception e)
            {
                Log.Error("codex app-server end", e);
            }
        }

        /// The process and all it started are gone: it stops counting, and the folder's last one takes the link with it.
        public void Leave()
        {
            if (Gone.TrySetResult()) _ = Left(this);
        }
    }

    /// A place for a new app-server in Bloom's folder for the account (null: Codex's default one), made ready: Bloom's
    /// config.toml, the sessions junction, and auth.json linked to the account's sign-in — after every process still on
    /// another one has ended and is gone. Null, with the reason, when the account is signed out, its sign-in can't be told
    /// or linked, or a sign-in into it runs: the chat then doesn't go on app-server. `end` ends the process when its
    /// sign-in changes.
    public static async Task<(Ticket? Ticket, string? Why)> Claim(string? accountHome, Action<Ticket> end, CancellationToken cancellation)
    {
        var place = PlaceOf(accountHome);
        await place.Gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref place.Held) > 0 || LoginMarker.Alive(place.Account)) return (null, "a sign-in into the account is running");
            Directory.CreateDirectory(place.Home);
            if (Redirected(place.Home)) return (null, "its folder is a link");
            string config = Path.Combine(place.Home, "config.toml");
            if (!File.Exists(config) || File.ReadAllText(config) != Config) File.WriteAllText(config, Config);
            if (Sessions(place.Home, place.Account) is { } why) return (null, why);
            for (int tries = 0; tries < 3; tries++)
            {
                if (!Read(place.Account, out var now)) return (null, "the account's auth.json can't be read");
                if (now is not { } stamp)
                {
                    await Settle(place, place.Live.ToList(), cancellation).ConfigureAwait(false);
                    Idle(place);
                    return (null, "the account is signed out");
                }
                // the link moves only with no process left on it; the others end when they're on another sign-in
                bool linked = Identity(place.Link) == stamp.File;
                await Settle(place, place.Live.Where(t => !linked || t.Stamp != stamp).ToList(), cancellation).ConfigureAwait(false);
                if (!linked && !Link(place, out why)) return (null, why);
                if (!Read(place.Account, out var after) || after != stamp || Identity(place.Link) != stamp.File) continue; // changed meanwhile
                var ticket = new Ticket(place, stamp, end);
                place.Live.Add(ticket);
                Watch(place);
                return (ticket, null);
            }
            return (null, "the account's sign-in kept changing");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or TimeoutException)
        {
            return (null, e.Message);
        }
        finally
        {
            place.Gate.Release();
        }
    }

    /// The account's auth.json was put in place by Bloom (CodexAuth): looked at as its watch would.
    public static Task Changed(string account)
    {
        string full = Full(account);
        List<Place> theirs;
        lock (places) theirs = places.Values.Where(p => string.Equals(p.Account, full, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.WhenAll(theirs.Select(Check));
    }

    /// A sign-in or sign-out is about to run in the account's folder (UsageLogin): every app-server in Bloom's folder for it
    /// ends and is waited for, the link goes, and none starts until the hold is let go (nor while the login terminal stays
    /// open, LoginMarker) — nothing of the old sign-in can be written over the new one or kept past the sign-out. Null when
    /// one would not end.
    public static async Task<IDisposable?> Hold(string? accountHome)
    {
        var place = PlaceOf(accountHome);
        await place.Gate.WaitAsync().ConfigureAwait(false);
        Interlocked.Increment(ref place.Held);
        try
        {
            await Settle(place, place.Live.ToList(), CancellationToken.None).ConfigureAwait(false);
            Idle(place);
            return new Holding(place);
        }
        catch (TimeoutException)
        {
            Interlocked.Decrement(ref place.Held);
            return null;
        }
        finally
        {
            place.Gate.Release();
        }
    }

    private sealed class Holding(Place place) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) Interlocked.Decrement(ref place.Held);
        }
    }

    /// Bloom starts: a link a Bloom that ended without its processes leaving (a crash) left behind goes — no app-server of
    /// this Bloom runs yet — so a sign-in signed out meanwhile isn't kept in Bloom's folder.
    public static Task Sweep() => Sweep(AiAccounts.All.Where(a => a.Kind == "chatgpt").Select(a => a.Home).ToList());

    internal static Task Sweep(IEnumerable<string?> accountHomes)
    {
        var all = accountHomes.Select(PlaceOf).ToList();
        return Task.Run(async () =>
        {
            foreach (var place in all)
            {
                await place.Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (place.Live.Count == 0 && File.Exists(place.Link))
                    {
                        Unlink(place);
                        Log.Write("codex app-server: a sign-in link left in " + place.Home + " is removed");
                    }
                }
                finally
                {
                    place.Gate.Release();
                }
            }
        });
    }

    /// The account's auth.json may have changed: the processes not on the sign-in it now holds end and are waited for, and
    /// with none left the link goes. A file that can't be read yet is left to the next look (each turn looks too).
    private static async Task Check(Place place)
    {
        await Task.Delay(100).ConfigureAwait(false); // a write in progress settles
        await place.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (place.Live.Count == 0 || !Read(place.Account, out var now)) return;
            bool linked = now is { } stamp && Identity(place.Link) == stamp.File;
            var ending = place.Live.Where(t => !linked || t.Stamp != now).ToList();
            if (ending.Count == 0) return;
            Log.Write($"codex app-server: the sign-in in {place.Account} changed or is gone; {ending.Count} process(es) on the old one end before it is linked again");
            await Settle(place, ending, CancellationToken.None).ConfigureAwait(false);
            if (place.Live.Count == 0) Idle(place);
        }
        catch (TimeoutException)
        {
            Log.Write("codex app-server: a process on the old sign-in in " + place.Account + " did not end");
        }
        finally
        {
            place.Gate.Release();
        }
    }

    /// The processes are asked to end, then waited for until they and all they started are gone. Under the place's gate.
    private static async Task Settle(Place place, List<Ticket> ending, CancellationToken cancellation)
    {
        if (ending.Count == 0) return;
        foreach (var t in ending) t.End();
        await Task.WhenAll(ending.Select(t => t.Gone.Task)).WaitAsync(EndWait, cancellation).ConfigureAwait(false);
        place.Live.RemoveAll(ending.Contains);
    }

    private static async Task Left(Ticket t)
    {
        var place = t.Place;
        await place.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            place.Live.Remove(t);
            if (place.Live.Count == 0) Idle(place);
        }
        finally
        {
            place.Gate.Release();
        }
    }

    /// No app-server runs in the folder: the watch and the link go. Under the place's gate.
    private static void Idle(Place place)
    {
        place.Watch?.Dispose();
        place.Watch = null;
        Unlink(place);
    }

    /// A watch on the account's auth.json while an app-server runs in the folder (Check). Under the place's gate.
    private static void Watch(Place place)
    {
        if (place.Watch is not null) return;
        try
        {
            var watch = new FileSystemWatcher(place.Account, "auth.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watch.Changed += (_, _) => _ = Check(place);
            watch.Created += (_, _) => _ = Check(place);
            watch.Deleted += (_, _) => _ = Check(place);
            watch.Renamed += (_, _) => _ = Check(place);
            watch.Error += (_, _) => _ = Check(place);
            watch.EnableRaisingEvents = true;
            place.Watch = watch;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Log.Write("codex app-server: no watch on " + place.Source + " (each turn still looks): " + e.Message);
        }
    }

    /// The account's sign-in now: true with null when it has none (signed out); false when it can't be told (held, half
    /// written: looked at three times).
    private static bool Read(string account, out Stamp? now)
    {
        string file = Path.Combine(account, "auth.json");
        for (int i = 0; ; i++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (Identity(stream.SafeFileHandle) is { } id)
                {
                    now = new Stamp(id, Who(JsonNode.Parse(stream)));
                    return true;
                }
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                now = null;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                /* held or being written: once more */
            }
            if (i == 2)
            {
                now = null;
                return false;
            }
            Thread.Sleep(50);
        }
    }

    /// Whose sign-in auth.json holds: the ChatGPT account and user (its id token), or the API key's hash. Kept in memory only.
    private static string Who(JsonNode? root)
    {
        if (root is not JsonObject o) throw new JsonException("auth.json holds no object");
        static string? Text(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        if (CodexAuth.From(o) is { } auth)
        {
            var claims = ChatGptLight.Claims(auth.IdToken) as JsonObject;
            string? account = auth.AccountId ?? Text((claims?["https://api.openai.com/auth"] as JsonObject)?["chatgpt_account_id"]);
            return "chatgpt " + account + " " + (Text(claims?["sub"]) ?? Text(claims?["email"]));
        }
        return Text(o["OPENAI_API_KEY"]) is { Length: > 0 } key ? "key " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16] : "";
    }

    /// Bloom's auth.json made a link to the account's (no process runs in the folder: Claim). Under the place's gate.
    private static bool Link(Place place, out string? why)
    {
        why = null;
        string temp = Path.Combine(place.Home, "auth." + Guid.NewGuid().ToString("N") + ".link");
        if (!CreateHardLinkW(temp, place.Source, IntPtr.Zero))
        {
            why = "auth.json can't be linked: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        try
        {
            File.Move(temp, place.Link, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Remove(temp);
            why = "auth.json can't be linked: " + e.Message;
            return false;
        }
    }

    /// Bloom's auth.json goes: a name of the account's file, whose own name stays. Under the place's gate.
    private static void Unlink(Place place)
    {
        try
        {
            if (!Directory.Exists(place.Home) || Redirected(place.Home)) return;
            foreach (string temp in Directory.EnumerateFiles(place.Home, "auth.*.link")) Remove(temp);
            if (File.Exists(place.Link) && !Remove(place.Link)) Log.Write("codex app-server: Bloom's link to the sign-in in " + place.Home + " can't be removed yet");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("codex app-server: Bloom's link to the sign-in in " + place.Home + " can't be removed yet: " + e.Message);
        }
    }

    private static bool Remove(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// The folder is itself a link (a junction somewhere else): nothing is linked or removed in it.
    private static bool Redirected(string folder) => (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;

    /// An app-server has started in `home` before (it has indexed the account's sessions); MarkReady once one has.
    public static bool Ready(string home) => File.Exists(Path.Combine(home, ReadyMarker));

    public static void MarkReady(string home)
    {
        try
        {
            if (!Ready(home)) File.WriteAllText(Path.Combine(home, ReadyMarker), "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* the next start is given longer again */
        }
    }

    private const string ReadyMarker = "bloom-ready";

    /// `home`\sessions as a junction to the account's sessions folder; the reason when it can't be.
    private static string? Sessions(string home, string account)
    {
        string target = Path.GetFullPath(Path.Combine(account, "sessions")), link = Path.Combine(home, "sessions");
        Directory.CreateDirectory(target);
        var info = new DirectoryInfo(link);
        if (info.Exists || info.LinkTarget is not null)
        {
            if (info.LinkTarget is { } now && Same(now, target)) return null;
            // a junction to another folder (the account's moved), or an empty folder: made again
            if (info.LinkTarget is null && info.EnumerateFileSystemInfos().Any()) return "its sessions folder is not the account's";
            info.Delete();
        }
        Junction(link, target);
        return null;
    }

    private static bool Same(string a, string b)
    {
        static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.StartsWith(@"\??\", StringComparison.Ordinal) ? p[4..] : p));
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    /// A junction (a mount point) at `link` to the folder `target`: unlike a symbolic link, any user may make one.
    internal static void Junction(string link, string target)
    {
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target), print = Encoding.Unicode.GetBytes(target);
        int paths = substitute.Length + 2 + print.Length + 2;
        using var data = new MemoryStream();
        using (var w = new BinaryWriter(data, Encoding.Unicode, true))
        {
            w.Write(MountPointTag);
            w.Write((ushort)(8 + paths)); // what follows the 8-byte header
            w.Write((ushort)0);
            w.Write((ushort)0); // the substitute name, then the print name, each ending in a NUL
            w.Write((ushort)substitute.Length);
            w.Write((ushort)(substitute.Length + 2));
            w.Write((ushort)print.Length);
            w.Write(substitute);
            w.Write((ushort)0);
            w.Write(print);
            w.Write((ushort)0);
        }
        byte[] buffer = data.ToArray();
        Directory.CreateDirectory(link);
        using var handle = CreateFileW(link, GenericWrite, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, SetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            Directory.Delete(link);
            throw new Win32Exception(error);
        }
    }

    /// The file's volume and index: the same for every hard link to it; null when it can't be opened.
    private static string? Identity(string path)
    {
        using var handle = CreateFileW(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        return handle.IsInvalid ? null : Identity(handle);
    }

    private static string? Identity(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info) ? $"{info.VolumeSerial:x8}:{info.IndexHigh:x8}{info.IndexLow:x8}" : null;

    private const uint MountPointTag = 0xA0000003, SetReparsePoint = 0x000900A4;
    private const uint ReadAttributes = 0x80, GenericWrite = 0x40000000, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public uint Attributes;
        public uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh; // three FILETIMEs
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
