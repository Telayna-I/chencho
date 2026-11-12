namespace Chencho.Core.Tests;

public class SpriteTests
{
    private static readonly Rgb Orange = Rgb.FromHex("#ff6b1a")!.Value;
    private static readonly Rgb Blue = Rgb.FromHex("#4f7cff")!.Value;
    private static readonly Rgb White = Rgb.FromHex("#ffffff")!.Value;

    public static TheoryData<string> Characters()
    {
        var ids = new TheoryData<string>();
        foreach (var character in CharacterTemplate.All) ids.Add(character.Id);
        return ids;
    }

    [Fact(DisplayName = "parses and shades colors")]
    public void ParsesAndShades()
    {
        Assert.Equal(new Rgb(0xd9, 0x77, 0x57), Rgb.FromHex("#d97757"));
        Assert.Equal(new Rgb(0xd9, 0x77, 0x57), Rgb.FromHex("#D97757"));
        Assert.Null(Rgb.FromHex("d97757"));
        Assert.Null(Rgb.FromHex("#d9775"));
        Assert.Null(Rgb.FromHex("#zz7757"));
        Assert.Equal(Rgb.FromHex("#8d4d39"), Rgb.FromHex("#d97757")!.Value.Shaded(-0.35));
        Assert.Equal(Rgb.FromHex("#e6a792"), Rgb.FromHex("#d97757")!.Value.Shaded(0.35));
    }

    [Fact(DisplayName = "writes colors as lowercase hex")]
    public void WritesHex()
    {
        Assert.Equal("#d97757", Rgb.FromHex("#D97757")!.Value.Hex);
        Assert.Equal("#00ff0a", new Rgb(0, 255, 10).Hex);
    }

    [Fact(DisplayName = "shading rounds halves away from zero")]
    public void ShadingRoundsHalvesAwayFromZero()
    {
        // 2 + 253 × 0.5 = 128.5: banker's rounding would give 128.
        Assert.Equal(new Rgb(129, 129, 129), new Rgb(2, 2, 2).Shaded(0.5));
    }

    [Fact(DisplayName = "loads the shared character templates, default first")]
    public void LoadsTemplates()
    {
        Assert.Equal(["default", "robot", "ghost", "cat"], CharacterTemplate.All.Select(c => c.Id));
    }

    [Theory(DisplayName = "is a well-formed 16×16 template")]
    [MemberData(nameof(Characters))]
    public void WellFormed(string id)
    {
        var c = CharacterTemplate.Named(id);
        Assert.Equal(16, c.Body.Length);
        foreach (var row in c.Body.Concat(c.Walk?.Rows ?? []))
        {
            Assert.True(row.Length == 16 && row.All("BDLFP.".Contains), $"bad row {row}");
        }
        if (c.Walk is { } walk) Assert.True(walk.Row + walk.Rows.Length <= 16, "walk runs past the body");
    }

