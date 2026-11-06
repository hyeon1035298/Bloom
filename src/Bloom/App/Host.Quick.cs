using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// Where AI 빠른 동작 stands: the text it works on, the action running or done, its answer.
internal sealed class QuickState
{
    public string Source = "";
    public bool FromSelection; // taken from a selection (the answer can replace it) or from the clipboard
    public IntPtr Target; // the window the text came from: where 바꿔 넣기 / 붙여넣기 goes
    public string? Action; // the QuickActions key, "custom", or "mine" (Mine)
    public string? Custom; // the user's own words for "custom"
    public Bloom.Search.UserCommand? Mine; // the 내 AI 명령 for "mine"
    public string? Progress; // a macro's step running now: "2/3"
    public string? Result, Error;
    public bool Busy;
    public CancellationTokenSource? Cts;

    public string ActionTitle => Action switch
    {
        "custom" => Custom ?? "",
        "mine" => Mine?.Name ?? "",
        _ => QuickActions.Find(Action ?? "") is { } a ? QuickActions.Title(a) : "",
    };

    /// An AI command that needs no text: it runs with nothing selected or copied.
    public bool NoSource => Action == "mine" && Mine?.Input == Bloom.Search.AiInput.None;
}

/// AI 빠른 동작: the hotkey copies the selection of the window in front (or takes what is on the clipboard),
/// the launcher lists the actions, the answer can go back over the selection.
internal sealed partial class Host
{
    public QuickState Quick { get; } = new();

    /// From the hotkey (`selection`: Ctrl+C into the window in front first) or the 명령 (the clipboard as it is).
    public async void StartQuick(bool selection)
    {
        Quick.Cts?.Cancel();
        await Take(selection ? Bloom.Search.AiInput.Selection : Bloom.Search.AiInput.Clipboard);
        Quick.Action = Quick.Custom = Quick.Result = Quick.Error = Quick.Progress = null;
        Quick.Mine = null;
        Quick.Busy = false;
        OpenView(Mode.Quick);
    }

    /// 내 AI 명령 from the search line or its own shortcut: its text (the selection of the window in front — the one
    /// before the launcher when it runs from there — or the clipboard, or none), then the run in the AI 빠른 동작 view.
    public async void StartAiCommand(Bloom.Search.UserCommand c)
    {
        Quick.Cts?.Cancel();
        if (c.Input == Bloom.Search.AiInput.Selection && Window.IsVisible)
        {
            Window.Dismiss();
            await Task.Delay(80); // the window before it is in front again
        }
        await Take(c.Input);
        Quick.Mine = c;
        Quick.Action = "mine";
        Quick.Custom = Quick.Result = Quick.Error = Quick.Progress = null;
        Quick.Busy = false;
        if (Quick.Source.Length == 0 && !Quick.NoSource) OpenView(Mode.Quick); // nothing selected or copied: the view says so
        else RunQuick("mine");
    }

    /// Fills the text the view works on: `input` is an AiInput.
    private async Task Take(string input)
    {
        bool selection = input == Bloom.Search.AiInput.Selection;
        IntPtr target = selection ? Win32.GetForegroundWindow() : Clipboard.LastForeground;
        _ = Win32.GetWindowThreadProcessId(target, out uint pid);
        if (pid == Environment.ProcessId) target = Clipboard.LastForeground; // Bloom itself was in front
        string? text = null;
        bool fromSelection = false;
        if (selection && target != IntPtr.Zero)
        {
            text = await CopySelection(target);
            fromSelection = text is not null;
        }
        if (input != Bloom.Search.AiInput.None) text ??= ClipboardText();
        Quick.Source = text?.Trim() ?? "";
        Quick.FromSelection = fromSelection;
        Quick.Target = target;
    }

