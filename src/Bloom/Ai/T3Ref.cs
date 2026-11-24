namespace Bloom.Ai;

/// Where a chat of the window's T3 side lives in T3: its thread (null for a new one not made yet), the project it is in
/// (or none: a scratch folder), and the thread as last read. Never saved: T3 keeps the thread.
internal sealed class T3Ref
{
    public string? ThreadId { get; set; }
    public string? ProjectId { get; set; }
    public bool Scratch { get; set; }
    public string Status { get; set; } = "idle";
    public int Pending { get; set; }
    public bool Settled { get; set; }

    /// The thread runs on Antigravity (T3Threads.Antigravity): Bloom shows it and sends it nothing.
    public bool ViewOnly { get; set; }

    /// The launch's id, kept so sending the same again after a lost answer makes one thread, not two; and what it
    /// launched (words, project, model): anything changed since gets a new id.
    public string? LaunchId { get; set; }
    public string? LaunchOf { get; set; }

    public bool Active => T3Wire.ActiveStatus(Status);
}
