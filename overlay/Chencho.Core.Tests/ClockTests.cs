namespace Chencho.Core.Tests;

public class ClockTests
{
    [Fact(DisplayName = "frames tick four times a second from the Unix epoch")]
    public void Ticks()
    {
        Assert.Equal(0, Clock.FrameAt(DateTimeOffset.UnixEpoch));
        Assert.Equal(0, Clock.FrameAt(DateTimeOffset.UnixEpoch.AddMilliseconds(249)));
        Assert.Equal(1, Clock.FrameAt(DateTimeOffset.UnixEpoch.AddMilliseconds(250)));
        Assert.Equal(-1, Clock.FrameAt(DateTimeOffset.UnixEpoch.AddMilliseconds(-1)));
        Assert.Equal(40_000, Clock.FrameAt(Fixtures.Now));
    }
}
