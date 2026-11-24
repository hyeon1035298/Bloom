using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The tools an API chat offers and how each call runs: Bloom's own "pc" tool (IPcTools, the MCP server's handlers)
/// and file and command tools in the chat's folder. As in the CLI chats, look-ups go straight through — the pc tool's
/// find_* and reading or listing inside the chat's folder (really inside: RealPath) — and everything else goes by
/// 설정's 권한 (ToolPermissions): 사용 안 함 is refused here (and its tool not offered), the rest waits for the user's
/// yes on the approval card (`ask`: Chats.Asked, which lets 항상 허용 and 이 대화에서 항상 허용 through): reading
/// elsewhere, every write, edit and command, every pc action. No result carries a stored key or a hidden variable's
/// value (Secrets).
internal sealed class ApiToolbox
{
    public const string ReadFile = "read_file", ListFolder = "list_folder", WriteFile = "write_file", EditFile = "edit_file", RunCommand = "run_command";

    /// A file read gives at most this much text; a result at most MaxResult (both cut with a note).
    public const int MaxRead = 100_000, MaxResult = 40_000;

    private const int MaxEntries = 300;
    private const string Declined = "The user declined this. Don't try it again unless they ask.";
    private const string SwitchedOff = "The user switched this off in Bloom's settings. Don't try it again.";
    private const string Redacted = "***";

    private readonly string folder;
    private readonly IPcTools? pc;
    private readonly IReadOnlyDictionary<string, string>? rules;
    private readonly Func<ToolAsk, Task<bool>> ask;
    private readonly string[] secrets;
    private readonly string? pcName;
    private readonly ApiMcp? mcp;

    /// `folder`: the chat's own (relative paths start there, commands run there); `pc`: Bloom's tool, when offered;
    /// `rules`: 설정's 권한 as the turn began (Settings.Permissions; null: no file or command tools at all); `secrets`:
    /// text no result may carry (Secrets()); `mcp`: the other MCP servers' tools, prepared for this turn (권한's 다른 MCP
    /// 도구 사용 안 함: none).
    public ApiToolbox(string folder, IPcTools? pc, IReadOnlyDictionary<string, string>? rules, Func<ToolAsk, Task<bool>> ask, IEnumerable<string>? secrets = null,
        ApiMcp? mcp = null)
    {
        this.folder = Path.GetFullPath(folder);
        this.pc = pc;
        this.rules = rules;
        this.ask = ask;
        this.mcp = mcp is not null && ToolPermissions.Of(rules ?? NoRules, ToolPermissions.Mcp) != ToolPermissions.Off ? mcp : null;
        secrets = (secrets ?? []).Concat(this.mcp?.Secrets ?? []);
        // the longest first, so one that holds another is taken out whole
        this.secrets = (secrets ?? []).Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToArray();
        var tools = new List<ApiTool>();
        if (pc is not null && PcTool(pc) is { } bloom)
        {
            tools.Add(bloom);
            pcName = bloom.Name;
        }
        // reading and listing stay (the chat's own folder needs no permission); an action switched off is not offered
        if (rules is not null) tools.AddRange(FileTools.Where(t => ActionOf(t.Name) is not { } action || ToolPermissions.Of(rules, action) != ToolPermissions.Off));
        if (this.mcp is not null) tools.AddRange(this.mcp.Tools);
        Tools = tools;
    }

    private static readonly Dictionary<string, string> NoRules = new();

    /// Names no MCP server's tool may take (McpNames): Bloom's own, and its pc tool as Claude Code names it.
    public static readonly string[] Reserved = { ReadFile, ListFolder, WriteFile, EditFile, RunCommand, "pc", "mcp__bloom__pc" };

    public IReadOnlyList<ApiTool> Tools { get; }

    public bool Offers(string tool) => Tools.Any(t => t.Name == tool);

    /// Some of the tools are other MCP servers'.
    public bool OffersMcp => mcp is not null && mcp.Tools.Count > 0;

    /// The 권한 a file or command tool asks under (reading: only outside the chat's folder, so none here).
    private static string? ActionOf(string tool) => tool switch
    {
        WriteFile => ToolPermissions.Write,
        EditFile => ToolPermissions.Edit,
        RunCommand => ToolPermissions.Command,
        _ => null,
    };

