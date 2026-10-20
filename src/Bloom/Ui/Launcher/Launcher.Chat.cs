using System.Linq;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The chat view's side: sending, the model and the thinking level.
internal sealed partial class Launcher
{
    private void CatalogUpdated()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            UpdateChrome();
            if (actions.IsOpen && actions.Heading == Tr("모델 · 생각 수준", "Model · thinking level")) PickModel();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void SendChat()
    {
        if (thread is null) return;
        string text = box.Text.Trim();
        string? image = attachedImage;
        if (text.Length == 0) text = ChatAttach.DefaultText(image is not null, attachedFiles.Count > 0);
        if (text.Length == 0 || thread.Busy) return;
        var page = attachment;
        var files = attachedFiles.ToList();
        attachment = null;
        attachedImage = null;
        attachedFiles.Clear();
        chat.ShowPending(attachedFiles, Unattach);
        suppressText = true;
        box.Clear();
        suppressText = false;
        host.Chats.Send(thread, text, page?.text is { } t ? ClipText(t, 15000) : null, page?.title, image, files);
        chat.Show(thread);
        UpdateChrome();
    }

    // ── 빠른 AI in the background ──────────────────────────────────────────────────────────────────
    // Esc leaves a running answer to finish on its own (■ 정지 or Ctrl+. stops it); the home's search line shows it until
    // it is looked at, Tab goes back to it, and a corner card says when it finished while Bloom was out of sight.
    private bool quickBusy, quickUnseen;

    /// The quick chat's conversation runs, or has an answer not looked at yet, while another view is open.
    private bool QuickPending => thread is not null && (thread.Busy || quickUnseen) && Mode != App.Mode.Chat;

    public void StopChat()
    {
        if (thread is { Busy: true } t) host.Chats.Stop(t);
    }

    /// Each update of the quick chat's conversation, in any view: only a start or an end changes anything.
    private void QuickChanged(ChatThread t)
    {
        if (t.Busy == quickBusy) return;
        quickBusy = t.Busy;
        if (!quickBusy && (Mode != App.Mode.Chat || !IsVisible))
        {
            quickUnseen = true;
            if (!IsVisible || !IsActive)
            {
                bool ok = t.Messages.LastOrDefault()?.Error != true;
                Notices.Result(ok, Tr("Bloom · 빠른 AI", "Bloom · Quick AI"), ok ? Tr("답변 완료", "Answer ready") : Tr("응답 없음 · 다시 보내세요", "No response. Send again."),
                    ClipText(t.Title, 40), Tr("AI 채팅 열기", "Open AI chat"), () => host.OpenChat(t));
            }
        }
        if (Mode != App.Mode.Chat && IsVisible) UpdateChrome(); // the home's chip
    }

    void IChatHost.ShowChat(ChatThread t) => ShowChat(t);

    void IChatHost.FocusComposer() => FocusBox();

    /// Ctrl+Shift+O: this conversation in the AI chat window, with what was typed and attached so far.
    public void OpenInWindow()
    {
        var files = attachedFiles.ToList();
        string draft = box.Text;
        attachedFiles.Clear();
        chat.ShowPending(attachedFiles, Unattach);
        suppressText = true;
        box.Clear();
        suppressText = false;
        host.OpenChatWindow(thread ?? host.Chats.New(), draft, files);
    }

    /// A conversation was deleted (here or in the chat window): it isn't shown any longer.
    public void Forget(ChatThread t)
    {
        if (!ReferenceEquals(t, thread)) return;
        if (Mode == App.Mode.Chat) ShowChat(host.Chats.New());
        else thread = null;
    }

    /// Ctrl+L: the conversations as a list in the launcher.
    public void ShowThreads() => Go(App.Mode.Threads);

    private string CurrentModel => Ai.AiModels.Normalize(thread?.Model ?? Ai.AiModels.DefaultModel());

    /// Ctrl+M and the search line's model chip: every model (Claude, ChatGPT, omp, then each API provider's), then this model's
    /// thinking levels, the current ones ticked, in one menu.
    public void PickModel()
    {
        string cur = CurrentModel;
        var ids = Ai.AiModels.All();
        if (!ids.Contains(cur)) ids.Add(cur);
        var list = ids.Select(m => new ActionItem { Title = Ai.AiModels.Who(m), Glyph = m == cur ? G.Check : G.Model, StayOpen = true, Run = () => SetModel(m) }).ToList();
        if (Ai.AiModels.HasEffort(cur))
        {
            string now = thread is null ? Ai.AiModels.DefaultEffort(cur) : Chats.EffortOf(thread);
            foreach (string level in Ai.AiModels.Efforts(cur))
                list.Add(new ActionItem { Title = Tr($"생각 수준 · {Ai.AiModels.EffortName(level)}", $"Thinking · {Ai.AiModels.EffortName(level)}"), Glyph = level == now ? G.Check : G.Bulb, StayOpen = true, Run = () => SetEffort(level) });
        }
        actions.Open(Tr("모델 · 생각 수준", "Model · thinking level"), list, ids.IndexOf(cur));
    }

    /// Ctrl+Shift+M: Claude → ChatGPT → omp (when it is on this PC), each side with the model last used there.
    public void SwitchProvider() => SetModel(Ai.AiModels.NextSide(CurrentModel));

    /// The conversation goes on with this model from the next message; it also becomes the default.
    public void SetModel(string model)
    {
        Chats.SetModel(thread, model);
        if (thread is not null && Mode == App.Mode.Chat) host.Chats.PreStart(thread);
        string level = Ai.AiModels.HasEffort(model) ? " · " + Ai.AiModels.EffortName(thread is null ? Ai.AiModels.DefaultEffort(model) : Chats.EffortOf(thread)) : "";
        ShowToast(Ai.AiModels.Who(model) + level);
        UpdateChrome();
    }

    /// Ctrl+E: the model's levels in turn (Claude 빠름 = thinking off … 최대; ChatGPT 낮음 … 울트라).
    public void CycleEffort()
    {
        string model = CurrentModel;
        if (!Ai.AiModels.HasEffort(model))
        {
            ShowToast(Tr("이 모델은 생각 수준을 지원하지 않습니다.", "This model has no thinking level setting."));
            return;
        }
        string now = thread is null ? Ai.AiModels.DefaultEffort(model) : Chats.EffortOf(thread);
        SetEffort(Ai.AiModels.Next(Ai.AiModels.Efforts(model), now));
    }

    private void SetEffort(string level)
    {
        Chats.SetEffort(thread, level);
        if (thread is not null && Mode == App.Mode.Chat) host.Chats.PreStart(thread);
        ShowToast(Tr($"생각 수준: {Ai.AiModels.EffortName(level)}", $"Thinking level: {Ai.AiModels.EffortName(level)}"));
        UpdateChrome();
    }
}
