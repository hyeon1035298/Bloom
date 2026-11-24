using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Bloom.Core;

namespace Bloom.Ai;

/// Antigravity (T3's `antigravity` driver: Google's agy CLI on the user's consumer Google sign-in) is view-only in Bloom
/// and in the add-ons: Google bars any program from writing to it, so Bloom reads and shows its
/// threads but sends them nothing — no words, no answers, no stop, no new thread on it. The user types in T3 itself.
internal static partial class T3Threads
{
    /// What Bloom says when it sends nothing to an Antigravity thread.
    public static string ViewOnlyNote => Tr("Antigravity 스레드는 보기만 돼요. T3에서 직접 입력하세요", "Antigravity threads are view-only here. Type in T3.");

    /// What it says when T3's state can't tell what a thread runs on (so nothing went to it).
    public static string UncheckedNote => Tr("T3 스레드가 어떤 모델인지 확인하지 못해 보내지 않았어요", "Couldn't tell what the T3 thread runs on, so nothing was sent.");

    /// A T3 driver of Antigravity; where Bloom knows no driver, a provider instance id (T3's default one is "antigravity").
    internal static bool Antigravity(string? driver) => driver?.Contains("antigravity", StringComparison.OrdinalIgnoreCase) == true;

    /// The provider instance `instance` runs Antigravity: by its driver in T3's settings (providerInstances), else by its id.
    public static bool AntigravityInstance(string instance) => Antigravity(InstanceDriverOf(instance) ?? instance);

    /// Where an instance's driver is read (InstanceDriver, T3's settings); a test's stand-in may take its place.
    internal static Func<string, string?> InstanceDriverOf = InstanceDriver;

    /// A T3 model id (AiModels.T3) on an Antigravity instance.
    public static bool AntigravityModel(string? model) => model is not null && AiModels.T3Parts(model) is { } p && AntigravityInstance(p.Instance);

    /// T3's thread `thread` runs on Antigravity: by the driver of its newest provider thread in T3's state, or the
    /// instance it is on; null when T3's state can't tell (not there or not read, no such thread).
    public static bool? AntigravityThread(string thread)
    {
        string db = Path.Combine(UserData, "statev2.sqlite");
        try
        {
            if (!File.Exists(db)) return null;
            bool? found = null;
            Native.SystemSqlite.ReadOnly(db, h => Native.SystemSqlite.Query(h,
                "select coalesce(t.provider_instance_id, t.default_provider), " +
                "(select p.driver from orchestration_v2_projection_provider_threads p where p.thread_id = t.thread_id order by p.updated_at desc limit 1) " +
                $"from orchestration_v2_projection_threads t where t.thread_id = {Literal(thread)}",
                row => found = Antigravity(Native.SystemSqlite.Text(row, 1)) || Native.SystemSqlite.Text(row, 0) is { Length: > 0 } instance && AntigravityInstance(instance)));
            return found;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error("t3 antigravity", e); // another T3 version's tables, or no SQLite: nothing is sent (T3Agent)
            return null;
        }
    }

    private static readonly object driversGate = new();
    private static (DateTime Stamp, Dictionary<string, string> Drivers)? drivers; // (driversGate)

    /// The driver T3's settings give the provider instance `instance`; null when they name none. Read again only when
    /// the file changed (the composer asks on every key).
    private static string? InstanceDriver(string instance)
    {
        string file = Path.Combine(UserData, "settings.json");
        try
        {
            DateTime stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            lock (driversGate)
            {
                if (drivers is not { } known || known.Stamp != stamp)
                {
                    known = (stamp, stamp == DateTime.MinValue ? new() : Drivers(File.ReadAllText(file)));
                    drivers = known;
                }
                return known.Drivers.GetValueOrDefault(instance);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// T3's settings → each provider instance's driver (`providerInstances.<id>.driver`); none for a broken file.
    internal static Dictionary<string, string> Drivers(string settings)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(settings);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("providerInstances", out var instances) && instances.ValueKind == JsonValueKind.Object)
                foreach (var i in instances.EnumerateObject())
                    if (i.Value.ValueKind == JsonValueKind.Object && i.Value.TryGetProperty("driver", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } driver)
                        all[i.Name] = driver;
        }
        catch (JsonException)
        {
            /* T3 writing it, or another shape: by the ids alone */
        }
        return all;
    }
}
