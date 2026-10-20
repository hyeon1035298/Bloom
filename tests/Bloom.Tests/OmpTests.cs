using System;
using System.IO;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// omp as an AI: the arguments of its two uses, how its rpc lines read, the slash-command policy, the model list and ids.
internal static class OmpTests
{
    public static void OneShotArgsAreToolless()
    {
        var a = OmpCli.OneShotArgs("anthropic/claude-haiku-5-5", null, "Translate.");
        Check.True(a.Contains("-p") && a.Contains("--no-session") && a.Contains("--no-tools"), "print mode, no session, no tools");
        Check.True(a.Contains("--no-skills") && a.Contains("--no-rules") && a.Contains("--no-extensions"), "nothing of omp's own is loaded");
        Check.Eq(a[a.IndexOf("--model") + 1], "anthropic/claude-haiku-5-5", "model");
        Check.True(a.Contains("--thinking=off"), "thinking off when no effort");
        Check.True(a.Contains("--system-prompt=Translate."), "Bloom's prompt replaces omp's, in one argument");
        Check.True(!a.Any(x => x.Contains("Translate") && !x.StartsWith("--system-prompt=")), "the question is not an argument (it goes on stdin)");
    }

    public static void OneShotLevel()
    {
        Check.True(OmpCli.OneShotArgs("m/x", "high", "s").Contains("--thinking=high"), "high");
        Check.True(OmpCli.OneShotArgs("m/x", "fast", "s").Contains("--thinking=off"), "the Claude side's 'fast' is off");
        Check.Eq(OmpCli.Level("nonsense"), "off", "unknown level");
        Check.Eq(OmpCli.Level("xhigh"), "xhigh", "xhigh");
        Check.Eq(OmpCli.Level("minimal"), "minimal", "minimal");
    }

    public static void NoModelGivenMeansOmpsOwn()
    {
        Check.True(!OmpCli.OneShotArgs("", null, "s").Contains("--model"), "omp picks the model itself");
        Check.True(!OmpCli.ChatArgs("", "medium", "S", "d", null, false).Contains("--model"), "omp picks the chat's model itself");
        Check.True(OmpCli.ChatArgs("a/b", "medium", "S", "d", null, false).Contains("--model"), "a model is passed when set");
    }

    public static void ChatArgsWithSearch()
    {
        var a = OmpCli.ChatArgs("anthropic/claude-sonnet-5-5", "medium", "Sys", @"C:\s", "abc-123", true);
        Check.True(a.Contains("--mode") && a[a.IndexOf("--mode") + 1] == "rpc" && a.Contains("--no-ui"), "rpc without UI");
        Check.True(a.Contains("--tools=web_search") && !a.Contains("--no-tools"), "web search is the only tool");
        Check.True(a.Contains("--resume=abc-123") && a.Contains(@"--session-dir=C:\s"), "resume by id from Bloom's folder");
        Check.True(a.Contains("--thinking=medium") && a.Contains("--system-prompt=Sys"), "level and prompt");
        Check.True(a.Contains("--no-skills") && a.Contains("--no-rules") && a.Contains("--no-extensions"), "nothing of the user's own omp setup is loaded");
    }

    public static void ChatArgsWithoutSearch()
    {
        var a = OmpCli.ChatArgs("m/x", "off", "S", "d", null, false);
        Check.True(a.Contains("--no-tools") && !a.Any(x => x.StartsWith("--tools")), "no tools at all");
        Check.True(!a.Any(x => x.StartsWith("--resume")), "fresh session");
    }

    public static void ShortNames()
    {
        Check.Eq(OmpCli.ShortName("anthropic/claude-sonnet-5-5"), "Sonnet 5.5", "sonnet");
        Check.Eq(OmpCli.ShortName("anthropic/claude-haiku-5-5"), "Haiku 5.5", "haiku");
        Check.Eq(OmpCli.ShortName("commandcode/deepseek/deepseek-v4.1-flash"), "Deepseek V4.1 Flash", "nested provider");
        Check.Eq(OmpCli.ShortName("anthropic/claude-opus-5"), "Opus 5", "single digit");
        Check.Eq(OmpCli.ShortName(""), "기본 모델", "omp's own default");
    }