    private string Rule(string action) => rules is null ? ToolPermissions.Off : ToolPermissions.Of(rules, action);

    /// What no tool result may carry, on a worker thread (it opens the stored keys): every API key kept in 설정 > AI 계정
    /// and the values ApiCommand keeps out of a command's environment.
    public static List<string> Secrets()
    {
        var list = ApiProviders.All.Select(p => ApiKeys.Get(p.Id)).OfType<string>().ToList();
        list.AddRange(ApiCommand.Environment().Hidden);
        return list;
    }

    private static readonly ApiTool[] FileTools =
    {
        new(ReadFile, "Read a text file on the user's PC. Relative paths start in your working folder.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        new(ListFolder, "List a folder on the user's PC (folders end with \\). Relative paths start in your working folder; \".\" is it.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        new(WriteFile, "Write a whole text file (UTF-8), made with its folders when missing, replacing one that is there. Waits for the user's OK.",
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""),
        new(EditFile, "Replace old_text, which must occur exactly once, with new_text in a text file. Waits for the user's OK.",
            """{"type":"object","properties":{"path":{"type":"string"},"old_text":{"type":"string"},"new_text":{"type":"string"}},"required":["path","old_text","new_text"]}"""),
        new(RunCommand, $"Run a Windows PowerShell command in your working folder; gives its output (at most {ApiCommand.MaxOutput} characters) and exit code; stopped after {(int)ApiCommand.Timeout.TotalSeconds} s, and whatever it starts ends with it. Waits for the user's OK.",
            """{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}"""),
    };

    /// The pc tool's MCP definition as a function (its name, description and input schema as they are).
    private static ApiTool? PcTool(IPcTools pc)
    {
        var d = JsonSerializer.SerializeToElement(pc.Definition);
        return d.TryGetProperty("inputSchema", out var schema)
            ? new ApiTool(ApiJson.Str(d, "name"), ApiJson.Str(d, "description"), schema.GetRawText())
            : null;
    }

    /// The chat's status line while a call runs.
    public string Doing(ApiToolCall call) => call.Name switch
    {
        ReadFile => Tr("파일 읽는 중…", "Reading file…"),
        ListFolder => Tr("폴더 보는 중…", "Viewing folder…"),
        WriteFile or EditFile => Tr("파일 쓰는 중…", "Writing file…"),
        RunCommand => Tr("명령 실행 중…", "Running command…"),
        _ when mcp?.Has(call.Name) == true => Tr("MCP 도구 사용 중…", "Using MCP tool…"),
        _ => Tr("PC에서 작업하는 중…", "Working on PC…"),
    };

    /// One call: asked first when it must be, then run. → (its result for the model, failed). Cancelling `stop` ends a
    /// command and throws OperationCanceledException.
    public async Task<(string Text, bool Error)> RunAsync(ApiToolCall call, CancellationToken stop)
    {
        JsonElement a;
        try
        {
            using var doc = JsonDocument.Parse(call.Arguments.Length > 0 ? call.Arguments : "{}");
            a = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ("The arguments were not valid JSON; call the tool again with a JSON object.", true);
        }
        if (a.ValueKind != JsonValueKind.Object) return ("The arguments must be a JSON object.", true);
        // the card shows what is asked about whole, so a call bigger than a card can show is not asked about at all
        if (call.Arguments.Length > ToolText.MaxPayload)
            return ($"The arguments are {call.Arguments.Length:N0} characters; at most {ToolText.MaxPayload:N0} can be shown for approval in full, so nothing was done. Send less at a time (split a file over several steps).", true);
        var (text, error) = await Run(call, a, stop).ConfigureAwait(false);
        foreach (string secret in secrets) text = text.Replace(secret, Redacted, StringComparison.Ordinal);
        // never an empty result (an empty file): some providers refuse a tool message with no content
        return (text.Length > MaxResult ? text[..MaxResult] + "\n… (cut)" : text.Length == 0 ? "(empty)" : text, error);
    }

