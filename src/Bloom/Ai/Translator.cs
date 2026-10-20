using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// Translation for screen OCR: one short CLI call per text, no conversation kept (each call is a
/// fresh, small prompt — what realtime translation repeats). Claude = the model picked for translation
/// (Haiku by default, no thinking); ChatGPT = the Codex model picked for translation; omp = the model picked for its quick
/// jobs (omp's own default model unless one is picked, no thinking). Each side has its
/// own thinking level (lowest by default). Both use the CLIs' own logins, like the chat; API = the API model picked
/// for translation, with the user's key. Every call counts against the user's plan.
internal static class Translator
{
    public static readonly (string value, string label)[] Targets = { ("auto", Tr("자동", "Auto")), ("ko", Tr("한국어", "Korean")), ("en", Tr("영어", "English")), ("ja", Tr("일본어", "Japanese")), ("zh", Tr("중국어", "Chinese")) };

    /// The side translation and AI 빠른 동작 use: the setting's, with omp only while it is on this PC and API only while an
    /// API model can be used (else Claude).
    public static string Provider() => Settings.Current.TranslateProvider switch
    {
        "chatgpt" => "chatgpt",
        "omp" when OmpCli.FindCli() is not null => "omp",
        "api" when Model("api").Length > 0 => "api",
        _ => "claude",
    };

    /// The sides to choose from, in their order: Claude, ChatGPT, omp when it is on this PC, and API when an API model can be used.
    public static string[] Providers()
    {
        var list = new System.Collections.Generic.List<string> { "claude", "chatgpt" };
        if (OmpCli.FindCli() is not null) list.Add("omp");
        if (Model("api").Length > 0) list.Add("api");
        return list.ToArray();
    }

    /// A side's short name for chips and choices: Claude, ChatGPT, omp, or the API model's provider.
    public static string Label(string provider) => provider switch
    {
        "chatgpt" => "ChatGPT",
        "omp" => "omp",
        "api" => AiModels.ApiProviderOf(Model("api"))?.Name ?? "API",
        _ => "Claude",
    };

    /// The model a side translates with: Claude Haiku, omp's quick-job model, the ChatGPT model set for translation
    /// (default: the catalog's lightest, its name containing "Luna" or "mini"), or the API model set for it (default: the
    /// first listed; "" when there is none).
    public static string Model(string provider)
    {
        if (provider == "api")
            return Settings.Current.TranslateApiModel is { } api && AiModels.ApiReady(api) ? api : ApiCatalog.Ids().FirstOrDefault() ?? "";
        if (provider == "omp") return AiModels.Omp(Settings.Current.OmpFastModel);
        if (provider != "chatgpt") return Array.IndexOf(AiModels.Claude, Settings.Current.TranslateClaudeModel) >= 0 ? Settings.Current.TranslateClaudeModel : "haiku";
        var s = Settings.Current;
        if (s.TranslateGptModel is { Length: > 0 } g && (GptCatalog.All.Count == 0 || GptCatalog.Find(g) is not null)) return AiModels.Gpt(g);
        var light = GptCatalog.All.FirstOrDefault(m => m.Name.Contains("Luna", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("mini", StringComparison.OrdinalIgnoreCase));
        string? slug = light?.Slug ?? GptCatalog.DefaultSlug();
        return slug is null ? "chatgpt" : AiModels.Gpt(slug);
    }

    /// The levels the side's translation model takes (Haiku: none).
    public static string[] Levels(string provider)
    {
        string m = Model(provider);
        return AiModels.HasEffort(m) ? AiModels.Efforts(m) : Array.Empty<string>();
    }

    /// The side's level, fitted to its model; null = the model has none (Haiku).
    public static string? Effort(string provider)
    {
        string m = Model(provider);
        if (!AiModels.HasEffort(m)) return null;
        var s = Settings.Current;
        return AiModels.Fit(m, provider switch { "omp" => s.OmpFastEffort, "chatgpt" => s.TranslateGptEffort, "api" => s.TranslateApiEffort, _ => s.TranslateClaudeEffort });
    }

    public static void SetEffort(string provider, string e)
    {
        if (provider == "omp") Settings.Current.OmpFastEffort = e;
        else if (provider == "chatgpt") Settings.Current.TranslateGptEffort = e;
        else if (provider == "api") Settings.Current.TranslateApiEffort = e;
        else Settings.Current.TranslateClaudeEffort = e;
        Settings.Save();
    }

    /// "Claude · Haiku" / "omp · Haiku 5.5 · 끔" / "ChatGPT · GPT-6-Luna · 최소" / "DeepSeek · deepseek-chat"
    public static string Who(string provider)
    {
        string who = AiModels.Who(Model(provider));
        return Effort(provider) is { } e ? who + " · " + AiModels.EffortName(e) : who;
    }

    /// "auto": text in the interface's language goes to another one (Korean ↔ English; Japanese and Chinese → English),
    /// anything else to the interface's language.
    public static string TargetFor(string text, string setting)
    {
        if (setting != "auto") return setting;
        int hangul = 0, latin = 0, kana = 0, han = 0, letters = 0;
        foreach (char c in text)
        {
            if (c is >= '가' and <= '힣') hangul++;
            else if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            else if (c is >= '\u3040' and <= '\u30FF') kana++;
            else if (c is >= '\u4E00' and <= '\u9FFF') han++;
            if (char.IsLetter(c)) letters++;
        }
        bool Most(int n) => letters > 0 && n * 2 > letters;
        return Code switch
        {
            "en" => Most(latin) ? "ko" : "en",
            "ja" => Most(kana + han) && kana > 0 ? "en" : "ja",
            "zh" => Most(han) && kana == 0 ? "en" : "zh",
            _ => Most(hangul) ? "en" : "ko",
        };
    }

    /// A language code's English name, for the prompts.
    public static string LanguageName(string code) => code switch { "en" => "English", "ja" => "Japanese", "zh" => "Simplified Chinese", _ => "Korean" };

    public static string TargetName(string t) => t switch { "en" => Tr("영어", "English"), "ja" => Tr("일본어", "Japanese"), "zh" => Tr("중국어", "Chinese"), _ => Tr("한국어", "Korean") };

    private static string Prompt(string target) =>
        $"You translate text that was read from the user's screen by OCR. Translate it into {LanguageName(target)}. " +
        "Fix obvious OCR mistakes (broken words, wrong letters) silently. Keep the line structure when it carries meaning (lists, dialogue, UI labels); join lines that are one sentence. " +
        "Output only the translation — no notes, quotes, or explanations. If the text is already in the target language, output it cleaned up.";

    /// → (translation, null) or (null, error).
    public static async Task<(string? text, string? error)> Run(string text, string provider, string target, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length > 6000) text = text[..6000];
        if (provider == "chatgpt")
        {
            var answer = await GptCatalog.ResolveAsync(() => OneShot.Ask(Prompt(target), text, provider,
                Model(provider), Effort(provider), "translate-" + target, ct), ct);
            return await answer;
        }
        return await OneShot.Ask(Prompt(target), text, provider, Model(provider), Effort(provider), "translate-" + target, ct);
    }
}
