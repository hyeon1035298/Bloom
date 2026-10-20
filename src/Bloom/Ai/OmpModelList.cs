using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// One model of the user's own list (설정 › AI › omp › 내 모델): omp's "provider/model", a name to show instead of the
/// made-up one, and the thinking level a chat starts at with it (null = the settings' level).
internal sealed class OmpModel
{
    public string Slug { get; set; } = "";
    public string? Name { get; set; }
    public string? Effort { get; set; }
}

/// The list the chat's model menu, Ctrl+M and the settings' model pickers use. Edited by hand in the settings; a model that
/// has left `omp models` stays in it, marked, never removed on its own.
internal static class OmpModelList
{
    public static List<OmpModel> Items => Settings.Current.OmpModels;

    /// The models the menus offer besides omp's own default, as "provider/model": the user's own list (empty until they
    /// make one). Nothing when omp isn't on this PC.
    public static List<string> Slugs() => Slugs(OmpCli.FindCli() is not null);

    /// `found`: omp is on this PC.
    internal static List<string> Slugs(bool found) => found ? Items.Select(m => m.Slug).ToList() : new();

    /// The model ids for the pickers: omp's own default model, then those of Slugs(); none when omp isn't on this PC.
    public static List<string> Ids() => Ids(OmpCli.FindCli() is not null);

    internal static List<string> Ids(bool found) => found ? Slugs(true).Prepend("").Select(AiModels.Omp).ToList() : new();

    public static OmpModel? Find(string slug) => Items.FirstOrDefault(m => m.Slug == slug);

    public static bool Contains(string slug) => Find(slug) is not null;

    public static string Provider(string slug) => slug.Contains('/') ? slug[..slug.IndexOf('/')] : "";

    /// The name shown: the user's, else made from the id (with the provider added when two listed models would read alike).
    public static string NameOf(string slug)
    {
        if (Find(slug)?.Name is { Length: > 0 } own) return own;
        string name = OmpCli.ShortName(slug);
        if (Items.Any(m => m.Slug != slug && m.Name is not { Length: > 0 } && OmpCli.ShortName(m.Slug) == name) && Provider(slug).Length > 0) name += " (" + Provider(slug) + ")";
        return name;
    }

    /// The level a new chat with this model starts at: the one set for it (fitted to the model by the caller), else null.
    public static string? EffortOf(string slug) => Find(slug)?.Effort is { Length: > 0 } e ? e : null;

    /// The model is on the list but `omp models` (read) no longer has it.
    public static bool Gone(string slug) => OmpCatalog.All.Count > 0 && OmpCatalog.Find(slug) is null;

    public static bool Add(string slug, string? name = null, string? effort = null)
    {
        slug = slug.Trim();
        if (slug.Length == 0 || !slug.Contains('/') || Contains(slug)) return false;
        Items.Add(new OmpModel { Slug = slug, Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(), Effort = effort });
        Settings.Save();
        return true;
    }

    public static void Remove(string slug)
    {
        Items.RemoveAll(m => m.Slug == slug);
        Settings.Save();
    }

    /// One place up (-1) or down (+1).
    public static void Move(string slug, int d)
    {
        int i = Items.FindIndex(m => m.Slug == slug), to = i + d;
        if (i < 0 || to < 0 || to >= Items.Count) return;
        (Items[i], Items[to]) = (Items[to], Items[i]);
        Settings.Save();
    }

    public static void Rename(string slug, string name)
    {
        if (Find(slug) is { } m) m.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Settings.Save();
    }

    public static void SetEffort(string slug, string? effort)
    {
        if (Find(slug) is { } m) m.Effort = string.IsNullOrEmpty(effort) ? null : effort;
        Settings.Save();
    }
}
