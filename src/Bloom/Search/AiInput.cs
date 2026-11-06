namespace Bloom.Search;

/// What an AI command (UserCommand of kind "ai") works on.
internal static class AiInput
{
    public const string Selection = "selection"; // the text selected in the window in front (the clipboard when none is)
    public const string Clipboard = "clipboard";
    public const string None = "none"; // the prompt alone

    public static string Name(string input) => input switch
    {
        Clipboard => Tr("클립보드", "Clipboard"),
        None => Tr("입력 없음", "No input"),
        _ => Tr("선택한 글", "Selected text"),
    };

    /// "선택", "clipboard", "없음", "2" … → the value; null for anything else. Empty = the selection.
    public static string? Parse(string typed) => typed.Trim().ToLowerInvariant() switch
    {
        "" or "1" or "선택" or "선택 글" or "선택한 글" or "selection" or "selected" or "selected text" => Selection,
        "2" or "클립보드" or "clipboard" => Clipboard,
        "3" or "없음" or "입력 없음" or "none" or "no input" => None,
        _ => null,
    };
}
