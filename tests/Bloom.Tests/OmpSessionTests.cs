using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// OmpSession against a stand-in omp (FakeOmp: this program started as `--mode rpc`): the process it starts and the
/// arguments it gets, a turn's text and end, the numbers after it, a stop, an error, a slash command, 정리, and Bloom's
/// MCP tool being asked about in the chat.
internal static class OmpSessionTests
{
    private sealed class Run : IDisposable
    {
        private readonly SettingsTransferTests.DataFolder data = new();
        private readonly string priorPath = Settings.Current.OmpPath ?? "";
        private readonly bool priorSearch = Settings.Current.OmpSearch;
        private readonly string? priorArgs = Environment.GetEnvironmentVariable("FAKE_OMP_ARGS");
        public readonly string ArgsFile;
        public readonly OmpSession Session;
        public readonly object Gate = new();
        public readonly List<string> Text = new(), Statuses = new(), Notes = new(), Ids = new();
        public readonly List<(bool ok, string? error)> Ends = new();
        public int Exits, UsageChanges;

        public Run(string model = "omp:", bool search = true)
        {
            string exe = Path.ChangeExtension(typeof(Run).Assembly.Location, ".exe"); // the apphost: `dotnet run` and the exe both start it
            Check.True(File.Exists(exe), "the test program's own exe (the stand-in omp)");
            Settings.Current.OmpPath = exe;
            Settings.Current.OmpSearch = search;
            ArgsFile = Path.Combine(data.Directory, "args.txt");
            Environment.SetEnvironmentVariable("FAKE_OMP_ARGS", ArgsFile);
            Session = new OmpSession(model, "medium", "conv-1");
            Session.TextDelta += t => { lock (Gate) Text.Add(t); };
            Session.Status += st => { lock (Gate) Statuses.Add(st ?? "(clear)"); };
            Session.TurnEnded += (ok, err) => { lock (Gate) Ends.Add((ok, err)); };
            Session.SessionStarted += id => { lock (Gate) Ids.Add(id); };
            Session.Exited += _ => Interlocked.Increment(ref Exits);
            Session.UsageChanged += () => Interlocked.Increment(ref UsageChanges);
            Session.CompactNote += (text, running) => { lock (Gate) Notes.Add((running ? "… " : "") + text); };
        }

        public void Start()
        {
            Check.True(Session.Start(null, out string? error), "the session starts: " + error);
        }

        public void Until(Func<bool> done, string what)
        {
            var end = DateTime.UtcNow.AddSeconds(20);
            while (!done() && DateTime.UtcNow < end) Thread.Sleep(10);
            Check.True(done(), "in time: " + what);
        }

        public string Said { get { lock (Gate) return string.Concat(Text); } }

        public int Turns { get { lock (Gate) return Ends.Count; } }

        public string[] Args => File.ReadAllLines(ArgsFile);

        public void Dispose()
        {
            Session.Dispose();
            Session.Closed.Wait(TimeSpan.FromSeconds(10));
            Settings.Current.OmpPath = priorPath.Length == 0 ? null : priorPath;
            Settings.Current.OmpSearch = priorSearch;
            Environment.SetEnvironmentVariable("FAKE_OMP_ARGS", priorArgs);
            data.Dispose();
        }
    }

