using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fixtura.Spike.Settings;

// Settings flattened to configuration keys as appsettings.json would spell them, plus the paths that hold arrays.
internal sealed class SettingsOverlay
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { Converters = { new JsonStringEnumConverter() } };

    // appsettings.json allows comments and trailing commas, so pasted snippets should too.
    private static readonly JsonDocumentOptions DocumentOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public SettingsOverlay()
    {
    }

    private SettingsOverlay(JsonElement root)
    {
        Add("", root);
    }

    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ArrayPaths { get; } = [];

    public static SettingsOverlay FromObject(object settings) =>
        new(JsonSerializer.SerializeToElement(settings, SerializerOptions));

    public static SettingsOverlay FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
        return new SettingsOverlay(document.RootElement.Clone());
    }

    // A later overlay wins key by key, and its arrays replace earlier ones whole, as they replace the app's.
    public void Merge(SettingsOverlay later)
    {
        foreach (string array in later.ArrayPaths)
        {
            foreach (string key in Values.Keys.Where(key => IsUnder(key, array)).ToList())
            {
                Values.Remove(key);
            }

            if (!ArrayPaths.Contains(array, StringComparer.OrdinalIgnoreCase))
            {
                ArrayPaths.Add(array);
            }
        }

        foreach (KeyValuePair<string, string?> setting in later.Values)
        {
            Values[setting.Key] = setting.Value;
        }
    }

    internal static bool IsUnder(string key, string section) =>
        key.Equals(section, StringComparison.OrdinalIgnoreCase) || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);

    private void Add(string path, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Add(Join(path, property.Name), property.Value);
                }

                break;
            case JsonValueKind.Array:
                ArrayPaths.Add(path);
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Add(Join(path, index++.ToString()), item);
                }

                break;
            case JsonValueKind.String:
                Values[path] = element.GetString();
                break;
            case JsonValueKind.Null:
                Values[path] = null;
                break;
            default:
                Values[path] = element.GetRawText();
                break;
        }
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}:{key}";
}
