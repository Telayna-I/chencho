// Mirrors shared/protocol.ts; keep the two in step.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Chencho.Core;

public enum SessionState
{
    /// <summary>Started, no prompt yet.</summary>
    Idle,
    Working,
    NeedsPermission,
    /// <summary>A question, a plan to review, or otherwise waiting on the user.</summary>
    NeedsInput,
    Compacting,
    /// <summary>Finished its turn: the user's move.</summary>
    Done,
    Error,
    /// <summary>Process gone; the session itself can be reopened.</summary>
    Ended,
}

public static class SessionStateExtensions
{
    /// <summary>The name on the wire, e.g. <c>needs_permission</c>.</summary>
    public static string WireName(this SessionState state) => state switch
    {
        SessionState.Idle => "idle",
        SessionState.Working => "working",
        SessionState.NeedsPermission => "needs_permission",
        SessionState.NeedsInput => "needs_input",
        SessionState.Compacting => "compacting",
        SessionState.Done => "done",
        SessionState.Error => "error",
        SessionState.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>Unknown states from a newer agent read as idle instead of failing the whole message.</summary>
    public static SessionState FromWireName(string name) => name switch
    {
        "working" => SessionState.Working,
        "needs_permission" => SessionState.NeedsPermission,
        "needs_input" => SessionState.NeedsInput,
        "compacting" => SessionState.Compacting,
        "done" => SessionState.Done,
        "error" => SessionState.Error,
        "ended" => SessionState.Ended,
        _ => SessionState.Idle,
    };
}

/// <param name="Character">Sprite id from characters.json.</param>
/// <param name="Color"><c>#rrggbb</c></param>
public sealed partial record SessionIcon(string Character, string Color)
{
    public static SessionIcon Default { get; } = new("default", "#d97757");

    /// <summary>The colors a session icon can take.</summary>
    public static IReadOnlyList<(string Name, string Color)> Palette { get; } =
    [
        ("Orange", "#ff6b1a"), ("Clay", "#d97757"), ("Yellow", "#ffd23f"), ("Green", "#4cd964"), ("Cyan", "#3ccfe6"),
        ("Blue", "#4f7cff"), ("Purple", "#a66cff"), ("Pink", "#ff5fa2"), ("White", "#e8e8e8"),
    ];

    internal static SessionIcon Read(JsonElement json) =>
        new(JsonRead.String(json, "character"), JsonRead.String(json, "color"));
}

/// <param name="Activity">Short detail for the tile, e.g. "Bash: npm test".</param>
/// <param name="Live">The Claude process is running.</param>
/// <param name="StateSince">When the state last changed.</param>
/// <param name="Position">Order among pinned sessions; null when not pinned.</param>
/// <param name="Pid">The Claude process id; null when unknown or not running.</param>
public sealed partial record PanelSession(
    string Id,
    string Name,
    string Cwd,
    SessionState State,
    string? Activity,
    bool Live,
    DateTimeOffset StateSince,
    bool Pinned,
    int? Position,
    SessionIcon Icon,
    int? Pid = null)
{
    internal static PanelSession Read(JsonElement json) => new(
        Id: JsonRead.String(json, "id"),
        Name: JsonRead.String(json, "name"),
        Cwd: JsonRead.String(json, "cwd"),
        State: SessionStateExtensions.FromWireName(JsonRead.String(json, "state")),
        Activity: JsonRead.OptionalString(json, "activity"),
        Live: JsonRead.Bool(json, "live"),
        StateSince: JsonRead.EpochMilliseconds(json, "stateSince"),
        Pinned: JsonRead.Bool(json, "pinned"),
        Position: JsonRead.OptionalInt(json, "position"),
        Icon: SessionIcon.Read(JsonRead.Field(json, "icon")),
        Pid: JsonRead.OptionalInt(json, "pid"));
}

/// <summary>A desktop-app session that can be pinned, running or not.</summary>
public sealed record LibraryEntry(string Id, string Name, string Cwd, DateTimeOffset LastActivityAt, bool Pinned)
{
    internal static LibraryEntry Read(JsonElement json) => new(
        Id: JsonRead.String(json, "id"),
        Name: JsonRead.String(json, "name"),
        Cwd: JsonRead.String(json, "cwd"),
        LastActivityAt: JsonRead.EpochMilliseconds(json, "lastActivityAt"),
        Pinned: JsonRead.Bool(json, "pinned"));
}

public abstract record ServerMessage
{
    private ServerMessage() { }

    /// <summary>Equal when their sessions are, in order.</summary>
    public sealed record Snapshot(IReadOnlyList<PanelSession> Sessions, string? FocusedId) : ServerMessage
    {
        public bool Equals(Snapshot? other) =>
            other is not null && FocusedId == other.FocusedId && Sessions.SequenceEqual(other.Sessions);

        public override int GetHashCode() => HashCode.Combine(Sessions.Count, FocusedId);
    }

    public sealed record SessionUpdate(PanelSession Session) : ServerMessage;

    public sealed record Removed(string Id) : ServerMessage;

    /// <summary>The session in front in the desktop app; null when none.</summary>
    public sealed record Focus(string? Id) : ServerMessage;

