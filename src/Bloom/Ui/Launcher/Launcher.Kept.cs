using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Core;
using Bloom.Search;
using Run = System.Windows.Documents.Run;

namespace Bloom.Ui;

/// The home kept between openings: the blocks the list still holds stay as they are where they would show the same
/// (rows, layout and icons included, the selection starts over), and a block that changed is drawn anew in its place.
/// Laying the whole home out anew took most of an opening's first frame (measured: 49 ms to the screen, 33 with
/// the home kept). The usage block's "1분 전" and resets change by the minute, so most openings redraw that block only.
/// Blocks taken out and put back were slower than drawn anew (WPF lays a returned block out again, all its rows), so
/// the kept ones never leave the list. A block's key is what it shows: its header, its items' words and icons, a
/// widget's words, colours and sizes, and what its tools are drawn from (its column, fold and rows as set — the same
/// items can show under another count); with what every block hangs on (HomeLook). The theme lets them go (ApplyTheme).
internal sealed partial class Launcher
{
    /// A home block as the list holds it: its key, its box in the list, its rows (in the keyboard's order), its strip.
    private sealed record Block(string Key, string? HomeKey, UIElement Holder, List<Row> Rows, FavStrip? Strip);

    private List<Block>? home; // the home as the list holds it whole (null: something else, or still being drawn)
    private string? homeLook;

    /// What the list's look hangs on besides its sections: the language, the icon style, 홈 편집 with the blocks' order
    /// and columns and whether there are two (the block tools' ▲ ▼ ‹ ›), the preview split and the screen's scale (text
    /// is laid out for it). The columns themselves follow the width as it changes (HomeColumns).
    private string HomeLook()
    {
        bool editing = HomeEditing || ShowHomeToolsForQa;
        return $"{Lang.Code}|{Settings.Current.IconStyle}|{editing}|{Home.Arrangement()}|{editing && HomeWide()}|{Split}|{VisualTreeHelper.GetDpi(this).PixelsPerDip}";
    }

    /// A section's key. A widget is made here to be read (its words, colours and sizes): `widget` is that one, for the
    /// section if it is drawn anew.
    internal static string SectionKey(Section s, out FrameworkElement? widget)
    {
        var b = new StringBuilder().Append('\u001d').Append(s.Title).Append('\u001f').Append(s.Note).Append('\u001f').Append(s.HomeKey)
            .Append('\u001f').Append(s.Strip).Append(s.Fallback).Append(s.Reload is null ? "" : "↻").Append(s.HomeRight).Append(s.Collapsed)
            .Append('\u001f').Append(s.HomeCount);
        widget = s.Widget?.Invoke();
        if (widget is not null) Look(b, widget);
        foreach (var i in s.Items)
        {
            b.Append('\u001e').Append(i.Id).Append('\u001f').Append(i.Kind).Append('\u001f').Append(i.Title).Append('\u001f').Append(i.Subtitle)
                .Append('\u001f').Append(i.Accessory).Append('\u001f').Append(i.Live is null ? "" : "live").Append('\u001f').Append(i.PrimaryLabel)
                .Append('\u001f').Append(i.StayOpen).Append(i.Pinnable).Append('\u001f').Append(i.Side?.Title).Append(i.Side?.Glyph)
                .Append(i.Remove is null ? "" : "✕");
            IconKey(b, i.Icon);
            // a favorites folder's tile shows its first items' icons
            if (FavStrip.FolderKids(i) is { } kids)
            {
                b.Append('\u001f').Append(kids.Count);
                foreach (var k in FavStrip.FolderShown(kids)) IconKey(b, k.Icon);
            }
        }
        return b.ToString();
    }

    private static void IconKey(StringBuilder b, IconSpec c) =>
        b.Append('\u001f').Append(c.ShellPath).Append('|').Append(c.CacheKey).Append('|').Append(c.Glyph).Append(c.Accent).Append('|').Append(c.Mono)
            .Append('|').Append(Paint(c.Fill)).Append('|').Append(c.Text).Append(c.Emoji).Append('|').Append(c.Area);

