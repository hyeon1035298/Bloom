namespace Bloom.Ai;

/// The reasoning effort a call was made at, as its log names it (Claude Code: the assistant line's "effort"; Codex: the
/// last turn_context's effort); Unknown when the line names none or a word not known here. In order of effort, so a
/// block can list the higher first. Shown as the logs write it: ToString().ToLowerInvariant() ("xhigh", "max").
internal enum TokenEffort : byte { Unknown, None, Minimal, Low, Medium, High, XHigh, Ultra, Persistent, Max }
