using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// The companies' logos Bloom fetches for accounts with none picked: the icon link found in a page, the SVG made readable
/// without its <style>, the one-grey test that draws such a logo in the text colour, and which picture an account shows.
internal static class OfficialLogosTests
{
    // chatgpt.com's favicon as it is served: no fill but the one its <style> gives, black, or white in the dark
    private const string GptLike = """
        <!-- a comment stays -->
        <svg xmlns="http://www.w3.org/2000/svg" width="180" height="180" viewBox="0 0 180 180" fill="none">
          <style>
            :root { fill: #000; }
            @media (prefers-color-scheme: dark) { :root { fill: #fff; } }
          </style>
          <path d="M20 20h140v140H20z"/>
        </svg>
        """;

    public static void IconLinkFromThePage()
    {
        const string page = """
            <html><head><meta charset="utf-8"/>
            <link rel="apple-touch-icon" href="/a/touch.png" sizes="180x180"/>
            <link rel="icon" href="/a/favicon-48.png" type="image/png" sizes="48x48"/>
            <link rel="icon" href="/cdn/assets/favicon.6a33781f61.svg?v=1&amp;x=2" type="image/svg+xml"/>
            """;
        Check.Eq(OfficialLogos.IconHref(page), "/cdn/assets/favicon.6a33781f61.svg?v=1&x=2", "the SVG one, its &amp; read");
        Check.Eq(OfficialLogos.IconHref("""<link href='/f.svg' rel='shortcut icon'>"""), "/f.svg", "single quotes, rel with two words, .svg by its name");
        Check.Eq(OfficialLogos.IconHref("""<link rel="apple-touch-icon" href="/t.svg"><link rel="icon" href="/f.png" type="image/png">"""), (string?)null, "no SVG icon: none");
    }

    public static void SvgStyleBecomesTheRootFill()
    {
        string svg = OfficialLogos.Normalized(GptLike);
        var root = XDocument.Parse(svg).Root!;
        Check.Eq(root.Attribute("fill")?.Value, "#000", "the light colour, on the root");
        Check.True(!root.Descendants().Any(e => e.Name.LocalName == "style"), "no <style> left");
        Check.Eq(root.Descendants().Count(e => e.Name.LocalName == "path"), 1, "the shape stays");
        var image = Check.NotNull(UsageLogo.Svg(svg), "Bloom's reader draws it");
        Check.True(UsageLogo.Svg(GptLike) is null, "and could not before");
        Check.True(UsageLogo.OneGrey(image), "one black: drawn in the text colour");
        bool refused = false;
        try
        {
            OfficialLogos.Normalized("""<svg xmlns="http://www.w3.org/2000/svg"><style>:root{fill:#000}</style></svg>""");
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        Check.True(refused, "an SVG with no shapes is refused");
    }

    public static void OneGreyOnly()
    {
        DrawingImage Svg(string body) => UsageLogo.Svg($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">{body}</svg>""")!;
        Check.True(!UsageLogo.OneGrey(Svg("""<path d="M2 2h20v20H2z" fill="#d97757"/>""")), "orange keeps its colour");
        Check.True(!UsageLogo.OneGrey(Svg("""<circle cx="12" cy="12" r="11" fill="#fff"/><path d="M6 6h12v12H6z" fill="#000"/>""")), "black on a white disc is two");
        Check.True(UsageLogo.OneGrey(Svg("""<path d="M2 2h9v9H2z" fill="#111"/><path d="M13 13h9v9h-9z" fill="#000"/>""")), "near-blacks are one");

        BitmapSource Pixels(params uint[] bgra)
        {
            var b = BitmapSource.Create(bgra.Length, 1, 96, 96, PixelFormats.Bgra32, null, bgra.SelectMany(p => System.BitConverter.GetBytes(p)).ToArray(), bgra.Length * 4);
            b.Freeze();
            return b;
        }
        Check.True(UsageLogo.OneGrey(Pixels(0x00000000, 0xFF000000, 0x40FF0000)), "black, the faint red edge not counted");
        Check.True(!UsageLogo.OneGrey(Pixels(0xFF000000, 0xFFFFFFFF)), "black and white pixels are two");
        Check.True(!UsageLogo.OneGrey(Pixels(0x00000000)), "nothing there: not a grey logo");
    }

    /// The user's pick first, else the company's logo, else none; the usage file and the cards say the same.
    public static void PickedBeforeOfficial()
    {
        using var folder = new SettingsTransferTests.DataFolder();
        var list = Settings.Current.AiAccounts;
        list.Clear();
        var claude = new AiAccount { Id = "claude", Kind = "claude" };
        list.Add(claude);
        Check.True(UsageIcons.Shown(claude) is null, "nothing fetched, nothing picked");

        string official = Path.Combine(Paths.Local, "usage-icons", "official-claude.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(official)!);
        File.WriteAllBytes(official, new byte[] { 0, 0, 1, 0, 0, 0 });
        Check.Eq(UsageIcons.Shown(claude), official, "the fetched logo");
        Check.True(UsageIcons.Shown(new AiAccount { Id = "x", Kind = "chatgpt" }) is null, "another kind's is not");

        string svg = Path.Combine(folder.Directory, "mine.svg");
        File.WriteAllText(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="#d97757"/></svg>""");
        UsageIcons.Pick(claude, svg);
        Check.Eq(UsageIcons.Shown(claude), claude.Icon, "the pick comes first");
        var cards = new System.Collections.Generic.List<UsageAccount> { new("Claude", "Max", new() { new("주간", 47, null, UsageLimit.WeekMins) }, System.DateTime.Now) { Id = "claude" } };
        Check.Eq((string?)JsonNode.Parse(UsageExport.Build(cards, true, 1))!["accounts"]![0]!["icon"], claude.Icon, "the usage file names the pick");

        UsageIcons.Clear(claude);
        Check.Eq(UsageIcons.Shown(claude), official, "cleared: the company's logo again");
        Check.Eq((string?)JsonNode.Parse(UsageExport.Build(cards, true, 1))!["accounts"]![0]!["icon"], official, "and the usage file names it");
        Check.True(File.Exists(official), "clearing a pick leaves the fetched logo");
    }
}
