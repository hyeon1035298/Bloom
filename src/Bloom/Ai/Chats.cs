using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

internal sealed class ChatMessage
{
    public string Role { get; set; } = "user"; // user | assistant
    public string Text { get; set; } = "";
    public long At { get; set; }
    public bool Error { get; set; }
    public string? Page { get; set; } // title of attached text (clipboard, screen text)
    public string? Image { get; set; } // a screen picture sent with it (Bloom Lens), a PNG under Local\lens
    public List<string>? Files { get; set; } // user: the attached files (copies in the thread's in\); assistant: files it made (out\)
    public string? Sent { get; set; } // user: the message as any model got it (time, a page's contents; Files are added by each side); null = Text (older files)

    public List<string>? Done { get; set; } // what the AI did on the PC with the user's yes ("✓ 옮기기: …", "✕ …")
    public List<ApiStep>? Steps { get; set; } // assistant, API chats: the model calls of an answer that used tools, sent again when the chat resumes
    public ApiTokens? Tokens { get; set; } // assistant, API chats: the tokens the provider counted for the answer (all its calls), shown under it
    [System.Text.Json.Serialization.JsonIgnore] public bool Streaming;
    [System.Text.Json.Serialization.JsonIgnore] public string? Status;
    [System.Text.Json.Serialization.JsonIgnore] public readonly List<ToolAsk> Asks = new(); // waiting for the user
}

internal sealed class ChatThread
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = Tr("새 대화", "New chat");
    public string Model { get; set; } = "sonnet"; // sonnet | opus | haiku | chatgpt:<Codex slug> | api:<provider>:<model> (AiModels)
    public string? Effort { get; set; } // a level of AiModels.Efforts(Model); null = the settings' level
    public string? SessionId { get; set; }
    public string? SessionProvider { get; set; } // whose session SessionId is (null: claude, older files)
    public string? SessionAccount { get; set; } // the AiAccount it lives in (null: the default one, older files)
    public long Created { get; set; }
    public long Updated { get; set; }
    public string? Group { get; set; } // ChatGroup.Id; null = not in a group
    public List<ChatMessage> Messages { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] public bool Busy => Messages.Count > 0 && Messages[^1].Streaming;
    [System.Text.Json.Serialization.JsonIgnore] public readonly HashSet<string> AlwaysAllowed = new(); // 이 대화에서 항상 허용
}

/// A folder of conversations in the AI chat window (chat-groups.json, in the order they were made).
internal sealed class ChatGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Folded { get; set; }
}

/// Conversations (one JSON file each under chats\) and their CLI sessions. A session left unused for
/// Settings.AiIdleMinutes (10 by default, 0 = never) is closed to give its memory back; the next message resumes it by id. A new
/// model or level restarts the session (resumed when the provider stays); a switch between Claude
/// and ChatGPT starts a new one that gets the conversation so far with the next message.
internal sealed partial class Chats
{
    private readonly Dispatcher ui;
    private readonly Dictionary<string, IChatSession> sessions = new();
    private readonly DispatcherTimer idle;
    private readonly string dir;
    private readonly ChatStore store;
    private bool shutdown;
    private readonly Func<ChatThread, string, IChatSession>? sessionFactory;
    private readonly string filesRoot;
    private readonly Func<string, IReadOnlyList<string>, Func<List<string>, string>, CancellationToken, Task<PreparedAttachments>> prepareAttachments;
    private sealed class Preparing(ChatMessage answer)
    {
        public readonly ChatMessage Answer = answer;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Sending;
    }
    private readonly Dictionary<string, Preparing> preparing = new();
    private readonly Dictionary<string, Task> deletingFiles = new();
    private readonly Dictionary<string, List<Task>> retiredPreparations = new();
    private sealed class Finishing(ChatMessage answer, bool immediate)
    {
        public readonly ChatMessage Answer = answer;
        public readonly bool Immediate = immediate;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SaveAsk; // the request id of the card asking whether to save the answer's file: blocks (AskToSave), while it waits
    }
    private readonly Dictionary<string, Finishing> finishing = new();
    private readonly Dictionary<string, Task> outputWork = new();
    private readonly Func<ChatOutputRequest, CancellationToken, Task<ChatOutputResult>> collectOutput;

    public readonly List<ChatThread> Threads = new();

    /// UI thread; the thread whose messages changed (streaming text, status, end of turn).
    public event Action<ChatThread>? Updated;

    /// UI thread; a CLI session started or ended (the chat window's live marks).
    public event Action? SessionsChanged;

    /// Every open CLI session ends (a switch that changes how sessions start); the conversations stay.
    public void EndAll()
    {
        CancelPending();
        foreach (string id in warm.Keys.ToList()) DropWarm(id);
        foreach (var t in Threads.Where(t => IsLive(t) || preparing.ContainsKey(t.Id) || finishing.ContainsKey(t.Id)).ToList()) EndSession(t);
    }

    /// The conversation has a running CLI session (it answers at once; 세션 종료 gives its memory back). A pre-started
    /// process nobody has sent to isn't one yet.
    public bool IsLive(ChatThread t) => sessions.TryGetValue(t.Id, out var s) && s.Alive && !warm.ContainsKey(t.Id) || liveForQa.Contains(t.Id);

    private readonly HashSet<string> liveForQa = new();

    /// QA pictures: the conversation shows as having an open session (nothing runs).
    public void SetLiveForQa(ChatThread t, bool live)
    {
        if (live) liveForQa.Add(t.Id);
        else liveForQa.Remove(t.Id);
    }

