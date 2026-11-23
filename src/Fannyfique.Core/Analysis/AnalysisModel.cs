using Fannyfique.Core.Indexing;
using Fannyfique.Core.Model;

namespace Fannyfique.Core.Analysis;

/// <summary>Un RAW dans un lot.</summary>
public sealed class LotFile(IndexedFile file, FileIdentity identity, Lot lot)
{
    public IndexedFile File { get; } = file;

    public FileIdentity Identity { get; } = identity;

    public Lot Lot { get; } = lot;

    public Copies Copies { get; internal set; } = null!;

    /// <summary>Copie considérée comme récupérable dans le calcul de l'espace gagné (CLAUDE.md 6.4).</summary>
    public bool Recoverable { get; internal set; }

    /// <summary>
    /// Fichier d'une source aux noms ignorés qui pourrait correspondre à plusieurs photos (rafale sans fractions de
    /// seconde, tailles identiques) : par prudence, il n'est rapproché d'aucune copie et compte comme unique.
    /// </summary>
    public bool Ambiguous { get; internal set; }

    public string FullPath => Path.Combine(Lot.Source.Path, File.RelativePath);
}

/// <summary>Toutes les copies d'un même RAW (même clé d'identité).</summary>
public sealed class Copies(FileIdentity identity)
{
    public FileIdentity Identity { get; } = identity;

    public List<LotFile> Files { get; } = [];

    /// <summary>Des copies ont des tailles différentes (CLAUDE.md 5.2).</summary>
    public bool Divergent { get; internal set; }

    public bool IsDuplicate => Files.Count > 1;

    /// <summary>Nom d'origine : celui d'une copie dont le nom fait foi, à défaut celui de la première copie.</summary>
    public string FileName => (Files.FirstOrDefault(f => !f.Lot.Source.IgnoreFileNames) ?? Files[0]).File.FileName;

