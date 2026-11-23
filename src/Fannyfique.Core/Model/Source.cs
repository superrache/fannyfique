namespace Fannyfique.Core.Model;

/// <summary>Dossier racine ajouté par l'utilisateur•ice, scanné récursivement (CLAUDE.md 4).</summary>
public sealed class Source
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Path { get; set; } = "";

    public SourceType Type { get; set; } = SourceType.Pc;

    public string Name { get; set; } = "";

    /// <summary>Numéro de série du volume, pour reconnaître un lecteur amovible même si sa lettre change.</summary>
    public string? VolumeSerial { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Les RAW de cette source ont été renommés (récupération de carte, ex. dossiers <c>recup_dir.1</c>) :
    /// ils sont rapprochés des autres copies sans tenir compte du nom (CLAUDE.md 5.2).
    /// </summary>
    public bool IgnoreFileNames { get; set; }

    /// <summary>Date du dernier scan complet de cette source (affichée « vu le … » quand elle est hors ligne).</summary>
    public DateTime? LastScanUtc { get; set; }

    public override string ToString() => $"{Name} ({Path})";
}
