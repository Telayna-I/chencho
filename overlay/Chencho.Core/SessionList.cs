namespace Chencho.Core;

/// <summary>The agent's sessions, kept current by its messages.</summary>
public sealed class SessionList
{
    private readonly Dictionary<string, PanelSession> sessions = [];

    public IReadOnlyDictionary<string, PanelSession> Sessions => sessions;

    public string? FocusedId { get; private set; }

    public void Apply(ServerMessage message)
    {
        switch (message)
        {
            case ServerMessage.Snapshot snapshot:
                sessions.Clear();
                // A repeated id keeps its last entry.
                foreach (var session in snapshot.Sessions) sessions[session.Id] = session;
                FocusedId = snapshot.FocusedId;
                break;
            case ServerMessage.SessionUpdate update:
                sessions[update.Session.Id] = update.Session;
                break;
            case ServerMessage.Removed removed:
                sessions.Remove(removed.Id);
                break;
            case ServerMessage.Focus focus:
                FocusedId = focus.Id;
                break;
            // Errors and library replies leave the list alone.
        }
    }

    /// <summary>
    /// Pinned sessions, then the active unpinned ones by name rather than activity, so creatures
    /// don't jump around as their states change.
    /// </summary>
    /// <remarks>
    /// Names compare in the current culture; equal names fall back to the id.
    /// </remarks>
    public IReadOnlyList<PanelSession> Shown(DateTimeOffset now)
    {
        var pinned = sessions.Values
            .Where(s => s.Pinned)
            .OrderBy(s => s.Position ?? 0)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
        var others = sessions.Values
            .Where(s => !s.Pinned && s.IsActive(now))
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
        return [.. pinned, .. others];
    }
}
