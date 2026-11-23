namespace Fannyfique.Core.Indexing;

/// <summary>Index des RAW connus, utilisable depuis plusieurs threads pendant un scan.</summary>
public sealed class FileIndex
{
    private readonly object _lock = new();
    private readonly Dictionary<string, IndexedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<FolderAnnexes>> _annexes = [];
    private readonly List<ScanSummaryEntry> _history = [];

    public int Count
    {
        get { lock (_lock) { return _files.Count; } }
    }

    public IndexedFile? Get(Guid sourceId, string relativePath)
    {
        lock (_lock)
        {
            return _files.GetValueOrDefault(Key(sourceId, relativePath));
        }
    }

    public void Upsert(IndexedFile file)
    {
        lock (_lock)
        {
            _files[Key(file.SourceId, file.RelativePath)] = file;
        }
    }

    /// <summary>Retire les fichiers d'une source qui ne vérifient pas <paramref name="keep"/>.</summary>
    public int RemoveWhere(Guid sourceId, Func<IndexedFile, bool> remove)
    {
        lock (_lock)
        {
            var keys = _files.Where(p => p.Value.SourceId == sourceId && remove(p.Value)).Select(p => p.Key).ToList();
            keys.ForEach(k => _files.Remove(k));
            return keys.Count;
        }
    }

    /// <summary>Retire tout ce qui concerne les sources absentes de <paramref name="knownSourceIds"/>.</summary>
    public void RemoveUnknownSources(IReadOnlySet<Guid> knownSourceIds)
    {
        lock (_lock)
        {
            var keys = _files.Where(p => !knownSourceIds.Contains(p.Value.SourceId)).Select(p => p.Key).ToList();
            keys.ForEach(k => _files.Remove(k));
            foreach (var id in _annexes.Keys.Where(id => !knownSourceIds.Contains(id)).ToList())
            {
                _annexes.Remove(id);
            }
        }
    }

    public void SetAnnexes(Guid sourceId, IEnumerable<FolderAnnexes> annexes)
    {
        lock (_lock)
        {
            _annexes[sourceId] = annexes.Where(a => !a.IsEmpty).ToList();
        }
    }

    public void AddHistory(ScanSummaryEntry entry)
    {
        lock (_lock)
        {
            _history.Add(entry);
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<IndexedFile> Files
    {
        get { lock (_lock) { return _files.Values.ToList(); } }
    }

    public IReadOnlyList<FolderAnnexes> Annexes
    {
        get { lock (_lock) { return _annexes.Values.SelectMany(a => a).ToList(); } }
    }

    public IReadOnlyList<ScanSummaryEntry> History
    {
        get { lock (_lock) { return _history.ToList(); } }
    }

    public IndexData ToData()
    {
        lock (_lock)
        {
            return new IndexData
            {
                Files = _files.Values.ToList(),
                Annexes = _annexes.Values.SelectMany(a => a).ToList(),
                History = _history.ToList(),
            };
        }
    }

    public static FileIndex FromData(IndexData? data)
    {
        var index = new FileIndex();
        if (data is null || data.SchemaVersion != IndexData.CurrentSchemaVersion)
        {
            return index; // schéma inconnu : reconstruction complète (CLAUDE.md 5.3)
        }

        foreach (var file in data.Files)
        {
            index._files[Key(file.SourceId, file.RelativePath)] = file;
        }

        foreach (var group in data.Annexes.GroupBy(a => a.SourceId))
        {
            index._annexes[group.Key] = group.ToList();
        }

        index._history.AddRange(data.History);
        return index;
    }

    private static string Key(Guid sourceId, string relativePath) => $"{sourceId:N}|{relativePath}";
}
