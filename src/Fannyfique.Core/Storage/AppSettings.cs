using Fannyfique.Core.Model;

namespace Fannyfique.Core.Storage;

/// <summary>Contenu de <c>fannyfique.json</c> (CLAUDE.md 8).</summary>
public sealed class AppSettings
{
    public List<Source> Sources { get; set; } = [];

    public List<string> RawExtensions { get; set; } = [.. FileKinds.DefaultRawExtensions];

    /// <summary>Nombre de lectures simultanées par source, selon son type (CLAUDE.md 5.3).</summary>
    public Dictionary<SourceType, int> Parallelism { get; set; } = new()
    {
        [SourceType.Pc] = 4,
        [SourceType.ExternalDisk] = 4,
        [SourceType.Nas] = 2,
        [SourceType.SdCard] = 4,
    };

    /// <summary>Ordre de préférence de la copie conservée dans le calcul de l'espace gagné (CLAUDE.md 6.4).</summary>
    public List<SourceType> KeepOrder { get; set; } =
        [SourceType.Nas, SourceType.ExternalDisk, SourceType.Pc, SourceType.SdCard];

    /// <summary>Faux : les copies sur des sources hors ligne sont ignorées dans les résultats (CLAUDE.md 4.3).</summary>
    public bool IgnoreOfflineSources { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Auto;

    public int ParallelismFor(SourceType type) => Math.Max(1, Parallelism.GetValueOrDefault(type, 4));
}

public enum AppTheme
{
    Auto,
    Light,
    Dark,
}