    /// 세션 종료: the conversation's CLI process ends now (an answer being written stops); the conversation stays and its
    /// next message resumes the session by id.
    public void EndSession(ChatThread t)
    {
        bool collectFiles = !preparing.TryGetValue(t.Id, out var preparation) || preparation.Sending;
        CancelPreStart(t);
        bool completing = CancelFinish(t);
        if (completing) collectFiles = false;
        bool pending = CancelPreparation(t);
        bool removed = sessions.Remove(t.Id, out var s);
        if (!pending && !removed && !completing) return;
        s?.Dispose();
        if (t.Busy) Finish(t, t.Messages[^1], false, Tr("세션 종료 · 답변 중지됨", "Session ended · answer stopped"), collectFiles, immediate: true);
        SessionsChanged?.Invoke();
    }

    public Chats(Dispatcher ui, Func<ChatThread, string, IChatSession>? sessionFactory = null, string? directory = null,
        Func<string, IReadOnlyList<string>, Func<List<string>, string>, CancellationToken, Task<PreparedAttachments>>? prepareAttachments = null,
        Func<ChatOutputRequest, CancellationToken, Task<ChatOutputResult>>? collectOutput = null)
    {
        this.ui = ui;
        store = new ChatStore(ui);
        this.collectOutput = collectOutput ?? ((request, token) => Task.FromResult(ChatOutput.Process(request, token)));
        this.sessionFactory = sessionFactory;
        this.prepareAttachments = prepareAttachments ?? PreparedAttachments.Copy;
        dir = directory ?? Paths.In("chats");
        filesRoot = directory is null ? Path.Combine(Paths.Local, "ai", "files") : Path.Combine(Path.GetDirectoryName(directory)!, "files");
        Directory.CreateDirectory(dir);
        foreach (string f in Directory.GetFiles(dir, "*.json"))
        {
            var t = Json.Load<ChatThread>(f);
            if (t.Id.Length > 0) Threads.Add(t);
        }
        Threads.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        LoadGroups();
        idle = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMinutes(1) };
        idle.Tick += (_, _) => ReapIdle();
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public ChatThread? Latest => Threads.Count > 0 ? Threads[0] : null;

    public ChatThread New()
    {
        string model = AiModels.DefaultModel();
        var t = new ChatThread { Id = Guid.NewGuid().ToString("N")[..12], Model = model, Effort = AiModels.DefaultEffort(model), Created = Now, Updated = Now };
        return t; // saved (and listed) once it has a message
    }

    /// The thread's level, one its model takes.
    public static string EffortOf(ChatThread t) => t.Effort is { } e ? AiModels.Fit(t.Model, e) : AiModels.DefaultEffort(t.Model);

    /// The conversation goes on with this model from its next message; it also becomes the default for new ones. The
    /// other side (Claude ↔ ChatGPT ↔ omp ↔ API) keeps its own level; the same side keeps the thread's, fitted to the model.
    public static void SetModel(ChatThread? t, string model)
    {
        bool sideChanged = AiModels.Provider(AiModels.Normalize(t?.Model ?? AiModels.DefaultModel())) != AiModels.Provider(model);
        var s = Settings.Current;
        s.AiProvider = AiModels.Provider(model);
        if (AiModels.OmpSlug(model) is { } ompSlug) s.OmpChatModel = ompSlug;
        else if (AiModels.GptSlug(model) is { } slug) s.GptModel = slug;
        else if (AiModels.IsApi(model)) s.ApiModel = model;
        else if (!AiModels.IsChatGpt(model)) s.AiModel = model;
        if (t is not null)
        {
            // a model on the user's own omp list may carry the level a chat starts at with it
            bool ownLevel = t.Model != model && AiModels.OmpSlug(model) is { } own && OmpModelList.EffortOf(own) is not null;
            t.Model = model;
            t.Effort = sideChanged || t.Effort is null || ownLevel ? AiModels.DefaultEffort(model) : AiModels.Fit(model, t.Effort);
        }
        Settings.Save();
    }

    /// A thinking level for the conversation, also the default on its side.
    public static void SetEffort(ChatThread? t, string level)
    {
        if (t is not null) t.Effort = level;
        string model = AiModels.Normalize(t?.Model ?? AiModels.DefaultModel());
        if (AiModels.IsOmp(model)) Settings.Current.OmpChatEffort = level;
        else if (AiModels.IsChatGpt(model)) Settings.Current.GptEffort = level;
        else if (AiModels.IsApi(model)) Settings.Current.ApiEffort = level;
        else Settings.Current.AiEffort = level;
        Settings.Save();
    }

