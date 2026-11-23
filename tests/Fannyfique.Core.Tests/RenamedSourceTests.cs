using Fannyfique.Core.Analysis;
using Fannyfique.Core.Indexing;
using Fannyfique.Core.Model;
using Fannyfique.Core.Storage;
using Fannyfique.Core.Tests.Support;

namespace Fannyfique.Core.Tests;

/// <summary>Sources aux noms ignorés : RAW renommés par une récupération de carte (dossiers recup_dir.N).</summary>
public class RenamedSourceTests
{
    private static readonly DateTime Shoot = new(2024, 5, 12, 14, 0, 0);
    private static readonly SourceType[] KeepOrder = [SourceType.Nas, SourceType.ExternalDisk, SourceType.Pc, SourceType.SdCard];

    private readonly Source _sd = new() { Name = "SD", Type = SourceType.SdCard, Path = "/sd" };
    private readonly Source _pc = new() { Name = "PC", Type = SourceType.Pc, Path = "/pc" };
    private readonly Source _recup = new() { Name = "Récup", Type = SourceType.Pc, Path = "/recup", IgnoreFileNames = true };
    private readonly Source _recup2 = new() { Name = "Récup 2", Type = SourceType.ExternalDisk, Path = "/recup2", IgnoreFileNames = true };
    private readonly List<IndexedFile> _files = [];

    private void Add(Source source, string path, DateTime? when, long size = 25_000_000, string? serial = "SN1") =>
        _files.Add(new IndexedFile
        {
            SourceId = source.Id,
            RelativePath = path,
            Size = size,
            DateTaken = when,
            Serial = serial,
            Make = "Canon",
            Model = "Canon EOS R6",
        });

    private AnalysisResult Analyze() =>
        Analyzer.Analyze([_sd, _pc, _recup, _recup2], _files, [], new HashSet<Guid>(), KeepOrder, false);

    [Fact]
    public void Les_fichiers_renommes_sont_rapproches_par_date_et_boitier()
    {
        for (var i = 1; i <= 5; i++)
        {
            Add(_sd, $"DCIM/IMG_{i:0000}.CR2", Shoot.AddSeconds(i));
            Add(_recup, $"recup_dir.1/f{1000 + i}.cr2", Shoot.AddSeconds(i));
        }

        var result = Analyze();

        var group = Assert.Single(result.Groups);
        Assert.Equal(5, group.Identities.Count);
        Assert.All(group.Members, m => Assert.True(m.IsWhole));
        Assert.Equal(5 * 25_000_000, group.RecoverableBytes);
        Assert.True(Assert.Single(result.SdCards).CanBeEmptied);

        var first = group.Identities[0];
        Assert.Equal("IMG_0001.CR2", first.FileName);
        Assert.Equal(["f1001.cr2"], first.OtherNames);
        Assert.All(first.Files, f => Assert.Equal(IdentityLevel.Certain, f.Identity.Level));
    }

