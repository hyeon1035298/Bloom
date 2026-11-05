using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Core;

namespace Bloom.Ai;

/// The companies' logos for accounts with none picked (UsageIcons.Shown): the Anthropic mark for Claude, the OpenAI mark
/// for ChatGPT, both one-colour SVGs Bloom carries (Assets\Logos; trademarks of their owners, not under Bloom's MIT), so
/// drawn in the text colour. The taskbar add-on reads files, so each is written out to
/// Local\usage-icons\official-<kind>.svg the first time it is asked for, and again when that file is gone or differs.
internal static class OfficialLogos
{
    private static readonly Dictionary<string, string> Marks = new() { ["claude"] = "anthropic", ["chatgpt"] = "openai" };
    private static readonly HashSet<string> Written = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);

    /// The logo file for this kind of account; null for another kind or when it can't be written.
    public static string? For(string kind)
    {
        if (!Marks.TryGetValue(kind, out var mark)) return null;
        string dir = Path.Combine(Paths.Local, "usage-icons"), file = Path.Combine(dir, $"official-{kind}.svg");
        lock (Written)
        {
            if (Written.Contains(file) && File.Exists(file)) return file;
            if (Failed.Contains(file)) return null;
            try
            {
                byte[] bytes = Mark(mark);
                if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(file + ".part", bytes);
                    File.Move(file + ".part", file, overwrite: true);
                }
                Written.Add(file);
                return file;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error($"official logo {kind}", e);
                Failed.Add(file);
                return null;
            }
        }
    }

    internal static byte[] Mark(string name)
    {
        using var stream = typeof(OfficialLogos).Assembly.GetManifestResourceStream($"Bloom.Assets.Logos.{name}.svg")
            ?? throw new InvalidOperationException($"no {name}.svg in the build");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
