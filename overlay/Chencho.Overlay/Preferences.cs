using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chencho.Core;

namespace Chencho.Overlay;

/// The strip's edge and collapsed state, kept in `overlay.json` beside the agent's data.
internal static class Preferences
{
    private static readonly string FilePath = Path.Combine(AgentConfig.DataDirectory, "overlay.json");
    private static JsonObject? store;

    private static JsonObject Store
    {
        get
        {
            if (store is not null) return store;
            try { store = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject; }
            catch (Exception) { /* missing or unreadable: defaults */ }
            return store ??= new JsonObject();
        }
    }

    public static ScreenEdge Edge
    {
        get => Store["edge"]?.GetValue<string>() == "left" ? ScreenEdge.Left : ScreenEdge.Right;
        set => Save("edge", value == ScreenEdge.Left ? "left" : "right");
    }

    public static bool Collapsed
    {
        get => Store["collapsed"]?.GetValue<bool>() ?? false;
        set => Save("collapsed", value);
    }

    /// <summary>"auto" follows Windows' display language; "en" or "es" picks one.</summary>
    public static string Language
    {
        get => Store["language"] is JsonValue value && value.TryGetValue<string>(out var language) && language is "en" or "es" ? language : "auto";
        set => Save("language", value);
    }

    private static void Save(string key, JsonNode value)
    {
        Store[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".new";
            File.WriteAllText(temporary, Store.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Write($"could not save preferences: {error.Message}");
        }
    }
}
