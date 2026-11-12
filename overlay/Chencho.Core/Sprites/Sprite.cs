using System.Globalization;

namespace Chencho.Core;

public enum Mood
{
    Idle,
    Working,
    Alert,
    Asking,
    Compacting,
    Error,
    /// <summary>Done with its turn and waiting for the user's reply.</summary>
    Finished,
    Sleeping,
}

/// <summary>An opaque sRGB color.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Only <c>#rrggbb</c>, in either case.</summary>
    public static Rgb? FromHex(string hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        for (var i = 1; i < hex.Length; i++)
        {
            if (!char.IsAsciiHexDigit(hex[i])) return null;
        }
        var v = int.Parse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Lowercase <c>#rrggbb</c>.</summary>
    public string Hex => $"#{R:x2}{G:x2}{B:x2}";

    /// <summary>Mixed toward black (amount &lt; 0) or white (amount &gt; 0); |amount| is 0…1.</summary>
    /// <remarks>Halves round away from zero.</remarks>
    public Rgb Shaded(double amount)
    {
        var target = amount < 0 ? 0.0 : 255.0;
        var t = Math.Abs(amount);
        byte Mix(byte v) => (byte)Math.Round(v + (target - v) * t, MidpointRounding.AwayFromZero);
        return new Rgb(Mix(R), Mix(G), Mix(B));
    }
}

/// <summary>A grid of sprite pixels; null is transparent.</summary>
/// <remarks>
/// Compared by content: appearances are compared every frame to skip unchanged layers, and pixels key bitmap caches.
/// Clone before mutating one that is used as a key.
/// </remarks>
public sealed class Pixels : IEquatable<Pixels>
{
    private readonly Rgb?[] cells;

    private Pixels(int width, int height, Rgb?[] cells)
    {
        Width = width;
        Height = height;
        this.cells = cells;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>All transparent.</summary>
    public static Pixels Empty(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        return new Pixels(width, height, new Rgb?[width * height]);
    }

    /// <summary><paramref name="x"/> counts from the left, <paramref name="y"/> from the top.</summary>
    public Rgb? this[int x, int y]
    {
        get => cells[IndexOf(x, y)];
        set => cells[IndexOf(x, y)] = value;
    }

    public Pixels Clone() => new(Width, Height, (Rgb?[])cells.Clone());

    private int IndexOf(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x), x, $"outside 0..<{Width}");
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y), y, $"outside 0..<{Height}");
        return y * Width + x;
    }

    public bool Equals(Pixels? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Width != other.Width || Height != other.Height) return false;
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] != other.cells[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Pixels);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.Add(Height);
        foreach (var cell in cells) hash.Add(cell);
        return hash.ToHashCode();
    }

    public static bool operator ==(Pixels? left, Pixels? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Pixels? left, Pixels? right) => !(left == right);

    public override string ToString() => $"Pixels {Width}×{Height}";
}

public sealed partial record SessionIcon
{
    /// <summary>The icon's color, or the default icon's when it isn't <c>#rrggbb</c>.</summary>
    public Rgb Tint => Rgb.FromHex(Color) ?? Rgb.FromHex(Default.Color)!.Value;
}

public static class Sprite
{
    /// <summary>A 16×16 character plus room above and to the right for overlays.</summary>
    public const int Size = 24;

    private const int BodyX = 4;
    private const int BodyY = 6;

    /// <summary>One animation frame. Unknown characters draw as the default one.</summary>
    public static Pixels Frame(string character, Rgb color, Mood mood, long frame)
    {
        var template = CharacterTemplate.Named(character);
        var pose = new Pose(mood, frame);
        var dark = color.Shaded(-0.35);
        var light = color.Shaded(0.35);
        Rgb? ColorOf(char cell) => cell switch
        {
            'B' => color,
            'D' => dark,
            'L' => light,
            'F' => Palette.Face,
            'P' => Palette.Pink,
            _ => null,
        };

        var pixels = Pixels.Empty(Size, Size);
        void Set(int x, int y, Rgb c)
        {
            if (x is >= 0 and < Size && y is >= 0 and < Size) pixels[x, y] = c;
        }

        var ox = BodyX + pose.Dx;
        var oy = BodyY + pose.Dy;
        var rows = BodyRows(template, pose.Walk);
        for (var y = 0; y < rows.Count; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (ColorOf(rows[y][x]) is { } c) Set(ox + x, oy + y, c);
            }
        }

        var eyeColor = template.EyeColor ?? Palette.Eye;
        foreach (var (ex, mirrored) in new[] { (template.Eyes.Left, false), (template.Eyes.Right, true) })
        {
            foreach (var (dx, dy) in EyeCells(pose.Eyes, mirrored))
            {
                Set(ox + ex + dx, oy + template.Eyes.Y + dy, eyeColor);
            }
        }

