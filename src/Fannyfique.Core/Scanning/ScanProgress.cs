using System.Collections.Concurrent;

namespace Fannyfique.Core.Scanning;

public enum ScanPhase
{
    NotStarted,
    Exploring,
    Reading,
    Analyzing,
    Done,
    Cancelled,
}

public enum SourceScanState
{
    Waiting,
    Exploring,
    Reading,
    Done,
    Offline,
}

/// <summary>
/// Compteurs d'avancement mis à jour par le scanner depuis plusieurs threads. L'interface en lit
/// une copie (<see cref="Snapshot"/>) à intervalle régulier plutôt que de recevoir un événement par
/// fichier (CLAUDE.md 5.4).
/// </summary>
public sealed class ScanProgress
{
    private readonly ConcurrentDictionary<Guid, SourceProgress> _sources = new();
    private readonly ConcurrentQueue<ScanError> _errors = new();
    private int _phase;
    private int _directories;
    private int _rawFound;
    private int _toRead;
    private int _read;
    private int _fromCache;
    private string? _currentDirectory;

    public ScanPhase Phase => (ScanPhase)Volatile.Read(ref _phase);

    public IReadOnlyCollection<ScanError> Errors => _errors.ToArray();

    internal void SetPhase(ScanPhase phase) => Volatile.Write(ref _phase, (int)phase);

    internal SourceProgress Source(Guid id) => _sources.GetOrAdd(id, _ => new SourceProgress(id));

    internal void DirectoryExplored(SourceProgress source, string path)
    {
        Interlocked.Increment(ref _directories);
        Interlocked.Increment(ref source.Directories);
        Volatile.Write(ref _currentDirectory, path);
    }

    internal void RawFound(SourceProgress source)
    {
        Interlocked.Increment(ref _rawFound);
        Interlocked.Increment(ref source.RawFound);
    }

    internal void AddToRead(int count) => Interlocked.Add(ref _toRead, count);

    internal void FromCache(SourceProgress source, int count)
    {
        Interlocked.Add(ref _fromCache, count);
        Interlocked.Add(ref source.FromCache, count);
    }

    internal void FileRead(SourceProgress source)
    {
        Interlocked.Increment(ref _read);
        Interlocked.Increment(ref source.Read);
    }

    internal void Error(SourceProgress source, string path, Exception exception)
    {
        Interlocked.Increment(ref source.Errors);
        _errors.Enqueue(new ScanError(source.SourceId, path, exception.Message));
    }

    public ScanProgressSnapshot Snapshot() => new(
        Phase,
        Volatile.Read(ref _directories),
        Volatile.Read(ref _rawFound),
        Volatile.Read(ref _toRead),
        Volatile.Read(ref _read),
        Volatile.Read(ref _fromCache),
        _errors.Count,
        Volatile.Read(ref _currentDirectory),
        _sources.Values.Select(s => s.Snapshot()).ToList());

    internal sealed class SourceProgress(Guid sourceId)
    {
        public readonly Guid SourceId = sourceId;
        public int State;
        public int Directories;
        public int RawFound;
        public int Read;
        public int FromCache;
        public int Errors;

        public void SetState(SourceScanState state) => Volatile.Write(ref State, (int)state);

        public SourceProgressSnapshot Snapshot() => new(
            SourceId,
            (SourceScanState)Volatile.Read(ref State),
            Volatile.Read(ref Directories),
            Volatile.Read(ref RawFound),
            Volatile.Read(ref Read),
            Volatile.Read(ref FromCache),
            Volatile.Read(ref Errors));
    }
}

public sealed record ScanError(Guid SourceId, string Path, string Message);

public sealed record ScanProgressSnapshot(
    ScanPhase Phase,
    int DirectoriesExplored,
    int RawFound,
    int ToRead,
    int Read,
    int FromCache,
    int ErrorCount,
    string? CurrentDirectory,
    IReadOnlyList<SourceProgressSnapshot> Sources);

public sealed record SourceProgressSnapshot(
    Guid SourceId,
    SourceScanState State,
    int Directories,
    int RawFound,
    int Read,
    int FromCache,
    int Errors);
