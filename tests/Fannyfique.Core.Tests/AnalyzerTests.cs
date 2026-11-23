using Fannyfique.Core.Analysis;
using Fannyfique.Core.Indexing;
using Fannyfique.Core.Model;

namespace Fannyfique.Core.Tests;

public class AnalyzerTests
{
    private static readonly DateTime Shoot = new(2024, 5, 12, 14, 0, 0);
    private static readonly SourceType[] KeepOrder = [SourceType.Nas, SourceType.ExternalDisk, SourceType.Pc, SourceType.SdCard];

    private readonly Source _sd = new() { Name = "SD", Type = SourceType.SdCard, Path = "/sd" };
    private readonly Source _pc = new() { Name = "PC", Type = SourceType.Pc, Path = "/pc" };
    private readonly Source _nas = new() { Name = "NAS", Type = SourceType.Nas, Path = "/nas" };
    private readonly List<IndexedFile> _files = [];
    private readonly HashSet<Guid> _offline = [];

    private void Add(Source source, string directory, int number, long size = 25_000_000, string? serial = "SN1", bool date = true, DateTime? when = null)
    {
        _files.Add(new IndexedFile
        {
            SourceId = source.Id,
            RelativePath = Path.Combine(directory, $"IMG_{number:0000}.CR2"),
            Size = size,
            DateTaken = date ? (when ?? Shoot.AddSeconds(number)) : null,
            Serial = serial,
            Make = "Canon",
            Model = "Canon EOS R6",
        });
    }

    private void AddRange(Source source, string directory, IEnumerable<int> numbers)
    {
        foreach (var n in numbers)
        {
            Add(source, directory, n);
        }
    }

    private AnalysisResult Analyze(bool ignoreOffline = false, List<FolderAnnexes>? annexes = null) =>
        Analyzer.Analyze([_sd, _pc, _nas], _files, annexes ?? [], _offline, KeepOrder, ignoreOffline);

