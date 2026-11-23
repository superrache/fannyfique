using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fannyfique.Core.Analysis;
using Fannyfique.Core.Formatting;
using Fannyfique.Core.Model;
using Fannyfique.Core.Platform;

namespace Fannyfique.App.ViewModels;

/// <summary>Écran Cartes SD : « puis-je vider cette carte ? » (CLAUDE.md 7.3).</summary>
public sealed partial class SdCardsViewModel(IFileExplorer explorer, MainViewModel main) : ObservableObject
{
    public ObservableCollection<SdCardItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public void Load(AnalysisResult analysis)
    {
        Items.Clear();
        foreach (var verdict in analysis.SdCards)
        {
            Items.Add(new SdCardItemViewModel(verdict, explorer));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void GoToSources() => main.CurrentPage = Page.Sources;
}

public sealed partial class SdCardItemViewModel : ObservableObject
{
    private const int MaxListedFiles = 1000;
    private readonly SdCardVerdict _verdict;
    private readonly IFileExplorer _explorer;

    public SdCardItemViewModel(SdCardVerdict verdict, IFileExplorer explorer)
    {
        _verdict = verdict;
        _explorer = explorer;
        CopyLines = verdict.CopiesBySource
            .Select(c => new CopyLine(c.Source.Type, $"{Format.Count(c.Count)} sur {c.Source.Name}", c.Online ? null : "hors ligne"))
            .ToList();
        UniqueLots = verdict.LotsWithUniqueFiles
            .Select(l => new UniqueLotItem(
                string.IsNullOrEmpty(l.RelativeDirectory) ? "(racine)" : l.RelativeDirectory,
                Format.Plural(verdict.OnlyHere.Count(f => f.Lot == l), "fichier unique", "fichiers uniques") + $" sur {Format.Count(l.FileCount)}",
                l.FullPath,
                verdict.Online,
                explorer))
            .ToList();
    }

    public string Name => _verdict.Source.Name;

    public string Path => _verdict.Source.Path;

    public bool Online => _verdict.Online;

    public bool IsScanned => _verdict.FileCount > 0;

    public bool CanBeEmptied => IsScanned && _verdict.CanBeEmptied;

    public bool HasUniqueFiles => !_verdict.CanBeEmptied;

    public string VerdictTitle => !IsScanned
        ? "Aucun RAW sur cette carte"
        : _verdict.CanBeEmptied
            ? "Peut être vidée"
            : $"{Format.Plural(_verdict.OnlyHere.Count, "fichier", "fichiers")} uniquement sur cette carte";

    public string VerdictGlyph => CanBeEmptied ? "" : HasUniqueFiles ? "" : "";

    public string ContentText => $"{Format.Plural(_verdict.FileCount, "RAW", "RAW")} · {Format.Bytes(_verdict.TotalBytes)}";

    public string FreeableText => _verdict.FreeableBytes > 0 ? $" · {Format.Bytes(_verdict.FreeableBytes)} libérables" : "";

    public string? OfflineNote => _verdict.OfflineOnlyCount > 0
        ? $"{Format.Plural(_verdict.OfflineOnlyCount, "fichier n'a", "fichiers n'ont")} de copie que sur une source hors ligne : vérifie-la avant de vider la carte."
        : null;

    public string? OfflineCardNote => Online ? null : $"Carte non branchée — résultats du {Format.Date(_verdict.Source.LastScanUtc?.ToLocalTime())}";

    public IReadOnlyList<CopyLine> CopyLines { get; }

    public IReadOnlyList<UniqueLotItem> UniqueLots { get; }

    [ObservableProperty]
    private bool _showFiles;

    public IReadOnlyList<UniqueFileItem> UniqueFiles => _verdict.OnlyHere
        .OrderBy(f => f.File.DateTaken ?? DateTime.MaxValue)
        .ThenBy(f => f.File.FileName, StringComparer.OrdinalIgnoreCase)
        .Take(MaxListedFiles)
        .Select(f => new UniqueFileItem(f.File.FileName, Format.DateTime(f.File.DateTaken), f.File.RelativeDirectory, Format.Bytes(f.File.Size)))
        .ToList();

    public string? TruncatedNote => _verdict.OnlyHere.Count > MaxListedFiles ? $"… et {Format.Count(_verdict.OnlyHere.Count - MaxListedFiles)} autres" : null;

    public string ToggleFilesText => ShowFiles ? "Masquer les fichiers" : "Voir les fichiers";

    partial void OnShowFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleFilesText));
        OnPropertyChanged(nameof(UniqueFiles));
    }

    [RelayCommand]
    private void ToggleFiles() => ShowFiles = !ShowFiles;

    [RelayCommand(CanExecute = nameof(Online))]
    private void Open() => _explorer.OpenFolder(Path);
}

public sealed record CopyLine(SourceType Type, string Text, string? Note);

public sealed record UniqueFileItem(string Name, string Date, string Folder, string Size);

public sealed partial class UniqueLotItem(string folder, string count, string fullPath, bool online, IFileExplorer explorer) : ObservableObject
{
    public string Folder { get; } = folder;

    public string Count { get; } = count;

    public bool Online { get; } = online;

    [RelayCommand(CanExecute = nameof(Online))]
    private void Open() => explorer.OpenFolder(fullPath);
}
