using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ai;

/// The AI chat window's T3 side: T3 Code's threads listed by project, one opened read as a Bloom chat and
/// followed while T3 runs it, words sent to it (into the running turn, or starting the next), its turn stopped, its
/// questions answered, and new threads made in a project T3 keeps. T3 runs everything; Bloom keeps nothing of it on
/// disk (the chats are ChatThreads with a T3Ref, never in Chats.Threads) and gives the threads none of Bloom's tools.
/// Every call goes through T3Agent, so 설정 > AI 'T3 Code 연결' off asks T3 nothing. An
/// Antigravity thread is view-only: read and shown, never sent to, stopped or answered, and no new one made on it. UI thread.
internal sealed class T3Chats
{
    private sealed class AgentLink : IT3Link
    {
        public bool Present => T3Agent.Present;
        public Task<T3Agent.Got> Look(string lane, string tool, JsonObject arguments, CancellationToken ct) => Task.Run(() => T3Agent.Look(lane, tool, arguments, ct));
        public Task<T3Agent.Got> Act(string tool, JsonObject arguments, CancellationToken ct) => Task.Run(() => T3Agent.Act(tool, arguments, ct));
        public Task<T3Agent.Sent> Say(string thread, string text, string clientRequestId, CancellationToken ct) => Task.Run(() => T3Agent.SayTo(thread, text, null, clientRequestId, ct));
        public Task<T3Agent.Sent> Answer(string thread, IReadOnlyDictionary<string, string> given, CancellationToken ct) => Task.Run(() => T3Agent.AnswerTo(thread, given, ct));
        public Task<T3Agent.Sent> Stop(string thread, CancellationToken ct) => Task.Run(() => T3Agent.StopThread(thread, ct));
        public Task<(string RequestId, List<(string Id, CardQuestion Question)> Questions)?> Pending(string thread, CancellationToken ct) => Task.Run(() => T3Agent.PendingOf(thread, ct));
    }

    /// The first page of a thread opened, the size of each page read after, and the longest text an item gives.
    internal const int OpenMessages = 30, FollowItems = 100, MaxChars = 20000;

    /// The threads the list asks for at once, and how many of each project it shows before 더 보기.
    internal const int ListLimit = 50, PerProject = 5;

    /// The list is read again when the window comes forward after this long.
    internal static readonly TimeSpan ListStale = TimeSpan.FromSeconds(10);

    private readonly Dispatcher ui;
    private readonly Action<ChatThread> updated;
    private readonly Dictionary<string, ChatThread> chats = new(); // by T3's thread id
    private readonly Dictionary<string, T3Mirror> mirrors = new();
    private readonly Dictionary<string, int> after = new(); // where each followed thread reads on from
    private readonly Dictionary<string, List<Echo>> echoes = new(); // what was sent and isn't in T3's items yet
    private readonly Dictionary<string, (string Text, string Note)> failed = new(); // the last send that didn't go
    private readonly CancellationTokenSource life = new();
    private DispatcherTimer? timer;
    private ChatThread? followed;
    private bool visible, front;
    private int failures;
    private DateTime listedAt, wokenAt;
    private bool shutdown;
    private bool owed; // a read of the followed thread found the lane busy: the read out now reads on when it ends
    private bool? listOwed; // a list read found its lane busy (true: for the next page): read when the one out ends
    private DispatcherTimer? wakeLater;

    /// Wakes come at most this often; one sooner is held until then.
    private static readonly TimeSpan WakeGap = TimeSpan.FromSeconds(2);

    private sealed record Echo(string Text, int UsersBefore, DateTime At);

    public IT3Link Link { get; set; } = new AgentLink();

    /// The threads listed (newest first), the projects T3 keeps, the cursor of the list's next page (null: no more), and
    /// how the last read of it went: "" read, "loading", "t3-off", "failed".
    public List<T3Row> Rows { get; private set; } = new();
    public List<T3Project> Projects { get; private set; } = new();
    public int? MoreCursor { get; private set; }
    public string ListState { get; private set; } = "";

    /// The words the list is narrowed to (t3_thread_list's titleContains); "" none.
    public string Query { get; private set; } = "";

    /// The projects whose list shows all its threads read (더 보기).
    public readonly HashSet<string> Unfolded = new();

    /// UI thread: the list or the projects changed.
    public event Action? ListChanged;

