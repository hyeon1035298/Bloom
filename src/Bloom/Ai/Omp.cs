using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Core;

namespace Bloom.Ai;

/// omp (oh-my-pi, a coding-agent CLI) as one more AI for Bloom: where it is, the arguments of its two uses (one answer with
/// `omp -p`, a chat with `omp --mode rpc`) and what a slash command in a chat means. Pure functions apart from the
/// lookups, so the tests can check them. omp names a model "provider/model" ("anthropic/claude-sonnet-5-5"); Bloom's id for
/// it is "omp:" + that, and "omp:" alone stands for the model omp itself would pick (no --model is passed).
///
/// Both uses replace omp's system prompt (--system-prompt). omp reads the user's own persona and project files (an AGENTS.md,
/// a personality file, rules, skills, extensions) into its prompt for its coding work; those are the user's instructions
/// for that work and must not colour a launcher's answers, so they are left out: no skills, rules or extensions are loaded,
/// the working folder is an empty one of Bloom's, and nothing is saved in omp's own session list.
internal static class OmpCli
{
    /// The omp levels, lowest first; "off" = no thinking.
    public static readonly string[] Levels = { "off", "minimal", "low", "medium", "high", "xhigh", "max" };

    /// omp.exe: the path set in the settings, else the PATH, else where bun puts global programs.
    public static string? FindCli(string? custom) => PretendMissing ? null : Find(custom, Environment.GetEnvironmentVariable("PATH") ?? "", Paths.Home);

