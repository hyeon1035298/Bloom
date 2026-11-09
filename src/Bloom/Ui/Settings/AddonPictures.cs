using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Addons;

namespace Bloom.Ui;

/// The pictures of the add-on pages (설정 > 애드온): an option's (PictureChoice) and a switch's thumb on its row's left,
/// as the add-on's schema ships them (AddonPicture, drawn by Pic); null where it has none. And the two of Bloom's own
/// usage settings the taskbar shows (아이콘 색, 이름표).
internal static class AddonPictures
{
    private static readonly Color Claude = Color.FromRgb(0xD9, 0x77, 0x57), Gpt = Color.FromRgb(0x10, 0xA3, 0x7F);

    /// An option's picture, by the setting's key and the option's value, or a page's own choice ("size", "clean.strength").
    public static Func<bool, FrameworkElement>? Option(AddonSchema? schema, string key, string value) =>
        schema?.Picture(key, value) is { } p ? on => Pic.Draw(p, on) : null;

    /// A switch's picture on its row's left (null: none).
    public static Func<bool, FrameworkElement>? Thumb(AddonSetting? item) =>
        item?.Easy?.Thumb is { } p ? on => Pic.Draw(p, on) : null;

    /// 아이콘 색 (Settings.UsageLogo): the two logos in their colours, or both in the text colour.
    public static Func<bool, FrameworkElement> Logo(string value) => value == "mono"
        ? on => new Pic(on).Dot(26, 22, 8, 0.45).Dot(46, 22, 8, 0.45).Done()
        : on => new Pic(on).Dot(26, 22, 8, color: Claude).Dot(46, 22, 8, color: Gpt).Done();

    /// 이름표 (Settings.UsageTag): the logo with no word, the account's name or its plan, then its two bars.
    public static Func<bool, FrameworkElement> Tag(string value) => value switch
    {
        "name" => on => Bars(new Pic(on).Dot(7, 22, 4.5, color: Claude).Text("Claude", 29, 22, 8.5, width: 34), 48, 20).Done(),
        "plan" => on => Bars(new Pic(on).Dot(9, 22, 4.5, color: Claude).Text("Max", 26, 22, 8.5, width: 22), 41, 24).Done(),
        _ => on => Bars(new Pic(on).Dot(16, 22, 5, color: Claude), 26, 36).Done(),
    };

    /// A switch's thumb: the picture in a faint tile, 80 × 50.
    public static FrameworkElement Tile(Func<bool, FrameworkElement> draw, bool on) => new SmoothBorder
    {
        Width = 80,
        Height = 50,
        CornerRadius = new CornerRadius(8),
        Background = Theme.P.Card,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Viewbox { Width = 74, Height = 44, Stretch = Stretch.Uniform, Child = draw(on) },
    };

    /// A usage logo's two bars from `x`, `w` long.
    private static Pic Bars(Pic p, double x, double w) =>
        p.Box(x, 17, w, 3.5, 1.75, 0.18).Acc(x, 17, Math.Round(w * 0.6), 3.5, 1.75).Box(x, 24, w, 3.5, 1.75, 0.18).Acc(x, 24, Math.Round(w * 0.3), 3.5, 1.75);
}
