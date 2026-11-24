using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// Bloom's side of Claude Code's approval hook (ClaudeApprovalHook, 설정 > 알림 > 알림 카드에서 승인): each request comes
/// over the pipe and becomes a card with 허용 · 거절 · 터미널에서 답하기 (Notices), or for AskUserQuestion its questions to
/// answer on the card (CardQuestions). The terminal keeps its own question —
/// Bloom answers nothing — when the switch is off, the session is a program's run (`claude -p`), its window is in front,
/// no card can show (알림 off, a full-screen app), or the user goes to the terminal (the card, its button, or the window
/// brought to the front). A card goes when Claude Code stops waiting (the hook ended). With the Run Card add-on
/// connected and taking requests (RunCardLink), its card asks first and Bloom's own only when it can't.
internal static class ClaudeApprovals
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan RunCardShownWait = TimeSpan.FromSeconds(1.5); // the add-on says shown or none in this time, else Bloom's card asks
    private static readonly HashSet<TaskCompletionSource<string>> open = new(); // the requests waiting on a card (UI thread)
    private static Dispatcher? ui;
    private static bool listening;

    /// Host, once: listens while the switch is on, and points the hook at this Bloom (a new install folder, settings
    /// brought from another PC).
    public static void Start(Dispatcher dispatcher)
    {
        ui = dispatcher;
        Apply();
        if (Settings.Current.NoticeApprove) _ = Task.Run(ClaudeHooks.Follow);
    }

    /// The switch changed: listening starts with the first "on"; from then on the switch decides each answer, and
    /// turned off, the cards waiting now leave their question to the terminal.
    public static void Apply()
    {
        if (!Settings.Current.NoticeApprove)
        {
            foreach (var waiting in open.ToList()) waiting.TrySetResult("");
            return;
        }
        if (ui is null || listening) return;
        listening = true;
        _ = Task.Run(Accept);
    }

    private static async Task Accept()
    {
        string name = ClaudeApprovalHook.Pipe;
        bool told = false;
        while (true)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                if (!told) Log.Error("approval pipe", e); // once per run of failures; tried again every few seconds
                told = true;
                await Task.Delay(5000);
                continue;
            }
            told = false;
            var connected = pipe;
            _ = Task.Run(() => Serve(connected));
        }
    }

    /// One hook run: its request in, the answer out ("allow", "deny", "answers:{…}" for AskUserQuestion, or an empty
    /// line: the terminal asks).
    private static async Task Serve(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8);
                using var writer = new StreamWriter(pipe, Utf8) { AutoFlush = true, NewLine = "\n" };
                string answer = "";
                if (await reader.ReadLineAsync() is { } line && Request.Parse(line) is { } r && Session(r) is { Interactive: true } s)
                {
                    var ended = reader.ReadLineAsync(); // the hook's end (Claude Code stopped waiting) closes the pipe
                    string? about = AgentText.Title(s.Name) ?? Folder(r.Cwd ?? s.Cwd);
                    answer = await ui!.InvokeAsync(() => Ask(r, about, ended)).Task.Unwrap();
                }
                await writer.WriteLineAsync(answer);
            }
            catch (IOException)
            {
                /* Claude Code stopped waiting */
            }
        }
    }

    /// The asking Claude Code's session file: in the Claude Code folder its transcript is in (a session started with
    /// its own CLAUDE_CONFIG_DIR), else in Bloom's. A known folder is the only one looked in: Bloom's may hold a stale
    /// file for the same, reused pid.
    private static (bool Interactive, string? Name, string? Cwd)? Session(Request r) =>
        r.Home is { } home ? ClaudeWatch.Session(r.Pid, home) : ClaudeWatch.Session(r.Pid);

    /// On the UI thread: the card, and what the user picks on it. A request the card can't show whole goes to the
    /// terminal: nothing the user allows stays out of sight.
    private static async Task<string> Ask(Request r, string? about, Task<string?> ended)
    {
        if (!Settings.Current.NoticeApprove || !r.Fits) return "";
        IntPtr window = await SessionWindow.Locate(r.Pid);
        if (!Settings.Current.NoticeApprove || (window != IntPtr.Zero && Win32.GetForegroundWindow() == window)) return ""; // switched off meanwhile, or the session is in front: its own question
        if (RunCardLink.Approvals && Settings.Current.Notices && await OnRunCard(r, window, ended) is { } given) return given;
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Terminal()
        {
            if (answer.TrySetResult("") && window != IntPtr.Zero) OpenWindows.Switch(window);
        }
        string key = "approve:" + Guid.NewGuid().ToString("N"), session = "claude:" + r.Session;
        var decline = (Tr("거절", "Deny"), (Action)(() => answer.TrySetResult("deny")));
        var terminal = (Tr("터미널에서 답하기", "Answer in Terminal"), (Action)Terminal);
        // AskUserQuestion: its questions and choices to pick from, in place of 허용
        var card = r.Questions is { } questions
            ? new Notice("waiting", Tr("Claude Code · 질문", "Claude Code · Question"), questions.Count == 1 ? Tr("Claude의 질문", "Question from Claude") : Tr($"Claude의 질문 {questions.Count}개", $"{questions.Count} Questions from Claude"), about, key,
                Click: Terminal, Gone: () => answer.TrySetResult(""), Buttons: new[] { decline, terminal }, Body: r.Body,
                Questions: new CardQuestions(questions, given => answer.TrySetResult(ClaudeApprovalHook.Answers + JsonSerializer.Serialize(given))))
            : new Notice("waiting", Tr("Claude Code · 승인 요청", "Claude Code · Approval Request"), r.Tool, about, key, Click: Terminal, Gone: () => answer.TrySetResult(""),
                Buttons: new[] { (Tr("허용", "Allow"), (Action)(() => answer.TrySetResult("allow"))), decline, terminal }, Body: r.Body);
        if (!Notices.Approval(session, card)) return "";
        open.Add(answer);
        var front = Front(window, answer);
        await Task.WhenAny((Task)answer.Task, ended);
        front.Stop();
        open.Remove(answer);
        Notices.Approved(session, key);
        return answer.Task.IsCompletedSuccessfully ? answer.Task.Result : "";
    }

    /// The request on the Run Card add-on's card: its answer; "" when Bloom stops waiting first (the hook ended, the
    /// session's window came to the front, the switch went off — the add-on's card is cancelled); null when the add-on
    /// can't take it (`none`, no word in time, the connection dropped before an answer) and Bloom's own card asks.
    private static async Task<string?> OnRunCard(Request r, IntPtr window, Task<string?> ended)
    {
        if (RunCardLink.Ask(r) is not { } ask) return null;
        var stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); // Bloom's side gave up
        open.Add(stopped);
        var front = Front(window, stopped);
        try
        {
            var first = await Task.WhenAny(ask.Shown, Task.Delay(RunCardShownWait), ended, stopped.Task);
            if (first == ask.Shown && ask.Shown.Result) await Task.WhenAny((Task)ask.Answered, ended, stopped.Task);
            if (ask.Answered.IsCompleted && ask.Answered.Result is { } answer)
            {
                if (answer != RunCardProtocol.Terminal) return answer;
                if (window != IntPtr.Zero) OpenWindows.Switch(window); // 터미널에서 답하기, as on Bloom's card
                return "";
            }
            ask.Cancel();
            return stopped.Task.IsCompleted || ended.IsCompleted ? "" : null;
        }
        finally
        {
            front.Stop();
            open.Remove(stopped);
        }
    }

    /// The session's window brought to the front another way: the terminal asks there (`answer` settles as "").
    private static DispatcherTimer Front(IntPtr window, TaskCompletionSource<string> answer)
    {
        var front = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        front.Tick += (_, _) =>
        {
            if (window != IntPtr.Zero && Win32.GetForegroundWindow() == window) answer.TrySetResult("");
        };
        front.Start();
        return front;
    }

    private static string? Folder(string? path) => string.IsNullOrEmpty(path) ? null : Path.GetFileName(path.TrimEnd('\\', '/'));

    /// What the hook carried: the Claude Code process that asks, its session, the tool, the whole of what it would do
    /// (Body: every field of the call as "name: value", made visible by ToolText so no control or format character and no
    /// line separator but \n is left in it) and its folder.
    internal sealed record Request(int Pid, string Session, string Tool, string Body, string? Cwd)
    {
        public const int MaxLines = 8, MaxChars = 600;

        /// The Claude Code folder the session keeps its files in, from the transcript's path (ClaudeWatch.HomeOf); null
        /// when the hook didn't say or the path isn't of that shape.
        public string? Home { get; init; }

        /// AskUserQuestion's questions, when the card can show them as choices (CardQuestions.Parse); null otherwise.
        public IReadOnlyList<CardQuestion>? Questions { get; init; }

        /// The whole request fits on a card: questions are shown whole as their choices.
        public bool Fits => Questions is not null || (Body.Length <= MaxChars && ToolText.Lines(Body) <= MaxLines);

        /// {"pid": n, "input": "<the hook's JSON>"} → the request; null for anything else.
        public static Request? Parse(string line)
        {
            try
            {
                using var outer = JsonDocument.Parse(line);
                var o = outer.RootElement;
                if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("pid", out var p) || !p.TryGetInt32(out int pid)
                    || !o.TryGetProperty("input", out var i) || i.ValueKind != JsonValueKind.String) return null;
                using var doc = JsonDocument.Parse(i.GetString()!);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return null;
                var lines = new List<string>();
                if (r.TryGetProperty("tool_input", out var input))
                {
                    if (input.ValueKind != JsonValueKind.Object) lines.Add(input.GetRawText());
                    else
                        foreach (var field in input.EnumerateObject()) // each under its name: a description can't pass for the command
                            lines.Add($"{field.Name}: {(field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : field.Value.GetRawText())}");
                }
                string tool = Str(r, "tool_name") ?? "?";
                var questions = tool == "AskUserQuestion" && r.TryGetProperty("tool_input", out var asked) ? CardQuestions.Parse(asked) : null;
                if (questions is not null) // the questions in words, for the log and the tooltip (the card shows them as choices)
                    lines = questions.Select(q => (q.Header.Length > 0 ? $"[{q.Header}] " : "") + q.Text + "\n" + string.Join("\n", q.Choices.Select(c => "· " + c.Label))).ToList();
                string body = ToolText.Visible(string.Join("\n", lines)).Trim();
                return new Request(pid, Str(r, "session_id") ?? "", tool, body, Str(r, "cwd")) { Questions = questions, Home = ClaudeWatch.HomeOf(Str(r, "transcript_path")) };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
