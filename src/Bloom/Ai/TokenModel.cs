namespace Bloom.Ai;

/// One model in a TokenView. Provider: "claude" (Claude Code's logs) or "codex"; Model as the log names it, its date
/// tail taken off ("claude-opus-5-5", "gpt-6.1-sol"). Cost: USD at API prices, null when no price is known. Share: of
/// the view's Sum.Total, 0…1.
internal sealed record TokenModel(string Provider, string Model, TokenCounts Counts, double? Cost, double Share);
