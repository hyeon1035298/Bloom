using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;

namespace Bloom.Ui;

/// An answer still being written, as Markdown.Render last drew it: one document kept from update to update, and for
/// each of its blocks the source it was made from. The next update rebuilds only from the first block whose source
/// changed (in practice the last one) and keeps the rest as they are.
internal sealed class MarkdownStream
{
    /// The document being kept; null until the first rendering.
    internal ReadText? View;

    /// The size it was drawn at: another size draws it anew.
    internal double Size;

    /// Per top-level block, in order: what it was made from (Markdown.Key).
    internal readonly List<string> Keys = new();

    /// The last block, which had its room below taken away, and the margin it had before.
    internal Block? Last;
    internal Thickness LastMargin;
}
