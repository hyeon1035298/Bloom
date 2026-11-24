using System;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// The AI failures Bloom words itself, one text each (used where they happen), and Friendly: a CLI's own error text
/// turned into what happened and what to do next. The raw text goes to the log; when it matches nothing known, a chat
/// shows it under the message for the bug report.
internal static class AiErrors
{
    public static string Stopped => Tr("답변 중지됨", "Response stopped");
    public static string ClaudeMissing => Tr("Claude Code를 찾을 수 없습니다. 설치 후 로그인하세요.", "Claude Code not found. Please install it and sign in.");
    public static string CodexMissing => Tr("Codex를 찾을 수 없습니다. 설치 후 로그인하세요.", "Codex not found. Please install it and sign in.");
    public static string OmpMissing => Tr("omp를 찾을 수 없습니다. PATH에 omp가 있는지 확인하거나 설정에서 경로를 지정하세요.", "omp not found. Make sure omp is in your PATH or set its path in Settings.");
    public static string OmpStartFailed => Tr("omp를 시작하지 못했습니다. 설치 상태를 확인하고 다시 보내세요.", "Failed to start omp. Check your installation and send again.");
    public static string OmpNoAnswer => Tr("omp가 응답 없이 종료되었습니다. 다시 보내세요.", "omp exited without a response. Please send again.");
    public static string Missing(bool chatgpt) => chatgpt ? CodexMissing : ClaudeMissing;
    public static string ClaudeStartFailed => Tr("Claude Code를 시작하지 못했습니다. 설치 상태를 확인하고 다시 보내세요.", "Failed to start Claude Code. Check your installation and send again.");
    public static string StartFailed => Tr("AI를 시작하지 못했습니다. 설치 상태를 확인하고 다시 시도하세요.", "Failed to start AI. Check your installation and try again.");
    public static string ClaudeNoAnswer => Tr("Claude의 응답이 없습니다. 다시 보내세요.", "No response from Claude. Please send again.");
    public static string CodexNoAnswer => Tr("Codex의 응답이 없습니다. 다시 보내세요.", "No response from Codex. Please send again.");
    public static string Failed => Tr("답변 실패 · 다시 보내세요", "Response failed · Please send again");
    public static string TimedOut => Tr("90초 동안 응답 없음 · 다시 시도하세요", "No response for 90 seconds · Please try again");
    public static string SignInChanged => Tr("ChatGPT 로그인 정보가 변경되어 답변을 중단했습니다. 다시 보내세요.", "Response stopped because your ChatGPT sign-in changed. Please send again.");

