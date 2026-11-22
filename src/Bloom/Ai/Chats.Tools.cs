using System;
using System.Linq;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The AI's tool calls in a conversation: look-ups go through, actions 설정's 권한 decides (ToolPermissions: 자동,
/// 항상 허용, 사용 안 함) or already allowed for the whole conversation go through or are refused and are recorded, the
/// rest wait on an approval card (ChatMessage.Asks) for the user's yes or no.
internal sealed partial class Chats
{
    /// Which calls run without asking, how they read and what 항상 허용 remembers (Host: BloomTools).
    public static IToolPolicy? Tools;

    /// UI thread; a tool call waits for the user (Host shows a notice when no Bloom window is in front).
    public event Action<ChatThread, ToolAsk>? AskWaiting;

    /// UI thread; the session asks about a tool call during the reply `m` (null: no reply is being written, so no).
    private void Asked(ChatThread t, ChatMessage? m, IChatSession s, ToolAsk raw)
    {
        if (m is null)
        {
            s.Answer(raw, false);
            return;
        }
        if (Tools?.Quiet(raw.Tool, raw.Input) == true)
        {
            s.Answer(raw, true);
            return;
        }
        // a session that words its own calls (an API chat's file and command tools) keeps its wording and key
        var ask = raw with
        {
            Says = raw.Says.Length > 0 ? raw.Says : Tools?.Says(raw.Tool, raw.Input) ?? raw.Tool,
            Key = raw.Key.Length > 0 ? raw.Key : Tools?.Key(raw.Tool, raw.Input) ?? raw.Tool,
            // the card shows the call whole; the ✓ / ✕ line under the answer keeps the short Says
            Detail = raw.Detail.Length > 0 ? raw.Detail : Tools?.Detail(raw.Tool, raw.Input) ?? "",
        };
        // the card shows a call whole or the call is not asked about: one too big for it is declined
        if (ask.Detail.Length > ToolText.MaxPayload * 2)
        {
            (m.Done ??= new()).Add("✕ " + Tr("승인 카드에 다 보여 줄 수 없을 만큼 커서 거절했습니다", "Declined: too big to show in full on the approval card"));
            s.Answer(ask, false);
            Updated?.Invoke(t);
            return;
        }
        // 설정's 권한 first (사용 안 함 says no, 항상 허용 yes, 자동 yes to Bloom's tool but for what it still asks about),
        // then this chat's 항상 허용; either way the answer shows the line. Under 자동 Claude Code judged its own tools
        // already: what it asks about is asked.
        string rule = ToolPermissions.Of(ask.Key);
        bool auto = rule == ToolPermissions.Auto && ToolPermissions.ActionOf(ask.Key) == ToolPermissions.Pc && Tools?.AutoAsks(ask.Tool, ask.Input) == false;
        if (rule == ToolPermissions.Off || (!ask.MustAsk && (rule == ToolPermissions.Allow || auto || t.AlwaysAllowed.Contains(ask.Key))))
        {
            bool allow = rule != ToolPermissions.Off;
            (m.Done ??= new()).Add((allow ? "✓ " : "✕ ") + ask.Says);
            s.Answer(ask, allow);
            Updated?.Invoke(t);
            return;
        }
        m.Asks.Add(ask);
        m.Status = Tr("승인 대기", "Approval pending");
        Updated?.Invoke(t);
        AskWaiting?.Invoke(t, ask);
    }

    /// UI thread; an action Bloom's MCP server holds for a ChatGPT conversation (Codex doesn't ask on its own): the same
    /// card as Claude's asks; false when that conversation has no Codex run going.
    public Task<bool> AskInConversation(string id, string tool, string input) =>
        sessions.TryGetValue(id, out var s) && s is IAsksInChat codex && s.Busy ? codex.Ask(tool, input) : Task.FromResult(false);

    /// The user's yes or no to a tool call; `always`: this conversation doesn't ask about that action again.
    public void Answer(ChatThread t, ToolAsk ask, bool allow, bool always = false)
    {
        var m = t.Messages.LastOrDefault(x => x.Asks.Contains(ask));
        if (m is null) return;
        m.Asks.Remove(ask);
        if (allow && always) t.AlwaysAllowed.Add(ask.Key);
        (m.Done ??= new()).Add((allow ? "✓ " : "✕ ") + ask.Says);
        m.Status = m.Asks.Count > 0 ? m.Status : allow ? Tr("작업하는 중…", "Working…") : null;
        // a question about saving the answer's file: blocks (AskToSave) goes on with the finish, not to a session
        if (saveAsks.Remove(ask.RequestId, out var save)) save(allow);
        else if (sessions.TryGetValue(t.Id, out var s)) s.Answer(ask, allow);
        Updated?.Invoke(t);
    }
}
