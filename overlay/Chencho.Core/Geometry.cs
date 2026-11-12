namespace Chencho.Core;

// Plain geometry for StripLayout, independent of WPF.
// Like the rects StripLayout makes, a Rect is assumed to have a non-negative size.

public readonly record struct Point(double X, double Y);

public readonly record struct Size(double Width, double Height);

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MidX => X + Width / 2;
    public double MaxX => X + Width;
    public double MinY => Y;
    public double MidY => Y + Height / 2;
    public double MaxY => Y + Height;

    /// <summary>The min edges are inside, the max edges outside.</summary>
    public bool Contains(Point point) => point.X >= MinX && point.X < MaxX && point.Y >= MinY && point.Y < MaxY;

    /// <summary>Shrunk by <paramref name="dx"/> on the left and right and <paramref name="dy"/> on the top and bottom.</summary>
    public Rect InsetBy(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);

    public Rect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}
