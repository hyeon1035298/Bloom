using System;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// The AI failures Bloom words itself, one text each (used where they happen), and Friendly: a CLI's own error text
/// turned into what happened and what to do next. The raw text goes to the log; when it matches nothing known, a chat
/// shows it under the message for the bug report.
internal static class AiErrors
{
    public static string Stopped => Tr("답변 중지됨", "Answer stopped");
    public static string ClaudeMissing => Tr("Claude Code를 찾지 못했습니다. 설치 후 로그인하세요.", "Claude Code not found. Install it and sign in.");
    public static string CodexMissing => Tr("Codex를 찾지 못했습니다. 설치 후 로그인하세요.", "Codex not found. Install it and sign in.");
    public static string OmpMissing => Tr("omp를 찾지 못했습니다. PATH에 omp가 있거나 설정에서 경로를 알려 주세요.", "omp not found. Put omp on the PATH or set its path in the settings.");
    public static string OmpStartFailed => Tr("omp를 시작하지 못했습니다. 설치 상태를 확인하고 다시 보내세요.", "Could not start omp. Check its installation and send again.");
    public static string OmpNoAnswer => Tr("omp가 답 없이 끝났습니다. 다시 보내세요.", "omp ended without an answer. Send again.");
    public static string Missing(bool chatgpt) => chatgpt ? CodexMissing : ClaudeMissing;
    public static string ClaudeStartFailed => Tr("Claude Code를 시작하지 못했습니다. 설치 상태를 확인하고 다시 보내세요.", "Could not start Claude Code. Check its installation and send again.");
    public static string StartFailed => Tr("AI를 시작하지 못했습니다. 설치 상태를 확인하고 다시 시도하세요.", "Could not start AI. Check its installation and try again.");
    public static string ClaudeNoAnswer => Tr("Claude 답변이 없습니다. 다시 보내세요.", "No answer from Claude. Send again.");
    public static string CodexNoAnswer => Tr("Codex 답변이 없습니다. 다시 보내세요.", "No answer from Codex. Send again.");
    public static string Failed => Tr("답변 실패 · 다시 보내세요", "Answer failed. Send the message again.");
    public static string TimedOut => Tr("90초 동안 응답 없음 · 다시 시도하세요", "No response for 90 s. Try again.");
    public static string SignInChanged => Tr("ChatGPT 로그인이 바뀌어 답변을 멈췄습니다. 다시 보내세요.", "The ChatGPT sign-in changed, so the answer stopped. Send it again.");

    // ── API providers: each failure worded where it happens (ApiClient), naming the provider ──
    public static string ApiModelGone => Tr("이 API 모델을 찾지 못했습니다. 다른 모델을 선택하고 다시 보내세요.", "This API model is not available. Choose another model and send again.");
    public static string ApiNoKey(string provider) => Tr($"{provider} API 키가 없습니다. 설정 > AI 계정에서 키를 추가하세요.", $"No {provider} API key. Add one in Settings > AI accounts.");
    public static string ApiNetwork(string provider) => Tr($"{provider}에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 보내세요.", $"Could not connect to {provider}. Check your connection and send again.");
    public static string ApiNoAnswer(string provider) => Tr($"{provider} 답변이 없습니다. 다시 보내세요.", $"No answer from {provider}. Send again.");
    public static string ApiTooManySteps(int steps) => Tr($"도구를 {steps}번 사용한 뒤 멈췄습니다. 이어서 하려면 다시 보내세요.", $"Stopped after {steps} tool steps. Send again to go on.");
    public static string ApiCutOff(string provider) => Tr($"{provider} 답변이 중간에 끊겼습니다. 다시 보내세요.", $"The answer from {provider} was cut off. Send again.");
    public static string ApiTooLarge(string provider) => Tr($"{provider} 답변이 너무 커서 연결을 닫았습니다. 요청을 나눠 다시 보내세요.", $"The answer from {provider} was too large, so the connection was closed. Split the request and send again.");
    public static string ApiTooLong(string provider) => Tr($"{provider} 답변이 너무 오래 이어져 연결을 닫았습니다. 다시 보내세요.", $"The answer from {provider} ran too long, so the connection was closed. Send again.");
    public static string ApiTurnTooLong => Tr("한 답변이 너무 오래 걸려 멈췄습니다. 이어서 하려면 다시 보내세요.", "One answer took too long and was stopped. Send again to go on.");
    public static string ApiTooManyCalls(int calls) => Tr($"도구 호출이 {calls}번을 넘어 멈췄습니다. 이어서 하려면 다시 보내세요.", $"Stopped after more than {calls} tool calls. Send again to go on.");

    /// A request the provider refused: 3xx a redirect (not followed, so the key goes nowhere else), 401/403 the key,
    /// 402/429 the rate or the credit, 404 the model, 5xx the provider itself; anything else with what it said
    /// (`message`, the key already taken out). A 404 or 5xx keeps what it said under the line too: a provider lists
    /// models a key may not use ("no longer available to new users") and says why only there.
    public static string ApiStatus(string provider, int status, string message) => status switch
    {
        404 or >= 500 => ApiStatusLine(provider, status) + Details(message),
        _ => ApiStatusLine(provider, status) is { Length: > 0 } line ? line : ApiFailed(provider, message),
    };