    public static void StartsRpcWithBloomsOwnArguments()
    {
        using var r = new Run();
        r.Start();
        r.Until(() => r.Ids.Count == 1, "the session id from get_state");
        Check.Eq(r.Ids[0], "fake-session", "the session id is announced");
        Check.Eq(r.Session.SessionId, "fake-session", "and kept to resume it");
        r.Until(() => File.Exists(r.ArgsFile) && r.Args.Length > 5, "the arguments");
        var a = r.Args;
        Check.True(a[0] == "--mode" && a[1] == "rpc" && a.Contains("--no-ui"), "omp's rpc mode");
        Check.True(!a.Contains("--model"), "no model passed: omp's own default");
        Check.True(a.Contains("--thinking=medium") && a.Contains("--tools=web_search"), "the level and web search");
        string prompt = a.First(x => x.StartsWith("--system-prompt=", StringComparison.Ordinal));
        Check.True(prompt.Contains("assistant in a chat inside Bloom") && prompt.Contains("besides web search"), "Bloom's instructions replace omp's own prompt");
        Check.True(a.Any(x => x.StartsWith("--session-dir=", StringComparison.Ordinal) && x.Contains("omp-sessions")), "sessions in Bloom's own folder");
        Check.True(a.Contains("--no-skills") && a.Contains("--no-rules") && a.Contains("--no-extensions"), "none of the user's omp setup");
        Check.True(File.Exists(Path.Combine(OmpMcp.WorkDir("conv-1"), ".keep")) || Directory.Exists(OmpMcp.WorkDir("conv-1")), "the conversation has a working folder of its own");
    }

    public static void ChosenModelIsPassed()
    {
        using var r = new Run("omp:vendor/some-model", search: false);
        r.Start();
        r.Until(() => File.Exists(r.ArgsFile) && r.Args.Length > 5, "the arguments");
        var a = r.Args;
        Check.Eq(a[Array.IndexOf(a, "--model") + 1], "vendor/some-model", "the model");
        Check.True(a.Contains("--no-tools") && !a.Any(x => x.StartsWith("--tools=", StringComparison.Ordinal)), "no tools when web search is off");
    }

    public static void ATurnStreamsAndEnds()
    {
        using var r = new Run();
        r.Start();
        Check.True(r.Session.Send("hello", Array.Empty<string>()), "sent");
        Check.True(r.Session.Busy, "busy while it answers");
        r.Until(() => r.Turns == 1, "the turn's end");
        Check.True(r.Ends[0].ok && r.Ends[0].error is null, "completed");
        Check.Eq(r.Said, "Hello, 다시", "the text, in the order it came (Hangul intact)");
        Check.True(!r.Session.Busy, "not busy after it");
        Check.True(r.Statuses.Contains("(clear)"), "the answer starting clears the status");
        // the numbers come after the turn (a get_state of its own)
        r.Until(() => r.Session.Usage is { Tokens: 1200 }, "the context numbers after the turn");
        Check.Eq(r.Session.Usage!.Value.Gauge, "1.2k / 200k", "the gauge's text");
        Check.True(r.UsageChanges > 0, "the gauge was told");
        Check.True(r.Session.Send("again", Array.Empty<string>()), "a second turn, same process");
        r.Until(() => r.Turns == 2, "the second turn");
        r.Until(() => r.Session.Usage is { Tokens: 2400 }, "numbers grow with the chat: the model remembers the first turn");
    }

