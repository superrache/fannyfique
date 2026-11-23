namespace Fannyfique.Core.Platform;

/// <summary>Informations sur les volumes (implémentation Windows dans l'application).</summary>
public interface IVolumeInfoProvider
{
    IReadOnlyList<VolumeInfo> GetRemovableVolumes();

    VolumeInfo? GetVolume(string path);
}

public sealed record VolumeInfo(string RootPath, string? Label, string? Serial, bool IsRemovable);

/// <summary>Ouverture de l'Explorateur de fichiers.</summary>
public interface IFileExplorer
{
    void OpenFolder(string path);

    void SelectFile(string path);
}
