using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Addons;

/// The taskbar as the mod sees it (state.json, protocol §5): the pinned order (apps, folders, the running folder's
/// place), each folder's icon and apps, and the running folder. Read only; folder changes go through TaskbarCommands.
internal sealed class TaskbarState
{
    public int Pid { get; set; }
    public StateFolders? Folders { get; set; }
    public List<StatePin> Pinned { get; set; } = new();
    public Dictionary<string, StateFolder> Items { get; set; } = new();
    public StateRunning? Running { get; set; }

    internal sealed class StateFolders
    {
        public bool Enabled { get; set; }
        public string? Path { get; set; }
    }

    /// kind: app (pinned outside any folder), folder (Name is the folder's), running.
    internal sealed class StatePin
    {
        public string Kind { get; set; } = "";
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? AppId { get; set; }
    }

    internal sealed class StateFolder
    {
        public StateIcon? Icon { get; set; }
        public List<StateApp> Apps { get; set; } = new();
    }

    /// kind: auto (the first apps drawn together) or file (Path, Index).
    internal sealed class StateIcon
    {
        public string Kind { get; set; } = "auto";
        public string? Path { get; set; }
        public int Index { get; set; }
    }

    internal sealed class StateApp
    {
        public string? File { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public string? AppId { get; set; }

        /// What its icon is drawn from: the shortcut, else the app's id in the shell's app list.
        public string? IconPath => Path is { Length: > 0 } p ? p : AppId is { Length: > 0 } id ? @"shell:AppsFolder\" + id : null;
    }

    internal sealed class StateRunning
    {
        public bool Enabled { get; set; }
        public string? Name { get; set; }
        public List<StateApp> Apps { get; set; } = new();
    }

    /// The folders in their pinned order, then any the pins don't name.
    public IEnumerable<string> FolderNames =>
        Pinned.Where(p => p.Kind == "folder" && p.Name is not null && Items.ContainsKey(p.Name)).Select(p => p.Name!)
            .Concat(Items.Keys.Where(k => !Pinned.Any(p => p.Kind == "folder" && p.Name == k))).Distinct();

    /// state.json in the add-on's data folder; null when missing, being replaced or unreadable.
    public static TaskbarState? Read(string dataDir) => AddonHub.ReadState<TaskbarState>(Path.Combine(dataDir, AddonProtocol.StateFile));
}

/// A folder command's answer: ok, or the mod's error (foldersOff, noFolder, exists, badName, notFound, badArgs, failed),
/// or "timeout" when no answer came.
internal readonly record struct CommandResult(bool Ok, string? Error)
{
    public const string Timeout = "timeout";
}

/// Folder commands to the mod (protocol §5): inbox\<id>.json written whole, the answer outbox\<id>.json awaited and
/// removed. Without an answer in 5 s the folder editing goes read only (TimedOut) until a later command is answered;
/// the unanswered command is taken back so it doesn't run later by surprise.
internal sealed class TaskbarCommands
{
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(5);

    private readonly string dataDir;
    private readonly TimeSpan wait;
    private int counter;

    public TaskbarCommands(string dataDir, TimeSpan? wait = null)
    {
        this.dataDir = dataDir;
        this.wait = wait ?? AnswerWait;
    }

    /// The last command went unanswered.
    public bool TimedOut { get; private set; }

    private DateTime timedOutAt;

    /// state.json was written at `at` (UTC): written after an unanswered command, the mod is back and so is editing.
    public void Heard(DateTime at)
    {
        if (TimedOut && at > timedOutAt) TimedOut = false;
    }

    /// Ids sort in the order they were made (the mod takes them in name order).
    public string NextId() => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture) + "-" + Interlocked.Increment(ref counter).ToString("0000", CultureInfo.InvariantCulture);

    public static JsonObject Command(string id, string cmd, JsonObject args) => new() { ["id"] = id, ["cmd"] = cmd, ["args"] = args };

    // the commands' arguments
    public static (string, JsonObject) Create(string name, IEnumerable<string> apps) => ("create", new() { ["name"] = name, ["apps"] = Strings(apps) });
    public static (string, JsonObject) Rename(string folder, string name) => ("rename", new() { ["folder"] = folder, ["name"] = name });
    public static (string, JsonObject) IconAuto(string folder) => ("icon", new() { ["folder"] = folder, ["kind"] = "auto" });
    public static (string, JsonObject) IconFile(string folder, string path, int index) => ("icon", new() { ["folder"] = folder, ["kind"] = "file", ["path"] = path, ["index"] = index });
    public static (string, JsonObject) Add(string folder, string app) => ("add", new() { ["folder"] = folder, ["app"] = app });
    public static (string, JsonObject) TakeOut(string folder, string item) => ("takeOut", new() { ["folder"] = folder, ["item"] = item });
    public static (string, JsonObject) Order(string folder, IEnumerable<string> names) => ("order", new() { ["folder"] = folder, ["names"] = Strings(names) });
    public static (string, JsonObject) Dissolve(string folder) => ("dissolve", new() { ["folder"] = folder });

    private static JsonArray Strings(IEnumerable<string> items) => new(items.Select(s => (JsonNode)JsonValue.Create(s)).ToArray());

    public Task<CommandResult> Send((string cmd, JsonObject args) c) => Task.Run(() => SendNow(c.cmd, c.args));

    private CommandResult SendNow(string cmd, JsonObject args)
    {
        string id = NextId();
        string inbox = Path.Combine(dataDir, AddonProtocol.InboxDir, id + ".json"), outbox = Path.Combine(dataDir, AddonProtocol.OutboxDir, id + ".json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(inbox)!);
            string text = Command(id, cmd, args).ToJsonString();
            AtomicFile.Write(inbox, stream =>
            {
                using var w = new StreamWriter(stream);
                w.Write(text);
            });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < wait)
            {
                if (Answer(outbox) is { } r)
                {
                    TimedOut = false;
                    return r;
                }
                Thread.Sleep(50);
            }
            if (Answer(outbox) is { } late)
            {
                TimedOut = false;
                return late;
            }
            TryDelete(inbox);
            timedOutAt = DateTime.UtcNow;
            TimedOut = true;
            Log.Write($"taskbar command {cmd}: no answer");
            return new CommandResult(false, CommandResult.Timeout);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("taskbar command", e);
            return new CommandResult(false, "failed");
        }
    }

    /// The answer when it is there (read, then removed); null while it isn't (or is still being written).
    private static CommandResult? Answer(string file)
    {
        if (!File.Exists(file)) return null;
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
        TryDelete(file);
        bool ok = o?["ok"] is JsonValue v && v.TryGetValue(out bool b) && b;
        string? error = o?["error"] is JsonValue ev && ev.TryGetValue(out string? s) ? s : null;
        return new CommandResult(ok, ok ? null : error ?? "failed");
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the mod has it open right now: it removes its own inbox file, and an outbox one is read only once
        }
    }
}