    private async Task<(string, bool)> Run(ApiToolCall call, JsonElement a, CancellationToken stop)
    {
        if (pc is not null && call.Name == pcName)
        {
            // Chats words the card and the key from the pc tool's own policy (Says and Key left empty)
            if (!pc.Quiet(call.Name, call.Arguments) && !await Asked(new ToolAsk(call.Id, call.Name, call.Arguments, ""), stop).ConfigureAwait(false)) return (Declined, true);
            return await pc.Call(call.Name, a).ConfigureAwait(false);
        }
        if (mcp?.Of(call.Name) is { } tool)
        {
            // every call asks (권한's 다른 MCP 도구; 이 대화에서 항상 허용 remembers the one tool of that server: its identity
            // and the tool's own name, not the function name, which can change); the card shows the arguments whole. No
            // "verb: " in the words: the card's always-button then names no action, as the key is the tool itself
            string says = Tr($"MCP 도구 {tool.Server} · {tool.Tool}", $"MCP Tool {tool.Server} · {tool.Tool}");
            var question = new ToolAsk(call.Id, call.Name, call.Arguments, ToolText.Line(says), tool.Key) { Detail = ToolText.Visible(a.ToString()) };
            if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
            return await mcp.CallAsync(call.Name, a, stop).ConfigureAwait(false);
        }
        if (rules is null || FileTools.All(t => t.Name != call.Name)) return ($"No tool named {call.Name}.", true);
        if (ActionOf(call.Name) is { } action && Rule(action) == ToolPermissions.Off) return (SwitchedOff, true);
        // the path as written: no %VARIABLE% is expanded, so neither the card nor an error text can show a variable's value
        string path = ApiJson.Str(a, "path").Trim().Trim('"');
        switch (call.Name)
        {
            case ReadFile or ListFolder:
                {
                    var (full, bad) = Resolve(path);
                    if (full is null) return (bad, true);
                    bool list = call.Name == ListFolder;
                    using var hold = new TraceHold(); // the links shown on the card stay as they were until the call is done
                    // a link on the way is looked for first, without following it: through one, nothing is opened before the
                    // user has said yes. Otherwise the file is opened once and judged by what the handle says
                    // (RealPath.IsInside); really inside the chat's folder it is a look-up, as Claude Code's Read there;
                    // elsewhere 권한 decides, before a network path is touched at all
                    var trace = Trace(full, hold);
                    OpenedFile? file = null;
                    int error = 0;
                    bool inside = false;
                    if (RealPath.Kind(full) == PathKind.Local && !trace.Any && RealPath.Lexically(folder, full))
                    {
                        file = RealPath.OpenExisting(full, false, out error);
                        inside = file is null ? RealPath.Within(folder, full) : RealPath.IsInside(folder, file);
                    }
                    try
                    {
                        if (!inside)
                        {
                            if (Rule(ToolPermissions.ReadOutside) == ToolPermissions.Off) return (SwitchedOff, true);
                            string line = ToolText.Line(full);
                            string? really = RealPath.Kind(full) == PathKind.Local && !trace.Any ? file?.Final ?? RealPath.Of(full) : null;
                            var question = PathAsk(call, list ? Tr($"폴더 보기: {line}", $"View folder: {line}") : Tr($"파일 읽기: {line}", $"Read file: {line}"), full, really, ToolPermissions.ReadOutside, trace);
                            if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
                            if (file is null)
                            {
                                // through a link: the one that was shown, read again (Followed), and the file opened there
                                var (at, bound) = Followed(full, trace, really);
                                if (at is null) return (Changed(full), true);
                                file = RealPath.OpenExisting(at, false, out error);
                                // what is opened is what was shown
                                if (file is not null && bound is not null && !RealPath.Same(file.Final, bound)) return (Changed(full), true);
                            }
                        }
                        if (file is null) return (NotOpened(full, error, list), true);
                        return list ? List(file, full) : Read(file, full);
                    }
                    finally
                    {
                        file?.Dispose();
                    }
                }
            case WriteFile:
                {
                    var (full, bad) = Resolve(path);
                    if (full is null) return (bad, true);
                    string content = ApiJson.Str(a, "content"), line = ToolText.Line(full);
                    using var hold = new TraceHold();
                    var trace = Trace(full, hold);
                    var (held, really) = BeforeWriting(full, trace);
                    using (held)
                    {
                        var question = PathAsk(call, Tr($"파일 쓰기: {line}", $"Write file: {line}"), full, really, ToolPermissions.Write, trace, held, ToolText.Block(Tr("쓸 내용", "Content to write"), content));
                        if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
                        var (at, bound) = Followed(full, trace, really);
                        return at is null ? (Changed(full), true) : Write(held, at, bound, content);
                    }
                }
            case EditFile:
                {
                    var (full, bad) = Resolve(path);
                    if (full is null) return (bad, true);
                    string oldText = ApiJson.Str(a, "old_text"), newText = ApiJson.Str(a, "new_text"), line = ToolText.Line(full);
                    using var hold = new TraceHold();
                    var trace = Trace(full, hold);
                    var (held, really) = BeforeWriting(full, trace);
                    using (held)
                    {
                        // an edit needs a file that is there: refused before the card, so nothing is left to be swapped in meanwhile
                        if (held is null && RealPath.Kind(full) == PathKind.Local && !trace.Any) return ($"There is no file at {full} to edit; use {WriteFile} to make one.", true);
                        string change = ToolText.Block(Tr("바꾸기 전", "Before"), oldText) + "\n" + ToolText.Block(Tr("바꾼 후", "After"), newText);
                        if (!await Asked(PathAsk(call, Tr($"파일 수정: {line}", $"Edit file: {line}"), full, really, ToolPermissions.Edit, trace, held, change), stop).ConfigureAwait(false)) return (Declined, true);
                        var (at, bound) = Followed(full, trace, really);
                        return at is null ? (Changed(full), true) : Edit(held, at, bound, oldText, newText);
                    }
                }
            default: // RunCommand
                {
                    string command = ApiJson.Str(a, "command").Trim();
                    if (command.Length == 0) return ("command must not be empty.", true);
                    string line = ToolText.Line(command);
                    // the card shows the whole command (Detail); the ✓ line under the answer its first 80 characters
                    var question = new ToolAsk(call.Id, call.Name, call.Arguments, Tr($"명령 실행: {line}", $"Run command: {line}"), ToolPermissions.Command) { Detail = ToolText.Visible(command) };
                    if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
                    Directory.CreateDirectory(folder);
                    return await ApiCommand.RunAsync(command, folder, stop).ConfigureAwait(false);
                }
        }
    }

