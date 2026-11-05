using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.App;
using Bloom.Ui;

namespace Bloom.Tests;

/// 알림 카드에서 승인: Bloom's hook entry in Claude Code's settings.json, the hook run's pipe and output, the request a
/// card shows, and cards telling their notice when they leave.
internal static class ClaudeApprovalTests
{
    private static readonly string[] Real = { ClaudeApprovalHook.Arg };
    private const string Exe = @"C:\Users\x\AppData\Local\BloomLauncher\current\Bloom.exe";

    // what Claude Code writes (JSON.stringify, 2 spaces) with someone else's PermissionRequest hook in it
    private const string Theirs = "{\n  \"theme\": \"dark\",\n  \"hooks\": {\n    \"PermissionRequest\": [\n      {\n        \"matcher\": \"*\",\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"C:/Users/x/.orca/agent-hooks/claude-hook.cmd\",\n            \"timeout\": 10\n          }\n        ]\n      }\n    ],\n    \"Stop\": []\n  },\n  \"env\": {\n    \"NOTE\": \"한글 <b> & 'q'\"\n  }\n}\n";

    public static void HookGoesInBesideTheirsAndOutLeavingTheFileAsItWas()
    {
        string added = Check.NotNull(ClaudeHooks.Edit(Theirs, Real, Exe), "the hook goes in");
        using (var doc = JsonDocument.Parse(added))
        {
            var asks = doc.RootElement.GetProperty("hooks").GetProperty("PermissionRequest");
            Check.Eq(asks.GetArrayLength(), 2, "their group and Bloom's");
            Check.Eq(asks[0].GetProperty("hooks")[0].GetProperty("command").GetString(), "C:/Users/x/.orca/agent-hooks/claude-hook.cmd", "theirs stays first");
            Check.Eq(asks[1].GetProperty("matcher").GetString(), "*", "every tool");
            var ours = asks[1].GetProperty("hooks")[0];
            Check.Eq(ours.GetProperty("command").GetString(), Exe, "Bloom.exe started directly");
            Check.Eq(ours.GetProperty("args").GetArrayLength(), 1, "one argument");
            Check.Eq(ours.GetProperty("args")[0].GetString(), ClaudeApprovalHook.Arg, "the hook's argument");
            Check.Eq(ours.GetProperty("timeout").GetInt32(), ClaudeHooks.TimeoutSeconds, "a card may wait that long");
            Check.Eq(doc.RootElement.GetProperty("env").GetProperty("NOTE").GetString(), "한글 <b> & 'q'", "other settings keep their text");
        }
        Check.True(ClaudeHooks.KeepsLayout(Theirs) && ClaudeHooks.KeepsLayout(added), "Claude Code's own layout is kept as it is");
        Check.Eq(ClaudeHooks.Edit(added, Real, Exe), null, "already in: nothing to write");
        Check.Eq(ClaudeHooks.Edit(added, Real, null), Theirs, "taking it out gives the file back byte for byte");
        Check.Eq(ClaudeHooks.Edit(Theirs, Real, null), null, "nothing of Bloom's: nothing to write");
    }

    public static void TheFileKeepsItsLayout()
    {
        foreach (var (newline, indent) in new[] { ("\r\n", "    "), ("\n", "\t"), ("\r\n", "  ") })
        {
            string theirs = Theirs.Replace("\n", newline, StringComparison.Ordinal).Replace("  ", indent, StringComparison.Ordinal);
            string added = Check.NotNull(ClaudeHooks.Edit(theirs, Real, Exe), "the hook goes in");
            Check.True(added.Contains(newline + indent + "\"hooks\"", StringComparison.Ordinal) && (newline == "\r\n" || !added.Contains('\r')), "written with the file's line ends and indent");
            Check.Eq(ClaudeHooks.Edit(added, Real, null), theirs, $"and back byte for byte ({newline.Length} char line ends, indent «{indent}»)");
        }
    }

