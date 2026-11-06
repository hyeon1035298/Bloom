using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bloom.Core;

namespace Bloom.Search;

/// One of 내 명령: a script (a file, or a PowerShell line) run from the launcher, a snippet pasted into the window in
/// front, or a chain of launcher lines run one after another. `{input}` takes what is typed after the keyword.
internal sealed class UserCommand
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "script"; // script | snippet | chain | ai
    public string Name { get; set; } = "";
    public string? Keyword { get; set; }
    public string Body { get; set; } = ""; // script: a file or a command line · snippet: the text · chain: steps split by ';' · ai: the prompt, steps split by ">>"
    public bool ShowWindow { get; set; } // script: in a console window (else hidden; the result comes as a notice)
    public string Input { get; set; } = AiInput.Selection; // ai: what the prompt works on (AiInput)
    public string? Every { get; set; } // ai: how often it runs by itself (AiSchedule: "30m", "2h", "3d", "daily 09:00"); null = only when run
    public long LastRun { get; set; } // ai, scheduled: when it last ran on its own, or when the schedule was set (unix ms)
    public string? Thread { get; set; } // ai, scheduled: the AI chat its results go into

    [JsonIgnore] public MatchTarget Match = null!;

    /// Keyword + space + input in the search line: a script, snippet or chain with {input}. An AI command takes its input
    /// from the selection or the clipboard instead.
    public bool TakesInput => Kind != "ai" && Body.Contains("{input}", StringComparison.OrdinalIgnoreCase);

    public bool IsAi => Kind == "ai";
}