    /// UI thread: a line for the window to show about this chat (how a send went, a stop that didn't).
    public event Action<ChatThread, string>? Noted;

    public T3Chats(Dispatcher ui, Action<ChatThread> updated)
    {
        this.ui = ui;
        this.updated = updated;
    }

    /// T3 is there to talk to (the window shows its side).
    public bool Present => Link.Present;

    // ── the list ───────────────────────────────────────────────────────────────────────────────

    /// Reads the list again (and the projects, the first time or when asked): `query` narrows it by title; `more` reads
    /// the next page onto it.
    public async Task Refresh(string? query = null, bool more = false, bool projects = false)
    {
        if (shutdown) return;
        if (query is not null && query != Query)
        {
            Query = query;
            more = false;
        }
        var args = new JsonObject { ["allProjects"] = true, ["includeSubagents"] = false, ["limit"] = ListLimit };
        if (Query.Length > 0) args["titleContains"] = Query;
        if (more && MoreCursor is { } cursor) args["cursor"] = cursor;
        listedAt = DateTime.UtcNow;
        if (Rows.Count == 0)
        {
            ListState = "loading";
            ListChanged?.Invoke();
        }
        if (projects || Projects.Count == 0) await ReadProjects();
        string asked = Query;
        var got = await Link.Look("list", "t3_thread_list", args, life.Token);
        if (shutdown) return;
        if (got.Why == "busy")
        {
            listOwed = (listOwed ?? false) || more;
            return;
        }
        try
        {
            if (asked != Query) return; // typed on meanwhile: the read owed is of the words now
            if (got.Node is not { } node)
            {
                ListState = got.Why;
                ListChanged?.Invoke();
                return;
            }
            var (rows, next) = T3Wire.List(node);
            Rows = more ? Rows.Concat(rows.Where(r => Rows.All(x => x.Id != r.Id))).ToList() : rows;
            MoreCursor = next;
            ListState = "";
            foreach (var row in rows)
                if (chats.TryGetValue(row.Id, out var t))
                {
                    string was = t.T3?.Status ?? "";
                    Mark(t, row);
                    // a turn of the followed thread began or ended out of sight of its reads: read it now
                    if (ReferenceEquals(t, followed) && visible && t.T3!.Status != was) _ = Tick();
                }
            ListChanged?.Invoke();
        }
        finally
        {
            if (asked != Query) listOwed ??= false;
            if (listOwed is { } again && !shutdown)
            {
                listOwed = null;
                _ = Refresh(more: again);
            }
        }
    }

    private async Task ReadProjects()
    {
        var got = await Link.Look("projects", "t3_project_list", new JsonObject { ["limit"] = 100 }, life.Token);
        if (got.Node is { } node) Projects = T3Wire.Projects(node).Projects;
    }

    /// The window came forward: the list is read again when it is older than ListStale.
    public void Forward()
    {
        if (DateTime.UtcNow - listedAt > ListStale) _ = Refresh();
    }

    /// The list's threads by project, in the order of each project's newest thread; a thread of no project (scratch)
    /// under "폴더 없이", one of a project T3 no longer lists under its id.
    internal static List<(string? ProjectId, string Title, List<T3Row> Rows)> Groups(IEnumerable<T3Row> rows, IReadOnlyList<T3Project> projects)
    {
        var groups = new List<(string? ProjectId, string Title, List<T3Row> Rows)>();
        foreach (var row in rows)
        {
            int at = groups.FindIndex(g => g.ProjectId == row.ProjectId);
            if (at < 0)
            {
                string title = row.ProjectId is null ? Tr("폴더 없이", "No folder") : projects.FirstOrDefault(p => p.Id == row.ProjectId)?.Title ?? Tr("다른 폴더", "Other Folders");
                groups.Add((row.ProjectId, title, new List<T3Row>()));
                at = groups.Count - 1;
            }
            groups[at].Rows.Add(row);
        }
        return groups;
    }

    // ── a thread opened ───────────────────────────────────────────────────────────────────────

    /// The chat of a listed thread (the same one each time it is opened).
    public ChatThread Chat(T3Row row)
    {
        if (!chats.TryGetValue(row.Id, out var t))
        {
            t = new ChatThread { Id = "t3:" + row.Id, T3 = new T3Ref { ThreadId = row.Id, ProjectId = row.ProjectId } };
            chats[row.Id] = t;
        }
        Mark(t, row);
        return t;
    }