    /// What a built element shows, as words: each element's kind, place, size and visibility, its words and colours,
    /// its tooltip, a grid's columns and rows (a usage bar's fill). Two widgets alike here look alike on screen.
    private static void Look(StringBuilder b, object node)
    {
        b.Append('<').Append(node.GetType().Name);
        if (node is FrameworkElement fe)
        {
            b.Append(' ').Append(fe.Visibility).Append(' ').Append(fe.Margin).Append(' ').Append(fe.Width).Append('x').Append(fe.Height)
                .Append(' ').Append(Grid.GetRow(fe)).Append(',').Append(Grid.GetColumn(fe)).Append(',').Append(Grid.GetColumnSpan(fe));
            if (fe.ToolTip is string tip) b.Append(" tip:").Append(tip);
        }
        switch (node)
        {
            case TextBlock t:
                b.Append(Paint(t.Foreground)).Append(' ').Append(t.FontSize).Append(t.FontWeight);
                if (t.Inlines.Count == 0) b.Append(" \"").Append(t.Text).Append('"');
                break;
            case Run r:
                b.Append(" \"").Append(r.Text).Append('"').Append(Paint(r.Foreground)).Append(' ').Append(r.FontSize).Append(r.FontWeight);
                break;
            case Border border:
                b.Append(Paint(border.Background)).Append(Paint(border.BorderBrush));
                break;
            case Panel panel:
                b.Append(Paint(panel.Background));
                break;
        }
        if (node is Grid g)
        {
            foreach (var c in g.ColumnDefinitions) b.Append(" c").Append(c.Width);
            foreach (var r in g.RowDefinitions) b.Append(" r").Append(r.Height);
        }
        if (node is DependencyObject d)
            foreach (object child in LogicalTreeHelper.GetChildren(d))
                Look(b, child);
        b.Append('>');
    }

    private static string Paint(Brush? brush) => brush switch
    {
        null => "",
        SolidColorBrush s => " " + s.Color,
        _ => " " + brush.GetType().Name,
    };

    /// The home the list holds, brought up to date in place (Render): false when it can't be (another home, blocks
    /// in another order, more new rows than come at once — in two columns, twice the rows show), then the home is drawn
    /// anew. `made`: widgets made for the keys.
    private bool KeepHome(List<Section> shown, string look, List<string> keys, Dictionary<Section, FrameworkElement> made)
    {
        if (home is null || homeLook != look || home.Count != shown.Count || list.Children.Count != 1 || homeGrid.Parent != list || homeGrid.Children.Count != home.Count) return false;
        int build = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            if (home[i].Key == keys[i]) continue;
            if (home[i].HomeKey != shown[i].HomeKey) return false;
            if (shown[i].Widget is null && !shown[i].Strip) build += shown[i].Items.Count;
        }
        if (build > (opening ? ScreenRows() : FirstRows) * (HomeWide() ? 2 : 1)) return false;
        for (int i = 0; i < shown.Count; i++)
        {
            var b = home[i];
            if (b.Key == keys[i])
            {
                // the same look, this opening's items (rows: the section's items in order, as drawn)
                for (int k = 0; k < b.Rows.Count; k++) b.Rows[k].Reuse(shown[i].Items[k]);
                b.Strip?.Reuse(shown[i].Items);
                continue;
            }
            int before = rows.Count;
            var into = StartSection(shown[i], null, made.GetValueOrDefault(shown[i]), out _, out var holder);
            if (shown[i].Widget is null && !shown[i].Strip)
                foreach (var item in shown[i].Items) into.Children.Add(AddRow(item));
            // into the old one's place (StartSection put it last)
            homeGrid.Children.Remove(holder);
            int at = homeGrid.Children.IndexOf(b.Holder);
            homeGrid.Children.RemoveAt(at);
            homeGrid.Children.Insert(at, holder!);
            home[i] = new Block(keys[i], shown[i].HomeKey, holder!, rows.GetRange(before, rows.Count - before), shown[i].Strip ? strip : null);
        }
        rows.Clear();
        strip = null;
        foreach (var b in home)
        {
            if (b.Strip is not null && strip is null) (strip, stripAt) = (b.Strip, rows.Count);
            rows.AddRange(b.Rows);
        }
        return true;
    }
}
