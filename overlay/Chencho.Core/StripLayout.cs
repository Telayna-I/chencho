namespace Chencho.Core;

public enum ScreenEdge { Left, Right }

public static class ScreenEdgeExtensions
{
    /// <summary><c>left</c> or <c>right</c>.</summary>
    public static string WireName(this ScreenEdge edge) => edge == ScreenEdge.Right ? "right" : "left";

    /// <summary>Null for anything but <c>left</c> and <c>right</c>.</summary>
    public static ScreenEdge? FromWireName(string? name) => name switch
    {
        "left" => ScreenEdge.Left,
        "right" => ScreenEdge.Right,
        _ => null,
    };
}

public abstract record StripHit
{
    private StripHit() { }

    public sealed record Cell(int Index) : StripHit;

    public sealed record Tab : StripHit;
}

/// <summary>Points, with a bottom-left origin (y up) unless noted.</summary>
/// <remarks>
/// The frame holds the strip against the screen edge and the tab beside it, on the strip's inner side.
/// Collapsing slides both toward the edge by <see cref="CollapseOffset"/>, which leaves only the tab on screen.
/// </remarks>
/// <param name="Scale">Points per sprite pixel.</param>
public sealed record StripLayout(int Count, double Scale)
{
    public const double Inset = 4;
    public const double LabelGap = 6;

    /// <summary>Sprite pixels.</summary>
    public static (int Width, int Height) TabPixels => (8, 21);

    private const int CellPaddingPixels = 2;
    private const double CellPixels = Sprite.Size + 2 * CellPaddingPixels;

    /// <summary>Halves keep every sprite pixel on whole device pixels at 200% display scaling.</summary>
    private static readonly double[] Scales = [2, 1.5, 1];

    public double CellSide => Scale * CellPixels;

    public Size StripSize => new(CellSide + 2 * Inset, Count * CellSide + 2 * Inset);

    public Size TabSize => new(TabPixels.Width * Scale, TabPixels.Height * Scale);

    public Size Size => new(StripSize.Width + TabSize.Width, StripSize.Height);

    /// <summary>Past the smallest scale the strip overflows, and <see cref="Frame"/> cuts it to the screen.</summary>
    public static StripLayout Fitting(int count, double height)
    {
        var pixels = count * CellPixels;
        foreach (var scale in Scales)
        {
            if (pixels * scale + 2 * Inset <= height) return new StripLayout(count, scale);
        }
        return new StripLayout(count, Scales[^1]);
    }

    public Rect Frame(Rect visible, ScreenEdge edge)
    {
        var height = Math.Min(Size.Height, visible.Height);
        var x = edge == ScreenEdge.Right ? visible.MaxX - Size.Width : visible.MinX;
        return new Rect(x, visible.MidY - height / 2, Size.Width, height);
    }

    public Rect StripRect(Rect frame, ScreenEdge edge)
    {
        var x = edge == ScreenEdge.Right ? frame.MaxX - StripSize.Width : frame.MinX;
        return new Rect(x, frame.MinY, StripSize.Width, frame.Height);
    }

    public Rect TabRect(Rect frame, ScreenEdge edge, bool collapsed)
    {
        var x = edge == ScreenEdge.Right ? frame.MinX : frame.MaxX - TabSize.Width;
        var open = new Rect(x, Math.Floor(frame.MidY - TabSize.Height / 2), TabSize.Width, TabSize.Height);
        return collapsed ? open.OffsetBy(CollapseOffset(edge), 0) : open;
    }

    public double CollapseOffset(ScreenEdge edge) => edge == ScreenEdge.Right ? StripSize.Width : -StripSize.Width;

    public StripHit? Hit(Point point, Rect frame, ScreenEdge edge, bool collapsed)
    {
        if (TabRect(frame, edge, collapsed).Contains(point)) return new StripHit.Tab();
        if (collapsed || !StripRect(frame, edge).Contains(point)) return null;
        return IndexAt(frame.MaxY - point.Y) is { } index ? new StripHit.Cell(index) : null;
    }

    /// <summary><paramref name="y"/> is measured down from the strip's top edge.</summary>
    private int? IndexAt(double y)
    {
        if (y < Inset) return null;
        var index = (int)((y - Inset) / CellSide);
        return index < Count ? index : null;
    }

    public Rect TileRect(Rect cell) => cell.InsetBy(Scale, Scale);

    public Rect SpriteRect(Rect cell)
    {
        var inset = CellPaddingPixels * Scale;
        return cell.InsetBy(inset, inset);
    }

    public Rect CellFrame(int index, Rect strip) =>
        new(strip.MinX + Inset, strip.MaxY - Inset - (index + 1) * CellSide, CellSide, CellSide);

    /// <summary>Beside <paramref name="beside"/>'s middle, facing into the screen and clear of <paramref name="outside"/>.</summary>
    public static Rect LabelFrame(Size size, Rect beside, Rect outside, ScreenEdge edge)
    {
        var x = edge == ScreenEdge.Right ? outside.MinX - LabelGap - size.Width : outside.MaxX + LabelGap;
        return new Rect(x, beside.MidY - size.Height / 2, size.Width, size.Height);
    }
}
