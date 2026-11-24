using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Ai;

namespace Bloom.Search;

/// 내 명령 values handed on to a program. Windows PowerShell 5.1 passes a program its arguments with no escaping: a value
/// with a space goes inside "…" as it is, so a " in it, or a \ at its end, splits it or joins it with the next one; and
/// cmd.exe (cmd itself, a .cmd/.bat, or anything after --%) reads & | < > ^ % and line breaks in what it gets. A template
/// whose statements start a program makes ScriptLine refuse those characters in every value, wherever it sits (a value
/// put in a variable or a string still reaches the program).
internal sealed partial class UserCommands
{
    /// How far a template's values can reach: PowerShell only, a program's command line, or cmd.exe's.
    private enum Reach { PowerShell, Program, Cmd }

    private static Reach Max(Reach a, Reach b) => a > b ? a : b;

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "elseif", "else", "switch", "foreach", "for", "while", "do", "until", "try", "catch", "finally", "trap", "throw",
        "return", "exit", "break", "continue", "function", "filter", "workflow", "param", "begin", "process", "end", "data",
        "dynamicparam", "class", "enum", "using",
    };

    /// Windows PowerShell 5.1's own aliases and functions that a program on PATH may share a name with (where.exe, sort.exe,
    /// curl.exe, Git's ls): PowerShell runs its own (-NoProfile, so there are no others).
    private static readonly HashSet<string> PsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "%", "?", "ac", "cat", "cd", "chdir", "clear", "cls", "copy", "cp", "curl", "del", "diff", "dir", "echo", "erase", "fc",
        "foreach", "gc", "gci", "gcm", "gl", "gm", "gps", "h", "help", "history", "iex", "ii", "irm", "iwr", "kill", "ls",
        "man", "md", "measure", "mkdir", "more", "mount", "move", "mv", "ni", "popd", "ps", "pushd", "pwd", "r", "rd", "ren",
        "ri", "rm", "rmdir", "saps", "sc", "select", "set", "sl", "sleep", "sort", "spps", "start", "sv", "tee", "type", "wget",
        "where", "write",
    };

    /// Where a statement's first word ends: a command name or a path, up to a space, an operator or a quote. Empty when the
    /// statement starts with something else ($x, (…), 'text', -not, [type]).
    private static int WordEnd(string body, int i)
    {
        if (!(char.IsLetterOrDigit(body[i]) || body[i] is '_' or '.' or '\\' or '/' or '~')) return i;
        int j = i;
        while (j < body.Length && !char.IsWhiteSpace(body[j]) && body[j] is not (';' or '|' or '&' or '(' or ')' or '{' or '}' or ',' or '<' or '>' or '`' or '$')
               && !IsSingleQuote(body[j]) && !IsDoubleQuote(body[j]))
            j++;
        return j;
    }

    /// The command after & or . when it isn't a bare word: a plain '…' or "…" names it; a { } script block is PowerShell's;
    /// a variable or an expression could be anything, cmd included.
    private static Reach CalledString(string body, int i)
    {
        char q = body[i];
        if (q == '{') return Reach.PowerShell;
        if (!IsSingleQuote(q) && !IsDoubleQuote(q)) return Reach.Cmd;
        int close = i + 1;
        while (close < body.Length && !(IsSingleQuote(q) ? IsSingleQuote(body[close]) : IsDoubleQuote(body[close]))) close++;
        string name = body[(i + 1)..Math.Min(close, body.Length)];
        return name.Length == 0 || (IsDoubleQuote(q) && name.IndexOfAny(['$', '`']) >= 0) || name.Contains('{') ? Reach.Cmd : Classify(name);
    }

    /// What runs for a command word: a .ps1 or a name PowerShell knows itself stays in PowerShell; a .cmd/.bat, cmd itself,
    /// or a path PowerShell may finish with .cmd goes through cmd; another file found (or named) is a program.
    private static Reach Classify(string word)
    {
        string ext = Path.GetExtension(word).ToLowerInvariant();
        if (ext == ".ps1") return Reach.PowerShell;
        if (ext is ".cmd" or ".bat") return Reach.Cmd;
        if (word.Contains('\\') || word.Contains('/')) return ext.Length > 0 && !word.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) ? Reach.Program : Reach.Cmd;
        if (PsNames.Contains(word)) return Reach.PowerShell;
        string file;
        try
        {
            file = McpLaunch.Resolve(word, Array.Empty<string>()).File; // looked up on PATH with PATHEXT; a .cmd/.bat comes back as cmd.exe
        }
        catch (McpException)
        {
            return Reach.Cmd;
        }
        if (string.Equals(file, word, StringComparison.Ordinal)) return ext is ".exe" or ".com" ? Reach.Program : Reach.PowerShell; // not on PATH: a cmdlet or function
        return Path.GetFileName(file).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ? Reach.Cmd : Reach.Program;
    }

    /// Throws CommandValueException, with what to change, for a value that can't reach `reach` safely. `raw`: the value
    /// stands after --% in a "…" Bloom adds, so it can't end in \ either.
    private static void Refuse(string key, string text, Reach reach, bool raw)
    {
        if (reach == Reach.PowerShell || text.Length == 0) return;
        string source = key.Equals("{clipboard}", StringComparison.OrdinalIgnoreCase) ? Tr("클립보드 내용", "clipboard content") : Tr("입력값", "input value");
        foreach (char c in text)
        {
            string? what = c == '"' ? "‘\"’"
                : reach < Reach.Cmd ? null
                : IsLineBreak(c) ? Tr("줄바꿈", "line break")
                : char.IsControl(c) ? Tr("제어", "control")
                : c is '&' or '|' or '<' or '>' or '^' or '%' ? $"‘{c}’" : null;
            if (what is not null)
                throw new CommandValueException(Tr($"이 명령은 {source}을 다른 프로그램에 전달하므로 {what} 문자를 사용할 수 없습니다. 해당 문자를 빼고 다시 실행하세요.", $"This command passes the {source} to another program and cannot contain {what} characters. Remove them and try again."));
        }
        if (text.EndsWith('\\') && (raw || text.Any(char.IsWhiteSpace)))
            throw new CommandValueException(Tr($"이 명령은 {source}을 다른 프로그램에 전달하므로 끝에 \\를 둘 수 없습니다. 끝의 \\를 빼고 다시 실행하세요.", $"This command passes the {source} to another program and cannot end with \\. Remove the trailing \\ and try again."));
    }
}
