using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

internal readonly record struct SessionStart(bool Success, string? Error = null);

/// One conversation's link to a model: Claude Code (a long-lived `claude -p` stream), Codex for ChatGPT (a long-lived
/// `codex app-server`, or one `codex exec` per turn as its fallback), omp (a long-lived `omp --mode rpc`), or an API provider (ApiSession, one HTTP stream per turn). All events are raised on a worker
/// thread.
internal interface IChatSession : IDisposable
{
    string Model { get; }
    string Effort { get; }
    string? SessionId { get; }
    bool Busy { get; }
    bool Alive { get; }
    DateTime LastUsed { get; }

    event Action<string>? SessionStarted;
    event Action<string>? TextDelta;
    event Action<string?>? Status; // "웹 검색 중…", null = clear
    event Action<string>? AssistantText; // a whole message (when no deltas came)
    event Action<bool, string?>? TurnEnded; // ok, error text
    event Action<string>? Exited; // stderr tail
    event Action<ToolAsk>? ToolAsked; // a tool call waits for the user's yes (Claude with Bloom's tools)
    event Action<string>? ToolAskWithdrawn; // the CLI took the question back (request id)

    bool Start(string? resume, out string? error);
    Task<SessionStart> StartAsync(string? resume, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        bool success = Start(resume, out string? error);
        return Task.FromResult(new SessionStart(success, error));
    }
    /// The user's answer to a ToolAsk (Bloom remembers 항상 허용 itself, per action; the CLI is never told to stop asking).
    void Answer(ToolAsk ask, bool allow);
    /// `images`: pictures sent along (Bloom Lens, attached photos) — Claude gets them in the message, Codex through
    /// --image. Other attached files are named in the text; the session may read them and write new ones (Chats.FilesOf).
    bool Send(string text, IReadOnlyList<string> images);
    Task<bool> SendAsync(string text, IReadOnlyList<string> images, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult(Send(text, images));
    }
    void Interrupt();
    /// Completes once a disposed session's process is gone (Dispose only starts the ending). A session with no process of
    /// its own is closed at once; the pre-start counts a process still ending against its cap (Chats.PreStart).
    Task Closed => Task.CompletedTask;
}

/// A session that knows how much of the model's memory is in use and can tidy it (omp's get_state and compact). Events
/// are raised on a worker thread.
internal interface IContextGauge
{
    OmpUsage? Usage { get; }
    bool Compacting { get; }

    /// The numbers changed, or a tidy-up started or ended.
    event Action? UsageChanged;

    /// One line about omp's tidying: `running` while it goes on (shown as the status), then the finished line.
    event Action<string, bool>? CompactNote;

    /// The `compact` rpc (`instructions`: what to keep, may be ""); false when it can't start (busy, already tidying, gone).
    bool Compact(string instructions);

    /// Asks omp for the numbers again (the answer raises UsageChanged).
    void RefreshUsage();
}

/// Models and thinking levels as the launcher names them. A model id is a Claude model ("sonnet",
/// "opus", "haiku"), a ChatGPT one: "chatgpt:<Codex slug>" ("chatgpt" alone, from older threads,
/// is Codex's own default model), an omp one: "omp:<provider/model>" ("omp:" alone is omp's own default model),
/// or an API one: "api:<provider id>:<the provider's model id>".
internal static class AiModels
{
    public static readonly string[] Claude = { "sonnet", "opus", "haiku" };

    /// Claude: "fast" = thinking off (the launcher's quick default); the rest are Claude Code's --effort levels.
    public static readonly string[] ClaudeEfforts = { "fast", "low", "medium", "high", "xhigh", "max" };

    /// An API model's level that sends none: the model's own default (the only one for a model that has no levels).
    public const string Auto = "auto";

    private const string GptPrefix = "chatgpt:", ApiPrefix = "api:", OmpPrefix = "omp:";

