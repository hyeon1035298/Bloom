using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 컬러 팔레트: the colours of a picture (Bloom Lens › 7, the drawing bar's 팔레트, or one kept in the colour history) —
/// the swatches, the one picked with the gradients the colour picker shows for a colour, 전체 복사, the palettes kept.
internal sealed partial class Engine
{
    public List<Section> PaletteView()
    {
        if (host.Palette is not { Colors.Count: > 0 } p)
        {
            var none = new Section(Tr("컬러 팔레트", "Color Palette"));
            none.Items.Add(new ResultItem { Id = "palette:none", Kind = ItemKind.Info, Title = Tr("팔레트 없음", "No palette"), Subtitle = Tr("Enter로 영역 고르기", "Press Enter to select region"), Icon = IconSpec.Of(G.Palette), PrimaryLabel = Tr("고르기", "Select"), Run = () => host.StartLens("palette") });
            var empty = new List<Section> { none };
            if (SavedPalettes(null, 5) is { } kept) empty.Add(kept);
            return empty;
        }
        var colors = p.Colors.ConvertAll(x => x.Color);
        int at = Math.Clamp(p.Selected, 0, colors.Count - 1);
        var c = colors[at];
        string hex = ColorBook.Hex(c);
        var sections = new List<Section>
        {
            new(Tr("컬러 팔레트", "Color Palette"), p.Busy ? Tr("색 추출 중…", "Extracting colors…") : Tr($"{colors.Count}색 · 많이 차지한 순", $"{colors.Count} colors · By dominance"))
            {
                Widget = () =>
                {
                    // − n + asks for one fewer or more than the count asked for last, which it shows once its colours come
                    int shown = p.Count;
                    return Ui.PaletteViews.Strip(p.Colors, at, shown, p.Busy, p.CanRemake ? n => _ = host.SetPaletteCount(p.Count + n - shown) : null, i => host.PickSwatch(i, true));
                },
            },
        };
        var picked = new Section(null);
        var now = FromColor(c, "palette:color:" + hex);
        now.Subtitle = $"{ColorBook.Name(c)} · {ColorBook.Rgb(c)} · {Ui.PaletteViews.Percent(p.Colors[at].Share)}";
        now.StayOpen = true;
        now.Run = () => Copied(hex, Tr($"{hex} 복사 완료", $"Copied {hex}"));
        picked.Items.Add(now);
        picked.Items.Add(new ResultItem
        {
            Id = "palette:copy",
            Kind = ItemKind.Command,
            Title = Tr("전체 복사", "Copy all"),
            Subtitle = Tr("HEX 목록 · CSS 변수 · 팔레트 이미지(PNG)", "HEX list · CSS variables · Palette image (PNG)"),
            Icon = IconSpec.Swatch(ColorBook.Stripes(colors)),
            PrimaryLabel = Tr("고르기", "Select"),
            StayOpen = true,
            Run = () => host.Window.Choose(Tr($"전체 복사 · {colors.Count}색", $"Copy All · {colors.Count} colors"), CopyChoices(colors)),
            MoreActions = () => CopyChoices(colors),
        });
        sections.Add(picked);
        sections.AddRange(Suggestions(c));
        if (SavedPalettes(p.Saved, 5) is { } others) sections.Add(others);
        return sections;
    }

    /// 전체 복사's choices: the HEX codes, CSS variables, or the palette as a picture with each HEX under its swatch.
    private List<ActionItem> CopyChoices(List<Color> colors) => new()
    {
        new() { Title = Tr("HEX 목록", "HEX list"), Glyph = G.Copy, StayOpen = true, Run = () => Copied(ColorBook.HexList(colors), Tr($"HEX {colors.Count}개 복사 완료", $"Copied {colors.Count} HEX codes")) },
        new() { Title = Tr("CSS 변수 (--color-1: …)", "CSS variables (--color-1: …)"), Glyph = G.Code, StayOpen = true, Run = () => Copied(ColorBook.CssVariables(colors), Tr($"CSS 변수 {colors.Count}개 복사 완료", $"Copied {colors.Count} CSS variables")) },
        new()
        {
            Title = Tr("팔레트 이미지 (PNG · HEX 표시)", "Palette image (PNG · with HEX)"),
            Glyph = G.Camera,
            StayOpen = true,
            Run = () => host.Window.ShowToast(Launch.Copied(Launch.CopyDrawn(Ui.PaletteViews.Picture(colors)), Tr("팔레트 이미지 복사 완료 · Ctrl+V로 붙여넣으세요", "Palette image copied · Press Ctrl+V to paste"))),
        },
    };

    private void Copied(string text, string toast) => host.Window.ShowToast(Launch.Copied(Launch.Copy(text), toast));

    /// The palettes kept in the colour history (but `except`, the one shown), newest first; Enter opens one again.
    private Section? SavedPalettes(SavedPalette? except, int count)
    {
        var s = new Section(Tr("최근 팔레트", "Recent Palettes"), Tr("Enter 다시 열기", "Enter to reopen"));
        foreach (var saved in ColorBook.Current.Palettes.Where(x => !ReferenceEquals(x, except)).Take(count)) s.Items.Add(FromPalette(saved));
        return s.Items.Count > 0 ? s : null;
    }

    private ResultItem FromPalette(SavedPalette saved)
    {
        var colors = saved.Read().ConvertAll(x => x.Color);
        return new ResultItem
        {
            Id = "palette:saved:" + saved.At.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Kind = ItemKind.Command,
            Title = string.Join("  ", colors.Take(5).Select(ColorBook.Hex)) + (colors.Count > 5 ? "  …" : ""),
            Subtitle = Tr($"{colors.Count}색 · {Ago(saved.At)}", $"{colors.Count} colors · {Ago(saved.At)}"),
            Icon = IconSpec.Swatch(ColorBook.Stripes(colors)),
            PrimaryLabel = Tr("열기", "Open"),
            StayOpen = true,
            Run = () => host.OpenPalette(saved),
            MoreActions = () => CopyChoices(colors),
        };
    }
}
