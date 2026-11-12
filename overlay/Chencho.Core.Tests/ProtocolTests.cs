namespace Chencho.Core.Tests;

public class ProtocolTests
{
    private const string SessionJson = """
        {"id":"34446bd8-0000-0000-0000-000000000001","name":"Node migration","cwd":"/src/app","state":"needs_permission",
         "activity":"Bash: npm test","live":true,"stateSince":5000000,"pinned":true,"position":2,
         "icon":{"character":"ghost","color":"#4f7cff"}}
        """;

    private static PanelSession? ParseSession(string sessionJson) =>
        ServerMessage.Parse($$"""{"type":"session","session":{{sessionJson}}}""") is ServerMessage.SessionUpdate update
            ? update.Session
            : null;

    [Fact(DisplayName = "decodes a session with every field")]
    public void DecodesSession()
    {
        var s = ParseSession(SessionJson);
        Assert.NotNull(s);
        Assert.Equal("34446bd8-0000-0000-0000-000000000001", s.Id);
        Assert.Equal("Node migration", s.Name);
        Assert.Equal(SessionState.NeedsPermission, s.State);
        Assert.Equal("Bash: npm test", s.Activity);
        Assert.True(s.Live);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(5000), s.StateSince);
        Assert.True(s.Pinned);
        Assert.Equal(2, s.Position);
        Assert.Equal(new SessionIcon("ghost", "#4f7cff"), s.Icon);
        Assert.Null(s.Pid);
    }

    [Fact(DisplayName = "reads the Claude process id when the agent knows it")]
    public void DecodesPid()
    {
        Assert.Equal(4242, ParseSession(SessionJson.Replace("\"pinned\"", "\"pid\":4242,\"pinned\""))?.Pid);
        Assert.Null(ParseSession(SessionJson.Replace("\"pinned\"", "\"pid\":null,\"pinned\""))!.Pid);
        Assert.Null(ParseSession(SessionJson)!.Pid);
        // Like the other optional fields, a pid of the wrong type makes the session unreadable.
        Assert.Null(ParseSession(SessionJson.Replace("\"pinned\"", "\"pid\":\"4242\",\"pinned\"")));
    }

    [Fact(DisplayName = "decodes a snapshot, null fields and all")]
    public void DecodesSnapshot()
    {
        var json = $$"""{"type":"snapshot","focusedId":null,"sessions":[{{SessionJson}},"""
            + """{"id":"b","name":"n","cwd":"/","state":"done","activity":null,"live":false,"stateSince":0,"pinned":false,"position":null,"icon":{"character":"default","color":"#d97757"}}]}""";
        var snapshot = Assert.IsType<ServerMessage.Snapshot>(ServerMessage.Parse(json));
        Assert.Equal(2, snapshot.Sessions.Count);
        Assert.Null(snapshot.FocusedId);
        Assert.Null(snapshot.Sessions[1].Activity);
        Assert.Null(snapshot.Sessions[1].Position);
    }

    [Fact(DisplayName = "a snapshot skips sessions it can't read instead of dropping them all")]
    public void SnapshotSkipsUnreadable()
    {
        var json = $$"""{"type":"snapshot","focusedId":"x","sessions":[{{SessionJson}},{"id":42}]}""";
        var snapshot = Assert.IsType<ServerMessage.Snapshot>(ServerMessage.Parse(json));
        Assert.Equal(["Node migration"], snapshot.Sessions.Select(s => s.Name));
        Assert.Equal("x", snapshot.FocusedId);
    }

    [Fact(DisplayName = "a state from a newer agent reads as idle")]
    public void UnknownStateIsIdle()
    {
        var s = ParseSession(SessionJson.Replace("needs_permission", "dreaming"));
        Assert.NotNull(s);
        Assert.Equal(SessionState.Idle, s.State);
    }

    [Fact(DisplayName = "states keep their wire names")]
    public void StateWireNames()
    {
        string[] names = ["idle", "working", "needs_permission", "needs_input", "compacting", "done", "error", "ended"];
        Assert.Equal(names, Enum.GetValues<SessionState>().Select(s => s.WireName()));
        Assert.Equal(Enum.GetValues<SessionState>(), names.Select(SessionStateExtensions.FromWireName));
    }

    [Fact(DisplayName = "decodes removed, focus and error")]
    public void DecodesSmallMessages()
    {
        Assert.Equal(new ServerMessage.Removed("a"), ServerMessage.Parse("""{"type":"removed","id":"a"}"""));
        Assert.Equal(new ServerMessage.Focus("a"), ServerMessage.Parse("""{"type":"focus","id":"a"}"""));
        Assert.Equal(new ServerMessage.Focus(null), ServerMessage.Parse("""{"type":"focus","id":null}"""));
        Assert.Equal(
            new ServerMessage.Error("unknown session"),
            ServerMessage.Parse("""{"type":"error","message":"unknown session"}"""));
    }

    [Fact(DisplayName = "ignores messages it doesn't use and anything malformed")]
    public void IgnoresUnknownAndMalformed()
    {
        Assert.Null(ServerMessage.Parse("""{"type":"surprise"}"""));
        Assert.Null(ServerMessage.Parse("""{"type":"removed"}"""));
        Assert.Null(ServerMessage.Parse("not json"));
        Assert.Null(ServerMessage.Parse("[]"));
    }

    [Fact(DisplayName = "decodes the session library, skipping entries it can't read")]
    public void DecodesLibrary()
    {
        var json = """{"type":"library","sessions":[{"id":"a","name":"Old work","cwd":"/src","lastActivityAt":5000,"pinned":true},{"id":42}]}""";
        Assert.Equal(
            new ServerMessage.Library([new LibraryEntry("a", "Old work", "/src", DateTimeOffset.FromUnixTimeSeconds(5), true)]),
            ServerMessage.Parse(json));
    }

    [Fact(DisplayName = "encodes open, pin and icon")]
    public void EncodesClientMessages()
    {
        Assert.Equal("""{"id":"abc","type":"open"}""", new ClientMessage.Open("abc").Json);
        Assert.Equal("""{"type":"library"}""", new ClientMessage.LibraryRequest().Json);
        Assert.Equal("""{"id":"abc","pinned":true,"type":"pin"}""", new ClientMessage.Pin("abc", true).Json);
        Assert.Equal(
            """{"icon":{"character":"cat","color":"#4f7cff"},"id":"abc","type":"icon"}""",
            new ClientMessage.Icon("abc", new SessionIcon("cat", "#4f7cff")).Json);
        // JSONSerialization escapes slashes too.
        Assert.Equal("""{"id":"a\/b \"c\"","type":"open"}""", new ClientMessage.Open("a/b \"c\"").Json);
    }

    [Fact(DisplayName = "client messages name their session, except the library request")]
    public void ClientMessagesNameTheirSession()
    {
        var icon = SessionIcon.Default;
        foreach (var message in new ClientMessage[] { new ClientMessage.Open("a"), new ClientMessage.Pin("a", false), new ClientMessage.Icon("a", icon) })
        {
            Assert.Equal("a", message.SessionId);
        }
        Assert.Null(new ClientMessage.LibraryRequest().SessionId);
    }

    [Fact(DisplayName = "the color palette is fixed and includes the default")]
    public void Palette()
    {
        Assert.Equal(
            ["#ff6b1a", "#d97757", "#ffd23f", "#4cd964", "#3ccfe6", "#4f7cff", "#a66cff", "#ff5fa2", "#e8e8e8"],
            SessionIcon.Palette.Select(p => p.Color));
        Assert.Contains(SessionIcon.Palette, p => p.Color == SessionIcon.Default.Color);
    }

    [Fact(DisplayName = "reads the pairing token from the agent's config")]
    public void TokenFromJson()
    {
        Assert.Equal("s3cret", AgentConfig.TokenFromJson("""{"token":"s3cret"}"""));
        Assert.Null(AgentConfig.TokenFromJson("""{"token":""}"""));
        Assert.Null(AgentConfig.TokenFromJson("{}"));
        Assert.Null(AgentConfig.TokenFromJson("garbage"));
    }

    [Fact(DisplayName = "reads the token from Chencho's agent, where agent/src/config.ts writes it")]
    public void ConfigFileLocation()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(localAppData, "chencho"), AgentConfig.DataDirectory);
        Assert.Equal(Path.Combine(localAppData, "chencho", "config.json"), AgentConfig.ConfigFile);
    }

    [Fact(DisplayName = "reads the token file, and finds none when it's missing")]
    public void TokenFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chencho-test-{Guid.NewGuid():N}.json");
        Assert.Null(AgentConfig.Token(path));
        try
        {
            File.WriteAllText(path, """{"token":"s3cret"}""");
            Assert.Equal("s3cret", AgentConfig.Token(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(DisplayName = "builds the WebSocket URL with the token escaped")]
    public void WebSocketUrl()
    {
        var url = AgentConfig.WebSocketUrl("a+b/c=");
        Assert.Equal("ws://127.0.0.1:7778/ws?token=a%2Bb/c%3D", url.AbsoluteUri);
    }
}
