namespace Fannyfique.Core.Storage;

/// <summary>Emplacements des fichiers de l'application : tout est écrit ici, jamais dans les sources.</summary>
public sealed class AppPaths(string dataDirectory)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fannyfique"));

    public string DataDirectory { get; } = dataDirectory;

    public string SettingsFile => Path.Combine(DataDirectory, "fannyfique.json");

    public string IndexFile => Path.Combine(DataDirectory, "index.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");
}
