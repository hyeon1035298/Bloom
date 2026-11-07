using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// 바뀐 점's pictures: the changelog names them by their GitHub address, Bloom carries this version's own and plays them
/// (Controls\AnimatedPicture.cs, WhatsNewWindow).
internal static class WhatsNewPictureTests
{
    private static string Changelog()
    {
        using var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Bloom.CHANGELOG.md")!;
        return new StreamReader(s).ReadToEnd();
    }

    private static byte[]? BuiltIn(string name)
    {
        using var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Bloom.WhatsNew." + name);
        if (s is null) return null;
        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        return bytes;
    }

    /// Windows reads WebP through its WebP image extension, which Windows Server (the release workflow's machine) lacks;
    /// there 바뀐 점 shows the alt texts instead (WhatsNewWindow.Picture), and the decoding checks are skipped.
    private static bool NoWebP(string test)
    {
        try
        {
            _ = new AnimatedPicture(BuiltIn("alttab.webp")!);
            return false;
        }
        catch (NotSupportedException)
        {
            Console.WriteLine($"  skip  {test}: this Windows has no WebP decoder");
            return true;
        }
    }

    /// Every picture this version's notes show, in either language, is built in, decodes and has a time for each frame.
    public static void ThisVersionsPicturesAreBuiltIn()
    {
        string log = Changelog().Replace("\r\n", "\n");
        int start = log.IndexOf("\n## " + ReleaseNotes.Current + "\n", StringComparison.Ordinal);
        Check.True(start >= 0, "a section for this version");
        int end = log.IndexOf("\n## ", start + 4, StringComparison.Ordinal);
        string section = end < 0 ? log[start..] : log[start..end];
        var names = Regex.Matches(section, @"!\[[^\]]*\]\((?<src>[^)\s]+)\)").Select(m => m.Groups["src"].Value.Split('/')[^1]).Distinct().ToList();
        foreach (string name in names) Check.True(BuiltIn(name) is not null, name + " is built in");
        if (NoWebP(nameof(ThisVersionsPicturesAreBuiltIn))) return;
        foreach (string name in names)
        {
            var bytes = BuiltIn(name);
            var pic = new AnimatedPicture(bytes!);
            Check.True(pic.Frames > 10, $"{name} moves ({pic.Frames} frames)");
            int total = AnimatedPicture.FrameTimes(bytes!, pic.Frames).Sum();
            Check.True(total is > 5000 and < 15000, $"{name} loops in {total} ms");
        }
    }

    public static void FrameTimesComeFromTheFile()
    {
        var bytes = BuiltIn("alttab.webp")!;
        var times = AnimatedPicture.FrameTimes(bytes, 0);
        Check.True(times.All(t => t == 40), "a count other than the file's: 40 ms each");
        Check.True(AnimatedPicture.FrameTimes([1, 2, 3], 3).SequenceEqual([40, 40, 40]), "not a WebP: 40 ms each");
    }

    /// [![alt](src)](url) is a picture with that link (a pane's reading, and 바뀐 점's); a chat answer keeps it as text.
    public static void LinkedPictureParses()
    {
        var blocks = MdParser.Parse("[![a moving picture](https://x/y/taskbar.webp)](bloom://settings/addons)", false, true);
        var span = blocks.Single().Spans.Single();
        Check.Eq(span.Kind, MdSpanKind.Picture, "a picture");
        Check.Eq(span.Source, "https://x/y/taskbar.webp", "its address");
        Check.Eq(span.Link?.Url, "bloom://settings/addons", "its link");
        Check.Eq(span.Text, "a moving picture", "its alt text");
        Check.True(MdParser.Parse("![x](y.png)", false, false).Single().Spans.All(s => s.Kind == MdSpanKind.Text), "a chat answer draws no pictures");
    }

    /// A paragraph that is one picture becomes the element made of it; one the maker can't make shows its alt text.
    public static void RenderPutsPicturesInTheirPlace()
    {
        if (NoWebP(nameof(RenderPutsPicturesInTheirPlace))) return;
        string md = "- one\n\n![shown](a/b/alttab.webp)\n\n![missing](a/b/none.webp)\n\n- two";
        var text = Markdown.Render(md, _ => { }, picture: s => s.Source!.EndsWith("alttab.webp") ? new AnimatedPicture(BuiltIn("alttab.webp")!) : null);
        var blocks = text.Document.Blocks.ToList();
        Check.True(blocks.OfType<BlockUIContainer>().Count(b => b.Child is AnimatedPicture) == 1, "one picture drawn");
        Check.True(blocks.OfType<Paragraph>().Any(p => new TextRange(p.ContentStart, p.ContentEnd).Text.Contains("missing")), "the other one's alt text");
    }
}
