namespace Bloom.Ai;

/// How far one log file has been read. A Claude Code file is known by its path; a Codex rollout by its thread's uuid
/// (Thread), so that it is followed when Codex moves it to archived_sessions or compresses it. Size and Mtime (UTC
/// ticks) as last seen; Offset: just past the last whole line read. Codex only: the model its last turn_context named
/// (it holds for the lines after it), whether a token_usage_record has been met (Records: its token_count lines no
/// longer count), and a hash of the last token_count total counted (LastTotal: a repeat counts nothing).
internal sealed class TokenFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long Mtime { get; set; }
    public long Offset { get; set; }
    public string? Thread { get; set; }
    public string? Model { get; set; }
    public bool Records { get; set; }
    public ulong LastTotal { get; set; }

    public bool IsCodex => Thread is not null;

    /// The file shrank (rewritten): read again from the start, as if new. The calls already counted stay counted once,
    /// since their keys are known.
    public void Restart()
    {
        Offset = 0;
        Model = null;
        Records = false;
        LastTotal = 0;
    }
}