    public void Send(ChatThread t, string text, string? page = null, string? pageTitle = null, string? image = null, IReadOnlyList<string>? files = null)
    {
        if (shutdown || t.Busy) return;
        Task deletion = Task.WhenAll(WhenDeleted(t), WhenFinished(t));
        if (!Threads.Contains(t)) Threads.Insert(0, t);
        if (t.Messages.Count == 0) t.Title = TitleOf(text, pageTitle ?? (files is { Count: > 0 } ? Path.GetFileName(files[0]) : null));
        // an omp chat's slash command goes to omp as it is (omp reads one only at the very start of the message) when it is
        // read-only; one that would change the omp shared with every other session is answered here; any other "/word" is a message
        string? command = AiModels.Provider(t.Model) == "omp" && page is null && image is null && files is not { Count: > 0 } && OmpCli.IsSlash(text.Trim())
            ? OmpCli.SlashName(text) : null;
        bool slash = command is not null && OmpCli.SlashAllowed.Contains(command);
        string prompt = slash ? text.Trim() : ClaudeSession.LocalTime() + "\n" + (page is null ? text : $"<page title=\"{Esc(pageTitle ?? "")}\">\n{page}\n</page>\n\n{text}");
        // kept with the message so an API session started later sends it again as it went now (ApiSession.Past)
        var user = new ChatMessage { Role = "user", Text = text, At = Now, Page = pageTitle, Image = image, Sent = prompt };
        t.Messages.Add(user);
        var answer = new ChatMessage { Role = "assistant", At = Now, Streaming = true, Status = Tr("생각하는 중…", "Thinking…") };
        t.Messages.Add(answer);
        if (command is not null && OmpLocalAnswer(command) is { } said)
        {
            answer.Text = said;
            Touch(t);
            Updated?.Invoke(t);
            Finish(t, answer, true, null);
            return;
        }
        var pending = new Preparing(answer);
        preparing[t.Id] = pending;
        Touch(t);
        Updated?.Invoke(t);

        string[] sources = files?.Take(MaxFiles).ToArray() ?? Array.Empty<string>();
        string folder = ThreadFiles(t);
        bool inline = AiModels.Inline(t.Model);
        _ = PrepareAndSend(t, user, pending, prompt, image, sources, folder, inline, UnavailableNote(), deletion, slash);
    }

    /// What Bloom answers itself to an omp slash command it doesn't send: where earlier chats are (/resume), or that the
    /// command changes the omp every session of the user shares (SlashRefused); null = it goes to omp.
    internal static string? OmpLocalAnswer(string command)
    {
        if (command == "resume")
            // each Bloom conversation keeps its own omp session: an older one is picked from the list, not swapped in here
            return Tr("Bloom에서는 대화마다 omp 세션이 따로 있습니다 — 지난 대화는 **왼쪽 목록**(또는 `Ctrl+F` 대화 찾기)에서 고르면 그 세션으로 이어집니다. 터미널 omp의 세션은 omp에서 `/resume`으로 여세요.",
                "Each Bloom conversation has its own omp session — pick an earlier one in the **list on the left** (or `Ctrl+F`) and it carries on in that session. Sessions from omp in a terminal open with `/resume` there.");
        if (!OmpCli.SlashRefused.Contains(command)) return null;
        return Tr($"`/{command}` 명령은 Bloom 채팅에서 쓰지 않습니다 — omp 설정은 다른 omp 세션과 함께 쓰이므로, 설정을 바꾸는 명령은 omp 터미널에서 실행하세요. 여기서 쓸 수 있는 명령: ",
            $"`/{command}` isn't used in a Bloom chat — omp's settings are shared with your other omp sessions, so commands that change them belong in an omp terminal. Here you can use: ")
            + string.Join(" ", OmpCli.SlashAllowed.Select(c => "`/" + c + "`"));
    }

    /// What an inlined file that can't be read becomes ("{0}" = its name), for the model to tell the user.
    internal static string UnavailableNote()
    {
        string nameSlot = "{0}";
        return Tr($"\n\n<attachment name=\"{nameSlot}\">(이 형식은 여기서 열 수 없어요 — 내용을 읽지 못했다고 알려 주세요)</attachment>", $"\n\n<attachment name=\"{nameSlot}\">(This format can't be opened here — tell the user its contents didn't come through)</attachment>");
    }

    internal Task WhenPrepared(ChatThread t) => preparing.TryGetValue(t.Id, out var pending) ? pending.Completion.Task : Task.CompletedTask;
    internal Task WhenDeleted(ChatThread t) => deletingFiles.TryGetValue(t.Id, out var pending) ? pending : Task.CompletedTask;
    internal Task WhenFinished(ChatThread t) => outputWork.TryGetValue(t.Id, out var pending) ? pending : Task.CompletedTask;

