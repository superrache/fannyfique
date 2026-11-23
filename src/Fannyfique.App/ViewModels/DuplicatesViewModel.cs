using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fannyfique.Core.Analysis;
using Fannyfique.Core.Formatting;
using Fannyfique.Core.Model;
using Fannyfique.Core.Platform;

namespace Fannyfique.App.ViewModels;

public enum GroupSort
{
    Recoverable,
    Date,
    Copies,
}

/// <summary>Écran Doublons (CLAUDE.md 7.4).</summary>
public sealed partial class DuplicatesViewModel(IFileExplorer explorer) : ObservableObject
{
    private List<GroupViewModel> _all = [];

    [ObservableProperty]
    private IReadOnlyList<GroupViewModel> _groups = [];

    [ObservableProperty]
    private IReadOnlyList<SummaryTile> _tiles = [];

    [ObservableProperty]
    private IReadOnlyList<DonutSegment> _segments = [];

    [ObservableProperty]
    private string _recoverableText = "";

    [ObservableProperty]
    private IReadOnlyList<SourceFilter> _sourceFilters = [];

    [ObservableProperty]
    private SourceFilter? _selectedSource;

    [ObservableProperty]
    private TypeFilterOption _selectedType = TypeFilterOption.All[0];

    [ObservableProperty]
    private bool _onlySdCards;

    [ObservableProperty]
    private bool _onlyIncomplete;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private DateTime? _from;

    [ObservableProperty]
    private DateTime? _to;

    [ObservableProperty]
    private SortOption _selectedSort = SortOption.All[0];

    [ObservableProperty]
    private string _resultText = "";

    public bool HasAnyGroup => _all.Count > 0;

    public bool NoMatch => _all.Count > 0 && Groups.Count == 0;

    public IReadOnlyList<TypeFilterOption> TypeFilters => TypeFilterOption.All;

    public IReadOnlyList<SortOption> SortOptions => SortOption.All;

    public void Load(AnalysisResult analysis, IReadOnlyList<Source> sources)
    {
        _all = analysis.Groups.Select(g => new GroupViewModel(g, explorer)).ToList();
        var summary = analysis.Summary;
        RecoverableText = Format.Bytes(summary.RecoverableBytes);
        Tiles =
        [
            new SummaryTile(Format.Count(summary.GroupCount), summary.GroupCount > 1 ? "lots en double" : "lot en double"),
            new SummaryTile(Format.Count(summary.DuplicateFileCount), "RAW en plusieurs copies"),
            new SummaryTile(Format.Count(summary.RawCount), $"RAW au total · {Format.Bytes(summary.RawBytes)}"),
        ];
        Segments = Enum.GetValues<SourceType>()
            .Select(t => new DonutSegment(t, summary.RecoverableByType.GetValueOrDefault(t), $"{t.Label()} : {Format.Bytes(summary.RecoverableByType.GetValueOrDefault(t))}"))
            .Where(s => s.Value > 0)
            .ToList();

        var selectedId = SelectedSource?.Source?.Id;
        SourceFilters = [new SourceFilter(null, "Toutes les sources"), .. sources.Where(s => s.Enabled).Select(s => new SourceFilter(s, s.Name))];
        SelectedSource = SourceFilters.FirstOrDefault(f => f.Source?.Id == selectedId) ?? SourceFilters[0];
        OnPropertyChanged(nameof(HasAnyGroup));
        ApplyFilters();
    }

    partial void OnSelectedSourceChanged(SourceFilter? value) => ApplyFilters();

    partial void OnSelectedTypeChanged(TypeFilterOption value) => ApplyFilters();

    partial void OnOnlySdCardsChanged(bool value) => ApplyFilters();

    partial void OnOnlyIncompleteChanged(bool value) => ApplyFilters();

    partial void OnSearchChanged(string value) => ApplyFilters();

    partial void OnFromChanged(DateTime? value) => ApplyFilters();

    partial void OnToChanged(DateTime? value) => ApplyFilters();

    partial void OnSelectedSortChanged(SortOption value) => ApplyFilters();

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedSource = SourceFilters.FirstOrDefault();
        SelectedType = TypeFilterOption.All[0];
        OnlySdCards = false;
        OnlyIncomplete = false;
        Search = "";
        From = null;
        To = null;
    }

    private void ApplyFilters()
    {
        IEnumerable<GroupViewModel> query = _all.Where(g => g.Matches(
            SelectedSource?.Source, SelectedType?.Type, OnlySdCards, OnlyIncomplete, Search.Trim(), From, To));
        query = (SelectedSort?.Sort ?? GroupSort.Recoverable) switch
        {
            GroupSort.Date => query.OrderByDescending(g => g.Group.LastDate ?? DateTime.MinValue),
            GroupSort.Copies => query.OrderByDescending(g => g.Group.Members.Count).ThenByDescending(g => g.Group.RecoverableBytes),
            _ => query.OrderByDescending(g => g.Group.RecoverableBytes),
        };
        Groups = query.ToList();
        ResultText = Groups.Count == _all.Count
            ? Format.Plural(_all.Count, "ligne")
            : $"{Format.Count(Groups.Count)} sur {Format.Plural(_all.Count, "ligne")}";
        OnPropertyChanged(nameof(NoMatch));
    }
}

