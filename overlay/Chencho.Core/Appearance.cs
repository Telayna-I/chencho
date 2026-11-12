namespace Chencho.Core;

/// <param name="Alpha">0…1</param>
public readonly record struct Paint(Rgb Rgb, double Alpha = 1)
{
    public static Paint Accent { get; } = new(Rgb.FromHex("#ff6b1a")!.Value);
    public static Paint Danger { get; } = new(Rgb.FromHex("#ff4d4d")!.Value);
    /// <summary>A session that finished its turn and waits for the user's reply.</summary>
    public static Paint Ready { get; } = new(Rgb.FromHex("#4cd964")!.Value);

    public static Paint White(double alpha) => new(new Rgb(255, 255, 255), alpha);

    internal Paint WithAlpha(double alpha) => this with { Alpha = alpha };
}

/// <param name="BorderWidth">Points.</param>
public readonly record struct Tile(Paint? Fill = null, Paint? Border = null, double BorderWidth = 1.5)
{
    /// <summary>A bare tile. Unlike <c>default(Tile)</c>, its border width is 1.5.</summary>
    public Tile() : this(null, null, 1.5) { }
}

/// <summary>What a cell of the strip shows: a sprite on a tile, faded by <paramref name="Opacity"/>.</summary>
public sealed record CreatureAppearance(Pixels Pixels, Tile Tile, double Opacity)
{
    private static readonly Rgb ControlColor = Rgb.FromHex("#8b909a")!.Value;
    private static readonly Rgb Black = new(0, 0, 0);

    /// <summary>The tile at the end of the strip that pins sessions: a plus in its middle.</summary>
    public static CreatureAppearance AddButton(bool hovered)
    {
        var pixels = Pixels.Empty(Sprite.Size, Sprite.Size);
        for (var x = 8; x <= 15; x++) { for (var y = 11; y <= 12; y++) pixels[x, y] = ControlColor; }
        for (var x = 11; x <= 12; x++) { for (var y = 8; y <= 15; y++) pixels[x, y] = ControlColor; }
        return new CreatureAppearance(pixels, hovered ? new Tile(Fill: Paint.White(0.12)) : new Tile(), 1);
    }

    /// <summary>
    /// The tab that collapses the strip: an arrow toward the edge while open, back out while collapsed.
    /// Collapsed, it carries the mark of its most urgent session.
    /// </summary>
    public static CreatureAppearance Tab(
        IReadOnlyList<PanelSession> sessions, bool collapsed, ScreenEdge edge, bool hovered, long frame, DateTimeOffset now)
    {
        string[] chevron = ["...X", "..XX", ".XX.", "XX..", ".XX.", "..XX", "...X"];
        var (width, height) = StripLayout.TabPixels;
        var left = (width - 4) / 2;
        var top = (height - chevron.Length) / 2;
        var pointsRight = (edge == ScreenEdge.Right) != collapsed;
        var pixels = Pixels.Empty(width, height);
        for (var y = 0; y < chevron.Length; y++)
        {
            for (var x = 0; x < chevron[y].Length; x++)
            {
                if (chevron[y][x] == 'X') pixels[left + (pointsRight ? 3 - x : x), top + y] = ControlColor;
            }
        }

        var tile = hovered ? new Tile(Fill: Paint.White(0.12)) : new Tile();
        if (collapsed && MostUrgent(sessions, now) is { } urgent)
        {
            tile = Marked(tile, urgent, frame, now);
            if (urgent.NeedsAttention) pixels = Outlined(pixels);
        }
        return new CreatureAppearance(pixels, tile, 1);
    }

    public static CreatureAppearance For(PanelSession session, bool focused, bool hovered, long frame, DateTimeOffset now)
    {
        var tile = new Tile();
        if (hovered) tile = tile with { Fill = Paint.White(0.12) };
        if (focused) tile = tile with { Fill = Paint.White(hovered ? 0.3 : 0.22), Border = Paint.White(0.75) };
        tile = Marked(tile, session, frame, now);

        var pixels = Sprite.Frame(session.Icon.Character, session.Icon.Tint, session.Mood(now), frame);
        // An accent tile may share the sprite's color.
        if (session.NeedsAttention) pixels = Outlined(pixels);
        if (!session.Live) pixels = Grayscaled(pixels);

        return new CreatureAppearance(pixels, tile, session.Live ? 1 : 0.45);
    }

    /// <summary>Attention outranks focus and hover.</summary>
    private static Tile Marked(Tile tile, PanelSession session, long frame, DateTimeOffset now) => session.State switch
    {
        SessionState.NeedsPermission =>
            new Tile(frame % 4 < 2 ? Paint.Accent : Paint.Accent.WithAlpha(0.25), Paint.Accent, 2),
        SessionState.NeedsInput =>
            new Tile(Paint.Accent.WithAlpha(frame % 8 < 4 ? 0.35 : 0.2), Paint.Accent, 2),
        SessionState.Done when session.Mood(now) == Mood.Finished => tile with { Border = Paint.Ready },
        SessionState.Error => tile with { Border = Paint.Danger },
        _ => tile,
    };

    /// <summary>Lower is more urgent; null carries no mark.</summary>
    private static int? Urgency(PanelSession session, DateTimeOffset now) => session.State switch
    {
        SessionState.NeedsPermission => 0,
        SessionState.NeedsInput => 1,
        SessionState.Error => 2,
        SessionState.Done when session.Mood(now) == Mood.Finished => 3,
        _ => null,
    };

    /// <summary>The first of the most urgent.</summary>
    private static PanelSession? MostUrgent(IReadOnlyList<PanelSession> sessions, DateTimeOffset now)
    {
        PanelSession? urgent = null;
        var best = int.MaxValue;
        foreach (var session in sessions)
        {
            if (Urgency(session, now) is { } rank && rank < best)
            {
                urgent = session;
                best = rank;
            }
        }
        return urgent;
    }

    private static Pixels Outlined(Pixels pixels)
    {
        bool Filled(int x, int y) => x >= 0 && x < pixels.Width && y >= 0 && y < pixels.Height && pixels[x, y] is not null;
        var result = pixels.Clone();
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (pixels[x, y] is not null) continue;
                var touchesSprite = Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1);
                if (touchesSprite) result[x, y] = Black;
            }
        }
        return result;
    }

    /// <summary>Rec. 709 luma, rounded half away from zero.</summary>
    private static Pixels Grayscaled(Pixels pixels)
    {
        var result = pixels.Clone();
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (pixels[x, y] is not { } c) continue;
                var r = 0.2126 * c.R;
                var g = 0.7152 * c.G;
                var b = 0.0722 * c.B;
                var luma = (byte)Math.Round(r + g + b, MidpointRounding.AwayFromZero);
                result[x, y] = new Rgb(luma, luma, luma);
            }
        }
        return result;
    }
}
