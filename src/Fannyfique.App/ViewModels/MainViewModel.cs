using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fannyfique.App.Services;
using Fannyfique.Core;
using Fannyfique.Core.Formatting;
using Fannyfique.Core.Model;
using Fannyfique.Core.Platform;
using Fannyfique.Core.Scanning;
using Fannyfique.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Fannyfique.App.ViewModels;

public enum Page
{
    Sources,
    SdCards,
    Duplicates,
}

/// <summary>Fenêtre principale : navigation, scan, détection des cartes SD, bilan.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly IVolumeInfoProvider _volumes;
    private readonly IDialogService _dialogs;
    private readonly ThemeManager _theme;
    private readonly ILogger<MainViewModel> _logger;
    private readonly HashSet<string> _dismissedVolumes = new(StringComparer.OrdinalIgnoreCase);
    private int _analysisVersion;

    public MainViewModel(
        Workspace workspace,
        IVolumeInfoProvider volumes,
        IFileExplorer explorer,
        IDialogService dialogs,
        ThemeManager theme,
        ILogger<MainViewModel> logger)
    {
        _workspace = workspace;
        _volumes = volumes;
        _dialogs = dialogs;
        _theme = theme;
        _logger = logger;
        Sources = new SourcesViewModel(workspace, volumes, explorer, dialogs, this);
        SdCards = new SdCardsViewModel(explorer, this);
        Duplicates = new DuplicatesViewModel(explorer);
        Scan = new ScanViewModel();
        _currentViewModel = Sources;
    }

    public SourcesViewModel Sources { get; }

    public SdCardsViewModel SdCards { get; }

    public DuplicatesViewModel Duplicates { get; }

    public ScanViewModel Scan { get; }

    [ObservableProperty]
    private Page _currentPage = Page.Sources;

    [ObservableProperty]
    private object _currentViewModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private bool _isLoading = true;

    /// <summary>Carte SD détectée mais pas encore enregistrée (CLAUDE.md 4.2).</summary>
    [ObservableProperty]
    private VolumeInfo? _detectedCard;

    [ObservableProperty]
    private string? _detectedCardText;

    /// <summary>Bilan du dernier scan (CLAUDE.md 7.5).</summary>
    [ObservableProperty]
    private string? _scanSummary;

    public ObservableCollection<ScanError> ScanErrors { get; } = [];

    public string ThemeLabel => _workspace.Settings.Theme switch
    {
        AppTheme.Light => "Thème clair",
        AppTheme.Dark => "Thème sombre",
        _ => "Thème automatique",
    };

    public string ThemeGlyph => _workspace.Settings.Theme switch
    {
        AppTheme.Light => "",
        AppTheme.Dark => "",
        _ => "",
    };

    partial void OnCurrentPageChanged(Page value)
    {
        CurrentViewModel = value switch
        {
            Page.SdCards => SdCards,
            Page.Duplicates => Duplicates,
            _ => Sources,
        };
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    public string PageTitle => CurrentPage switch
    {
        Page.SdCards => "Cartes SD",
        Page.Duplicates => "Doublons",
        _ => "Sources",
    };

    public string PageSubtitle => CurrentPage switch
    {
        Page.SdCards => "Puis-je vider mes cartes ? Tout ce qui est copié ailleurs peut partir.",
        Page.Duplicates => "Les copies d'un même lot, côte à côte. À toi de trier, Fannyfique ne supprime rien.",
        _ => "Les dossiers à comparer : PC, disques externes, NAS et cartes SD.",
    };

    [RelayCommand]
    private void Navigate(Page page) => CurrentPage = page;

    public async Task InitializeAsync()
    {
        try
        {
            // Une source réseau injoignable peut bloquer longtemps : chaque test a son délai.
            var online = new Dictionary<Guid, bool>();
            await Task.WhenAll(_workspace.Settings.Sources.Select(async s =>
            {
                bool result;
                try
                {
                    result = await Task.Run(() => Directory.Exists(s.Path)).WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException)
                {
                    result = false;
                }

                lock (online)
                {
                    online[s.Id] = result;
                }
            }));
            _workspace.RefreshOnlineState(s => online.GetValueOrDefault(s.Id));
            await RefreshAnalysisAsync();
            CheckRemovableVolumes();
            if (_workspace.Settings.Sources.Count > 0 && _workspace.Analysis is { } analysis)
            {
                CurrentPage = analysis.SdCards.Any(c => c.FileCount > 0) ? Page.SdCards : Page.Duplicates;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'initialisation");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Recalcule l'analyse (après un changement de type, d'activation…).</summary>
    public async Task RefreshAnalysisAsync(bool refreshSources = true)
    {
        // Plusieurs changements rapprochés : seule la dernière analyse est affichée.
        var version = ++_analysisVersion;
        var analysis = await Task.Run(_workspace.Analyze);
        if (version != _analysisVersion)
        {
            return;
        }

        if (refreshSources)
        {
            Sources.Refresh(_workspace.OfflineSourceIds);
        }

        SdCards.Load(analysis);
        Duplicates.Load(analysis, _workspace.Settings.Sources);
        OnPropertyChanged(nameof(SdCardsBadge));
        OnPropertyChanged(nameof(DuplicatesBadge));
    }

    public string? SdCardsBadge => _workspace.Analysis?.SdCards.Count(c => c.FileCount > 0) is > 0 and var n ? n.ToString() : null;

    public string? DuplicatesBadge => _workspace.Analysis?.Summary.GroupCount is > 0 and var n ? Format.Count(n) : null;

    private bool CanStartScan() => !IsScanning && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (_workspace.Settings.Sources.Count(s => s.Enabled) == 0)
        {
            CurrentPage = Page.Sources;
            Sources.AddFoldersCommand.Execute(null);
            return;
        }

        IsScanning = true;
        ScanSummary = null;
        ScanErrors.Clear();
        var progress = new ScanProgress();
        using var cts = new CancellationTokenSource();
        Scan.Start(progress, _workspace.Settings.Sources, cts);
        try
        {
            var outcome = await _workspace.ScanAsync(progress, cts.Token);
            Scan.Stop();
            await RefreshAnalysisAsync();
            ShowSummary(outcome);
            CurrentPage = outcome.Analysis.SdCards.Any(c => c.FileCount > 0) ? Page.SdCards : Page.Duplicates;
        }
        catch (OperationCanceledException)
        {
            Scan.Stop();
            ScanSummary = "Scan annulé. Ce qui a déjà été lu est conservé : le prochain scan reprendra là où celui-ci s'est arrêté.";
            await RefreshAnalysisAsync();
        }
        catch (Exception ex)
        {
            Scan.Stop();
            _logger.LogError(ex, "Échec du scan");
            _dialogs.Error("Échec du scan", $"Le scan s'est interrompu : {ex.Message}");
        }
        finally
        {
            foreach (var error in progress.Errors.Take(500))
            {
                ScanErrors.Add(error);
            }

            OnPropertyChanged(nameof(ScanErrorsText));
            IsScanning = false;
            CheckRemovableVolumes(); // un lecteur a pu être branché pendant le scan
        }
    }

    public string? ScanErrorsText => ScanErrors.Count == 0 ? null : Format.Plural(ScanErrors.Count, "fichier ou dossier illisible", "fichiers ou dossiers illisibles");

    private void ShowSummary(ScanOutcome outcome)
    {
        var parts = new List<string> { $"Scan terminé en {Format.Duration(outcome.Report.Duration)}" };
        var summary = outcome.Analysis.Summary;
        parts.Add(summary.RecoverableBytes > 0
            ? $"{Format.Bytes(summary.RecoverableBytes)} de doublons à trier"
            : "aucun doublon, tout est rangé !");
        if (outcome.FreedBytes > 0)
        {
            parts.Add($"depuis le dernier scan : {Format.Bytes(outcome.FreedBytes)} libérés, bravo !");
        }

        if (outcome.Current.SdCardsReady > 0)
        {
            parts.Add(Format.Plural(outcome.Current.SdCardsReady, "carte SD prête", "cartes SD prêtes") + " à être vidée" + (outcome.Current.SdCardsReady > 1 ? "s" : ""));
        }

        ScanSummary = string.Join(" · ", parts);
    }

    [RelayCommand]
    private void DismissSummary()
    {
        ScanSummary = null;
        ScanErrors.Clear();
        OnPropertyChanged(nameof(ScanErrorsText));
    }

    /// <summary>Appelé au démarrage et à chaque branchement de lecteur.</summary>
    public void CheckRemovableVolumes()
    {
        if (IsScanning)
        {
            return; // les sources ne changent pas pendant un scan
        }

        try
        {
            foreach (var volume in _volumes.GetRemovableVolumes())
            {
                var dcim = Path.Combine(volume.RootPath, "DCIM");
                if (volume.Serial is null || !Directory.Exists(dcim))
                {
                    continue;
                }

                var known = _workspace.Settings.Sources
                    .Where(s => string.Equals(s.VolumeSerial, volume.Serial, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var source in known)
                {
                    // Même volume, autre lettre de lecteur : on met le chemin à jour silencieusement.
                    var root = Path.GetPathRoot(source.Path);
                    if (root is not null && !root.StartsWith(@"\\", StringComparison.Ordinal)
                        && !string.Equals(root, volume.RootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        source.Path = Path.Combine(volume.RootPath, Path.GetRelativePath(root, source.Path));
                        _workspace.SaveSettings();
                        _logger.LogInformation("Source {Source} : nouvelle lettre de lecteur", source);
                        Sources.Refresh(_workspace.OfflineSourceIds);
                    }
                }

                if (known.Count > 0)
                {
                    continue;
                }

                if (!_dismissedVolumes.Contains(volume.Serial) && DetectedCard is null)
                {
                    DetectedCard = volume;
                    var label = string.IsNullOrWhiteSpace(volume.Label) ? "Carte SD" : volume.Label;
                    DetectedCardText = $"Carte SD détectée : {label} ({volume.RootPath.TrimEnd('\\')}) — l'ajouter aux sources ?";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Détection des lecteurs amovibles impossible");
        }
    }

    [RelayCommand]
    private void AddDetectedCard()
    {
        if (DetectedCard is not { } volume)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(volume.Label) ? "Carte SD" : volume.Label;
        _workspace.AddSource(Path.Combine(volume.RootPath, "DCIM"), SourceType.SdCard, label, volume.Serial);
        Sources.Refresh(_workspace.OfflineSourceIds);
        DetectedCard = null;
        CurrentPage = Page.Sources;
    }

    [RelayCommand]
    private void DismissDetectedCard()
    {
        if (DetectedCard?.Serial is { } serial)
        {
            _dismissedVolumes.Add(serial);
        }

        DetectedCard = null;
    }

    [RelayCommand]
    private void CycleTheme()
    {
        _workspace.Settings.Theme = _workspace.Settings.Theme switch
        {
            AppTheme.Auto => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.Auto,
        };
        _workspace.SaveSettings();
        _theme.Apply(_workspace.Settings.Theme);
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeGlyph));
    }

    /// <summary>Dossiers déposés sur la fenêtre.</summary>
    public void DropFolders(IEnumerable<string> paths)
    {
        if (!IsScanning)
        {
            Sources.AddPaths(paths.Where(Directory.Exists));
            CurrentPage = Page.Sources;
        }
    }
}
