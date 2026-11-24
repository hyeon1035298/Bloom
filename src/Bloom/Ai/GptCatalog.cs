using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// UI reads use the last snapshot for the selected account. Filesystem checks and parsing stay on a worker.
internal static class GptCatalog
{
    public sealed record Model(string Slug, string Name, string[] Levels, string Description);
    internal static readonly string[] FallbackLevels = { "low", "medium", "high", "xhigh" };
    private readonly record struct Source(string? AccountHome, string DefaultHome);
    private static readonly object gate = new();
    private static readonly Dictionary<Source, BackgroundSnapshot<CatalogReader.Snapshot>> caches = new();

    public static string Home => CodexAuth.DefaultHome();
    public static event Action? Updated;

    // Account/settings access belongs to the caller; worker closures contain paths only.
    private static BackgroundSnapshot<CatalogReader.Snapshot> Cache()
    {
        var source = new Source(AiAccounts.ChatHome("chatgpt"), Home);
        lock (gate)
        {
            if (caches.TryGetValue(source, out var cache)) return cache;
            var reader = new CatalogReader(source.AccountHome, source.DefaultHome);
            cache = new(CatalogReader.Snapshot.Empty, reader.Load, 2000, "model catalog");
            cache.Updated += () => Updated?.Invoke();
            caches.Add(source, cache);
            return cache;
        }
    }

    private static CatalogReader.Snapshot Read() => Demo.On ? CatalogReader.Snapshot.Empty : Cache().Read();
    public static bool Ready => Read().Loaded;
    public static IReadOnlyList<Model> All => Read().Models;

    /// Call on the owning UI thread before resolving a send's model/effort. Waiting never blocks its Dispatcher.
    public static Task<bool> RefreshAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        return Demo.On ? Task.FromResult(true) : RefreshCurrentAsync(Cache, cancellation);
    }

    internal static async Task<bool> RefreshCurrentAsync(Func<BackgroundSnapshot<CatalogReader.Snapshot>> capture,
        CancellationToken cancellation = default)
        => await ResolveCurrentAsync(capture, snapshot => snapshot.Loaded, cancellation).ConfigureAwait(false);

    /// Resolve values (or start an async operation) in the same UI callback that validates the account.
    public static Task<T> ResolveAsync<T>(Func<T> resolve, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        return Demo.On ? Task.FromResult(resolve()) : ResolveCurrentAsync(Cache, _ => resolve(), cancellation);
    }

    internal static async Task<T> ResolveCurrentAsync<T>(Func<BackgroundSnapshot<CatalogReader.Snapshot>> capture,
        Func<CatalogReader.Snapshot, T> resolve, CancellationToken cancellation = default)
    {
        var owner = Dispatcher.CurrentDispatcher;
        var cache = capture();
        while (true)
        {
            var snapshot = await cache.Refresh().WaitAsync(cancellation).ConfigureAwait(false);
            var attempt = await owner.InvokeAsync(() =>
            {
                var current = capture();
                bool ready = ReferenceEquals(current, cache);
                return (Current: current, Ready: ready, Value: ready ? resolve(snapshot) : default!);
            }, DispatcherPriority.Normal, cancellation).Task.ConfigureAwait(false);
            if (attempt.Ready) return attempt.Value;
            cache = attempt.Current; // account changed during the read: resolve from the new source
        }
    }

    public static Model? Find(string? slug) => slug is null ? null : All.FirstOrDefault(model => model.Slug == slug);

    public static string? DefaultSlug()
    {
        var snapshot = Read();
        string? configured = snapshot.ConfigModel;
        if (configured is not null && (snapshot.Models.Count == 0 || snapshot.Models.Any(model => model.Slug == configured))) return configured;
        return snapshot.Models.Count > 0 ? snapshot.Models[0].Slug : configured;
    }

    public static string Name(string? slug) => (slug ?? DefaultSlug()) is { } value ? Find(value)?.Name ?? value : "ChatGPT";
    public static string[] Levels(string? slug) => Find(slug ?? DefaultSlug())?.Levels ?? FallbackLevels;
    public static string? ConfigModel() => Read().ConfigModel;

    /// A short Korean line for the menus (Codex's own words for a kind not known here).
    public static string About(Model m)
    {
        string d = m.Description;
        bool Has(string start) => d.StartsWith(start, StringComparison.OrdinalIgnoreCase);
        if (Has("Latest workhorse")) return Tr("코딩 및 일반 작업용 모델", "Model for coding and general tasks");
        if (Has("Frontier")) return Tr("복잡한 작업용 모델", "Model for complex tasks");
        if (Has("Previous generation workhorse")) return Tr("이전 세대 모델", "Previous-generation model");
        if (Has("Fast and affordable")) return Tr("간단한 작업용 모델", "Model for simple tasks");
        if (Has("Older generation workhorse")) return Tr("이전 모델", "Legacy model");
        if (Has("Older balanced")) return Tr("이전 세대 균형형 모델", "Previous-generation balanced model");
        if (Has("Older fast")) return Tr("이전 세대 빠른 모델", "Previous-generation fast model");
        return d;
    }

}
