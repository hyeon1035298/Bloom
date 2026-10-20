using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Search;

/// An external search provider (providers.json): "ff 보고서" runs `command` with "보고서" as its last argument, and what it prints
/// (one JSON object) becomes result rows. A small script of the user's own, in any language, lives behind it.
internal sealed class Provider
{
    public string Keyword = "";
    public string Name = "";
    public string[] Command = Array.Empty<string>();
    public string? Cwd;
    public int MinChars = 1;
    public int TimeoutMs = 8000;
    public MatchTarget Match = null!;
}

/// What a row does on Enter: url (http/https only), copy, or run (an argument list, no shell).
internal sealed record ProviderAction(string Type, string? Text, string[]? Args);

internal sealed record ProviderItem(string Title, string? Subtitle, ProviderAction? Action);

/// One run's answer: rows, or a one-line error (also when the output was not what the contract says).
internal sealed record ProviderReply(List<ProviderItem> Items, string? Error);

internal static class Providers
{
    public const int MaxItems = 20;
    private const int MaxOutput = 1 << 20;

    private static List<Provider> current = new();
    private static DateTime stamp = DateTime.MinValue;
    private static bool seen;

    /// The configured providers; providers.json is read again whenever its time changes (a stat per call).
    public static IReadOnlyList<Provider> Current
    {
        get
        {
            string file = Paths.In("providers.json");
            DateTime t = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (!seen || t != stamp)
            {
                seen = true;
                stamp = t;
                current = t == DateTime.MinValue ? new() : ParseConfig(ReadAll(file));
            }
            return current;
        }
    }