    /// A question about a path: the card shows it whole, and where it really leads when a link takes it elsewhere
    /// (`really`: told before, for a local path only — no file system call is made here, and none on a network path
    /// before the user has said yes), that a file already there is replaced and has other names, and `body` (a write's
    /// content, an edit's texts). `says` is the short line the ✓ history keeps.
    private static ToolAsk PathAsk(ApiToolCall call, string says, string full, string? really, string key, LinkTrace trace, OpenedFile? held = null, string body = "")
    {
        string detail = ToolText.Visible(full);
        if (trace.Any)
        {
            // nothing was opened to tell this: each link's own data says where it leads, one after the other
            for (var hop = trace; hop is { Any: true }; hop = hop.Then)
            {
                string link = ToolText.Visible(hop.Link!), target = hop.Target is null ? Tr("알 수 없는 위치", "Unknown location") : ToolText.Visible(hop.Target);
                detail += "\n" + Tr($"링크를 거칩니다: {link} → {target}", $"Follows link: {link} → {target}");
                if (hop.Remote) detail += "\n" + Tr("링크가 네트워크 경로나 장치로 이어집니다.", "The link points to a network path or device.");
            }
            // the file it comes to: what is opened after the answer, and only if the links are still these
            if (trace.End().Expected is { } ends)
            {
                string shown = ToolText.Visible(ends);
                detail += "\n" + Tr($"실제 위치: {shown}", $"Actual path: {shown}");
            }
        }
        if (really is not null && !RealPath.Same(really, full))
        {
            string shown = ToolText.Visible(really);
            detail += "\n" + Tr($"실제 위치: {shown}", $"Actual path: {shown}");
        }
        if (RealPath.Kind(full) == PathKind.Network) detail += "\n" + Tr("네트워크 경로입니다.", "This is a network path.");
        if (held is { IsDirectory: false })
        {
            detail += "\n" + Tr("이미 존재하는 파일입니다.", "File already exists.");
            if (held.Links > 1) detail += " " + Tr($"다른 이름이 {held.Links - 1}개 더 있어 다른 위치에서도 바뀝니다.", $"Has {held.Links - 1} other hard link(s), so it will also change in other locations.");
        }
        if (body.Length > 0) detail += "\n" + body;
        return new ToolAsk(call.Id, call.Name, call.Arguments, says, key) { Detail = detail };
    }

