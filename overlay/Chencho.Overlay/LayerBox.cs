using System;
using System.Windows;
using System.Windows.Media;

namespace Chencho.Overlay;

[Flags]
internal enum Corners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}

/// A rounded box whose fill covers the whole rect, with the border painted over it, inside the bounds.
/// (A WPF Border keeps its background out from under the border, which shows through a translucent line.)
internal sealed class LayerBox : FrameworkElement
{
    private Color? fill;
    private Color? border;
    private double borderWidth;
    private double radius;
    private Corners corners = Corners.All;

    public LayerBox()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    public void Paint(Color? fill, Color? border, double borderWidth)
    {
        if (this.fill == fill && this.border == border && this.borderWidth == borderWidth) return;
        this.fill = fill;
        this.border = border;
        this.borderWidth = borderWidth;
        InvalidateVisual();
    }

    public void Shape(double radius, Corners corners)
    {
        if (this.radius == radius && this.corners == corners) return;
        this.radius = radius;
        this.corners = corners;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (fill is { A: > 0 } f) context.DrawGeometry(Theme.Brush(f), null, RoundedRect(bounds, radius, corners));
        if (border is { A: > 0 } b && borderWidth > 0)
        {
            var inner = new Rect(bounds.X + borderWidth, bounds.Y + borderWidth,
                Math.Max(0, bounds.Width - 2 * borderWidth), Math.Max(0, bounds.Height - 2 * borderWidth));
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(RoundedRect(bounds, radius, corners));
            // The inner edge rounds with radius − width / 2 rather than radius − width, for a softer inner corner.
            ring.Children.Add(RoundedRect(inner, Math.Max(0, radius - borderWidth / 2), corners));
            ring.Freeze();
            context.DrawGeometry(Theme.Brush(b), null, ring);
        }
    }

    public static Geometry RoundedRect(Rect rect, double radius, Corners corners)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        double Corner(Corners c) => (corners & c) != 0 ? r : 0;
        var tl = Corner(Corners.TopLeft);
        var tr = Corner(Corners.TopRight);
        var br = Corner(Corners.BottomRight);
        var bl = Corner(Corners.BottomLeft);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(rect.Left + tl, rect.Top), true, true);
            g.LineTo(new Point(rect.Right - tr, rect.Top), true, false);
            if (tr > 0) g.ArcTo(new Point(rect.Right, rect.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new Point(rect.Right, rect.Bottom - br), true, false);
            if (br > 0) g.ArcTo(new Point(rect.Right - br, rect.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new Point(rect.Left + bl, rect.Bottom), true, false);
            if (bl > 0) g.ArcTo(new Point(rect.Left, rect.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new Point(rect.Left, rect.Top + tl), true, false);
            if (tl > 0) g.ArcTo(new Point(rect.Left + tl, rect.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
