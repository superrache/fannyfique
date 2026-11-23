using System.Security.Cryptography;
using Fannyfique.Core.Indexing;
using Fannyfique.Core.Metadata;
using Fannyfique.Core.Model;
using Fannyfique.Core.Scanning;
using Fannyfique.Core.Storage;
using Fannyfique.Core.Tests.Support;

namespace Fannyfique.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private static readonly DateTime Shoot = new(2024, 5, 12, 14, 0, 0);
    private readonly TempTree _tree = new();
    private readonly CountingReader _reader = new(new MetadataExtractorReader());
    private readonly AppSettings _settings = new();
    private readonly FileIndex _index = new();
    private readonly Source _sd;
    private readonly Source _pc;

    public ScannerTests()
    {
        _sd = new Source { Name = "SD", Type = SourceType.SdCard, Path = _tree["sd"] };
        _pc = new Source { Name = "PC", Type = SourceType.Pc, Path = _tree["pc"] };
        TestRaw.Write(_tree["sd/DCIM/100CANON/IMG_0001.CR2"], Shoot);
        TestRaw.Write(_tree["sd/DCIM/100CANON/IMG_0002.CR2"], Shoot.AddSeconds(1));
        TestRaw.Write(_tree["pc/2024/Mariage/IMG_0001.CR2"], Shoot);
        File.WriteAllText(_tree["pc/2024/Mariage/IMG_0001.JPG"], "jpeg");
        File.WriteAllText(_tree["pc/2024/Mariage/IMG_0001.xmp"], "xmp");
        File.WriteAllText(_tree["pc/2024/notes.txt"], "texte");
    }

    public void Dispose() => _tree.Dispose();

    private Task<ScanReport> Scan(ScanProgress? progress = null, CancellationToken ct = default) =>
        new Scanner(_reader).ScanAsync([_sd, _pc], _settings, _index, progress ?? new ScanProgress(), cancellationToken: ct);

    [Fact]
    public async Task Indexe_les_RAW_de_toutes_les_sources()
    {
        var progress = new ScanProgress();
        var report = await Scan(progress);

        Assert.Equal(3, _index.Count);
        Assert.Empty(report.OfflineSourceIds);
        var file = _index.Get(_pc.Id, Path.Combine("2024", "Mariage", "IMG_0001.CR2"));
        Assert.NotNull(file);
        Assert.Equal(Shoot, file.DateTaken);
        Assert.Equal(MetadataStatus.Ok, file.Status);
        Assert.NotNull(_pc.LastScanUtc);

        var snapshot = progress.Snapshot();
        Assert.Equal(ScanPhase.Done, snapshot.Phase);
        Assert.Equal(3, snapshot.RawFound);
        Assert.Equal(3, snapshot.Read);
    }

    [Fact]
    public async Task Compte_les_annexes_des_dossiers_de_RAW()
    {
        await Scan();

        var annexes = Assert.Single(_index.Annexes);
        Assert.Equal(_pc.Id, annexes.SourceId);
        Assert.Equal((1, 1), (annexes.Jpeg, annexes.Xmp));
    }

    [Fact]
    public async Task Second_scan_ne_relit_que_les_fichiers_nouveaux_ou_modifies()
    {
        await Scan();
        _reader.Count = 0;

        TestRaw.Write(_tree["pc/2024/Mariage/IMG_0002.CR2"], Shoot.AddSeconds(1));
        var modified = _tree["sd/DCIM/100CANON/IMG_0001.CR2"];
        TestRaw.Write(modified, Shoot, padding: 2000);
        File.SetLastWriteTimeUtc(modified, DateTime.UtcNow.AddMinutes(1));

        var progress = new ScanProgress();
        await Scan(progress);

        Assert.Equal(2, _reader.Count);
        Assert.Equal(2, progress.Snapshot().FromCache);
        Assert.Equal(4, _index.Count);
    }

    [Fact]
    public async Task Les_fichiers_disparus_sont_retires_de_l_index()
    {
        await Scan();
        File.Delete(_tree["sd/DCIM/100CANON/IMG_0002.CR2"]);

        await Scan();

        Assert.Equal(2, _index.Count);
        Assert.Null(_index.Get(_sd.Id, Path.Combine("DCIM", "100CANON", "IMG_0002.CR2")));
    }

    [Fact]
    public async Task Une_source_hors_ligne_garde_ses_donnees()
    {
        await Scan();
        Directory.Move(_sd.Path, _sd.Path + "-retiree");

        var progress = new ScanProgress();
        var report = await Scan(progress);

        Assert.Contains(_sd.Id, report.OfflineSourceIds);
        Assert.Equal(3, _index.Count);
        Assert.Contains(progress.Snapshot().Sources, s => s.SourceId == _sd.Id && s.State == SourceScanState.Offline);
    }

    [Fact]
    public async Task Les_sources_retirees_sont_purgees_de_l_index()
    {
        await Scan();

        await new Scanner(_reader).ScanAsync([_pc], _settings, _index, new ScanProgress());

        Assert.All(_index.Files, f => Assert.Equal(_pc.Id, f.SourceId));
    }

    [Fact]
    public async Task Une_source_desactivee_n_est_pas_scannee_mais_reste_dans_l_index()
    {
        await Scan();
        _reader.Count = 0;
        _sd.Enabled = false;
        TestRaw.Write(_tree["sd/DCIM/100CANON/IMG_0003.CR2"], Shoot);

        await Scan();

        Assert.Equal(0, _reader.Count);
        Assert.Equal(3, _index.Count);
    }

    [Fact]
    public async Task Ignore_les_dossiers_exclus()
    {
        TestRaw.Write(_tree["pc/$RECYCLE.BIN/IMG_9.CR2"], Shoot);
        TestRaw.Write(_tree["pc/System Volume Information/IMG_9.CR2"], Shoot);
        TestRaw.Write(_tree["pc/.thumbnails/IMG_9.CR2"], Shoot);

        await Scan();

        Assert.Equal(3, _index.Count);
    }

    [Fact]
    public async Task Extensions_insensibles_a_la_casse()
    {
        TestRaw.Write(_tree["pc/minuscules/img_1.cr2"], Shoot);

        await Scan();

        Assert.Equal(4, _index.Count);
    }

    [Fact]
    public async Task Un_format_illisible_donne_une_identite_faible_sans_erreur()
    {
        TestRaw.WriteGarbage(_tree["pc/abime/IMG_7.CR2"]);

        var progress = new ScanProgress();
        await Scan(progress);

        var file = _index.Get(_pc.Id, Path.Combine("abime", "IMG_7.CR2"));
        Assert.Equal(MetadataStatus.Unreadable, file!.Status);
        Assert.Equal(0, progress.Snapshot().ErrorCount);
    }

    [Fact]
    public async Task Une_erreur_de_lecture_est_comptee_et_le_fichier_sera_relu()
    {
        var failing = new FailingReader(_tree["pc/2024/Mariage/IMG_0001.CR2"]);
        var progress = new ScanProgress();
        await new Scanner(failing).ScanAsync([_pc], _settings, _index, progress);

        Assert.Equal(1, progress.Snapshot().ErrorCount);
        Assert.Equal(MetadataStatus.Error, _index.Files.Single().Status);

        await Scan();
        Assert.Equal(MetadataStatus.Ok, _index.Get(_pc.Id, Path.Combine("2024", "Mariage", "IMG_0001.CR2"))!.Status);
    }

    [Fact]
    public async Task Annuler_garde_les_lectures_faites_sans_rien_retirer()
    {
        await Scan();
        File.Delete(_tree["sd/DCIM/100CANON/IMG_0002.CR2"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = new ScanProgress();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scan(progress, cts.Token));

        Assert.Equal(ScanPhase.Cancelled, progress.Phase);
        Assert.Equal(3, _index.Count);
    }

    [Fact]
    public async Task Le_scan_ne_modifie_aucun_fichier_des_sources()
    {
        var before = Fingerprint(_tree.Root);

        await Scan();
        await Scan();

        Assert.Equal(before, Fingerprint(_tree.Root));
    }

    private static string Fingerprint(string root) => string.Join('\n', Directory
        .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(p => File.Exists(p)
            ? $"{p}|{File.GetLastWriteTimeUtc(p):O}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}"
            : $"{p}/"));

    private sealed class CountingReader(IRawMetadataReader inner) : IRawMetadataReader
    {
        public int Count;

        public RawMetadata Read(string path)
        {
            Interlocked.Increment(ref Count);
            return inner.Read(path);
        }
    }

    private sealed class FailingReader(string failingPath) : IRawMetadataReader
    {
        public RawMetadata Read(string path) => path == failingPath
            ? throw new IOException("Réseau indisponible")
            : new MetadataExtractorReader().Read(path);
    }
}
