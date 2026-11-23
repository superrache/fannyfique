namespace Fannyfique.Core.Storage;

/// <summary>
/// Seul point d'ouverture des fichiers des sources : lecture seule, sans gêner les autres
/// programmes (CLAUDE.md 3.3).
/// </summary>
public static class SafeFile
{
    public static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 16 * 1024,
        FileOptions.RandomAccess);
}