    [Theory(DisplayName = "has both eyes on its body")]
    [MemberData(nameof(Characters))]
    public void EyesOnBody(string id)
    {
        var c = CharacterTemplate.Named(id);
        foreach (var x in new[] { c.Eyes.Left, c.Eyes.Right })
        {
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                Assert.True(c.Body[c.Eyes.Y + dy][x + dx] != '.', $"eye cell off the body at {x + dx},{c.Eyes.Y + dy}");
            }
        }
    }

    [Fact(DisplayName = "every character and mood fits the sprite")]
    public void EveryFrameFits()
    {
        foreach (var c in CharacterTemplate.All)
        {
            foreach (var mood in Enum.GetValues<Mood>())
            {
                for (var frame = 0; frame < 16; frame++)
                {
                    var pixels = Sprite.Frame(c.Id, Orange, mood, frame);
                    Assert.True(pixels.Width == Sprite.Size && pixels.Height == Sprite.Size, $"{c.Id} {mood} {frame}");
                }
            }
        }
    }

    [Fact(DisplayName = "frames before the epoch cycle like the ones after it")]
    public void NegativeFramesCycle()
    {
        // 48 is a multiple of every cycle length.
        foreach (var mood in Enum.GetValues<Mood>())
        {
            for (var frame = -48; frame < 0; frame++)
            {
                Assert.Equal(Sprite.Frame("robot", Orange, mood, frame + 48), Sprite.Frame("robot", Orange, mood, frame));
            }
        }
    }

    [Fact(DisplayName = "the body is tinted with the icon color")]
    public void TintedBody()
    {
        Assert.True(Sprite.Frame("default", Orange, Mood.Idle, 1).Contains(Orange));
        Assert.False(Sprite.Frame("default", Blue, Mood.Idle, 1).Contains(Orange));
    }

    [Fact(DisplayName = "unknown characters fall back to the default one")]
    public void UnknownCharacter()
    {
        Assert.Equal(Sprite.Frame("default", Orange, Mood.Idle, 1), Sprite.Frame("nope", Orange, Mood.Idle, 1));
    }

    [Fact(DisplayName = "working moves between frames; idle stands still between blinks")]
    public void WorkingMovesIdleBlinks()
    {
        Assert.NotEqual(Sprite.Frame("default", Orange, Mood.Working, 0), Sprite.Frame("default", Orange, Mood.Working, 1));
        Assert.Equal(Sprite.Frame("default", Orange, Mood.Idle, 1), Sprite.Frame("default", Orange, Mood.Idle, 2));
        Assert.True(
            Sprite.Frame("default", Orange, Mood.Idle, 0) != Sprite.Frame("default", Orange, Mood.Idle, 1),
            "frame 0 blinks");
    }

    [Fact(DisplayName = "alert, asking, error and sleeping carry their overlay")]
    public void Overlays()
    {
        static bool Has(Mood mood, long frame, string hex) =>
            Sprite.Frame("default", Rgb.FromHex("#ff6b1a")!.Value, mood, frame).Contains(Rgb.FromHex(hex)!.Value);

        Assert.True(Has(Mood.Alert, 0, "#ffd23f"));
        Assert.True(Has(Mood.Asking, 0, "#ffffff"));
        Assert.True(Has(Mood.Error, 0, "#ff4d4d"));
        Assert.True(Has(Mood.Sleeping, 0, "#c8d0dc"));
    }

    [Fact(DisplayName = "trimming drops the transparent margins around a sprite")]
    public void Trimming()
    {
        var red = Rgb.FromHex("#ff0000")!.Value;
        var pixels = Pixels.Empty(4, 4);
        pixels[1, 1] = red;
        pixels[2, 2] = red;
        var expected = Pixels.Empty(2, 2);
        expected[0, 0] = red;
        expected[1, 1] = red;
        Assert.Equal(expected, Sprite.Trimmed(pixels));
        Assert.Equal(Pixels.Empty(0, 0), Sprite.Trimmed(Pixels.Empty(2, 2)));
    }

    [Fact(DisplayName = "working hops two pixels and shows typing dots, so every character looks busy")]
    public void WorkingHopsWithDots()
    {
        static int Top(Pixels pixels) =>
            Enumerable.Range(0, pixels.Height).First(y => Enumerable.Range(0, pixels.Width).Any(x => pixels[x, y] == Orange));

        foreach (var character in CharacterTemplate.All)
        {
            var frames = Enumerable.Range(0, 4).Select(f => Sprite.Frame(character.Id, Orange, Mood.Working, f)).ToArray();
            var tops = frames.Select(Top).ToArray();
            Assert.Equal(2, tops.Max() - tops.Min());
            foreach (var frame in frames)
            {
                var hasDots = Enumerable.Range(0, 6).Any(y => Enumerable.Range(17, Sprite.Size - 17).Any(x => frame[x, y] == White));
                Assert.True(hasDots, $"{character.Id} has dots");
            }
        }
    }

    [Fact(DisplayName = "a finished creature holds up a green check")]
    public void FinishedCheck()
    {
        var green = Rgb.FromHex("#4cd964")!.Value;
        for (var frame = 0; frame < 8; frame++)
        {
            Assert.True(Sprite.Frame("cat", Orange, Mood.Finished, frame).Contains(green));
        }
        Assert.False(Sprite.Frame("cat", Orange, Mood.Idle, 1).Contains(green));
    }

    [Fact(DisplayName = "pixels compare and hash by content, so they can key caches")]
    public void PixelsEquality()
    {
        var a = Sprite.Frame("ghost", Orange, Mood.Asking, 2);
        var b = Sprite.Frame("ghost", Orange, Mood.Asking, 2);
        Assert.NotSame(a, b);
        Assert.True(a == b && a.Equals(b) && a.GetHashCode() == b.GetHashCode());

        var copy = a.Clone();
        Assert.Equal(a, copy);
        copy[0, 0] = Blue;
        Assert.True(a != copy && a[0, 0] is null, "a clone is independent");
        Assert.NotEqual(Pixels.Empty(2, 3), Pixels.Empty(3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => a[Sprite.Size, 0]);
    }
}
