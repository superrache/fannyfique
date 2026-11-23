using System.Globalization;
using Fannyfique.Core.Indexing;

namespace Fannyfique.Core.Analysis;

/// <summary>Niveau de confiance dans l'identité d'un RAW (CLAUDE.md 5.2).</summary>
public enum IdentityLevel
{
    /// <summary>Nom + date de prise de vue + numéro de série du boîtier.</summary>
    Certain,

    /// <summary>Nom + date de prise de vue + marque et modèle (pas de numéro de série).</summary>
    Probable,

    /// <summary>Nom + taille (métadonnées illisibles).</summary>
    Weak,
}

/// <summary>Clé d'identité : deux RAW de même clé sont des copies l'un de l'autre.</summary>
public readonly record struct FileIdentity(string Key, IdentityLevel Level)
{
    public static FileIdentity Of(IndexedFile file)
    {
        var name = file.FileName.ToUpperInvariant();
        return WithoutName(file) is { } key
            ? new($"{name}|{key.Key}", key.Level)
            : new($"{name}|B:{file.Size}", IdentityLevel.Weak);
    }

    /// <summary>
    /// Clé sans le nom de fichier, pour les sources aux fichiers renommés : date de prise de vue et boîtier.
    /// Nulle si la date est inconnue (rien ne permet alors de rapprocher le fichier).
    /// </summary>
    public static FileIdentity? WithoutName(IndexedFile file)
    {
        if (file.DateTaken is not { } date)
        {
            return null;
        }

        var when = date.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(file.Serial)
            ? new($"{when}|M:{file.Camera?.ToUpperInvariant()}", IdentityLevel.Probable)
            : new($"{when}|S:{file.Serial.Trim().ToUpperInvariant()}", IdentityLevel.Certain);
    }
}