/// 내 명령 (usercommands.json): made in the launcher (내 명령 view, or 내 명령으로 등록 on a script file), found by
/// name or keyword like a quicklink ("bk D:\사진" runs 백업 with D:\사진 for {input}).
internal sealed partial class UserCommands
{
    public List<UserCommand> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static UserCommands Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<UserCommands>(Paths.In("usercommands.json"));
        Index();
        saver = Saver.ForJson(Paths.In("usercommands.json"), () => Current);
    }

    public static UserCommand New(string kind, string name, string body, string? keyword) => new() { Id = Guid.NewGuid().ToString("N")[..12], Kind = kind, Name = name, Body = body, Keyword = keyword };

    private static void Index()
    {
        foreach (var c in Current.Items)
        {
            var aliases = new List<string> { c.Kind switch { "snippet" => "snippet 스니펫 상용구", "chain" => "chain macro 묶음 매크로", "ai" => "ai prompt 프롬프트 AI 명령", _ => "script 스크립트 명령" } };
            if (c.Keyword is { Length: > 0 } k) aliases.Add(k);
            c.Match = new MatchTarget(c.Name, aliases);
        }
    }

    private static void Touch()
    {
        Index();
        saver?.Touch();
        Changed?.Invoke();
    }

    public static void Upsert(UserCommand c)
    {
        int at = Current.Items.FindIndex(x => x.Id == c.Id);
        if (at >= 0) Current.Items[at] = c;
        else Current.Items.Add(c);
        Touch();
    }

    public static void Remove(UserCommand c)
    {
        if (Current.Items.RemoveAll(x => x.Id == c.Id) > 0) Touch();
    }

    /// 설정 > 데이터 > 사용자 명령: every command goes, and so do the item shortcuts given to them (their copies would run
    /// a command that is gone); the caller registers the shortcuts again. → how many commands went.
    public static int Clear()
    {
        var ids = Current.Items.Select(c => "my:" + c.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return 0;
        Current.Items.Clear();
        Touch();
        if (Settings.Current.ItemKeys.RemoveAll(k => ids.Contains(k.Item.Id)) > 0) Settings.Save();
        return ids.Count;
    }

    public static void Flush() => saver?.Flush();

    /// "bk D:\사진" → (백업, "D:\사진"); the keyword alone with a space waits for the words.
    public static (UserCommand command, string input)? Typed(string text)
    {
        int sp = text.IndexOf(' ');
        if (sp <= 0) return null;
        string kw = text[..sp];
        var c = Current.Items.FirstOrDefault(x => x.TakesInput && string.Equals(x.Keyword, kw, StringComparison.OrdinalIgnoreCase));
        return c is null ? null : (c, text[(sp + 1)..]);
    }

    /// Is the keyword free (among 내 명령 and the browser shortcuts)?
    public static bool KeywordFree(string keyword, string? self) =>
        !Current.Items.Any(x => x.Id != self && string.Equals(x.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
        && !Quicklinks.Current.Items.Any(x => x.Id != self && string.Equals(x.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

    // ── what a command does ─────────────────────────────────────────────────────────────────────
    /// {input}, {date} (2026-10-08), {time} (14:05), {clipboard}; a snippet's "\n" is a new line.
    public static string Expand(string text, string input, Func<string>? clipboard = null, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        string Put(string s, string key, Func<string> value) => s.Contains(key, StringComparison.OrdinalIgnoreCase) ? s.Replace(key, value(), StringComparison.OrdinalIgnoreCase) : s;
        text = Put(text, "{input}", () => input.Trim());
        text = Put(text, "{date}", () => t.ToString("yyyy-MM-dd"));
        text = Put(text, "{time}", () => t.ToString("HH:mm"));
        if (clipboard is not null) text = Put(text, "{clipboard}", clipboard);
        return text;
    }

    public static string SnippetText(string body) => body.Replace("\\n", "\n");

    /// A script line with {input} and {clipboard} put in as DATA, whatever the template wraps around them: the value can
    /// never end a quote, start a subexpression or a new command. For PowerShell (a .ps1, or a command line) the
    /// scanner follows the template's own quoting ('…', "…", here-strings, comments, $( )) and writes the value the way
    /// that spot needs: a bare spot gets a quoted string, a '…' spot gets every ' ‘ ’ ‚ ‛ doubled, a "…" spot gets
    /// ` $ " “ ” „ escaped with a backtick; line breaks never leave a comment or a here-string. For a .bat, .cmd, .exe
    /// or .com line the value goes inside "…" with no quote, line break or (for cmd) % in it.
    public static string ScriptLine(string body, string input, Func<string>? clipboard = null, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var map = new Dictionary<string, (string text, bool literal)>(StringComparer.OrdinalIgnoreCase)
        {
            ["{input}"] = (input.Trim(), false),
            ["{date}"] = (t.ToString("yyyy-MM-dd"), true),
            ["{time}"] = (t.ToString("HH:mm"), true),
        };
        if (clipboard is not null && body.Contains("{clipboard}", StringComparison.OrdinalIgnoreCase)) map["{clipboard}"] = (clipboard(), false);
        string ext = Path.GetExtension(SplitFile(Expand(body, "x", null, t).Trim()).file ?? "").ToLowerInvariant();
        return ext is ".bat" or ".cmd" or ".exe" or ".com" ? FillProgram(body, map, ext is ".bat" or ".cmd") : FillPowerShell(body, map);
    }

    /// PowerShell's own quote characters: ' ‘ ’ ‚ ‛ and " “ ” „ (the tokenizer takes each of them as a quote).
    private static bool IsSingleQuote(char c) => c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B';

    private static bool IsDoubleQuote(char c) => c is '"' or '\u201C' or '\u201D' or '\u201E';

    private static bool IsLineBreak(char c) => c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    /// The text as one PowerShell single-quoted string: '…' with every single-quote character doubled.
    public static string PsQuote(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 2).Append('\'');
        foreach (char c in s)
        {
            sb.Append(c);
            if (IsSingleQuote(c)) sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }

    private enum Ps { Bare, Single, Double, HereSingle, HereDouble, LineComment, BlockComment }

    private static string NoBreaks(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) sb.Append(IsLineBreak(c) ? ' ' : c);
        return sb.ToString();
    }

    private static string EscapeDouble(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c is '`' or '$' || IsDoubleQuote(c)) sb.Append('`');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// The value as it must be written at a spot of PowerShell text in state `st`.
    /// Inside the $( ) of a "…" string PowerShell drops a backtick that stands right before a double-quote character, even
    /// in a '…' there; one more backtick in front of each such quote keeps the value as it was.
    private static string KeepBackticks(string s)
    {
        if (!s.Contains('`')) return s;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (IsDoubleQuote(s[i]) && i > 0 && s[i - 1] == '`') sb.Append('`');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string PsValue(string text, Ps st, char before, char after, bool nested)
    {
        if (text.Length == 0) return "";
        switch (st)
        {
            case Ps.Single:
                return nested ? KeepBackticks(PsQuote(text)[1..^1]) : PsQuote(text)[1..^1];
            case Ps.Double:
                return EscapeDouble(text);
            case Ps.HereSingle:
                return NoBreaks(text);
            case Ps.HereDouble:
                return EscapeDouble(NoBreaks(text));
            case Ps.LineComment:
                return NoBreaks(text);
            case Ps.BlockComment:
                return text.Replace(">", " ");
            default:
                string q = nested ? KeepBackticks(PsQuote(text)) : PsQuote(text);
                bool alone = (before == '\0' || char.IsWhiteSpace(before) || before is '(' or '=' or ',' or ';' or '|')
                    && (after == '\0' || char.IsWhiteSpace(after) || after is ')' or ',' or ';' or '|');
                // inside a word ("a{input}b") PowerShell's argument mode reads a $( ) with its own quirks (it eats the
                // backtick of `"), so the value goes in as base64, which has no character it could misread
                return alone ? q : "$([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) + "')))";
        }
    }

    private static string? PlaceholderAt(string body, int i, IReadOnlyDictionary<string, (string text, bool literal)> map)
    {
        foreach (string key in map.Keys)
            if (string.Compare(body, i, key, 0, key.Length, StringComparison.OrdinalIgnoreCase) == 0 && i + key.Length <= body.Length) return key;
        return null;
    }

    private static string FillPowerShell(string body, IReadOnlyDictionary<string, (string text, bool literal)> map)
    {
        var sb = new System.Text.StringBuilder(body.Length + 32);
        var outer = new Stack<(Ps state, int depth)>();
        Ps st = Ps.Bare;
        int depth = 0;
        for (int i = 0; i < body.Length;)
        {
            char c = body[i], n = i + 1 < body.Length ? body[i + 1] : '\0';
            if (c == '{' && PlaceholderAt(body, i, map) is { } key)
            {
                var (text, literal) = map[key];
                int end = i + key.Length;
                sb.Append(literal ? text : PsValue(text, st, i > 0 ? body[i - 1] : '\0', end < body.Length ? body[end] : '\0', outer.Count > 0));
                i = end;
                continue;
            }
            int take = 1; // characters copied as they are
            switch (st)
            {
                case Ps.Bare:
                    if (IsSingleQuote(c)) st = Ps.Single;
                    else if (IsDoubleQuote(c)) st = Ps.Double;
                    else if (c == '`' && n != '\0') take = 2;
                    else if (c == '#' && (i == 0 || char.IsWhiteSpace(body[i - 1]) || body[i - 1] is ';' or '(' or '|' or '&' or ',' or '=')) st = Ps.LineComment;
                    else if (c == '<' && n == '#') (st, take) = (Ps.BlockComment, 2);
                    else if (c == '@' && (IsSingleQuote(n) || IsDoubleQuote(n)) && (i + 2 >= body.Length || IsLineBreak(body[i + 2])))
                        (st, take) = (IsSingleQuote(n) ? Ps.HereSingle : Ps.HereDouble, 2);
                    else if (c == '(') depth++;
                    else if (c == ')' && depth > 0 && --depth == 0 && outer.Count > 0) (st, depth) = outer.Pop();
                    break;
                case Ps.Single:
                    if (IsSingleQuote(c)) { if (IsSingleQuote(n)) take = 2; else st = Ps.Bare; }
                    break;
                case Ps.Double:
                    if (c == '`' && n != '\0') take = 2;
                    else if (IsDoubleQuote(c)) { if (IsDoubleQuote(n)) take = 2; else st = Ps.Bare; }
                    else if (c == '$' && n == '(')
                    {
                        outer.Push((Ps.Double, depth));
                        (st, depth, take) = (Ps.Bare, 1, 2);
                    }
                    break;
                case Ps.HereSingle:
                    if (IsLineBreak(c) && i + 2 < body.Length && IsSingleQuote(n) && body[i + 2] == '@') (st, take) = (Ps.Bare, 3);
                    break;
                case Ps.HereDouble:
                    if (c == '`' && n != '\0') take = 2;
                    else if (IsLineBreak(c) && i + 2 < body.Length && IsDoubleQuote(n) && body[i + 2] == '@') (st, take) = (Ps.Bare, 3);
                    break;
                case Ps.LineComment:
                    if (IsLineBreak(c)) st = Ps.Bare;
                    break;
                default:
                    if (c == '#' && n == '>') (st, take) = (Ps.Bare, 2);
                    break;
            }
            take = Math.Min(take, body.Length - i);
            sb.Append(body, i, take);
            i += take;
        }
        return sb.ToString();
    }

    /// For a program or a .bat/.cmd: the value always inside "…" (the template's own quotes are followed), with no ",
    /// line break or, for cmd, % (which cmd expands on its command line, even inside quotes) in it, and a trailing
    /// backslash doubled so a program's argument parser can't read it as an escape of the closing quote.
    private static string FillProgram(string body, IReadOnlyDictionary<string, (string text, bool literal)> map, bool cmd)
    {
        var sb = new System.Text.StringBuilder(body.Length + 16);
        bool quoted = false;
        for (int i = 0; i < body.Length;)
        {
            if (body[i] == '{' && PlaceholderAt(body, i, map) is { } key)
            {
                var (text, literal) = map[key];
                i += key.Length;
                if (literal) sb.Append(text);
                else if (text.Length > 0)
                {
                    var v = new System.Text.StringBuilder(text.Length);
                    foreach (char c in text)
                        if (c != '"' && !(cmd && c == '%')) v.Append(IsLineBreak(c) ? ' ' : c);
                    int slashes = 0;
                    while (slashes < v.Length && v[v.Length - 1 - slashes] == '\\') slashes++;
                    if (!cmd && (!quoted || (i < body.Length && body[i] == '"'))) v.Append('\\', slashes);
                    sb.Append(quoted ? v.ToString() : "\"" + v + "\"");
                }
                continue;
            }
            if (body[i] == '"') quoted = !quoted;
            sb.Append(body[i++]);
        }
        return sb.ToString();
    }

    /// A chain's steps: what would be typed in the search line, one after another.
    public static List<string> Steps(string body) =>
        body.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [GeneratedRegex(@"^(?:wait|기다리기|대기)\s*(\d+(?:\.\d+)?)\s*(?:초|s|sec|seconds?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WaitRx();

    /// "기다리기 2초" / "wait 2" → 2 s (at most a minute); null for any other step.
    public static TimeSpan? Wait(string step) =>
        WaitRx().Match(step) is { Success: true } m && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s)
            ? TimeSpan.FromSeconds(Math.Min(s, 60)) : null;

    /// The program that runs a script line: a .ps1 through PowerShell, a .bat/.cmd through cmd, a program as it is,
    /// anything else as a PowerShell command. Hidden runs read the output as UTF-8; shown ones stay open at the end.
    public static ProcessStartInfo StartInfo(string line, bool show)
    {
        line = line.Trim();
        var (file, args) = SplitFile(line);
        string ext = file is null ? "" : Path.GetExtension(file).ToLowerInvariant();
        var psi = new ProcessStartInfo
        {
            UseShellExecute = show,
            CreateNoWindow = !show,
            RedirectStandardOutput = !show,
            RedirectStandardError = !show,
            StandardOutputEncoding = show ? null : System.Text.Encoding.UTF8,
            StandardErrorEncoding = show ? null : System.Text.Encoding.UTF8,
            WorkingDirectory = file is not null && Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : Paths.Home,
        };
        // PowerShell gets its words through ArgumentList (quotes and the backslashes before them escaped the Windows
        // way); cmd reads its own line, so it gets the line as it is
        void PowerShell(string command)
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass" }) psi.ArgumentList.Add(a);
            if (show) psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add((show ? "" : "[Console]::OutputEncoding=[Text.Encoding]::UTF8; ") + command.Trim());
        }
        switch (ext)
        {
            case ".ps1":
                PowerShell($"& '{file!.Replace("'", "''")}' {args}");
                break;
            case ".bat" or ".cmd":
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = $"{(show ? "/k" : "/c")} \"{(show ? "" : "chcp 65001>nul & ")}\"{file}\" {args}\"";
                break;
            case ".exe" or ".com":
                psi.FileName = file!;
                psi.Arguments = args;
                break;
            default:
                PowerShell(line);
                break;
        }
        return psi;
    }

    /// The file a script line starts with ("C:\a b\x.ps1" -arg, or a path with no spaces), and the rest.
    public static (string? file, string args) SplitFile(string line)
    {
        if (line.StartsWith('"') && line.IndexOf('"', 1) is > 1 and var close)
        {
            string quoted = line[1..close];
            return File.Exists(quoted) ? (quoted, line[(close + 1)..].Trim()) : (null, line);
        }
        if (File.Exists(line)) return (line, "");
        // an unquoted path with spaces: the shortest start that is a file
        for (int sp = line.IndexOf(' '); sp > 0; sp = line.IndexOf(' ', sp + 1))
            if (File.Exists(line[..sp])) return (line[..sp], line[(sp + 1)..].Trim());
        return (null, line);
    }

    /// The last line worth showing from a run's output.
    public static string? LastLine(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) is { } l ? (l.Length > 160 ? l[..160] + "…" : l) : null;
}