public sealed record SummaryTile(string Value, string Label);

public sealed record DonutSegment(SourceType Type, double Value, string Label);

public sealed record SourceFilter(Source? Source, string Label);

public sealed record TypeFilterOption(SourceType? Type, string Label)
{
    public static IReadOnlyList<TypeFilterOption> All { get; } =
        [new(null, "Tous les types"), .. Enum.GetValues<SourceType>().Select(t => new TypeFilterOption(t, t.Label()))];
}

public sealed record SortOption(GroupSort Sort, string Label)
{
    public static IReadOnlyList<SortOption> All { get; } =
    [
        new(GroupSort.Recoverable, "Espace récupérable"),
        new(GroupSort.Date, "Date de prise de vue"),
        new(GroupSort.Copies, "Nombre de copies"),
    ];
}

/// <summary>Une ligne : un groupe de lots liés (CLAUDE.md 6.2).</summary>
public sealed partial class GroupViewModel : ObservableObject
{
    private IReadOnlyList<IdentityRowViewModel>? _rows;

    public GroupViewModel(DuplicateGroup group, IFileExplorer explorer)
    {
        Group = group;
        Cards = group.Members.Select(m => new LotCardViewModel(m, explorer)).ToList();
    }

    public DuplicateGroup Group { get; }

    public IReadOnlyList<LotCardViewModel> Cards { get; }

    public string DateText => Format.DateRange(Group.FirstDate, Group.LastDate);

    public string CamerasText => Group.Cameras.Count == 0 ? "Boîtier inconnu" : string.Join(", ", Group.Cameras);

    public string CopiesText => $"{Format.Plural(Group.Members.Count, "copie")} · {Format.Plural(Group.Identities.Count, "RAW", "RAW")}";

    public string RecoverableText => Group.RecoverableBytes > 0 ? Format.Bytes(Group.RecoverableBytes) : "0 o";

    public bool HasSdCard => Group.HasSdCard;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Tableau fichier × copie, construit à la première ouverture.</summary>
    public IReadOnlyList<IdentityRowViewModel> Rows => _rows ??= BuildRows();

