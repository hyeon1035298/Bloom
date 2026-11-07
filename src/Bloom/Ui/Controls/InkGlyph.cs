using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Bloom.Ui;

/// An icon glyph (Segoe Fluent Icons, Theme.Glyph) with its ink, not its character box, in the middle of the space
/// it is given. The box is the glyph's advance by the font's line height, and each icon sits in it a little off its own
/// way: up and left by a dip or so in the 34-dip tiles of the notice cards, enough to see. On each axis the glyph is
/// centred on (Center alignment, no fixed size there, left-aligned text) the middle of its outline goes onto the middle
/// of its layout slot (the room its parent gave it, margins left out), so a glyph in a tile, a button or beside a label
/// is centred by what is drawn. The move is exact, not rounded to device pixels as InkText's is: a whole-pixel move left
/// up to half a pixel at 100 %, and more in a scaled card (큰 알림 is 1.2×) or a picture drawn at 2×.
internal sealed class InkGlyph : TextBlock
{
    private static readonly Dictionary<(string font, double size, string text), Rect> Ink = new();
    private readonly TranslateTransform shift = new();
    private Rect placedSlot = Rect.Empty;
    private Vector placedOffset;

    static InkGlyph()
    {
        // TextBlock seals OnPropertyChanged: a new glyph or size moves the ink through these instead
        foreach (var p in new[] { TextProperty, FontSizeProperty, FontFamilyProperty })
            p.OverrideMetadata(typeof(InkGlyph), new FrameworkPropertyMetadata((d, _) => ((InkGlyph)d).Place()));
    }

    public InkGlyph()
    {
        RenderTransform = shift;
        // the slot or the rounded place in it can change with the glyph's size the same (a parent resized)
        Loaded += (_, _) =>
        {
            LayoutUpdated -= Moved;
            LayoutUpdated += Moved;
            Place();
        };
        Unloaded += (_, _) => LayoutUpdated -= Moved;
    }

    private void Moved(object? sender, EventArgs e)
    {
        if (LayoutInformation.GetLayoutSlot(this) != placedSlot || VisualTreeHelper.GetOffset(this) != placedOffset) Place();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Place();
    }

    private void Place()
    {
        if (RenderTransform != shift) return; // a caller's own transform: left as it is
        double dx = 0, dy = 0;
        placedSlot = LayoutInformation.GetLayoutSlot(this);
        placedOffset = VisualTreeHelper.GetOffset(this);
        bool across = HorizontalAlignment == HorizontalAlignment.Center && double.IsNaN(Width) && TextAlignment == TextAlignment.Left;
        bool down = VerticalAlignment == VerticalAlignment.Center && double.IsNaN(Height);
        if (Text.Length > 0 && RenderSize.Width > 0 && (across || down) && !placedSlot.IsEmpty)
        {
            var ink = Outline();
            if (!ink.IsEmpty)
            {
                // the slot's middle from the glyph's origin, in its own dips
                double cx = placedSlot.X + Margin.Left + Math.Max(0, placedSlot.Width - Margin.Left - Margin.Right) / 2 - placedOffset.X - Padding.Left;
                double cy = placedSlot.Y + Margin.Top + Math.Max(0, placedSlot.Height - Margin.Top - Margin.Bottom) / 2 - placedOffset.Y - Padding.Top;
                if (across) dx = Math.Round(cx - (ink.X + ink.Width / 2), 3);
                if (down) dy = Math.Round(cy - (ink.Y + ink.Height / 2), 3);
            }
        }
        if (shift.X != dx) shift.X = dx;
        if (shift.Y != dy) shift.Y = dy;
    }

    /// The glyph's outline box in dips from its origin (kept per glyph and size).
    private Rect Outline()
    {
        var key = (FontFamily.Source, FontSize, Text);
        if (Ink.TryGetValue(key, out var ink)) return ink;
        var ft = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.Black, 1.0);
        ink = ft.BuildGeometry(new Point(0, 0)).Bounds;
        if (Ink.Count > 2000) Ink.Clear();
        Ink[key] = ink;
        return ink;
    }
}
