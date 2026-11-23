using Fannyfique.Core.Indexing;
using Fannyfique.Core.Model;

namespace Fannyfique.Core.Analysis;

/// <summary>Regroupe les copies en lots et groupes, calcule couverture, espace gagné et verdicts (CLAUDE.md 6).</summary>
public static class Analyzer
{
    public static AnalysisResult Analyze(
        IReadOnlyList<Source> sources,
        IReadOnlyList<IndexedFile> files,
        IReadOnlyList<FolderAnnexes> annexes,
        IReadOnlySet<Guid> offlineSourceIds,
        IReadOnlyList<SourceType> keepOrder,
        bool ignoreOfflineSources)
    {
        var result = new AnalysisResult();
        var included = sources
            .Select((source, order) => (source, order))
            .Where(s => s.source.Enabled && !(ignoreOfflineSources && offlineSourceIds.Contains(s.source.Id)))
            .ToDictionary(s => s.source.Id);

        // Lots et identités. Les fichiers des sources aux noms ignorés sont rapprochés ensuite (MatchRenamed).
        var lots = new Dictionary<(Guid, string), Lot>();
        var copies = new Dictionary<string, Copies>();
        var renamed = new List<(IndexedFile File, Lot Lot)>();
        foreach (var file in files)
        {
            if (!included.TryGetValue(file.SourceId, out var s))
            {
                continue;
            }

            var lotKey = (file.SourceId, file.RelativeDirectory.ToUpperInvariant());
            if (!lots.TryGetValue(lotKey, out var lot))
            {
                lot = new Lot(s.source, !offlineSourceIds.Contains(s.source.Id), file.RelativeDirectory) { Index = lots.Count };
                lots.Add(lotKey, lot);
            }

            if (s.source.IgnoreFileNames)
            {
                renamed.Add((file, lot));
                continue;
            }

            var identity = FileIdentity.Of(file);
            if (!copies.TryGetValue(identity.Key, out var c))
            {
                c = new Copies(identity);
                copies.Add(identity.Key, c);
            }

            Join(new LotFile(file, identity, lot), c);
        }

        MatchRenamed(renamed, copies, source => included[source.Id].order);

        foreach (var a in annexes)
        {
            if (lots.TryGetValue((a.SourceId, a.RelativeDirectory.ToUpperInvariant()), out var lot))
            {
                lot.Annexes = a;
            }
        }

        // Copies divergentes et copies récupérables (CLAUDE.md 6.4).
        var ranks = keepOrder.Select((type, i) => (type, i)).DistinctBy(t => t.type).ToDictionary(t => t.type, t => t.i);
        int Rank(LotFile f) => ranks.GetValueOrDefault(f.Lot.Source.Type, keepOrder.Count);
        foreach (var c in copies.Values.Where(c => c.IsDuplicate))
        {
            c.Divergent = c.Files.Select(f => f.File.Size).Distinct().Skip(1).Any();
            if (c.Divergent)
            {
                continue;
            }

            var keeper = c.Files
                .OrderBy(Rank)
                .ThenBy(f => f.Lot.Source.IgnoreFileNames) // à rang égal, la copie qui a gardé son nom d'origine
                .ThenBy(f => included[f.Lot.Source.Id].order)
                .ThenBy(f => f.File.RelativePath, StringComparer.OrdinalIgnoreCase)
                .First();
            foreach (var f in c.Files.Where(f => f != keeper))
            {
                f.Recoverable = true;
            }
        }

        // Statistiques des lots.
        foreach (var lot in lots.Values)
        {
            lot.TotalBytes = lot.Files.Sum(f => f.File.Size);
            lot.FirstDate = lot.Files.Min(f => f.File.DateTaken);
            lot.LastDate = lot.Files.Max(f => f.File.DateTaken);
            lot.Cameras = Cameras(lot.Files);
            lot.CommonCount = lot.Files.Count(f => f.Copies.Files.Any(o => o.Lot != lot));
            lot.DivergentCount = lot.Files.Count(f => f.Copies.Divergent);
            lot.ProbableCount = lot.Files.Count(f => f.Identity.Level == IdentityLevel.Probable);
            lot.WeakCount = lot.Files.Count(f => f.Identity.Level == IdentityLevel.Weak);
            lot.AmbiguousCount = lot.Files.Count(f => f.Ambiguous);
            lot.RecoverableBytes = lot.Files.Where(f => f.Recoverable).Sum(f => f.File.Size);
        }

        BuildGroups(copies.Values, result);
        BuildSummary(lots.Values, copies.Values, result);
        BuildSdVerdicts(sources, included.Keys.ToHashSet(), offlineSourceIds, lots.Values, result);
        return result;
    }