    [Fact]
    public void Sans_l_option_les_fichiers_renommes_ne_sont_pas_rapproches()
    {
        _recup.IgnoreFileNames = false;
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot);
        Add(_recup, "recup_dir.1/f1001.cr2", Shoot);

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Le_nom_d_un_fichier_renomme_ne_suffit_pas()
    {
        // Même nom par hasard, mais une autre photo.
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot);
        Add(_recup, "recup_dir.1/IMG_0001.CR2", Shoot.AddSeconds(1));

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Une_rafale_est_departagee_par_la_taille()
    {
        // Rafale sans fractions de seconde : même date, même boîtier.
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100);
        Add(_sd, "DCIM/IMG_0002.CR2", Shoot, size: 200);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 200);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot, size: 100);

        var group = Assert.Single(Analyze().Groups);

        var img1 = group.Identities.Single(c => c.FileName == "IMG_0001.CR2");
        var img2 = group.Identities.Single(c => c.FileName == "IMG_0002.CR2");
        Assert.Equal(["f2.cr2"], img1.OtherNames);
        Assert.Equal(["f1.cr2"], img2.OtherNames);
        Assert.False(img1.Divergent || img2.Divergent);
    }

    [Fact]
    public void Une_rafale_de_meme_taille_reste_ambigue()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100);
        Add(_sd, "DCIM/IMG_0002.CR2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot, size: 100);

        var result = Analyze();

        Assert.Empty(result.Groups);
        Assert.False(Assert.Single(result.SdCards).CanBeEmptied);
        Assert.Equal(0, result.Summary.RecoverableBytes);
    }

    [Fact]
    public void Deux_fichiers_renommes_ne_peuvent_pas_pretendre_a_la_meme_copie()
    {
        // La carte n'a gardé qu'une photo de la rafale ; la récupération en contient deux de même taille.
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot, size: 100);

        var result = Analyze();

        Assert.Empty(result.Groups);
        Assert.False(Assert.Single(result.SdCards).CanBeEmptied);
    }

    [Fact]
    public void Un_fichier_de_rafale_sans_copie_de_meme_taille_reste_unique()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot, size: 300);

        var group = Assert.Single(Analyze().Groups);

        var copy = Assert.Single(group.Identities);
        Assert.Equal(["f1.cr2"], copy.OtherNames);
        var recup = group.Members.Single(m => m.Lot.Source == _recup);
        Assert.Equal((1, 0), (recup.Lot.UniqueCount, recup.Lot.AmbiguousCount));
    }

    [Fact]
    public void Les_fichiers_ambigus_sont_comptes_sur_le_lot()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100);
        Add(_sd, "DCIM/IMG_0002.CR2", Shoot, size: 100);
        Add(_sd, "DCIM/IMG_0003.CR2", Shoot.AddSeconds(5), size: 100);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.1/f3.cr2", Shoot.AddSeconds(5), size: 100);

        var member = Assert.Single(Analyze().Groups).Members.Single(m => m.Lot.Source == _recup);

        Assert.Equal(2, member.Lot.AmbiguousCount);
        Assert.Equal(1, member.Count);
        Assert.Equal(2, member.Lot.UniqueCount);
    }

    [Fact]
    public void Une_copie_renommee_tronquee_est_divergente()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 25_000_000);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 12_000_000);

        var result = Analyze();

        Assert.True(Assert.Single(Assert.Single(result.Groups).Identities).Divergent);
        Assert.Equal(0, result.Summary.RecoverableBytes);
        Assert.False(Assert.Single(result.SdCards).CanBeEmptied);
    }

    [Fact]
    public void Sans_numero_de_serie_la_taille_doit_etre_identique()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", Shoot, size: 100, serial: null);
        Add(_sd, "DCIM/IMG_0002.CR2", Shoot.AddSeconds(1), size: 100, serial: null);
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100, serial: null);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot.AddSeconds(1), size: 999, serial: null);

        var copy = Assert.Single(Assert.Single(Analyze().Groups).Identities);

        Assert.Equal("IMG_0001.CR2", copy.FileName);
        Assert.All(copy.Files, f => Assert.Equal(IdentityLevel.Probable, f.Identity.Level));
    }

    [Fact]
    public void Sans_metadonnees_un_fichier_renomme_n_est_jamais_rapproche()
    {
        Add(_sd, "DCIM/IMG_0001.CR2", null, size: 100);
        Add(_recup, "recup_dir.1/IMG_0001.CR2", null, size: 100);
        Add(_recup2, "recup_dir.1/IMG_0001.CR2", null, size: 100);

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Deux_sources_renommees_se_rapprochent_entre_elles()
    {
        Add(_recup, "recup_dir.1/f1.cr2", Shoot);
        Add(_recup, "recup_dir.1/f2.cr2", Shoot.AddSeconds(1));
        Add(_recup2, "recup_dir.7/f88.cr2", Shoot);
        Add(_recup2, "recup_dir.7/f89.cr2", Shoot.AddSeconds(1));

        var group = Assert.Single(Analyze().Groups);

        Assert.Equal(2, group.Identities.Count);
        Assert.All(group.Members, m => Assert.True(m.IsWhole));
    }

    [Fact]
    public void Les_fichiers_d_une_meme_source_renommee_ne_sont_pas_rapproches_entre_eux()
    {
        Add(_recup, "recup_dir.1/f1.cr2", Shoot, size: 100);
        Add(_recup, "recup_dir.2/f2.cr2", Shoot, size: 100);

        Assert.Empty(Analyze().Groups);
    }

    [Fact]
    public void Les_dossiers_recup_dir_de_la_meme_seance_sont_sur_des_lignes_differentes()
    {
        // PhotoRec répartit une séance entre plusieurs dossiers.
        for (var i = 1; i <= 6; i++)
        {
            Add(_pc, $"Mariage/IMG_{i:0000}.CR2", Shoot.AddSeconds(i));
            Add(_recup, $"recup_dir.{(i <= 3 ? 1 : 2)}/f{i}.cr2", Shoot.AddSeconds(i));
        }

        var groups = Analyze().Groups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(3, g.Identities.Count));
        var mariage = groups[0].Members.Single(m => m.Lot.Source == _pc);
        Assert.Equal((3, 3, 0), (mariage.Count, mariage.ElsewhereCount, mariage.Lot.UniqueCount));
    }

    [Fact]
    public void A_rang_egal_la_copie_conservee_est_celle_qui_a_garde_son_nom()
    {
        // Le dossier récupéré passe avant dans la liste des sources, mais a perdu les noms.
        var recup = new Source { Name = "Récup", Type = SourceType.Pc, Path = "/recup", IgnoreFileNames = true };
        var pc = new Source { Name = "PC", Type = SourceType.Pc, Path = "/pc" };
        _files.Add(new IndexedFile { SourceId = pc.Id, RelativePath = "A/IMG_0001.CR2", Size = 100, DateTaken = Shoot, Serial = "SN1" });
        _files.Add(new IndexedFile { SourceId = recup.Id, RelativePath = "recup_dir.1/f1.cr2", Size = 100, DateTaken = Shoot, Serial = "SN1" });

        var result = Analyzer.Analyze([recup, pc], _files, [], new HashSet<Guid>(), KeepOrder, false);

        var copy = Assert.Single(Assert.Single(result.Groups).Identities);
        Assert.False(copy.Files.Single(f => f.Lot.Source == pc).Recoverable);
        Assert.True(copy.Files.Single(f => f.Lot.Source == recup).Recoverable);
    }

    [Fact]
    public void L_option_est_enregistree_dans_les_preferences()
    {
        using var tree = new TempTree();
        var paths = new AppPaths(tree.Root);
        var settings = new AppSettings { Sources = [_recup, _pc] };

        JsonFileStore.Save(paths.SettingsFile, settings, indented: true);
        var loaded = JsonFileStore.Load<AppSettings>(paths.SettingsFile)!;

        Assert.True(loaded.Sources[0].IgnoreFileNames);
        Assert.False(loaded.Sources[1].IgnoreFileNames);
    }
}
