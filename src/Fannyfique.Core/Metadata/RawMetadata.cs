namespace Fannyfique.Core.Metadata;

/// <summary>Métadonnées utiles à l'identité d'un RAW (CLAUDE.md 5.2).</summary>
public sealed record RawMetadata(DateTime? DateTaken, string? SerialNumber, string? Make, string? Model)
{
    public static readonly RawMetadata Empty = new(null, null, null, null);
}

public interface IRawMetadataReader
{
    /// <summary>Lit les métadonnées d'un fichier, en lecture seule.</summary>
    /// <exception cref="RawFormatException">Le fichier est lisible mais son format n'est pas reconnu.</exception>
    /// <exception cref="IOException">Le fichier n'a pas pu être lu.</exception>
    RawMetadata Read(string path);
}

public sealed class RawFormatException(string message, Exception? inner = null) : Exception(message, inner);
