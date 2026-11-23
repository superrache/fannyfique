namespace Fannyfique.Core.Model;

/// <summary>Type d'une source (CLAUDE.md 4.1).</summary>
public enum SourceType
{
    Pc,
    ExternalDisk,
    Nas,
    SdCard,
}

public static class SourceTypeExtensions
{
    public static string Label(this SourceType type) => type switch
    {
        SourceType.Pc => "PC",
        SourceType.ExternalDisk => "Disque externe",
        SourceType.Nas => "NAS",
        SourceType.SdCard => "Carte SD",
        _ => type.ToString(),
    };
}