    private static string ApiStatusLine(string provider, int status) => status switch
    {
        >= 300 and < 400 => Tr($"{provider}에서 요청을 다른 주소로 넘겼습니다. 키를 보호하기 위해 따라가지 않았습니다.", $"{provider} redirected the request elsewhere. Bloom did not follow it, to keep the key safe."),
        401 or 403 => Tr($"{provider}에서 API 키를 거부했습니다. 설정 > AI 계정에서 키를 확인하세요.", $"{provider} rejected the API key. Check it in Settings > AI accounts."),
        402 or 429 => Tr($"{provider} 요청 한도나 크레딧이 소진되었습니다. 잠시 후 다시 보내거나 요금제를 확인하세요.", $"{provider} rate limit or credit reached. Send again later or check your plan."),
        404 => Tr($"{provider}에서 이 모델을 찾지 못했습니다. 다른 모델을 선택하고 다시 보내세요.", $"{provider} could not find this model. Choose another model and send again."),
        >= 500 => Tr($"{provider} 서버 오류가 발생했습니다. 잠시 후 다시 보내세요.", $"{provider} server error. Send again shortly."),
        _ => "",
    };

    /// A failed request with the provider's own message under it (none: just the line).
    public static string ApiFailed(string provider, string message) =>
        Tr($"{provider} 요청이 실패했습니다. 다시 보내세요.", $"{provider} request failed. Send again.") + Details(message);

    private static string Details(string message) => message.Length > 0 ? "\n\n" + Tr($"오류 내용: {message}", $"Details: {message}") : "";

    /// Bloom's own texts pass through Friendly unchanged.
    private static bool Own(string e) => new[] { Stopped, ClaudeMissing, CodexMissing, OmpMissing, OmpStartFailed, OmpNoAnswer, ClaudeStartFailed, StartFailed, ClaudeNoAnswer, CodexNoAnswer, Failed, TimedOut, SignInChanged }.Contains(e);

    /// An omp error in words: it signs in with `omp login`, a usage limit is its provider's; anything else as Friendly.
    public static string Omp(string e, bool details = false)
    {
        if (Own(e)) return e;
        string l = e.ToLowerInvariant();
        if (l.Contains("login") || l.Contains("log in") || l.Contains("oauth") || l.Contains("authenticat") || l.Contains("401") || l.Contains("unauthorized"))
            return Tr("omp 로그인이 필요합니다. 터미널에서 omp login을 한 번 실행하세요.", "omp needs you to sign in. Run omp login once in a terminal.") + (details ? "\n\n" + Last(e) : "");
        return Friendly(e, false, details);
    }

    private static string Last(string e)
    {
        string last = e.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0) ?? "";
        return last.Length > 200 ? last[..200] + "…" : last;
    }

    /// `details`: an unknown error keeps its last line under the message (a chat answer; toasts stay one line).
    public static string Friendly(string e, bool chatgpt, bool details = false)
    {
        if (Own(e)) return e;
        if (e.Length > 0) Log.Write("ai error: " + e);
        string l = e.ToLowerInvariant();
        if (l.Contains("login") || l.Contains("log in") || l.Contains("oauth") || l.Contains("authenticat") || l.Contains("401") || l.Contains("unauthorized"))
            return chatgpt ? Tr("ChatGPT 로그인이 필요합니다. 터미널에서 codex login을 실행하세요.", "Sign in to ChatGPT. Run codex login in a terminal.") : Tr("Claude 로그인이 필요합니다. 터미널에서 claude를 실행하고 로그인하세요.", "Sign in to Claude. Run claude in a terminal and sign in.");
        if (l.Contains("rate limit") || l.Contains("usage limit") || l.Contains("limit reached") || l.Contains("quota") || l.Contains("429"))
            return Tr("AI 사용 한도에 도달했습니다. 한도 초기화 후 다시 보내세요.", "AI usage limit reached. Send again after the limit resets.");
        if ((l.Contains("claude") || l.Contains("codex")) && l.Contains("not found"))
            return Missing(chatgpt);
        if (l.Contains("timeout") || l.Contains("timed out"))
            return Tr("AI 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.", "AI response timed out. Try again shortly.");
        if (l.Contains("connect") || l.Contains("network") || l.Contains("fetch") || l.Contains("socket"))
            return Tr("AI에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 보내세요.", "Could not connect to AI. Check your connection and send again.");
        if (l.Contains("permission") || l.Contains("access denied"))
            return Tr("AI가 필요한 파일에 접근하지 못했습니다. 파일 권한을 확인하고 다시 보내세요.", "AI could not access a required file. Check file permissions and send again.");
        if (l.Contains("model") && (l.Contains("unsupported") || l.Contains("not available") || l.Contains("does not exist")))
            return Tr("선택한 AI 모델을 사용할 수 없습니다. 다른 모델을 선택하고 다시 보내세요.", "AI model unavailable. Choose another model and send again.");
        string generic = Tr("AI 답변을 받지 못했습니다. 다시 보내세요. 반복되면 로그인과 인터넷 연결을 확인하세요.", "Could not get an AI answer. Send again. If it persists, check sign-in and connection.");
        string? last = e.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
        if (!details || last is null) return generic;
        string shown = last.Length > 200 ? last[..200] + "…" : last;
        return generic + "\n\n" + Tr($"오류 내용: {shown}", $"Details: {shown}");
    }
}
