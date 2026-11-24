using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.App;

/// 설정 > 정보 > 문제 보고: the facts a bug report needs (version, Windows, language, drawing mode, features off, the end
/// of the log) as text to paste into a GitHub issue. Who the user is stays out of it: the profile folder, the user name,
/// the PC's name and e-mail addresses are replaced before the text leaves this class, and ReportMask shortens the other
/// paths and URLs and drops key-like tokens. Bloom sends nothing itself: the text
/// goes to the clipboard and the issue page opens.
internal static class ProblemReport
{
    /// The log lines a report ends with.
    public const int LogLines = 200;

    private const string ProfilePlaceholder = "%USERPROFILE%", UserPlaceholder = "<user>", PcPlaceholder = "<pc>", EmailPlaceholder = "<email>";

    /// A letter, digit or underscore: a name inside a longer word is not the name.
    private const string WordChar = @"[\p{L}\p{N}_]";

    /// The report, masked. Its labels are English: the one reading it is the developer.
    public static string Build()
    {
        var s = Settings.Current;
        var text = new StringBuilder();
        text.AppendLine($"Bloom {ReleaseNotes.Current}");
        text.AppendLine($"Windows: {RuntimeInformation.OSDescription}");
        text.AppendLine($"Language: {Lang.Code}");
        text.AppendLine($"Drawing: {(s.GpuDrawing ? "GPU" : "CPU")}");
        text.AppendLine($"Features off: {(s.FeaturesOff.Count == 0 ? "none" : string.Join(", ", s.FeaturesOff))}");
        text.AppendLine();
        text.AppendLine($"Log (last {LogLines} lines):");
        text.AppendLine("```");
        text.AppendLine(LogTail(Paths.In("log.txt"), LogLines));
        text.AppendLine("```");
        return ReportMask.Apply(Mask(text.ToString(), Paths.Home, Environment.UserName, Environment.MachineName));
    }

    /// The last `lines` lines of the log ("" when there is none). Read in one pass while Log may be appending.
    internal static string LogTail(string file, int lines)
    {
        if (!File.Exists(file)) return "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var tail = new Queue<string>(lines);
        while (reader.ReadLine() is { } line)
        {
            if (tail.Count == lines) tail.Dequeue();
            tail.Enqueue(line);
        }
        return string.Join(Environment.NewLine, tail);
    }

    /// E-mail addresses, the profile folder (as written with \, \\ or /), the user name and the PC's name replaced by
    /// placeholders. One pass over the text, so a placeholder is never matched again (a user named "user").
    internal static string Mask(string text, string profile, string user, string machine)
    {
        // the local part as mail allows it: the plain characters (an apostrophe or a slash too) or a quoted name
        var parts = new List<string> { @"(?<email>(?:""[^""\r\n]+""|[\w.!#$%&'*+/=?^`{|}~\-]+)@[\w\-]+(?:\.[\w\-]+)+)" };
        if (profile.Length > 0)
        {
            string folder = profile.TrimEnd('\\', '/');
            string forms = string.Join("|", new[] { folder.Replace("\\", "\\\\", StringComparison.Ordinal), folder, folder.Replace('\\', '/') }.Distinct().Select(Regex.Escape));
            parts.Add($"(?<profile>(?:{forms})(?!{WordChar}))");
        }
        if (user.Length > 0) parts.Add($"(?<!{WordChar})(?<user>{Regex.Escape(user)})(?!{WordChar})");
        if (machine.Length > 0) parts.Add($"(?<!{WordChar})(?<pc>{Regex.Escape(machine)})(?!{WordChar})");
        return Regex.Replace(text, string.Join("|", parts), m =>
            m.Groups["email"].Success ? EmailPlaceholder
            : m.Groups["profile"].Success ? ProfilePlaceholder
            : m.Groups["user"].Success ? UserPlaceholder
            : PcPlaceholder, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// The page for a new issue, title and a short body filled in. The report itself goes through the clipboard: it is too
    /// long for an address, and what is in the address is sent to GitHub as soon as the page opens.
    public static string IssueUrl()
    {
        string title = Tr($"문제 보고 · Bloom {ReleaseNotes.Current}", $"Bug Report · Bloom {ReleaseNotes.Current}");
        string body = Tr("무슨 일이 있었는지 적고, 복사한 진단 정보를 아래에 붙여넣으세요 (Ctrl+V).", "Describe what happened and paste the copied diagnostic info below (Ctrl+V).") + "\n\n";
        return $"{Updates.Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }
}