    private static string ReadAll(string file)
    {
        try { return File.ReadAllText(file, Encoding.UTF8); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    /// providers.json → providers. A broken file or entry gives nothing for it; the others still work.
    public static List<Provider> ParseConfig(string json)
    {
        var list = new List<Provider>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string kw = Str(e, "keyword")?.Trim() ?? "";
                if (kw.Length == 0 || kw.Any(char.IsWhiteSpace)) continue;
                var cmd = new List<string>();
                if (e.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.Array)
                    foreach (var a in c.EnumerateArray())
                        if (a.ValueKind == JsonValueKind.String) cmd.Add(a.GetString()!);
                if (cmd.Count == 0 || cmd[0].Length == 0) continue;
                string name = Str(e, "name") is { Length: > 0 } n ? n : kw;
                var p = new Provider
                {
                    Keyword = kw,
                    Name = name,
                    Command = cmd.ToArray(),
                    Cwd = Str(e, "cwd") is { Length: > 0 } cwd ? cwd : null,
                    MinChars = Math.Max(1, Int(e, "minChars", 1)),
                    TimeoutMs = Math.Clamp(Int(e, "timeoutMs", 8000), 200, 120_000),
                };
                p.Match = new MatchTarget(name, new[] { kw });
                list.Add(p);
            }
        }
        catch (JsonException)
        {
            // an unreadable file: no providers
        }
        return list;
    }

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string key, int fallback) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : fallback;

    /// "ff 보고서" → (the "ff" provider, "보고서"). The keyword is whole (followed by a space) and compared without case;
    /// "ff" alone is not a call. The rest may be empty ("ff "): the row then asks for words.
    /// A provider wins over everything else that uses the same keyword (quicklinks like "yt", commands).
    public static (Provider provider, string rest)? Match(string text, IReadOnlyList<Provider>? providers = null)
    {
        string t = text.TrimStart();
        foreach (var p in providers ?? Current)
        {
            if (t.Length <= p.Keyword.Length || !t.StartsWith(p.Keyword, StringComparison.OrdinalIgnoreCase) || !char.IsWhiteSpace(t[p.Keyword.Length])) continue;
            return (p, t[p.Keyword.Length..].Trim());
        }
        return null;
    }

    /// Only http and https are opened in the browser: nothing that runs a program or reads a file.
    public static bool IsSafeUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// The adapter's stdout → rows. Anything but the contract's JSON object gives one error line, never an exception.
    public static ProviderReply ParseReply(string? output)
    {
        var items = new List<ProviderItem>();
        if (string.IsNullOrWhiteSpace(output)) return new(items, Tr("출력 없음", "The output is empty"));
        try
        {
            using var doc = JsonDocument.Parse(output!.Trim(), new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(items, Tr("출력이 JSON 객체가 아님", "The output is not a JSON object"));
            string? error = Str(root, "error") is { Length: > 0 } er ? er : null;
            if (root.TryGetProperty("items", out var arr))
            {
                if (arr.ValueKind != JsonValueKind.Array) return new(items, Tr("items가 배열이 아님", "items is not an array"));
                foreach (var e in arr.EnumerateArray())
                {
                    if (items.Count >= MaxItems) break;
                    if (e.ValueKind != JsonValueKind.Object || Str(e, "title") is not { Length: > 0 } title) continue;
                    items.Add(new ProviderItem(title, Str(e, "subtitle"), ReadAction(e)));
                }
            }
            else if (error is null) return new(items, Tr("items 없음", "The output has no items"));
            return new(items, error);
        }
        catch (JsonException)
        {
            return new(items, Tr("출력이 올바른 JSON이 아님", "The output is not valid JSON"));
        }
    }

    private static ProviderAction? ReadAction(JsonElement item)
    {
        if (!item.TryGetProperty("action", out var a) || a.ValueKind != JsonValueKind.Object) return null;
        string type = Str(a, "type")?.Trim().ToLowerInvariant() ?? "";
        if (!a.TryGetProperty("value", out var v)) return null;
        switch (type)
        {
            case "url" or "copy":
                return v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? new ProviderAction(type, s, null) : null;
            case "run":
                if (v.ValueKind != JsonValueKind.Array) return null;
                var args = new List<string>();
                foreach (var x in v.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) return null;
                    args.Add(x.GetString()!);
                }
                return args.Count > 0 && args[0].Length > 0 ? new ProviderAction(type, null, args.ToArray()) : null;
            default:
                return null;
        }
    }

    /// Runs the adapter: no window, no shell, the words as one last argument, in a job of its own (CommandJob) so that
    /// what it started ends with it. Cancelled (new words) → OperationCanceledException after the job is ended; over its
    /// time → an error reply.
    public static async Task<ProviderReply> Run(Provider p, string query, CancellationToken stop)
    {
        var args = p.Command.Skip(1).Append(query).ToList();
        (string file, string arguments) launch;
        try
        {
            launch = McpLaunch.Resolve(p.Command[0], args);
        }
        catch (McpException)
        {
            return new(new(), Tr("검색어의 일부 글자는 이 명령에 넘길 수 없습니다", "The words hold a character this command cannot take"));
        }
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string name && e.Value is string value) env[name] = value;
        env["PYTHONIOENCODING"] = "utf-8";
        env["BLOOM_PROVIDER"] = "1";
        using var job = CommandJob.Create();
        if (job is null) return new(new(), Tr("작업 개체 만들기 실패", "Could not create a job for the command"));
        ChildProcess proc;
        try
        {
            proc = ChildProcess.Start(McpLaunch.CommandLine(launch.file, launch.arguments), p.Cwd is not null && Directory.Exists(p.Cwd) ? p.Cwd : null, env, job);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(new(), Tr($"실행 실패: {e.Message}", $"Could not start the command: {e.Message}"));
        }
        using (proc)
        {
            proc.Input.Dispose();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(p.TimeoutMs);
            var outText = ReadLimited(proc.Output);
            var errText = ReadLimited(proc.Error);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
                job.Kill(); // what it left running ends with it, and with them their hold on the pipes
                string output = await outText;
                string err = await errText;
                var reply = ParseReply(output);
                if (reply.Items.Count == 0 && reply.Error is not null && !string.IsNullOrWhiteSpace(err) && proc.ExitCode != 0)
                    return reply with { Error = reply.Error + " · " + FirstLine(err) };
                return reply;
            }
            catch (OperationCanceledException)
            {
                proc.Kill();
                job.Kill();
                if (stop.IsCancellationRequested) throw;
                return new(new(), Tr("시간 초과", "Timed out"));
            }
        }
    }

    private static string FirstLine(string s)
    {
        foreach (var line in s.Split('\n'))
            if (line.Trim() is { Length: > 0 } t) return t.Length > 120 ? t[..120] + "…" : t;
        return "";
    }

    /// Reads a stream to its end, keeping at most MaxOutput characters (the rest is drained, so the process never blocks).
    private static async Task<string> ReadLimited(Stream stream)
    {
        using var r = new StreamReader(stream, new UTF8Encoding(false));
        var sb = new StringBuilder();
        var buf = new char[8192];
        int n;
        while ((n = await r.ReadAsync(buf, 0, buf.Length)) > 0)
            if (sb.Length < MaxOutput) sb.Append(buf, 0, Math.Min(n, MaxOutput - sb.Length));
        return sb.ToString();
    }

    /// Starts a `run` action: the array as an argument list, no shell, no window; the result is ignored. Started under the
    /// shared launch gate (ChildProcess.Spawn), like every other launch of Bloom's.
    public static void StartDetached(string[] args, string? cwd)
    {
        var psi = new ProcessStartInfo { FileName = args[0], UseShellExecute = false, CreateNoWindow = true };
        for (int i = 1; i < args.Length; i++) psi.ArgumentList.Add(args[i]);
        if (cwd is not null && Directory.Exists(cwd)) psi.WorkingDirectory = cwd;
        ChildProcess.Spawn(psi)?.Dispose();
    }
}
