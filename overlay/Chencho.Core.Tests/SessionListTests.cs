using static Chencho.Core.Tests.Fixtures;

namespace Chencho.Core.Tests;

public class SessionListTests
{
    [Fact(DisplayName = "a snapshot replaces everything")]
    public void SnapshotReplaces()
    {
        var list = new SessionList();
        list.Apply(new ServerMessage.SessionUpdate(Session(name: "stale")));
        var fresh = Session(name: "fresh");
        list.Apply(new ServerMessage.Snapshot([fresh], fresh.Id));
        Assert.Equal(["fresh"], list.Shown(Now).Select(s => s.Name));
        Assert.Equal(fresh.Id, list.FocusedId);
    }

    [Fact(DisplayName = "session upserts, removed deletes, focus moves")]
    public void UpsertRemoveFocus()
    {
        var list = new SessionList();
        var a = Session(name: "a");
        list.Apply(new ServerMessage.SessionUpdate(a));
        a = a with { State = SessionState.Working };
        list.Apply(new ServerMessage.SessionUpdate(a));
        Assert.Equal([SessionState.Working], list.Shown(Now).Select(s => s.State));

        list.Apply(new ServerMessage.Focus(a.Id));
        Assert.Equal(a.Id, list.FocusedId);
        list.Apply(new ServerMessage.Removed(a.Id));
        Assert.Empty(list.Shown(Now));
        list.Apply(new ServerMessage.Focus(null));
        Assert.Null(list.FocusedId);
    }

    [Fact(DisplayName = "errors leave the list alone")]
    public void ErrorsLeaveListAlone()
    {
        var list = new SessionList();
        list.Apply(new ServerMessage.SessionUpdate(Session()));
        list.Apply(new ServerMessage.Focus("x"));
        var sessions = list.Sessions.ToDictionary();
        list.Apply(new ServerMessage.Error("boom"));
        Assert.Equal(sessions, list.Sessions);
        Assert.Equal("x", list.FocusedId);
    }

    [Fact(DisplayName = "shows pinned sessions by position, then the active unpinned ones by name")]
    public void ShownOrder()
    {
        var list = new SessionList();
        list.Apply(new ServerMessage.Snapshot(
            [
                Session(name: "zeta", state: SessionState.Working),
                Session(name: "pinned second", state: SessionState.Idle, pinned: true, position: 1),
                Session(name: "alpha", state: SessionState.NeedsInput),
                Session(name: "open but resting", state: SessionState.Idle),
                Session(name: "finished long ago", state: SessionState.Done, stateSince: Now.AddSeconds(-3600)),
                Session(name: "gone", state: SessionState.Ended, live: false),
                Session(name: "pinned first", state: SessionState.Ended, live: false, pinned: true, position: 0),
            ],
            null));
        Assert.Equal(["pinned first", "pinned second", "alpha", "zeta"], list.Shown(Now).Select(s => s.Name));
    }

    [Fact(DisplayName = "names sort like localizedCompare, not by code point")]
    public void NamesSortLocalized()
    {
        var list = new SessionList();
        list.Apply(new ServerMessage.Snapshot(
            [Session(name: "Beta", state: SessionState.Working), Session(name: "alpha", state: SessionState.Working)],
            null));
        Assert.Equal(["alpha", "Beta"], list.Shown(Now).Select(s => s.Name));
    }

    [Fact(DisplayName = "sessions with the same name keep a stable order")]
    public void SameNameStableOrder()
    {
        var list = new SessionList();
        PanelSession[] twins = [Session(name: "twin"), Session(name: "twin")];
        list.Apply(new ServerMessage.Snapshot(twins.Reverse().ToArray(), null));
        Assert.Equal(twins.Select(s => s.Id), list.Shown(Now).Select(s => s.Id));
    }
}
