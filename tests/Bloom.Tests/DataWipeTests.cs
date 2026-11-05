using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// 설정 > 데이터's deletions: only inside the data folders, a junction inside unlinked and never followed (what it points to
/// — Codex's own sessions — stays), a hard link's other name kept, the whole folder of the reset likewise.
internal static class DataWipeTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);

    /// A data folder that Paths.Deletable allows (a dev --data folder holds settings.json), and a folder outside it.
    private static void InData(Action<string> body)
    {
        using var data = new SettingsTransferTests.DataFolder();
        File.WriteAllText(Paths.In("settings.json"), "{}");
        string outside = Path.Combine(Path.GetTempPath(), "bloom-wipe-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            body(outside);
        }
        finally
        {
            // a junction left inside goes as a link before its target does; DataFolder then deletes the folder again
            Directory.CreateDirectory(data.Directory);
            File.WriteAllText(Paths.In("settings.json"), "{}");
            DataWipe.DeleteRoot(Paths.Data);
            Directory.CreateDirectory(data.Directory);
            Directory.Delete(outside, true);
        }
    }

    public static void JunctionTargetSurvivesTheFolder() => InData(outside =>
    {
        string rollout = Path.Combine(outside, "rollout.jsonl");
        File.WriteAllText(rollout, "keep me");
        string home = Path.Combine(Paths.Local, "codex-app-server");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "x");
        CodexAppHome.Junction(Path.Combine(home, "sessions"), outside);
        Check.True(File.Exists(Path.Combine(home, "sessions", "rollout.jsonl")), "the junction shows the file it points to");
        Check.Eq(DataWipe.Size(home), 1L, "the size doesn't follow the junction");
        Check.Eq(DataWipe.Delete(home), 0, "nothing stays");
        Check.True(!Directory.Exists(home), "the folder is gone");
        Check.Eq(File.ReadAllText(rollout), "keep me", "the junction's target and its file stay");
    });

    public static void JunctionTargetSurvivesTheReset() => InData(outside =>
    {
        string rollout = Path.Combine(outside, "rollout.jsonl");
        File.WriteAllText(rollout, "keep me");
        string account = Path.Combine(Paths.Local, "accounts", "chatgpt-test", "bloom-app-server");
        Directory.CreateDirectory(account);
        CodexAppHome.Junction(Path.Combine(account, "sessions"), outside);
        File.WriteAllText(Paths.In("notes.json"), "{}");
        Check.True(DataWipe.DeleteRoot(Paths.Data), "the whole data folder goes");
        Check.True(!Directory.Exists(Paths.Data), "it is gone");
        Check.Eq(File.ReadAllText(rollout), "keep me", "the junction's target and its file stay");
    });

    public static void HardLinkKeepsTheOtherName() => InData(outside =>
    {
        string auth = Path.Combine(outside, "auth.json");
        File.WriteAllText(auth, "sign-in");
        string home = Path.Combine(Paths.Local, "codex-app-server");
        Directory.CreateDirectory(home);
        Check.True(CreateHardLinkW(Path.Combine(home, "auth.json"), auth, IntPtr.Zero), "the link was made");
        Check.Eq(DataWipe.Delete(home), 0, "nothing stays");
        Check.Eq(File.ReadAllText(auth), "sign-in", "the account's own auth.json stays");
    });

    public static void NothingOutsideOrThroughALink() => InData(outside =>
    {
        string file = Path.Combine(outside, "mine.txt");
        File.WriteAllText(file, "x");
        Check.True(!DataWipe.Inside(file), "a path outside the data folders is not inside");
        Check.True(!DataWipe.Inside(Paths.Data), "the data folder itself is not inside it");
        Check.True(!DataWipe.Inside("notes.json"), "a relative path is never inside");
        Check.Eq(DataWipe.Delete(file), 1, "a path outside is refused");
        Check.True(File.Exists(file), "and stays");
        string link = Path.Combine(Paths.Local, "codex-app-server", "sessions");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CodexAppHome.Junction(link, outside);
        string through = Path.Combine(link, "mine.txt");
        Check.True(!DataWipe.Inside(through), "a path through a junction is not inside");
        Check.Eq(DataWipe.Delete(through), 1, "it is refused");
        Check.True(File.Exists(file), "and the file it named stays");
        Check.True(DataWipe.Inside(Paths.In("notes.json")) && DataWipe.Inside(Path.Combine(Paths.Local, "lens")), "Bloom's own files are inside");
    });

    public static void FileInUseStaysAndIsCounted() => InData(_ =>
    {
        string dir = Path.Combine(Paths.Local, "lens");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.png"), "a");
        string held = Path.Combine(dir, "b.png");
        File.WriteAllText(held, "b");
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            Check.True(DataWipe.Delete(dir) > 0, "a file in use is counted, not thrown");
        Check.True(File.Exists(held) && !File.Exists(Path.Combine(dir, "a.png")), "the rest went");
        Check.Eq(DataWipe.Delete(dir), 0, "once let go it goes too");
    });

    public static void CliHistoryIsTheAddedAccountsOnly()
    {
        var paths = DataParts.CliHistory(new[] { ("claude", @"C:\b\accounts\claude-1"), ("chatgpt", @"C:\b\accounts\chatgpt-2") }).ToList();
        Check.Eq(string.Join(" | ", paths), @"C:\b\accounts\claude-1\projects | C:\b\accounts\chatgpt-2\sessions | C:\b\accounts\chatgpt-2\archived_sessions", "Claude Code's projects, Codex's sessions");
    }
}
