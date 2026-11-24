using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bloom.Search;

/// One package winget can install.
internal sealed record WingetPackage(string Name, string Id, string Version, string Source);

/// Windows' own package manager (winget, part of App Installer): searching it and installing from it. winget has no
/// machine-readable search output, so its table is read by column; the columns are measured in screen cells, where a
/// Hangul or CJK letter takes two. Nothing is installed without the user's yes in the launcher.
internal static class Winget
{
    private const int NoMatch = -1978335212; // APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND
    private const int AlreadyInstalled = -1978335189; // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE: nothing newer

    /// winget.exe: the App Installer alias in the user's WindowsApps folder (null when App Installer isn't there).
    public static string? Exe
    {
        get
        {
            string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            return File.Exists(alias) ? alias : null;
        }
    }

    /// The packages matching `query` (at most `max`), or null when winget isn't there or failed.
    public static async Task<List<WingetPackage>?> Search(string query, int max, CancellationToken stop)
    {
        if (Exe is not { } exe) return null;
        var (code, output) = await Run(exe, new[] { "search", query, "--count", max.ToString(System.Globalization.CultureInfo.InvariantCulture), "--accept-source-agreements", "--disable-interactivity" }, stop);
        if (code == NoMatch) return new();
        return code == 0 ? Table(output) : null;
    }

    /// Installs one package quietly in the background; (ok, what winget said last). Installers that ask for
    /// administrator rights still bring up Windows' own prompt.
    public static async Task<(bool ok, string? said)> Install(WingetPackage p)
    {
        if (Exe is not { } exe) return (false, Tr("winget이 없습니다", "winget is not installed"));
        var (code, output) = await Run(exe, InstallArgs(p), CancellationToken.None);
        if (code == AlreadyInstalled) return (true, Tr("최신 버전 설치됨", "Latest version installed"));
        return (code == 0, UserCommands.LastLine(output) ?? (code == 0 ? null : Tr($"코드 {code}", $"Code {code}")));
    }

    public static string[] InstallArgs(WingetPackage p) => new[] { "install", "--id", p.Id, "--exact", "--source", p.Source.Length > 0 ? p.Source : "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };

    /// The same install, typed in a terminal the user can watch (and copy). The Id and Source come from winget's table
    /// text, so anything beyond letters, digits and . _ + - is a PowerShell '…' string (never code).
    public static string CommandLine(WingetPackage p)
    {
        string Value(string v) => PlainArg.IsMatch(v) && v[0] != '-' ? v : UserCommands.PsQuote(v);
        var args = InstallArgs(p);
        for (int i = 1; i < args.Length; i++)
            if (args[i - 1] is "--id" or "--source") args[i] = Value(args[i]); // only these two come from outside
        return "winget " + string.Join(" ", args);
    }

    private static readonly System.Text.RegularExpressions.Regex PlainArg = new(@"^[A-Za-z0-9._+\-]+$");

    private static async Task<(int code, string output)> Run(string exe, string[] args, CancellationToken stop)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Bloom.Ai.ChildProcess.Spawn(psi);
        if (p is null) return (-1, "");
        var read = p.StandardOutput.ReadToEndAsync(stop);
        _ = p.StandardError.ReadToEndAsync(stop); // drained so a full pipe never stalls winget
        try
        {
            await p.WaitForExitAsync(stop);
            return (p.ExitCode, await read);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(true); // a search the user typed past
            }
            catch (InvalidOperationException)
            {
                // it ended by itself
            }
            throw;
        }
    }

    /// winget's table: a header, a line of dashes, then one package a line. The header's words mark where each column
    /// starts; the last column is the source when there are four or more (Name Id Version [Match] Source).
    public static List<WingetPackage> Table(string output)
    {
        var lines = new List<string>();
        foreach (string raw in output.Split('\n'))
        {
            string l = raw.TrimEnd('\r');
            int cr = l.LastIndexOf('\r'); // progress spinners redraw the line with \r
            lines.Add(cr >= 0 ? l[(cr + 1)..] : l);
        }
        int dash = lines.FindIndex(l => l.Length > 10 && l.Trim().Trim('-').Length == 0);
        var list = new List<WingetPackage>();
        if (dash < 1) return list;
        var starts = Starts(lines[dash - 1]);
        if (starts.Count < 3) return list;
        for (int i = dash + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0) break;
            var cells = Cut(lines[i], starts);
            string id = cells[1].Trim();
            if (id.Length == 0 || id.Contains(' ')) continue;
            list.Add(new WingetPackage(cells[0].Trim(), id, cells[2].Trim(), starts.Count >= 4 ? cells[^1].Trim() : ""));
        }
        return list;
    }

    /// Where each header word starts, in screen cells.
    private static List<int> Starts(string header)
    {
        var starts = new List<int>();
        int col = 0;
        bool space = true;
        foreach (var rune in header.EnumerateRunes())
        {
            bool isSpace = Rune.IsWhiteSpace(rune);
            if (!isSpace && space) starts.Add(col);
            space = isSpace;
            col += Cells(rune);
        }
        return starts;
    }

    private static string[] Cut(string line, List<int> starts)
    {
        var cells = new StringBuilder[starts.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = new StringBuilder();
        int col = 0, at = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            while (at + 1 < starts.Count && col >= starts[at + 1]) at++;
            cells[at].Append(rune.ToString());
            col += Cells(rune);
        }
        return Array.ConvertAll(cells, c => c.ToString());
    }

    /// Two cells for wide letters (Hangul, CJK, full-width forms, most emoji), one for the rest.
    public static int Cells(Rune r)
    {
        int c = r.Value;
        return c is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD
            ? 2 : 1;
    }
}
