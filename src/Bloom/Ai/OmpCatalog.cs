using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The models omp offers (`omp models`), read once and kept in a file so the settings and the model menus have them without
/// waiting four seconds. Empty until it is read (or when omp isn't on this PC).
internal static partial class OmpCatalog
{
    [GeneratedRegex(@"^(\S+) \(\d+\)$")]
    private static partial Regex Header();

    internal sealed record Entry(string Slug, string[] Thinking, bool Images)
    {
        /// "anthropic" of "anthropic/claude-sonnet-5-5"; "commandcode" of "commandcode/google/gemini-3.8-flash".
        public string Provider => OmpModelList.Provider(Slug);
    }

    /// A fresh list arrived (raised on a worker thread, as GptCatalog's).
    public static event Action? Updated;

    private static List<Entry> all = new();
    private static int started;

    public static IReadOnlyList<Entry> All => all;

    public static Entry? Find(string slug) => all.FirstOrDefault(e => e.Slug == slug);

    /// When the saved list was read from omp; null = never.
    public static DateTime? Read
    {
        get
        {
            try
            {
                return File.Exists(CacheFile) ? File.GetLastWriteTime(CacheFile) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private static string CacheFile => Paths.InLocal(@"ai\omp-models.txt");

    /// The table `omp models` prints: a "provider (n)" line, then one row per model: model │ context │ max-out │ thinking │ images.
    public static List<Entry> Parse(string table)
    {
        var list = new List<Entry>();
        string? provider = null;
        foreach (string raw in table.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (!line.Contains('│') && !line.Contains('┌') && !line.Contains('├') && !line.Contains('└'))
            {
                var h = Header().Match(line);
                if (h.Success) provider = h.Groups[1].Value;
                continue;
            }
            if (provider is null || line[0] != '│') continue;
            string[] cells = line.Trim('│').Split('│').Select(c => c.Trim()).ToArray();
            if (cells.Length < 5 || cells[0] == "model" || cells[0].Length == 0) continue;
            string[] thinking = cells[3] == "-" ? Array.Empty<string>() : cells[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            list.Add(new Entry(provider + "/" + cells[0], thinking, cells[4].Equals("yes", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    /// The levels a model takes: "off" and its own; a model the list doesn't know takes the usual ones.
    public static string[] LevelsOf(string slug)
    {
        var e = all.FirstOrDefault(x => x.Slug == slug);
        if (e is null) return OmpCli.Levels;
        return new[] { "off" }.Concat(e.Thinking).ToArray();
    }

    /// Reads the saved list at once and, when it is missing or old, asks `omp models` in the background (Updated follows).
    public static void Ensure()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
        bool fresh = false;
        try
        {
            if (File.Exists(CacheFile))
            {
                var list = Parse(File.ReadAllText(CacheFile));
                if (list.Count > 0) all = list;
                fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromDays(3);
            }
        }
        catch (IOException e)
        {
            Log.Error("omp models cache", e);
        }
        if (!fresh) _ = Fetch(null);
    }

    /// 새로고침: asks `omp models` now, whatever the age of the saved list; `finished(ok)` runs when it answered (false = omp
    /// is missing or gave no table; the old list stays).
    public static Task Refresh(Action<bool>? finished = null)
    {
        Interlocked.Exchange(ref started, 1);
        return Fetch(finished);
    }

    private static Task Fetch(Action<bool>? finished) => Task.Run(async () =>
    {
        bool ok = false;
        try
        {
            if (OmpCli.FindCli() is not { } exe) return;
            var psi = OmpCli.Psi(exe);
            psi.ArgumentList.Add("models");
            var (code, output, _) = await OneShot.RunCli(psi, "", default);
            var list = code == 0 ? Parse(output) : new List<Entry>();
            if (list.Count == 0) return;
            all = list;
            ok = true;
            try
            {
                File.WriteAllText(CacheFile, output);
            }
            catch (IOException e)
            {
                Log.Error("omp models cache", e);
            }
            Updated?.Invoke();
        }
        finally
        {
            finished?.Invoke(ok);
        }
    });
}