    public sealed record Error(string Message) : ServerMessage;

    /// <summary>Reply to a library request, most recent first. Equal when their entries are, in order.</summary>
    public sealed record Library(IReadOnlyList<LibraryEntry> Sessions) : ServerMessage
    {
        public bool Equals(Library? other) => other is not null && Sessions.SequenceEqual(other.Sessions);

        public override int GetHashCode() => Sessions.Count;
    }

    /// <summary>Null for malformed or unknown messages.</summary>
    public static ServerMessage? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return JsonRead.String(root, "type") switch
            {
                "snapshot" => new Snapshot(
                    Lenient(JsonRead.Field(root, "sessions"), PanelSession.Read),
                    JsonRead.OptionalString(root, "focusedId")),
                "session" => new SessionUpdate(PanelSession.Read(JsonRead.Field(root, "session"))),
                "removed" => new Removed(JsonRead.String(root, "id")),
                "focus" => new Focus(JsonRead.OptionalString(root, "id")),
                "error" => new Error(JsonRead.String(root, "message")),
                "library" => new Library(Lenient(JsonRead.Field(root, "sessions"), LibraryEntry.Read)),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lets one unreadable session be skipped instead of failing the whole snapshot.</summary>
    private static T[] Lenient<T>(JsonElement array, Func<JsonElement, T> read)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new JsonException("expected an array");
        var values = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            try
            {
                values.Add(read(item));
            }
            catch (JsonException)
            {
                // Skipped; the rest still count.
            }
        }
        return values.ToArray();
    }
}

/// <remarks><c>Pin</c> and <c>Icon</c> also accept a desktop session that isn't running: the agent starts tracking it.</remarks>
public abstract record ClientMessage
{
    private ClientMessage() { }

    public sealed record Open(string Id) : ClientMessage;

    public sealed record Pin(string Id, bool Pinned) : ClientMessage;

    /// <summary>The agent stores icons with pins, so this also pins the session, and unpinning resets its icon.</summary>
    public sealed record Icon(string Id, SessionIcon SessionIcon) : ClientMessage;

    /// <summary>Asks for the desktop sessions that can be pinned; answered with <see cref="ServerMessage.Library"/>.</summary>
    public sealed record LibraryRequest : ClientMessage;

    public string? SessionId => this switch
    {
        Open open => open.Id,
        Pin pin => pin.Id,
        Icon icon => icon.Id,
        _ => null,
    };

    /// <summary>
    /// Compact, with sorted keys, escaped like Foundation's JSONSerialization (which also escapes "/"), so the agent
    /// receives the same text from either overlay.
    /// </summary>
    public string Json => this switch
    {
        Open open => $$"""{"id":{{Quote(open.Id)}},"type":"open"}""",
        Pin pin => $$"""{"id":{{Quote(pin.Id)}},"pinned":{{(pin.Pinned ? "true" : "false")}},"type":"pin"}""",
        Icon icon => $$"""{"icon":{"character":{{Quote(icon.SessionIcon.Character)}},"color":{{Quote(icon.SessionIcon.Color)}}},"id":{{Quote(icon.Id)}},"type":"icon"}""",
        LibraryRequest => """{"type":"library"}""",
        _ => throw new UnreachableException(),
    };

    private static string Quote(string value)
    {
        var json = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '/': json.Append("\\/"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                case < ' ': json.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: json.Append(c); break;
            }
        }
        return json.Append('"').ToString();
    }
}

/// <summary>
/// Reads fields strictly: a required field must be present, not null, and of its type; an optional one may be
/// missing or null, but not of another type. Every failure is a <see cref="JsonException"/>.
/// </summary>
internal static class JsonRead
{
    public static JsonElement? Optional(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw new JsonException("expected an object");
        return obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
    }

    public static JsonElement Field(JsonElement obj, string key) =>
        Optional(obj, key) ?? throw new JsonException($"\"{key}\" is missing or null");

    public static string String(JsonElement obj, string key) => AsString(Field(obj, key), key);

    public static string? OptionalString(JsonElement obj, string key) =>
        Optional(obj, key) is { } value ? AsString(value, key) : null;

    public static bool Bool(JsonElement obj, string key) => Field(obj, key).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new JsonException($"\"{key}\" is not a boolean"),
    };

    public static int Int(JsonElement obj, string key) => AsInt(Field(obj, key), key);

    public static int? OptionalInt(JsonElement obj, string key) =>
        Optional(obj, key) is { } value ? AsInt(value, key) : null;

    /// <summary>The agent sends times as epoch milliseconds.</summary>
    public static DateTimeOffset EpochMilliseconds(JsonElement obj, string key)
    {
        var value = Field(obj, key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var ms))
            throw new JsonException($"\"{key}\" is not a number");
        try
        {
            return DateTimeOffset.UnixEpoch.AddTicks(checked((long)Math.Round(ms * TimeSpan.TicksPerMillisecond)));
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
        {
            throw new JsonException($"\"{key}\" is out of range", e);
        }
    }

    private static string AsString(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException($"\"{key}\" is not a string");

    private static int AsInt(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)
            ? i
            : throw new JsonException($"\"{key}\" is not an integer");
}
