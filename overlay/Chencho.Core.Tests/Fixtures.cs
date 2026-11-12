namespace Chencho.Core.Tests;

internal static class Fixtures
{
    public static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(10_000);

    private static int sequence;

    public static PanelSession Session(
        string? name = null,
        SessionState state = SessionState.Done,
        string? activity = null,
        bool live = true,
        DateTimeOffset? stateSince = null,
        bool pinned = false,
        int? position = null)
    {
        var n = Interlocked.Increment(ref sequence);
        return new PanelSession(
            Id: $"00000000-0000-0000-0000-{n:D12}",
            Name: name ?? $"session {n}",
            Cwd: "/tmp",
            State: state,
            Activity: activity,
            Live: live,
            StateSince: stateSince ?? Now,
            Pinned: pinned,
            Position: position,
            Icon: SessionIcon.Default);
    }

    /// <summary>Every pixel, row by row.</summary>
    public static IEnumerable<Rgb?> Cells(this Pixels pixels)
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++) yield return pixels[x, y];
        }
    }

    public static bool Contains(this Pixels pixels, Rgb color) => pixels.Cells().Contains(color);
}
