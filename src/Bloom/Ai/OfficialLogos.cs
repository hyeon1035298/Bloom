using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Xml.Linq;
using Bloom.Core;

namespace Bloom.Ai;

/// The companies' own logos for accounts with none picked (UsageIcons.Shown). Bloom ships none: it fetches each from
/// its company's site, as a browser fetches a site's icon, into Local\usage-icons\official-<kind>.<ext>, once: at start
/// while the file is missing. A failure leaves the letter tile and a log line, and the next start tries again.
internal static class OfficialLogos
{
    private const int MaxBytes = 256 * 1024;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    private static readonly string[] Kinds = { "claude", "chatgpt" };
    private static int started;

    /// New logos are in (Host: the home and the usage file again). On the UI thread.
    public static event Action? Updated;

    private static string Dir => Path.Combine(Paths.Local, "usage-icons");

    private static string FileOf(string kind) => Path.Combine(Dir, $"official-{kind}{(kind == "claude" ? ".ico" : ".svg")}");

    /// The logo fetched for this kind of account; null while there is none.
    public static string? For(string kind) => Kinds.Contains(kind) && File.Exists(FileOf(kind)) ? FileOf(kind) : null;

    /// Host, once: fetches what is missing, off the UI thread.
    public static void Start(Dispatcher ui)
    {
        if (Interlocked.Exchange(ref started, 1) == 1 || !AiUsage.Available) return;
        var due = Kinds.Where(k => !File.Exists(FileOf(k))).ToList();
        if (due.Count == 0) return;
        _ = Task.Run(async () =>
        {
            bool any = false;
            foreach (var kind in due)
            {
                try
                {
                    Save(kind, kind == "claude" ? await Claude().ConfigureAwait(false) : await ChatGpt().ConfigureAwait(false));
                    any = true;
                }
                catch (Exception e)
                {
                    Log.Error($"official logo {kind}", e);
                }
            }
            if (any) _ = ui.BeginInvoke(() => Updated?.Invoke());
        });
    }

    private static async Task<byte[]> Claude()
    {
        byte[] ico = await Get(new Uri("https://claude.ai/favicon.ico")).ConfigureAwait(false);
        if (ico.Length < 6 || ico[0] != 0 || ico[1] != 0 || ico[2] != 1 || ico[3] != 0) throw new InvalidDataException("claude.ai/favicon.ico is not an icon");
        return ico;
    }

    /// The icon chatgpt.com's page names (its file name changes with each build), from the page's first bytes.
    private static async Task<byte[]> ChatGpt()
    {
        var home = new Uri("https://chatgpt.com/");
        string page = Encoding.UTF8.GetString(await Get(home, partial: true).ConfigureAwait(false));
        var url = new Uri(home, IconHref(page) ?? throw new InvalidDataException("chatgpt.com names no SVG icon"));
        if (url.Scheme != Uri.UriSchemeHttps || url.Host != home.Host) throw new InvalidDataException($"{url}: not on chatgpt.com");
        return Encoding.UTF8.GetBytes(Normalized(Encoding.UTF8.GetString(await Get(url).ConfigureAwait(false))));
    }

    /// The href of the page's first `<link rel="icon">` that is an SVG; null when there is none.
    internal static string? IconHref(string page)
    {
        foreach (Match link in Regex.Matches(page, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attrs = Regex.Matches(link.Value, @"([\w-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')")
                .GroupBy(m => m.Groups[1].Value.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Success ? g.First().Groups[2].Value : g.First().Groups[3].Value));
            bool icon = attrs.TryGetValue("rel", out var rel) && rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("icon", StringComparer.OrdinalIgnoreCase);
            if (icon && attrs.TryGetValue("href", out var href) && href.Length > 0
                && (attrs.GetValueOrDefault("type") == "image/svg+xml" || href.Split('?')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
                return href;
        }
        return null;
    }

    /// The SVG with the colour its `<style>` gives the whole picture (`:root { fill }`, the light one, the first) written
    /// on the root and the styles gone: Bloom's reader and the taskbar's read attributes only. Shapes and colours stay.
    internal static string Normalized(string svg)
    {
        var doc = XDocument.Parse(svg);
        if (doc.Root is not { Name.LocalName: "svg" } root) throw new InvalidDataException("not an SVG");
        var styles = root.Descendants().Where(e => e.Name.LocalName == "style").ToList();
        string? fill = styles.Select(s => Regex.Match(s.Value, @":root\s*\{[^}]*?\bfill\s*:\s*([^;}\s]+)")).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        foreach (var s in styles) s.Remove();
        if (fill is not null) root.SetAttributeValue("fill", fill);
        if (!root.Descendants().Any(e => e.Name.LocalName == "path" && e.Attribute("d") is not null)) throw new InvalidDataException("the SVG has no shapes");
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static void Save(string kind, byte[] bytes)
    {
        Directory.CreateDirectory(Dir);
        string file = FileOf(kind), part = file + ".part";
        File.WriteAllBytes(part, bytes);
        File.Move(part, file, overwrite: true);
    }

    /// A GET's body, redirects followed by hand (https only, five at most): all of it up to MaxBytes, or with `partial`
    /// its first MaxBytes (a page of which only the head is read).
    private static async Task<byte[]> Get(Uri url, bool partial = false)
    {
        using var limit = new CancellationTokenSource(Limit);
        for (int hops = 0; ; hops++)
        {
            if (url.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException($"{url}: not https");
            using var response = await ApiClient.Shared.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next && hops < 5)
            {
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"{url.Host}: HTTP {(int)response.StatusCode}");
            await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            var buffer = new byte[MaxBytes + 1];
            int read = 0, n;
            while (read < buffer.Length && (n = await body.ReadAsync(buffer.AsMemory(read), limit.Token).ConfigureAwait(false)) > 0) read += n;
            if (read > MaxBytes && !partial) throw new InvalidDataException($"{url.Host}: more than {MaxBytes} bytes");
            return buffer[..Math.Min(read, MaxBytes)];
        }
    }
}
