using static Chencho.Core.Tests.Fixtures;

namespace Chencho.Core.Tests;

public class StatusTests
{
    [Fact(DisplayName = "maps states to moods")]
    public void StatesToMoods()
    {
        Assert.Equal(Mood.Working, Session(state: SessionState.Working).Mood(Now));
        Assert.Equal(Mood.Alert, Session(state: SessionState.NeedsPermission).Mood(Now));
        Assert.Equal(Mood.Asking, Session(state: SessionState.NeedsInput).Mood(Now));
        Assert.Equal(Mood.Compacting, Session(state: SessionState.Compacting).Mood(Now));
        Assert.Equal(Mood.Error, Session(state: SessionState.Error).Mood(Now));
        Assert.Equal(Mood.Sleeping, Session(state: SessionState.Ended).Mood(Now));
    }

    [Fact(DisplayName = "a finished session waits for you, then dozes off after five minutes")]
    public void FinishedDozesOff()
    {
        Assert.Equal(Mood.Finished, Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60)).Mood(Now));
        Assert.Equal(Mood.Idle, Session(state: SessionState.Idle, stateSince: Now.AddSeconds(-60)).Mood(Now));
        Assert.Equal(Mood.Sleeping, Session(state: SessionState.Done, stateSince: Now.AddSeconds(-6 * 60)).Mood(Now));
    }

    [Fact(DisplayName = "active: working or waiting on the user, or finished less than five minutes ago")]
    public void Active()
    {
        foreach (var state in new[] { SessionState.Working, SessionState.Compacting, SessionState.NeedsPermission, SessionState.NeedsInput })
        {
            Assert.True(Session(state: state).IsActive(Now), $"{state}");
        }
        Assert.True(Session(state: SessionState.Done, stateSince: Now.AddSeconds(-60)).IsActive(Now));
        Assert.True(Session(state: SessionState.Error, stateSince: Now.AddSeconds(-60)).IsActive(Now));
        Assert.False(Session(state: SessionState.Done, stateSince: Now.AddSeconds(-6 * 60)).IsActive(Now));
        Assert.False(Session(state: SessionState.Error, stateSince: Now.AddSeconds(-6 * 60)).IsActive(Now));
        Assert.False(Session(state: SessionState.Idle).IsActive(Now));
        Assert.False(Session(state: SessionState.Ended, live: false).IsActive(Now));
        Assert.False(Session(state: SessionState.Working, live: false).IsActive(Now), "its process is gone");
    }

    [Fact(DisplayName = "only permission and input requests need the user")]
    public void OnlyRequestsNeedTheUser()
    {
        var needy = Enum.GetValues<SessionState>().Where(state => Session(state: state).NeedsAttention);
        Assert.Equal([SessionState.NeedsPermission, SessionState.NeedsInput], needy);
    }

    [Fact(DisplayName = "summarises the strip for its tab")]
    public void Summary()
    {
        Assert.Equal("no sessions", Status.Summary([]));
        Assert.Equal("1 session", Status.Summary([Session(state: SessionState.Working)]));
        Assert.Equal("2 sessions", Status.Summary([Session(state: SessionState.Working), Session(state: SessionState.Done)]));
        Assert.Equal("1 needs you", Status.Summary([Session(state: SessionState.NeedsInput), Session(state: SessionState.Working)]));
        Assert.Equal("2 need you", Status.Summary([Session(state: SessionState.NeedsInput), Session(state: SessionState.NeedsPermission)]));
    }

    [Fact(DisplayName = "ago")]
    public void Ago()
    {
        Assert.Equal("now", Status.Ago(Now, Now));
        Assert.Equal("now", Status.Ago(Now.AddSeconds(30), Now));
        Assert.Equal("42s", Status.Ago(Now.AddSeconds(-42), Now));
        Assert.Equal("5m", Status.Ago(Now.AddSeconds(-5 * 60), Now));
        Assert.Equal("3h", Status.Ago(Now.AddSeconds(-3 * 3600), Now));
        Assert.Equal("2d", Status.Ago(Now.AddSeconds(-2 * 86400), Now));
    }

    [Fact(DisplayName = "statusLine")]
    public void StatusLine()
    {
        Assert.Equal("Bash: npm test", Session(state: SessionState.Working, activity: "Bash: npm test").StatusLine(Now));
        Assert.Equal("working", Session(state: SessionState.Working).StatusLine(Now));
        Assert.Equal("done · 5m", Session(state: SessionState.Done, stateSince: Now.AddSeconds(-5 * 60)).StatusLine(Now));
        Assert.Equal("idle · 42s", Session(state: SessionState.Idle, stateSince: Now.AddSeconds(-42)).StatusLine(Now));
        Assert.Equal(
            "closed · 2h",
            Session(state: SessionState.Ended, live: false, stateSince: Now.AddSeconds(-2 * 3600)).StatusLine(Now));
        Assert.Equal(
            "needs permission",
            Session(state: SessionState.NeedsPermission, activity: "Claude needs your permission to use Bash").StatusLine(Now));
        Assert.Equal("waiting for you", Session(state: SessionState.NeedsInput).StatusLine(Now));
        Assert.Equal("Plan ready", Session(state: SessionState.NeedsInput, activity: "Plan ready").StatusLine(Now));
        Assert.Equal("compacting", Session(state: SessionState.Compacting).StatusLine(Now));
        Assert.Equal("error", Session(state: SessionState.Error).StatusLine(Now));
        Assert.Equal("error · rate limited", Session(state: SessionState.Error, activity: "rate limited").StatusLine(Now));
    }
}
