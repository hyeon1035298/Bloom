using System;
using System.IO;
using System.Threading;
using Bloom.Search;

namespace Bloom.Tests;

/// External search providers: the config, the keyword match, the adapter's JSON and the url scheme gate.
internal static class ProvidersTests
{
    private const string Config = """
        [
          {"keyword":"ff","name":"파일 찾기","command":["powershell","C:\\a\\ff.ps1"],"cwd":"C:\\a","minChars":2,"timeoutMs":5000},
          {"keyword":"ec","command":["powershell","ec.ps1"]},
          {"keyword":"","command":["x"]},
          {"keyword":"bad word","command":["x"]},
          {"keyword":"nocmd","command":[]}
        ]
        """;

    public static void ConfigReads()
    {
        var list = Providers.ParseConfig(Config);
        Check.Eq(list.Count, 2, "entries without a keyword or command are skipped");
        Check.Eq(list[0].Name, "파일 찾기", "name");
        Check.Eq(list[0].Command[1], @"C:\a\ff.ps1", "command words");
        Check.Eq(list[0].MinChars, 2, "minChars");
        Check.Eq(list[0].TimeoutMs, 5000, "timeoutMs");
        Check.Eq(list[1].Name, "ec", "no name: the keyword");
        Check.Eq(list[1].MinChars, 1, "default minChars");
        Check.Eq(list[1].TimeoutMs, 8000, "default timeout");
        Check.Eq(Providers.ParseConfig("{ not json").Count, 0, "a broken file gives none");
    }

    public static void KeywordMatch()
    {
        var list = Providers.ParseConfig(Config);
        var hit = Providers.Match("ff 보고서 초안", list);
        Check.True(hit is { } h && h.provider.Keyword == "ff" && h.rest == "보고서 초안", "keyword + rest");
        Check.True(Providers.Match("FF 보고서", list) is { } up && up.provider.Keyword == "ff", "keyword without case");
        Check.True(Providers.Match("  ec  이름 ", list) is { } m && m.provider.Keyword == "ec" && m.rest == "이름", "leading and extra spaces");
        Check.Eq(Providers.Match("ff", list)?.rest, null, "the keyword alone is not a call");
        Check.True(Providers.Match("ffmpeg", list) is null, "the keyword is a whole word");
        Check.True(Providers.Match("ff ", list) is { } e && e.rest == "", "keyword and a space: empty words");
        Check.True(Providers.Match("gg 고양이", list) is null, "other keywords are not ours");
    }

    public static void ReplyReads()
    {
        var r = Providers.ParseReply("""
            {"items":[
              {"title":"열기","subtitle":"s","action":{"type":"url","value":"https://a.b/c"}},
              {"title":"복사","action":{"type":"copy","value":"text"}},
              {"title":"실행","action":{"type":"run","value":["cmd","/c","dir"]}},
              {"title":"행동 없음"},
              {"title":"나쁜 행동","action":{"type":"run","value":"notepad"}},
              {"subtitle":"no title"}
            ],"error":"일부 실패"}
            """);
        Check.Eq(r.Items.Count, 5, "rows with a title stay");
        Check.Eq(r.Error, "일부 실패", "the optional error");
        Check.Eq(r.Items[0].Action!.Type, "url", "url action");
        Check.Eq(r.Items[0].Action!.Text, "https://a.b/c", "url value");
        Check.Eq(r.Items[2].Action!.Args!.Length, 3, "run takes the array");
        Check.True(r.Items[3].Action is null && r.Items[4].Action is null, "no or malformed action: a row without Enter");
    }

    public static void ReplyLimitAndErrors()
    {
        var many = new System.Text.StringBuilder("{\"items\":[");
        for (int i = 0; i < 30; i++) many.Append(i > 0 ? "," : "").Append("{\"title\":\"t").Append(i).Append("\"}");
        many.Append("]}");
        Check.Eq(Providers.ParseReply(many.ToString()).Items.Count, Providers.MaxItems, "at most 20 rows");
        foreach (string bad in new[] { "", "   ", "not json", "[1,2]", "{\"items\":5}", "{}", "{\"items\":[" })
        {
            var r = Providers.ParseReply(bad);
            Check.True(r.Items.Count == 0 && !string.IsNullOrEmpty(r.Error), $"bad output «{bad}» is one error line");
        }
        var only = Providers.ParseReply("{\"error\":\"DB 없음\"}");
        Check.True(only.Items.Count == 0 && only.Error == "DB 없음", "an error alone is shown as it is");
    }

    public static void UrlSchemes()
    {
        Check.True(Providers.IsSafeUrl("https://example.com/a?b=1"), "https");
        Check.True(Providers.IsSafeUrl("http://127.0.0.1:8080/"), "http");
        foreach (string bad in new[] { "file:///C:/Windows/notepad.exe", "javascript:alert(1)", "ms-settings:", "ftp://x/y", @"C:\Windows\notepad.exe", "example.com", "", "calc" })
            Check.True(!Providers.IsSafeUrl(bad), $"«{bad}» is refused");
        Check.True(!Providers.IsSafeUrl(null), "null is refused");
    }

    /// A provider whose command is a PowerShell script with this body (a file in the temp folder; -File passes the words on as they are).
    private static Provider Powershell(string body, int timeoutMs = 20_000)
    {
        string file = Path.Combine(Path.GetTempPath(), "bloom-provider-test-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(file, body);
        return Providers.ParseConfig(System.Text.Json.JsonSerializer.Serialize(new[] { new { keyword = "t", command = new[] { "powershell", "-NoProfile", "-File", file }, timeoutMs } }))[0];
    }

    /// Deletes the scripts of the providers it holds.
    private sealed class Cleanup(params Provider[] providers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var p in providers) File.Delete(p.Command[3]);
        }
    }

    public static void RunReadsWhatTheCommandPrints()
    {
        // the words are the last argument, one argument whatever they hold (no shell reads them)
        var p = Powershell("param([string]$w)\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n@{ items = @(@{ title = $w; subtitle = [string]$args.Count }) } | ConvertTo-Json -Compress -Depth 5");
        using var _ = new Cleanup(p);
        var r = Providers.Run(p, "한글 & echo \"hi\" | x", CancellationToken.None).GetAwaiter().GetResult();
        Check.True(r.Error is null && r.Items.Count == 1, "one row, no error: " + r.Error);
        Check.Eq(r.Items[0].Title, "한글 & echo \"hi\" | x", "the words arrive whole, as one argument");
    }

    public static void RunTimesOutAndCancels()
    {
        var slow = Powershell("Start-Sleep 30", 400);
        var other = Powershell("Start-Sleep 30");
        using var _ = new Cleanup(slow, other);
        var r = Providers.Run(slow, "x", CancellationToken.None).GetAwaiter().GetResult();
        Check.True(r.Items.Count == 0 && r.Error is not null, "over its time: one error line");
        using var stop = new CancellationTokenSource(300);
        bool cancelled = false;
        try { Providers.Run(other, "x", stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.True(cancelled, "new words cancel the run");
        var missing = Providers.Run(new Provider { Keyword = "m", Command = new[] { @"C:\no\such\program.exe" } }, "x", CancellationToken.None).GetAwaiter().GetResult();
        Check.True(missing.Items.Count == 0 && missing.Error is not null, "a program that does not start: one error line");
    }
}