    private async Task PrepareAndSend(ChatThread t, ChatMessage user, Preparing pending, string prompt,
        string? image, string[] sources, string folder, bool inline, string unavailable, Task deletion, bool slash = false)
    {
        PreparedAttachments? attached = null;
        IChatSession? session = null;
        bool Current() => preparing.TryGetValue(t.Id, out var current) && ReferenceEquals(current, pending);
        try
        {
            var token = pending.Cancellation.Token;
            await deletion.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (sources.Length > 0)
                attached = await Task.Run(() => prepareAttachments(folder, sources, inline ? list => Inline(list, unavailable) : _ => "", token), token).ConfigureAwait(false);
            var images = new List<string>();
            if (image is not null) images.Add(image);
            if (attached is not null) images.AddRange(attached.Files.Where(f => ImageType(f) is not null));
            await OnUi(() => Current() && AiModels.IsChatGpt(t.Model) ? GptCatalog.RefreshAsync(token) : Task.FromResult(true)).ConfigureAwait(false);
            var started = await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(new SessionStart(false));
                if (attached is { Files.Count: > 0 })
                {
                    user.Files = attached.Files;
                    attached.Accept();
                    prompt += inline ? attached.Inline : "\n\n<attachments>\n" + string.Join("\n", attached.Files.Select(f => "- " + FilesRel(t) + "/" + Path.GetRelativePath(folder, f).Replace('\\', '/'))) + "\n</attachments>";
                    Save(t);
                    Updated?.Invoke(t);
                }
                if (!Current()) return Task.FromResult(new SessionStart(false));
                session = SessionFor(t, out string? resume, out bool fresh, out bool start);
                if (!Current()) return Task.FromResult(new SessionStart(false));
                var preStarted = TakeWarm(t, session);
                if (fresh && !slash) prompt = Earlier(t) + prompt; // a slash command is read by omp only at the very start of the message
                turnStarted[t.Id] = DateTime.UtcNow.AddSeconds(-1);
                if (Settings.Current.AiTitles && !slash) leads[t.Id] = new ChatTitles.Lead();
                else leads.Remove(t.Id);
                return start ? session.StartAsync(resume, token) : preStarted is not null ? preStarted.Start.WaitAsync(token) : Task.FromResult(new SessionStart(true));
            }).ConfigureAwait(false);
            bool sent = false;
            if (started.Success)
                sent = await OnUi(() =>
                {
                    if (!Current() || session is null) return Task.FromResult(false);
                    pending.Sending = true;
                    return session.SendAsync(prompt, images, token);
                }).ConfigureAwait(false);
            await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(false);
                preparing.Remove(t.Id);
                if (!started.Success && session is not null && sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, session))
                {
                    sessions.Remove(t.Id);
                    session.Dispose();
                }
                if (!sent) Finish(t, pending.Answer, false, started.Error ?? Tr("메시지를 보내지 못했습니다. 다시 보내세요.", "Could not send the message. Send it again."), pending.Sending);
                else idle.Start();
                SessionsChanged?.Invoke();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                await OnUi(() =>
            {
                if (!Current()) return Task.FromResult(false);
                preparing.Remove(t.Id);
                if (sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, session))
                {
                    sessions.Remove(t.Id);
                    current.Dispose();
                }
                Finish(t, pending.Answer, false, e is OperationCanceledException ? AiErrors.Stopped : e.Message, pending.Sending);
                SessionsChanged?.Invoke();
                return Task.FromResult(true);
            }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ui.HasShutdownStarted) { }
        }
        finally
        {
            attached?.Dispose();
            pending.Cancellation.Dispose();
            pending.Completion.TrySetResult();
        }
    }

    private Task<T> OnUi<T>(Func<Task<T>> action) => ui.HasShutdownStarted
        ? Task.FromCanceled<T>(new CancellationToken(true))
        : ui.CheckAccess() ? action() : ui.InvokeAsync(action).Task.Unwrap();

    private bool CancelPreparation(ChatThread t)
    {
        if (!preparing.Remove(t.Id, out var pending)) return false;
        foreach (string id in retiredPreparations.Keys.ToArray())
        {
            retiredPreparations[id].RemoveAll(task => task.IsCompleted);
            if (retiredPreparations[id].Count == 0) retiredPreparations.Remove(id);
        }
        if (!retiredPreparations.TryGetValue(t.Id, out var retired)) retiredPreparations[t.Id] = retired = new();
        retired.Add(pending.Completion.Task);
        _ = pending.Cancellation.CancelAsync();
        return true;
    }

    private string ThreadFiles(ChatThread t) => Path.Combine(filesRoot, t.Id);

    private static string Esc(string s) => s.Replace("\"", "'");

    // ── files in a conversation ─────────────────────────────────────────────────────────────────
    /// Attached files: at most this many per message, each at most this big.
    public const int MaxFiles = 10;
    public const long MaxFileBytes = 50L * 1024 * 1024;

    private readonly Dictionary<string, DateTime> turnStarted = new();

    private readonly Dictionary<string, ChatTitles.Lead> leads = new(); // the start of each answer, for its title line

    /// The answer's text without its title line; a finished title line renames the conversation.
    private string Shown(ChatThread t, string piece)
    {
        if (!leads.TryGetValue(t.Id, out var lead)) return piece;
        string shown = lead.Take(piece, out string? field, out string? task);
        if (task is not null) t.Title = ChatTitles.Compose(t.Model, field ?? "", task);
        return shown;
    }

    /// The thread's folder as the sessions see it (relative to Local\ai) and on disk.
    public static string FilesRel(ChatThread t) => "files/" + t.Id;

    public static string FilesDir(ChatThread t) => Path.Combine(Paths.Local, "ai", "files", t.Id);

    /// The media type Claude takes for a picture, or null when the file is not one.
    public static string? ImageType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    /// ChatGPT reads no files itself: text files go into the message (each up to 200 KB), pictures as images, and the
    /// rest is named so it can say it could not open them.
    internal static string Inline(List<string> attached, string unavailable)
    {
        var sb = new StringBuilder();
        foreach (string f in attached)
        {
            string name = Path.GetFileName(f);
            if (ImageType(f) is not null) continue;
            if (TextOf(f) is { } text) sb.Append($"\n\n<attachment name=\"{Esc(name)}\">\n{text}\n</attachment>");
            else sb.Append(unavailable.Replace("{0}", Esc(name), StringComparison.Ordinal));
        }
        return sb.ToString();
    }

    /// A file's text when it is text (UTF-8, no NUL, at most 200 KB), else null.
    private static string? TextOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 200 * 1024) return null;
            byte[] bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// The conversation so far, for a session that has not seen it (Claude ↔ ChatGPT).
    private static string Earlier(ChatThread t)
    {
        var sb = new StringBuilder();
        foreach (var m in t.Messages.Take(t.Messages.Count - 2).Where(m => !m.Error && m.Text.Length > 0).TakeLast(16))
            sb.Append(m.Role == "user" ? "User: " : "Assistant: ").Append(m.Text.Length > 2000 ? m.Text[..2000] + "…" : m.Text).Append("\n\n");
        return sb.Length == 0 ? "" : "<earlier_conversation>\n" + sb.ToString().TrimEnd() + "\n</earlier_conversation>\n\n";
    }

    private static string TitleOf(string text, string? page)
    {
        string first = text.Trim().Split('\n')[0].Trim();
        if (first.Length == 0 && page is not null) first = page;
        return first.Length > 42 ? first[..42] + "…" : first.Length == 0 ? Tr("새 대화", "New chat") : first;
    }

    /// Off: ChatGPT chats run one `codex exec` per turn as before (WarmBench compares the two).
    internal bool CodexAppServer = true;

    /// A ChatGPT conversation's session: a long-lived `codex app-server`, on `codex exec` when that won't start.
    private IChatSession ChatGptSession(ChatThread t, string effort, string? home)
    {
        CodexSession Exec() => new(t.Model, effort, FilesRel(t), t.Id) { AccountHome = home };
        return CodexAppServer ? new CodexAppSession(t.Model, effort, t.Id, Exec) { AccountHome = home } : Exec();
    }

    /// `warming`: the pre-start (Chats.PreStart) makes the conversation's process ahead of its message; it is listed as
    /// warm and not as a live session until the message takes it over.
    private IChatSession SessionFor(ChatThread t, out string? resume, out bool fresh, out bool start, bool warming = false)
    {
        resume = null;
        fresh = false;
        start = false;
        string effort = EffortOf(t), provider = AiModels.Provider(t.Model);
        var account = AiAccounts.Chat(provider);
        sessions.TryGetValue(t.Id, out var s);
        if (s is not null && warm.TryGetValue(t.Id, out var preStarted) && ReferenceEquals(preStarted.Session, s))
        {
            if (s.Model == t.Model && s.Effort == effort && Usable(preStarted) && preStarted.Print == PrintOf(t))
            {
                resume = preStarted.Resume; // started with it already; the earlier conversation is still owed when it had no session to resume
                fresh = resume is null && t.Messages.Count > 2;
                return s;
            }
            DropWarm(t.Id); // it has died, or was launched for other inputs than this send's: a new one starts
            s = null;
        }
        if (s is { Alive: true } && s.Model == t.Model && s.Effort == effort) return s; // 설정's account change ends them (EndAll)
        if (s is not null)
        {
            if (!DropWarm(t.Id)) s.Dispose();
            sessions.Remove(t.Id);
        }
        s = sessionFactory is not null ? sessionFactory(t, effort)
            : Demo.On ? new DemoSession(t.Model, effort)
            : provider == "api" ? new ApiSession(t.Model, effort, t.Id, ApiSession.Past(t.Messages), UnavailableNote(), ThreadFiles(t))
            : provider == "omp" ? new OmpSession(t.Model, effort, t.Id)
            : provider == "chatgpt" ? ChatGptSession(t, effort, account.Home)
            : new ClaudeSession(t.Model, effort, FilesRel(t), "Bloom · " + (t.Messages.Count > 0 ? (t.Title.Length > 40 ? t.Title[..40] + "…" : t.Title) : Tr("새 대화", "New chat"))) { AccountHome = account.Home };
        // a session lives in its account's folder: under another account it starts fresh, with the chat so far given to it;
        // an API session always starts from the saved chat itself (ApiSession.Past), so it is neither resumed nor told it
        bool api = provider == "api";
        resume = ResumeFor(t, provider, account.Id);
        fresh = !api && resume is null && t.Messages.Count > 2;
        bool IsCurrent() => sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, s);
        ChatMessage? Current() => IsCurrent() && !finishing.ContainsKey(t.Id) && t.Messages.Count > 0 && t.Messages[^1].Role == "assistant" && t.Messages[^1].Streaming ? t.Messages[^1] : null;
        bool needBreak = false;
        var events = new ChatEventPump(ui);
        s.SessionStarted += id => events.Post(() =>
        {
            if (!IsCurrent()) return;
            if (warm.TryGetValue(t.Id, out var waiting) && ReferenceEquals(waiting.Session, s))
            {
                waiting.InitId = id; // nothing has been sent: the conversation keeps its own id until a turn happens
                return;
            }
            t.SessionId = id;
            t.SessionProvider = provider;
            t.SessionAccount = account.Id;
            Save(t);
        });
        void Append(string d)
        {
            if (Current() is not { } m) return;
            d = Shown(t, d);
            if (d.Length == 0) return;
            if (needBreak && m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            needBreak = false;
            m.Text += d;
            m.Status = null;
            Updated?.Invoke(t);
        }
        s.TextDelta += d => events.PostText(d, Append);
        s.AssistantText += whole => events.Post(() =>
        {
            if (Current() is not { } m) return;
            whole = Shown(t, whole);
            if (whole.Length == 0) return;
            if (m.Text.Length > 0 && !m.Text.EndsWith('\n')) m.Text += "\n\n";
            m.Text += whole;
            Updated?.Invoke(t);
        });
        s.Status += st => events.Post(() =>
        {
            if (Current() is not { } m) return;
            if (st is not null) needBreak = true;
            m.Status = st;
            Updated?.Invoke(t);
        });
        s.TurnEnded += (ok, err) => events.Post(() =>
        {
            if (Current() is { } m) Finish(t, m, ok, err);
        });
        // omp's numbers (how full the model's memory is) and its tidying of it, as a line under the answer, kept with it
        if (s is IContextGauge gauge)
        {
            gauge.UsageChanged += () => events.Post(() =>
            {
                if (gauge.Usage is { } u) usages[t.Id] = u;
                if (gauge.Compacting) compacting.Add(t.Id);
                else compacting.Remove(t.Id);
                UsageChanged?.Invoke(t);
            });
            gauge.CompactNote += (text, running) => events.Post(() =>
            {
                var m = Current() ?? (running ? null : t.Messages.LastOrDefault(x => x.Role == "assistant"));
                if (m is null) return;
                if (running) m.Status = text; // the line under the answer being written
                else
                {
                    if (m.Streaming) m.Status = null;
                    (m.Done ??= new()).Add("ℹ " + text);
                    Save(t);
                }
                Updated?.Invoke(t);
            });
        }
        s.ToolAsked += raw => events.Post(() => Asked(t, Current(), s, raw));
        s.ToolAskWithdrawn += id => events.Post(() =>
        {
            if (Current() is { } m && m.Asks.RemoveAll(a => a.RequestId == id) > 0) Updated?.Invoke(t);
        });
        // an API answer's tool calls, kept with it before it finishes (TurnEnded comes after) so the chat's file has them
        if (s is ApiSession apiSession)
        {
            apiSession.StepsDone += steps => events.Post(() =>
            {
                if (Current() is { } m) m.Steps = steps;
            });
            apiSession.Counted += tokens => events.Post(() =>
            {
                if (Current() is { } m) m.Tokens = tokens;
            });
            // a note about its tools (an MCP server left out) as a line under the answer, kept with it
            apiSession.Noted += note => events.Post(() =>
            {
                if (Current() is not { } m) return;
                (m.Done ??= new()).Add("ℹ " + note);
                Updated?.Invoke(t);
            });
        }
        s.Exited += _ => events.Post(() =>
        {
            // a pre-started process that ended on its own (its sign-in changed) is dropped as any: it counts until it is gone
            if (warm.TryGetValue(t.Id, out var idleOne) && ReferenceEquals(idleOne.Session, s))
            {
                DropWarm(t.Id);
                return;
            }
            if (sessions.TryGetValue(t.Id, out var cur) && ReferenceEquals(cur, s) && sessions.Remove(t.Id)) SessionsChanged?.Invoke();
        });
        start = true;
        if (warming) warm[t.Id] = new WarmSession(s, resume, provider, account.Id, PrintOf(t));
        sessions[t.Id] = s;
        if (!warming) SessionsChanged?.Invoke();
        return s;
    }

    private void Finish(ChatThread t, ChatMessage m, bool ok, string? error, bool collectFiles = true, bool immediate = false)
    {
        if (finishing.ContainsKey(t.Id)) return;
        m.Status = null;
        m.Asks.Clear();
        if (leads.Remove(t.Id, out var lead)) m.Text += lead.Flush();
        // an API answer with nothing in it (a title line only, once stripped) and no tool call is no answer
        if (ok && AiModels.IsApi(t.Model) && m.Text.Trim().Length == 0 && m.Steps is not { Count: > 0 })
        {
            ok = false;
            error = AiErrors.ApiNoAnswer(AiModels.ApiProviderOf(t.Model)?.Name ?? "API");
        }
        DateTime? since = turnStarted.Remove(t.Id, out var started) ? started : null;
        // ```file: blocks are saved only for an answer that finished, and as 파일 쓰기 (the 권한) says: 사용 안 함 saves none,
        // 매번 묻기 asks first (below) — replacing a file included — and 항상 허용 saves
        string write = ToolPermissions.Of(ToolPermissions.Write);
        bool fenced = ok && write != ToolPermissions.Off && AiModels.Inline(t.Model) && m.Text.Contains("```file:", StringComparison.Ordinal);
        if (!collectFiles || (since is null && !fenced))
        {
            Complete(t, m, ok, error);
            return;
        }
        var pending = new Finishing(m, immediate);
        finishing[t.Id] = pending;
        foreach (string id in outputWork.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray()) outputWork.Remove(id);
        outputWork[t.Id] = pending.Completion.Task;
        if (immediate) Complete(t, m, ok, error);
        string nameSlot = "{0}";
        string saved = Tr($"**{nameSlot}** — 파일 저장 완료", $"**{nameSlot}** — file saved");
        var snapshot = new ChatOutputRequest(m.Text, Path.Combine(ThreadFiles(t), "out"), since, fenced, saved);
        if (fenced && write == ToolPermissions.Ask && !t.AlwaysAllowed.Contains(ToolPermissions.Write))
        {
            AskToSave(t, pending, snapshot, ok, error);
            return;
        }
        _ = FinishOutput(t, pending, snapshot, ok, error);
        Updated?.Invoke(t);
    }

    /// What an answer's file: blocks wait on when 파일 쓰기 is 매번 묻기: a card listing the files (and which replace one),
    /// whose answer carries on with the finish — yes saves them, no leaves the blocks in the text. The finish stays open
    /// meanwhile (a stop ends it, CancelFinish).
    private void AskToSave(ChatThread t, Finishing pending, ChatOutputRequest request, bool ok, string? error)
    {
        var files = ChatOutput.FencedFiles(request.Text);
        // the card shows the files whole or they are not asked about (the same budget as a tool call's card, Asked): files
        // too big for it are not saved, and the blocks stay in the text
        var detail = new StringBuilder();
        bool fits = files.Sum(f => (long)f.Name.Length + f.Content.Length) <= ToolText.MaxPayload;
        if (fits)
            foreach (var (name, content) in files)
            {
                bool replaces = File.Exists(Path.Combine(request.Directory, name));
                detail.Append(detail.Length > 0 ? "\n" : "").Append(ToolText.Block(replaces ? Tr($"{name} · 이미 있는 파일을 덮어씁니다", $"{name} · replaces the file that is there") : Tr($"{name} · 새 파일", $"{name} · a new file"), content));
            }
        if (!fits || detail.Length > ToolText.MaxPayload * 2)
        {
            (pending.Answer.Done ??= new()).Add("✕ " + Tr("승인 카드에 다 보여 줄 수 없을 만큼 커서 거절했습니다", "Declined: too big to show in full on the approval card"));
            _ = FinishOutput(t, pending, request with { Fenced = false }, ok, error);
            return;
        }
        string names = ToolText.Line(string.Join(", ", files.Select(f => f.Name)));
        var ask = new ToolAsk(Guid.NewGuid().ToString("N"), "file", "{}", Tr($"파일 저장: {names}", $"Save files: {names}"), ToolPermissions.Write) { Detail = detail.ToString() };
        pending.SaveAsk = ask.RequestId;
        saveAsks[ask.RequestId] = allow =>
        {
            if (!finishing.TryGetValue(t.Id, out var current) || !ReferenceEquals(current, pending)) return; // stopped or ended meanwhile
            pending.SaveAsk = null;
            _ = FinishOutput(t, pending, allow ? request : request with { Fenced = false }, ok, error);
        };
        // a finish that ends here (a stop, the session's end, the chat deleted) completes at once: whatever waits for it goes on
        pending.Cancellation.Token.Register(() => pending.Completion.TrySetResult());
        pending.Answer.Asks.Add(ask);
        pending.Answer.Status = Tr("승인 대기", "Approval pending");
        Updated?.Invoke(t);
        AskWaiting?.Invoke(t, ask);
    }

    /// The answers to the save questions (AskToSave) still waiting, by request id.
    private readonly Dictionary<string, Action<bool>> saveAsks = new();

    private async Task FinishOutput(ChatThread t, Finishing pending, ChatOutputRequest snapshot, bool ok, string? error)
    {
        ChatOutputResult? result = null;
        try
        {
            try
            {
                result = await Task.Run(() => collectOutput(snapshot, pending.Cancellation.Token), pending.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                Log.Error("chat output", e);
                ok = false;
                error = e.Message;
            }
            if (pending.Cancellation.IsCancellationRequested) return;
            await OnUi(() =>
            {
                if (shutdown || !finishing.TryGetValue(t.Id, out var current) || !ReferenceEquals(current, pending)) return Task.FromResult(false);
                finishing.Remove(t.Id);
                if (result is not null)
                {
                    pending.Answer.Text = result.Text;
                    pending.Answer.Files = result.Files?.ToList();
                }
                if (pending.Immediate)
                {
                    Save(t);
                    Updated?.Invoke(t);
                }
                else Complete(t, pending.Answer, ok, error);
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ui.HasShutdownStarted) { }
        finally
        {
            pending.Cancellation.Dispose();
            pending.Completion.TrySetResult();
        }
    }

    private bool CancelFinish(ChatThread t)
    {
        if (!finishing.Remove(t.Id, out var pending)) return false;
        // a save question still waiting goes with the finish it belongs to — its card too — on every way out (a stop, the
        // session's end, the chat deleted): a late click then finds nothing and saves nothing
        if (pending.SaveAsk is { } id)
        {
            saveAsks.Remove(id);
            pending.Answer.Asks.RemoveAll(a => a.RequestId == id);
            if (pending.Answer.Asks.Count == 0 && pending.Answer.Streaming) pending.Answer.Status = null;
            pending.SaveAsk = null;
        }
        _ = pending.Cancellation.CancelAsync();
        return true;
    }

    private void Complete(ChatThread t, ChatMessage m, bool ok, string? error)
    {
        m.Streaming = false;
        m.Status = null;
        m.Asks.Clear();
        if (!ok)
        {
            m.Error = true;
            // an API session's errors are already worded (ApiClient) and name their provider
            string e = error is null ? AiErrors.Failed : AiModels.IsApi(t.Model) ? error : AiModels.IsOmp(t.Model) ? AiErrors.Omp(error, details: true) : AiErrors.Friendly(error, AiModels.IsChatGpt(t.Model), details: true);
            m.Text = m.Text.Length > 0 ? m.Text + "\n\n" + e : e;
        }
        Touch(t);
        Updated?.Invoke(t);
    }

    // ── how full the model's memory is (omp chats only) ─────────────────────────────────────────
    private readonly Dictionary<string, OmpUsage> usages = new();
    private readonly HashSet<string> compacting = new();

    /// UI thread; the numbers of this conversation's omp session changed, or a tidy-up of it started or ended.
    public event Action<ChatThread>? UsageChanged;

    /// What the model holds in this conversation (read after each turn); null for a chat on another side, or before omp has said.
    public OmpUsage? UsageOf(ChatThread t) => AiModels.Provider(t.Model) == "omp" && usages.TryGetValue(t.Id, out var u) ? u : null;

    public bool IsCompacting(ChatThread t) => compacting.Contains(t.Id);

    /// Asks a running omp session of this conversation for its numbers (opening a chat shows them at once); starts nothing.
    public void RefreshUsage(ChatThread t)
    {
        if (sessions.TryGetValue(t.Id, out var s) && s.Alive && s is IContextGauge g) g.RefreshUsage();
    }

    /// 정리: omp summarises the earlier part of the conversation (the messages on screen stay; only what the model remembers shrinks).
    public bool Compact(ChatThread t)
    {
        if (t.Busy || IsCompacting(t) || AiModels.Provider(t.Model) != "omp" || t.Messages.Count == 0) return false;
        var s = SessionFor(t, out string? resume, out _, out bool start);
        if (s is not IContextGauge gauge) return false;
        if (!start) return gauge.Compact("");
        // no omp process of this conversation is running (it was closed for being idle): start it on its saved session first
        compacting.Add(t.Id);
        UsageChanged?.Invoke(t);
        var starting = s.StartAsync(resume, CancellationToken.None); // the settings are read here, on the UI thread
        _ = Task.Run(async () =>
        {
            var result = await starting.ConfigureAwait(false);
            await OnUi(() =>
            {
                if (result.Success && gauge.Compact("")) return Task.FromResult(true);
                compacting.Remove(t.Id);
                if (sessions.TryGetValue(t.Id, out var current) && ReferenceEquals(current, s))
                {
                    sessions.Remove(t.Id);
                    s.Dispose();
                    SessionsChanged?.Invoke();
                }
                UsageChanged?.Invoke(t);
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        });
        return true;
    }

    public void Stop(ChatThread t)
    {
        if (preparing.ContainsKey(t.Id) || finishing.ContainsKey(t.Id))
        {
            EndSession(t);
            return;
        }
        if (sessions.TryGetValue(t.Id, out var s)) s.Interrupt();
    }

    public void Delete(ChatThread t)
    {
        Task previousDelete = WhenDeleted(t);
        CancelPreparation(t);
        CancelFinish(t);
        Task output = WhenFinished(t);
        Task<bool> jsonDeleted = store.Delete(Path.Combine(dir, t.Id + ".json"));
        Task preparation = retiredPreparations.Remove(t.Id, out var retired) ? Task.WhenAll(retired) : Task.CompletedTask;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string folder = ThreadFiles(t);
        foreach (string id in deletingFiles.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToList()) deletingFiles.Remove(id);
        deletingFiles[t.Id] = Task.Run(async () =>
        {
            await previousDelete.ConfigureAwait(false);
            await preparation.ConfigureAwait(false);
            await output.ConfigureAwait(false);
            await jsonDeleted.ConfigureAwait(false);
            await released.Task.ConfigureAwait(false);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("delete chat files", e); }
        });
        CancelPreStart(t);
        bool removed = sessions.Remove(t.Id, out var s);
        Threads.Remove(t);
        if (t.SessionProvider == "omp" || s is OmpSession)
        {
            // its omp session and working folder (.omp/mcp.json) go once the process that holds them has ended
            string? ompSession = t.SessionProvider == "omp" ? t.SessionId : null;
            _ = (s?.Closed ?? Task.CompletedTask).ContinueWith(_ =>
            {
                if (ompSession is not null) OmpCli.DeleteSession(ompSession);
                OmpMcp.Forget(t.Id);
            }, TaskScheduler.Default);
        }
        leads.Remove(t.Id);
        turnStarted.Remove(t.Id);
        usages.Remove(t.Id);
        compacting.Remove(t.Id);
        try { s?.Dispose(); }
        finally { released.TrySetResult(); }
        if (removed) SessionsChanged?.Invoke();
    }

    private void Touch(ChatThread t)
    {
        t.Updated = Now;
        Threads.Remove(t);
        Threads.Insert(0, t);
        Save(t);
        // keep the newest 100 conversations outside groups (one put in a group stays until it is deleted)
        while (Threads.Count(x => x.Group is null) > 100)
        {
            var old = Threads.Last(x => x.Group is null);
            if (old.Busy) break;
            Delete(old);
        }
    }

    private void Save(ChatThread t)
    {
        if (t.Messages.Count == 0) return;
        store.Save(Path.Combine(dir, t.Id + ".json"), t);
    }

    public Task<bool> FlushAsync() => store.FlushAsync();
    public void Flush() => store.FlushAsync().GetAwaiter().GetResult();

    private void ReapIdle()
    {
        int before = sessions.Count, idleMinutes = Settings.Current.AiIdleMinutes;
        foreach (var (id, s) in sessions.ToList())
            if (idleMinutes > 0 && !preparing.ContainsKey(id) && !warm.ContainsKey(id) && !s.Busy && DateTime.UtcNow - s.LastUsed > TimeSpan.FromMinutes(idleMinutes))
            {
                s.Dispose();
                sessions.Remove(id);
            }
        if (sessions.Count != before) SessionsChanged?.Invoke();
        if (sessions.Count == 0) idle.Stop();
    }

    public void Shutdown(bool save = true)
    {
        if (shutdown) return;
        shutdown = true;
        idle.Stop();
        StopPreStart();
        foreach (var pending in preparing.Values) _ = pending.Cancellation.CancelAsync();
        preparing.Clear();
        foreach (var pending in finishing.Values) _ = pending.Cancellation.CancelAsync();
        finishing.Clear();
        foreach (var s in sessions.Values) s.Dispose();
        sessions.Clear();
        if (save)
        {
            foreach (var t in Threads.ToArray())
                if (t.Busy)
                {
                    if (leads.Remove(t.Id, out var lead)) t.Messages[^1].Text += lead.Flush();
                    Complete(t, t.Messages[^1], false, AiErrors.Stopped);
                }
        }
        store.CloseAsync().GetAwaiter().GetResult(); // explicit shutdown durability; the store worker never calls the UI
    }
}
