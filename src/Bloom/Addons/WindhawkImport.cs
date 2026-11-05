using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Bloom.Core;
using Microsoft.Win32;

namespace Bloom.Addons;

/// Where the values of a mod's settings come from: a registry key in use, a list in the tests.
internal interface ISettingsSource
{
    /// The values by name; null when there are none to bring in (the key isn't there).
    IReadOnlyList<KeyValuePair<string, object?>>? Read();
}

/// The settings of the Windhawk build of the same mod, read only: Windhawk keeps them under its engine key in HKLM.
internal sealed class WindhawkRegistry : ISettingsSource
{
    public const string Key = @"SOFTWARE\Windhawk\Engine\Mods\local@pill-taskbar\Settings";

    public IReadOnlyList<KeyValuePair<string, object?>>? Read()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = hklm.OpenSubKey(Key, writable: false);
            if (k is null) return null;
            var list = new List<KeyValuePair<string, object?>>();
            foreach (string name in k.GetValueNames()) list.Add(new(name, k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
            return list;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("windhawk settings", e);
            return null;
        }
    }
}

/// 설정 > 작업표시줄 > Windhawk 설정 가져오기: the mod's settings as they are in Windhawk become the add-on's, so moving
/// from the Windhawk build to the add-on keeps the taskbar looking the same.
internal static class WindhawkImport
{
    /// The flat map the runtime reads (values.json): REG_DWORD as a signed number (0xFFFFFFFF is -1, as Windhawk's
    /// Wh_GetIntSetting reads it), REG_SZ as text; other kinds are not settings of a mod and are left out.
    public static JsonObject? Map(ISettingsSource source)
    {
        if (source.Read() is not { } values) return null;
        var map = new JsonObject();
        foreach (var (name, value) in values)
        {
            switch (value)
            {
                case int i:
                    map[name] = i;
                    break;
                case uint u:
                    map[name] = unchecked((int)u);
                    break;
                case string s:
                    map[name] = s;
                    break;
            }
        }
        return map;
    }

    /// Writes the values to the user's settings file and the runtime's values.json (each whole or not at all); how many
    /// there were, or -1 when there was nothing to bring in. IOException when a file could not be written.
    public static int Import(ISettingsSource source, string settingsFile, string valuesFile)
    {
        if (Map(source) is not { } map) return -1;
        string text = AddonHub.ValuesText(map);
        foreach (string file in new[] { settingsFile, valuesFile })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            AtomicFile.Write(file, stream =>
            {
                using var w = new StreamWriter(stream);
                w.Write(text);
            });
        }
        return map.Count;
    }
}
