using System;
using System.Text.RegularExpressions;

namespace Bloom.Core;

/// Edits of a Markdown text in its own source: a checklist item's box ticked or cleared (메모 and 문서's 보기). Lines
/// are counted at '\n' and found in place, so everything else — "\r\n" or "\n", the last line's end — stays as it was.
internal static class MarkdownText
{
    /// A list item: its indent, its marker (-, *, + or a number of up to 9 digits with . or )) and its text. Ui\Markdown.cs
    /// draws lists by it.
    public static readonly Regex ListItem = new(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$");

    /// A checklist box at the start of a list item's text — [ ], [x] or [X], then a space or the line's end — its mark
    /// in group 1: the one rule for a checklist item, drawn (Ui\Markdown.cs) and ticked (ToggleTask).
    public static readonly Regex TaskBox = new(@"^\[([ xX])\](?:\s+|$)");

    /// A line's quote marks (">", "> >", ">>" …), as Ui\Markdown.cs takes them off: each > and one space after it.
    private static readonly Regex QuoteMarks = new(@"^(\s*>\s?)*");

    private static readonly Regex Heading = new(@"^#{1,6}\s+");
    private static readonly Regex Link = new(@"!?\[([^\]]*)\]\([^)]*\)"); // [text](url) and ![alt](url) → the words
    private static readonly Regex Strong = new(@"(\*\*|__|~~)(.+?)\1");
    private static readonly Regex Emphasis = new(@"(?<![\w*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])"); // *word*, not 2*3*4

    /// The text without its Markdown marks, for a one-line look (알림 센터, result cards): headings, quote marks, list
    /// markers and checklist boxes off each line, code fences dropped, **bold**, *italic*, ~~struck~~, `code` and links
    /// as their plain words.
    public static string Plain(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (Fence(line) is not null) continue;
            line = line[QuoteMarks.Match(line).Length..];
            line = Heading.Replace(line, "");
            if (ListItem.Match(line) is { Success: true } item) line = TaskBox.Replace(item.Groups[3].Value, "");
            line = Link.Replace(line, "$1");
            line = Strong.Replace(line, "$2");
            line = Emphasis.Replace(line, "$1");
            line = line.Replace("`", "", StringComparison.Ordinal);
            sb.Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// The fence a line opens or closes code with (``` or ~~~, at any indent, in a list item too), or null:
    /// Ui\Markdown.cs draws code by the same rule.
    public static string? Fence(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith("```", StringComparison.Ordinal) ? "```" : t.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : null;
    }

    /// Line `line` (0-based) without its line break, or null past the end.
    public static string? Line(string text, int line) => Find(text, line) is (int start, int end) ? text[start..end] : null;

    /// The text with the box of the checklist item on `line` flipped ([ ] ↔ [x]), or null when that line is no longer
    /// `expectedLine` (the text changed since it was drawn) or no longer a checklist item, or sits in fenced code.
    public static string? ToggleTask(string text, int line, string expectedLine)
    {
        if (Find(text, line) is not (int start, int end)) return null;
        string l = text[start..end];
        if (l != expectedLine || Fence(l) is not null || InCode(text, start)) return null;
        int quote = QuoteMarks.Match(l).Length;
        var item = ListItem.Match(l[quote..]);
        if (!item.Success) return null;
        var task = TaskBox.Match(item.Groups[3].Value);
        if (!task.Success) return null;
        int box = start + quote + item.Groups[3].Index + task.Groups[1].Index; // the character between the brackets
        return string.Concat(text.AsSpan(0, box), text[box] == ' ' ? "x" : " ", text.AsSpan(box + 1));
    }

    /// Where line `line` starts and where its text ends (before "\r\n" or "\n").
    private static (int start, int end)? Find(string text, int line)
    {
        if (line < 0) return null;
        int start = 0;
        for (int k = 0; k < line; k++)
        {
            int nl = text.IndexOf('\n', start);
            if (nl < 0) return null;
            start = nl + 1;
        }
        int end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, end);
    }

    /// Whether a fence opened above `upTo` (a line start) is still open there.
    private static bool InCode(string text, int upTo)
    {
        string? open = null;
        for (int at = 0; at < upTo;)
        {
            int nl = text.IndexOf('\n', at);
            if (nl < 0 || nl >= upTo) nl = upTo;
            string? fence = Fence(text[at..nl]);
            if (open is null) open = fence;
            else if (fence == open) open = null;
            at = nl + 1;
        }
        return open is not null;
    }
}