    public static void SearchShowsAStatus()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("SEARCH the weather", Array.Empty<string>());
        r.Until(() => r.Turns == 1, "the turn");
        Check.True(r.Statuses.Contains("웹 검색 중…"), "the web search line");
    }

    public static void StopEndsTheTurnWithItsText()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("SLOW please", Array.Empty<string>());
        r.Until(() => r.Said.Length > 0, "text before the stop");
        r.Session.Interrupt();
        r.Until(() => r.Turns == 1, "the stop's end");
        Check.True(r.Ends[0].ok, "a stop is no error");
        Check.Eq(r.Said, "Hello, ", "what was written stays");
        Check.True(!r.Session.Busy && r.Session.Alive, "the process goes on for the next message");
        Check.True(r.Session.Send("hello", Array.Empty<string>()), "and takes it");
        r.Until(() => r.Turns == 2, "the next turn");
    }

    public static void AnErrorEndsTheTurnWithOmpsWords()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("FAIL now", Array.Empty<string>());
        r.Until(() => r.Turns == 1, "the turn's end");
        Check.True(!r.Ends[0].ok && r.Ends[0].error == "401 unauthorized", "an error with omp's own text");
        Check.True(AiErrors.Omp(r.Ends[0].error!).Contains("omp login"), "which the chat words as a sign-in problem");
    }

    public static void ASlashCommandAnswersFromOmp()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("/session", Array.Empty<string>());
        r.Until(() => r.Turns == 1, "the command's end");
        Check.True(r.Ends[0].ok, "ok");
        Check.Eq(r.Said, "```\nSession: fake-session\n```", "what omp printed, as a code block");
    }

    public static void TidyingTheEarlierPart()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("hello", Array.Empty<string>());
        r.Until(() => r.Turns == 1, "a turn first");
        r.Until(() => r.Session.Usage is { Tokens: 1200 }, "its numbers");
        Check.True(r.Session.Compact(""), "정리 starts");
        r.Until(() => { lock (r.Gate) return r.Notes.Count >= 2; }, "the finished line");
        string[] notes;
        lock (r.Gate) notes = r.Notes.ToArray();
        Check.Eq(notes[0], "… 대화가 길어져 앞부분을 정리하는 중…", "the running line comes first");
        Check.Eq(notes[1], "앞부분을 요약했습니다 (이전 12k 토큰)", "then what it did, with the tokens it had");
        Check.True(!r.Session.Compacting, "over");
        Check.True(r.Session.Usage is { Tokens: null }, "the numbers are unknown until the next answer");
    }

    public static void ATypedCompactIsATurnOfItsOwn()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("hello", Array.Empty<string>());
        r.Until(() => r.Turns == 1, "a turn first");
        r.Until(() => r.Session.Usage is { Tokens: 1200 }, "its numbers");
        Check.True(r.Session.Send("/compact keep the decisions", Array.Empty<string>()), "sent as a command");
        r.Until(() => r.Turns == 2, "its end");
        Check.True(r.Ends[1].ok, "ok");
        Check.True(r.Said.EndsWith("앞부분을 요약했습니다 (이전 12k 토큰)", StringComparison.Ordinal), "the answer is the finished line");
    }

    public static void ADeadSessionEndsItsTurn()
    {
        using var r = new Run();
        r.Start();
        r.Session.Send("SLOW forever", Array.Empty<string>());
        r.Until(() => r.Said.Length > 0, "text");
        r.Session.Dispose(); // the process is closed under the turn
        r.Until(() => r.Exits == 1, "the process ends");
        Check.True(!r.Session.Alive, "dead");
        Check.True(!r.Session.Send("hello", Array.Empty<string>()), "nothing can be sent to it");
    }

    public static void BloomsToolIsAskedInTheChat()
    {
        using var r = new Run();
        r.Start();
        var asked = new List<ToolAsk>();
        r.Session.ToolAsked += a => { lock (asked) asked.Add(a); };
        Check.True(!r.Session.Ask("pc", "{}").Result, "no question outside a turn: refused");
        r.Session.Send("SLOW use the PC", Array.Empty<string>());
        r.Until(() => r.Said.Length > 0, "the turn is on");
        var task = r.Session.Ask("pc", "{\"do\":\"open\",\"q\":\"notepad\"}");
        Check.True(!task.IsCompleted, "it waits for the user");
        ToolAsk ask;
        lock (asked) ask = asked.Single();
        Check.Eq(ask.Tool, "pc", "the card's tool");
        r.Session.Answer(ask, true);
        Check.True(task.Wait(TimeSpan.FromSeconds(5)) && task.Result, "the yes goes back to the waiting call");
        var second = r.Session.Ask("pc", "{}");
        r.Session.Interrupt(); // a stop withdraws what is still waiting
        Check.True(second.Wait(TimeSpan.FromSeconds(5)) && !second.Result, "a stop answers a waiting question with no");
        r.Until(() => r.Turns == 1, "the stop's end");
    }
}
