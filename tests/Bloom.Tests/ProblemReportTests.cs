using System;
using System.IO;
using System.Linq;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Tests;

/// 문제 보고: who the user is stays out of the text that goes to GitHub.
internal static class ProblemReportTests
{
    private const string Profile = @"C:\Users\alex";

    public static void MaskHidesProfileNamesAndMail()
    {
        string masked = ProblemReport.Mask(
            @"open C:\Users\alex\AppData\a.json by alex on DESKTOP-ABC; mail Alex.K+x@example.com; esc C:\\Users\\alex\\b; url c:/users/ALEX/c",
            Profile, "alex", "DESKTOP-ABC");
        Check.Eq(masked, @"open %USERPROFILE%\AppData\a.json by <user> on <pc>; mail <email>; esc %USERPROFILE%\\b; url %USERPROFILE%/c", "masked");
    }

    public static void MaskHidesWholeUnusualMail()
    {
        Check.Eq(ProblemReport.Mask("from jane.o'connor@example.org", Profile, "alex", "DESKTOP-ABC"), "from <email>", "an apostrophe in the name");
        Check.Eq(ProblemReport.Mask("to \"jane doe\"@example.org.", Profile, "alex", "DESKTOP-ABC"), "to <email>.", "a quoted name");
        Check.Eq(ProblemReport.Mask("cc a!b#c%d&e*f/g=h?i^j`k{l|m}n~o@x.co", Profile, "alex", "DESKTOP-ABC"), "cc <email>", "the other characters mail allows");
    }

    public static void MaskKeepsOtherWordsAndFolders()
    {
        string text = @"alexwoo wrote C:\Users\alex2\x and pc DESKTOP-ABCD, 2 users@home";
        Check.Eq(ProblemReport.Mask(text, Profile, "alex", "DESKTOP-ABC"), text, "a name inside a longer word or folder name is not the user");
    }

    public static void MaskDoesNotMatchItsOwnPlaceholders()
    {
        // a user named "user" or "email": the placeholders it wrote must not be masked again
        Check.Eq(ProblemReport.Mask("user wrote to a@b.co", @"C:\Users\user", "user", "pc"), "<user> wrote to <email>", "one pass");
        Check.Eq(ProblemReport.Mask(@"email in C:\Users\email\x", @"C:\Users\email", "email", "PC1"), @"<user> in %USERPROFILE%\x", "one pass, profile");
    }

    public static void LogTailIsTheLastLines()
    {
        string file = Path.Combine(Path.GetTempPath(), "bloom-report-test-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Check.Eq(ProblemReport.LogTail(file, 3), "", "no log yet");
            File.WriteAllLines(file, Enumerable.Range(1, 250).Select(i => "line " + i));
            string[] tail = ProblemReport.LogTail(file, ProblemReport.LogLines).Split(Environment.NewLine);
            Check.Eq(tail.Length, ProblemReport.LogLines, "200 lines");
            Check.Eq((tail[0], tail[^1]), ("line 51", "line 250"), "the end of the log");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void ReportHasTheFactsAndNoPersonalPaths()
    {
        using var data = new SettingsTransferTests.DataFolder();
        File.WriteAllText(Paths.In("log.txt"), $"10-14 09:00:00.000 [x] IOException: could not read {Paths.Home}\\Documents\\a.txt for me@example.com{Environment.NewLine}");
        string report = ProblemReport.Build();
        Check.True(report.StartsWith("Bloom " + ReleaseNotes.Current, StringComparison.Ordinal), "the version first");
        foreach (string label in new[] { "Windows: ", "Language: ", "Drawing: ", "Features off: ", "Log (last 200 lines):" })
            Check.True(report.Contains(label, StringComparison.Ordinal), label + " is in the report");
        Check.True(report.Contains(@"could not read %USERPROFILE%\Documents\a.txt for <email>", StringComparison.Ordinal), "the log line, masked");
        Check.True(!report.Contains(Paths.Home, StringComparison.OrdinalIgnoreCase), "no profile path");
    }

    public static void IssuePageIsShortAndFilled()
    {
        string url = ProblemReport.IssueUrl();
        Check.True(url.StartsWith(Updates.Repo + "/issues/new?title=", StringComparison.Ordinal), "the repository's new issue page: " + url);
        Check.True(Uri.UnescapeDataString(url).Contains("Ctrl+V", StringComparison.Ordinal), "the body asks to paste the report");
        Check.True(url.Length < 500, "the report itself is not in the address");
    }
}
