namespace Bloom.Ai;

/// How far one log file has been read. A Claude Code file is known by its path; a Codex rollout by its thread's uuid
/// (Thread), so that it is followed when Codex moves it to archived_sessions or compresses it. Size and Mtime (UTC
/// ticks) as last seen; Offset: just past the last whole line read. Codex only: the model and the effort its last
/// turn_context named (they hold for the lines after it), whether a token_usage_record has been met (Records: its
/// token_count lines no longer count), and a hash of the last token_count total counted (LastTotal: a repeat counts
/// nothing). Met, Parent, Folder and Bloom: what its session_meta told during this pass (handed to TokenIndex.Describe,
/// never kept here).
internal sealed class TokenFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long Mtime { get; set; }
    public long Offset { get; set; }
    public string? Thread { get; set; }
    public string? Model { get; set; }
    public TokenEffort Effort { get; set; }
    public bool Records { get; set; }
    public ulong LastTotal { get; set; }

    /// This pass read the rollout's session_meta: its parent thread (parent_thread_id), the last name of its folder
    /// (cwd) and whether Bloom started it (originator "bloom").
    public bool Met { get; set; }
    public string? Parent { get; set; }
    public string? Folder { get; set; }
    public bool Bloom { get; set; }

    public bool IsCodex => Thread is not null;

    /// The file shrank (rewritten): read again from the start, as if new. The calls already counted stay counted once,
    /// since their keys are known.
    public void Restart()
    {
        Offset = 0;
        Model = null;
        Effort = TokenEffort.Unknown;
        Records = false;
        LastTotal = 0;
        (Met, Parent, Folder, Bloom) = (false, null, null, false);
    }
}
