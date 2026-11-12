namespace Chencho.Core.Tests;

public class StripLayoutTests
{
    private static readonly Rect Screen = new(0, 0, 1440, 900);

    private static Rect Bounds(StripLayout layout) => new(0, 0, layout.Size.Width, layout.Size.Height);

    [Fact(DisplayName = "creatures are as big as the screen allows, in steps that land on whole pixels at 200% scaling")]
    public void FittingScale()
    {
        Assert.Equal(2, StripLayout.Fitting(3, 900).Scale);
        Assert.Equal(1.5, StripLayout.Fitting(20, 900).Scale);
        Assert.Equal(1, StripLayout.Fitting(40, 900).Scale);
    }

    [Fact(DisplayName = "the strip is one cell wide and one cell tall per creature, plus its inset; the tab sits beside it")]
    public void Sizes()
    {
        var layout = StripLayout.Fitting(3, 900);
        Assert.Equal(56, layout.CellSide);
        Assert.Equal(new Size(56 + 2 * StripLayout.Inset, 3 * 56 + 2 * StripLayout.Inset), layout.StripSize);
        Assert.Equal(new Size(16, 42), layout.TabSize);
        Assert.Equal(new Size(layout.StripSize.Width + layout.TabSize.Width, layout.StripSize.Height), layout.Size);
    }

    [Fact(DisplayName = "inside its frame, the strip sits against the edge and the tab at mid-height on its inner side")]
    public void StripAndTabInFrame()
    {
        var layout = StripLayout.Fitting(3, 900);
        var bounds = Bounds(layout);
        Assert.Equal(new Rect(16, 0, 64, 176), layout.StripRect(bounds, ScreenEdge.Right));
        Assert.Equal(new Rect(0, 67, 16, 42), layout.TabRect(bounds, ScreenEdge.Right, collapsed: false));
        Assert.Equal(new Rect(0, 0, 64, 176), layout.StripRect(bounds, ScreenEdge.Left));
        Assert.Equal(new Rect(64, 67, 16, 42), layout.TabRect(bounds, ScreenEdge.Left, collapsed: false));
    }

    [Fact(DisplayName = "collapsing slides the strip past the edge, leaving only the tab against it")]
    public void Collapsing()
    {
        var layout = StripLayout.Fitting(3, 900);
        var bounds = Bounds(layout);
        Assert.Equal(64, layout.CollapseOffset(ScreenEdge.Right));
        Assert.Equal(-64, layout.CollapseOffset(ScreenEdge.Left));
        Assert.Equal(new Rect(64, 67, 16, 42), layout.TabRect(bounds, ScreenEdge.Right, collapsed: true));
        Assert.Equal(new Rect(0, 67, 16, 42), layout.TabRect(bounds, ScreenEdge.Left, collapsed: true));
    }

    [Fact(DisplayName = "docks to the middle of the chosen edge")]
    public void Docks()
    {
        var layout = StripLayout.Fitting(3, Screen.Height);
        var right = layout.Frame(Screen, ScreenEdge.Right);
        Assert.Equal(Screen.MaxX, right.MaxX);
        Assert.Equal(Screen.MidY, right.MidY);
        Assert.Equal(layout.Size, new Size(right.Width, right.Height));
        var left = layout.Frame(Screen.OffsetBy(100, 40), ScreenEdge.Left);
        Assert.Equal(100, left.MinX);
        Assert.Equal(Screen.MidY + 40, left.MidY);
    }

    [Fact(DisplayName = "a strip taller than the screen is cut to the screen")]
    public void CutToScreen()
    {
        var frame = StripLayout.Fitting(40, 900).Frame(Screen, ScreenEdge.Right);
        Assert.Equal(Screen.MinY, frame.MinY);
        Assert.Equal(Screen.Height, frame.Height);
    }