    private static void Join(LotFile file, Copies copies)
    {
        copies.Files.Add(file);
        file.Copies = copies;
        file.Lot.Files.Add(file);
    }

    /// <summary>
    /// Rapproche les RAW des sources aux noms ignorés (fichiers renommés par une récupération de carte) des autres
    /// copies, par date de prise de vue et boîtier seulement. Sans le nom, plusieurs photos d'une rafale peuvent
    /// partager la même clé : un rapprochement n'est retenu que s'il est sans équivoque, car conclure à tort
    /// qu'une photo est copiée ailleurs pourrait la faire supprimer. Dans le doute, le fichier reste unique.
    /// </summary>
    private static void MatchRenamed(List<(IndexedFile File, Lot Lot)> renamed, Dictionary<string, Copies> copies, Func<Source, int> sourceOrder)
    {
        // Copies existantes, par clé sans le nom. Toutes les copies d'une identité ont la même date et le même boîtier.
        var byKey = new Dictionary<string, List<Copies>>();
        foreach (var c in copies.Values)
        {
            if (FileIdentity.WithoutName(c.Files[0].File) is { } k)
            {
                Candidates(k.Key).Add(c);
            }
        }

        List<Copies> Candidates(string key)
        {
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey.Add(key, list);
            }

            return list;
        }

        Copies New(FileIdentity identity)
        {
            var c = new Copies(identity);
            copies.Add(identity.Key, c);
            return c;
        }

