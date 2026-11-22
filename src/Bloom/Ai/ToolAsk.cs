namespace Bloom.Ai;

/// A tool call the AI wants to make that waits for the user's yes or no (Claude Code's can_use_tool control request,
/// with --permission-prompt-tool stdio). `Input` stays raw JSON: a yes sends it back as it came. `Says` is how the chat
/// names it ("옮기기: a.pdf → D:\문서"); `Key` is what 이 대화에서 항상 허용 remembers — one action ("pc:move"), never
/// the whole tool.
internal sealed record ToolAsk(string RequestId, string Tool, string Input, string Says, string Key = "")
{
    /// The whole of what it will do when Says can't hold it (an API chat's command, a file's full path), already made
    /// visible (ToolText): the card shows it complete, in monospace. Empty: Says is all of it.
    public string Detail { get; init; } = "";

    /// The question is put to the user whatever 설정's 권한 or an earlier 항상 허용 says (사용 안 함 still refuses), and its
    /// card has no 항상 허용: there is nothing to remember it by (a page address that names no web host).
    public bool MustAsk { get; init; }
}
