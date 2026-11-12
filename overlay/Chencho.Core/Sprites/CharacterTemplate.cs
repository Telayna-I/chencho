using System.Text.Json;

namespace Chencho.Core;

/// <summary>Read from shared/characters.json.</summary>
/// <param name="Body">16 rows of 16 cells: B body, D darker, L lighter, F face plate, P pink, "." empty.</param>
/// <param name="Walk">Rows that replace the body from a row down on alternate frames while working.</param>
/// <param name="Eyes">Top-left corners of the two 2×2 eyes.</param>
/// <param name="EyeColor">Null for the default dark eyes.</param>
/// <param name="Labels">Names in other languages, by language code: { "es": "Gato" }. `Label` is the English one.</param>
public sealed record CharacterTemplate(
    string Id,
    string Label,
    string[] Body,
    CharacterWalk? Walk,
    CharacterEyes Eyes,
    Rgb? EyeColor,
    IReadOnlyDictionary<string, string>? Labels = null)
{
    public string LabelIn(Language language) =>
        language == Language.Spanish && Labels?.TryGetValue("es", out var spanish) == true ? spanish : Label;

    /// <summary>
    /// The default comes first. The JSON is embedded in the assembly, so failing to read it is a build bug.
    /// </summary>
    public static IReadOnlyList<CharacterTemplate> All { get; } = Load();

    /// <summary>Unknown ids get the default character.</summary>
    public static CharacterTemplate Named(string id) => All.FirstOrDefault(c => c.Id == id) ?? All[0];

    private static CharacterTemplate[] Load()
    {
        using var stream = typeof(CharacterTemplate).Assembly.GetManifestResourceStream("characters.json")
            ?? throw new InvalidOperationException("characters.json is missing from the Chencho.Core assembly");
        try
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("expected an array");
            var characters = document.RootElement.EnumerateArray().Select(Read).ToArray();
            if (characters.Length == 0) throw new JsonException("no characters");
            return characters;
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException("characters.json is malformed in the Chencho.Core assembly", e);
        }
    }

    private static CharacterTemplate Read(JsonElement json)
    {
        var walk = JsonRead.Optional(json, "walk") is { } w
            ? new CharacterWalk(JsonRead.Int(w, "row"), Strings(JsonRead.Field(w, "rows")))
            : null;
        var eyes = JsonRead.Field(json, "eyes");
        return new CharacterTemplate(
            Id: JsonRead.String(json, "id"),
            Label: JsonRead.String(json, "label"),
            Body: Strings(JsonRead.Field(json, "body")),
            Walk: walk,
            Eyes: new CharacterEyes(JsonRead.Int(eyes, "y"), JsonRead.Int(eyes, "left"), JsonRead.Int(eyes, "right")),
            // A color that isn't #rrggbb falls back to the default eyes.
            EyeColor: JsonRead.OptionalString(json, "eyeColor") is { } hex ? Rgb.FromHex(hex) : null,
            Labels: JsonRead.Optional(json, "labels") is { ValueKind: JsonValueKind.Object } labels
                ? labels.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!)
                : null);
    }

    private static string[] Strings(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new JsonException("expected a string"))
                .ToArray()
            : throw new JsonException("expected an array");
}

/// <summary>Rows that replace the body from <paramref name="Row"/> down on alternate frames while working.</summary>
public sealed record CharacterWalk(int Row, string[] Rows);

/// <summary>Top-left corners of the two 2×2 eyes: both on row <paramref name="Y"/>.</summary>
public readonly record struct CharacterEyes(int Y, int Left, int Right);
