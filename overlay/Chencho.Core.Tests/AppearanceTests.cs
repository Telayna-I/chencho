using static Chencho.Core.Tests.Fixtures;

namespace Chencho.Core.Tests;

public class AppearanceTests
{
    private static readonly Rgb Black = new(0, 0, 0);

    private static CreatureAppearance Look(PanelSession s, bool focused = false, bool hovered = false, long frame = 1) =>
        CreatureAppearance.For(s, focused, hovered, frame, Now);

    [Fact(DisplayName = "a bare tile has a 1.5-point border width, unlike default(Tile)")]
    public void BareTileBorderWidth()
    {
        Assert.Equal(1.5, new Tile().BorderWidth);
        Assert.Equal(1.5, new Tile(Fill: Paint.White(0.12)).BorderWidth);
        Assert.Equal(new Tile(null, null, 1.5), new Tile());
    }

    [Fact(DisplayName = "a plain session is its sprite on a bare tile")]
    public void PlainSession()
    {
        var s = Session(state: SessionState.Idle);
        var a = Look(s);
        Assert.Equal(new Tile(), a.Tile);
        Assert.Equal(1.0, a.Opacity);
        Assert.Equal(Sprite.Frame(s.Icon.Character, s.Icon.Tint, Mood.Idle, 1), a.Pixels);
    }

    [Fact(DisplayName = "hover tints the tile; focus tints it more and rings it")]
    public void HoverAndFocus()
    {
        Assert.Equal(new Tile(Fill: Paint.White(0.12)), Look(Session(state: SessionState.Idle), hovered: true).Tile);
        Assert.Equal(
            new Tile(Fill: Paint.White(0.22), Border: Paint.White(0.75)),
            Look(Session(state: SessionState.Idle), focused: true).Tile);
        Assert.Equal(Paint.White(0.3), Look(Session(state: SessionState.Idle), focused: true, hovered: true).Tile.Fill);
    }

    [Fact(DisplayName = "a permission request blinks the tile in accent once a second, even when focused")]
    public void PermissionBlinks()
    {
        foreach (var focused in new[] { false, true })
        {
            var lit = Look(Session(state: SessionState.NeedsPermission), focused: focused, frame: 0).Tile;
            var dim = Look(Session(state: SessionState.NeedsPermission), focused: focused, frame: 2).Tile;
            Assert.Equal(new Tile(Fill: Paint.Accent, Border: Paint.Accent, BorderWidth: 2), lit);
            Assert.Equal(new Tile(Fill: new Paint(Paint.Accent.Rgb, 0.25), Border: Paint.Accent, BorderWidth: 2), dim);
            Assert.Equal(lit, Look(Session(state: SessionState.NeedsPermission), focused: focused, frame: 4).Tile);
        }
    }

    [Fact(DisplayName = "waiting for input rings the tile in accent and pulses its fill")]
    public void InputPulses()
    {
        var pulse = Enumerable.Range(0, 8).Select(f => Look(Session(state: SessionState.NeedsInput), frame: f).Tile).ToArray();
        Assert.All(pulse, tile => Assert.True(tile.Border == Paint.Accent && tile.BorderWidth == 2));
        Assert.Equal(new Paint(Paint.Accent.Rgb, 0.35), pulse[0].Fill);
        Assert.Equal(new Paint(Paint.Accent.Rgb, 0.2), pulse[4].Fill);
    }

