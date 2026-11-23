namespace Fannyfique.Core.Indexing;

/// <summary>Un RAW connu de l'index (CLAUDE.md 5.3).</summary>
public sealed class IndexedFile
{
    public Guid SourceId { get; init; }

    /// <summary>Chemin relatif à la racine de la source.</summary>
    public string RelativePath { get; init; } = "";

    public long Size { get; init; }

    public DateTime LastWriteUtc { get; init; }

    public DateTime? DateTaken { get; init; }

    public string? Serial { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public MetadataStatus Status { get; init; }

    public DateTime LastSeenUtc { get; set; }

    public string FileName => Path.GetFileName(RelativePath);

    public string RelativeDirectory => Path.GetDirectoryName(RelativePath) ?? "";

    public string? Camera => (Make, Model) switch
    {
        (null, null) => null,
        (_, null) => Make,
        (null, _) => Model,
        // Beaucoup de fabricants répètent la marque dans le modèle : « Canon » + « Canon EOS R6 »,
        // « NIKON CORPORATION » + « NIKON D750 ».
        _ => Model!.StartsWith(Make!.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ? Model : $"{Make} {Model}",
    };
}

public enum MetadataStatus
{
    /// <summary>Métadonnées lues.</summary>
    Ok,

    /// <summary>Format non reconnu : le fichier ne sera pas relu tant qu'il ne change pas.</summary>
    Unreadable,

    /// <summary>Erreur de lecture (disque, réseau, droits) : le fichier sera relu au prochain scan.</summary>
    Error,
}