    private List<IdentityRowViewModel> BuildRows()
    {
        var lots = Group.Lots.ToList();
        return Group.Identities
            .Select(c => new IdentityRowViewModel(c, lots))
            .ToList();
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public bool Matches(Source? source, SourceType? type, bool onlySd, bool onlyIncomplete, string search, DateTime? from, DateTime? to)
    {
        if (source is not null && Group.Lots.All(l => l.Source.Id != source.Id))
        {
            return false;
        }

        if (type is { } t && Group.Lots.All(l => l.Source.Type != t))
        {
            return false;
        }

        if (onlySd && !Group.HasSdCard)
        {
            return false;
        }

        if (onlyIncomplete && !Group.HasIncompleteLot)
        {
            return false;
        }

        if (search.Length > 0 && !Group.Lots.Any(l =>
            l.FullPath.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || l.Source.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
        {
            return false;
        }

        if (from is { } f && (Group.LastDate is null || Group.LastDate < f.Date))
        {
            return false;
        }

        if (to is { } u && (Group.FirstDate is null || Group.FirstDate >= u.Date.AddDays(1)))
        {
            return false;
        }

        return true;
    }
}

/// <summary>Une carte : une copie, c'est-à-dire un lot (CLAUDE.md 7.4).</summary>
public sealed partial class LotCardViewModel(GroupMember member, IFileExplorer explorer) : ObservableObject
{
    public GroupMember Member { get; } = member;

    public Lot Lot => Member.Lot;

    public SourceType Type => Lot.Source.Type;

    public string SourceName => Lot.Source.Name;

    public string TypeLabel => Lot.Source.Type.Label();

    public string Folder => string.IsNullOrEmpty(Lot.RelativeDirectory) ? "(racine)" : Lot.RelativeDirectory;

    public string FullPath => Lot.FullPath;

    public string CountText => Format.Plural(Lot.FileCount, "RAW", "RAW");

    public string SizeText => Format.Bytes(Lot.TotalBytes);

    public string DateText => Format.DateRange(Lot.FirstDate, Lot.LastDate);

    /// <summary>Part du dossier concernée par cette ligne.</summary>
    public double Coverage => Member.Share;

    public bool IsComplete => Member.IsWhole;

    public string CoverageText => Member.IsWhole
        ? "100 % copié ailleurs"
        : $"{Format.Percent(Member.Share)} en commun ici — {Format.Plural(Member.Count, "RAW", "RAW")}";

    /// <summary>Le reste du dossier : fichiers uniques et fichiers rattachés à d'autres lignes.</summary>
    public string? RestText
    {
        get
        {
            var parts = new List<string>();
            if (Member.ElsewhereCount > 0)
            {
                var others = Lot.Groups.Count - 1;
                parts.Add($"{Format.Count(Member.ElsewhereCount)} dans {Format.Plural(others, "autre ligne", "autres lignes")}");
            }

            if (Lot.UniqueCount > 0)
            {
                parts.Add(Format.Plural(Lot.UniqueCount, "fichier unique", "fichiers uniques"));
            }

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    public string? RecoverableText => Member.RecoverableBytes > 0 ? $"{Format.Bytes(Member.RecoverableBytes)} récupérables" : null;

    public bool Online => Lot.SourceOnline;

    public IReadOnlyList<Badge> Badges { get; } = BuildBadges(member.Lot);

    [RelayCommand(CanExecute = nameof(Online))]
    private void Open() => explorer.OpenFolder(Lot.FullPath);

    private static List<Badge> BuildBadges(Lot lot)
    {
        var badges = new List<Badge>();
        if (!lot.SourceOnline)
        {
            badges.Add(new Badge($"Hors ligne — vu le {Format.Date(lot.Source.LastScanUtc?.ToLocalTime())}", BadgeKind.Muted));
        }

        if (lot.Source.IgnoreFileNames)
        {
            badges.Add(new Badge("Noms ignorés", BadgeKind.Info,
                "Fichiers renommés (récupération de carte) : reconnus par leur date de prise de vue et leur boîtier."));
        }

        if (lot.AmbiguousCount > 0)
        {
            badges.Add(new Badge($"{Format.Plural(lot.AmbiguousCount, "ambigu")}", BadgeKind.Warning,
                "Plusieurs photos prises au même instant avec le même boîtier : sans leur nom, impossible de savoir laquelle est laquelle. Par prudence, comptées comme uniques."));
        }

        if (lot.DivergentCount > 0)
        {
            badges.Add(new Badge($"⚠ {Format.Plural(lot.DivergentCount, "divergent")}", BadgeKind.Warning,
                "Même photo mais taille différente : fichier retouché, tronqué ou corrompu. Non compté dans l'espace gagné."));
        }

        if (lot.ProbableCount > 0)
        {
            badges.Add(new Badge($"{Format.Count(lot.ProbableCount)} sans n° de série", BadgeKind.Muted,
                "Identifiés par nom, date et modèle d'appareil (identité probable)."));
        }

        if (lot.WeakCount > 0)
        {
            badges.Add(new Badge($"{Format.Count(lot.WeakCount)} sans métadonnées", BadgeKind.Warning,
                "Métadonnées illisibles : identifiés par nom et taille seulement (identité faible)."));
        }

        if (lot.Annexes is { } a)
        {
            var parts = new List<string>();
            if (a.Jpeg > 0) parts.Add($"{Format.Count(a.Jpeg)} JPEG");
            if (a.Heic > 0) parts.Add($"{Format.Count(a.Heic)} HEIC");
            if (a.Xmp > 0) parts.Add($"{Format.Count(a.Xmp)} XMP");
            badges.Add(new Badge("+ " + string.Join(", ", parts), BadgeKind.Info, "Fichiers annexes dans ce dossier (non comptés comme doublons)."));
        }

        return badges;
    }
}

public enum BadgeKind
{
    Info,
    Muted,
    Warning,
}

public sealed record Badge(string Text, BadgeKind Kind, string? ToolTip = null);

/// <summary>Une ligne du tableau détaillé : un RAW et sa présence dans chaque copie.</summary>
public sealed class IdentityRowViewModel(Copies copies, IReadOnlyList<Lot> lots)
{
    public string FileName => copies.FileName;

    /// <summary>Noms des copies renommées, en infobulle.</summary>
    public string? OtherNamesText => copies.OtherNames is { Count: > 0 } names ? "Aussi nommé : " + string.Join(", ", names) : null;

    public string DateText => Format.DateTime(copies.DateTaken);

    public IReadOnlyList<CellKind> Cells { get; } = lots
        .Select(l => copies.Files.Any(f => f.Lot == l)
            ? copies.Divergent ? CellKind.Divergent : CellKind.Present
            : CellKind.Missing)
        .ToList();
}

public enum CellKind
{
    Present,
    Missing,
    Divergent,
}
