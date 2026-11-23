using System.Collections.Concurrent;
using Fannyfique.Core.Indexing;
using Fannyfique.Core.Metadata;
using Fannyfique.Core.Model;
using Fannyfique.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fannyfique.Core.Scanning;

/// <summary>Scan incrémental des sources en trois phases (CLAUDE.md 5.3).</summary>
public sealed class Scanner(
    IRawMetadataReader reader,
    TimeProvider? clock = null,
    ILogger<Scanner>? logger = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>Intervalle des sauvegardes intermédiaires de l'index pendant la lecture.</summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Met à jour <paramref name="index"/> à partir des sources actives. En cas d'annulation, les
    /// métadonnées déjà lues restent dans l'index mais aucun fichier n'en est retiré.
    /// </summary>
    /// <param name="checkpoint">Appelé régulièrement pendant la lecture pour sauvegarder l'index.</param>
    public async Task<ScanReport> ScanAsync(
        IReadOnlyList<Source> sources,
        AppSettings settings,
        FileIndex index,
        ScanProgress progress,
        Func<FileIndex, Task>? checkpoint = null,
        CancellationToken cancellationToken = default)
    {
        var started = _clock.GetUtcNow();
        var rawExtensions = new HashSet<string>(settings.RawExtensions, StringComparer.OrdinalIgnoreCase);
        var enabled = sources.Where(s => s.Enabled).ToList();
        index.RemoveUnknownSources(sources.Select(s => s.Id).ToHashSet());

        try
        {
            // Phase 1 : exploration, une tâche par source (souvent des disques différents).
            progress.SetPhase(ScanPhase.Exploring);
            enabled.ForEach(s => progress.Source(s.Id));
            var explorations = await Task.WhenAll(enabled.Select(source => Task.Run(
                () => Explore(source, rawExtensions, progress.Source(source.Id), progress, cancellationToken),
                cancellationToken)));

            // Phase 2 : lecture des métadonnées des fichiers nouveaux ou modifiés.
            progress.SetPhase(ScanPhase.Reading);
            var now = _clock.GetUtcNow().UtcDateTime;
            var work = explorations.Where(e => e.Online).Select(e => (e, ToRead: Triage(e, index, progress, now))).ToList();
            progress.AddToRead(work.Sum(w => w.ToRead.Count));

            using var checkpointCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var checkpointLoop = checkpoint is null ? Task.CompletedTask : CheckpointLoop(index, checkpoint, checkpointCts.Token);
            try
            {
                await Task.WhenAll(work.Select(w => ReadAll(w.e, w.ToRead, settings, index, progress, now, cancellationToken)));
            }
            finally
            {
                checkpointCts.Cancel();
                await checkpointLoop;
            }

            // Phase 3 : on retire de l'index ce qui a disparu des sources en ligne.
            progress.SetPhase(ScanPhase.Analyzing);
            foreach (var exploration in explorations.Where(e => e.Online))
            {
                var seen = exploration.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var removed = index.RemoveWhere(exploration.Source.Id, f => !seen.Contains(f.RelativePath));
                index.SetAnnexes(exploration.Source.Id, exploration.Annexes.Values);
                exploration.Source.LastScanUtc = now;
                progress.Source(exploration.Source.Id).SetState(SourceScanState.Done);
                _logger.LogInformation(
                    "Source {Source} : {Count} RAW, {Removed} retirés de l'index",
                    exploration.Source, exploration.Files.Count, removed);
            }

            progress.SetPhase(ScanPhase.Done);
            return new ScanReport(
                explorations.Where(e => !e.Online).Select(e => e.Source.Id).ToHashSet(),
                progress.Errors.ToList(),
                _clock.GetUtcNow() - started);
        }
        catch (OperationCanceledException)
        {
            progress.SetPhase(ScanPhase.Cancelled);
            throw;
        }
    }

    private Exploration Explore(
        Source source,
        HashSet<string> rawExtensions,
        ScanProgress.SourceProgress sourceProgress,
        ScanProgress progress,
        CancellationToken cancellationToken)
    {
        var exploration = new Exploration(source);
        if (!Directory.Exists(source.Path))
        {
            _logger.LogInformation("Source hors ligne : {Source}", source);
            sourceProgress.SetState(SourceScanState.Offline);
            return exploration;
        }

        exploration.Online = true;
        sourceProgress.SetState(SourceScanState.Exploring);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(source.Path));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            progress.DirectoryExplored(sourceProgress, directory.FullName);

            var relativeDirectory = Path.GetRelativePath(source.Path, directory.FullName);
            if (relativeDirectory == ".")
            {
                relativeDirectory = "";
            }

            FolderAnnexes? annexes = null;
            var hasRaw = false;
            try
            {
                foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo sub)
                    {
                        // Pas de jonctions ni de liens symboliques : évite les boucles (CLAUDE.md 4.4).
                        if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint) && !FileKinds.IsExcludedDirectory(sub.Name))
                        {
                            pending.Push(sub);
                        }

                        continue;
                    }

                    var file = (FileInfo)entry;
                    if (rawExtensions.Contains(file.Extension))
                    {
                        hasRaw = true;
                        exploration.Files.Add(new FoundFile(
                            Path.Combine(relativeDirectory, file.Name), file.FullName, file.Length, file.LastWriteTimeUtc));
                        progress.RawFound(sourceProgress);
                    }
                    else if (FileKinds.GetAnnexKind(file.Extension) is { } kind)
                    {
                        annexes ??= new FolderAnnexes { SourceId = source.Id, RelativeDirectory = relativeDirectory };
                        annexes.Add(kind);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Dossier inaccessible : {Path}", directory.FullName);
                progress.Error(sourceProgress, directory.FullName, ex);
            }

            if (hasRaw && annexes is not null)
            {
                exploration.Annexes[relativeDirectory] = annexes;
            }
        }

        return exploration;
    }

    /// <summary>Reprend de l'index les fichiers inchangés et renvoie ceux à lire.</summary>
    private static List<FoundFile> Triage(Exploration exploration, FileIndex index, ScanProgress progress, DateTime now)
    {
        var toRead = new List<FoundFile>();
        var cached = 0;
        foreach (var file in exploration.Files)
        {
            var known = index.Get(exploration.Source.Id, file.RelativePath);
            if (known is not null
                && known.Size == file.Size
                && known.LastWriteUtc == file.LastWriteUtc
                && known.Status != MetadataStatus.Error
                && known.RelativePath == file.RelativePath)
            {
                known.LastSeenUtc = now;
                cached++;
            }
            else
            {
                toRead.Add(file);
            }
        }

        progress.FromCache(progress.Source(exploration.Source.Id), cached);
        return toRead;
    }

    private async Task ReadAll(
        Exploration exploration,
        List<FoundFile> files,
        AppSettings settings,
        FileIndex index,
        ScanProgress progress,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var source = exploration.Source;
        var sourceProgress = progress.Source(source.Id);
        sourceProgress.SetState(SourceScanState.Reading);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.ParallelismFor(source.Type),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(files, options, (file, _) =>
        {
            index.Upsert(ReadOne(source, file, sourceProgress, progress, now));
            progress.FileRead(sourceProgress);
            return ValueTask.CompletedTask;
        });
    }

    private IndexedFile ReadOne(Source source, FoundFile file, ScanProgress.SourceProgress sourceProgress, ScanProgress progress, DateTime now)
    {
        RawMetadata metadata;
        MetadataStatus status;
        try
        {
            metadata = reader.Read(file.FullPath);
            status = MetadataStatus.Ok;
        }
        catch (RawFormatException ex)
        {
            _logger.LogInformation("Métadonnées illisibles : {Path} ({Message})", file.FullPath, ex.Message);
            metadata = RawMetadata.Empty;
            status = MetadataStatus.Unreadable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lecture impossible : {Path}", file.FullPath);
            progress.Error(sourceProgress, file.FullPath, ex);
            metadata = RawMetadata.Empty;
            status = MetadataStatus.Error;
        }

        return new IndexedFile
        {
            SourceId = source.Id,
            RelativePath = file.RelativePath,
            Size = file.Size,
            LastWriteUtc = file.LastWriteUtc,
            DateTaken = metadata.DateTaken,
            Serial = metadata.SerialNumber,
            Make = metadata.Make,
            Model = metadata.Model,
            Status = status,
            LastSeenUtc = now,
        };
    }

    private async Task CheckpointLoop(FileIndex index, Func<FileIndex, Task> checkpoint, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(CheckpointInterval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await checkpoint(index);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la sauvegarde intermédiaire de l'index");
        }
    }

    private sealed class Exploration(Source source)
    {
        public Source Source { get; } = source;

        public bool Online { get; set; }

        public List<FoundFile> Files { get; } = [];

        public Dictionary<string, FolderAnnexes> Annexes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FoundFile(string RelativePath, string FullPath, long Size, DateTime LastWriteUtc);
}

/// <summary>Bilan d'un scan terminé.</summary>
public sealed record ScanReport(IReadOnlySet<Guid> OfflineSourceIds, IReadOnlyList<ScanError> Errors, TimeSpan Duration);