        foreach (var overlay in pose.Overlays)
        {
            for (var y = 0; y < overlay.Glyph.Length; y++)
            {
                for (var x = 0; x < overlay.Glyph[y].Length; x++)
                {
                    if (overlay.Glyph[y][x] == 'X') Set(overlay.X + x, overlay.Y + y, overlay.Color);
                }
            }
        }
        return pixels;
    }

    /// <summary>Without the transparent rows and columns around the drawing; 0×0 when nothing is drawn.</summary>
    public static Pixels Trimmed(Pixels pixels)
    {
        int top = -1, bottom = -1, left = int.MaxValue, right = -1;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (pixels[x, y] is null) continue;
                if (top < 0) top = y;
                bottom = y;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }
        if (top < 0) return Pixels.Empty(0, 0);

        var result = Pixels.Empty(right - left + 1, bottom - top + 1);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++) result[x, y] = pixels[left + x, top + y];
        }
        return result;
    }

    private static class Palette
    {
        public static readonly Rgb Bang = Rgb.FromHex("#ffd23f")!.Value;
        public static readonly Rgb Question = Rgb.FromHex("#ffffff")!.Value;
        public static readonly Rgb Cross = Rgb.FromHex("#ff4d4d")!.Value;
        public static readonly Rgb Z = Rgb.FromHex("#c8d0dc")!.Value;
        public static readonly Rgb DotOn = Rgb.FromHex("#ffffff")!.Value;
        public static readonly Rgb DotOff = Rgb.FromHex("#4a4f5a")!.Value;
        public static readonly Rgb Face = Rgb.FromHex("#16171c")!.Value;
        public static readonly Rgb Pink = Rgb.FromHex("#ff8fb1")!.Value;
        public static readonly Rgb Check = Rgb.FromHex("#4cd964")!.Value;
        public static readonly Rgb Eye = Rgb.FromHex("#111216")!.Value;
    }

    private static class Glyphs
    {
        public static readonly string[] Bang = ["X", "X", "X", ".", "X"];
        public static readonly string[] Question = ["XXX", "..X", ".XX", "...", ".X."];
        public static readonly string[] Cross = ["X.X", ".X.", "X.X"];
        public static readonly string[] Z = ["XXXX", "..X.", ".X..", "XXXX"];
        public static readonly string[] Dot = ["X"];
        public static readonly string[] Check = [".....X", "....XX", "X..XX.", "XXXX..", ".XX..."];
    }

    private static readonly (int X, int Y)[] ZSpots = [(15, 5), (18, 2), (20, 0)];

    private enum EyeStyle { Open, Closed, Squint }

    /// <summary>One blink every four seconds.</summary>
    private static readonly EyeStyle[] Blink = [EyeStyle.Closed, .. Enumerable.Repeat(EyeStyle.Open, 15)];

    private sealed record Overlay(string[] Glyph, int X, int Y, Rgb Color);

    private sealed class Pose
    {
        public int Dx { get; }
        public int Dy { get; }
        public EyeStyle Eyes { get; } = EyeStyle.Open;
        public bool Walk { get; }
        public Overlay[] Overlays { get; } = [];

        public Pose(Mood mood, long frame)
        {
            // A non-negative modulo, so frames before the epoch cycle too.
            T Cycle<T>(params T[] values) => values[(int)((frame % values.Length + values.Length) % values.Length)];

            switch (mood)
            {
                case Mood.Idle:
                    Eyes = Cycle(Blink);
                    break;
                case Mood.Working:
                    Dy = Cycle(0, -1, -2, -1);
                    Walk = Cycle(false, true);
                    var raised = Cycle(0, 1, 2);
                    Overlays = Enumerable.Range(0, 3)
                        .Select(i => new Overlay(Glyphs.Dot, 17 + i * 2, i == raised ? 3 : 4, Palette.DotOn))
                        .ToArray();
                    break;
                case Mood.Alert:
                    Dy = Cycle(0, -2, -3, -2);
                    Overlays = Cycle<Overlay[]>([new Overlay(Glyphs.Bang, 21, 0, Palette.Bang)], []);
                    break;
                case Mood.Asking:
                    Dy = Cycle(0, 0, -1, -1);
                    Overlays = [new Overlay(Glyphs.Question, 19, 0, Palette.Question)];
                    break;
                case Mood.Compacting:
                    Dy = Cycle(0, 1);
                    Eyes = EyeStyle.Squint;
                    var lit = Cycle(0, 1, 2);
                    Overlays = Enumerable.Range(0, 3)
                        .Select(i => new Overlay(Glyphs.Dot, 17 + i * 2, 3, i == lit ? Palette.DotOn : Palette.DotOff))
                        .ToArray();
                    break;
                case Mood.Error:
                    Dx = Cycle(-1, 1, -1, 0, 0, 0, 0, 0);
                    Eyes = EyeStyle.Squint;
                    Overlays = [new Overlay(Glyphs.Cross, 19, 1, Palette.Cross)];
                    break;
                case Mood.Finished:
                    Eyes = Cycle(Blink);
                    Overlays = [new Overlay(Glyphs.Check, 17, 0, Palette.Check)];
                    break;
                case Mood.Sleeping:
                    var count = Cycle(1, 1, 2, 2, 3, 3, 0, 0);
                    Eyes = EyeStyle.Closed;
                    Overlays = ZSpots.Take(count).Select(spot => new Overlay(Glyphs.Z, spot.X, spot.Y, Palette.Z)).ToArray();
                    break;
            }
        }
    }

    private static IReadOnlyList<string> BodyRows(CharacterTemplate character, bool walking)
    {
        if (!walking || character.Walk is not { } walk) return character.Body;
        return character.Body
            .Select((line, y) => y >= walk.Row && y < walk.Row + walk.Rows.Length ? walk.Rows[y - walk.Row] : line)
            .ToArray();
    }

    private static readonly (int, int)[] OpenEye = [(0, 0), (1, 0), (0, 1), (1, 1)];
    private static readonly (int, int)[] ClosedEye = [(0, 1), (1, 1)];
    private static readonly (int, int)[] SquintLeft = [(0, 0), (1, 1)];
    private static readonly (int, int)[] SquintRight = [(1, 0), (0, 1)];

    private static (int, int)[] EyeCells(EyeStyle style, bool mirrored)
    {
        if (style == EyeStyle.Open) return OpenEye;
        if (style == EyeStyle.Closed) return ClosedEye;
        return mirrored ? SquintRight : SquintLeft;
    }
}
