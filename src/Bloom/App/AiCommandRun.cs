using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Search;

namespace Bloom.App;

/// 내 AI 명령: a saved prompt run on a text (the selection, the clipboard, or none), one step after another when it has
/// several (a macro: each step works on the answer of the one before). Each step is one OneShot call with the model,
/// thinking level and account picked for the AI chat; the model may search the web, nothing else.
internal static class AiCommandRun
{
    /// Steps are split by this in the prompt: "핵심 3줄 요약 >> 영어로 번역".
    public const string StepMark = ">>";

    private const string System =
        "You are an assistant built into a desktop launcher, running a command the user saved. Do what the instruction asks. " +
        "Reply with ONLY the result, ready to use: no preamble, no notes about what you did, no quotes around it. " +
        "Keep the text's language unless the instruction says otherwise. Use markdown where it helps.";

    /// The prompts the command runs, in order. A step that is the name or keyword of another AI command runs that
    /// command's own steps (one level: a step inside those is a prompt as written).
    public static List<string> Steps(UserCommand c, IEnumerable<UserCommand> all)
    {
        var list = new List<string>();
        foreach (string step in Split(c.Body))
        {
            var other = all.FirstOrDefault(x => x.IsAi && x.Id != c.Id
                && (string.Equals(x.Name, step, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Keyword, step, StringComparison.OrdinalIgnoreCase)));
            if (other is null) list.Add(step);
            else list.AddRange(Split(other.Body));
        }
        return list;
    }

    public static List<string> Split(string body) =>
        body.Split(StepMark, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// What one step sends: the prompt with {input} (and {date} {time}) filled in, or the prompt and then the text.
    public static string Message(string prompt, string input, DateTime? now = null)
    {
        prompt = UserCommands.SnippetText(prompt); // "\n" in the one-line form is a new line
        if (prompt.Contains("{input}", StringComparison.OrdinalIgnoreCase)) return UserCommands.Expand(prompt, input, null, now);
        prompt = UserCommands.Expand(prompt, "", null, now);
        return input.Trim().Length == 0 ? prompt : $"{prompt}\n\n<text>\n{input.Trim()}\n</text>";
    }

    /// The model the AI chat would use now, as "Claude · Sonnet · 높음".
    public static string Who()
    {
        string model = AiModels.DefaultModel();
        string effort = AiModels.DefaultEffort(model);
        return AiModels.HasEffort(model) ? AiModels.Who(model) + " · " + AiModels.EffortName(effort) : AiModels.Who(model);
    }

    /// Runs every step; `step` hears (1-based step, steps) as each starts. → the last answer, or the first error (and the
    /// model it ran on); (null, null, …) when cancelled.
    public static async Task<(string? text, string? error, string model)> RunAsync(UserCommand c, string input, IEnumerable<UserCommand> all, Action<int, int>? step, CancellationToken ct)
    {
        string model = AiModels.DefaultModel();
        string provider = AiModels.Provider(model);
        string? effort = AiModels.HasEffort(model) ? AiModels.DefaultEffort(model) : null;
        var steps = Steps(c, all);
        if (steps.Count == 0) return (null, Tr("프롬프트가 비어 있습니다. 명령을 고치세요.", "The prompt is empty. Edit the command."), model);
        string text = input;
        for (int i = 0; i < steps.Count; i++)
        {
            step?.Invoke(i + 1, steps.Count);
            var (answer, error) = await OneShot.Ask(System, Message(steps[i], text, null), provider, model, effort, "mine-" + c.Id, ct, web: true);
            if (ct.IsCancellationRequested) return (null, null, model);
            if (answer is null) return (null, steps.Count > 1 ? Tr($"{i + 1}단계 실패 · {error}", $"Step {i + 1} failed · {error}") : error ?? Tr("응답 없음 · 다시 시도하세요", "No response. Try again."), model);
            text = answer;
        }
        return (text, null, model);
    }
}
