using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;

namespace Bloom.App;

/// What a start opens and in which order. Each window waits for the one before it to close; the launcher waits for nothing.
internal static class StartQueue
{
    internal enum Item { Launcher, Tutorial, WhatsNew, Settings }

    /// The pages a start shows (none: no tutorial), and what was found on this PC for them. `handed` = a document start,
    /// `tour` = --tour. `detect` reads folders and the registry, so it runs only when a page may be due: an onboarded user
    /// who has seen the late pages (every start after the first on a version) skips it.
    public static (List<string> Run, TutorialEnv? Env) Tutorial(bool handed, bool tour, bool? onboarded, IReadOnlyCollection<string>? seen, Func<TutorialEnv> detect)
    {
        if (handed || !(tour || onboarded != true || TutorialSteps.Late.Any(id => seen?.Contains(id) != true))) return ([], null);
        // the window gets this same env, so the run and the pages cannot disagree
        var env = detect();
        return (tour ? TutorialSteps.Full(env.Has) : TutorialSteps.AtStart(onboarded, seen, env.Has), env);
    }

    /// handed = started by Explorer to open a document; show = --show.
    public static List<Item> Plan(bool handed, bool tutorial, bool updated, bool settings, bool show)
    {
        // a document start opens only the document: the tutorial waits for the next start
        if (handed) return updated ? [Item.WhatsNew] : [];
        var plan = new List<Item>();
        if (tutorial)
        {
            // one window after another: the tutorial, then what is new, then settings
            plan.Add(Item.Tutorial);
            if (updated) plan.Add(Item.WhatsNew);
            if (settings) plan.Add(Item.Settings);
            return plan;
        }
        // without a tutorial a start stays as it was: settings (or the launcher) at once, what is new a moment later
        if (settings) plan.Add(Item.Settings);
        else if (show) plan.Add(Item.Launcher);
        if (updated) plan.Add(Item.WhatsNew);
        return plan;
    }
}