    /// A new T3 chat, not made in T3 until its first message: in the project last used (or scratch), on the model that
    /// project starts with, else the one last picked, else that of the newest thread.
    public ChatThread Draft()
    {
        string? project = Settings.Current.T3Project;
        bool scratch = project == ScratchKey;
        if (!scratch && project is not null && Projects.Count > 0 && Projects.All(p => p.Id != project)) project = null;
        project ??= Projects.FirstOrDefault()?.Id;
        var t = new ChatThread { Id = "t3:new:" + Guid.NewGuid().ToString("N")[..12], Title = Tr("새 T3 스레드", "New T3 Thread"), T3 = new T3Ref { ProjectId = scratch ? null : project, Scratch = scratch || project is null } };
        t.Model = ModelFor(t) ?? "t3:";
        return t;
    }

    /// Settings.T3Project's value for 폴더 없이.
    internal const string ScratchKey = "scratch";

    /// A new chat's project picked (null: 폴더 없이); its model follows the project's own unless one was picked here.
    public void Pick(ChatThread t, string? project)
    {
        if (t.T3 is not { ThreadId: null } r) return;
        string? before = ModelFor(t);
        r.ProjectId = project;
        r.Scratch = project is null;
        if (t.Model == before || !AiModels.IsT3(t.Model) || AiModels.T3Parts(t.Model) is null) t.Model = ModelFor(t) ?? t.Model;
        Settings.Current.T3Project = project ?? ScratchKey;
        Settings.Save();
        updated(t);
    }

    /// The model a new thread in `t`'s project starts with: the project's own → the one last picked → the newest thread's;
    /// never an Antigravity one (a new thread is never made on it).
    internal string? ModelFor(ChatThread t) => DefaultModel(Projects.FirstOrDefault(p => p.Id == t.T3?.ProjectId), Settings.Current.T3Pick, Rows);

    internal static string? DefaultModel(T3Project? project, string? pick, IEnumerable<T3Row> recent)
    {
        if (project is { Instance: { Length: > 0 } i, Model: { Length: > 0 } m } && !T3Threads.AntigravityInstance(i)) return AiModels.T3(i, m);
        if (pick is not null && AiModels.T3Parts(pick) is not null && !T3Threads.AntigravityModel(pick)) return pick;
        return recent.FirstOrDefault(r => r.Instance.Length > 0 && r.Model.Length > 0 && !T3Threads.AntigravityInstance(r.Instance)) is { } row ? AiModels.T3(row.Instance, row.Model) : null;
    }

    /// The models a new thread can start on: those T3's projects start with, the one last picked, those of the threads
    /// listed (T3 says no more of what it has without its catalog); none of Antigravity's.
    public List<string> Models()
    {
        var list = new List<string>();
        void Add(string? id)
        {
            if (id is not null && AiModels.T3Parts(id) is not null && !list.Contains(id) && !T3Threads.AntigravityModel(id)) list.Add(id);
        }
        foreach (var p in Projects) Add(p.Instance is { Length: > 0 } && p.Model is { Length: > 0 } ? AiModels.T3(p.Instance, p.Model) : null);
        Add(Settings.Current.T3Pick);
        foreach (var r in Rows) Add(r.Instance.Length > 0 && r.Model.Length > 0 ? AiModels.T3(r.Instance, r.Model) : null);
        return list;
    }

    /// The model picked for a new chat; also the one the next new chat starts with when its project has none.
    public void SetModel(ChatThread t, string model)
    {
        if (t.T3 is not { ThreadId: null } || AiModels.T3Parts(model) is null || T3Threads.AntigravityModel(model)) return;
        t.Model = model;
        Settings.Current.T3Pick = model;
        Settings.Save();
        updated(t);
    }

