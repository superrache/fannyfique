using Fannyfique.Core.Model;

namespace Fannyfique.Core.Indexing;

/// <summary>Fichiers annexes (JPEG, HEIC, XMP) d'un dossier contenant des RAW (CLAUDE.md 5.1).</summary>
public sealed class FolderAnnexes
{
    public Guid SourceId { get; init; }

    public string RelativeDirectory { get; init; } = "";

    public int Jpeg { get; set; }

    public int Heic { get; set; }

    public int Xmp { get; set; }

    public bool IsEmpty => Jpeg == 0 && Heic == 0 && Xmp == 0;

    public void Add(AnnexKind kind)
    {
        switch (kind)
        {
            case AnnexKind.Jpeg: Jpeg++; break;
            case AnnexKind.Heic: Heic++; break;
            case AnnexKind.Xmp: Xmp++; break;
        }
    }
}