    internal static string? Find(string? custom, string path, string home)
    {
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom.Trim())) return custom.Trim();
        foreach (string dir in path.Split(';'))
        {
            try
            {
                string p = Path.Combine(dir.Trim().Trim('"'), "omp.exe");
                if (dir.Trim().Length > 0 && File.Exists(p)) return p;
            }
            catch
            {
                /* bad PATH entry */
            }
        }
        foreach (string p in new[] { Path.Combine(home, @".bun\bin\omp.exe"), Path.Combine(home, @".local\bin\omp.exe") })
            if (File.Exists(p)) return p;
        return null;
    }

    /// QA only (`--qa-set omp-missing`): omp is pretended not to be on this PC, wherever it is.
    internal static bool PretendMissing;

    public static string? FindCli() => FindCli(Settings.Current.OmpPath);

    /// The empty folder omp works in: no project files around, nothing to read by accident.
    public static string WorkDir() => Folder(@"ai\omp-work\.keep");

    /// Where the chats' omp sessions are kept (resumed by id), apart from omp's own list.
    public static string SessionDir() => Folder(@"ai\omp-sessions\.keep");

    private static string Folder(string keep)
    {
        string folder = Path.GetDirectoryName(Paths.InLocal(keep))!;
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// Does a saved chat session with this id exist (omp refuses to resume one that is gone)?
    public static bool HasSession(string id)
    {
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        try
        {
            return Directory.GetFiles(SessionDir(), "*_" + id + ".jsonl").Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// A deleted chat's omp session goes with it.
    public static void DeleteSession(string id)
    {
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        try
        {
            foreach (string f in Directory.GetFiles(SessionDir(), "*_" + id + ".jsonl")) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            /* the folder is Bloom's own; a leftover costs only disk */
        }
    }

    /// A message omp reads as a slash command: "/" and a letter first (a path like "/etc" is one too, omp then treats it as an
    /// ordinary prompt when no such command exists).
    public static bool IsSlash(string text) => text.Length > 1 && text[0] == '/' && char.IsLetter(text[1]);

    /// "/session now" → "session".
    public static string SlashName(string text)
    {
        string t = text.Trim().TrimStart('/');
        int end = 0;
        while (end < t.Length && !char.IsWhiteSpace(t[end])) end++;
        return t[..end].ToLowerInvariant();
    }

    /// Slash commands a Bloom chat passes to omp as commands: the read-only ones and /compact.
    public static readonly HashSet<string> SlashAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "compact", "shake", "session", "context", "usage", "tools", "jobs", "changelog", "dirs", "todo",
    };

    /// Refused in a Bloom chat (answered locally, nothing sent): the chat runs on the user's own omp, whose settings every
    /// other omp session shares — these change them (/fast, /mcp, /plugins …), move files or the session (/move, /export),
    /// put it outside (/share), start agent work, or switch the model/session Bloom keeps track of itself. Any other "/word"
    /// (a path, a command omp doesn't know) goes as an ordinary message.
    public static readonly HashSet<string> SlashRefused = new(StringComparer.OrdinalIgnoreCase)
    {
        "modelpreset", "switch", "effort", "fast", "slow", "extended-context", "computer", "skillful", "advisor", "mcp", "ssh",
        "plugins", "marketplace", "reload-plugins", "memory", "browser", "autoresearch",
        "move", "wt", "add-dir", "remove-dir", "export", "share", "dump", "rename", "pin",
        "ratchet", "prewalk", "security", "force", "retry", "init",
        "model", "new", "clear", "resume", "fork", "branch", // resume: answered with where Bloom keeps earlier chats (Chats.Send)
    };

    /// "/compact" or "/compact keep the decisions" → the instructions ("" = none); null for anything else.
    public static string? CompactCommand(string text)
    {
        string t = text.Trim();
        if (!t.StartsWith("/compact", StringComparison.OrdinalIgnoreCase)) return null;
        string rest = t[8..];
        return rest.Length == 0 ? "" : char.IsWhiteSpace(rest[0]) ? rest.Trim() : null;
    }

    /// `omp -p`: one answer, the question on stdin (so a text that starts with "-" or "@" is never read as an option or a file).
    /// `model` "" = omp's own default; `effort` null = off.
    public static List<string> OneShotArgs(string model, string? effort, string system)
    {
        var a = new List<string> { "-p", "--no-session", "--no-title" };
        if (model.Length > 0) a.AddRange(new[] { "--model", model });
        a.AddRange(new[]
        {
            "--thinking=" + Level(effort), "--system-prompt=" + system,
            "--no-tools", "--no-skills", "--no-rules", "--no-extensions", "--no-lsp", "--no-pty",
        });
        return a;
    }

    /// `omp --mode rpc` for a chat. Tools: none, or web search only; nothing that reads or writes files or runs commands. The
    /// MCP tools of the conversation (OmpMcp) come from its working folder's .omp/mcp.json: --tools / --no-tools leave them alone.
    public static List<string> ChatArgs(string model, string effort, string system, string sessionDir, string? resume, bool search)
    {
        var a = new List<string> { "--mode", "rpc", "--no-ui", "--no-title" };
        if (model.Length > 0) a.AddRange(new[] { "--model", model });
        a.AddRange(new[]
        {
            "--thinking=" + Level(effort), "--system-prompt=" + system, "--session-dir=" + sessionDir,
            search ? "--tools=web_search" : "--no-tools",
            "--no-skills", "--no-rules", "--no-extensions", "--no-lsp", "--no-pty",
        });
        if (!string.IsNullOrEmpty(resume)) a.Add("--resume=" + resume);
        return a;
    }

    /// omp's own level name for a Bloom one: "fast" (the Claude side's off) and anything unknown count as off.
    public static string Level(string? effort) => effort is "minimal" or "low" or "medium" or "high" or "xhigh" or "max" ? effort : "off";

    public static ProcessStartInfo Psi(string exe, string? work = null)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            StandardInputEncoding = utf8,
            WorkingDirectory = work ?? WorkDir(),
        };
        psi.Environment["NO_COLOR"] = "1";
        // omp connects its MCP servers in the background and gives them only 250 ms before the session starts; the first
        // message comes at once, so without this it would be answered with no MCP tool (measured). The wait ends when every
        // server has connected or failed, so a chat without MCP servers starts as fast as before.
        psi.Environment["OMP_MCP_STARTUP_TIMEOUT_MS"] = "20000";
        return psi; // omp keeps its own logins and keys: nothing is removed from the environment (Cli.CleanEnvironment is for the others)
    }

    /// The bare model name of "provider/model" for display: "claude-sonnet-5-5" → "Sonnet 5.5", "deepseek-v4.1-flash" → "Deepseek V4.1 Flash".
    /// "" (omp's own default) → "Default model".
    public static string ShortName(string slug)
    {
        if (slug.Length == 0) return Tr("기본 모델", "Default model");
        string name = slug.Contains('/') ? slug[(slug.IndexOf('/') + 1)..] : slug;
        if (name.Contains('/')) name = name[(name.LastIndexOf('/') + 1)..];
        if (name.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) name = name[7..];
        var words = new List<string>();
        foreach (string part in name.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            bool digits = part.All(char.IsDigit);
            if (digits && words.Count > 0 && words[^1].All(c => char.IsDigit(c) || c == '.')) words[^1] += "." + part;
            else words.Add(part.Length > 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part);
        }
        return words.Count == 0 ? slug : string.Join(' ', words);
    }
}
