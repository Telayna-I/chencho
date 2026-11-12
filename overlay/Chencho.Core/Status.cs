namespace Chencho.Core;

public sealed partial record PanelSession
{
    public bool NeedsAttention => State is SessionState.NeedsPermission or SessionState.NeedsInput;

    /// <summary>
    /// Doing something or waiting on the user, or finished recently: the strip shows these even unpinned.
    /// </summary>
    public bool IsActive(DateTimeOffset now)
    {
        if (!Live) return false;
        return State switch
        {
            SessionState.Working or SessionState.Compacting or SessionState.NeedsPermission or SessionState.NeedsInput => true,
            SessionState.Done or SessionState.Error => Status.SecondsBetween(StateSince, now) <= Status.DozeAfterSeconds,
            _ => false,
        };
    }

    // Inside PanelSession, a bare `Mood` names this method, hence `Core.Mood` for the enum.
    public Mood Mood(DateTimeOffset now) => State switch
    {
        SessionState.Working => Core.Mood.Working,
        SessionState.NeedsPermission => Core.Mood.Alert,
        SessionState.NeedsInput => Core.Mood.Asking,
        SessionState.Compacting => Core.Mood.Compacting,
        SessionState.Error => Core.Mood.Error,
        SessionState.Ended => Core.Mood.Sleeping,
        SessionState.Idle => Status.SecondsBetween(StateSince, now) > Status.DozeAfterSeconds ? Core.Mood.Sleeping : Core.Mood.Idle,
        SessionState.Done => Status.SecondsBetween(StateSince, now) > Status.DozeAfterSeconds ? Core.Mood.Sleeping : Core.Mood.Finished,
        _ => throw new ArgumentOutOfRangeException(nameof(State), State, null),
    };

    /// <summary>The hover label's second line, e.g. "Bash: npm test" or "done · 5m".</summary>
    public string StatusLine(DateTimeOffset now, Language language = Language.English)
    {
        var activity = Activity is { } a ? Localization.Phrase(a, language) : null;
        var ago = Status.Ago(StateSince, now, language);
        return State switch
        {
            SessionState.Working => activity ?? language.Text("working", "trabajando"),
            SessionState.NeedsPermission => language.Text("needs permission", "necesita permiso"),
            SessionState.NeedsInput => activity ?? language.Text("waiting for you", "esperándote"),
            SessionState.Compacting => language.Text("compacting", "compactando"),
            SessionState.Error => activity is not null ? $"error · {activity}" : "error",
            SessionState.Idle => $"{language.Text("idle", "inactiva")} · {ago}",
            SessionState.Done => $"{language.Text("done", "terminó")} · {ago}",
            SessionState.Ended => $"{language.Text("closed", "cerrada")} · {ago}",
            _ => throw new ArgumentOutOfRangeException(nameof(State), State, null),
        };
    }
}

public static class Status
{
    /// <summary>Finished or idle sessions doze off after five minutes.</summary>
    internal const double DozeAfterSeconds = 5 * 60;

    /// <summary>What the strip holds, as the tab's status line.</summary>
    public static string Summary(IReadOnlyList<PanelSession> shown, Language language = Language.English)
    {
        var needy = shown.Count(s => s.NeedsAttention);
        if (needy > 0) return language.Text($"{needy} need{(needy == 1 ? "s" : "")} you", $"{needy} te necesita{(needy == 1 ? "" : "n")}");
        return shown.Count switch
        {
            0 => language.Text("no sessions", "sin sesiones"),
            1 => language.Text("1 session", "1 sesión"),
            _ => language.Text($"{shown.Count} sessions", $"{shown.Count} sesiones"),
        };
    }

    /// <summary>
    /// Whole seconds, minutes, hours or days; "now" under five seconds or for times ahead. The unit letters read
    /// the same in Spanish (segundos, minutos, horas, días).
    /// </summary>
    public static string Ago(DateTimeOffset since, DateTimeOffset now, Language language = Language.English)
    {
        var s = Math.Max(0, (long)SecondsBetween(since, now));
        return s switch
        {
            < 5 => language.Text("now", "ahora"),
            < 60 => $"{s}s",
            < 3600 => $"{s / 60}m",
            < 86400 => $"{s / 3600}h",
            _ => $"{s / 86400}d",
        };
    }

    internal static double SecondsBetween(DateTimeOffset since, DateTimeOffset now) => (now - since).TotalSeconds;
}
