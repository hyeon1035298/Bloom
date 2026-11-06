using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// Running 내 명령 — a script hidden (its result as a notice card) or in a console window, a snippet pasted into the
/// window in front, a chain's lines one after another — and the file actions of a search result (rename, move, copy,
/// recycle) the way Explorer does them.
internal sealed partial class Host
{
    private readonly Dictionary<string, Process> runningScripts = new();
    private readonly Dictionary<string, string> lastOutput = new();

    public bool IsRunning(UserCommand c) => runningScripts.ContainsKey(c.Id);

    public string? LastOutput(UserCommand c) => lastOutput.GetValueOrDefault(c.Id);

    /// {clipboard} in a snippet or a script line.
    public string ClipText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    /// `show` / `admin`: this once in a console window (as administrator); `depth`: how deep in chains this run is.
    public void RunMine(UserCommand c, string input, bool? show = null, bool admin = false, int depth = 0)
    {
        switch (c.Kind)
        {
            case "snippet":
                _ = Clipboard.PasteText(UserCommands.Expand(UserCommands.SnippetText(c.Body), input, ClipText));
                break;
            case "chain":
                _ = RunChain(c, input, depth);
                break;
            case "ai":
                StartAiCommand(c);
                break;
            default:
                RunScript(c, input, show ?? c.ShowWindow, admin);
                break;
        }
    }

    public void StopMine(UserCommand c)
    {
        if (!runningScripts.TryGetValue(c.Id, out var p)) return;
        try
        {
            p.Kill(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            /* it ended on its own */
        }
    }

    private static string Head(bool ok) => ok ? Tr("사용자 명령 · 완료", "Custom command · Done") : Tr("사용자 명령 · 실패", "Custom command · Failed");

    private void RunScript(UserCommand c, string input, bool show, bool admin)
    {
        if (!show && !admin && runningScripts.ContainsKey(c.Id))
        {
            Window.ShowToast(Tr($"‘{c.Name}’ 실행 중", $"‘{c.Name}’ is still running"));
            return;
        }
        // a failure notice's button: the command's actions in the launcher
        void OpenActions()
        {
            OpenView(Mode.Commands, c.Name);
            Window.OpenActions(Engine.FromCommand(c, input));
        }
        var psi = UserCommands.StartInfo(UserCommands.ScriptLine(c.Body, input, ClipText), show || admin);
        if (admin) psi.Verb = "runas";
        Process? p;
        try
        {
            p = Bloom.Ai.ChildProcess.Spawn(psi);
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return; // the administrator question was answered no
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Error("my command " + c.Name, e);
            Notices.Result(false, Head(false), c.Name, Tr("명령을 실행하지 못했습니다. 파일 경로와 권한을 확인하고 다시 실행하세요.", "Could not run the command. Check the file path and permissions, then retry."), Tr("명령 동작 열기", "Open command actions"), OpenActions);
            return;
        }
        if (p is null || show || admin) return;
        runningScripts[c.Id] = p;
        Later();
        var clock = Stopwatch.StartNew();
        _ = Task.Run(async () =>
        {
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            string output = await outTask, error = await errTask;
            int code = p.ExitCode;
            p.Dispose();
            await ui.InvokeAsync(() =>
            {
                runningScripts.Remove(c.Id);
                lastOutput[c.Id] = (output.TrimEnd() + (error.Trim().Length > 0 ? "\n" + error.Trim() : "")).Trim();
                bool ok = code == 0;
                // what the command printed last says how it went: a success shows it (or the time it took), a failure
                // its exit code and that line, with the command's actions (마지막 출력 복사, 명령 편집) one click away
                string? said = UserCommands.LastLine(ok ? output : error.Trim().Length > 0 ? error : output);
                if (ok) Notices.Result(true, Head(true), c.Name, said ?? Tr($"소요 시간 {Notices.Span(clock.Elapsed)}", $"Elapsed: {Notices.Span(clock.Elapsed)}"));
                else Notices.Result(false, Head(false), c.Name, said is null ? Tr($"종료 코드 {code}", $"Exit code {code}") : Tr($"종료 코드 {code} · {said}", $"Exit code {code} · {said}"),
                    Tr("명령 동작 열기", "Open command actions"), OpenActions);
                Later();
            });
        });
    }

    /// A chain: each line as if typed in the search line and Enter pressed on its first answer; "기다리기 2초" waits.
    /// Lines that find nothing are skipped, and a notice says which.
    private async Task RunChain(UserCommand c, string input, int depth)
    {
        if (depth > 2)
        {
            Notices.Result(false, Head(false), c.Name, Tr("묶음은 3단계까지 포함할 수 있습니다. 단계를 줄이세요.", "Chains can be nested up to three levels. Remove a level."));
            return;
        }
        await Task.Delay(150); // the launcher is out of the way first
        var skipped = new List<string>();
        foreach (string line in UserCommands.Steps(c.Body))
        {
            string step = UserCommands.Expand(line, input, ClipText);
            if (UserCommands.Wait(step) is { } wait)
            {
                await Task.Delay(wait);
                continue;
            }
            if (Engine.StepAction(step, depth) is { } run)
            {
                try
                {
                    run();
                }
                catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                {
                    Log.Error("chain step " + step, e);
                    skipped.Add(step);
                }
                await Task.Delay(350); // an app or a page gets a moment to open before the next line
            }
            else skipped.Add(step);
        }
        if (skipped.Count > 0) Notices.Result(false, Head(false), c.Name, Tr($"건너뛴 단계: {string.Join(", ", skipped)}", $"Skipped: {string.Join(", ", skipped)}"));
    }

    // ── file actions (Ctrl+K on a file or folder) ─────────────────────────────────────────────
    /// 다른 폴더로 옮기기 / 복사: the Windows folder picker, then Explorer's own move or copy.
    public void MoveOrCopy(string path, bool move)
    {
        Window.Dismiss();
        var pick = new Microsoft.Win32.OpenFolderDialog
        {
            Title = move ? Tr("옮길 폴더 선택", "Choose destination folder") : Tr("복사할 폴더 선택", "Choose copy destination"),
            InitialDirectory = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "",
        };
        if (pick.ShowDialog() != true) return;
        string name = Path.GetFileName(path.TrimEnd('\\'));
        bool ok = move ? ShellFiles.MoveTo(path, pick.FolderName, IntPtr.Zero) : ShellFiles.CopyTo(path, pick.FolderName, IntPtr.Zero);
        if (ok) Notices.Result(true, move ? Tr("파일 · 옮김", "File · Moved") : Tr("파일 · 복사함", "File · Copied"), name, pick.FolderName);
        else if (File.Exists(path) || Directory.Exists(path)) Notices.Result(false, Tr("파일 · 그대로", "File · Unchanged"), name, move ? Tr("파일 이동 실패 또는 취소 · 대상 폴더와 권한을 확인하고 다시 시도하세요", "Move failed or cancelled. Check the destination and permissions, then retry.") : Tr("파일 복사 실패 또는 취소 · 대상 폴더와 권한을 확인하고 다시 시도하세요", "Copy failed or cancelled. Check the destination and permissions, then retry."));
    }

    /// 휴지통으로 보내기 (asked first in the launcher).
    public bool Recycle(string path) => ShellFiles.Recycle(path, IntPtr.Zero);
}
