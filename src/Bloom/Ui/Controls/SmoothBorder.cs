using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bloom.Ui;

/// A Border whose rounded corners have continuous curvature (G2): each corner is a superellipse that leaves the edge
/// with no curvature at all and bends without a step, instead of a circular arc meeting the edge at a kink. Layout,
/// padding and hit-testing are Border's own; only the drawing differs. CornerRadius keeps its meaning (the radius a
/// circular corner of the same look would have); a radius of half the short side or more is a pill, its ends round.
internal class SmoothBorder : Border
{
    /// A line drawn just inside the top edge (a button's light, a chosen segment's), clipped to the shape.
    public static readonly DependencyProperty TopLineProperty = DependencyProperty.Register(nameof(TopLine), typeof(Brush), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// A line drawn just inside the bottom edge (a key cap's shade), clipped to the shape.
    public static readonly DependencyProperty BottomLineProperty = DependencyProperty.Register(nameof(BottomLine), typeof(Brush), typeof(SmoothBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? TopLine
    {
        get => (Brush?)GetValue(TopLineProperty);
        set => SetValue(TopLineProperty, value);
    }

    public Brush? BottomLine
    {
        get => (Brush?)GetValue(BottomLineProperty);
        set => SetValue(BottomLineProperty, value);
    }

    private Geometry? fill, stroke;
    private Size builtFor;
    private CornerRadius builtRadius;
    private double builtThickness = -1;

    // Lift drawn in software: the shadow's blur, drop and darkness, and its rings for the size built
    private (double blur, double depth, double opacity)? soft;
    private Geometry[]? rings;
    private const int RingCount = 14;

    /// A floating card's shadow (menus, the 더보기 panel), black, `depth` below it. With the GPU it is a
    /// DropShadowEffect. Drawn in software (GPU로 그리기 off, the default) that blur is worked out again on the CPU for
    /// any change inside the card, a row lit under the pointer too, and for each frame of its growth: about 13 ms a
    /// step and 190 ms an opening in the launcher (popup-perf), the menus lagging behind the pointer. There the card
    /// draws a soft shadow itself instead, rings around its shape fading out, which only a changed part redraws.
    public void Lift(double blur, double depth, double opacity)
    {
        bool software = RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly || RenderCapability.Tier >> 16 == 0;
        if (software)
        {
            soft = (blur, depth, opacity);
            rings = null;
            InvalidateVisual();
        }
        else Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Colors.Black };
    }

    /// The shadow every popup menu and dropdown list has.
    public void LiftMenu() => Lift(26, 6, 0.3);

    /// The drawn shadow is not part of the card: a click on it goes to whatever is under it (outside a popup: closes it).
    protected override HitTestResult? HitTestCore(PointHitTestParameters hit) =>
        soft is not null && !new Rect(RenderSize).Contains(hit.HitPoint) ? null : base.HitTestCore(hit);

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width <= 0 || size.Height <= 0) return;
        var cr = CornerRadius;
        if (cr.TopLeft + cr.TopRight + cr.BottomRight + cr.BottomLeft <= 0 && TopLine is null && BottomLine is null && soft is null)
        {
            base.OnRender(dc);
            return;
        }
        double t = BorderBrush is null ? 0 : BorderThickness.Left;
        if (fill is null || size != builtFor || CornerRadius != builtRadius || t != builtThickness)
        {
            fill = Shape(new Rect(size), CornerRadius, 0);
            stroke = t > 0 ? Shape(new Rect(t / 2, t / 2, Math.Max(0, size.Width - t), Math.Max(0, size.Height - t)), CornerRadius, t / 2) : null;
            builtFor = size;
            builtRadius = CornerRadius;
            builtThickness = t;
            rings = null;
        }
        if (soft is { } s) DrawSoftShadow(dc, size, s);
        if (Background is { } bg) dc.DrawGeometry(bg, null, fill);
        if (TopLine is not null || BottomLine is not null)
        {
            dc.PushClip(fill);
            if (TopLine is { } top) dc.DrawRectangle(top, null, new Rect(0, 0, size.Width, 1));
            if (BottomLine is { } bottom) dc.DrawRectangle(bottom, null, new Rect(0, size.Height - 1, size.Width, 1));
            dc.Pop();
        }
        if (stroke is not null && BorderBrush is { } line) dc.DrawGeometry(null, new Pen(line, t), stroke);
    }

