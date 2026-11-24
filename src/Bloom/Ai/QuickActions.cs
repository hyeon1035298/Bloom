using System;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// One AI action on a piece of text.
internal sealed record QuickAction(string Key, string Ko, string En, char Glyph, string Words, string Instruction);

/// AI 빠른 동작: what can be done to a piece of text in one go (summarize, translate, fix, …). Each is one
/// OneShot call with the translation engine's model (light and fast by default; 설정 > Bloom Lens > 번역).
internal static class QuickActions
{
    public static readonly QuickAction[] All =
    {
        new("summarize", "요약하기", "Summarize", '', "summarize summary tldr 요약 줄이기 정리", "Summarize the text concisely: a short paragraph, or 3–5 bullet points when it has several separate points."),
        new("translate", "번역하기", "Translate", '', "translate translation 번역 영어 한국어 일본어", ""), // the target comes from the translation settings
        new("fix", "맞춤법 · 문법 고치기", "Spelling · Grammar", '', "fix spelling grammar proofread typo 맞춤법 문법 오타 띄어쓰기 고치기 교정", "Fix spelling, grammar, spacing and punctuation only. Change nothing else — not the wording, not the tone."),
        new("improve", "자연스럽게 다듬기", "Polish Writing", '', "improve rewrite polish better 다듬기 자연스럽게 고쳐쓰기 윤문", "Rewrite the text so it reads naturally and clearly, keeping its meaning, language and tone."),
        new("shorter", "짧게 줄이기", "Make Shorter", '', "shorter shorten concise 짧게 줄이기 간결", "Make it about half as long, keeping the key points and the tone."),
        new("longer", "자세히 늘리기", "Expand Details", '', "longer expand elaborate 길게 늘리기 자세히", "Expand it with more detail and explanation, keeping the tone and language."),
        new("formal", "정중하게", "More Polite", '', "formal polite professional 정중 공손 존댓말 격식 비즈니스", "Rewrite it in a polite, professional tone. In Korean, use natural polite speech (해요체 or 하십시오체, whichever fits)."),
        new("casual", "편하게", "More Casual", '', "casual friendly 편하게 친근 반말", "Rewrite it in a friendly, casual tone, keeping the language."),
        new("bullets", "핵심만 목록으로", "Key Bullet Points", '', "bullet list key points 목록 핵심 정리 글머리", "Turn it into a concise bullet list of the key points (markdown \"- \" bullets)."),
        new("explain", "쉽게 설명하기", "Explain Simply", '', "explain meaning what code 설명 뜻 쉽게 코드", "Explain what the text means in simple words. If it is code, explain what it does step by step. Answer in {ui}."),
        new("reply", "답장 초안 쓰기", "Draft a Reply", '', "reply answer respond email message 답장 답변 회신 메일", "The user received this message. Draft a reply they could send, in the message's language and a matching tone."),
    };

    public static QuickAction? Find(string key) => Array.Find(All, a => a.Key == key);

    private const string Base =
        "You are a writing assistant built into a desktop launcher. The user selected some text and chose an action. " +
        "Apply the action to the text they send. Reply with ONLY the resulting text — no preamble, no quotes around it, no notes or explanations " +
        "(unless the action is to explain). Keep the text's language unless the action is translation. Keep line breaks, lists and markdown where they fit.";

    /// The system prompt for an action, or for the user's own words (`custom`).
    public static string System(string key, string? custom, string source)
    {
        if (custom is { Length: > 0 }) return Base + "\n\nThe action, in the user's words: " + custom;
        var a = Find(key);
        if (a is null) return Base;
        string instruction = key == "translate"
            ? $"Translate the text into {Translator.LanguageName(Translator.TargetFor(source, Settings.Current.TranslateTo))}. Natural, not word for word."
            : a.Instruction.Replace("{ui}", Translator.LanguageName(Code), StringComparison.Ordinal);
        return Base + "\n\nThe action: " + instruction;
    }

    /// The words an action is found by, in both languages.
    public static MatchTarget Match(QuickAction a) => MatchTarget.Named(a.Ko, a.En, a.Words);

    public static string Title(QuickAction a) => Tr(a.Ko, a.En);

    public static bool Known(string typed) => All.Any(a => string.Equals(Title(a), typed.Trim(), StringComparison.OrdinalIgnoreCase));
}