    /// <summary>Autres noms portés par les copies (fichiers renommés).</summary>
    public IReadOnlyList<string> OtherNames => Files
        .Select(f => f.File.FileName)
        .Where(n => !string.Equals(n, FileName, StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public DateTime? DateTaken => Files[0].File.DateTaken;
}

/// <summary>Les RAW d'un même dossier, dans une source (CLAUDE.md 6.1).</summary>
public sealed class Lot(Source source, bool online, string relativeDirectory)
{
    public Source Source { get; } = source;

    public bool SourceOnline { get; } = online;

    public string RelativeDirectory { get; } = relativeDirectory;

    public string FullPath => Path.Combine(Source.Path, RelativeDirectory);

    public List<LotFile> Files { get; } = [];

    public FolderAnnexes? Annexes { get; internal set; }

    /// <summary>Lignes de résultats où figure ce lot (un lot peut être dans plusieurs lignes, CLAUDE.md 6.2).</summary>
    public List<DuplicateGroup> Groups { get; } = [];

    /// <summary>Numéro d'ordre, pour comparer des ensembles de lots.</summary>
    internal int Index { get; init; }

    public int FileCount => Files.Count;

    public long TotalBytes { get; internal set; }

    public DateTime? FirstDate { get; internal set; }

    public DateTime? LastDate { get; internal set; }

    public IReadOnlyList<string> Cameras { get; internal set; } = [];

    /// <summary>RAW présents dans au moins un autre lot.</summary>
    public int CommonCount { get; internal set; }

    /// <summary>RAW présents seulement dans ce lot.</summary>
    public int UniqueCount => FileCount - CommonCount;

    /// <summary>Part des RAW présents ailleurs, entre 0 et 1 (CLAUDE.md 6.3).</summary>
    public double Coverage => FileCount == 0 ? 0 : (double)CommonCount / FileCount;

    public bool IsFullyCopied => FileCount > 0 && CommonCount == FileCount;

    public int DivergentCount { get; internal set; }

    public int ProbableCount { get; internal set; }

    public int WeakCount { get; internal set; }

    /// <summary>RAW non rapprochés car ambigus sans leur nom (voir <see cref="LotFile.Ambiguous"/>).</summary>
    public int AmbiguousCount { get; internal set; }

    public long RecoverableBytes { get; internal set; }
}

/// <summary>Un lot dans une ligne de résultats : seulement ses RAW qui appartiennent à cette ligne.</summary>
public sealed class GroupMember(Lot lot, DuplicateGroup group)
{
    public Lot Lot { get; } = lot;

    public DuplicateGroup Group { get; } = group;

    /// <summary>RAW du lot rattachés à cette ligne.</summary>
    public List<LotFile> Files { get; } = [];

    public int Count => Files.Count;

    /// <summary>Part des RAW du lot rattachés à cette ligne, entre 0 et 1.</summary>
    public double Share => Lot.FileCount == 0 ? 0 : (double)Count / Lot.FileCount;

    /// <summary>Tous les RAW du lot sont dans cette ligne (et donc copiés ailleurs).</summary>
    public bool IsWhole => Count == Lot.FileCount;

    /// <summary>RAW du lot copiés ailleurs mais rattachés à d'autres lignes.</summary>
    public int ElsewhereCount => Lot.CommonCount - Count;

    public long RecoverableBytes { get; internal set; }
}

/// <summary>
/// Une ligne de résultats (CLAUDE.md 6.2) : des lots qui ont tous des RAW en commun.
/// Un lot peut figurer dans plusieurs lignes ; chaque RAW n'est rattaché qu'à une seule ligne.
/// </summary>
public sealed class DuplicateGroup
{
    public List<GroupMember> Members { get; } = [];

    public IEnumerable<Lot> Lots => Members.Select(m => m.Lot);

    /// <summary>Tous les RAW du groupe, une entrée par identité, triés par date puis nom.</summary>
    public List<Copies> Identities { get; } = [];

    public DateTime? FirstDate { get; internal set; }

    public DateTime? LastDate { get; internal set; }

    public IReadOnlyList<string> Cameras { get; internal set; } = [];

    public long RecoverableBytes { get; internal set; }

    public int DuplicateFileCount { get; internal set; }

    public bool HasSdCard => Members.Any(m => m.Lot.Source.Type == SourceType.SdCard);

    public bool HasIncompleteLot => Members.Any(m => !m.IsWhole);
}

/// <summary>Réponse à « puis-je vider cette carte SD ? » (CLAUDE.md 7.3).</summary>
public sealed class SdCardVerdict(Source source, bool online)
{
    public Source Source { get; } = source;

    public bool Online { get; } = online;

    public int FileCount { get; internal set; }

    public long TotalBytes { get; internal set; }

    /// <summary>RAW sans copie de même taille sur une source qui n'est pas une carte SD.</summary>
    public List<LotFile> OnlyHere { get; } = [];

    public List<Lot> LotsWithUniqueFiles { get; } = [];

    /// <summary>Nombre de RAW de la carte ayant une copie sur chaque autre source.</summary>
    public List<SourceCopyCount> CopiesBySource { get; } = [];

    /// <summary>RAW dont les seules copies sont sur des sources hors ligne.</summary>
    public int OfflineOnlyCount { get; internal set; }

    public long FreeableBytes { get; internal set; }

    public bool CanBeEmptied => OnlyHere.Count == 0;
}

public sealed record SourceCopyCount(Source Source, bool Online, int Count);

public sealed class AnalysisSummary
{
    public int RawCount { get; internal set; }

    public long RawBytes { get; internal set; }

    public int GroupCount { get; internal set; }

    /// <summary>RAW ayant au moins une autre copie.</summary>
    public int DuplicateFileCount { get; internal set; }

    public long RecoverableBytes { get; internal set; }

    public Dictionary<SourceType, long> RecoverableByType { get; } = [];
}

public sealed class AnalysisResult
{
    public List<DuplicateGroup> Groups { get; } = [];

    public List<SdCardVerdict> SdCards { get; } = [];

    public AnalysisSummary Summary { get; } = new();
}
