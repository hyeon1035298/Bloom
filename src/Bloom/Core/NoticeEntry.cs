namespace Bloom.Core;

/// One line of 알림 센터: how something ended, a look at its result, and the AI chat it opens (if any).
internal sealed class NoticeEntry
{
    public string Id { get; set; } = "";
    public long At { get; set; } // unix ms
    public bool Ok { get; set; }
    public string Head { get; set; } = ""; // "AI 명령 · 완료"
    public string Title { get; set; } = ""; // what it is about: the command's name
    public string? Detail { get; set; } // the first words of the result, or the error
    public string? Thread { get; set; } // the AI chat a click opens

    [System.Text.Json.Serialization.JsonIgnore] public string? Key; // a waiting approval's card key (NoticeLog.Waiting)
    [System.Text.Json.Serialization.JsonIgnore] public System.Action? Open; // Enter while this Bloom runs: the session window
}
