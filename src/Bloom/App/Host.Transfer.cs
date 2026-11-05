using System;
using System.IO;
using System.Linq;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// Settings out to a file and back in (설정 > 정보, the first-run screens, the 설정 내보내기 / 불러오기 commands), and the
/// two ways to start over or go: 초기화 and 삭제.
internal sealed partial class Host
{
    /// Asks where, writes the file and shows it in Explorer. False when cancelled or it failed (the user was told).
    public bool ExportSettings(Window? owner)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Tr("Bloom 설정 내보내기", "Export Bloom settings"),
            FileName = SettingsTransfer.SuggestedName(),
            DefaultExt = ".zip",
            Filter = Tr("Bloom 설정 (*.zip)|*.zip", "Bloom settings (*.zip)|*.zip"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner) != true) return false;
        try
        {
            SaveAll();
            string ver = typeof(Host).Assembly.GetName().Version?.ToString(3) ?? "";
            SettingsTransfer.Export(dlg.FileName, ver);
            Launch.Open("explorer.exe", $"/select,\"{dlg.FileName}\"");
            Log.Write("settings exported");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("export", e);
            Tell(owner, Tr("설정 내보내기 실패", "Settings export failed"), Tr("파일을 저장하지 못했습니다. 저장 위치와 권한을 확인하고 다시 시도하세요.", "Could not save the file. Check the destination and permissions, then retry."));
            return false;
        }
    }

    /// Asks for a file, puts its settings in place (what they replace goes to backups\) and applies them at once.
    public bool ImportSettings(Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Tr("Bloom 설정 불러오기", "Import Bloom settings"),
            Filter = Tr("Bloom 설정 (*.zip)|*.zip", "Bloom settings (*.zip)|*.zip"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner) != true) return false;
        try
        {
            SaveAll();
            var files = SettingsTransfer.Import(dlg.FileName);
            Reload();
            Log.Write($"settings imported ({files.Count} files)");
            Tell(owner, Tr("설정 불러오기 완료", "Settings imported"), Tr("기존 설정은 데이터 폴더에 백업되었습니다. AI 권한과 MCP 서버 선택은 이 PC의 설정을 그대로 유지했습니다.", "Previous settings backed up in the data folder. AI permissions and the MCP servers switched on were kept as they are on this PC."));
            if (Lang.Pending(Settings.Current.Language)
                && ConfirmWindow.Ask(owner, Tr("Bloom 다시 시작", "Restart Bloom"), Tr("불러온 언어를 적용하려면 Bloom을 다시 시작해야 합니다.", "Restart Bloom to apply the imported language."),
                    (Tr("다시 시작", "Restart"), MessageBoxResult.Yes), (Tr("나중에", "Later"), MessageBoxResult.No)) == MessageBoxResult.Yes)
                Restart();
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error("import", e);
            string reason = e.Data["BloomCopy"] as string
                ?? (e is UnauthorizedAccessException
                    ? Tr("설정 파일에 접근하지 못했습니다. 파일과 데이터 폴더의 권한을 확인하고 다시 시도하세요.", "Could not access settings. Check permissions for the file and data folder, then retry.")
                    : Tr("설정 파일을 읽지 못했습니다. Bloom에서 내보낸 파일을 선택하세요.", "Could not read settings. Choose a file exported from Bloom."));
            Tell(owner, Tr("설정 불러오기 실패", "Settings import failed"), reason);
            return false;
        }
    }

    /// 설정 > 데이터 > Bloom 초기화: asked once (설정 내보내기 후 초기화 · 초기화 · 취소, 취소 the default). Then what Bloom put
    /// outside its folders goes (LeaveOutside: the added accounts sign out, Claude Code's hook, the Win-key helper) and a
    /// new Bloom is started that waits for this one to end, deletes both data folders and starts like a first install
    /// (the first-run screens). Returns false when cancelled or it could not start.
    public void ResetAll(Window? owner) => _ = ResetAllAsync(owner);

    private async System.Threading.Tasks.Task<bool> ResetAllAsync(Window? owner)
    {
        bool helper = WinKeyHelperInstalled;
        string head = Tr("Bloom 초기화", "Reset Bloom");
        string what = Tr("Bloom이 저장한 모든 데이터를 지우고 처음 설정 화면부터 다시 시작합니다. 설정, 즐겨찾기, AI 채팅, 메모, 클립보드 기록, 추가한 AI 계정의 로그인, API 키, 애드온의 설정이 포함됩니다. Windows 시작 시 실행은 다시 켜집니다. 되돌릴 수 없습니다.",
                "Deletes everything Bloom saved and starts again from the first-run screens. This includes settings, favorites, AI chats, notes, clipboard history, the sign-ins of added AI accounts, API keys and add-on settings. Start with Windows turns on again. This can't be undone.")
            + "\n\n" + Tr("Bloom 프로그램, Claude Code와 Codex의 기본 로그인과 기록, 이 PC의 다른 파일은 남습니다.", "The Bloom program, the default sign-ins and history of Claude Code and Codex, and the other files on this PC stay.")
            + (helper ? "\n\n" + Tr("관리자 Win 키 도우미도 지웁니다. 관리자 승인을 묻습니다.", "The admin Win-key helper goes too. Windows asks for admin approval.") : "");
        var pick = ConfirmWindow.Ask(owner, head, what, MessageBoxResult.Cancel,
            (Tr("설정 내보내기 후 초기화", "Export settings, then reset"), MessageBoxResult.Yes, ButtonTone.Plain),
            (Tr("초기화", "Reset"), MessageBoxResult.OK, ButtonTone.Danger),
            (Tr("취소", "Cancel"), MessageBoxResult.Cancel, ButtonTone.Plain));
        if (pick == MessageBoxResult.Cancel) return false;
        if (pick == MessageBoxResult.Yes && !ExportSettings(owner)) return false;
        if (!DocWindow.SettleAll()) return false; // a document's edits not in its file yet: asked before anything starts
        bool signedOut = AiAccounts.All.Any(a => !a.IsDefault);
        await LeaveOutside(owner, head, helper, Tr("관리자 승인을 받지 못해 도우미가 설치된 채로 남았습니다. 꺼진 동안에는 동작하지 않습니다. Windows 설정 › 앱에서 ‘Bloom Win-key helper’를 삭제할 수 있습니다.", "Without admin approval the helper stays installed. It does nothing while off. Remove ‘Bloom Win-key helper’ in Windows Settings › Apps."));
        if (!StartSuccessor("--reset-from"))
        {
            Tell(owner, Tr("초기화 실패", "Reset failed"), signedOut
                ? Tr("Bloom을 다시 시작하지 못했습니다. 추가한 AI 계정의 로그아웃 말고는 삭제된 데이터가 없습니다. Bloom을 다시 열고 시도하세요.", "Could not restart Bloom. Apart from signing out of the added AI accounts, no data was deleted. Reopen Bloom and try again.")
                : Tr("Bloom을 다시 시작하지 못했습니다. 삭제된 데이터는 없습니다. Bloom을 다시 열고 시도하세요.", "Could not restart Bloom. No data was deleted. Reopen Bloom and try again."));
            return false;
        }
        Log.Write("reset: quitting for the new start"); // which stops the add-on hosts before it deletes anything
        Quit(save: false);
        return true;
    }

    /// 설정 > 데이터 > Bloom 삭제 (never in the search): asked twice (sure? the chats, settings and favorites too?), then a
    /// new Bloom waits for this one to end, deletes the data folders when asked and runs the setup's uninstaller (the
    /// files, the Apps entry, the shortcuts). Deleting the data first clears what Bloom put outside its folders, as the
    /// reset does (LeaveOutside). A copy not installed with the setup (the portable zip) can't remove its own folder: it
    /// stops starting with Windows, takes its hook out of Claude Code (no uninstaller does it) and its folder opens in
    /// Explorer to be deleted.
    public void UninstallBloom(Window? owner) => _ = UninstallBloomAsync(owner);

    private async System.Threading.Tasks.Task<bool> UninstallBloomAsync(Window? owner)
    {
        bool installed = Updates.Uninstaller() is not null;
        string what = installed
            ? Tr("이 PC에서 Bloom과 시작 메뉴 바로 가기를 삭제하고 Windows 시작 시 자동 실행을 해제합니다.", "Removes Bloom and its Start menu shortcut from this PC and stops it starting with Windows.")
            : Tr("설치 프로그램으로 설치하지 않은 Bloom입니다. Bloom이 종료되고 Windows 시작 시 자동 실행이 해제됩니다. 종료 후 열리는 폴더를 직접 삭제하세요.", "This copy was not installed with the setup. Bloom quits and stops starting with Windows. Delete the folder that opens afterwards.");
        if (ConfirmWindow.Ask(owner, Tr("Bloom 삭제", "Uninstall Bloom"), what, (Tr("Bloom 삭제", "Uninstall Bloom"), MessageBoxResult.OK), (Tr("취소", "Cancel"), MessageBoxResult.Cancel)) != MessageBoxResult.OK) return false;
        // keeping is the accent choice: deleting the data can't be undone
        var data = ConfirmWindow.Ask(owner, Tr("저장된 데이터", "Saved data"),
            Tr("데이터를 삭제하면 AI 채팅, 설정, 즐겨찾기, 메모, 클립보드 기록이 영구 삭제되고, 추가한 AI 계정에서 먼저 로그아웃합니다. 유지하면 다시 설치한 뒤 그대로 사용할 수 있습니다.", "Deleting data permanently removes AI chats, settings, favorites, notes and clipboard history, and signs out of the added AI accounts first. Kept data is used again after reinstalling."),
            (Tr("데이터 유지", "Keep data"), MessageBoxResult.No), (Tr("데이터 삭제", "Delete data"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.Cancel));
        if (data == MessageBoxResult.Cancel) return false;
        bool wipe = data == MessageBoxResult.Yes;
        if (!DocWindow.SettleAll()) return false; // a document's edits not in its file yet: asked before anything starts
        string helperLeft = Tr("관리자 승인을 받지 못해 Win 키 도우미는 삭제되지 않았습니다. Bloom 삭제 후 Windows 설정 › 앱에서 ‘Bloom Win-key helper’를 삭제하세요.", "Without admin approval the Win-key helper stays installed. After Bloom is removed, remove ‘Bloom Win-key helper’ in Windows Settings › Apps.");
        bool signedOut = wipe && AiAccounts.All.Any(a => !a.IsDefault);
        if (wipe) await LeaveOutside(owner, Tr("Bloom 삭제", "Uninstall Bloom"), WinKeyHelperInstalled, helperLeft);
        else
        {
            if (!installed) ClaudeHooks.Remove(); // the setup's uninstaller takes it out of an installed copy
            if (WinKeyHelperInstalled) await RemoveWinKeyHelper(owner, helperLeft);
        }
        if (!StartSuccessor("--uninstall-from", wipe ? new[] { "--wipe" } : Array.Empty<string>()))
        {
            Tell(owner, Tr("삭제 실패", "Uninstall failed"), signedOut
                ? Tr("Bloom을 다시 시작하지 못했습니다. 추가한 AI 계정의 로그아웃 말고는 삭제된 데이터가 없습니다. Bloom을 다시 열고 시도하세요.", "Could not restart Bloom. Apart from signing out of the added AI accounts, no data was deleted. Reopen Bloom and try again.")
                : Tr("Bloom을 다시 시작하지 못했습니다. 삭제된 데이터는 없습니다. Bloom을 다시 열고 시도하세요.", "Could not restart Bloom. No data was deleted. Reopen Bloom and try again."));
            return false;
        }
        Autostart.Set(false);
        LogonTask.Remove(); // the new start (--uninstall-from) stops the add-on hosts before anything is deleted
        FileAssoc.Set(false); // a copy without the setup has no uninstaller to do it (Program: the setup's own step)
        if (!installed) Launch.Open("explorer.exe", $"/select,\"{Environment.ProcessPath}\"");
        Log.Write("uninstall: quitting" + (wipe ? ", the data goes too" : ""));
        Quit(save: !wipe);
        return true;
    }

    /// A new Bloom that waits for this one to end (`--reset-from` / `--restart-from` / `--uninstall-from`), with the same
    /// dev options.
    private bool StartSuccessor(string flag, params string[] extra)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            psi.ArgumentList.Add(flag);
            psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Paths.IsDev)
            {
                psi.ArgumentList.Add("--data");
                psi.ArgumentList.Add(Paths.Data);
            }
            if (Opt.NoHook) psi.ArgumentList.Add("--no-hook");
            if (Opt.NoTray) psi.ArgumentList.Add("--no-tray");
            foreach (string a in extra) psi.ArgumentList.Add(a);
            Bloom.Ai.ChildProcess.Spawn(psi);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(flag, e);
            return false;
        }
    }

    /// A new language (and anything else read once per run) takes effect in a fresh Bloom; `then` = --settings / --show.
    public void Restart(string? then = null)
    {
        if (!DocWindow.SettleAll()) return; // 취소 on a document's unwritten edits: no restart
        SaveAll();
        if (!StartSuccessor("--restart-from", then is null ? Array.Empty<string>() : new[] { then })) return;
        Log.Write("restart");
        Quit();
    }

    public void ReloadForQa() => Reload();

    /// Everything that was read from the data folder, read again and applied (look, keys, names, lists).
    private void Reload()
    {
        Settings.Load();
        Favorites.Load();
        AppNames.Load();
        Quicklinks.Load();
        UserCommands.Load();
        ColorBook.Load();
        Settings.Current.Onboarded = true;
        Settings.Save();
        if (!Paths.IsDev) Autostart.Set(Settings.Current.Autostart);
        LogonTask.SyncLater();
        FileAssoc.Repair(); // the 마크다운 문서 switch brought in
        Theme.Apply(Settings.Current.Theme);
        ApplyHotkeys();
        AppsChanged();
        Window.DataChanged();
        settingsWindow?.Refresh();
    }

    private static void Tell(Window? owner, string title, string text) => ConfirmWindow.Tell(owner, title, text);
}