    /// Ctrl+C into `target` once the hotkey's own keys are up; the copy, or null when nothing was selected
    /// (the clipboard did not change within half a second).
    private static async Task<string?> CopySelection(IntPtr target)
    {
        for (int i = 0; i < 40 && Win32.ModifiersDown(); i++) await Task.Delay(20);
        uint before = GetClipboardSequenceNumber();
        Win32.SetForegroundWindow(target);
        Win32.SendChord(0x11, 0x43); // Ctrl+C
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(20);
            if (GetClipboardSequenceNumber() != before)
            {
                await Task.Delay(30); // the app may put several formats one after another
                return ClipboardText() is { Length: > 0 } t ? t : null;
            }
        }
        return null;
    }

    private static string? ClipboardText()
    {
        for (int i = 0; i < 5; i++)
            try
            {
                return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30); // another app holds the clipboard for a moment
            }
        return null;
    }

    /// Runs an action (or the user's own words) on the text; the launcher shows it working, then the answer.
    /// "mine" runs Quick.Mine (내 AI 명령) with the chat's model, step by step; the rest use the translation engine's.
    public async void RunQuick(string action, string? custom = null)
    {
        Quick.Action = action;
        if (Quick.Source.Length == 0 && !Quick.NoSource) return;
        Quick.Cts?.Cancel();
        var cts = Quick.Cts = new CancellationTokenSource();
        Quick.Custom = custom;
        Quick.Result = Quick.Error = Quick.Progress = null;
        Quick.Busy = true;
        string? text, error;
        if (action == "mine" && Quick.Mine is { } mine)
        {
            if (!Window.IsVisible || Window.Mode != Mode.Quick) OpenView(Mode.Quick); // from a shortcut: the run shows at once
            else Later();
            (text, error, _) = await AiCommandRun.RunAsync(mine, Quick.Source, Bloom.Search.UserCommands.Current.Items, (i, n) =>
            {
                Quick.Progress = n > 1 ? $"{i}/{n}" : null;
                Later();
            }, cts.Token);
            Usage.Touch("my:" + mine.Id);
        }
        else
        {
            Later();
            string provider = Translator.Provider();
            (text, error) = await OneShot.Ask(QuickActions.System(action, custom, Quick.Source), Quick.Source, provider, Translator.Model(provider), Translator.Effort(provider), "quick", cts.Token);
        }
        if (cts.IsCancellationRequested) return;
        Quick.Progress = null;
        Quick.Busy = false;
        Quick.Result = text;
        Quick.Error = text is null ? error ?? Tr("응답 없음 · 다시 시도하세요", "No response. Try again.") : null;
        Log.Write($"quick: {action} → {(text is null ? "error" : text.Length + " chars")}");
        // the answer comes back in front even if the launcher was closed while it worked
        if (!Window.IsVisible || Window.Mode != Mode.Quick) OpenView(Mode.Quick);
        else Later();
    }

    /// A 내 AI 명령 picked in the AI 빠른 동작 list: it works on the text already there.
    public void RunMineOn(Bloom.Search.UserCommand c)
    {
        Quick.Mine = c;
        RunQuick("mine");
    }

    public void CancelQuick()
    {
        Quick.Cts?.Cancel();
        Quick.Busy = false;
        Quick.Action = null;
        Quick.Progress = null;
        Later();
    }

    /// The answer over the selection it came from (or into that window when it came from the clipboard).
    public void PasteQuick()
    {
        if (Quick.Result is not { } text) return;
        Window.Dismiss();
        _ = Clipboard.PasteText(text, Quick.Target);
    }

    /// The text and the answer go on in an AI chat.
    public void QuickToChat()
    {
        string intro = Quick.Result is { } r && Quick.Source.Length == 0
            ? Tr($"‘{Quick.ActionTitle}’를 실행한 결과는 이랬어요. 이어서 이야기해요.\n\n[결과]\n{r}", $"I ran ‘{Quick.ActionTitle}’; this was the result. Let's continue.\n\n[Result]\n{r}")
            : Quick.Result is { } r2
            ? Tr($"아래 글에 ‘{Quick.ActionTitle}’를 했고, 결과는 이랬어요. 이어서 이야기해요.\n\n[원문]\n{Quick.Source}\n\n[결과]\n{r2}", $"I ran ‘{Quick.ActionTitle}’ on the text below; this was the result. Let's continue.\n\n[Text]\n{Quick.Source}\n\n[Result]\n{r2}")
            : Quick.Source;
        Window.ShowChat(Chats.New(), intro);
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
