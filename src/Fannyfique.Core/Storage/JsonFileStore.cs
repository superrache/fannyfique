using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fannyfique.Core.Storage;

/// <summary>Lecture et écriture atomique de fichiers JSON (CLAUDE.md 2, persistance).</summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Compact = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions Indented = CreateOptions(indented: true);

    public static T? Load<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Compact);
    }

    /// <summary>Écrit dans un fichier temporaire puis le substitue au fichier cible.</summary>
    public static void Save<T>(string path, T value, bool indented = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, value, indented ? Indented : Compact);
        }

        if (File.Exists(path))
        {
            File.Replace(tmp, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    private static JsonSerializerOptions CreateOptions(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
