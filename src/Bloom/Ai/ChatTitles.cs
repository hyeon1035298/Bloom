using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bloom.Ai;

/// Conversation titles the AI sets per task: "[◆ Opus • Code ] 로그인 수정". The AI opens a reply with a hidden
/// <chat-title field="…">…</chat-title> line on its first answer and again when a new task starts; Bloom takes the
/// line out of the answer and adds the model's mark itself, so the mark is always the model that answered.
internal static partial class ChatTitles
{
    public static readonly string[] Fields = { "Design", "Video", "3D", "Code", "Search", "Files", "Etc" };

    public const string Instruction =
        "Chat title: start your reply with one line <chat-title field=\"F\">short task</chat-title> in your first reply, and again only "
        + "when the user's message starts a clearly different task (never for follow-ups on the same one). The user doesn't see this line; "
        + "Bloom adds the model's name. The short task is 2–6 words in the user's language, no quotes. F is one of: Design (Photoshop, "
        + "Illustrator, images, UI, presentations), Video (Premiere, After Effects, video editing or generation), 3D (Unity, Blender, "
        + "modeling, avatars), Code (coding, web, scripts, tools), Search (research, web search, product lookup), Files (file "
        + "organization, sorting, cleanup), Etc (anything else). ";

    /// The model's mark: ◆ Opus, ◇ Sonnet, ∴ Haiku, ❖ Fable; ✦ Astra, ✹ Sol, ◉ Terra, ☾ Luna for ChatGPT (by the family in
    /// the Codex slug, "gpt-6.1-sol"); another model keeps its own name. An API model is marked by its family the same
    /// way when it is one of these, else named; an omp one the same when it is a Claude.
    public static string Mark(string model)
    {
        if (AiModels.ApiParts(model) is { } api)
        {
            string family = Mark(api.id);
            bool known = family != "◇ Sonnet" || api.id.Contains("sonnet", StringComparison.OrdinalIgnoreCase);
            return known ? family : AiModels.Name(model);
        }
        if (AiModels.IsChatGpt(model))
        {
            string slug = AiModels.GptSlug(model) ?? GptCatalog.DefaultSlug() ?? "";
            string[] parts = slug.ToLowerInvariant().Split('-');
            foreach (var (family, mark) in new[] { ("astra", "✦ Astra"), ("sol", "✹ Sol"), ("terra", "◉ Terra"), ("luna", "☾ Luna") })
                if (parts.Contains(family)) return mark;
            return GptCatalog.Name(AiModels.GptSlug(model));
        }
        if (AiModels.OmpSlug(model) is { } ompSlug)
        {
            // omp's Claude models are marked as Claude's; its own default is "omp" and another maker's model keeps its own name
            if (ompSlug.Length == 0) return "omp";
            if (!ompSlug.Contains("claude", StringComparison.OrdinalIgnoreCase)) return AiModels.Name(model);
            model = ompSlug;
        }
        string m = model.ToLowerInvariant();
        if (m.Contains("opus", StringComparison.Ordinal)) return "◆ Opus";
        if (m.Contains("haiku", StringComparison.Ordinal)) return "∴ Haiku";
        if (m.Contains("fable", StringComparison.Ordinal)) return "❖ Fable";
        return "◇ Sonnet";
    }

    /// "[◆ Opus • Code ] 로그인 수정"; a field not in the list counts as Etc.
    public static string Compose(string model, string field, string task)
    {
        string f = Fields.FirstOrDefault(x => string.Equals(x, field.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Etc";
        string s = Spaces().Replace(task, " ").Trim().Trim('"', '\'', '“', '”', '‘', '’');
        if (s.Length > 40) s = s[..39].TrimEnd() + "…";
        return $"[{Mark(model)} • {f} ] {s}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^<chat-title(?:\s+field\s*=\s*[""']?([^""'>]*)[""']?)?\s*>(.*?)</chat-title>$", RegexOptions.Singleline)]
    private static partial Regex Tag();

    private const string Open = "<chat-title", Close = "</chat-title>";

    /// The start of one answer as it streams in: held back while it may still be a title line, then given out without it.
    internal sealed class Lead
    {
        private StringBuilder? held = new(); // null once the start is settled

        /// The text to show now ("" while waiting); `field`/`task` when a title line just ended.
        public string Take(string piece, out string? field, out string? task)
        {
            field = task = null;
            if (held is null) return piece;
            held.Append(piece);
            string s = held.ToString(), lead = s.TrimStart();
            bool maybe = lead.Length < Open.Length ? Open.StartsWith(lead, StringComparison.Ordinal) : lead.StartsWith(Open, StringComparison.Ordinal);
            if (maybe)
            {
                int end = lead.IndexOf(Close, StringComparison.Ordinal);
                if (end < 0 && lead.Length < 400) return "";
                if (end >= 0)
                {
                    held = null;
                    var m = Tag().Match(lead[..(end + Close.Length)]);
                    if (m.Success && m.Groups[2].Value.Trim().Length > 0)
                    {
                        field = m.Groups[1].Value;
                        task = m.Groups[2].Value;
                    }
                    return lead[(end + Close.Length)..].TrimStart();
                }
            }
            held = null;
            return s;
        }

        /// What is still held when the answer ends (an answer that only looked like a title line).
        public string Flush()
        {
            string s = held?.ToString() ?? "";
            held = null;
            return s;
        }
    }
}