    private void Mark(ChatThread t, T3Row row)
    {
        t.Title = row.Title.Length > 0 ? row.Title : Tr("제목 없는 스레드", "Untitled thread");
        if (row.Instance.Length > 0 && row.Model.Length > 0) t.Model = AiModels.T3(row.Instance, row.Model);
        t.Updated = new DateTimeOffset(DateTime.SpecifyKind(row.Updated, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        if (t.T3 is { } r)
        {
            r.ProjectId = row.ProjectId;
            r.Settled = row.Settled;
            r.Status = row.Status;
            if (row.Instance.Length > 0) r.ViewOnly = T3Threads.AntigravityInstance(row.Instance);
            if (row.Pending > 0) r.Pending = row.Pending;
        }
    }

    /// The window shows `t` (null: a Bloom chat, or nothing): it is read now when it hasn't been, and followed while T3
    /// runs it — every second while the window is in front, every three while it only shows, not while it is hidden.
    public void Watch(ChatThread? t, bool visible, bool front)
    {
        if (shutdown) return;
        bool moved = !ReferenceEquals(t, followed), shown = visible && !this.visible;
        followed = t?.T3?.ThreadId is not null ? t : null;
        this.visible = visible;
        this.front = front;
        if (moved) failures = 0;
        // read when it is new here, when the window comes back to it, and when the window shows again: a turn may have
        // begun or ended while nothing read it
        if (followed is { } f && visible && (moved || shown || !mirrors.ContainsKey(f.T3!.ThreadId!))) _ = Tick();
        else Schedule();
    }

    /// How long until the followed thread is read again: null = not until something wakes it (it is idle, or the
    /// window is hidden). A failed read waits 5 s, then longer up to 15 s.
    internal static TimeSpan? Delay(bool active, bool pending, bool visible, bool front, int failures)
    {
        if (!visible) return null;
        if (failures > 0) return TimeSpan.FromSeconds(Math.Min(15, 5 * (1 << Math.Min(failures - 1, 2))));
        if (!active) return null;
        return front && !pending ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3);
    }

    private void Schedule()
    {
        timer?.Stop();
        if (shutdown || followed?.T3 is not { } r) return;
        if (Delay(r.Active || Echoing(r.ThreadId!), r.Pending > 0, visible, front, failures) is not { } wait) return;
        timer ??= NewTimer();
        timer.Interval = wait;
        timer.Start();
    }

    private DispatcherTimer NewTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background, ui);
        t.Tick += (_, _) =>
        {
            t.Stop();
            _ = Tick();
        };
        return t;
    }

    private DispatcherTimer NewWakeLater()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background, ui);
        t.Tick += (_, _) =>
        {
            t.Stop();
            Wake();
        };
        return t;
    }

    private bool Echoing(string thread) => echoes.TryGetValue(thread, out var list) && list.Count > 0;

    /// Something about T3's sessions changed (Notices saw a turn start or end): the followed thread is read now, and
    /// the list when it shows. At most once every two seconds: one sooner is held until then, not dropped.
    public void Wake()
    {
        if (shutdown) return;
        var since = DateTime.UtcNow - wokenAt;
        if (since < WakeGap)
        {
            wakeLater ??= NewWakeLater();
            if (!wakeLater.IsEnabled)
            {
                wakeLater.Interval = WakeGap - since;
                wakeLater.Start();
            }
            return;
        }
        wokenAt = DateTime.UtcNow;
        if (followed is not null && visible) _ = Tick();
        if (visible && listedAt != default) _ = Refresh();
    }

    /// One read of the followed thread: its first page when it is new here, else what T3 wrote since (and the question
    /// it waits on).
    private async Task Tick()
    {
        if (shutdown || followed is not { T3.ThreadId: { } id } t) return;
        bool opening = !mirrors.TryGetValue(id, out var mirror);
        var args = opening
            ? new JsonObject { ["threadId"] = id, ["view"] = "messages", ["limit"] = OpenMessages, ["maxCharsPerItem"] = MaxChars, ["runLimit"] = 1 }
            : new JsonObject { ["threadId"] = id, ["view"] = "activity", ["afterPosition"] = after.GetValueOrDefault(id, -1), ["limit"] = FollowItems, ["maxCharsPerItem"] = MaxChars, ["runLimit"] = 1 };
        var got = await Link.Look("follow", "t3_thread_read", args, life.Token);
        if (shutdown) return;
        if (got.Why == "busy")
        {
            owed = true; // the read before is still out (maybe of another thread): it reads on for this one when it ends
            return;
        }
        if (got.Node is not { } node)
        {
            failures++;
            if (opening && t.Messages.Count == 0)
            {
                t.Messages.Add(new ChatMessage { Role = "assistant", Error = true, Text = got.Why == "t3-off" ? Tr("T3에 연결할 수 없습니다. T3 Code가 켜져 있는지, 설정 > AI에서 'T3 Code 연결'이 켜져 있는지 확인하세요.", "Could not reach T3. Make sure T3 Code is running and 'T3 Code connection' is turned on in Settings > AI.") : Tr("스레드를 읽지 못했습니다. 잠시 후 다시 시도하세요.", "Could not load the thread. Please try again shortly.") });
                updated(t);
            }
            Next(false);
            return;
        }
        failures = 0;
        var page = T3Wire.Read(node);
        if (mirror is null)
        {
            mirrors[id] = mirror = new T3Mirror();
            t.Messages.Clear();
        }
        mirror.Take(page.Items);
        if (page.Thread is { } row)
        {
            Mark(t, row);
            t.T3!.Pending = row.Pending;
            // the list's line of it says what the read said (its dot, its title)
            int at = Rows.FindIndex(x => x.Id == row.Id);
            if (at >= 0 && Rows[at] != row)
            {
                Rows[at] = row;
                ListChanged?.Invoke();
            }
        }
        // more written than one page holds: on from the page's end; else from what T3 hasn't finished
        after[id] = page.More && page.Next is { } next ? next : opening && t.T3!.Active ? mirror.AfterOpen() : mirror.After();
        T3Question? question = null;
        bool approval = false;
        if (t.T3!.Pending > 0)
        {
            // an Antigravity thread's question is not Bloom's to answer: it shows as waiting in T3
            if (!t.T3.ViewOnly && await Link.Pending(id, life.Token) is { } p && p.Questions.Count > 0) question = new T3Question(p.RequestId, p.Questions.Select(q => q.Question).ToList());
            else approval = true; // T3 lists only questions: what waits is an approval, given in T3
        }
        if (shutdown) return;
        Render(t, question, approval);
        Next(page.More);
    }

    /// After a read: the next at once when more is written than a page held, or a read found the lane busy meanwhile;
    /// else when Schedule says.
    private void Next(bool more)
    {
        if (shutdown) return;
        if (more || owed)
        {
            owed = false;
            _ = Tick();
        }
        else Schedule();
    }

    /// The chat made to read as the thread's items, with the question it waits on, and what was sent and isn't there yet.
    private void Render(ChatThread t, T3Question? question, bool approval)
    {
        string id = t.T3!.ThreadId!;
        var fresh = mirrors.TryGetValue(id, out var mirror) ? mirror.Messages(t.T3.Active) : new List<ChatMessage>();
        if (echoes.TryGetValue(id, out var sent))
        {
            // an echo goes once T3 has the message (a user message after those there were when it was sent), or after a while
            sent.RemoveAll(e => DateTime.UtcNow - e.At > TimeSpan.FromMinutes(2) || fresh.Where(m => m.Role == "user").Skip(e.UsersBefore).Any(m => m.Text.Trim() == e.Text.Trim()));
            foreach (var e in sent)
            {
                fresh.Add(new ChatMessage { Role = "user", Text = e.Text });
                if (!t.T3.Active) fresh.Add(new ChatMessage { Role = "assistant", Streaming = true, Status = Tr("보내는 중…", "Sending…") });
            }
            if (sent.Count > 0 && t.T3.Active && fresh[^1].Role == "user") fresh.Add(new ChatMessage { Role = "assistant", Streaming = true });
        }
        if (failed.TryGetValue(id, out var no))
        {
            fresh.Add(new ChatMessage { Role = "user", Text = no.Text });
            fresh.Add(new ChatMessage { Role = "assistant", Error = true, Text = no.Note });
        }
        if (question is not null || approval)
        {
            var host = fresh.Count > 0 && fresh[^1].Role == "assistant" ? fresh[^1] : null;
            if (host is null) fresh.Add(host = new ChatMessage { Role = "assistant" });
            host.Question = question;
            host.ApprovalWait = approval && question is null;
            if (host.Streaming)
                host.Status = question is not null ? Tr("답을 기다리는 중", "Waiting for reply") : t.T3.ViewOnly ? Tr("T3에서 기다리는 중", "Waiting in T3") : Tr("승인을 기다리는 중", "Waiting for approval");
        }
        if (T3Mirror.Apply(t.Messages, fresh)) updated(t);
    }

    // ── what the user does ─────────────────────────────────────────────────────────────────────

    /// Words for the chat: a new one becomes a thread in T3; an open one takes them into its running turn (or the next),
    /// or as the answer to the question it waits on. An Antigravity thread takes none (view-only).
    public void Send(ChatThread t, string text)
    {
        if (shutdown || t.T3 is not { } r || text.Trim().Length == 0) return;
        if (Barred(t)) return;
        if (r.ThreadId is null)
        {
            _ = Launch(t, text);
            return;
        }
        _ = SendTo(t, r.ThreadId, text);
    }

    private async Task SendTo(ChatThread t, string id, string text)
    {
        failed.Remove(id);
        Add(id, text);
        Render(t, t.Messages.LastOrDefault()?.Question, false);
        var sent = await Link.Say(id, text, Guid.NewGuid().ToString(), life.Token);
        if (shutdown) return;
        if (!sent.Ok)
        {
            echoes.Remove(id);
            failed[id] = (text, Notices.SentNote(sent));
            Render(t, null, false);
            return;
        }
        if (sent.How is "steered" or "queued" or "answered") Noted?.Invoke(t, Notices.SentNote(sent));
        if (sent.How == "answered") echoes.Remove(id); // the words went as the question's answers, not as a message
        t.T3!.Status = "running"; // until the next read says otherwise
        t.T3.Pending = 0;
        Render(t, null, false);
        failures = 0;
        if (ReferenceEquals(t, followed)) _ = Tick();
        _ = Refresh();
    }

    private void Add(string id, string text)
    {
        if (!echoes.TryGetValue(id, out var list)) echoes[id] = list = new();
        list.Add(new Echo(text, mirrors.TryGetValue(id, out var m) ? m.Messages(false).Count(x => x.Role == "user") : 0, DateTime.UtcNow));
    }

    /// ■ or Esc: T3 interrupts the thread's turn (never an Antigravity one's).
    public async Task Stop(ChatThread t)
    {
        if (shutdown || t.T3?.ThreadId is not { } id || Barred(t)) return;
        var stopped = await Link.Stop(id, life.Token);
        if (shutdown) return;
        if (!stopped.Ok)
            Noted?.Invoke(t, Notices.StopNote(stopped));
        echoes.Remove(id);
        if (ReferenceEquals(t, followed)) _ = Tick();
    }

    /// The orange box's answers (by each question's text) to the question the thread waits on.
    public async Task Answer(ChatThread t, IReadOnlyDictionary<string, string> given)
    {
        if (shutdown || t.T3?.ThreadId is not { } id || Barred(t)) return;
        var sent = await Link.Answer(id, given, life.Token);
        if (shutdown) return;
        Noted?.Invoke(t, Notices.SentNote(sent));
        if (sent.Ok)
        {
            t.T3!.Pending = 0;
            t.T3.Status = "running";
            Render(t, null, false);
        }
        if (ReferenceEquals(t, followed)) _ = Tick();
    }

    /// An Antigravity thread, or a new one on an Antigravity model: view-only, so the window says so and nothing goes to
    /// T3 (T3Agent refuses it too).
    private bool Barred(ChatThread t)
    {
        if (t.T3 is not { } r || !(r.ThreadId is null ? T3Threads.AntigravityModel(t.Model) : r.ViewOnly)) return false;
        Noted?.Invoke(t, T3Threads.ViewOnlyNote);
        return true;
    }

    /// The first message of a new chat: T3 makes the thread (in its project's own checkout, or a new scratch folder) at
    /// full access on the model picked, and the chat follows it from then on.
    private async Task Launch(ChatThread t, string text)
    {
        var r = t.T3!;
        if (AiModels.T3Parts(t.Model) is not { } model)
        {
            Noted?.Invoke(t, Tr("먼저 모델을 선택하세요", "Select a model first"));
            return;
        }
        if (t.Messages.Any(m => m.Streaming)) return; // the launch before is still out
        string launch = string.Join("\n", text, r.ProjectId, r.Scratch, t.Model);
        if (r.LaunchId is null || r.LaunchOf != launch)
        {
            r.LaunchId = Guid.NewGuid().ToString();
            r.LaunchOf = launch;
        }
        t.Title = TitleOf(text);
        t.Messages.Clear();
        t.Messages.Add(new ChatMessage { Role = "user", Text = text });
        t.Messages.Add(new ChatMessage { Role = "assistant", Streaming = true, Status = Tr("T3에 스레드를 만드는 중…", "Creating thread in T3…") });
        updated(t);
        var project = Projects.FirstOrDefault(p => p.Id == r.ProjectId);
        var got = await Link.Act("t3_thread_launch", LaunchArgs(t.Title, text, r.Scratch ? null : r.ProjectId, model.Instance, model.Model, project, r.LaunchId), life.Token);
        if (shutdown) return;
        if (got.Node is not { } node || T3Wire.Launched(node) is not { } id)
        {
            t.Messages[^1].Streaming = false;
            t.Messages[^1].Status = null;
            t.Messages[^1].Error = true;
            t.Messages[^1].Text = got.Why == "t3-off" ? Tr("T3에 연결할 수 없어 스레드를 만들지 못했습니다.", "Could not reach T3 to create a thread.")
                : got.Why is T3Agent.ViewOnly or T3Agent.Unchecked ? Notices.SentNote(new T3Agent.Sent(false, got.Why)) : Tr("T3에서 스레드를 만들지 못했습니다. 같은 메시지를 다시 보내면 동일한 요청으로 다시 시도합니다.", "T3 could not create the thread. Send the same message again to retry the request.");
            t.Messages.RemoveAt(0);
            updated(t);
            return;
        }
        r.ThreadId = id;
        r.Status = "running";
        t.Id = "t3:" + id;
        chats[id] = t;
        Settings.Current.T3Pick = t.Model;
        Settings.Current.T3Project = r.Scratch ? ScratchKey : r.ProjectId;
        Settings.Save();
        // what was typed shows until T3's own copy of it is read
        echoes[id] = new List<Echo> { new(text, 0, DateTime.UtcNow) };
        if (followed is null && visible) followed = t;
        _ = Refresh();
        if (ReferenceEquals(followed, t)) _ = Tick();
        else updated(t);
    }

    /// t3_thread_launch's arguments: a project's thread in that project's own checkout (`root`), a scratch one in a new
    /// folder; full access, the default interaction; the project's own options for its own model; `launchId` so a send
    /// again after a lost answer makes the same thread.
    internal static JsonObject LaunchArgs(string title, string text, string? projectId, string instance, string model, T3Project? project, string launchId)
    {
        var selection = new JsonObject { ["instanceId"] = instance, ["model"] = model };
        if (project is { Options: { } options } && project.Instance == instance && project.Model == model) selection["options"] = options.DeepClone();
        var args = new JsonObject
        {
            ["title"] = title,
            ["message"] = text,
            ["modelSelection"] = selection,
            ["runtimeMode"] = "full-access",
            ["interactionMode"] = "default",
            ["clientRequestId"] = launchId,
        };
        if (projectId is not null)
        {
            args["projectId"] = projectId;
            args["workspaceStrategy"] = new JsonObject { ["type"] = "root" };
        }
        else args["scratch"] = true;
        return args;
    }

    private static string TitleOf(string text)
    {
        string first = text.Trim().Split('\n')[0].Trim();
        return first.Length > 60 ? first[..60] + "…" : first.Length == 0 ? Tr("새 T3 스레드", "New T3 Thread") : first;
    }

    /// T3 brought forward at the chat's thread (t3code://…, as the notices' T3에서 열기).
    public static bool OpenInT3(ChatThread t) => t.T3?.ThreadId is { } id && T3Threads.OpenThread(id);

    public void Shutdown()
    {
        shutdown = true;
        timer?.Stop();
        life.Cancel();
    }

    // ── QA pictures ────────────────────────────────────────────────────────────────────────────

    /// QA only: a list and projects as T3 might give them, with no call to T3.
    internal void SetForQa(List<T3Row> rows, List<T3Project> projects)
    {
        Rows = rows;
        Projects = projects;
        ListState = "";
        listedAt = DateTime.UtcNow;
        ListChanged?.Invoke();
    }

    /// QA only: a listed thread's chat shown with these items (and the question it waits on), with no call to T3.
    internal ChatThread ChatForQa(T3Row row, IEnumerable<T3Item> items, T3Question? question = null, bool approval = false)
    {
        var t = Chat(row);
        var mirror = new T3Mirror();
        mirror.Take(items);
        mirrors[row.Id] = mirror;
        t.T3!.Pending = question is not null || approval ? 1 : 0;
        Render(t, question, approval);
        return t;
    }
}