    /// RingCount copies of the shape, `depth` lower, from half the blur narrower to half the blur wider, laid over one
    /// another with the same faint black: where they all overlap it is about `opacity` dark, half that at the card's
    /// edge, fading to nothing at the widest, as a blur would. The card's own background covers the part under it.
    private void DrawSoftShadow(DrawingContext dc, Size size, (double blur, double depth, double opacity) s)
    {
        if (rings is null)
        {
            rings = new Geometry[RingCount];
            double reach = s.blur / 2;
            for (int i = 0; i < RingCount; i++)
            {
                double grow = reach * (1 - 2.0 * i / (RingCount - 1)); // +reach … −reach
                var cr = CornerRadius;
                var r = new Rect(-grow, s.depth - grow, Math.Max(0, size.Width + 2 * grow), Math.Max(0, size.Height + 2 * grow));
                rings[i] = Shape(r, new CornerRadius(cr.TopLeft + grow, cr.TopRight + grow, cr.BottomRight + grow, cr.BottomLeft + grow));
            }
        }
        // each layer's alpha so that RingCount of them come to `opacity`: 1 - (1 - a)^n = opacity
        double a = 1 - Math.Pow(1 - s.opacity, 1.0 / RingCount);
        var ink = RingInk(a);
        foreach (var g in rings) dc.DrawGeometry(ink, null, g);
    }

    private static readonly Dictionary<byte, SolidColorBrush> RingInks = new();

    private static SolidColorBrush RingInk(double alpha)
    {
        byte key = (byte)Math.Round(alpha * 255);
        lock (RingInks)
        {
            if (RingInks.TryGetValue(key, out var hit)) return hit;
            var b = new SolidColorBrush(Color.FromArgb(key, 0, 0, 0));
            b.Freeze();
            RingInks[key] = b;
            return b;
        }
    }

    private static readonly Dictionary<(double w, double h, double tl, double tr, double br, double bl), Geometry> Cache = new();

    /// The outline of `r` with smooth corners (each corner its own radius, less `inset` for a stroke drawn inside).
    public static Geometry Shape(Rect r, CornerRadius radius, double inset = 0)
    {
        double tl = Math.Max(0, radius.TopLeft - inset), tr = Math.Max(0, radius.TopRight - inset), br = Math.Max(0, radius.BottomRight - inset), bl = Math.Max(0, radius.BottomLeft - inset);
        if (tl + tr + br + bl <= 0) return new RectangleGeometry(r);
        var key = (r.Width, r.Height, tl, tr, br, bl);
        lock (Cache)
            if (r.X == 0 && r.Y == 0 && Cache.TryGetValue(key, out var hit)) return hit;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double w = r.Width, h = r.Height, x = r.X, y = r.Y;
            // each corner: where it starts, the direction along the edge (u) and toward the next edge (v)
            var corners = new (Point start, Vector u, Vector v, double radius)[]
            {
                (new Point(x, y + Reach(tl, w, h)), new Vector(0, -1), new Vector(1, 0), tl),
                (new Point(x + w - Reach(tr, w, h), y), new Vector(1, 0), new Vector(0, 1), tr),
                (new Point(x + w, y + h - Reach(br, w, h)), new Vector(0, 1), new Vector(-1, 0), br),
                (new Point(x + Reach(bl, w, h), y + h), new Vector(-1, 0), new Vector(0, -1), bl),
            };
            c.BeginFigure(corners[0].start, true, true);
            foreach (var (start, u, v, rad) in corners)
            {
                c.LineTo(start, true, false);
                Corner(c, start, u, v, rad, w, h);
            }
        }
        g.Freeze();
        lock (Cache)
            if (r.X == 0 && r.Y == 0)
            {
                if (Cache.Count >= 1024) Cache.Clear(); // a window dragged to many sizes
                Cache[key] = g;
            }
        return g;
    }

    // The superellipse |x|^n + |y|^n = 1 with n = 2^1.32 in a box of 1.2 × the radius: the same size as the corner it
    // replaced (Figma's 60 % smoothing), within 0.006 of the radius. Each quarter is two cubics meeting at its middle:
    // the first leaves the edge flat (its control points on the edge, so no curvature there), the second is its mirror,
    // and the mirror makes the curvature the same on both sides of the middle. Off the true curve by 0.00012 of the box.
    private const double Box = 1.2;
    private const double Mid = 0.7575773280750414; // 2^(-1/n): where the curve crosses the box's diagonal
    private const double Near = 0.1706; // the first control point along the edge, fitted
    private const double Far = 2 * Mid - 1; // the second: on the edge, on the tangent through the middle

    /// How far along each edge a corner reaches: the superellipse's box, or a pill's half height.
    private static double Reach(double radius, double w, double h)
    {
        if (radius <= 0) return 0;
        double max = Math.Min(w, h) / 2;
        return radius >= max ? max : Math.Min(radius * Box, max);
    }

    private static void Corner(StreamGeometryContext c, Point start, Vector u, Vector v, double radius, double w, double h)
    {
        if (radius <= 0) return;
        double max = Math.Min(w, h) / 2;
        if (radius >= max)
        {
            // a pill's end (or a circle): a round arc, as a G2 corner there would blunt it beside round knobs
            c.ArcTo(start + max * u + max * v, new Size(max, max), 0, false, SweepDirection.Clockwise, true, true);
            return;
        }
        double b = Math.Min(radius * Box, max);
        Point At(double along, double into) => start + b * along * u + b * into * v;
        c.BezierTo(At(Near, 0), At(Far, 0), At(Mid, 1 - Mid), true, true);
        c.BezierTo(At(1, 1 - Far), At(1, 1 - Near), At(1, 1), true, true);
    }
}
