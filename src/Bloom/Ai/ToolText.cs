using System.Globalization;
using System.Text;

namespace Bloom.Ai;

/// What an approval card shows of a tool call (a command, a path), so nothing can hide in it: control characters as
/// their Control Pictures (␀ … ␟, ␡; a tab ␉, a lone CR ␍), invisible format characters and other line separators
/// (bidi overrides, zero-width marks, BOM, U+2028) as ⟨U+202E⟩; line breaks stay line breaks.
internal static class ToolText
{
    public static string Visible(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n') sb.Append(c);
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') continue; // CRLF: the line break alone
                sb.Append('␍');
            }
            else if (c < ' ') sb.Append((char)(0x2400 + c));
            else if (c == '\u007f') sb.Append('␡');
            else if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                sb.Append($"⟨U+{(int)c:X4}⟩");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// How many lines `text` makes when every line separator counts: \n, a lone \r (CRLF once), \v, \f, U+0085, U+2028
    /// and U+2029.
    public static int Lines(string text)
    {
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] is '\n' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')))
                lines++;
        return lines;
    }

    /// The most characters of a call's arguments an approval card is made for: a call with more is refused (ApiToolbox) or
    /// declined (Chats.Asked) rather than asked about with part of it hidden. The card scrolls what it shows.
    public const int MaxPayload = 200_000;

    /// A labelled block for a card — `── label ──` then `text` made visible, whole: what is approved is all shown.
    public static string Block(string label, string text) => $"── {label} ──\n{Visible(text)}";

    /// The same on one line, cut at `max` (the chat's ✓ line under the answer; the card shows all of it).
    public static string Line(string text, int max = 80)
    {
        string line = Visible(text).Replace("\n", " ⏎ ", System.StringComparison.Ordinal);
        return line.Length > max ? line[..max] + "…" : line;
    }
}
