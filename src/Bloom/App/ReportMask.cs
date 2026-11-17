using System.Linq;
using System.Text.RegularExpressions;

namespace Bloom.App;

/// The second pass over a problem report (after the profile, user, PC and e-mail pass): what the log holds besides them.
/// Paths outside the profile keep their drive or share marker and the file name, URLs lose their query and fragment, key-like
/// tokens and `key=value` secrets go, and a very long line (raw tool output) is cut.
internal static class ReportMask
{
    public const int LineMax = 300;

    private const string Part = @"[^\\/:*?""<>|\r\n]";

    // a drive path (\ or / or the doubled \\ of escaped text) or a share: directories, then the file name without spaces
    private static readonly Regex Paths = new(
        $@"(?<![A-Za-z0-9])(?<drive>[A-Za-z]:)[\\/]+(?<dirs>(?:{Part}+[\\/]+)+)(?<name>[^\s\\/:*?""<>|]*)"
        + $@"|(?<![\w:.\\/])\\\\+(?<unc>[^\s\\/:*?""'<>|]+)[\\/]+(?<udirs>(?:{Part}+[\\/]+)*)(?<uname>[^\s\\/:*?""<>|]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Urls = new(@"(?<url>\b[a-z][a-z0-9+.\-]*://[^\s?#""'<>]*)[?#][^\s""'<>]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Secrets = new(@"(?<key>\b(?:api[_\-]?key|token|secret|password|passwd|authorization|bearer)\b\s*[:=]?\s*)(?<value>[^\s,;""'}]{6,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // sk-… style keys, and 32+ character base64 / hex runs that mix in a digit
    private static readonly Regex Tokens = new(@"(?<![A-Za-z0-9_\-])(?:sk-[A-Za-z0-9_\-]{16,}|(?=[A-Za-z0-9+_\-]*\d)[A-Za-z0-9+_\-]{32,}={0,2})", RegexOptions.CultureInvariant);

    public static string Apply(string text)
    {
        text = Urls.Replace(text, m => m.Groups["url"].Value + "?…");
        text = Paths.Replace(text, m => m.Groups["drive"].Success
            ? m.Groups["drive"].Value + @"\…\" + m.Groups["name"].Value
            : @"\\…\" + m.Groups["uname"].Value);
        text = Secrets.Replace(text, m => m.Groups["key"].Value + "…");
        text = Tokens.Replace(text, "…");
        return string.Join("\n", text.Split('\n').Select(Cut));
    }

    private static string Cut(string line) => line.Length <= LineMax ? line : line[..LineMax].TrimEnd('\r') + "… (cut)";
}