    /// The links on the way to `full`, found without following any (RealPath.Trace): below the chat's folder when it is
    /// inside it, else from the drive's root.
    private LinkTrace Trace(string full, TraceHold? hold) => RealPath.Trace(folder, full, hold);

    /// After the answer, the path to use and where the file opened there must be (null: not told): for a path with no link,
    /// the path itself and `really`. Through a link, the path is traced again and must give the very value the card showed
    /// (the same links, the same identities and targets), and the file is then the one at the target the card named. Null:
    /// something was replaced, or the target can't be named and leads off this PC.
    private (string? Path, string? Bound) Followed(string full, LinkTrace shown, string? really)
    {
        if (!shown.Any) return (full, really);
        if (Trace(full, null) != shown) return (null, null);
        var end = shown.End();
        if (end.Expected is not null) return (end.Expected, end.Expected);
        return end.Remote ? (null, null) : (full, null); // a reparse point whose target is not told: as shown, "somewhere unknown"
    }

    /// Before the user is asked about a write or edit, for a local path with no link on the way only: the file already
    /// there, opened (what is written later goes through this handle), and where the path really leads — the file's own final path, else its
    /// nearest existing ancestor's plus the rest.
    private static (OpenedFile? Held, string? Really) BeforeWriting(string full, LinkTrace trace)
    {
        if (RealPath.Kind(full) != PathKind.Local || trace.Any) return (null, null); // through a link: nothing opened before the answer
        var held = RealPath.OpenExisting(full, true, out _);
        return (held, held?.Final ?? RealPath.Of(full));
    }

    private static string Changed(string full) => $"{full} leads somewhere else than it did when you were asked, so nothing was done.";

    /// Why a path could not be opened (Windows error: 2 and 3 are "not there").
    private static string NotOpened(string full, int error, bool list) =>
        error is 2 or 3 ? (list ? $"There is no folder at {full}." : $"There is no file at {full}.") : $"Could not open {full}: {new System.ComponentModel.Win32Exception(error).Message}";

    /// The user's answer; a stopped turn answers no (and then throws, ending the loop).
    private async Task<bool> Asked(ToolAsk question, CancellationToken stop)
    {
        bool allowed = await ask(question).ConfigureAwait(false);
        stop.ThrowIfCancellationRequested();
        return allowed;
    }