    [Fact(DisplayName = "a finished session is ringed in green until it dozes off, even when it's in front")]
    public void FinishedRing()
    {
        Assert.Equal(new Tile(Border: Paint.Ready), Look(Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60))).Tile);
        Assert.Equal(
            new Tile(Fill: Paint.White(0.22), Border: Paint.Ready),
            Look(Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60)), focused: true).Tile);
        Assert.Equal(new Tile(), Look(Session(state: SessionState.Done, stateSince: Now.AddSeconds(-6 * 60))).Tile);
    }

    [Fact(DisplayName = "errors ring the tile in the danger color")]
    public void ErrorRing()
    {
        Assert.Equal(Paint.Danger, Look(Session(state: SessionState.Error)).Tile.Border);
    }

    [Fact(DisplayName = "a creature on an accent tile gets a black outline around its sprite")]
    public void Outline()
    {
        var s = Session(state: SessionState.NeedsPermission);
        var plain = Sprite.Frame(s.Icon.Character, s.Icon.Tint, Mood.Alert, 1);
        var outlined = Look(s).Pixels;
        for (var y = 0; y < Sprite.Size; y++)
        {
            for (var x = 0; x < Sprite.Size; x++)
            {
                if (plain[x, y] is { } c)
                {
                    Assert.Equal(c, outlined[x, y]);
                    continue;
                }
                var touches = new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) }.Any(n =>
                    n.Item1 is >= 0 and < Sprite.Size && n.Item2 is >= 0 and < Sprite.Size && plain[n.Item1, n.Item2] is not null);
                Assert.Equal(touches ? Black : null, outlined[x, y]);
            }
        }
        Assert.False(Look(Session(state: SessionState.Done)).Pixels.Contains(Black), "no outline on a bare tile");
    }

    [Fact(DisplayName = "a closed session's creature is grey and faded")]
    public void ClosedIsGrey()
    {
        var a = Look(Session(state: SessionState.Ended, live: false));
        Assert.Equal(0.45, a.Opacity);
        Assert.All(a.Pixels.Cells().OfType<Rgb>(), c => Assert.True(c.R == c.G && c.G == c.B));
        Assert.Contains(a.Pixels.Cells(), c => c is not null);
    }

    [Fact(DisplayName = "the add button is a plus in the middle of its tile, which hover lights")]
    public void AddButton()
    {
        var plain = CreatureAppearance.AddButton(hovered: false);
        Assert.Equal(new Tile(), plain.Tile);
        Assert.Equal(1.0, plain.Opacity);
        Assert.Equal(new Tile(Fill: Paint.White(0.12)), CreatureAppearance.AddButton(hovered: true).Tile);

        var lit = new HashSet<(int X, int Y)>();
        for (var y = 0; y < plain.Pixels.Height; y++)
        {
            for (var x = 0; x < plain.Pixels.Width; x++)
            {
                if (plain.Pixels[x, y] is not null) lit.Add((x, y));
            }
        }
        Assert.True(lit.Contains((11, 11)) && lit.Contains((12, 12)), "the centre is lit");
        Assert.True(lit.All(p => lit.Contains((23 - p.X, p.Y)) && lit.Contains((p.X, 23 - p.Y))), "centred both ways");
        Assert.True(lit.All(p => p.X == 11 || p.X == 12 || p.Y == 11 || p.Y == 12), "only the two arms");
    }

    [Fact(DisplayName = "the tab's arrow points toward the edge while open, and back into the screen while collapsed")]
    public void TabArrow()
    {
        static Pixels Arrow(ScreenEdge edge, bool collapsed) =>
            CreatureAppearance.Tab([], collapsed, edge, hovered: false, frame: 1, Now).Pixels;

        static ScreenEdge? Pointing(Pixels pixels)
        {
            var lit = (from y in Enumerable.Range(0, pixels.Height)
                       from x in Enumerable.Range(0, pixels.Width)
                       where pixels[x, y] is not null
                       select (X: x, Y: y)).ToList();
            var tip = lit.Where(p => p.Y == pixels.Height / 2).Select(p => p.X).ToList();
            var (minX, maxX) = (lit.Min(p => p.X), lit.Max(p => p.X));
            if (tip.Max() == maxX && tip.Min() > minX) return ScreenEdge.Right;
            if (tip.Min() == minX && tip.Max() < maxX) return ScreenEdge.Left;
            return null;
        }

        static Pixels Reversed(Pixels pixels)
        {
            var reversed = Pixels.Empty(pixels.Width, pixels.Height);
            for (var y = 0; y < pixels.Height; y++)
            {
                for (var x = 0; x < pixels.Width; x++) reversed[x, pixels.Height - 1 - y] = pixels[x, y];
            }
            return reversed;
        }

        var open = Arrow(ScreenEdge.Right, collapsed: false);
        Assert.Equal(21, open.Height);
        Assert.True(open.Width == 8, "fills the 8×21 tab");
        Assert.Equal(open, Reversed(open));
        Assert.Equal(ScreenEdge.Right, Pointing(open));
        Assert.Equal(ScreenEdge.Left, Pointing(Arrow(ScreenEdge.Right, collapsed: true)));
        Assert.Equal(ScreenEdge.Left, Pointing(Arrow(ScreenEdge.Left, collapsed: false)));
        Assert.Equal(ScreenEdge.Right, Pointing(Arrow(ScreenEdge.Left, collapsed: true)));
    }

    [Fact(DisplayName = "a collapsed tab carries the mark of its most urgent session")]
    public void CollapsedTabMark()
    {
        static CreatureAppearance Tab(IReadOnlyList<PanelSession> sessions, bool collapsed = true, bool hovered = false) =>
            CreatureAppearance.Tab(sessions, collapsed, ScreenEdge.Right, hovered, frame: 0, Now);

        var finished = Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60));
        var failed = Session(state: SessionState.Error);
        var asking = Session(state: SessionState.NeedsInput);
        var blocked = Session(state: SessionState.NeedsPermission);
        Assert.Equal(new Tile(), Tab([]).Tile);
        Assert.Equal(
            new Tile(),
            Tab([Session(state: SessionState.Working), Session(state: SessionState.Done, stateSince: Now.AddSeconds(-6 * 60))]).Tile);
        Assert.Equal(new Tile(Border: Paint.Ready), Tab([finished]).Tile);
        Assert.Equal(new Tile(Border: Paint.Danger), Tab([finished, failed]).Tile);
        Assert.Equal(Look(asking, frame: 0).Tile, Tab([finished, asking, failed]).Tile);
        Assert.Equal(Look(blocked, frame: 0).Tile, Tab([asking, finished, blocked]).Tile);
        Assert.True(Tab([blocked]).Pixels.Contains(Black), "outlined on an accent tile");
        Assert.False(Tab([finished]).Pixels.Contains(Black), "no outline on a bare tile");
        Assert.True(Tab([blocked], collapsed: false).Tile == new Tile(), "while open, the creatures show it");
    }

    [Fact(DisplayName = "hovering the tab tints it, under any mark")]
    public void TabHover()
    {
        static Tile Tab(IReadOnlyList<PanelSession> sessions, bool collapsed) =>
            CreatureAppearance.Tab(sessions, collapsed, ScreenEdge.Right, hovered: true, frame: 0, Now).Tile;

        Assert.Equal(new Tile(Fill: Paint.White(0.12)), Tab([], collapsed: false));
        Assert.Equal(new Tile(Fill: Paint.White(0.12)), Tab([], collapsed: true));
        Assert.Equal(
            new Tile(Fill: Paint.White(0.12), Border: Paint.Ready),
            Tab([Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60))], collapsed: true));
    }

    [Fact(DisplayName = "appearances compare by value, pixels included")]
    public void ValueEquality()
    {
        var s = Session(state: SessionState.Working);
        var a = Look(s, frame: 3);
        var b = Look(s, frame: 3);
        Assert.NotSame(a.Pixels, b.Pixels);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Look(s, frame: 4));
    }
}