    public static void SetKeepsTheBomAndFollowsTheSwitch()
    {
        string home = Path.Combine(Path.GetTempPath(), "bloom-claude-home-" + Guid.NewGuid().ToString("N")[..8]);
        string? before = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Directory.CreateDirectory(home);
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", home);
            string file = Path.Combine(home, "settings.json");
            byte[] original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Theirs.Replace("\n", "\r\n", StringComparison.Ordinal))).ToArray();
            File.WriteAllBytes(file, original);
            Check.True(ClaudeHooks.Set(true), "the hook goes in");
            byte[] on = File.ReadAllBytes(file);
            Check.True(on.Length > original.Length && on[0] == 0xEF && on[1] == 0xBB && on[2] == 0xBF, "the BOM stays");
            Check.True(Encoding.UTF8.GetString(on).Contains(ClaudeApprovalHook.Arg, StringComparison.Ordinal), "with Bloom's entry");
            Check.True(ClaudeHooks.Set(false), "and goes out");
            Check.True(File.ReadAllBytes(file).AsSpan().SequenceEqual(original), "leaving the file as it was, byte for byte");
            Check.True(!File.Exists(file + ".bloom-backup"), "Claude Code's own layout needs no copy aside");

            byte[] byHand = Encoding.UTF8.GetBytes("{\"theme\":\"dark\",\"permissions\":{\"allow\":[\"Read\", \"Bash(ls)\"]},\"note\":\"\\u00e9\"}");
            File.WriteAllBytes(file, byHand);
            Check.True(ClaudeHooks.Set(true), "the hook goes into a file laid out by hand");
            Check.True(File.ReadAllBytes(file + ".bloom-backup").AsSpan().SequenceEqual(byHand), "its original is kept aside, byte for byte");
            Check.True(ClaudeHooks.Set(false), "and goes out");
            using var back = JsonDocument.Parse(File.ReadAllText(file));
            Check.Eq(back.RootElement.GetProperty("permissions").GetProperty("allow")[1].GetString(), "Bash(ls)", "its contents stay");
            Check.Eq(back.RootElement.GetProperty("note").GetString(), "é", "escapes keep what they stand for");
            Check.True(File.ReadAllBytes(file + ".bloom-backup").AsSpan().SequenceEqual(byHand), "the copy aside is the first original, kept");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", before);
            Directory.Delete(home, true);
        }
    }

    public static void NewInstallFolderReplacesTheOldPath()
    {
        string old = ClaudeHooks.Edit(Theirs, Real, @"D:\old\Bloom.exe")!;
        string moved = Check.NotNull(ClaudeHooks.Edit(old, Real, Exe), "a new path is written");
        using var doc = JsonDocument.Parse(moved);
        var asks = doc.RootElement.GetProperty("hooks").GetProperty("PermissionRequest");
        Check.Eq(asks.GetArrayLength(), 2, "still one entry of Bloom's");
        Check.Eq(asks[1].GetProperty("hooks")[0].GetProperty("command").GetString(), Exe, "at the new path");
    }

    public static void DevCopyKeepsItsOwnEntry()
    {
        string[] dev = { ClaudeApprovalHook.Arg, "--data", @"C:\dev\data" };
        string both = ClaudeHooks.Edit(ClaudeHooks.Edit(Theirs, Real, Exe), dev, @"C:\dev\Bloom.exe")!;
        string devGone = Check.NotNull(ClaudeHooks.Edit(both, dev, null), "the dev entry goes");
        Check.Eq(devGone, ClaudeHooks.Edit(Theirs, Real, Exe), "the installed Bloom's entry stays");
    }

    public static void NoSettingsFileGetsOneAndForeignShapesAreLeftAlone()
    {
        string made = Check.NotNull(ClaudeHooks.Edit(null, Real, Exe), "a file is made");
        using (var doc = JsonDocument.Parse(made))
            Check.Eq(doc.RootElement.GetProperty("hooks").GetProperty("PermissionRequest").GetArrayLength(), 1, "with the hook alone");
        Check.Eq(ClaudeHooks.Edit(made, Real, null), "{}", "taking it out leaves an empty object");
        foreach (string bad in new[] { "[1]", "{\"hooks\": 3}", "{\"hooks\": {\"PermissionRequest\": {}}}", "{ not json" })
        {
            bool threw = false;
            try
            {
                ClaudeHooks.Edit(bad, Real, Exe);
            }
            catch (JsonException)
            {
                threw = true;
            }
            Check.True(threw, "an unexpected settings.json is not rewritten: " + bad);
        }
    }

    public static void HookOutputIsClaudeCodesDecision()
    {
        using (var allow = JsonDocument.Parse(ClaudeApprovalHook.Output("allow")!))
        {
            var o = allow.RootElement.GetProperty("hookSpecificOutput");
            Check.Eq(o.GetProperty("hookEventName").GetString(), "PermissionRequest", "event name");
            Check.Eq(o.GetProperty("decision").GetProperty("behavior").GetString(), "allow", "allow");
        }
        using (var deny = JsonDocument.Parse(ClaudeApprovalHook.Output("deny")!))
        {
            var d = deny.RootElement.GetProperty("hookSpecificOutput").GetProperty("decision");
            Check.Eq(d.GetProperty("behavior").GetString(), "deny", "deny");
            Check.True(d.GetProperty("message").GetString()!.Length > 0, "deny tells Claude why");
        }
        Check.Eq(ClaudeApprovalHook.Output(null), null, "no answer: no decision, the terminal asks");
    }

    public static void HookRunCarriesTheRequestAndReadsTheAnswer()
    {
        foreach (var (reply, expected) in new[] { ("allow", "allow"), ("deny", "deny"), ("", null) })
        {
            string name = "bloom-test-approve-" + Guid.NewGuid().ToString("N")[..12];
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var serve = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                var reader = new StreamReader(server, new UTF8Encoding(false));
                string line = (await reader.ReadLineAsync())!;
                var writer = new StreamWriter(server, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                await writer.WriteLineAsync(reply);
                return line;
            });
            string? got = ClaudeApprovalHook.Ask(name, 4242, "{\"tool_name\":\"Bash\"}");
            Check.Eq(got, expected, $"Bloom's \"{reply}\"");
            using var sent = JsonDocument.Parse(serve.Result);
            Check.Eq(sent.RootElement.GetProperty("pid").GetInt32(), 4242, "the asking Claude Code's process");
            Check.Eq(sent.RootElement.GetProperty("input").GetString(), "{\"tool_name\":\"Bash\"}", "the hook's input as it came");
        }
        var clock = Stopwatch.StartNew();
        Check.Eq(ClaudeApprovalHook.Ask("bloom-test-none-" + Guid.NewGuid().ToString("N")[..12], 1, "{}"), null, "no Bloom running: no decision");
        Check.True(clock.ElapsedMilliseconds < 500, $"and no wait for it ({clock.ElapsedMilliseconds} ms)");
    }

    public static void RequestShowsAllItWouldDoOrLeavesItToTheTerminal()
    {
        static string Line(object input) => JsonSerializer.Serialize(new { pid = 7, input = JsonSerializer.Serialize(input) });
        var bash = Check.NotNull(ClaudeApprovals.Request.Parse(Line(new { session_id = "s1", cwd = @"C:\work\app", tool_name = "Bash", tool_input = new { description = "Run tests", command = "npm test\r\nrm -rf build", run_in_background = true } })), "a Bash request");
        Check.Eq(bash.Pid, 7, "pid");
        Check.Eq(bash.Session, "s1", "session");
        Check.Eq(bash.Tool, "Bash", "tool");
        Check.Eq(bash.Body, "Run tests\ncommand: npm test\nrm -rf build\nrun_in_background: true", "every field, every line of the command");
        Check.True(bash.Fits, "short enough for the card");
        Check.Eq(bash.Cwd, @"C:\work\app", "folder");
        var mcp = Check.NotNull(ClaudeApprovals.Request.Parse(Line(new { session_id = "s2", tool_name = "mcp__blender__render", tool_input = new { frames = 3 } })), "an MCP request");
        Check.Eq(mcp.Body, "frames: 3", "no text: its fields");
        var edit = Check.NotNull(ClaudeApprovals.Request.Parse(Line(new { session_id = "s3", tool_name = "Edit", tool_input = new { file_path = @"C:\a.cs", old_string = new string('x', 400), new_string = new string('y', 400) } })), "an Edit request");
        Check.True(!edit.Fits, "too long to show whole: the terminal asks");
        var lines = Check.NotNull(ClaudeApprovals.Request.Parse(Line(new { session_id = "s4", tool_name = "Bash", tool_input = new { command = string.Join("\n", Enumerable.Range(1, ClaudeApprovals.Request.MaxLines + 1).Select(i => "echo " + i)) } })), "a long script");
        Check.True(!lines.Fits, "more lines than the card shows: the terminal asks");
        Check.Eq(ClaudeApprovals.Request.Parse("{\"pid\": 1}"), null, "no input");
        Check.Eq(ClaudeApprovals.Request.Parse("{\"pid\": 1, \"input\": \"[1]\"}"), null, "input not an object");
        Check.Eq(ClaudeApprovals.Request.Parse("nope"), null, "not JSON");
    }

    public static void CardsLeavingTellTheirNotice()
    {
        var window = new NoticeWindow { Offscreen = true };
        try
        {
            int gone = 0;
            for (int i = 0; i < 5; i++) window.Add(new Notice("waiting", "Claude Code · 승인 요청", "card " + i, null, "k" + i, Gone: () => gone++));
            Check.Eq(gone, 1, "a fifth card pushes the oldest out, and its notice hears it");
            window.Add(new Notice("waiting", "Claude Code · 승인 요청", "again", null, "k4", Gone: () => gone++));
            Check.Eq(gone, 2, "a card replaced by one with its key hears it");
            window.Clear();
            Check.Eq(gone, 6, "clearing tells the four cards left");
        }
        finally
        {
            window.Close();
        }
    }

    public static void PreviewsNeverPushRealCardsOut()
    {
        var window = new NoticeWindow { Offscreen = true };
        try
        {
            int gone = 0;
            for (int i = 0; i < 3; i++) window.Add(new Notice("waiting", "Claude Code · 승인 요청", "card " + i, null, "k" + i, Gone: () => gone++));
            window.Add(new Notice("done", "Codex · 작업 완료", "real", null, Gone: () => gone++));
            window.Add(new Notice("done", "Codex · 작업 완료", "preview", null, Sample: true));
            window.Add(new Notice("done", "Codex · 작업 완료", "preview 2", null, Sample: true));
            Check.Eq(gone, 0, "a preview past four cards goes itself, never an approval or a real card");
            window.ClearSamples();
            window.Hold(true);
            window.Hold(false);
            Check.Eq(gone, 0, "previews cleared, the window held and let go: the real cards all stay");
            window.Clear();
            Check.Eq(gone, 4, "the four real cards were there to the end");
        }
        finally
        {
            window.Close();
        }
    }
}