    [Fact]
    public void Deux_copies_completes_forment_un_groupe_de_deux_lots()
    {
        AddRange(_sd, "DCIM/100CANON", Enumerable.Range(1, 10));
        AddRange(_pc, "Photos/Mariage", Enumerable.Range(1, 10));

        var result = Analyze();

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Members.Count);
        Assert.All(group.Lots, l => Assert.True(l.IsFullyCopied));
        Assert.Equal(10, group.Identities.Count);
        Assert.Equal(20, group.DuplicateFileCount);
        Assert.Equal(Shoot.AddSeconds(1), group.FirstDate);
        Assert.Equal(Shoot.AddSeconds(10), group.LastDate);
        Assert.Equal(["Canon EOS R6"], group.Cameras);
    }

    [Fact]
    public void Les_lots_sans_doublon_n_apparaissent_pas()
    {
        AddRange(_pc, "A", Enumerable.Range(1, 5));
        AddRange(_pc, "B", Enumerable.Range(6, 5));

        var result = Analyze();

        Assert.Empty(result.Groups);
        Assert.Equal(10, result.Summary.RawCount);
        Assert.Equal(0, result.Summary.RecoverableBytes);
    }

    [Fact]
    public void Couverture_d_un_lot_incomplet()
    {
        AddRange(_sd, "DCIM", Enumerable.Range(1, 10));
        AddRange(_pc, "Selection", [1, 2, 3, 4]);

        var group = Assert.Single(Analyze().Groups);
        var sd = group.Lots.Single(l => l.Source == _sd);
        var pc = group.Lots.Single(l => l.Source == _pc);

        Assert.Equal((4, 6, 0.4), (sd.CommonCount, sd.UniqueCount, sd.Coverage));
        Assert.True(pc.IsFullyCopied);
        Assert.True(group.HasIncompleteLot);
    }

    [Fact]
    public void Les_lots_lies_en_chaine_ne_forment_pas_une_seule_ligne()
    {
        AddRange(_sd, "DCIM", [1, 2]);
        AddRange(_pc, "A", [2, 3]);
        AddRange(_nas, "B", [3, 4]);

        var groups = Analyze().Groups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(2, g.Members.Count));
        var pc = groups.SelectMany(g => g.Members).Where(m => m.Lot.Source == _pc).ToList();
        Assert.Equal(2, pc.Count);
        Assert.All(pc, m => Assert.Equal((1, 0.5, 1), (m.Count, m.Share, m.ElsewhereCount)));
        Assert.Equal(2, pc[0].Lot.Groups.Count);
    }

    [Fact]
    public void Chaine_A_B_C_D_E_une_ligne_par_recoupement()
    {
        // A = B (même shooting), C contient 3 photos de A/B et des photos de D, D en partage d'autres avec E.
        AddRange(_pc, "A", Enumerable.Range(1, 50));
        AddRange(_nas, "B", Enumerable.Range(1, 50));
        AddRange(_nas, "C", [.. Enumerable.Range(1, 3), .. Enumerable.Range(100, 40), .. Enumerable.Range(300, 57)]);
        AddRange(_nas, "D", [.. Enumerable.Range(100, 40), .. Enumerable.Range(200, 20)]);
        AddRange(_sd, "E", [.. Enumerable.Range(200, 20), .. Enumerable.Range(400, 5)]);

        var rows = Analyze().Groups.Select(g => g.Members.Select(m => m.Lot.RelativeDirectory).Order().ToArray()).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Contains(["A", "B", "C"], rows);
        Assert.Contains(["C", "D"], rows);
        Assert.Contains(["D", "E"], rows);

        var c = Analyze().Groups.Single(g => g.Members.Count == 3).Members.Single(m => m.Lot.RelativeDirectory == "C");
        Assert.Equal((3, 40, 57), (c.Count, c.ElsewhereCount, c.Lot.UniqueCount));
    }

    [Fact]
    public void Une_selection_partielle_reste_sur_la_ligne_du_lot_complet()
    {
        AddRange(_sd, "DCIM", Enumerable.Range(1, 10));
        AddRange(_nas, "2024", Enumerable.Range(1, 10));
        AddRange(_pc, "Selection", [2, 5, 7]);

        var group = Assert.Single(Analyze().Groups);

        Assert.Equal(3, group.Members.Count);
        Assert.Equal(10, group.Identities.Count);
        Assert.All(group.Members, m => Assert.True(m.IsWhole));
        Assert.Equal(325_000_000, group.RecoverableBytes);
    }

    [Fact]
    public void Carte_SD_triee_dans_plusieurs_dossiers_une_ligne_par_dossier()
    {
        AddRange(_sd, "DCIM/100CANON", Enumerable.Range(1, 30));
        AddRange(_pc, "Mariage", Enumerable.Range(1, 10));
        AddRange(_pc, "Vacances", Enumerable.Range(11, 15));

        var result = Analyze();

        Assert.Equal(2, result.Groups.Count);
        var sd = result.Groups.Select(g => g.Members.Single(m => m.Lot.Source == _sd)).ToList();
        Assert.Equal([15, 10], sd.Select(m => m.Count));
        Assert.All(sd, m => Assert.Equal(5, m.Lot.UniqueCount));
        Assert.True(Assert.Single(result.Groups, g => g.Members.Any(m => m.Lot.RelativeDirectory == "Mariage")).HasIncompleteLot);
    }

    [Fact]
    public void Chaque_RAW_n_est_compte_que_dans_une_ligne()
    {
        AddRange(_sd, "DCIM", Enumerable.Range(1, 20));
        AddRange(_pc, "Tout", Enumerable.Range(1, 20));
        AddRange(_nas, "Best1", [1, 2, 3]);
        AddRange(_nas, "Best2", [10, 11]);

        var result = Analyze();

        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(20, result.Groups.Sum(g => g.Identities.Count));
        Assert.Equal(result.Summary.RecoverableBytes, result.Groups.Sum(g => g.RecoverableBytes));
        Assert.Equal(20, result.Groups.SelectMany(g => g.Members).Where(m => m.Lot.Source == _sd).Sum(m => m.Count));
    }

    [Fact]
    public void Deux_boitiers_de_meme_modele_ne_sont_pas_confondus()
    {
        Add(_sd, "DCIM", 1, serial: "BOITIER-A");
        Add(_pc, "Photos", 1, serial: "BOITIER-B");

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Meme_nom_mais_autre_date_n_est_pas_un_doublon()
    {
        Add(_sd, "DCIM", 1, when: Shoot);
        Add(_pc, "Photos", 1, when: Shoot.AddYears(1));

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Niveaux_d_identite()
    {
        Add(_sd, "DCIM", 1);
        Add(_sd, "DCIM", 2, serial: null);
        Add(_sd, "DCIM", 3, date: false);
        Add(_pc, "Photos", 1);
        Add(_pc, "Photos", 2, serial: null);
        Add(_pc, "Photos", 3, date: false);

        var lot = Assert.Single(Analyze().Groups).Lots.First(l => l.Source == _sd);

        Assert.Equal(
            [IdentityLevel.Certain, IdentityLevel.Probable, IdentityLevel.Weak],
            lot.Files.Select(f => f.Identity.Level).Order());
        Assert.Equal((1, 1), (lot.ProbableCount, lot.WeakCount));
        Assert.True(lot.IsFullyCopied);
    }

    [Fact]
    public void Identite_faible_depend_de_la_taille()
    {
        Add(_sd, "DCIM", 1, date: false, size: 100);
        Add(_pc, "Photos", 1, date: false, size: 200);

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Espace_gagne_garde_la_copie_preferee()
    {
        AddRange(_sd, "DCIM", Enumerable.Range(1, 4));
        AddRange(_pc, "Photos", Enumerable.Range(1, 4));
        AddRange(_nas, "Archives", Enumerable.Range(1, 4));

        var result = Analyze();
        var group = Assert.Single(result.Groups);

        Assert.Equal(0, group.Lots.Single(l => l.Source == _nas).RecoverableBytes);
        Assert.Equal(100_000_000, group.Lots.Single(l => l.Source == _pc).RecoverableBytes);
        Assert.Equal(100_000_000, group.Lots.Single(l => l.Source == _sd).RecoverableBytes);
        Assert.Equal(200_000_000, group.RecoverableBytes);
        Assert.Equal(200_000_000, result.Summary.RecoverableBytes);
        Assert.Equal(100_000_000, result.Summary.RecoverableByType[SourceType.SdCard]);
        Assert.Equal(0, result.Summary.RecoverableByType[SourceType.Nas]);
    }

    [Fact]
    public void Doublons_dans_une_meme_source()
    {
        AddRange(_pc, "Import", [1, 2]);
        AddRange(_pc, "Tri", [1, 2]);

        var group = Assert.Single(Analyze().Groups);
        Assert.Equal(50_000_000, group.RecoverableBytes);
    }

    [Fact]
    public void Les_copies_divergentes_sont_signalees_et_non_comptees()
    {
        Add(_sd, "DCIM", 1, size: 100);
        Add(_pc, "Photos", 1, size: 120);

        var group = Assert.Single(Analyze().Groups);

        Assert.All(group.Lots, l => Assert.Equal(1, l.DivergentCount));
        Assert.True(group.Identities.Single().Divergent);
        Assert.Equal(0, group.RecoverableBytes);
    }

    [Fact]
    public void Annexes_rattachees_au_lot()
    {
        Add(_sd, "DCIM", 1);
        Add(_pc, "Photos", 1);
        var annexes = new FolderAnnexes { SourceId = _pc.Id, RelativeDirectory = "Photos", Jpeg = 3 };

        var group = Assert.Single(Analyze(annexes: [annexes]).Groups);

        Assert.Same(annexes, group.Lots.Single(l => l.Source == _pc).Annexes);
    }

    [Fact]
    public void Carte_SD_entierement_copiee_peut_etre_videe()
    {
        AddRange(_sd, "DCIM/100CANON", Enumerable.Range(1, 6));
        AddRange(_sd, "DCIM/101CANON", Enumerable.Range(7, 4));
        AddRange(_nas, "2024", Enumerable.Range(1, 10));
        AddRange(_pc, "Selection", [1, 2]);

        var verdict = Assert.Single(Analyze().SdCards);

        Assert.True(verdict.CanBeEmptied);
        Assert.Equal(10, verdict.FileCount);
        Assert.Equal(250_000_000, verdict.FreeableBytes);
        Assert.Equal([(_nas, 10), (_pc, 2)], verdict.CopiesBySource.Select(c => (c.Source, c.Count)));
    }

    [Fact]
    public void Carte_SD_avec_fichiers_uniques()
    {
        AddRange(_sd, "DCIM/100CANON", Enumerable.Range(1, 10));
        AddRange(_sd, "DCIM/101CANON", [11, 12]);
        AddRange(_pc, "Photos", Enumerable.Range(1, 7));

        var verdict = Assert.Single(Analyze().SdCards);

        Assert.False(verdict.CanBeEmptied);
        Assert.Equal(5, verdict.OnlyHere.Count);
        Assert.Equal(2, verdict.LotsWithUniqueFiles.Count);
        Assert.Equal(175_000_000, verdict.FreeableBytes);
    }

    [Fact]
    public void Une_copie_sur_une_autre_carte_SD_ne_suffit_pas()
    {
        var sd2 = new Source { Name = "SD2", Type = SourceType.SdCard, Path = "/sd2" };
        Add(_sd, "DCIM", 1);
        Add(sd2, "DCIM", 1);

        var result = Analyzer.Analyze([_sd, sd2], _files, [], _offline, KeepOrder, false);

        Assert.All(result.SdCards, v => Assert.False(v.CanBeEmptied));
    }

    [Fact]
    public void Une_copie_divergente_n_est_pas_une_copie_sure()
    {
        Add(_sd, "DCIM", 1, size: 100);
        Add(_pc, "Photos", 1, size: 99);

        Assert.False(Assert.Single(Analyze().SdCards).CanBeEmptied);
    }

    [Fact]
    public void Copies_uniquement_hors_ligne()
    {
        AddRange(_sd, "DCIM", [1, 2]);
        AddRange(_nas, "2024", [1, 2]);
        _offline.Add(_nas.Id);

        var verdict = Assert.Single(Analyze().SdCards);
        Assert.True(verdict.CanBeEmptied);
        Assert.Equal(2, verdict.OfflineOnlyCount);
        Assert.False(verdict.CopiesBySource.Single().Online);
        Assert.False(Assert.Single(Analyze().Groups).Lots.Single(l => l.Source == _nas).SourceOnline);
    }

    [Fact]
    public void Ignorer_les_sources_hors_ligne()
    {
        AddRange(_sd, "DCIM", [1, 2]);
        AddRange(_nas, "2024", [1, 2]);
        _offline.Add(_nas.Id);

        var result = Analyze(ignoreOffline: true);

        Assert.Empty(result.Groups);
        Assert.False(Assert.Single(result.SdCards).CanBeEmptied);
    }

    [Fact]
    public void Les_sources_desactivees_sont_ignorees()
    {
        AddRange(_sd, "DCIM", [1, 2]);
        AddRange(_pc, "Photos", [1, 2]);
        _pc.Enabled = false;

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Groupes_tries_par_espace_recuperable()
    {
        AddRange(_sd, "Petit", [1]);
        AddRange(_pc, "Petit", [1]);
        AddRange(_sd, "Grand", Enumerable.Range(10, 5));
        AddRange(_pc, "Grand", Enumerable.Range(10, 5));

        var groups = Analyze().Groups;

        Assert.Equal("Grand", groups[0].Members[0].Lot.RelativeDirectory);
    }
}
