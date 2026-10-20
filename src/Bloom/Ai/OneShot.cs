using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// One question, one answer, no conversation kept: a fresh CLI call with its own system prompt and no tools
/// (translation, AI 빠른 동작, icon drawing). Claude Code (`claude -p`), Codex (`codex exec`) or omp (`omp -p`), each with the
/// user's own login, or an API model with the user's key (ApiClient). Every call counts against the user's plan.
internal static class OneShot
{
    /// → (answer, null) or (null, error); (null, null) when cancelled. `name` names the Codex instructions file.
    public static async Task<(string? text, string? error)> Ask(string system, string text, string provider, string model, string? effort, string name, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0) return ("", null);
        if (Demo.On) return await DemoAnswers.Ask(name, text, ct); // recording mode: nothing is asked
        if (text.Length > 12000) text = text[..12000];
        if (provider == "api") return await ApiClient.AskAsync(model, effort, system, text, ct);
        if (provider == "omp") return await Omp(system, text, model, effort, ct);
        string? accountHome = AiAccounts.ChatHome(provider);
        return provider == "chatgpt" ? await Codex(system, text, model, effort, name, accountHome, ct) : await Claude(system, text, model, effort, accountHome, ct);
    }

    private static async Task<(string? text, string? error)> Claude(string system, string text, string model, string? effort, string? accountHome, CancellationToken ct)
    {
        string? cli = ClaudeSession.FindCli(Settings.Current.ClaudePath);
        if (cli is null) return (null, AiErrors.ClaudeMissing);
        var psi = Psi(cli);
        AiAccounts.UseHome(psi, "claude", accountHome);
        bool fast = effort is null or "fast";
        foreach (string a in new[]
        {
            "-p", "--model", model, "--output-format", "text", "--system-prompt", system,
            "--tools", "", "--strict-mcp-config", "--disable-slash-commands", "--name", "Bloom",
            "--settings", fast ? "{\"disableAllHooks\":true,\"alwaysThinkingEnabled\":false}" : "{\"disableAllHooks\":true}",
        }) psi.ArgumentList.Add(a);
        if (!fast)
        {
            psi.ArgumentList.Add("--effort");
            psi.ArgumentList.Add(effort!);
        }
        var (code, output, error) = await RunCli(psi, text, ct);
        if (ct.IsCancellationRequested) return (null, null);
        if (code != 0 || output.Trim().Length == 0) return (null, Tail(error, output, Tr("Claude 답변이 없습니다. 다시 시도하세요.", "No answer from Claude. Try again.")));
        return (output.Trim(), null);
    }

    /// `omp -p`: no tools, nothing saved, an empty folder, Bloom's system prompt instead of omp's (OmpCli.OneShotArgs); the
    /// question goes in on stdin. `model` is Bloom's id ("omp:provider/model"); `effort` null = thinking off.
    private static async Task<(string? text, string? error)> Omp(string system, string text, string model, string? effort, CancellationToken ct)
    {
        string? exe = OmpCli.FindCli();
        if (exe is null) return (null, AiErrors.OmpMissing);
        var psi = OmpCli.Psi(exe);
        foreach (string a in OmpCli.OneShotArgs(AiModels.OmpSlug(model) ?? model, effort, system)) psi.ArgumentList.Add(a);
        var (code, output, error) = await RunCli(psi, text, ct);
        if (ct.IsCancellationRequested) return (null, null);
        if (code != 0 || output.Trim().Length == 0) return (null, OmpTail(error, output));
        return (output.Trim(), null);
    }

    /// omp's stderr has a "Working..." progress line before the real message.
    public static string OmpProgressless(string error) => string.Join('\n', error.Split('\n').Where(l => l.Trim().Length > 0 && !l.Trim().StartsWith("Working", StringComparison.Ordinal)));

    private static string OmpTail(string error, string output)
    {
        string e = OmpProgressless(error);
        return e.Length > 0 ? AiErrors.Omp(e) : output.Trim().Length > 0 ? AiErrors.Omp(output.Trim()) : AiErrors.OmpNoAnswer;
    }

    private static async Task<(string? text, string? error)> Codex(string system, string text, string model, string? effort, string name, string? accountHome, CancellationToken ct)
    {
        string? exe = CodexSession.FindCli(Settings.Current.CodexPath);
        if (exe is null) return (null, AiErrors.CodexMissing);
        string cwd = WorkDir();
        string instructions = Path.Combine(cwd, name + ".md");
        await File.WriteAllTextAsync(instructions, system, ct);
        var psi = Psi(exe);
        AiAccounts.UseHome(psi, "chatgpt", accountHome);
        _ = CodexAuth.Unsaved(accountHome); // tokens the light way refreshed go in first
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--ignore-user-config", "--sandbox", "read-only", "--cd", cwd, "--color", "never" };
        if (AiModels.GptSlug(model) is { } slug)
        {
            args.Add("--model");
            args.Add(slug);
        }
        foreach (string c in new[]
        {
            $"model_reasoning_effort=\"{effort ?? AiModels.Fit(model, "minimal")}\"",
            $"model_instructions_file='{instructions}'",
            "include_environment_context=false",
            "include_permissions_instructions=false",
        })
        {
            args.Add("-c");
            args.Add(c);
        }
        args.Add("-");
        foreach (string a in args) psi.ArgumentList.Add(a);
        var (code, output, error) = await RunCli(psi, text, ct);
        if (ct.IsCancellationRequested) return (null, null);
        string? answer = null, failure = null;
        foreach (string line in output.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                string type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type == "item.completed" && r.TryGetProperty("item", out var it) && it.TryGetProperty("type", out var itype) && itype.GetString() == "agent_message"
                    && it.TryGetProperty("text", out var tx))
                    answer = tx.GetString();
                else if (type is "turn.failed" or "error")
                    failure = r.ToString();
            }
            catch (JsonException)
            {
                /* a log line */
            }
        }
        if (answer is { Length: > 0 }) return (answer.Trim(), null);
        if (failure is not null) return (null, AiErrors.Friendly(failure, true));
        return (null, Tail(error, "", code == 0 ? Tr("ChatGPT 답변이 없습니다. 다시 시도하세요.", "No answer from ChatGPT. Try again.") : Tr("Codex 답변 실패 · 다시 시도하세요", "Codex answer failed. Try again."), true));
    }

    /// The CLIs' working folder (Local\ai): no project files around, nothing to read by accident.
    public static string WorkDir()
    {
        string keep = Paths.InLocal(@"ai\.keep");
        if (!File.Exists(keep))
        {
            try
            {
                using (new FileStream(keep, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { } // the usage looks start two CLIs at once
            }
            catch (IOException)
            {
                /* the other one made it */
            }
        }
        return Path.GetDirectoryName(keep)!;
    }

    public static ProcessStartInfo Psi(string exe)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            StandardInputEncoding = utf8,
            WorkingDirectory = WorkDir(),
        };
        Cli.CleanEnvironment(psi);
        return psi;
    }

    /// Runs the CLI with `input` on stdin; 90 s at most.
    public static async Task<(int code, string output, string error)> RunCli(ProcessStartInfo psi, string input, CancellationToken ct)
    {
        Process? p = null;
        try
        {
            p = ChildProcess.Spawn(psi);
            if (p is null) return (-1, "", AiErrors.StartFailed);
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);
            await p.StandardInput.WriteAsync(input);
            p.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await p.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(true);
                }
                catch
                {
                    /* gone */
                }
                return (-1, "", ct.IsCancellationRequested ? "" : AiErrors.TimedOut);
            }
            return (p.ExitCode, await outTask, await errTask);
        }
        catch (Exception e)
        {
            Log.Error("one-shot", e);
            return (-1, "", e.Message);
        }
        finally
        {
            p?.Dispose();
        }
    }

    public static string Short(string s) => s.Length > 160 ? s[..160] + "…" : s;

    private static string Tail(string error, string output, string fallback, bool chatgpt = false)
    {
        string e = (error.Trim().Length > 0 ? error : output).Trim();
        if (e.Length == 0) return fallback;
        return AiErrors.Friendly(e, chatgpt);
    }
}
