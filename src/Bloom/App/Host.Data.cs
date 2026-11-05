using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// 설정 > 데이터's deletions that reach past one store: the chats, the CLIs' conversations in added accounts, the caches,
/// the sign-outs, the settings back to their defaults, and what the reset and the uninstall clear outside Bloom's folders.
internal sealed partial class Host
{
    /// The added accounts as (kind, folder): only those with a folder of Bloom's; a default account has none.
    public static List<(string kind, string home)> AddedHomes() =>
        AiAccounts.All.Where(a => !a.IsDefault && a.Home is not null).Select(a => (a.Kind, a.Home!)).ToList();

    /// AI 채팅 기록: the chat window closes, every session ends, every conversation goes as one deleted by hand (its
    /// attachments and omp session with it), then the groups and the pasted pictures. → entries that stayed.
    public int ClearChats()
    {
        chatWindow?.Close();
        Chats.EndAll();
        DeleteThreads(Chats.Threads.ToList());
        foreach (var g in Chats.Groups.ToList()) Chats.DeleteGroup(g);
        return DataWipe.Delete(Path.Combine(Paths.Local, "ai", "paste"));
    }

    /// 추가 계정의 CLI 기록: the chats' sessions end, and Bloom's Codex app-servers on those accounts end and start no more
    /// until it is done (CodexAppHome.Hold); then the conversation folders inside the accounts go. Their sign-ins stay.
    /// → entries that stayed.
    public async Task<int> ClearCliHistory()
    {
        var added = AddedHomes();
        Chats.EndAll();
        var holds = new List<IDisposable>();
        try
        {
            foreach (var (_, home) in added.Where(a => a.kind == "chatgpt"))
                if (await CodexAppHome.Hold(home) is { } held) holds.Add(held);
            return await Task.Run(() => DataWipe.Delete(DataParts.CliHistory(added)));
        }
        finally
        {
            foreach (var h in holds) h.Dispose();
        }
    }

    /// 캐시: the sessions and the default account's app-server end as above, then what can be made again goes
    /// (DataParts.Cache; the sessions junction in it is unlinked, auth.json is a hard link and only that name goes).
    /// → entries that stayed.
    public async Task<int> ClearCache()
    {
        Chats.EndAll();
        using var hold = await CodexAppHome.Hold(null);
        return await Task.Run(() => DataWipe.Delete(DataParts.Cache()));
    }

    /// 추가한 AI 계정, and the reset or the uninstall that deletes the data: AI chat goes back to the default accounts and
    /// every added account signs out in its own CLI (UsageLogin.SignOut ends the sign-in and sends the folder to the
    /// Recycle Bin), each given `each` before the next starts. → how many there were.
    public async Task<int> SignOutAdded(TimeSpan each)
    {
        var added = AiAccounts.All.Where(a => !a.IsDefault).ToList();
        if (added.Count == 0) return 0;
        Chats.EndAll();
        foreach (string kind in new[] { "claude", "chatgpt" })
            if (!AiAccounts.Chat(kind).IsDefault) AiAccounts.UseForChat(kind, AiAccounts.DefaultId(kind));
        Settings.Save();
        foreach (var a in added)
        {
            var signOut = UsageLogin.SignOut(a);
            if (await Task.WhenAny(signOut, Task.Delay(each)) != signOut) Log.Write("ai sign-out: still running after the wait, going on");
        }
        return added.Count;
    }

    /// 설정 기본값으로: pending writes saved first (SaveAll), settings.json written with only what Settings.Kept keeps,
    /// then a new Bloom that waits for this one to end; this one quits without saving over it.
    public void RestoreDefaults(Window? owner)
    {
        if (!DocWindow.SettleAll()) return;
        SaveAll();
        Settings.WriteDefaults();
        if (!StartSuccessor("--restart-from"))
        {
            Settings.KeepCurrent();
            Tell(owner, Tr("설정 기본값으로", "Default settings"), Tr("Bloom을 다시 시작하지 못해 설정을 그대로 두었습니다. Bloom을 다시 열고 시도하세요.", "Could not restart Bloom, so the settings stay as they were. Reopen Bloom and try again."));
            return;
        }
        Log.Write("settings: back to the defaults, restarting");
        Quit(save: false);
    }

    /// The reset, or the uninstall that deletes the data: what Bloom put outside its folders goes before them. The added
    /// accounts sign out (their sign-ins end on the server, not only here), Bloom's hook leaves Claude Code's
    /// settings.json, and the elevated Win-key helper is removed with the admin's approval (`helper`; told when refused).
    private async Task LeaveOutside(Window? owner, string head, bool helper, string helperLeft)
    {
        int added = AiAccounts.All.Count(a => !a.IsDefault);
        if (added > 0)
        {
            Notices.Result(true, head, Tr($"추가한 AI 계정 {added}개에서 로그아웃하는 중", $"Signing out of {added} added AI accounts"), null);
            await SignOutAdded(TimeSpan.FromSeconds(45));
        }
        ClaudeHooks.Remove();
        if (helper) await RemoveWinKeyHelper(owner, helperLeft);
    }

    /// The elevated Win-key helper is outside Bloom's folder: stopped, then removed with the admin's approval.
    private async Task RemoveWinKeyHelper(Window? owner, string left)
    {
        winKeyAdmin?.Dispose();
        winKeyAdmin = null;
        if (await ElevatedWinKey.RunElevated("--remove") != ElevatedWinKey.Elevated.Done) Tell(owner, WinKeyLeftTitle, left);
    }

    private static bool WinKeyHelperInstalled => ElevatedWinKey.Offered && ElevatedWinKey.Check() != ElevatedWinKey.Copy.None;
}
