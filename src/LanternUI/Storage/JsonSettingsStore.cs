using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lantern.UI;

/// <summary>
/// settings.json: a flat map of string values with defaults. Unknown keys are kept, so a newer
/// build's settings survive an older one (and a hand-edited file is never rewritten away).
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _defaults;
    private readonly JsonObject _data = new();

    public JsonSettingsStore(string path, IReadOnlyDictionary<string, string>? defaults = null)
    {
        _path = path;
        _defaults = defaults == null ? new Dictionary<string, string>() : new Dictionary<string, string>(defaults);
        Load();
    }

    public string Path => _path;

    public void Load()
    {
        foreach (var pair in _defaults) _data[pair.Key] = pair.Value;

        try
        {
            if (!File.Exists(_path)) return;
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject saved) return;
            foreach (var pair in saved)
                _data[pair.Key] = Text(pair.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file must not stop the launcher: defaults are already in place.
        }
    }

    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var copy = new JsonObject();
        foreach (var pair in _data) copy[pair.Key] = pair.Value?.DeepClone();
        File.WriteAllText(_path, copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public string Str(string key) => _data.TryGetPropertyValue(key, out var value) ? Text(value) : "";

    public void Set(string key, string value) => _data[key] = value ?? "";

    public bool Bool(string key) => string.Equals(Str(key), "true", StringComparison.OrdinalIgnoreCase);

    public void SetBool(string key, bool value) => _data[key] = value ? "true" : "false";

    /// <summary>Every value as text, whatever JSON type it was written with.</summary>
    private static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value => value.ToJsonString(),
        _ => node.ToJsonString(),
    };
}