    /// A path as the model wrote it → its full path (lexically, nothing looked at); or why not. A device namespace
    /// (\\?\, \\.\), a reserved device name and an alternate stream are refused: nothing here needs them.
    private (string? Full, string Error) Resolve(string path)
    {
        const string NotAPath = "path must be a file or folder path.", NoDevices = "Device paths (\\\\?\\, \\\\.\\, NUL, COM1…) and file streams are not supported.";
        if (path.Length == 0) return (null, NotAPath);
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path));
        }
        catch (NotSupportedException)
        {
            return (null, NoDevices); // a colon past the drive: a file stream
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException)
        {
            return (null, NotAPath);
        }
        return RealPath.Kind(full) == PathKind.Device ? (null, NoDevices) : (full, "");
    }

    private static (string, bool) Read(OpenedFile file, string full)
    {
        try
        {
            if (file.IsDirectory) return ($"{full} is a folder; use {ListFolder}.", true);
            using var stream = new FileStream(file.Handle, FileAccess.Read, 1);
            var bytes = new byte[Math.Min(stream.Length, MaxRead * 4L)];
            int n = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (Array.IndexOf(bytes, (byte)0, 0, n) >= 0) return ($"{full} is not a text file.", true);
            string text = Encoding.UTF8.GetString(bytes, 0, n);
            return text.Length > MaxRead || stream.Length > n ? (text[..Math.Min(text.Length, MaxRead)] + "\n… (the rest of the file is not shown)", false) : (text, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not read {full}: {e.Message}", true);
        }
    }

    private static (string, bool) List(OpenedFile file, string full)
    {
        try
        {
            if (!file.IsDirectory) return ($"There is no folder at {full}.", true);
            // read through the handle that was judged, never by opening its path again
            var entries = RealPath.Entries(file, MaxEntries + 1);
            var sb = new StringBuilder();
            foreach (var (name, folder, size) in entries.Take(MaxEntries))
                sb.Append(folder ? name + "\\" : $"{name} ({size:N0} bytes)").Append('\n');
            if (entries.Count > MaxEntries) sb.Append($"… (more than {MaxEntries} entries)");
            return (sb.Length == 0 ? "(empty)" : sb.ToString().TrimEnd(), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not list {full}: {e.Message}", true);
        }
    }

    /// The file to write once the user has said yes: the one opened before (`held`), else the path opened now — made if
    /// it is not there, not emptied — which must lead where the card said (`really`, null: not told, a network path).
    /// Null with the reason when it can't be had.
    private static (OpenedFile? File, string Error) ForWriting(OpenedFile? held, string full, string? really, bool mustExist)
    {
        if (held is not null) return (held, "");
        // nothing is made (a folder, an empty file) before the folder it goes into has been opened and checked: CreateIn
        var file = mustExist ? RealPath.OpenExisting(full, true, out int error) : RealPath.CreateIn(full, really, out error);
        if (file is null)
            return (null, error == RealPath.Diverged ? Changed(full) : mustExist ? NotOpened(full, error, false) : $"Could not write {full}: {new System.ComponentModel.Win32Exception(error).Message}");
        // it must lead where the card said; a file that was not there when the user was asked and now has other names was
        // put there since (a hard link to something elsewhere): nothing is written to it
        if ((really is not null && !RealPath.Same(file.Final, really)) || (!mustExist && file is { Created: false, Links: > 1 }))
        {
            file.Dispose();
            return (null, Changed(full));
        }
        return (file, "");
    }

    private static (string, bool) Write(OpenedFile? held, string full, string? really, string content)
    {
        var (file, why) = ForWriting(held, full, really, mustExist: false);
        if (file is null) return (why, true);
        try
        {
            if (file.IsDirectory) return ($"{full} is a folder.", true);
            // emptied only now, through the handle that was judged
            using var stream = new FileStream(file.Handle, FileAccess.Write, 1);
            stream.SetLength(0);
            stream.Write(new UTF8Encoding(false).GetBytes(content));
            return ($"Wrote {full} ({content.Length:N0} characters).", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not write {full}: {e.Message}", true);
        }
        finally
        {
            if (!ReferenceEquals(file, held)) file.Dispose();
        }
    }

    private static (string, bool) Edit(OpenedFile? held, string full, string? really, string oldText, string newText)
    {
        var (file, why) = ForWriting(held, full, really, mustExist: true);
        if (file is null) return (why, true);
        try
        {
            if (file.IsDirectory) return ($"{full} is a folder.", true);
            if (oldText.Length == 0) return ("old_text must not be empty.", true);
            using var stream = new FileStream(file.Handle, FileAccess.ReadWrite, 1);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            string text = new UTF8Encoding(false).GetString(bytes);
            int at = text.IndexOf(oldText, StringComparison.Ordinal);
            if (at < 0) return ("old_text is not in the file; read it again and copy the text exactly.", true);
            if (text.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal) >= 0) return ("old_text occurs more than once; include more of the text around it.", true);
            stream.Position = 0;
            stream.SetLength(0);
            stream.Write(new UTF8Encoding(false).GetBytes(text[..at] + newText + text[(at + oldText.Length)..]));
            return ($"Edited {full}.", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not edit {full}: {e.Message}", true);
        }
        finally
        {
            if (!ReferenceEquals(file, held)) file.Dispose();
        }
    }
}