    // ── API providers: each failure worded where it happens (ApiClient), naming the provider ──
    public static string ApiModelGone => Tr("이 API 모델을 찾을 수 없습니다. 다른 모델을 선택하고 다시 보내세요.", "API model not found. Select a different model and send again.");
    public static string ApiNoKey(string provider) => Tr($"{provider} API 키가 없습니다. 설정 > AI 계정에서 키를 추가하세요.", $"No {provider} API key found. Add a key in Settings > AI Accounts.");
    public static string ApiNetwork(string provider) => Tr($"{provider}에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 보내세요.", $"Could not connect to {provider}. Check your internet connection and send again.");
    public static string ApiNoAnswer(string provider) => Tr($"{provider}의 응답이 없습니다. 다시 보내세요.", $"No response from {provider}. Please send again.");
    public static string ApiTooManySteps(int steps) => Tr($"도구를 {steps}번 사용한 뒤 멈췄습니다. 계속하려면 다시 보내세요.", $"Stopped after using tools {steps} times. Send again to continue.");
    public static string ApiCutOff(string provider) => Tr($"{provider} 답변이 중간에 끊겼습니다. 다시 보내세요.", $"Response from {provider} was interrupted. Please send again.");
    public static string ApiTooLarge(string provider) => Tr($"{provider} 답변이 너무 길어 연결을 종료했습니다. 요청을 나누어 다시 보내세요.", $"Closed connection because the response from {provider} was too large. Split your request and send again.");
    public static string ApiTooLong(string provider) => Tr($"{provider} 답변이 너무 오래 걸려 연결을 종료했습니다. 다시 보내세요.", $"Closed connection because the response from {provider} took too long. Please send again.");
    public static string ApiTurnTooLong => Tr("답변 시간이 너무 오래 걸려 멈췄습니다. 계속하려면 다시 보내세요.", "Stopped because the response took too long. Send again to continue.");
    public static string ApiTooManyCalls(int calls) => Tr($"도구 호출이 {calls}회를 초과하여 멈췄습니다. 계속하려면 다시 보내세요.", $"Stopped after exceeding {calls} tool calls. Send again to continue.");

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
        >= 300 and < 400 => Tr($"{provider}에서 요청을 다른 주소로 리디렉션했습니다. 키 보호를 위해 이동하지 않았습니다.", $"{provider} redirected the request to another URL. Did not follow to protect your API key."),
        401 or 403 => Tr($"{provider}에서 API 키를 거부했습니다. 설정 > AI 계정에서 키를 확인하세요.", $"{provider} rejected the API key. Check your key in Settings > AI Accounts."),
        402 or 429 => Tr($"{provider} 요청 한도 또는 크레딧이 소진되었습니다. 잠시 후 다시 보내거나 요금제를 확인하세요.", $"{provider} rate limit or credits exhausted. Try again later or check your plan."),
        404 => Tr($"{provider}에서 이 모델을 찾을 수 없습니다. 다른 모델을 선택하고 다시 보내세요.", $"Model not found on {provider}. Select a different model and send again."),
        >= 500 => Tr($"{provider} 서버 오류가 발생했습니다. 잠시 후 다시 보내세요.", $"{provider} server error. Please try sending again shortly."),
        _ => "",
    };

    /// A failed request with the provider's own message under it (none: just the line).
    public static string ApiFailed(string provider, string message) =>
        Tr($"{provider} 요청에 실패했습니다. 다시 보내세요.", $"{provider} request failed. Please send again.") + Details(message);

    private static string Details(string message) => message.Length > 0 ? "\n\n" + Tr($"오류 내용: {message}", $"Error details: {message}") : "";

    /// Bloom's own texts pass through Friendly unchanged.
    private static bool Own(string e) => new[] { Stopped, ClaudeMissing, CodexMissing, OmpMissing, OmpStartFailed, OmpNoAnswer, ClaudeStartFailed, StartFailed, ClaudeNoAnswer, CodexNoAnswer, Failed, TimedOut, SignInChanged }.Contains(e);

    /// An omp error in words: it signs in with `omp login`, a usage limit is its provider's; anything else as Friendly.
    public static string Omp(string e, bool details = false)
    {
        if (Own(e)) return e;
        string l = e.ToLowerInvariant();
        if (l.Contains("login") || l.Contains("log in") || l.Contains("oauth") || l.Contains("authenticat") || l.Contains("401") || l.Contains("unauthorized"))
            return Tr("omp 로그인이 필요합니다. 터미널에서 omp login을 한 번 실행하세요.", "omp sign-in required. Run omp login once in your terminal.") + (details ? "\n\n" + Last(e) : "");
        // omp's default model can be one the signed-in account doesn't offer ("model_not_found", "model is not supported")
        if (l.Contains("model_not_found") || (l.Contains("model") && (l.Contains("not supported") || l.Contains("does not exist"))))
            return Tr("이 계정에서는 선택한 모델을 사용할 수 없습니다. 다른 모델을 선택해 다시 보내세요.", "The selected model is not available for this account. Choose a different model and send again.") + (details ? "\n\n" + Last(e) : "");
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
            return chatgpt ? Tr("ChatGPT 로그인이 필요합니다. 터미널에서 codex login을 실행하세요.", "ChatGPT sign-in required. Run codex login in your terminal.") : Tr("Claude 로그인이 필요합니다. 터미널에서 claude를 실행하고 로그인하세요.", "Claude sign-in required. Run claude in your terminal and sign in.");
        if (l.Contains("rate limit") || l.Contains("usage limit") || l.Contains("limit reached") || l.Contains("quota") || l.Contains("429"))
            return Tr("AI 사용 한도에 도달했습니다. 한도가 초기화된 후 다시 보내세요.", "AI usage limit reached. Please send again after the limit resets.");
        if ((l.Contains("claude") || l.Contains("codex")) && l.Contains("not found"))
            return Missing(chatgpt);
        if (l.Contains("timeout") || l.Contains("timed out"))
            return Tr("AI 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.", "AI response timed out. Please try again shortly.");
        if (l.Contains("connect") || l.Contains("network") || l.Contains("fetch") || l.Contains("socket"))
            return Tr("AI에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 보내세요.", "Could not connect to AI. Check your internet connection and send again.");
        if (l.Contains("permission") || l.Contains("access denied"))
            return Tr("AI가 필요한 파일에 접근하지 못했습니다. 파일 권한을 확인하고 다시 보내세요.", "AI could not access the required files. Check file permissions and send again.");
        if (l.Contains("model") && (l.Contains("unsupported") || l.Contains("not available") || l.Contains("does not exist")))
            return Tr("선택한 AI 모델을 사용할 수 없습니다. 다른 모델을 선택하고 다시 보내세요.", "The selected AI model is unavailable. Choose a different model and send again.");
        string generic = Tr("AI 답변을 받지 못했습니다. 다시 보내세요. 문제가 반복되면 로그인 상태와 인터넷 연결을 확인하세요.", "Did not receive an AI response. Please send again. If this persists, check your sign-in and internet connection.");
        string? last = e.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
        if (!details || last is null) return generic;
        string shown = last.Length > 200 ? last[..200] + "…" : last;
        return generic + "\n\n" + Tr($"오류 내용: {shown}", $"Error details: {shown}");
    }
}
