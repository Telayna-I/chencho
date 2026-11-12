using System.Text;
using System.Text.Json;

namespace Chencho.Core;

public static class AgentConfig
{
    /// <summary>The agent's WebSocket port: PANEL_PORT in shared/protocol.ts.</summary>
    public const int PanelPort = 7778;

    /// <summary>Where the agent keeps its data on Windows: <c>%LOCALAPPDATA%\chencho</c>. Mirrors agent/src/config.ts.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chencho");

    /// <summary>Written by the agent: <c>{"token": "..."}</c>.</summary>
    public static string ConfigFile { get; } = Path.Combine(DataDirectory, "config.json");

    /// <summary>The pairing token from <paramref name="path"/>, by default <see cref="ConfigFile"/>; null when there is none.</summary>
    public static string? Token(string? path = null)
    {
        string json;
        try
        {
            json = File.ReadAllText(path ?? ConfigFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return TokenFromJson(json);
    }

    /// <summary>Null when the JSON is unreadable or its token is missing or empty.</summary>
    public static string? TokenFromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonRead.OptionalString(document.RootElement, "token") is { Length: > 0 } token ? token : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Uri WebSocketUrl(string token) =>
        new($"ws://127.0.0.1:{PanelPort}/ws?token={EscapeQueryValue(token)}");

    /// <summary>
    /// Percent-encodes UTF-8 bytes outside Foundation's <c>urlQueryAllowed</c> set, and "+", "=" and "&amp;" too:
    /// URLComponents leaves "+" and "=" alone in queries, and the agent would read "+" as a space.
    /// </summary>
    private static string EscapeQueryValue(string value)
    {
        const string allowedPunctuation = "!$'()*,-./:;?@_~";
        var escaped = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || allowedPunctuation.Contains(c)) escaped.Append(c);
            else escaped.Append('%').Append(b.ToString("X2"));
        }
        return escaped.ToString();
    }
}
