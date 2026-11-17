using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;
using Microsoft.Win32;

namespace Bloom.Browser;

/// A browser installed on this PC (from Windows' list of browsers): its key there, its name, its program.
internal sealed record InstalledBrowser(string Key, string Name, string Exe);

/// Handing things to the browser: a search (Enter on "웹에서 검색"), an address typed in the search
/// line, links in answers and the 브라우저 shortcuts. No extension, no reading of the browser's own files — the
/// browser is just started with the address (설정 > 검색: which browser, which search engine).
internal static class Web
{
    public static readonly (string key, string name, string url)[] Engines =
    {
        ("google", "Google", "https://www.google.com/search?q="),
        ("naver", Tr("네이버", "Naver"), "https://search.naver.com/search.naver?query="),
        ("bing", "Bing", "https://www.bing.com/search?q="),
        ("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q="),
    };

    public static string EngineName => Array.Find(Engines, e => e.key == Settings.Current.SearchEngine).name ?? "Google";

    private static List<InstalledBrowser>? installed;

    /// The browsers Windows knows about (HKCU and HKLM StartMenuInternet), read once; "default" is not among them.
    public static IReadOnlyList<InstalledBrowser> Installed => installed ??= Find();

    private static List<InstalledBrowser> Find()
    {
        var list = new List<InstalledBrowser>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var root = hive.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
            if (root is null) continue;
            foreach (string key in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(key);
                string? name = k?.GetValue(null) as string;
                string? cmd = k?.OpenSubKey(@"shell\open\command")?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(cmd) || ExeOf(cmd) is not { } exe || !File.Exists(exe)) continue;
                string id = key.ToLowerInvariant();
                if (list.Exists(b => b.Key == id || string.Equals(b.Exe, exe, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new InstalledBrowser(id, string.IsNullOrWhiteSpace(name) ? key : name, exe));
            }
        }
        // Aside does not always register itself; it is found where it installs
        if (!list.Exists(b => b.Name.Contains("Aside", StringComparison.OrdinalIgnoreCase)))
            foreach (string p in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Aside\Application\Aside.exe"), Path.Combine(Paths.LocalAppData, @"Aside\Application\Aside.exe") })
                if (File.Exists(p))
                {
                    list.Add(new InstalledBrowser("aside", "Aside", p));
                    break;
                }
        return list.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// "\"C:\…\chrome.exe\" --single-argument %1" → C:\…\chrome.exe
    private static string? ExeOf(string command)
    {
        string c = command.Trim();
        if (c.StartsWith('"')) return c.IndexOf('"', 1) is > 1 and var end ? c[1..end] : null;
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : null;
    }

    /// The browser picked in the settings, if it is still installed (null = the Windows default browser).
    public static InstalledBrowser? Picked
    {
        get
        {
            string key = Settings.Current.WebBrowser;
            if (key is "" or "default") return null;
            return Installed.FirstOrDefault(b => b.Key == key) ?? (key == "aside" ? Installed.FirstOrDefault(b => b.Name.Contains("Aside", StringComparison.OrdinalIgnoreCase)) : null);
        }
    }

    public static string BrowserName => Picked?.Name ?? Tr("기본 브라우저", "Default browser");

    /// The choices for the settings and the first-run screens: the Windows default first, then what is installed.
    public static (string value, string label)[] Choices => new[] { ("default", Tr("Windows 기본 브라우저", "Windows' default browser")) }.Concat(Installed.Select(b => (b.Key, b.Name))).ToArray();

    public static void Search(string text)
    {
        string url = (Array.Find(Engines, e => e.key == Settings.Current.SearchEngine).url ?? Engines[0].url) + Uri.EscapeDataString(text.Trim());
        Open(url);
    }

    /// The address Open hands on: an absolute http, https or mailto address, escaped (no spaces, quotes or backslashes);
    /// null for anything else. Other schemes (file:, \\host\share, search-ms:, ms-…:) would start whatever program
    /// registered them, or make Windows sign in to another computer.
    public static string? Address(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https" or "mailto")) return null;
        if (u.Scheme != "mailto" && u.Host.Length == 0) return null;
        return u.AbsoluteUri;
    }

    /// Opens a web address in the picked browser (mailto: in the mail app Windows has for it); anything else is refused
    /// with a notice, whoever asked for it. `defaultBrowser`: Windows' default browser even when one is picked (the
    /// "기본 브라우저로 열기" actions), through the same check.
    public static void Open(string url, bool defaultBrowser = false)
    {
        if (Address(url) is not { } address)
        {
            Log.Write($"web: refused a link ({(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) ? u.Scheme + ":" : "no scheme")})");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Notices.Result(false, Tr("Bloom · 링크", "Bloom · Link"), Tr("링크 열지 않음", "Link not opened"), Tr("http·https 주소만 열 수 있습니다", "Only http and https addresses open")));
            return;
        }
        Win32.AllowSetForegroundWindow(-1);
        // an escaped http(s) address starts with its scheme, never "-", and holds no quote: one argument to the browser
        if (!defaultBrowser && Picked is { } b && !address.StartsWith("mailto:", StringComparison.Ordinal)) Launch.Open(b.Exe, "\"" + address + "\"");
        else Launch.Open(address);
    }

    public static string? HostOf(string url)
    {
        try
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host.ToLowerInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex UrlLike = new(@"^(https?://\S+|(localhost|127\.0\.0\.1)(:\d+)?(/\S*)?|([\p{L}\d-]+\.)+[\p{L}]{2,}(:\d+)?(/\S*)?)$", RegexOptions.IgnoreCase);

    /// "google.com" → https://google.com; null when the text is not an address.
    public static string? AsUrl(string text)
    {
        string t = text.Trim();
        if (t.Contains(' ') || t.Length < 4 || !UrlLike.IsMatch(t)) return null;
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return t;
        // a bare word with a file-like ending ("report.pdf", "setup.exe") is not a site
        string tld = t.Split('/')[0].Split(':')[0];
        tld = tld[(tld.LastIndexOf('.') + 1)..].ToLowerInvariant();
        if (tld is "exe" or "pdf" or "txt" or "png" or "jpg" or "zip" or "lnk" or "dll" or "json" or "md" or "cs" or "js" or "ts" or "py" or "mp4" or "mp3" or "wav" or "docx" or "xlsx" or "pptx" or "psd" or "unity" or "fbx" or "blend") return null;
        return (t.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || t.StartsWith("127.", StringComparison.Ordinal) ? "http://" : "https://") + t;
    }
}
