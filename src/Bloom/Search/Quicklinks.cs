using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Bloom.Core;

namespace Bloom.Search;

/// A saved address; `{query}` in it is replaced by what is typed after the keyword ("yt 고양이").
internal sealed class Quicklink
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Keyword { get; set; }

    [JsonIgnore] public MatchTarget Match = null!;

    public bool TakesQuery => Url.Contains("{query}", StringComparison.OrdinalIgnoreCase);

    public string For(string query) => Url.Replace("{query}", Uri.EscapeDataString(query.Trim()), StringComparison.OrdinalIgnoreCase);

    /// The address with the placeholder taken out, for the subtitle.
    public string Plain => Url.Replace("{query}", "", StringComparison.OrdinalIgnoreCase);
}

/// Quicklinks: search addresses and bookmarks reached by name or keyword.
internal sealed class Quicklinks
{
    public List<Quicklink> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Quicklinks Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        saver?.Dispose();
        string file = Paths.In("quicklinks.json");
        Current = File.Exists(file) ? Json.Load<Quicklinks>(file) : new Quicklinks { Items = Defaults() };
        Index();
        saver = Saver.ForJson(Paths.In("quicklinks.json"), () => Current);
        if (!File.Exists(file)) saver.Touch();
    }

    private static List<Quicklink> Defaults() => new()
    {
        New(Tr("구글 검색", "Google search"), "https://www.google.com/search?q={query}", "g"),
        New(Tr("네이버 검색", "Naver search"), "https://search.naver.com/search.naver?query={query}", "n"),
        New(Tr("유튜브 검색", "YouTube search"), "https://www.youtube.com/results?search_query={query}", "yt"),
        New(Tr("나무위키", "Namu Wiki"), "https://namu.wiki/Go?q={query}", "nw"),
        New(Tr("파파고 번역", "Papago translate"), "https://papago.naver.com/?sk=auto&tk=ko&st={query}", "pp"),
        New(Tr("BOOTH 검색", "BOOTH search"), "https://booth.pm/ko/search/{query}", "bt"),
        New(Tr("깃허브 검색", "GitHub search"), "https://github.com/search?q={query}", "gh"),
        New(Tr("구글 지도", "Google Maps"), "https://www.google.com/maps/search/{query}", "map"),
    };

    public static Quicklink New(string name, string url, string? keyword) => new() { Id = Guid.NewGuid().ToString("N")[..12], Name = name, Url = url, Keyword = keyword };

    private static void Index()
    {
        foreach (var q in Current.Items)
        {
            var aliases = new List<string> { "quicklink 퀵링크" };
            if (q.Keyword is { Length: > 0 } k) aliases.Add(k);
            if (HostOf(q.Plain) is { } h) aliases.Add(h);
            q.Match = new MatchTarget(q.Name, aliases);
        }
    }

    private static void Touch()
    {
        Index();
        saver?.Touch();
        Changed?.Invoke();
    }

    public static void Upsert(Quicklink q)
    {
        int at = Current.Items.FindIndex(x => x.Id == q.Id);
        if (at >= 0) Current.Items[at] = q;
        else Current.Items.Add(q);
        Touch();
    }

    public static void Remove(Quicklink q)
    {
        if (Current.Items.RemoveAll(x => x.Id == q.Id) > 0) Touch();
    }

    /// 설정 > 데이터 > 사이트 바로 가기: the user's own go and the built-in ones come back.
    public static void Reset()
    {
        Current.Items = Defaults();
        Touch();
    }

    public static void Flush() => saver?.Flush();

    /// "yt 고양이" → (YouTube, "고양이"); the keyword alone with a space waits for the words.
    public static (Quicklink link, string query)? Typed(string text)
    {
        int sp = text.IndexOf(' ');
        if (sp <= 0) return null;
        string kw = text[..sp];
        var q = Current.Items.FirstOrDefault(x => x.TakesQuery && string.Equals(x.Keyword, kw, StringComparison.OrdinalIgnoreCase));
        return q is null ? null : (q, text[(sp + 1)..]);
    }

    public static string? HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Host.Length == 0) return null;
        return u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
    }

    /// What a typed address should become: a scheme when it has none, and a check that it is one.
    public static string? Normalize(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return null;
        if (!url.Contains("://")) url = "https://" + url;
        string probe = url.Replace("{query}", "x", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(probe, UriKind.Absolute, out var u) && u.Host.Contains('.') ? url : null;
    }

    /// "youtube.com/results?…" → "Youtube"
    public static string SuggestName(string url)
    {
        string? h = HostOf(url.Replace("{query}", "x", StringComparison.OrdinalIgnoreCase));
        if (h is null) return "";
        var parts = h.Split('.');
        string main = parts.Length >= 2 ? parts[^2] : parts[0];
        if (main is "co" or "or" or "go" && parts.Length >= 3) main = parts[^3];
        return main.Length == 0 ? "" : char.ToUpperInvariant(main[0]) + main[1..];
    }

    /// The first free keyword among the name's first letters.
    public static string SuggestKeyword(string name, string? self)
    {
        string latin = new(name.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9').ToArray());
        if (latin.Length == 0) return "";
        foreach (int n in new[] { 2, 3, 4 })
        {
            if (latin.Length < n) break;
            string k = latin[..n];
            if (!Current.Items.Any(x => x.Id != self && string.Equals(x.Keyword, k, StringComparison.OrdinalIgnoreCase))) return k;
        }
        return "";
    }
}