    /// Every level name from lowest to highest, to find the nearest one a model takes.
    private static readonly string[] Rank = { Auto, "fast", "none", "off", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" };

    public static bool IsChatGpt(string model) => model == "chatgpt" || model.StartsWith(GptPrefix, StringComparison.Ordinal);

    public static bool IsApi(string model) => model.StartsWith(ApiPrefix, StringComparison.Ordinal);

    public static bool IsOmp(string model) => model.StartsWith(OmpPrefix, StringComparison.Ordinal);

    /// The side a model is on: "claude", "chatgpt", "omp" or "api" (Settings.AiProvider's values).
    public static string Provider(string model) => IsChatGpt(model) ? "chatgpt" : IsApi(model) ? "api" : IsOmp(model) ? "omp" : "claude";

    /// ChatGPT, omp (in a chat) and the APIs have no file tools: attachments go into the message, files come back as ```file: blocks.
    public static bool Inline(string model) => IsChatGpt(model) || IsApi(model) || IsOmp(model);

    public static string Gpt(string slug) => GptPrefix + slug;

    /// "omp:" + "provider/model"; an empty slug is omp's own default model.
    public static string Omp(string slug) => OmpPrefix + slug;

    /// "provider/model" of an omp model id ("" = omp's own default); null for the others.
    public static string? OmpSlug(string model) => IsOmp(model) ? model[OmpPrefix.Length..] : null;

    public static string Api(string provider, string id) => ApiPrefix + provider + ":" + id;

    /// An API model id's provider and the provider's own model id (which may hold ':' itself, as OpenRouter's do).
    public static (string provider, string id)? ApiParts(string model)
    {
        if (!IsApi(model)) return null;
        int colon = model.IndexOf(':', ApiPrefix.Length);
        return colon < 0 ? null : (model[ApiPrefix.Length..colon], model[(colon + 1)..]);
    }

    public static ApiProvider? ApiProviderOf(string model) => ApiParts(model) is { } p ? ApiProviders.Find(p.provider) : null;

    /// An API model that can be used now: its provider has a key and listed it.
    public static bool ApiReady(string model) => ApiParts(model) is { } p && ApiKeys.Has(p.provider) && ApiCatalog.Find(p.provider, p.id) is not null;

    /// Every model for the pickers: Claude's, ChatGPT's, omp's (when omp is on this PC), then each API provider's with a
    /// key, in that order.
    public static List<string> All() => Claude.Concat(GptCatalog.All.Select(m => Gpt(m.Slug))).Concat(OmpModelList.Ids()).Concat(ApiCatalog.Ids()).ToList();

    /// The Codex slug of a ChatGPT model id; null = Codex's own default.
    public static string? GptSlug(string model) => model.StartsWith(GptPrefix, StringComparison.Ordinal) ? model[GptPrefix.Length..] : null;

    /// "chatgpt" (older threads) as the model it stands for, so it compares with the menu's ids.
    public static string Normalize(string model) => model == "chatgpt" && GptCatalog.DefaultSlug() is { } s ? Gpt(s) : model;

    /// Haiku has no effort setting, nor an API or omp model that takes no reasoning level.
    public static bool HasEffort(string model) => IsApi(model) || IsOmp(model) ? Efforts(model).Length > 1 : model != "haiku";

    /// The levels a model takes: Claude's six, the ChatGPT model's own list from Codex, or the API model's (its
    /// provider's rule for it, Auto first; ApiClient sends only these).
    public static string[] Efforts(string model) => IsOmp(model) ? OmpCatalog.LevelsOf(OmpSlug(model)!)
        : IsChatGpt(model) ? GptCatalog.Levels(GptSlug(model))
        : IsApi(model) ? ApiParts(model) is { } p && ApiProviders.Find(p.provider) is { } provider ? ApiProviders.EffortsFor(provider, p.id) : new[] { Auto }
        : ClaudeEfforts;

    /// The same level when the model takes it, else the nearest one below it, else its lowest.
    public static string Fit(string model, string effort)
    {
        // A cold catalog cannot decide that a saved level is unsupported. Resolve again before sending.
        if (IsChatGpt(model) && GptCatalog.Find(GptSlug(model) ?? GptCatalog.DefaultSlug()) is null && Array.IndexOf(Rank, effort) >= 0) return effort;
        var levels = Efforts(model);
        if (Array.IndexOf(levels, effort) >= 0) return effort;
        int want = Array.IndexOf(Rank, effort);
        string? best = null;
        int bestRank = -1;
        foreach (string l in levels)
        {
            int r = Array.IndexOf(Rank, l);
            if (r <= want && r > bestRank)
            {
                best = l;
                bestRank = r;
            }
        }
        return best ?? levels[0];
    }

    /// An API side with no model to use: a send names the problem (AiErrors.ApiModelGone) rather than going to Claude.
    public const string ApiUnset = ApiPrefix;

    /// The API model new conversations use, for the settings page and the chats alike: the one set when it can be
    /// used, else a known-good one (ApiCatalog.DefaultModel: the provider's Preferred model it lists, else its first),
    /// else the one set (its list not read yet, or its key gone: the send says so) unless its provider refused it
    /// (ApiCatalog.IsUnusable), else ApiUnset.
    public static string ApiDefault()
    {
        string? set = Settings.Current.ApiModel;
        if (set is not null && ApiReady(set)) return set;
        if (ApiCatalog.DefaultModel() is { } known) return known;
        return set is not null && !(ApiParts(set) is { } p && ApiCatalog.IsUnusable(p.provider, p.id)) ? set : ApiUnset;
    }

    /// New conversations: the model of the side chosen in the settings.
    public static string DefaultModel()
    {
        var s = Settings.Current;
        if (s.AiProvider == "api") return ApiDefault();
        if (s.AiProvider == "omp") return Omp(s.OmpChatModel);
        if (s.AiProvider != "chatgpt") return Array.IndexOf(Claude, s.AiModel) >= 0 ? s.AiModel : "sonnet";
        string? slug = s.GptModel is { Length: > 0 } g && (GptCatalog.All.Count == 0 || GptCatalog.Find(g) is not null) ? g : GptCatalog.DefaultSlug();
        return slug is null ? "chatgpt" : Gpt(slug);
    }

    /// The settings' level for that side, fitted to the model.
    public static string DefaultEffort(string model) => Fit(model, IsOmp(model) ? OmpModelList.EffortOf(OmpSlug(model)!) ?? Settings.Current.OmpChatEffort : IsChatGpt(model) ? Settings.Current.GptEffort : IsApi(model) ? Settings.Current.ApiEffort : Settings.Current.AiEffort);

    public static string Name(string m) => IsOmp(m) ? OmpModelList.NameOf(OmpSlug(m)!)
        : IsChatGpt(m) ? GptCatalog.Name(GptSlug(m))
        : ApiParts(m) is { } api ? ApiCatalog.Find(api.provider, api.id)?.Name ?? api.id
        : IsApi(m) ? Tr("모델 없음", "No model")
        : m switch { "opus" => "Opus", "haiku" => "Haiku", _ => "Sonnet" };

    /// "Claude · Sonnet" / "ChatGPT · GPT-6-Astra" / "omp · Sonnet 5.5" / "OpenRouter · Kimi K3"
    public static string Who(string m) => (IsOmp(m) ? "omp" : IsChatGpt(m) ? "ChatGPT" : IsApi(m) ? ApiProviderOf(m)?.Name ?? "API" : "Claude") + " · " + Name(m);

    public static string EffortName(string e) => e switch
    {
        Auto => Tr("기본", "Default"),
        "none" or "off" => Tr("끔", "Off"),
        "minimal" => Tr("최소", "Minimal"),
        "low" => Tr("낮음", "Low"),
        "medium" => Tr("중간", "Medium"),
        "high" => Tr("높음", "High"),
        "xhigh" => Tr("더 높음", "Extra high"),
        "max" => Tr("최대", "Max"),
        "ultra" => Tr("울트라", "Ultra"),
        _ => Tr("빠름", "Fast"),
    };

    public static string Next(string[] list, string cur) => list[(Array.IndexOf(list, cur) + 1) % list.Length];

    /// The sides Ctrl+Shift+M turns through, in order: Claude, ChatGPT, and omp when it is on this PC.
    public static List<string> Sides()
    {
        var list = new List<string> { "claude", "chatgpt" };
        if (OmpCli.FindCli() is not null) list.Add("omp");
        return list;
    }

    /// The model of the side after `current`'s (the one last used there); the settings' side moves with it. A side that
    /// isn't turned through (API) goes to ChatGPT.
    public static string NextSide(string current)
    {
        var sides = Sides();
        Settings.Current.AiProvider = sides[(Math.Max(0, sides.IndexOf(Provider(current))) + 1) % sides.Count];
        return DefaultModel();
    }

    /// The name of the side Ctrl+Shift+M goes to from `current`.
    public static string NextSideName(string current)
    {
        var sides = Sides();
        return sides[(Math.Max(0, sides.IndexOf(Provider(current))) + 1) % sides.Count] switch { "omp" => "omp", "chatgpt" => "ChatGPT", _ => "Claude" };
    }
}