    [Fact(DisplayName = "finds the creature or the tab under a point")]
    public void HitTesting()
    {
        var layout = StripLayout.Fitting(3, 900);
        var bounds = Bounds(layout);
        const double inset = StripLayout.Inset;
        StripHit? Hit(double x, double fromTop, ScreenEdge edge = ScreenEdge.Right, bool collapsed = false) =>
            layout.Hit(new Point(x, bounds.MaxY - fromTop), bounds, edge, collapsed);

        Assert.Equal(new StripHit.Cell(0), Hit(40, fromTop: inset));
        Assert.Equal(new StripHit.Cell(0), Hit(40, fromTop: inset + 55.9));
        Assert.Equal(new StripHit.Cell(1), Hit(40, fromTop: inset + 56));
        Assert.Equal(new StripHit.Cell(2), Hit(40, fromTop: inset + 3 * 56 - 1));
        Assert.Null(Hit(40, fromTop: inset - 1));
        Assert.Null(Hit(40, fromTop: inset + 3 * 56));
        Assert.Equal(new StripHit.Tab(), Hit(8, fromTop: 88));
        Assert.Null(Hit(8, fromTop: 20));
        Assert.Equal(new StripHit.Tab(), Hit(72, fromTop: 88, edge: ScreenEdge.Left));
        Assert.Equal(new StripHit.Cell(0), Hit(30, fromTop: inset, edge: ScreenEdge.Left));

        Assert.Equal(new StripHit.Tab(), Hit(72, fromTop: 88, collapsed: true));
        Assert.Null(Hit(40, fromTop: inset, collapsed: true));
        Assert.Null(Hit(8, fromTop: 88, collapsed: true));
        Assert.Equal(new StripHit.Tab(), Hit(8, fromTop: 88, edge: ScreenEdge.Left, collapsed: true));
    }

    [Fact(DisplayName = "places a cell, and its label facing into the screen, clear of the tab")]
    public void CellAndLabel()
    {
        var layout = StripLayout.Fitting(3, 900);
        var frame = layout.Frame(Screen, ScreenEdge.Right);
        var strip = layout.StripRect(frame, ScreenEdge.Right);
        var cell = layout.CellFrame(1, strip);
        Assert.Equal(new Rect(strip.MinX + StripLayout.Inset, strip.MaxY - StripLayout.Inset - 2 * 56, 56, 56), cell);

        var label = new Size(200, 40);
        var onRight = StripLayout.LabelFrame(label, cell, frame, ScreenEdge.Right);
        Assert.Equal(frame.MinX - StripLayout.LabelGap, onRight.MaxX);
        Assert.Equal(cell.MidY, onRight.MidY);
        var leftFrame = layout.Frame(Screen, ScreenEdge.Left);
        var leftCell = layout.CellFrame(1, layout.StripRect(leftFrame, ScreenEdge.Left));
        var onLeft = StripLayout.LabelFrame(label, leftCell, leftFrame, ScreenEdge.Left);
        Assert.Equal(leftFrame.MaxX + StripLayout.LabelGap, onLeft.MinX);
    }

    [Fact(DisplayName = "inside a cell, the tile leaves one sprite pixel of margin and the sprite sits centred")]
    public void TileAndSpriteInCell()
    {
        var layout = StripLayout.Fitting(3, 900);
        var cell = new Rect(10, 20, 56, 56);
        Assert.Equal(new Rect(12, 22, 52, 52), layout.TileRect(cell));
        Assert.Equal(new Rect(14, 24, 48, 48), layout.SpriteRect(cell));
    }

    [Fact(DisplayName = "edges keep their wire names")]
    public void EdgeWireNames()
    {
        Assert.Equal("left", ScreenEdge.Left.WireName());
        Assert.Equal("right", ScreenEdge.Right.WireName());
        Assert.Equal(ScreenEdge.Left, ScreenEdgeExtensions.FromWireName("left"));
        Assert.Equal(ScreenEdge.Right, ScreenEdgeExtensions.FromWireName("right"));
        Assert.Null(ScreenEdgeExtensions.FromWireName("top"));
    }
}