    public static void FindsOmpWhereItIs()
    {
        string root = Path.Combine(Path.GetTempPath(), "bloom-omp-find-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bin = Path.Combine(root, "bin"), bun = Path.Combine(root, "home", ".bun", "bin");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(bun);
            Check.True(OmpCli.Find(null, bin, Path.Combine(root, "home")) is null, "nowhere: not found");
            File.WriteAllText(Path.Combine(bun, "omp.exe"), "");
            Check.Eq(OmpCli.Find(null, bin, Path.Combine(root, "home")), Path.Combine(bun, "omp.exe"), "where bun puts global programs");
            File.WriteAllText(Path.Combine(bin, "omp.exe"), "");
            Check.Eq(OmpCli.Find(null, "C:\\no-such-folder;\"" + bin + "\"", Path.Combine(root, "home")), Path.Combine(bin, "omp.exe"), "the PATH comes first");
            string own = Path.Combine(root, "mine.exe");
            File.WriteAllText(own, "");
            Check.Eq(OmpCli.Find(own, bin, Path.Combine(root, "home")), own, "the path in the settings comes before all");
            Check.Eq(OmpCli.Find(Path.Combine(root, "gone.exe"), bin, Path.Combine(root, "home")), Path.Combine(bin, "omp.exe"), "a path that is not there falls back to the lookup");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    public static void RpcTextDeltas()
    {
        var e = OmpEvent.Parse("{\"type\":\"message_update\",\"assistantMessageEvent\":{\"type\":\"text_delta\",\"contentIndex\":0,\"delta\":\"안녕\",\"partial\":{}}}");
        Check.Eq(e.Kind, OmpKind.Delta, "delta");
        Check.Eq(e.Text, "안녕", "its text");
        Check.Eq(OmpEvent.Parse("{\"type\":\"message_update\",\"assistantMessageEvent\":{\"type\":\"text_start\",\"contentIndex\":0}}").Kind, OmpKind.StatusClear, "an answer starts: the status goes");
        Check.Eq(OmpEvent.Parse("{\"type\":\"message_update\",\"assistantMessageEvent\":{\"type\":\"thinking_delta\",\"delta\":\"x\"}}").Kind, OmpKind.None, "thinking is not shown");
    }

    public static void RpcToolStatus()
    {
        var e = OmpEvent.Parse("{\"type\":\"tool_execution_start\",\"toolCallId\":\"t\",\"toolName\":\"web_search\",\"args\":{}}");
        Check.Eq(e.Kind, OmpKind.Status, "status");
        Check.Eq(e.Text, "웹 검색 중…", "web search line");
        Check.Eq(OmpEvent.Parse("{\"type\":\"tool_execution_start\",\"toolName\":\"mcp__bloom_pc\"}").Text, "PC에서 작업하는 중…", "Bloom's PC tool");
        Check.Eq(OmpEvent.Parse("{\"type\":\"tool_execution_start\",\"toolName\":\"mcp__git_issue\"}").Text, "도구 쓰는 중…", "another MCP server's tool");
    }

    public static void RpcTurnEnds()
    {
        var ok = OmpEvent.Parse("{\"type\":\"prompt_result\",\"id\":\"p1\",\"agentInvoked\":true,\"status\":\"completed\",\"sessionSettled\":true}");
        Check.True(ok.Kind == OmpKind.TurnEnded && ok.Ok, "completed is ok");
        var stopped = OmpEvent.Parse("{\"type\":\"prompt_result\",\"id\":\"p1\",\"status\":\"aborted\"}");
        Check.True(stopped.Kind == OmpKind.TurnEnded && stopped.Ok, "the user's stop keeps the text, no error");
        var bad = OmpEvent.Parse("{\"type\":\"prompt_result\",\"id\":\"p1\",\"status\":\"error\",\"error\":\"429 rate limit\"}");
        Check.True(bad.Kind == OmpKind.TurnEnded && !bad.Ok && bad.Text == "429 rate limit", "an error with omp's words");
        var refused = OmpEvent.Parse("{\"id\":\"p2\",\"type\":\"response\",\"command\":\"prompt\",\"success\":false,\"error\":\"busy\"}");
        Check.True(refused.Kind == OmpKind.TurnEnded && !refused.Ok && refused.Text == "busy", "a refused prompt ends the turn");
        Check.Eq(OmpEvent.Parse("{\"type\":\"response\",\"command\":\"prompt\",\"success\":true}").Kind, OmpKind.None, "an accepted prompt is not an end");
    }

    public static void RpcContextUsage()
    {
        var e = OmpEvent.Parse("{\"id\":\"state\",\"type\":\"response\",\"command\":\"get_state\",\"success\":true,\"data\":{\"sessionId\":\"s1\",\"contextUsage\":{\"tokens\":14200,\"contextWindow\":200000,\"percent\":7.1}}}");
        Check.True(e.Kind == OmpKind.SessionId && e.Text == "s1" && e.Usage == new OmpUsage(14200, 200000), "the session id and the numbers of one get_state");
        var again = OmpEvent.Parse("{\"id\":\"usage\",\"type\":\"response\",\"command\":\"get_state\",\"success\":true,\"data\":{\"contextUsage\":{\"tokens\":null,\"contextWindow\":100000,\"percent\":0}}}");
        Check.True(again.Kind == OmpKind.Usage && again.Usage is { Tokens: null, Window: 100000 }, "tokens may be null");
        var none = OmpEvent.Parse("{\"type\":\"response\",\"command\":\"get_state\",\"success\":true,\"data\":{\"model\":{}}}");
        Check.True(none.Kind == OmpKind.Usage && none.Usage is null, "no contextUsage: still an answer, no numbers");
    }

    public static void GaugeText()
    {
        Check.Eq(new OmpUsage(14200, 200000).Gauge, "14.2k / 200k", "14.2k / 200k");
        Check.Eq(new OmpUsage(850, 1_000_000).Gauge, "850 / 1M", "small count, a million window");
        Check.Eq(new OmpUsage(null, 100000).Gauge, "? / 100k", "unknown count");
        Check.Eq(new OmpUsage(123456, 200000).Gauge, "123k / 200k", "three digits drop the decimal");
        Check.Near(new OmpUsage(150000, 200000).Percent, 75, 0.001, "percent");
        Check.Eq(new OmpUsage(null, 100000).Percent, 0.0, "no count, no percent");
    }

    public static void AutoCompaction()
    {
        var start = OmpEvent.Parse("{\"type\":\"auto_compaction_start\",\"reason\":\"threshold\",\"action\":\"remote\"}");
        Check.True(start.Kind == OmpKind.CompactStart && start.Text == "remote", "start");
        var end = OmpEvent.Parse("{\"type\":\"auto_compaction_end\",\"action\":\"remote\",\"aborted\":false,\"willRetry\":false,\"result\":{\"summary\":\"x\",\"tokensBefore\":28275}}");
        Check.True(end.Kind == OmpKind.CompactEnd && end.Detail == "done" && end.Before == 28275 && end.Ok, "end with its tokens before");
        Check.Eq(OmpEvent.Parse("{\"type\":\"auto_compaction_end\",\"action\":\"remote\",\"aborted\":false,\"willRetry\":false,\"skipped\":true,\"result\":{}}").Detail, "skipped", "skipped");
        Check.Eq(OmpEvent.Parse("{\"type\":\"auto_compaction_end\",\"action\":\"soft\",\"aborted\":true}").Detail, "aborted", "aborted");
        Check.Eq(OmpEvent.Parse("{\"type\":\"auto_compaction_end\",\"action\":\"soft\",\"aborted\":false}").Detail, "failed", "no result and not skipped is a failure");
        Check.Eq(OmpEvent.CompactEndText("remote", 37600, 19400, false, false), "앞부분을 요약했습니다 (37.6k→19.4k 토큰)", "the finished line with its numbers");
        Check.Eq(OmpEvent.CompactEndText("remote", 37600, -1, false, false), "앞부분을 요약했습니다 (이전 37.6k 토큰)", "only the old count");
        Check.True(OmpEvent.CompactEndText("shake", -1, -1, false, false).StartsWith("긴 도구 결과를 덜어냈습니다"), "a shake says what it did, not 'summarised'");
        Check.True(OmpEvent.CompactStartText("shake").Contains("도구 결과") && OmpEvent.CompactStartText("remote").Contains("앞부분"), "start lines by kind");
    }

    public static void ManualCompact()
    {
        var ok = OmpEvent.Parse("{\"id\":\"compact\",\"type\":\"response\",\"command\":\"compact\",\"success\":true,\"data\":{\"summary\":\"s\",\"tokensBefore\":37607}}");
        Check.True(ok.Kind == OmpKind.Compacted && ok.Before == 37607, "compact answered");
        var small = OmpEvent.Parse("{\"id\":\"compact\",\"type\":\"response\",\"command\":\"compact\",\"success\":false,\"error\":\"Nothing to compact (session too small)\"}");
        Check.True(small.Kind == OmpKind.CompactFailed && small.Text.Contains("too small"), "refused");
        Check.Eq(OmpEvent.CompactFailText(small.Text), "아직 정리할 만큼 길지 않습니다", "too small, in words");
        Check.Eq(OmpEvent.CompactFailText("Already compacted"), "이미 정리했습니다 — 더 줄일 내용이 없습니다", "already done");
        Check.Eq(OmpCli.CompactCommand("/compact"), "", "bare");
        Check.Eq(OmpCli.CompactCommand(" /compact 결정만 남겨 "), "결정만 남겨", "with instructions");
        Check.Eq(OmpCli.CompactCommand("/compactor"), null, "another word");
        Check.Eq(OmpCli.CompactCommand("compact"), null, "no slash");
    }

    public static void SlashCommands()
    {
        var out1 = OmpEvent.Parse("{\"type\":\"command_output\",\"text\":\"Session: 01a1\\nCWD: C:\\\\x\"}");
        Check.True(out1.Kind == OmpKind.CommandOutput && out1.Text.StartsWith("Session: 01a1"), "command output");
        var done = OmpEvent.Parse("{\"id\":\"p3\",\"type\":\"response\",\"command\":\"prompt\",\"success\":true,\"data\":{\"agentInvoked\":false}}");
        Check.Eq(done.Kind, OmpKind.LocalDone, "agentInvoked false ends the turn at once");
        Check.Eq(OmpEvent.Parse("{\"id\":\"p3\",\"type\":\"response\",\"command\":\"prompt\",\"success\":true,\"data\":{\"agentInvoked\":true}}").Kind, OmpKind.None, "an agent turn still waits for its prompt_result");
        Check.True(OmpCli.IsSlash("/session") && OmpCli.IsSlash("/model x") && !OmpCli.IsSlash("/") && !OmpCli.IsSlash("/ 1") && !OmpCli.IsSlash("a /b"), "what counts as a slash command");
    }

    /// What a Bloom chat does with "/word": read-only commands go to omp, ones that change the shared omp (or share the
    /// session outside, or swap the model/session Bloom tracks) are refused, anything else (a path) stays a message.
    public static void SlashPolicy()
    {
        Check.Eq(OmpCli.SlashName("  /Session  now"), "session", "name of a command with words after it");
        foreach (string c in new[] { "/share", "/fast", "/mcp add x", "/plugins", "/model anthropic/x", "/new", "/export a.html" })
            Check.True(OmpCli.SlashRefused.Contains(OmpCli.SlashName(c)) && !OmpCli.SlashAllowed.Contains(OmpCli.SlashName(c)), "refused: " + c);
        foreach (string c in new[] { "/compact", "/compact keep decisions", "/shake", "/session", "/context", "/usage" })
            Check.True(OmpCli.SlashAllowed.Contains(OmpCli.SlashName(c)), "passed to omp: " + c);
        string path = OmpCli.SlashName("/etc/hosts 이게 뭐야");
        Check.True(!OmpCli.SlashAllowed.Contains(path) && !OmpCli.SlashRefused.Contains(path), "a path is an ordinary message");
    }

    public static void SlashAnsweredHere()
    {
        Check.True(Chats.OmpLocalAnswer("resume")!.Contains("왼쪽 목록"), "/resume: where Bloom keeps earlier chats");
        string refused = Chats.OmpLocalAnswer("fast")!;
        Check.True(refused.Contains("`/fast`") && refused.Contains("`/compact`") && refused.Contains("`/session`"), "a refused command names itself and what works here");
        Check.True(Chats.OmpLocalAnswer("session") is null && Chats.OmpLocalAnswer("compact") is null, "an allowed command goes to omp");
        Check.True(Chats.OmpLocalAnswer("etc") is null, "a path goes to omp as a message");
        Lang.Force(true);
        try
        {
            Check.True(Chats.OmpLocalAnswer("resume")!.Contains("list on the left"), "in English");
        }
        finally
        {
            Lang.Force(false);
        }
    }

    public static void RpcSessionId()
    {
        var e = OmpEvent.Parse("{\"id\":\"state\",\"type\":\"response\",\"command\":\"get_state\",\"success\":true,\"data\":{\"sessionId\":\"01a1-xyz\",\"model\":{}}}");
        Check.Eq(e.Kind, OmpKind.SessionId, "session id");
        Check.Eq(e.Text, "01a1-xyz", "its value");
    }

    public static void RpcNoise()
    {
        Check.Eq(OmpEvent.Parse("").Kind, OmpKind.None, "empty");
        Check.Eq(OmpEvent.Parse("Working...").Kind, OmpKind.None, "a log line");
        Check.Eq(OmpEvent.Parse("{\"type\":\"ready\",\"protocolVersion\":1}").Kind, OmpKind.None, "ready");
        Check.Eq(OmpEvent.Parse("{broken").Kind, OmpKind.None, "broken json");
    }

    /// What `omp models` prints (shortened): a "provider (n)" line, then a table of model │ context │ max-out │ thinking │ images.
    private const string Table = """
        anthropic (2)
        ┌────────────────┬─────────┬─────────┬─────────────┬────────┐
        │ model          │ context │ max-out │ thinking    │ images │
        ├────────────────┼─────────┼─────────┼─────────────┼────────┤
        │ claude-haiku-4 │    200K │     64K │ minimal,low │ yes    │
        │ claude-old     │    200K │    8.2K │ -           │ no     │
        └────────────────┴─────────┴─────────┴─────────────┴────────┘

        commandcode (1)
        ┌─────────────────────────────┬─────────┬─────────┬──────────┬────────┐
        │ model                       │ context │ max-out │ thinking │ images │
        ├─────────────────────────────┼─────────┼─────────┼──────────┼────────┤
        │ deepseek/deepseek-v4.1-flash│    128K │     32K │ low,high │ yes    │
        └─────────────────────────────┴─────────┴─────────┴──────────┴────────┘
        """;

    public static void CatalogParse()
    {
        var list = OmpCatalog.Parse(Table.Replace("\n", "\r\n"));
        Check.Eq(list.Count, 3, "three models");
        Check.Eq(list[0].Slug, "anthropic/claude-haiku-4", "provider/model");
        Check.Eq(string.Join(",", list[0].Thinking), "minimal,low", "levels");
        Check.True(list[0].Images && !list[1].Images, "images column");
        Check.Eq(list[1].Thinking.Length, 0, "'-' = no thinking");
        Check.Eq(list[2].Slug, "commandcode/deepseek/deepseek-v4.1-flash", "a model that has a slash itself");
        Check.Eq(OmpCatalog.Parse("nothing useful").Count, 0, "no table");
        Check.Eq(OmpCatalog.Parse("").Count, 0, "empty output");
    }

    public static void ProviderOfEntry()
    {
        var list = OmpCatalog.Parse(Table);
        Check.Eq(list[0].Provider, "anthropic", "provider");
        Check.Eq(list[2].Provider, "commandcode", "provider of a slash-in-model entry");
        Check.Eq(OmpModelList.Provider("vendor/some-model"), "vendor", "provider of a slug");
    }

    public static void MenuModelsFollowOmpAndTheUsersList()
    {
        var s = Settings.Current;
        var keep = s.OmpModels.ToList();
        try
        {
            s.OmpModels.Clear();
            Check.Eq(OmpModelList.Ids(false).Count, 0, "no omp on this PC: nothing to pick, no omp in the menus");
            Check.Eq(OmpModelList.Slugs(false).Count, 0, "none either");
            Check.Eq(string.Join(",", OmpModelList.Ids(true)), "omp:", "an empty list shows omp's own default model only");
            Check.True(OmpModelList.Add("anthropic/claude-sonnet-5-5") && OmpModelList.Add("vendor/x"), "added");
            Check.Eq(string.Join(",", OmpModelList.Ids(true)), "omp:,omp:anthropic/claude-sonnet-5-5,omp:vendor/x", "the user's own list follows it");
            Check.Eq(OmpModelList.Ids(false).Count, 0, "and still nothing without omp");
        }
        finally
        {
            s.OmpModels.Clear();
            s.OmpModels.AddRange(keep);
        }
    }

    public static void ListedModelNames()
    {
        var s = Settings.Current;
        var keep = s.OmpModels.ToList();
        try
        {
            s.OmpModels.Clear();
            OmpModelList.Add("anthropic/claude-haiku-5-5");
            OmpModelList.Add("vendor/gemini-3.8-flash");
            Check.Eq(AiModels.Name(AiModels.Omp("vendor/gemini-3.8-flash")), "Gemini 3.8 Flash", "made-up name");
            OmpModelList.Rename("vendor/gemini-3.8-flash", "Gemini 빠름");
            Check.Eq(AiModels.Who(AiModels.Omp("vendor/gemini-3.8-flash")), "omp · Gemini 빠름", "own name");
            OmpModelList.Add("other/gemini-3.8-flash");
            OmpModelList.Rename("vendor/gemini-3.8-flash", "");
            Check.Eq(OmpModelList.NameOf("other/gemini-3.8-flash"), "Gemini 3.8 Flash (other)", "alike names get their provider");
            Check.True(!OmpModelList.Add("other/gemini-3.8-flash") && !OmpModelList.Add("nothing"), "no duplicate, provider/model only");
            OmpModelList.SetEffort("anthropic/claude-haiku-5-5", "low");
            Check.Eq(OmpModelList.EffortOf("anthropic/claude-haiku-5-5"), "low", "its own level");
            OmpModelList.Move("anthropic/claude-haiku-5-5", 1);
            Check.Eq(s.OmpModels[1].Slug, "anthropic/claude-haiku-5-5", "moved down");
            OmpModelList.Remove("other/gemini-3.8-flash");
            Check.True(!OmpModelList.Contains("other/gemini-3.8-flash"), "removed");
        }
        finally
        {
            s.OmpModels.Clear();
            s.OmpModels.AddRange(keep);
        }
    }

    public static void ModelIds()
    {
        string id = AiModels.Omp("anthropic/claude-sonnet-5-5");
        Check.True(AiModels.IsOmp(id) && !AiModels.IsChatGpt(id) && !AiModels.IsApi(id), "an omp id");
        Check.Eq(AiModels.Provider(id), "omp", "provider");
        Check.Eq(AiModels.OmpSlug(id), "anthropic/claude-sonnet-5-5", "slug back");
        Check.Eq(AiModels.OmpSlug("sonnet"), null, "not an omp id");
        Check.Eq(AiModels.Provider("sonnet"), "claude", "claude unchanged");
        Check.Eq(AiModels.Provider(AiModels.Gpt("gpt-x")), "chatgpt", "chatgpt unchanged");
        Check.Eq(AiModels.Who(id), "omp · Sonnet 5.5", "who");
        Check.Eq(AiModels.Who(AiModels.Omp("")), "omp · 기본 모델", "omp's own default model");
        Check.True(AiModels.Inline(id), "omp has no file tools in a chat: files go in the message");
        Check.Eq(AiModels.EffortName("off"), "끔", "off in Korean");
    }

    /// The default is the original one, and omp is only one more side to pick.
    public static void OmpIsNotTheDefault()
    {
        var s = new Settings();
        Check.Eq(s.AiProvider, "claude", "new chats start on Claude");
        Check.Eq(s.TranslateProvider, "claude", "translation too");
        Check.Eq(s.OmpChatModel, "", "omp's own default model unless one is picked");
        Check.True(!AiModels.Sides().Contains("omp") || OmpCli.FindCli() is not null, "omp is turned through only when it is here");
        Check.True(AiModels.Sides().IndexOf("claude") < AiModels.Sides().IndexOf("chatgpt"), "Claude then ChatGPT as before");
    }

    public static void ThisSidesDefaultsAndLevels()
    {
        var s = Settings.Current;
        var keep = (s.AiProvider, s.OmpChatModel, s.OmpChatEffort);
        var listed = s.OmpModels.ToList();
        try
        {
            s.OmpModels.Clear();
            s.AiProvider = "omp";
            s.OmpChatModel = "";
            s.OmpChatEffort = "high";
            Check.Eq(AiModels.DefaultModel(), "omp:", "new chats use omp's own default model");
            Check.Eq(AiModels.DefaultEffort("omp:"), "high", "and the settings' level");
            s.OmpChatModel = "vendor/m";
            Check.Eq(AiModels.DefaultModel(), "omp:vendor/m", "or the one picked");
            OmpModelList.Add("vendor/m");
            OmpModelList.SetEffort("vendor/m", "low");
            Check.Eq(AiModels.DefaultEffort("omp:vendor/m"), "low", "a listed model's own starting level wins");
            var t = new ChatThread { Model = "sonnet", Effort = "high" };
            Chats.SetModel(t, "omp:vendor/m");
            Check.True(t.Model == "omp:vendor/m" && t.Effort == "low" && s.AiProvider == "omp" && s.OmpChatModel == "vendor/m", "picking an omp model in a chat saves it as the side's default");
            Chats.SetEffort(t, "max");
            Check.Eq(s.OmpChatEffort, "max", "the level is kept per side");
        }
        finally
        {
            (s.AiProvider, s.OmpChatModel, s.OmpChatEffort) = keep;
            s.OmpModels.Clear();
            s.OmpModels.AddRange(listed);
        }
    }

    public static void LevelsOfAnUnknownModel()
    {
        string id = AiModels.Omp("someone/unlisted-model");
        Check.True(AiModels.HasEffort(id), "the usual levels");
        Check.Eq(AiModels.Fit(id, "off"), "off", "off stays");
        Check.Eq(AiModels.Fit(id, "medium"), "medium", "medium stays");
        Check.Eq(AiModels.Fit("omp:", "xhigh"), "xhigh", "omp's own default takes the usual levels too");
    }

    public static void ChatTitleMarkForOmp()
    {
        Check.Eq(ChatTitles.Mark(AiModels.Omp("anthropic/claude-opus-5-5")), "◆ Opus", "opus");
        Check.Eq(ChatTitles.Mark(AiModels.Omp("anthropic/claude-haiku-5-5")), "∴ Haiku", "haiku");
        Check.Eq(ChatTitles.Mark(AiModels.Omp("commandcode/deepseek/deepseek-v4.1-flash")), "Deepseek V4.1 Flash", "another maker keeps its name");
        Check.Eq(ChatTitles.Mark(AiModels.Omp("")), "omp", "omp's own default is named omp, not marked as a Claude");
    }

    public static void ErrorsInWords()
    {
        Check.True(AiErrors.Omp("401 unauthorized").Contains("omp login"), "a sign-in problem says how");
        Check.True(AiErrors.Omp("429 rate limit").Contains("한도"), "a limit is the provider's");
        Check.Eq(AiErrors.Omp(AiErrors.OmpMissing), AiErrors.OmpMissing, "Bloom's own text passes through");
    }

    public static void TranslationSideNeedsOmp()
    {
        var s = Settings.Current;
        string keep = s.TranslateProvider;
        try
        {
            s.TranslateProvider = "omp";
            bool here = OmpCli.FindCli() is not null;
            Check.Eq(Translator.Provider(), here ? "omp" : "claude", "omp translates only while it is on this PC, else Claude");
            Check.Eq(Translator.Providers().Contains("omp"), here, "and is offered only then");
            Check.Eq(Translator.Model("omp"), "omp:" + s.OmpFastModel, "its quick-job model");
            Check.True(Translator.Levels("omp").Length > 1, "with levels");
        }
        finally
        {
            s.TranslateProvider = keep;
        }
    }
}
