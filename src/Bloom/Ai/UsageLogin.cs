using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Signs an added account in (설정 > AI 계정): the CLI's own login, run into the account's folder in a terminal window of
/// its own — `claude auth login --claudeai` with CLAUDE_CONFIG_DIR, `codex login` with CODEX_HOME. The browser sign-in
/// happens on Anthropic's / OpenAI's pages, and a code the browser may show goes into that terminal, never into Bloom:
/// Bloom only waits for the CLI to finish and checks that its sign-in file is there (AiAccounts.SignedIn). LoginMarker
/// keeps track of the terminal until it closes, even past the wait or a Bloom restart.
internal static class UsageLogin
{
    /// The longest a sign-in may take before Bloom stops waiting (the terminal stays the user's to close).
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);

    /// The account whose sign-in runs (one at a time).
    public static string? Running { get; private set; }

    /// A sign-in runs for this account, or its terminal is still open.
    public static bool Open(AiAccount account) => Running == account.Id || LoginMarker.Alive(account.Home);

    /// A sign-in started or ended.
    public static event Action? Changed;

    /// Why a sign-in for `kind` can't start now (another one runs, the CLI is missing); null = it can. Asked before an
    /// account is added, so a failed start leaves no empty account behind.
    public static string? Blocked(string kind)
    {
        if (Running is not null) return Tr("다른 계정 로그인 진행 중", "Another account sign-in is in progress");
        return CliFor(kind) is null ? AiErrors.Missing(kind == "chatgpt") : null;
    }

    private static string? CliFor(string kind) => kind == "chatgpt" ? CodexSession.FindCli(Settings.Current.CodexPath) : ClaudeSession.FindCli(Settings.Current.ClaudePath);

    /// → null when signed in, else what went wrong (shown under the account).
    public static async Task<string?> Run(AiAccount account)
    {
        if (account.Home is null) return Tr("기본 계정은 Claude Code · Codex에서 직접 로그인하세요", "Sign in to the default account directly in Claude Code · Codex");
        if (LoginMarker.Alive(account.Home)) return Tr("이 계정의 로그인 터미널이 아직 열려 있습니다. 먼저 닫아 주세요.", "The sign-in terminal for this account is still open. Close it first.");
        if (Blocked(account.Kind) is { } why) return why;
        bool claude = account.Kind == "claude";
        string cli = CliFor(account.Kind)!;
        // a sign-in counts when the CLI writes its file anew: "다시 로그인" starts with the old, spent one still there
        string file = AiAccounts.SignInFile(account)!;
        var before = File.GetLastWriteTimeUtc(file); // 1601 when there is none yet
        bool Fresh() => File.GetLastWriteTimeUtc(file) > before;
        // a window of its own (no redirection): the CLI shows its link there and reads a pasted code itself
        var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = OneShot.WorkDir() };
        foreach (string a in claude ? new[] { "auth", "login", "--claudeai" } : new[] { "login" }) psi.ArgumentList.Add(a);
        Cli.CleanEnvironment(psi);
        AiAccounts.UseHome(psi, account.Kind, account.Home);
        Running = account.Id;
        Changed?.Invoke();
        Process? p = null;
        try
        {
            // `codex login` writes the account's auth.json: Bloom's app-servers on the old sign-in end first, and none starts
            // until it is done (CodexAppHome.Hold; LoginMarker while the terminal stays open)
            using var hold = claude ? null : await CodexAppHome.Hold(account.Home);
            if (!claude && hold is null) return AiErrors.StartFailed;
            p = ChildProcess.Spawn(psi);
            if (p is null) return AiErrors.StartFailed;
            LoginMarker.Mark(account.Home, p);
            var started = DateTime.Now;
            // Claude Code may stay open after its login; the sign-in file is the answer either way
            while (!p.HasExited && !Fresh() && DateTime.Now - started < Longest)
                await Task.Delay(1500);
            if (!Fresh()) await Task.Delay(1000); // written just as the CLI ended
            if (!Fresh()) return Tr("로그인 안 됨 · 다시 시도하세요", "Not signed in · Please try again");
            AiUsage.SignedIn(account.Id);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Error("ai login", e);
            return AiErrors.StartFailed;
        }
        finally
        {
            Running = null;
            if (p is { HasExited: false }) _ = Linger(account.Home, p);
            else
            {
                LoginMarker.Unmark(account.Home);
                p?.Dispose();
            }
            Changed?.Invoke();
        }
    }

    /// The longest a sign-out's CLI may take before it is ended (the folder leaves either way).
    private static readonly TimeSpan SignOutLongest = TimeSpan.FromSeconds(30);

    /// An added account leaves (설정 > AI 계정 › X or 로그아웃): off the list at once, then its CLI signs it out in its own
    /// folder — `claude auth logout` with CLAUDE_CONFIG_DIR, `codex logout` with CODEX_HOME, so the default sign-in is
    /// never touched — and the folder goes to the Recycle Bin without the sign-in file (deleted here when a CLI that is
    /// missing, failed or ran out of time left it) or tokens Bloom had not put in place yet (CodexAuth.Forget).
    public static async Task SignOut(AiAccount account)
    {
        if (AiAccounts.Remove(account) is not { } home) return;
        bool claude = account.Kind == "claude";
        string file = AiAccounts.SignInFile(account)!;
        IDisposable? hold = null;
        try
        {
            // as for a sign-in: Bloom's app-servers on this sign-in end first, and none starts on it until the folder is gone
            if (!claude)
            {
                hold = await CodexAppHome.Hold(home);
                if (hold is null) Log.Write("ai sign-out: Bloom's app-servers on the account did not end — " + home);
            }
            if (File.Exists(file) && CliFor(account.Kind) is { } cli) await Logout(cli, account.Kind, home);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Error("ai sign-out", e);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("ai sign-out", e);
            }
            if (!claude) CodexAuth.Forget(home);
            AiAccounts.Recycle(home);
            hold?.Dispose();
        }
    }

    /// The CLI's own sign-out in the account's folder, with no window; ended after SignOutLongest.
    private static async Task Logout(string cli, string kind, string home)
    {
        var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = OneShot.WorkDir() };
        foreach (string a in kind == "claude" ? new[] { "auth", "logout" } : new[] { "logout" }) psi.ArgumentList.Add(a);
        Cli.CleanEnvironment(psi);
        AiAccounts.UseHome(psi, kind, home);
        using var p = ChildProcess.Spawn(psi);
        if (p is null) return;
        using var limit = new CancellationTokenSource(SignOutLongest);
        try
        {
            await p.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(true);
            }
            catch (Exception e)
            {
                Log.Error("ai sign-out", e); // gone meanwhile, or part of its tree would not end
            }
        }
    }

    /// The terminal outlived the wait: the account stays locked (LoginMarker) until it closes.
    private static async Task Linger(string home, Process p)
    {
        try
        {
            await p.WaitForExitAsync();
        }
        finally
        {
            LoginMarker.Unmark(home);
            p.Dispose();
            Changed?.Invoke();
        }
    }
}