        // Une source après l'autre : les fichiers d'une même source ne sont jamais rapprochés entre eux,
        // mais ceux d'une source renommée peuvent l'être de ceux d'une autre source renommée.
        foreach (var source in renamed.GroupBy(r => r.Lot.Source).OrderBy(g => sourceOrder(g.Key)))
        {
            foreach (var bucket in source.GroupBy(r => FileIdentity.WithoutName(r.File)))
            {
                if (bucket.Key is not { } key)
                {
                    // Sans date de prise de vue, il ne reste que la taille : rien ne permet de rapprocher ce fichier.
                    foreach (var (file, lot) in bucket)
                    {
                        var weak = new FileIdentity($"U:{source.Key.Id}|{file.RelativePath.ToUpperInvariant()}", IdentityLevel.Weak);
                        Join(new LotFile(file, weak, lot), New(weak));
                    }

                    continue;
                }

                var items = bucket.ToList();
                var pool = Candidates(key.Key);
                var created = new List<Copies>();
                foreach (var ((file, lot), match) in items.Zip(Pair(items.Select(i => i.File).ToList(), pool, key.Level)))
                {
                    switch (match)
                    {
                        case { Copies: { } c }:
                            Join(new LotFile(file, new FileIdentity(c.Identity.Key, key.Level), lot), c);
                            break;
                        case { Ambiguous: true }:
                            var lone = new FileIdentity($"A:{source.Key.Id}|{file.RelativePath.ToUpperInvariant()}", key.Level);
                            Join(new LotFile(file, lone, lot) { Ambiguous = true }, New(lone));
                            break;
                        default:
                            // Aucune copie connue : nouvelle identité, que d'autres sources renommées pourront rejoindre.
                            var own = new FileIdentity($"R:{key.Key}|{source.Key.Id}|{file.RelativePath.ToUpperInvariant()}", key.Level);
                            var copy = New(own);
                            Join(new LotFile(file, own, lot), copy);
                            created.Add(copy);
                            break;
                    }
                }

                pool.AddRange(created);
            }
        }
    }

    private readonly record struct RenamedMatch(Copies? Copies, bool Ambiguous);

    /// <summary>
    /// Associe les fichiers renommés d'une source, de même clé sans le nom, aux copies candidates.
    /// Un fichier seul face à une seule copie, avec numéro de série, lui est associé (une taille différente le
    /// signalera divergent). Sinon la taille exacte doit départager : un fichier n'est associé qu'à l'unique copie
    /// de même taille, et seulement si aucun autre fichier de la source ne peut prétendre à cette copie.
    /// </summary>
    private static List<RenamedMatch> Pair(List<IndexedFile> items, List<Copies> pool, IdentityLevel level)
    {
        if (pool.Count == 0)
        {
            return items.Select(_ => new RenamedMatch(null, false)).ToList();
        }

        if (level == IdentityLevel.Certain && items.Count == 1 && pool.Count == 1)
        {
            return [new RenamedMatch(pool[0], false)];
        }

        var compatible = items
            .Select(f => pool.Where(c => c.Files.Any(o => o.File.Size == f.Size)).ToList())
            .ToList();
        var claims = compatible.SelectMany(l => l).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        return compatible
            .Select(l => l switch
            {
                [] => new RenamedMatch(null, false),
                [var c] when claims[c] == 1 => new RenamedMatch(c, false),
                _ => new RenamedMatch(null, true),
            })
            .ToList();
    }

    /// <summary>
    /// Lignes de résultats (CLAUDE.md 6.2). Chaque RAW en plusieurs copies a une « signature » : l'ensemble des lots
    /// qui le contiennent. Chaque signature maximale (non incluse dans une autre) forme une ligne, et les RAW dont la
    /// signature est incluse dans celle d'une ligne y sont rattachés. Tous les lots d'une ligne partagent donc les RAW
    /// de sa signature : pas de chaîne A–B–C–D où A et D n'auraient rien en commun. Un lot peut figurer dans plusieurs
    /// lignes, mais chaque RAW n'est rattaché qu'à une seule.
    /// </summary>
    private static void BuildGroups(IEnumerable<Copies> copies, AnalysisResult result)
    {
        var blocks = new Dictionary<string, Block>();
        foreach (var c in copies)
        {
            var members = c.Files.Select(f => f.Lot).Distinct().OrderBy(l => l.Index).ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var key = string.Join(',', members.Select(l => l.Index));
            if (!blocks.TryGetValue(key, out var block))
            {
                block = new Block(key, members);
                blocks.Add(key, block);
            }

            block.Identities.Add(c);
        }

        var blocksByLot = blocks.Values
            .SelectMany(b => b.Lots.Select(l => (l, b)))
            .ToLookup(p => p.l, p => p.b);
        List<Block> Supersets(Block block) => blocksByLot[block.Lots.MinBy(l => blocksByLot[l].Count())!]
            .Where(other => other.Lots.Count > block.Lots.Count && block.LotSet.IsSubsetOf(other.LotSet))
            .ToList();

        var supersets = blocks.Values.ToDictionary(b => b, Supersets);
        var groups = new Dictionary<Block, DuplicateGroup>();
        foreach (var block in blocks.Values.Where(b => supersets[b].Count == 0))
        {
            groups[block] = new DuplicateGroup();
        }

        // Rattachement des RAW à leur ligne : la plus grosse des lignes dont la signature contient la leur.
        foreach (var block in blocks.Values)
        {
            var target = supersets[block].Count == 0
                ? block
                : supersets[block]
                    .Where(groups.ContainsKey)
                    .OrderByDescending(b => b.Identities.Count)
                    .ThenBy(b => b.Lots.Count)
                    .ThenBy(b => b.Key, StringComparer.Ordinal)
                    .First();
            groups[target].Identities.AddRange(block.Identities);
        }

        foreach (var (block, group) in groups)
        {
            var members = block.Lots.ToDictionary(l => l, l => new GroupMember(l, group));
            foreach (var file in group.Identities.SelectMany(c => c.Files))
            {
                members[file.Lot].Files.Add(file);
            }

            foreach (var member in members.Values)
            {
                member.RecoverableBytes = member.Files.Where(f => f.Recoverable).Sum(f => f.File.Size);
                member.Lot.Groups.Add(group);
            }

            group.Members.AddRange(members.Values
                .OrderBy(m => m.Lot.Source.Type)
                .ThenBy(m => m.Lot.Source.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(m => m.Lot.RelativeDirectory, StringComparer.CurrentCultureIgnoreCase));
            var ordered = group.Identities
                .OrderBy(c => c.DateTaken ?? DateTime.MaxValue)
                .ThenBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            group.Identities.Clear();
            group.Identities.AddRange(ordered);
            var files = group.Identities.SelectMany(c => c.Files).ToList();
            group.FirstDate = files.Min(f => f.File.DateTaken);
            group.LastDate = files.Max(f => f.File.DateTaken);
            group.Cameras = Cameras(files);
            group.RecoverableBytes = group.Members.Sum(m => m.RecoverableBytes);
            group.DuplicateFileCount = files.Count;
            result.Groups.Add(group);
        }

        result.Groups.Sort((a, b) => b.RecoverableBytes.CompareTo(a.RecoverableBytes));
    }

    /// <summary>RAW partageant la même signature (même ensemble de lots).</summary>
    private sealed class Block(string key, List<Lot> lots)
    {
        public string Key { get; } = key;

        public List<Lot> Lots { get; } = lots;

        public HashSet<Lot> LotSet { get; } = [.. lots];

        public List<Copies> Identities { get; } = [];
    }

    private static void BuildSummary(IEnumerable<Lot> lots, IEnumerable<Copies> copies, AnalysisResult result)
    {
        var summary = result.Summary;
        foreach (var lot in lots)
        {
            summary.RawCount += lot.FileCount;
            summary.RawBytes += lot.TotalBytes;
            summary.RecoverableBytes += lot.RecoverableBytes;
            summary.RecoverableByType[lot.Source.Type] = summary.RecoverableByType.GetValueOrDefault(lot.Source.Type) + lot.RecoverableBytes;
        }

        summary.GroupCount = result.Groups.Count;
        summary.DuplicateFileCount = copies.Where(c => c.IsDuplicate).Sum(c => c.Files.Count);
    }

    private static void BuildSdVerdicts(
        IReadOnlyList<Source> sources,
        IReadOnlySet<Guid> included,
        IReadOnlySet<Guid> offlineSourceIds,
        IEnumerable<Lot> lots,
        AnalysisResult result)
    {
        var lotsBySource = lots.ToLookup(l => l.Source.Id);
        var sdCards = sources
            .Where(s => s.Type == SourceType.SdCard && included.Contains(s.Id))
            .OrderBy(s => offlineSourceIds.Contains(s.Id))
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var card in sdCards)
        {
            var verdict = new SdCardVerdict(card, !offlineSourceIds.Contains(card.Id));
            var counts = new Dictionary<Source, int>();
            foreach (var lot in lotsBySource[card.Id])
            {
                var lotHasUnique = false;
                foreach (var file in lot.Files)
                {
                    verdict.FileCount++;
                    verdict.TotalBytes += file.File.Size;

                    // Une copie sûre : même taille, sur une source qui n'est pas une carte SD.
                    var safeCopies = file.Copies.Files
                        .Where(o => o.Lot.Source.Type != SourceType.SdCard && o.File.Size == file.File.Size)
                        .ToList();
                    if (safeCopies.Count == 0)
                    {
                        verdict.OnlyHere.Add(file);
                        lotHasUnique = true;
                        continue;
                    }

                    verdict.FreeableBytes += file.File.Size;
                    if (safeCopies.All(o => !o.Lot.SourceOnline))
                    {
                        verdict.OfflineOnlyCount++;
                    }

                    foreach (var source in safeCopies.Select(o => o.Lot.Source).Distinct())
                    {
                        counts[source] = counts.GetValueOrDefault(source) + 1;
                    }
                }

                if (lotHasUnique)
                {
                    verdict.LotsWithUniqueFiles.Add(lot);
                }
            }

            verdict.CopiesBySource.AddRange(counts
                .OrderByDescending(p => p.Value)
                .Select(p => new SourceCopyCount(p.Key, !offlineSourceIds.Contains(p.Key.Id), p.Value)));
            result.SdCards.Add(verdict);
        }
    }

    private static IReadOnlyList<string> Cameras(IEnumerable<LotFile> files) => files
        .Select(f => f.File.Camera)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
