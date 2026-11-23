using Fannyfique.Core.Analysis;
using Fannyfique.Core.Indexing;
using Fannyfique.Core.Metadata;
using Fannyfique.Core.Model;
using Fannyfique.Core.Scanning;
using Fannyfique.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fannyfique.Core;

/// <summary>
/// État de l'application : préférences, index, dernière analyse. Orchestre chargement, scan,
/// analyse et sauvegardes. Toutes les écritures vont dans <see cref="AppPaths.DataDirectory"/>.
/// </summary>
public sealed class Workspace
{
    private readonly AppPaths _paths;
    private readonly IRawMetadataReader _reader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _indexSaveLock = new(1, 1);

    private Workspace(AppPaths paths, AppSettings settings, FileIndex index, IRawMetadataReader reader, ILoggerFactory loggerFactory)
    {
        _paths = paths;
        Settings = settings;
        Index = index;
        _reader = reader;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Workspace>();
    }

    public AppSettings Settings { get; }

    public FileIndex Index { get; }

    public IReadOnlySet<Guid> OfflineSourceIds { get; private set; } = new HashSet<Guid>();

    public AnalysisResult? Analysis { get; private set; }

    public static Workspace Load(AppPaths paths, IRawMetadataReader? reader = null, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<Workspace>();
        var settings = TryLoad<AppSettings>(paths.SettingsFile, logger) ?? new AppSettings();
        var index = FileIndex.FromData(TryLoad<IndexData>(paths.IndexFile, logger));
        logger.LogInformation("Chargé : {Sources} sources, {Files} RAW dans l'index", settings.Sources.Count, index.Count);
        return new Workspace(paths, settings, index, reader ?? new MetadataExtractorReader(), loggerFactory);
    }

    public void SaveSettings() => JsonFileStore.Save(_paths.SettingsFile, Settings, indented: true);

    public async Task SaveIndexAsync()
    {
        await _indexSaveLock.WaitAsync();
        try
        {
            var data = Index.ToData();
            await Task.Run(() => JsonFileStore.Save(_paths.IndexFile, data));
        }
        finally
        {
            _indexSaveLock.Release();
        }
    }

    public Source AddSource(string path, SourceType type, string? name = null, string? volumeSerial = null, bool ignoreFileNames = false)
    {
        var source = new Source
        {
            Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), // garde « E:\ » pour une racine
            Type = type,
            Name = string.IsNullOrWhiteSpace(name) ? DefaultName(path) : name.Trim(),
            VolumeSerial = volumeSerial,
            IgnoreFileNames = ignoreFileNames,
        };
        Settings.Sources.Add(source);
        SaveSettings();
        return source;
    }

    /// <summary>Retire une source et ses données de l'index, sans toucher aux fichiers.</summary>
    public async Task RemoveSourceAsync(Source source)
    {
        Settings.Sources.Remove(source);
        SaveSettings();
        Index.RemoveUnknownSources(Settings.Sources.Select(s => s.Id).ToHashSet());
        await SaveIndexAsync();
    }

    /// <summary>Source déjà enregistrée pour ce volume amovible.</summary>
    public Source? FindByVolume(string volumeSerial) =>
        Settings.Sources.FirstOrDefault(s => string.Equals(s.VolumeSerial, volumeSerial, StringComparison.OrdinalIgnoreCase));

    /// <summary>Détermine les sources hors ligne sans scanner (au démarrage).</summary>
    public void RefreshOnlineState(Func<Source, bool>? isOnline = null)
    {
        isOnline ??= s => Directory.Exists(s.Path);
        OfflineSourceIds = Settings.Sources.Where(s => !isOnline(s)).Select(s => s.Id).ToHashSet();
    }

    public AnalysisResult Analyze()
    {
        Analysis = Analyzer.Analyze(
            Settings.Sources,
            Index.Files,
            Index.Annexes,
            OfflineSourceIds,
            Settings.KeepOrder,
            Settings.IgnoreOfflineSources);
        return Analysis;
    }

    public async Task<ScanOutcome> ScanAsync(ScanProgress progress, CancellationToken cancellationToken = default)
    {
        var previous = Index.History.LastOrDefault();
        var scanner = new Scanner(_reader, logger: _loggerFactory.CreateLogger<Scanner>());
        ScanReport report;
        try
        {
            report = await scanner.ScanAsync(Settings.Sources, Settings, Index, progress, _ => SaveIndexAsync(), cancellationToken);
        }
        finally
        {
            SaveSettings();
            await SaveIndexAsync();
        }

        OfflineSourceIds = report.OfflineSourceIds;
        var analysis = await Task.Run(Analyze, cancellationToken);
        var current = new ScanSummaryEntry(
            DateTime.UtcNow,
            analysis.Summary.RecoverableBytes,
            analysis.SdCards.Count(c => !c.CanBeEmptied),
            analysis.SdCards.Count(c => c.CanBeEmptied && c.FileCount > 0));
        Index.AddHistory(current);
        await SaveIndexAsync();
        _logger.LogInformation(
            "Scan terminé en {Duration} : {Groups} groupes, {Bytes} octets récupérables",
            report.Duration, analysis.Summary.GroupCount, analysis.Summary.RecoverableBytes);
        return new ScanOutcome(report, analysis, previous, current);
    }

    /// <summary>Nombre et poids des RAW d'une source dans l'index.</summary>
    public (int Count, long Bytes) StatsFor(Source source)
    {
        var files = Index.Files.Where(f => f.SourceId == source.Id).ToList();
        return (files.Count, files.Sum(f => f.Size));
    }

    public IReadOnlyDictionary<Guid, (int Count, long Bytes)> StatsBySource() => Index.Files
        .GroupBy(f => f.SourceId)
        .ToDictionary(g => g.Key, g => (g.Count(), g.Sum(f => f.Size)));

    private static string DefaultName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private static T? TryLoad<T>(string path, ILogger logger)
        where T : class
    {
        try
        {
            return JsonFileStore.Load<T>(path);
        }
        catch (Exception ex)
        {
            // Fichier corrompu : on le met de côté et on repart de zéro plutôt que de bloquer l'application.
            logger.LogError(ex, "Lecture impossible de {Path}, le fichier est mis de côté", path);
            try
            {
                File.Move(path, path + $".corrompu-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            }
            catch (IOException)
            {
            }

            return null;
        }
    }
}

/// <summary>Résultat d'un scan, avec le bilan précédent pour mesurer le progrès (CLAUDE.md 7.5).</summary>
public sealed record ScanOutcome(ScanReport Report, AnalysisResult Analysis, ScanSummaryEntry? Previous, ScanSummaryEntry Current)
{
    /// <summary>Doublons en moins depuis le scan précédent.</summary>
    public long FreedBytes => Previous is null ? 0 : Math.Max(0, Previous.RecoverableBytes - Current.RecoverableBytes);
}
