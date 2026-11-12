using System.Text.RegularExpressions;

namespace Chencho.Core;

public enum Language { English, Spanish }

/// <summary>
/// The overlay's words in English and Spanish. The agent speaks English on the wire, so its fixed phrases are
/// translated here, when they're shown. Anything else it relays, such as a tool's command or a message Claude Code
/// wrote, is shown as it came.
/// </summary>
public static class Localization
{
    /// <summary>`preference` is "en", "es" or anything else for the system's language (`culture`, e.g. "es").</summary>
    public static Language Resolve(string? preference, string culture) => preference switch
    {
        "en" => Language.English,
        "es" => Language.Spanish,
        _ => culture.Equals("es", StringComparison.OrdinalIgnoreCase) ? Language.Spanish : Language.English,
    };

    public static string Text(this Language language, string english, string spanish) =>
        language == Language.Spanish ? spanish : english;

    private static readonly Dictionary<string, string> Phrases = new()
    {
        ["Thinking…"] = "Pensando…",
        ["Asked you a question"] = "Te hizo una pregunta",
        ["Plan ready for review"] = "Plan listo para revisar",
        ["Needs your permission"] = "Necesita tu permiso",
        ["Waiting for you"] = "Esperándote",
        ["Compacting context"] = "Compactando el contexto",
        ["Error"] = "Error",
        // The overlay's own notices, and the agent's replies to what it sent.
        ["not connected to the agent"] = "sin conexión con el agente",
        ["can't find its terminal"] = "no se encontró su terminal",
        ["invalid message"] = "mensaje inválido",
        ["unknown session"] = "sesión desconocida",
        ["invalid session id"] = "id de sesión inválido",
        ["its transcript is gone"] = "su transcript ya no existe",
    };

    private static readonly (Regex Pattern, string Spanish)[] Patterns =
    [
        (new Regex(@"^Subagent: (.*)$"), "Subagente: $1"),
        (new Regex(@"^already running in a terminal \(pid (\d+)\)$"), "ya está abierta en una terminal (pid $1)"),
        (new Regex(@"^its folder no longer exists: (.*)$"), "su carpeta ya no existe: $1"),
        (new Regex(@"^(\S+) exited with (-?\d+)(.*)$"), "$1 terminó con el código $2$3"),
    ];

    /// <summary>One of the agent's activity lines or error messages, or one of the overlay's notices.</summary>
    public static string Phrase(string english, Language language)
    {
        if (language == Language.English) return english;
        if (Phrases.TryGetValue(english, out var spanish)) return spanish;
        foreach (var (pattern, replacement) in Patterns)
            if (pattern.IsMatch(english)) return pattern.Replace(english, replacement);
        return english;
    }

    private static readonly Dictionary<string, string> ColorNames = new()
    {
        ["Orange"] = "Naranja", ["Clay"] = "Arcilla", ["Yellow"] = "Amarillo", ["Green"] = "Verde", ["Cyan"] = "Cian",
        ["Blue"] = "Azul", ["Purple"] = "Violeta", ["Pink"] = "Rosa", ["White"] = "Blanco",
    };

    /// <summary>A name from <see cref="SessionIcon.Palette"/>.</summary>
    public static string ColorName(string english, Language language) =>
        language == Language.Spanish && ColorNames.TryGetValue(english, out var spanish) ? spanish : english;
}
