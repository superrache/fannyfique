namespace Fannyfique.Core.Model;

/// <summary>Extensions des fichiers pris en compte (CLAUDE.md 5.1).</summary>
public static class FileKinds
{
    public static readonly IReadOnlyList<string> DefaultRawExtensions =
    [
        ".CR2", ".CR3", ".CRW", ".NEF", ".NRW", ".ARW", ".SR2", ".RAF", ".ORF",
        ".RW2", ".RWL", ".PEF", ".DNG", ".SRW", ".3FR", ".IIQ",
    ];

    /// <summary>Type de fichier annexe (signalé sur la carte, jamais compté comme doublon).</summary>
    public static AnnexKind? GetAnnexKind(string extension) => extension.ToUpperInvariant() switch
    {
        ".JPG" or ".JPEG" => AnnexKind.Jpeg,
        ".HEIC" => AnnexKind.Heic,
        ".XMP" => AnnexKind.Xmp,
        _ => null,
    };

    /// <summary>Dossiers jamais parcourus (CLAUDE.md 4.4).</summary>
    public static bool IsExcludedDirectory(string name) =>
        name.StartsWith('.')
        || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
}

public enum AnnexKind
{
    Jpeg,
    Heic,
    Xmp,
}
