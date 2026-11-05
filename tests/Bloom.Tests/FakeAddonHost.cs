using System;
using System.IO;
using System.Threading;

namespace Bloom.Tests;

/// A stand-in for an add-on's host exe: this test program, started with the host's `--serve …` arguments by AddonRunner.
/// What it does comes from $BLOOM_FAKE_ADDON (inherited from the test that starts it):
///   exit:<n>       ends at once with code n
///   wait:<event>   waits for that (test-only) Stop event, then ends with 0
///   hang           ignores everything for 30 s (Stop has to end it)
/// The arguments it got go to the file $BLOOM_FAKE_ADDON_ARGS, one per line, once it is ready.
internal static class FakeAddonHost
{
    public const string Mode = "BLOOM_FAKE_ADDON", ArgsFile = "BLOOM_FAKE_ADDON_ARGS";

    public static bool Called(string[] args) => args.Length >= 2 && args[0] == Addons.AddonProtocol.ServeArg;

    public static int Run(string[] args)
    {
        string mode = Environment.GetEnvironmentVariable(Mode) ?? "exit:1";
        EventWaitHandle? stop = mode.StartsWith("wait:", StringComparison.Ordinal) ? new EventWaitHandle(false, EventResetMode.ManualReset, mode["wait:".Length..]) : null;
        if (Environment.GetEnvironmentVariable(ArgsFile) is { Length: > 0 } file)
        {
            File.WriteAllLines(file + ".part", args);
            File.Move(file + ".part", file, true);
        }
        if (mode.StartsWith("exit:", StringComparison.Ordinal)) return int.Parse(mode["exit:".Length..], System.Globalization.CultureInfo.InvariantCulture);
        if (stop is not null)
        {
            using (stop) return stop.WaitOne(30_000) ? 0 : 3;
        }
        Thread.Sleep(30_000);
        return 4;
    }
}
