using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Bloom.Core;

namespace Bloom.Ai;

/// "AI로 아이콘 만들기": Claude draws one flat symbol as SVG path data; the favorite then wears it on its
/// black tile in the accent colour ("svg:<path data>"). One CLI call (Sonnet, thinking off) per icon —
/// each counts against the user's plan once. The answer is checked by parsing it as a WPF geometry.
internal static partial class IconMaker
{
    private const string System =
        "You draw app icons as SVG path data. Reply with ONLY the contents of one SVG path `d` attribute for a 24x24 viewBox — " +
        "no <svg>, no quotes, no code fences, no words. The icon is one flat colour drawn with FILLED shapes (fill-rule nonzero; " +
        "make outlines as filled rings, never strokes), centred, about 2 units of padding, simple and bold enough to read at 16 px, " +
        "in the style of modern system icon sets (Fluent / Material). No letters or text, no background square or circle frame. " +
        "At most 1800 characters.";

    /// → ("svg:<d>", null) or (null, error).
    public static async Task<(string? icon, string? error)> Draw(string name, string? idea, CancellationToken ct)
    {
        if (Demo.On) return (null, Tr("녹화 모드에서는 아이콘을 생성할 수 없습니다", "Cannot generate icons in recording mode"));
        string? cli = ClaudeSession.FindCli(Settings.Current.ClaudePath);
        if (cli is null) return (null, AiErrors.ClaudeMissing);
        var psi = OneShot.Psi(cli);
        AiAccounts.UseHome(psi, "claude", AiAccounts.ChatHome("claude"));
        foreach (string a in new[]
        {
            "-p", "--model", "sonnet", "--output-format", "text", "--system-prompt", System,
            "--tools", "", "--strict-mcp-config", "--disable-slash-commands",
            "--settings", "{\"disableAllHooks\":true,\"alwaysThinkingEnabled\":false}",
        }) psi.ArgumentList.Add(a);
        string ask = idea is { Length: > 0 } ? $"An icon for \"{name}\": {idea}" : $"An icon for the app or shortcut \"{name}\" (pick the symbol people would recognise for it).";
        var (code, output, error) = await OneShot.RunCli(psi, ask, ct);
        if (ct.IsCancellationRequested) return (null, null);
        if (code != 0 || output.Trim().Length == 0) return (null, error.Trim().Length > 0 ? AiErrors.Friendly(error, false) : Tr("Claude의 응답이 없습니다. 다시 시도하세요.", "No response from Claude. Please try again."));
        string? d = PathData(output);
        if (d is null) return (null, Tr("아이콘을 읽지 못했습니다. 다시 그려 주세요.", "Could not read the icon. Please draw it again."));
        return ("svg:" + d, null);
    }

    /// The path data in an answer (bare, in d="…", or inside an <svg>), only when WPF can draw it.
    public static string? PathData(string answer)
    {
        string t = answer.Trim().Trim('`').Trim();
        if (DAttr().Match(t) is { Success: true } m) t = m.Groups[1].Value;
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length is < 4 or > 4000 || !Regex.IsMatch(t, @"^[MmZzLlHhVvCcSsQqTtAa0-9eE\s,.\-+]+$")) return null;
        try
        {
            var g = Geometry.Parse(t);
            return g.Bounds.IsEmpty || g.Bounds.Width < 1 || g.Bounds.Height < 1 ? null : t;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Clip(string s)
    {
        s = s.Trim();
        return s.Length > 140 ? s[..140] + "…" : s;
    }

    [GeneratedRegex("(?:^|\\s)d\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex DAttr();
}
