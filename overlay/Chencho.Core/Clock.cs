namespace Chencho.Core;

/// <summary>The animation clock every creature shares.</summary>
public static class Clock
{
    public const int FramesPerSecond = 4;

    /// <summary>The animation frame at <paramref name="now"/>, counted from the Unix epoch.</summary>
    public static long FrameAt(DateTimeOffset now) =>
        (long)Math.Floor(now.ToUnixTimeMilliseconds() / (1000.0 / FramesPerSecond));
}
