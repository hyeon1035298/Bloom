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
/// brought to the front). A card goes when Claude Code stops waiting (the hook ended).
internal static class ClaudeApprovals
{
    private static readonly UTF8Encoding Utf8 = new(false);
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
                if (await reader.ReadLineAsync() is { } line && Request.Parse(line) is { } r && ClaudeWatch.Session(r.Pid) is { Interactive: true } s)
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

    /// On the UI thread: the card, and what the user picks on it. A request the card can't show whole goes to the
    /// terminal: nothing the user allows stays out of sight.
    private static async Task<string> Ask(Request r, string? about, Task<string?> ended)
    {
        if (!Settings.Current.NoticeApprove || !r.Fits) return "";
        IntPtr window = await SessionWindow.Locate(r.Pid);
        if (!Settings.Current.NoticeApprove || (window != IntPtr.Zero && Win32.GetForegroundWindow() == window)) return ""; // switched off meanwhile, or the session is in front: its own question
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Terminal()
        {
            if (answer.TrySetResult("") && window != IntPtr.Zero) OpenWindows.Switch(window);
        }
        string key = "approve:" + Guid.NewGuid().ToString("N"), session = "claude:" + r.Session;
        var decline = (Tr("거절", "Decline"), (Action)(() => answer.TrySetResult("deny")));
        var terminal = (Tr("터미널에서 답하기", "Answer in terminal"), (Action)Terminal);
        // AskUserQuestion: its questions and choices to pick from, in place of 허용
        var card = r.Questions is { } questions
            ? new Notice("waiting", Tr("Claude Code · 질문", "Claude Code · Question"), questions.Count == 1 ? Tr("Claude의 질문", "Claude's question") : Tr($"Claude의 질문 {questions.Count}개", $"Claude's {questions.Count} questions"), about, key,
                Click: Terminal, Gone: () => answer.TrySetResult(""), Buttons: new[] { decline, terminal }, Body: r.Body,
                Questions: new CardQuestions(questions, given => answer.TrySetResult(ClaudeApprovalHook.Answers + JsonSerializer.Serialize(given))))
            : new Notice("waiting", Tr("Claude Code · 승인 요청", "Claude Code · Approval request"), r.Tool, about, key, Click: Terminal, Gone: () => answer.TrySetResult(""),
                Buttons: new[] { (Tr("허용", "Allow"), (Action)(() => answer.TrySetResult("allow"))), decline, terminal }, Body: r.Body);
        if (!Notices.Approval(session, card)) return "";
        open.Add(answer);
        // the session's window brought to the front another way: the terminal asks there
        var front = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        front.Tick += (_, _) =>
        {
            if (window != IntPtr.Zero && Win32.GetForegroundWindow() == window) answer.TrySetResult("");
        };
        front.Start();
        await Task.WhenAny((Task)answer.Task, ended);
        front.Stop();
        open.Remove(answer);
        Notices.Approved(session, key);
        return answer.Task.IsCompletedSuccessfully ? answer.Task.Result : "";
    }

    private static string? Folder(string? path) => string.IsNullOrEmpty(path) ? null : Path.GetFileName(path.TrimEnd('\\', '/'));

    /// What the hook carried: the Claude Code process that asks, its session, the tool, the whole of what it would do
    /// (Body: the tool's first text — a command, a file, an address — then its other fields as "name: value") and its
    /// folder.
    internal sealed record Request(int Pid, string Session, string Tool, string Body, string? Cwd)
    {
        public const int MaxLines = 8, MaxChars = 600;

        /// AskUserQuestion's questions, when the card can show them as choices (CardQuestions.Parse); null otherwise.
        public IReadOnlyList<CardQuestion>? Questions { get; init; }

        /// The whole request fits on a card: questions are shown whole as their choices.
        public bool Fits => Questions is not null || (Body.Length <= MaxChars && Body.Split('\n').Length <= MaxLines);

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
                    {
                        var fields = input.EnumerateObject().ToList();
                        int first = fields.FindIndex(x => x.Value.ValueKind == JsonValueKind.String);
                        if (first >= 0) lines.Add(fields[first].Value.GetString()!);
                        for (int f = 0; f < fields.Count; f++)
                            if (f != first) lines.Add($"{fields[f].Name}: {(fields[f].Value.ValueKind == JsonValueKind.String ? fields[f].Value.GetString() : fields[f].Value.GetRawText())}");
                    }
                }
                string tool = Str(r, "tool_name") ?? "?";
                var questions = tool == "AskUserQuestion" && r.TryGetProperty("tool_input", out var asked) ? CardQuestions.Parse(asked) : null;
                if (questions is not null) // the questions in words, for the log and the tooltip (the card shows them as choices)
                    lines = questions.Select(q => (q.Header.Length > 0 ? $"[{q.Header}] " : "") + q.Text + "\n" + string.Join("\n", q.Choices.Select(c => "· " + c.Label))).ToList();
                string body = string.Join("\n", lines).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
                return new Request(pid, Str(r, "session_id") ?? "", tool, body, Str(r, "cwd")) { Questions = questions };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
