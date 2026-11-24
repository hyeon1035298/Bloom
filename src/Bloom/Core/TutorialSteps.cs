using System.Collections.Generic;
using System.Linq;

namespace Bloom.Core;

/// The tutorial's pages by id: which of them a PC shows and which an existing user has yet to see.
internal static class TutorialSteps
{
    public const string Start = "start", Open = "open", Looks = "looks", Browser = "browser", Home = "home", Ai = "ai",
        Addons = "addons", Notify = "notify", Keys = "keys", Done = "done";

    public static readonly string[] All = [Start, Open, Looks, Browser, Home, Ai, Addons, Notify, Keys, Done];
    /// What the first-run screens covered before the ids were recorded.
    public static readonly string[] Before = [Start, Open, Looks, Browser, Home, Ai, Done];
    /// Pages that can come back on their own after an update.
    public static readonly string[] Late = [Addons, Notify, Keys];

    /// What decides whether a page exists on this PC.
    internal readonly record struct Has(bool Addons, bool Notifier);

    public static bool Applies(string id, Has has) => id switch { Addons => has.Addons, Notify => has.Notifier, _ => true };

    /// The whole tour, first run or "Bloom 둘러보기".
    public static List<string> Full(Has has) => [.. All.Where(id => Applies(id, has))];

    /// The pages an onboarded user has not seen and that apply here. Empty before the first run.
    public static List<string> Pending(bool? onboarded, IReadOnlyCollection<string>? seen, Has has) =>
        onboarded != true ? [] : [.. Late.Where(id => Applies(id, has) && seen?.Contains(id) != true)];

    /// What a start shows: the whole tour on a first run, otherwise only the pending pages.
    public static List<string> AtStart(bool? onboarded, IReadOnlyCollection<string>? seen, Has has) =>
        onboarded != true ? Full(has) : Pending(onboarded, seen, has);

    /// Every known id, plus ids a newer Bloom wrote.
    public static List<string> AllSeen(IReadOnlyCollection<string>? seen) => [.. All, .. (seen ?? []).Where(id => !All.Contains(id))];
}
