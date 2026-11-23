namespace Fannyfique.Core.Indexing;

/// <summary>Contenu sérialisé de <c>index.json</c>.</summary>
public sealed class IndexData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<IndexedFile> Files { get; set; } = [];

    public List<FolderAnnexes> Annexes { get; set; } = [];

    /// <summary>Bilans des scans précédents, pour afficher le progrès (CLAUDE.md 7.5).</summary>
    public List<ScanSummaryEntry> History { get; set; } = [];
}

public sealed record ScanSummaryEntry(DateTime DateUtc, long RecoverableBytes, int SdCardsToSort, int SdCardsReady);
