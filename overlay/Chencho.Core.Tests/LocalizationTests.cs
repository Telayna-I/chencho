using static Chencho.Core.Tests.Fixtures;

namespace Chencho.Core.Tests;

public class LocalizationTests
{
    private const Language Es = Language.Spanish;

    [Fact(DisplayName = "a chosen language wins; otherwise the system's, Spanish only for es")]
    public void Resolve()
    {
        Assert.Equal(Language.English, Localization.Resolve("en", "es"));
        Assert.Equal(Language.Spanish, Localization.Resolve("es", "en"));
        Assert.Equal(Language.Spanish, Localization.Resolve("auto", "es"));
        Assert.Equal(Language.Spanish, Localization.Resolve(null, "ES"));
        Assert.Equal(Language.English, Localization.Resolve(null, "pt"));
        Assert.Equal(Language.English, Localization.Resolve("fr", "de"));
    }

    [Fact(DisplayName = "status lines in Spanish")]
    public void SpanishStatusLines()
    {
        var fiveMinutes = Now.AddSeconds(-5 * 60);
        Assert.Equal("trabajando", Session(state: SessionState.Working).StatusLine(Now, Es));
        Assert.Equal("necesita permiso", Session(state: SessionState.NeedsPermission, activity: "Needs your permission").StatusLine(Now, Es));
        Assert.Equal("esperándote", Session(state: SessionState.NeedsInput).StatusLine(Now, Es));
        Assert.Equal("compactando", Session(state: SessionState.Compacting).StatusLine(Now, Es));
        Assert.Equal("error · rate_limit", Session(state: SessionState.Error, activity: "rate_limit").StatusLine(Now, Es));
        Assert.Equal("inactiva · 5m", Session(state: SessionState.Idle, stateSince: fiveMinutes).StatusLine(Now, Es));
        Assert.Equal("terminó · 5m", Session(state: SessionState.Done, stateSince: fiveMinutes).StatusLine(Now, Es));
        Assert.Equal("cerrada · ahora", Session(state: SessionState.Ended).StatusLine(Now, Es));
    }

    [Fact(DisplayName = "the agent's fixed phrases are translated; tool details and Claude's own words are not")]
    public void Activities()
    {
        Assert.Equal("Pensando…", Session(state: SessionState.Working, activity: "Thinking…").StatusLine(Now, Es));
        Assert.Equal("Te hizo una pregunta", Session(state: SessionState.NeedsInput, activity: "Asked you a question").StatusLine(Now, Es));
        Assert.Equal("Subagente: Explore", Session(state: SessionState.Working, activity: "Subagent: Explore").StatusLine(Now, Es));
        Assert.Equal("Bash: npm test", Session(state: SessionState.Working, activity: "Bash: npm test").StatusLine(Now, Es));
        Assert.Equal("Claude wants to know", Session(state: SessionState.NeedsInput, activity: "Claude wants to know").StatusLine(Now, Es));
        Assert.Equal("Thinking…", Session(state: SessionState.Working, activity: "Thinking…").StatusLine(Now));
    }

    [Fact(DisplayName = "the agent's errors and the overlay's notices in Spanish")]
    public void Notices()
    {
        Assert.Equal("sin conexión con el agente", Localization.Phrase("not connected to the agent", Es));
        Assert.Equal("ya está abierta en una terminal (pid 4164)", Localization.Phrase("already running in a terminal (pid 4164)", Es));
        Assert.Equal(@"su carpeta ya no existe: D:\R&D x", Localization.Phrase(@"its folder no longer exists: D:\R&D x", Es));
        Assert.Equal("cmd.exe terminó con el código 1: boom", Localization.Phrase("cmd.exe exited with 1: boom", Es));
        Assert.Equal("something new", Localization.Phrase("something new", Es));
        Assert.Equal("unknown session", Localization.Phrase("unknown session", Language.English));
    }

    [Fact(DisplayName = "summaries and ago in Spanish")]
    public void Summaries()
    {
        Assert.Equal("sin sesiones", Status.Summary([], Es));
        Assert.Equal("1 sesión", Status.Summary([Session()], Es));
        Assert.Equal("2 sesiones", Status.Summary([Session(), Session()], Es));
        Assert.Equal("1 te necesita", Status.Summary([Session(state: SessionState.NeedsInput), Session()], Es));
        Assert.Equal("2 te necesitan", Status.Summary([Session(state: SessionState.NeedsInput), Session(state: SessionState.NeedsPermission)], Es));
        Assert.Equal("ahora", Status.Ago(Now, Now, Es));
        Assert.Equal("3h", Status.Ago(Now.AddHours(-3), Now, Es));
    }

    [Fact(DisplayName = "every character and palette color has a Spanish name")]
    public void Names()
    {
        Assert.Equal(["Bichito", "Robot", "Fantasma", "Gato"], CharacterTemplate.All.Select(c => c.LabelIn(Es)));
        Assert.Equal(["Critter", "Robot", "Ghost", "Cat"], CharacterTemplate.All.Select(c => c.LabelIn(Language.English)));
        foreach (var (name, _) in SessionIcon.Palette) Assert.NotEqual(name, Localization.ColorName(name, Es));
    }
}
