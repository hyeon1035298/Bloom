using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// The companies' logos Bloom carries for accounts with none picked: written out as files Bloom's reader draws, the
/// one-grey test that draws such a logo in the text colour, and which picture an account shows.
internal static class OfficialLogosTests
{
    public static void ShippedMarksDraw()
    {
        using var folder = new SettingsTransferTests.DataFolder();
        foreach (var (kind, mark) in new[] { ("claude", "anthropic"), ("chatgpt", "openai") })
        {
            string file = Check.NotNull(OfficialLogos.For(kind), $"{kind}: a file");
            Check.Eq(Path.GetDirectoryName(file), Path.Combine(Paths.Local, "usage-icons"), $"{kind}: in Local\\usage-icons");
            Check.True(File.ReadAllBytes(file).SequenceEqual(OfficialLogos.Mark(mark)), $"{kind}: the {mark} mark as shipped");
            var image = Check.NotNull(UsageLogo.Svg(File.ReadAllText(file)), $"{kind}: Bloom's reader draws it");
            Check.True(UsageLogo.OneGrey(image), $"{kind}: one black, drawn in the text colour");

            File.WriteAllText(file, "<svg/>");
            Check.Eq(OfficialLogos.For(kind), file, $"{kind}: the same file");
            File.Delete(file);
            Check.Eq(OfficialLogos.For(kind), file, $"{kind}: written again when gone");
            Check.True(File.ReadAllBytes(file).SequenceEqual(OfficialLogos.Mark(mark)), $"{kind}: whole again");
        }
        Check.True(OfficialLogos.For("gemini") is null, "no logo for another kind");
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

    /// The user's pick first, else the company's logo; the usage file and the cards say the same.
    public static void PickedBeforeOfficial()
    {
        using var folder = new SettingsTransferTests.DataFolder();
        var list = Settings.Current.AiAccounts;
        list.Clear();
        var claude = new AiAccount { Id = "claude", Kind = "claude" };
        list.Add(claude);
        string official = Path.Combine(Paths.Local, "usage-icons", "official-claude.svg");
        Check.Eq(UsageIcons.Shown(claude), official, "nothing picked: the company's logo");

        string svg = Path.Combine(folder.Directory, "mine.svg");
        File.WriteAllText(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="#d97757"/></svg>""");
        UsageIcons.Pick(claude, svg);
        Check.Eq(UsageIcons.Shown(claude), claude.Icon, "the pick comes first");
        var cards = new System.Collections.Generic.List<UsageAccount> { new("Claude", "Max", new() { new("주간", 47, null, UsageLimit.WeekMins) }, System.DateTime.Now) { Id = "claude" } };
        Check.Eq((string?)JsonNode.Parse(UsageExport.Build(cards, true, 1))!["accounts"]![0]!["icon"], claude.Icon, "the usage file names the pick");

        UsageIcons.Clear(claude);
        Check.Eq(UsageIcons.Shown(claude), official, "cleared: the company's logo again");
        Check.Eq((string?)JsonNode.Parse(UsageExport.Build(cards, true, 1))!["accounts"]![0]!["icon"], official, "and the usage file names it");
        Check.True(File.Exists(official), "clearing a pick leaves the company's logo");
    }
}
